using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TarkovDBEditor.Services
{
    /// <summary>
    /// Wiki 페이지 캐싱 및 아이콘 다운로드 서비스
    /// - Wiki 페이지 정보 캐싱 (서버 과부하 방지)
    /// - 아이콘 URL 가져오기 (Infobox icon 파라미터 파싱 + imageinfo API)
    /// - 아이콘 이미지 배치 다운로드 (WikiID로 저장)
    /// </summary>
    public class WikiCacheService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string MediaWikiApiUrl = "https://escapefromtarkov.fandom.com/api.php";
        private const string SpecialExportUrl = "https://escapefromtarkov.fandom.com/wiki/Special:Export";

        private readonly string _cacheDir;
        private readonly string _iconDir;
        private readonly string _pageCachePath;
        private readonly string _logPath;

        /// <summary>
        /// 캐시된 페이지 정보 (페이지 이름 -> 페이지 데이터)
        /// </summary>
        private Dictionary<string, CachedPageInfo> _pageCache = new();

        public WikiCacheService(string? basePath = null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TarkovDBEditor/1.0");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);

            basePath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wiki_data");
            _cacheDir = Path.Combine(basePath, "cache");
            _iconDir = Path.Combine(basePath, "icons");
            _pageCachePath = Path.Combine(_cacheDir, "page_cache.json");
            _logPath = Path.Combine(_cacheDir, "cache_update.log");

            Directory.CreateDirectory(_cacheDir);
            Directory.CreateDirectory(_iconDir);
        }

        /// <summary>
        /// 로그 파일에 메시지 기록
        /// </summary>
        private void WriteLog(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.AppendAllText(_logPath, $"[{timestamp}] {message}\n");
            }
            catch { /* 로그 실패는 무시 */ }
        }

        /// <summary>
        /// 로그 파일 초기화 (새 세션 시작)
        /// </summary>
        public void ClearLog()
        {
            try
            {
                if (File.Exists(_logPath))
                    File.Delete(_logPath);
            }
            catch { }
        }

        #region 캐시 관리

        /// <summary>
        /// 캐시 파일에서 페이지 캐시 로드
        /// </summary>
        public async Task LoadCacheAsync(CancellationToken cancellationToken = default)
        {
            if (File.Exists(_pageCachePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_pageCachePath, cancellationToken);
                    var cacheData = JsonSerializer.Deserialize<PageCacheData>(json);
                    if (cacheData?.Pages != null)
                    {
                        _pageCache = cacheData.Pages;
                    }
                }
                catch
                {
                    _pageCache = new Dictionary<string, CachedPageInfo>();
                }
            }
        }

        /// <summary>
        /// 캐시를 파일로 저장
        /// </summary>
        public async Task SaveCacheAsync(CancellationToken cancellationToken = default)
        {
            var cacheData = new PageCacheData
            {
                LastUpdated = DateTime.UtcNow,
                Pages = _pageCache
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(cacheData, options);
            await File.WriteAllTextAsync(_pageCachePath, json, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// 캐시된 페이지 정보 가져오기 (없으면 null)
        /// </summary>
        public CachedPageInfo? GetCachedPage(string pageName)
        {
            return _pageCache.TryGetValue(pageName, out var info) ? info : null;
        }

        /// <summary>
        /// 캐시 통계 정보
        /// </summary>
        public (int totalCached, int withIcon, int withContent, int withRevision) GetCacheStats()
        {
            var total = _pageCache.Count;
            var withIcon = _pageCache.Values.Count(p => !string.IsNullOrEmpty(p.IconUrl));
            var withContent = _pageCache.Values.Count(p => !string.IsNullOrEmpty(p.PageContent));
            var withRevision = _pageCache.Values.Count(p => p.RevisionId.HasValue);
            return (total, withIcon, withContent, withRevision);
        }

        /// <summary>
        /// 캐시된 페이지 콘텐츠 가져오기 (없으면 null)
        /// </summary>
        public string? GetCachedPageContent(string pageName)
        {
            return _pageCache.TryGetValue(pageName, out var info) ? info.PageContent : null;
        }

        /// <summary>
        /// 캐시에 페이지 정보 저장
        /// </summary>
        public void SetCachedPage(string pageName, CachedPageInfo pageInfo)
        {
            _pageCache[pageName] = pageInfo;
        }

        #endregion

        #region 페이지 콘텐츠 캐싱 (리비전 기반)

        /// <summary>
        /// 여러 페이지의 최신 리비전 ID를 가져옵니다 (5개 병렬 처리)
        /// </summary>
        public async Task<Dictionary<string, long>> GetLatestRevisionIdsAsync(
            IEnumerable<string> pageNames,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, long>();
            var pageList = pageNames.ToList();

            if (pageList.Count == 0)
                return result;

            const int batchSize = 50;  // Fandom API titles 제한 50개
            var batches = pageList
                .Select((page, idx) => new { page, idx })
                .GroupBy(x => x.idx / batchSize)
                .Select(g => g.Select(x => x.page).ToList())
                .ToList();

            var totalBatches = batches.Count;
            var completed = 0;
            var lockObj = new object();
            var semaphore = new SemaphoreSlim(5);  // 5개 병렬

            progress?.Invoke($"Checking revisions: {totalBatches} batches (5 parallel)...");

            var tasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var batchResult = await FetchRevisionsBatchAsync(batch, cancellationToken);

                    lock (lockObj)
                    {
                        foreach (var kvp in batchResult)
                        {
                            result[kvp.Key] = kvp.Value;
                        }
                        completed++;
                        if (completed % 10 == 0 || completed == totalBatches)
                        {
                            progress?.Invoke($"Checking revisions [{completed}/{totalBatches}]...");
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
            progress?.Invoke($"Revision check complete: {result.Count} pages");

            return result;
        }

        /// <summary>
        /// 단일 배치의 리비전 ID 가져오기
        /// </summary>
        private async Task<Dictionary<string, long>> FetchRevisionsBatchAsync(
            List<string> pageNames,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, long>();

            // 공백 그대로 유지 (API가 알아서 처리)
            var titles = string.Join("|", pageNames);
            var url = $"{MediaWikiApiUrl}?action=query&titles={Uri.EscapeDataString(titles)}&prop=revisions&rvprop=ids&format=json";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("query", out var query) &&
                query.TryGetProperty("pages", out var pages))
            {
                foreach (var page in pages.EnumerateObject())
                {
                    var pageData = page.Value;

                    if (pageData.TryGetProperty("title", out var titleProp) &&
                        pageData.TryGetProperty("revisions", out var revisions) &&
                        revisions.GetArrayLength() > 0)
                    {
                        var title = titleProp.GetString();
                        var revId = revisions[0].GetProperty("revid").GetInt64();

                        if (!string.IsNullOrEmpty(title))
                        {
                            result[title] = revId;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 페이지 콘텐츠를 캐싱하고 변경된 페이지만 업데이트합니다
        /// 리비전 ID를 비교하여 변경된 페이지만 다시 가져옵니다
        /// </summary>
        public async Task<PageCacheUpdateResult> UpdatePageCacheAsync(
            IEnumerable<string> pageNames,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new PageCacheUpdateResult();
            var pageList = pageNames.ToList();
            result.TotalPages = pageList.Count;

            progress?.Invoke($"Checking {pageList.Count} pages for updates...");

            // 1. 최신 리비전 ID 가져오기
            var latestRevisions = await GetLatestRevisionIdsAsync(pageList, progress, cancellationToken);

            // 2. 캐시와 비교하여 업데이트가 필요한 페이지 찾기
            var pagesToUpdate = new List<string>();
            var pagesUpToDate = new List<string>();
            var noRevisionFromApi = 0;
            var notInCache = 0;
            var noRevisionInCache = 0;
            var revisionMismatch = 0;
            var noContentInCache = 0;

            foreach (var pageName in pageList)
            {
                if (!latestRevisions.TryGetValue(pageName, out var latestRevId))
                {
                    // 리비전 정보를 가져오지 못한 페이지는 업데이트 대상
                    pagesToUpdate.Add(pageName);
                    noRevisionFromApi++;
                    continue;
                }

                if (!_pageCache.TryGetValue(pageName, out var cached))
                {
                    pagesToUpdate.Add(pageName);
                    notInCache++;
                    continue;
                }

                if (!cached.RevisionId.HasValue)
                {
                    pagesToUpdate.Add(pageName);
                    noRevisionInCache++;
                    continue;
                }

                if (cached.RevisionId.Value != latestRevId)
                {
                    pagesToUpdate.Add(pageName);
                    revisionMismatch++;
                    continue;
                }

                if (string.IsNullOrEmpty(cached.PageContent))
                {
                    pagesToUpdate.Add(pageName);
                    noContentInCache++;
                    continue;
                }

                // 모든 조건 통과 - 스킵
                pagesUpToDate.Add(pageName);
                result.UpToDate++;
            }

            var updateSummary = $"Pages: {pagesUpToDate.Count} up-to-date, {pagesToUpdate.Count} need update " +
                $"(noRevApi:{noRevisionFromApi}, notInCache:{notInCache}, noRevCache:{noRevisionInCache}, revMismatch:{revisionMismatch}, noContent:{noContentInCache})";
            progress?.Invoke(updateSummary);
            WriteLog(updateSummary);

            // noRevisionFromApi가 높으면 샘플 로그
            if (noRevisionFromApi > 0)
            {
                var sampleNoRev = pageList.Where(p => !latestRevisions.ContainsKey(p)).Take(10);
                WriteLog($"Sample pages with no revision from API: {string.Join(", ", sampleNoRev)}");
            }

            // notInCache가 높으면 샘플 로그
            if (notInCache > 0)
            {
                var sampleNotInCache = pageList.Where(p => latestRevisions.ContainsKey(p) && !_pageCache.ContainsKey(p)).Take(10);
                WriteLog($"Sample pages not in cache: {string.Join(", ", sampleNotInCache)}");
                WriteLog($"Cache contains {_pageCache.Count} pages. Sample cache keys: {string.Join(", ", _pageCache.Keys.Take(5))}");
            }

            if (pagesToUpdate.Count == 0)
            {
                return result;
            }

            // 3. 업데이트가 필요한 페이지 콘텐츠 가져오기 (5개 병렬)
            const int batchSize = 50;
            var batches = pagesToUpdate
                .Select((page, idx) => new { page, idx })
                .GroupBy(x => x.idx / batchSize)
                .Select(g => g.Select(x => x.page).ToList())
                .ToList();

            var totalBatches = batches.Count;
            var completed = 0;
            var lockObj = new object();
            var semaphore = new SemaphoreSlim(5);  // 5개 병렬

            progress?.Invoke($"Fetching page content: {totalBatches} batches (5 parallel)...");

            var tasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var pageContents = await ExportPagesWithRevisionAsync(batch, cancellationToken);

                    lock (lockObj)
                    {
                        foreach (var (pageName, content, revisionId) in pageContents)
                        {
                            // 캐시 업데이트
                            if (!_pageCache.ContainsKey(pageName))
                            {
                                _pageCache[pageName] = new CachedPageInfo
                                {
                                    PageName = pageName,
                                    CachedAt = DateTime.UtcNow
                                };
                            }

                            var cached = _pageCache[pageName];
                            var isNew = string.IsNullOrEmpty(cached.PageContent);

                            cached.PageContent = content;
                            cached.RevisionId = revisionId;
                            cached.ContentFetchedAt = DateTime.UtcNow;

                            // 콘텐츠가 변경되면 아이콘도 다시 추출
                            var newIconFileName = ExtractIconFromInfobox(content);
                            if (cached.IconFileName != newIconFileName)
                            {
                                cached.IconFileName = newIconFileName;
                                cached.IconUrl = null;
                                cached.IconUrlFetchedAt = null;
                            }

                            if (isNew)
                                result.NewPages++;
                            else
                                result.Updated++;
                        }

                        completed++;
                        if (completed % 10 == 0 || completed == totalBatches)
                        {
                            progress?.Invoke($"Fetching page content [{completed}/{totalBatches}]...");
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        result.Failed += batch.Count;
                        progress?.Invoke($"Error fetching pages: {ex.Message}");
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            progress?.Invoke($"Cache update complete: {result.NewPages} new, {result.Updated} updated, {result.UpToDate} unchanged, {result.Failed} failed");
            return result;
        }

        /// <summary>
        /// Special:Export를 사용하여 여러 페이지 소스와 리비전 ID를 가져옵니다
        /// </summary>
        private async Task<List<(string PageName, string Content, long RevisionId)>> ExportPagesWithRevisionAsync(
            List<string> pageNames,
            CancellationToken cancellationToken)
        {
            var result = new List<(string, string, long)>();

            if (pageNames.Count == 0)
                return result;

            var postData = new Dictionary<string, string>
            {
                { "catname", "" },
                { "pages", string.Join("\n", pageNames) },
                { "curonly", "1" },
                { "wpDownload", "1" }
            };

            var content = new FormUrlEncodedContent(postData);
            var response = await _httpClient.PostAsync(SpecialExportUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // XML 파싱
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xmlContent);

            var namespaces = new[]
            {
                "http://www.mediawiki.org/xml/export-0.11/",
                "http://www.mediawiki.org/xml/export-0.10/",
                "http://www.mediawiki.org/xml/export-0.9/"
            };

            System.Xml.XmlNodeList? pageNodes = null;
            System.Xml.XmlNamespaceManager? nsmgr = null;

            foreach (var ns in namespaces)
            {
                nsmgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("mw", ns);
                pageNodes = doc.SelectNodes("//mw:page", nsmgr);
                if (pageNodes != null && pageNodes.Count > 0)
                    break;
            }

            if (pageNodes == null || pageNodes.Count == 0)
            {
                pageNodes = doc.SelectNodes("//page");
            }

            if (pageNodes != null)
            {
                foreach (System.Xml.XmlNode pageNode in pageNodes)
                {
                    var titleNode = nsmgr != null
                        ? pageNode.SelectSingleNode("mw:title", nsmgr) ?? pageNode.SelectSingleNode("title")
                        : pageNode.SelectSingleNode("title");
                    var textNode = nsmgr != null
                        ? pageNode.SelectSingleNode("mw:revision/mw:text", nsmgr) ?? pageNode.SelectSingleNode("revision/text")
                        : pageNode.SelectSingleNode("revision/text");
                    var idNode = nsmgr != null
                        ? pageNode.SelectSingleNode("mw:revision/mw:id", nsmgr) ?? pageNode.SelectSingleNode("revision/id")
                        : pageNode.SelectSingleNode("revision/id");

                    if (titleNode != null && textNode != null)
                    {
                        var title = titleNode.InnerText;
                        var text = textNode.InnerText;
                        var revId = idNode != null && long.TryParse(idNode.InnerText, out var id) ? id : 0;

                        result.Add((title, text, revId));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 캐시된 페이지 콘텐츠에서 Infobox가 없는 페이지 목록을 반환합니다
        /// 캐시에 없는 페이지는 건너뜁니다 (UpdatePageCacheAsync 먼저 호출 필요)
        /// </summary>
        public HashSet<string> GetPagesWithoutInfoboxFromCache(IEnumerable<string> pageNames)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pageName in pageNames)
            {
                if (_pageCache.TryGetValue(pageName, out var cached) &&
                    !string.IsNullOrEmpty(cached.PageContent))
                {
                    // Infobox가 없으면 카테고리 설명 페이지로 간주
                    if (!cached.PageContent.Contains("{{Infobox", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(pageName);
                    }
                }
            }

            return result;
        }

        #endregion

        #region 아이콘 URL 가져오기

        /// <summary>
        /// 여러 페이지의 아이콘 URL을 배치로 가져옵니다
        /// 캐시된 페이지 콘텐츠에서 icon 파라미터를 추출하고 imageinfo API로 URL 변환
        /// 주의: UpdatePageCacheAsync를 먼저 호출하여 캐시가 준비되어 있어야 합니다
        /// </summary>
        public async Task<Dictionary<string, string?>> GetIconUrlsAsync(
            IEnumerable<string> pageNames,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string?>();
            var pageList = pageNames.ToList();
            var pagesNeedIconUrl = new List<string>(); // 아이콘 URL 조회가 필요한 페이지

            // 1. 캐시에서 확인
            foreach (var pageName in pageList)
            {
                if (_pageCache.TryGetValue(pageName, out var cached))
                {
                    if (cached.IconUrlFetchedAt.HasValue && !string.IsNullOrEmpty(cached.IconUrl))
                    {
                        // 이미 아이콘 URL이 있음
                        result[pageName] = cached.IconUrl;
                    }
                    else if (!string.IsNullOrEmpty(cached.PageContent))
                    {
                        // 콘텐츠에서 아이콘 파일명 추출 (항상 다시 파싱)
                        cached.IconFileName = ExtractIconFromInfobox(cached.PageContent);
                        if (!string.IsNullOrEmpty(cached.IconFileName))
                        {
                            pagesNeedIconUrl.Add(pageName);
                        }
                    }
                    // 콘텐츠가 없으면 스킵 (UpdatePageCacheAsync 먼저 호출 필요)
                }
            }

            progress?.Invoke($"Icon URLs: {result.Count} cached, {pagesNeedIconUrl.Count} need URL resolution");

            if (pagesNeedIconUrl.Count == 0)
            {
                return result;
            }

            // 2. 아이콘 파일명 수집
            var iconFileNames = new Dictionary<string, string>();
            foreach (var pageName in pagesNeedIconUrl)
            {
                if (_pageCache.TryGetValue(pageName, out var cached) &&
                    !string.IsNullOrEmpty(cached.IconFileName))
                {
                    iconFileNames[pageName] = cached.IconFileName;
                }
            }

            progress?.Invoke($"Found {iconFileNames.Count} icon file names, resolving URLs...");

            // 3. imageinfo API로 파일명을 실제 URL로 변환
            var uniqueFileNames = iconFileNames.Values.Distinct().ToList();
            var fileUrlMap = await GetFileUrlsAsync(uniqueFileNames, progress, cancellationToken);

            // 4. 결과 매핑 및 캐시 저장 (대소문자/공백/언더스코어 무시하여 매칭)
            foreach (var pageName in pagesNeedIconUrl)
            {
                string? iconUrl = null;

                if (iconFileNames.TryGetValue(pageName, out var fileName))
                {
                    // 직접 매칭 시도
                    if (!fileUrlMap.TryGetValue(fileName, out iconUrl))
                    {
                        // 대소문자 무시, 공백/언더스코어 정규화하여 매칭 시도
                        var normalizedFileName = fileName.Replace(" ", "_").ToLowerInvariant();
                        foreach (var kvp in fileUrlMap)
                        {
                            var normalizedKey = kvp.Key.Replace(" ", "_").ToLowerInvariant();
                            if (normalizedKey == normalizedFileName)
                            {
                                iconUrl = kvp.Value;
                                break;
                            }
                        }
                    }
                }

                result[pageName] = iconUrl;

                // 캐시 업데이트
                if (_pageCache.TryGetValue(pageName, out var cached))
                {
                    cached.IconUrl = iconUrl;
                    cached.IconUrlFetchedAt = DateTime.UtcNow;
                }
            }

            var withIcon = result.Values.Count(v => !string.IsNullOrEmpty(v));
            progress?.Invoke($"Icon URLs resolved: {withIcon}/{result.Count} items have icons");

            return result;
        }

        /// <summary>
        /// Infobox에서 icon 파라미터를 추출합니다
        /// |icon = FileName.png 형식
        /// icon이 없으면 |image 파라미터를 fallback으로 사용 (Weapons 등)
        /// </summary>
        private static string? ExtractIconFromInfobox(string pageContent)
        {
            if (string.IsNullOrEmpty(pageContent))
                return null;

            // 1. |icon = FileName.png 또는 |icon=FileName.png 패턴 매칭 시도
            var iconValue = ExtractImageParam(pageContent, "icon");
            if (!string.IsNullOrEmpty(iconValue))
                return iconValue;

            // 2. icon이 없으면 |image 파라미터 fallback (Weapons 카테고리 등)
            iconValue = ExtractImageParam(pageContent, "image");
            if (!string.IsNullOrEmpty(iconValue))
                return iconValue;

            return null;
        }

        /// <summary>
        /// Infobox에서 특정 이미지 파라미터를 추출합니다
        /// </summary>
        private static string? ExtractImageParam(string pageContent, string paramName)
        {
            // |paramName = FileName.png 또는 |paramName=FileName.png 패턴 매칭
            // 공백 허용, 파일명에 공백/특수문자 허용
            var match = Regex.Match(pageContent, $@"\|{paramName}\s*=\s*([^\|\}}\n]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var value = match.Groups[1].Value.Trim();

                // 파일명만 추출 (File: 접두사 제거, [[]] 제거)
                value = Regex.Replace(value, @"^\[\[(?:File:|Image:)?", "", RegexOptions.IgnoreCase);
                value = Regex.Replace(value, @"\]\]$", "");
                value = Regex.Replace(value, @"^(?:File:|Image:)", "", RegexOptions.IgnoreCase);

                // 파이프 이후 제거 (|thumb, |100px 등)
                var pipeIndex = value.IndexOf('|');
                if (pipeIndex > 0)
                {
                    value = value.Substring(0, pipeIndex);
                }

                value = value.Trim();

                // URL 인코딩된 문자 디코딩 (%2C -> , 등)
                try
                {
                    value = Uri.UnescapeDataString(value);
                }
                catch
                {
                    // 디코딩 실패 시 원본 유지
                }

                // 유효한 이미지 파일명인지 확인
                if (!string.IsNullOrEmpty(value) &&
                    (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                     value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                     value.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                     value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// 파일명 목록을 실제 URL로 변환합니다 (imageinfo API)
        /// 5개 인스턴스로 병렬 처리
        /// </summary>
        private async Task<Dictionary<string, string>> GetFileUrlsAsync(
            List<string> fileNames,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            var result = new ConcurrentDictionary<string, string>();

            if (fileNames.Count == 0)
                return new Dictionary<string, string>(result);

            const int batchSize = 50;
            const int parallelInstances = 5;

            // 50개씩 배치로 나눔
            var batches = new List<List<string>>();
            for (int i = 0; i < fileNames.Count; i += batchSize)
            {
                batches.Add(fileNames.Skip(i).Take(batchSize).ToList());
            }

            var totalBatches = batches.Count;
            var processedBatches = 0;

            // 세마포어로 동시 실행 수 제한
            using var semaphore = new SemaphoreSlim(parallelInstances);

            var tasks = batches.Select(async (batch, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var currentProcessed = Interlocked.Increment(ref processedBatches);
                    progress?.Invoke($"Resolving file URLs [{currentProcessed}/{totalBatches}] (parallel x{parallelInstances})...");

                    await ProcessFileUrlBatchAsync(batch, result, cancellationToken);

                    // Rate limiting per instance
                    await Task.Delay(50, cancellationToken);
                }
                catch (Exception ex)
                {
                    progress?.Invoke($"Error resolving file URLs batch {index + 1}: {ex.Message}");
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks);

            return new Dictionary<string, string>(result);
        }

        /// <summary>
        /// 단일 배치의 파일 URL을 처리합니다
        /// </summary>
        private async Task ProcessFileUrlBatchAsync(
            List<string> batch,
            ConcurrentDictionary<string, string> result,
            CancellationToken cancellationToken)
        {
            // File: 접두사 추가하여 titles 생성
            var titles = string.Join("|", batch.Select(f => "File:" + f.Replace(" ", "_")));
            var url = $"{MediaWikiApiUrl}?action=query&titles={Uri.EscapeDataString(titles)}&prop=imageinfo&iiprop=url&format=json";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("query", out var query) &&
                query.TryGetProperty("pages", out var pages))
            {
                foreach (var page in pages.EnumerateObject())
                {
                    var pageData = page.Value;

                    if (pageData.TryGetProperty("title", out var titleProp))
                    {
                        var title = titleProp.GetString();
                        if (!string.IsNullOrEmpty(title))
                        {
                            // "File:" 접두사 제거
                            var fileName = title.StartsWith("File:") ? title.Substring(5) : title;

                            if (pageData.TryGetProperty("imageinfo", out var imageinfo) &&
                                imageinfo.GetArrayLength() > 0)
                            {
                                var firstInfo = imageinfo[0];
                                if (firstInfo.TryGetProperty("url", out var urlProp))
                                {
                                    var imageUrl = urlProp.GetString();
                                    if (!string.IsNullOrEmpty(imageUrl))
                                    {
                                        // 언더스코어 버전과 공백 버전 모두 저장 (매칭 호환성)
                                        result[fileName] = imageUrl;
                                        result[fileName.Replace("_", " ")] = imageUrl;
                                        result[fileName.Replace(" ", "_")] = imageUrl;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region 아이콘 이미지 다운로드

        /// <summary>
        /// 아이콘 이미지를 WikiID로 저장하여 배치 다운로드합니다
        /// </summary>
        public async Task<IconDownloadResult> DownloadIconsAsync(
            IEnumerable<(string WikiId, string? IconUrl)> items,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new IconDownloadResult();
            var itemList = items.Where(i => !string.IsNullOrEmpty(i.IconUrl)).ToList();

            result.TotalItems = itemList.Count;

            if (itemList.Count == 0)
            {
                progress?.Invoke("No icons to download");
                return result;
            }

            // 이미 다운로드된 아이콘 확인
            var toDownload = new List<(string WikiId, string IconUrl)>();
            foreach (var (wikiId, iconUrl) in itemList)
            {
                var ext = GetImageExtension(iconUrl!);
                var filePath = Path.Combine(_iconDir, $"{wikiId}{ext}");

                if (File.Exists(filePath))
                {
                    result.AlreadyDownloaded++;
                }
                else
                {
                    toDownload.Add((wikiId, iconUrl!));
                }
            }

            progress?.Invoke($"Icons: {result.AlreadyDownloaded} already downloaded, {toDownload.Count} to download");

            if (toDownload.Count == 0)
            {
                return result;
            }

            // 병렬 다운로드 (최대 5개 동시)
            var semaphore = new SemaphoreSlim(5);
            var lockObj = new object();
            var completed = 0;

            var tasks = toDownload.Select(async item =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var success = await DownloadIconAsync(item.WikiId, item.IconUrl, cancellationToken);

                    lock (lockObj)
                    {
                        completed++;
                        if (success)
                            result.Downloaded++;
                        else
                            result.Failed++;

                        if (completed % 100 == 0 || completed == toDownload.Count)
                        {
                            progress?.Invoke($"Downloading icons: {completed}/{toDownload.Count} ({result.Downloaded} success, {result.Failed} failed)");
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            progress?.Invoke($"Icon download complete: {result.Downloaded} downloaded, {result.Failed} failed, {result.AlreadyDownloaded} already existed");
            return result;
        }

        /// <summary>
        /// 단일 아이콘 다운로드
        /// </summary>
        private async Task<bool> DownloadIconAsync(string wikiId, string iconUrl, CancellationToken cancellationToken)
        {
            try
            {
                var ext = GetImageExtension(iconUrl);
                var filePath = Path.Combine(_iconDir, $"{wikiId}{ext}");

                using var response = await _httpClient.GetAsync(iconUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);

                return true;
            }
            catch (Exception ex)
            {
                // 실패한 다운로드 로깅
                _failedDownloads.TryAdd(wikiId, (iconUrl, ex.Message));
                return false;
            }
        }

        // 실패한 다운로드 추적용
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Url, string Error)> _failedDownloads = new();

        /// <summary>
        /// 실패한 다운로드 목록 반환 및 초기화
        /// </summary>
        public Dictionary<string, (string Url, string Error)> GetAndClearFailedDownloads()
        {
            var result = new Dictionary<string, (string Url, string Error)>(_failedDownloads);
            _failedDownloads.Clear();
            return result;
        }

        /// <summary>
        /// URL에서 이미지 확장자 추출
        /// </summary>
        private static string GetImageExtension(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var ext = Path.GetExtension(path).ToLowerInvariant();

                // 일반적인 이미지 확장자만 허용
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".webp")
                {
                    return ext;
                }
            }
            catch { }

            return ".png"; // 기본값
        }

        /// <summary>
        /// 특정 WikiID의 아이콘 파일 경로 가져오기 (없으면 null)
        /// </summary>
        public string? GetIconPath(string wikiId)
        {
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
            foreach (var ext in extensions)
            {
                var path = Path.Combine(_iconDir, $"{wikiId}{ext}");
                if (File.Exists(path))
                    return path;
            }
            return null;
        }

        #endregion

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    #region Cache Models

    /// <summary>
    /// 페이지 캐시 데이터 (JSON 저장용)
    /// </summary>
    public class PageCacheData
    {
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonPropertyName("pages")]
        public Dictionary<string, CachedPageInfo> Pages { get; set; } = new();
    }

    /// <summary>
    /// 캐시된 페이지 정보
    /// </summary>
    public class CachedPageInfo
    {
        [JsonPropertyName("pageName")]
        public string PageName { get; set; } = "";

        [JsonPropertyName("revisionId")]
        public long? RevisionId { get; set; }

        [JsonPropertyName("pageContent")]
        public string? PageContent { get; set; }

        [JsonPropertyName("iconFileName")]
        public string? IconFileName { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("cachedAt")]
        public DateTime CachedAt { get; set; }

        [JsonPropertyName("contentFetchedAt")]
        public DateTime? ContentFetchedAt { get; set; }

        [JsonPropertyName("iconUrlFetchedAt")]
        public DateTime? IconUrlFetchedAt { get; set; }
    }

    /// <summary>
    /// 아이콘 다운로드 결과
    /// </summary>
    public class IconDownloadResult
    {
        public int TotalItems { get; set; }
        public int Downloaded { get; set; }
        public int AlreadyDownloaded { get; set; }
        public int Failed { get; set; }
    }

    /// <summary>
    /// 페이지 캐시 업데이트 결과
    /// </summary>
    public class PageCacheUpdateResult
    {
        public int TotalPages { get; set; }
        public int NewPages { get; set; }
        public int Updated { get; set; }
        public int UpToDate { get; set; }
        public int Failed { get; set; }
    }

    #endregion
}
