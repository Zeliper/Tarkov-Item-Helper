using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TarkovDBEditor.Services
{
    /// <summary>
    /// Wiki 퀘스트 데이터를 가져오고 tarkov.dev와 매칭하는 서비스
    /// </summary>
    public class WikiQuestService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string MediaWikiApiUrl = "https://escapefromtarkov.fandom.com/api.php";
        private const string SpecialExportUrl = "https://escapefromtarkov.fandom.com/wiki/Special:Export";
        private const string TarkovDevApiUrl = "https://api.tarkov.dev/graphql";

        private readonly string _cacheDir;
        private readonly string _questCachePath;
        private readonly string _logPath;

        // 퀘스트 캐시
        private Dictionary<string, CachedQuestInfo> _questCache = new();

        // 제외할 카테고리
        private static readonly string[] ExcludeCategories = new[]
        {
            "Event quests",
            "Seasonal quests",
            "Legacy quests",
            "Event content",
            "Historical content"
        };

        // 제외할 페이지 (카테고리 개요 페이지 등)
        private static readonly HashSet<string> ExcludePages = new(StringComparer.OrdinalIgnoreCase)
        {
            "Quests"  // 퀘스트 개요 페이지
        };

        // 트레이더 본명 -> 일반 이름 매핑
        private static readonly Dictionary<string, string> TraderNameAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Pavel Yegorovich Romanenko", "Prapor" },
            { "Elvira Khabibullina", "Therapist" },
            { "Alexander Fyodorovich Kiselyov", "Skier" },
            { "Abramyan Arshavir Sarkisivich", "Ragman" },
            { "Arshavir Sarkisivich", "Ragman" }
        };

        public WikiQuestService(string? basePath = null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TarkovDBEditor/1.0");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);

            basePath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wiki_data");
            _cacheDir = Path.Combine(basePath, "cache");
            _questCachePath = Path.Combine(_cacheDir, "quest_cache.json");
            _logPath = Path.Combine(_cacheDir, "quest_update.log");

            Directory.CreateDirectory(_cacheDir);
        }

        private void WriteLog(string message)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.AppendAllText(_logPath, $"[{timestamp}] {message}\n");
            }
            catch { }
        }

        public void ClearLog()
        {
            try { if (File.Exists(_logPath)) File.Delete(_logPath); } catch { }
        }

        #region 캐시 관리

        public async Task LoadCacheAsync(CancellationToken cancellationToken = default)
        {
            if (File.Exists(_questCachePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_questCachePath, cancellationToken);
                    var cache = JsonSerializer.Deserialize<QuestCacheFile>(json);
                    if (cache?.Quests != null)
                    {
                        _questCache = cache.Quests;
                    }
                }
                catch { _questCache = new(); }
            }
        }

        public async Task SaveCacheAsync(CancellationToken cancellationToken = default)
        {
            var cache = new QuestCacheFile
            {
                LastUpdated = DateTime.UtcNow,
                Quests = _questCache
            };

            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_questCachePath, json, cancellationToken);
        }

        public (int total, int withContent, int withRevision) GetCacheStats()
        {
            var total = _questCache.Count;
            var withContent = _questCache.Values.Count(p => !string.IsNullOrEmpty(p.PageContent));
            var withRevision = _questCache.Values.Count(p => p.RevisionId.HasValue);
            return (total, withContent, withRevision);
        }

        #endregion

        #region Wiki 퀘스트 목록 가져오기

        /// <summary>
        /// Quests 카테고리에서 모든 퀘스트 목록을 가져옵니다 (이벤트 퀘스트 제외)
        /// </summary>
        public async Task<List<string>> GetAllQuestPagesAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke("Fetching quest list from Wiki...");

            // 먼저 제외할 퀘스트 목록 가져오기
            var excludedQuests = await GetExcludedQuestsAsync(progress, cancellationToken);
            WriteLog($"Excluded quests count: {excludedQuests.Count}");

            var questPages = new List<string>();
            string? continueToken = null;

            do
            {
                var url = $"{MediaWikiApiUrl}?action=query&list=categorymembers&cmtitle=Category:Quests&cmlimit=500&cmtype=page&format=json";
                if (!string.IsNullOrEmpty(continueToken))
                {
                    url += $"&cmcontinue={Uri.EscapeDataString(continueToken)}";
                }

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("query", out var query) &&
                    query.TryGetProperty("categorymembers", out var members))
                {
                    foreach (var member in members.EnumerateArray())
                    {
                        var title = member.GetProperty("title").GetString();
                        if (!string.IsNullOrEmpty(title) &&
                            !excludedQuests.Contains(title) &&
                            !ExcludePages.Contains(title))
                        {
                            questPages.Add(title);
                        }
                    }
                }

                continueToken = null;
                if (doc.RootElement.TryGetProperty("continue", out var cont) &&
                    cont.TryGetProperty("cmcontinue", out var cmcontinue))
                {
                    continueToken = cmcontinue.GetString();
                }
            } while (!string.IsNullOrEmpty(continueToken));

            progress?.Invoke($"Found {questPages.Count} quests (excluding {excludedQuests.Count} event/legacy quests)");
            WriteLog($"Total quests: {questPages.Count}");

            return questPages;
        }

        /// <summary>
        /// 제외할 퀘스트 목록 가져오기 (이벤트, 시즌, 레거시)
        /// </summary>
        private async Task<HashSet<string>> GetExcludedQuestsAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var category in ExcludeCategories)
            {
                progress?.Invoke($"Fetching excluded category: {category}...");

                string? continueToken = null;
                do
                {
                    var url = $"{MediaWikiApiUrl}?action=query&list=categorymembers&cmtitle=Category:{Uri.EscapeDataString(category)}&cmlimit=500&cmtype=page&format=json";
                    if (!string.IsNullOrEmpty(continueToken))
                    {
                        url += $"&cmcontinue={Uri.EscapeDataString(continueToken)}";
                    }

                    try
                    {
                        var response = await _httpClient.GetAsync(url, cancellationToken);
                        response.EnsureSuccessStatusCode();

                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        using var doc = JsonDocument.Parse(json);

                        if (doc.RootElement.TryGetProperty("query", out var query) &&
                            query.TryGetProperty("categorymembers", out var members))
                        {
                            foreach (var member in members.EnumerateArray())
                            {
                                var title = member.GetProperty("title").GetString();
                                if (!string.IsNullOrEmpty(title))
                                {
                                    excluded.Add(title);
                                }
                            }
                        }

                        continueToken = null;
                        if (doc.RootElement.TryGetProperty("continue", out var cont) &&
                            cont.TryGetProperty("cmcontinue", out var cmcontinue))
                        {
                            continueToken = cmcontinue.GetString();
                        }
                    }
                    catch
                    {
                        break; // 카테고리가 없을 수 있음
                    }
                } while (!string.IsNullOrEmpty(continueToken));
            }

            return excluded;
        }

        #endregion

        #region 리비전 기반 캐싱

        /// <summary>
        /// 퀘스트 페이지의 최신 리비전 ID를 가져옵니다 (50개씩 5개 병렬)
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

            const int batchSize = 50;
            var batches = pageList
                .Select((page, idx) => new { page, idx })
                .GroupBy(x => x.idx / batchSize)
                .Select(g => g.Select(x => x.page).ToList())
                .ToList();

            var totalBatches = batches.Count;
            var completed = 0;
            var lockObj = new object();
            var semaphore = new SemaphoreSlim(5);

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
                            result[kvp.Key] = kvp.Value;
                        completed++;
                        if (completed % 5 == 0 || completed == totalBatches)
                            progress?.Invoke($"Checking revisions [{completed}/{totalBatches}]...");
                    }
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            progress?.Invoke($"Revision check complete: {result.Count} quests");
            return result;
        }

        private async Task<Dictionary<string, long>> FetchRevisionsBatchAsync(
            List<string> pageNames,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, long>();
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
                            result[title] = revId;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 퀘스트 캐시 업데이트 (리비전 비교)
        /// </summary>
        public async Task<QuestCacheUpdateResult> UpdateQuestCacheAsync(
            IEnumerable<string> questNames,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new QuestCacheUpdateResult();
            var questList = questNames.ToList();
            result.TotalQuests = questList.Count;

            progress?.Invoke($"Checking {questList.Count} quests for updates...");

            var latestRevisions = await GetLatestRevisionIdsAsync(questList, progress, cancellationToken);

            var questsToUpdate = new List<string>();
            var noRevFromApi = 0;
            var notInCache = 0;
            var revMismatch = 0;

            foreach (var questName in questList)
            {
                if (!latestRevisions.TryGetValue(questName, out var latestRevId))
                {
                    questsToUpdate.Add(questName);
                    noRevFromApi++;
                    continue;
                }

                if (!_questCache.TryGetValue(questName, out var cached) ||
                    !cached.RevisionId.HasValue)
                {
                    questsToUpdate.Add(questName);
                    notInCache++;
                    continue;
                }

                if (cached.RevisionId.Value != latestRevId)
                {
                    questsToUpdate.Add(questName);
                    revMismatch++;
                    continue;
                }

                result.UpToDate++;
            }

            var summary = $"Quests: {result.UpToDate} up-to-date, {questsToUpdate.Count} need update (noRevApi:{noRevFromApi}, notInCache:{notInCache}, revMismatch:{revMismatch})";
            progress?.Invoke(summary);
            WriteLog(summary);

            if (questsToUpdate.Count == 0)
                return result;

            // 업데이트 필요한 퀘스트 콘텐츠 가져오기 (50개씩 5개 병렬)
            const int batchSize = 50;
            var batches = questsToUpdate
                .Select((q, idx) => new { q, idx })
                .GroupBy(x => x.idx / batchSize)
                .Select(g => g.Select(x => x.q).ToList())
                .ToList();

            var totalBatches = batches.Count;
            var completed = 0;
            var lockObj = new object();
            var semaphore = new SemaphoreSlim(5);

            progress?.Invoke($"Fetching quest content: {totalBatches} batches (5 parallel)...");

            var tasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var contents = await ExportPagesAsync(batch, cancellationToken);
                    lock (lockObj)
                    {
                        foreach (var (name, content, revId) in contents)
                        {
                            if (!_questCache.ContainsKey(name))
                            {
                                _questCache[name] = new CachedQuestInfo { QuestName = name, CachedAt = DateTime.UtcNow };
                                result.NewQuests++;
                            }
                            else
                            {
                                result.Updated++;
                            }

                            var cached = _questCache[name];
                            cached.PageContent = content;
                            cached.RevisionId = revId;
                            cached.ContentFetchedAt = DateTime.UtcNow;

                            // Infobox에서 트레이더 정보 추출
                            cached.Trader = ExtractTraderFromInfobox(content);

                            // Requirements 섹션에서 MinLevel, MinScavKarma, Faction, RequiredEdition, ExcludedEdition, RequiredDecodeCount 추출
                            cached.MinLevel = ExtractMinLevel(content);
                            cached.MinScavKarma = ExtractMinScavKarma(content);
                            cached.Faction = ExtractFaction(content);
                            cached.RequiredEdition = ExtractRequiredEdition(content);
                            cached.ExcludedEdition = ExtractExcludedEdition(content);
                            cached.RequiredDecodeCount = ExtractRequiredDecodeCount(content);
                        }

                        completed++;
                        if (completed % 5 == 0 || completed == totalBatches)
                            progress?.Invoke($"Fetching quest content [{completed}/{totalBatches}]...");
                    }
                }
                catch (Exception ex)
                {
                    lock (lockObj) { result.Failed += batch.Count; }
                    WriteLog($"Error fetching quests: {ex.Message}");
                }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);
            progress?.Invoke($"Quest cache update complete: {result.NewQuests} new, {result.Updated} updated, {result.UpToDate} unchanged");
            return result;
        }

        private async Task<List<(string Name, string Content, long RevisionId)>> ExportPagesAsync(
            List<string> pageNames,
            CancellationToken cancellationToken)
        {
            var result = new List<(string, string, long)>();
            var pages = string.Join("\n", pageNames);

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("pages", pages),
                new KeyValuePair<string, string>("curonly", "1")
            });

            var response = await _httpClient.PostAsync(SpecialExportUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var xml = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);

            var nsMgr = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("mw", "http://www.mediawiki.org/xml/export-0.11/");

            var pageNodes = doc.SelectNodes("//mw:page", nsMgr);
            if (pageNodes != null)
            {
                foreach (System.Xml.XmlNode pageNode in pageNodes)
                {
                    var titleNode = pageNode.SelectSingleNode("mw:title", nsMgr);
                    var textNode = pageNode.SelectSingleNode("mw:revision/mw:text", nsMgr);
                    var revIdNode = pageNode.SelectSingleNode("mw:revision/mw:id", nsMgr);

                    if (titleNode != null && textNode != null && revIdNode != null)
                    {
                        var title = titleNode.InnerText;
                        var text = textNode.InnerText;
                        if (long.TryParse(revIdNode.InnerText, out var revId))
                        {
                            result.Add((title, text, revId));
                        }
                    }
                }
            }

            return result;
        }

        private string? ExtractTraderFromInfobox(string content)
        {
            // |given by = [[Ragman]] 또는 |givenby = [[Prapor]] 형식에서 트레이더 이름 추출
            // "given by" (공백 포함) 또는 "givenby" (공백 없음) 둘 다 지원
            var match = Regex.Match(content, @"\|given\s*by\s*=\s*\[\[([^\]|]+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return NormalizeTraderName(match.Groups[1].Value.Trim());

            // 링크 없이 직접 트레이더 이름만 있는 경우
            match = Regex.Match(content, @"\|given\s*by\s*=\s*([^\|\}\[\]\n]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var trader = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(trader))
                    return NormalizeTraderName(trader);
            }

            return null;
        }

        /// <summary>
        /// 트레이더 본명을 일반적인 트레이더 이름으로 변환
        /// </summary>
        private static string NormalizeTraderName(string traderName)
        {
            if (string.IsNullOrEmpty(traderName))
                return traderName;

            // 본명 매핑에 있으면 일반 이름으로 변환
            if (TraderNameAliases.TryGetValue(traderName, out var normalizedName))
                return normalizedName;

            return traderName;
        }

        /// <summary>
        /// PageContent에서 ==Requirements== 섹션의 MinLevel 추출
        /// 패턴: * Must be level {N} to start this quest.
        /// </summary>
        public static int? ExtractMinLevel(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            // ==Requirements== 또는 == Requirements== 섹션 찾기
            var reqMatch = Regex.Match(content, @"==\s*Requirements\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!reqMatch.Success)
                return null;

            var requirementsSection = reqMatch.Groups[1].Value;

            // * Must be level {N} to start this quest.
            var levelMatch = Regex.Match(requirementsSection, @"\*\s*Must be level (\d+) to start this quest", RegexOptions.IgnoreCase);
            if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var level))
            {
                return level;
            }

            return null;
        }

        /// <summary>
        /// PageContent에서 Kappa 필수 여부를 추출
        /// Infobox의 |reqkappa = Yes/No 필드를 파싱
        /// </summary>
        public static bool ExtractKappaRequired(string content)
        {
            if (string.IsNullOrEmpty(content))
                return false;

            // Infobox에서 reqkappa 필드 찾기
            // 예: |reqkappa     =<font color="red">Yes</font>
            // 예: |reqkappa     =<font color="green">No</font>
            var match = Regex.Match(content, @"\|reqkappa\s*=\s*(?:<font[^>]*>)?\s*(Yes|No)\s*(?:</font>)?", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Equals("Yes", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// PageContent에서 "Related Quest Items" wikitable을 파싱하여 필요 아이템 목록 반환
        /// TarkovHelper의 WikiQuestParser.ParseRequiredItems 로직을 참고
        /// </summary>
        public static List<ParsedRequiredItem> ExtractRequiredItems(string content)
        {
            var items = new List<ParsedRequiredItem>();
            if (string.IsNullOrEmpty(content))
                return items;

            // "Related Quest Items" 텍스트 위치 찾기
            var relatedIndex = content.IndexOf("Related Quest Items", StringComparison.OrdinalIgnoreCase);
            if (relatedIndex < 0)
                return items;

            // "Related Quest Items" 앞에서 가장 가까운 {| 찾기 (테이블 시작)
            var tableStartIndex = content.LastIndexOf("{|", relatedIndex, StringComparison.Ordinal);
            if (tableStartIndex < 0)
                return items;

            // 해당 테이블의 끝 |} 찾기
            var tableEndIndex = content.IndexOf("|}", relatedIndex, StringComparison.Ordinal);
            if (tableEndIndex < 0)
                return items;

            var tableContent = content.Substring(tableStartIndex, tableEndIndex - tableStartIndex + 2);

            // 행 분리 "|-"
            var rows = Regex.Split(tableContent, @"\|-");

            // rowspan 처리를 위한 컬럼 값 저장
            string? currentNotes = null;
            int notesRowSpan = 0;

            int sortOrder = 0;
            foreach (var row in rows)
            {
                // 헤더 행 건너뛰기 - 실제 헤더 셀 형식(!Icon, !Name 등)만 체크
                // 데이터 행에서 파일명(VodkaIcon.png 등)이나 FIR 컬럼(!<font...)을 잘못 필터링하지 않도록 함
                if (Regex.IsMatch(row, @"!\s*Icon|!\s*Item|!\s*Name|!\s*Amount|!\s*Quantity", RegexOptions.IgnoreCase))
                    continue;

                // rowspan 확인 (Notes 컬럼)
                var rowspanMatch = Regex.Match(row, @"rowspan\s*=\s*""?(\d+)""?", RegexOptions.IgnoreCase);
                if (rowspanMatch.Success)
                {
                    notesRowSpan = int.Parse(rowspanMatch.Groups[1].Value);
                    // Notes 추출
                    var notesMatch = Regex.Match(row, @"rowspan[^|]*\|\s*([^|]+?)(?:\||\}\})");
                    if (notesMatch.Success)
                    {
                        currentNotes = notesMatch.Groups[1].Value.Trim();
                    }
                }

                var item = ParseTableRow(row, notesRowSpan > 0 ? currentNotes : null);
                if (item != null)
                {
                    item.SortOrder = sortOrder++;
                    items.Add(item);
                }

                // rowspan 카운트다운
                if (notesRowSpan > 0)
                {
                    notesRowSpan--;
                    if (notesRowSpan == 0)
                    {
                        currentNotes = null;
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// wikitable 행을 파싱하여 ParsedRequiredItem 반환
        /// </summary>
        private static ParsedRequiredItem? ParseTableRow(string row, string? effectiveNotes = null)
        {
            // 헤더 행(!)이 포함된 경우 스킵
            if (row.Contains("!Icon") || row.Contains("!Item") || row.Contains("! Icon") || row.Contains("! Item"))
                return null;

            // | 또는 || 또는 줄바꿈+| 또는 줄바꿈+! 로 셀 분리
            // Wiki 테이블 형식: 줄바꿈 후 | 또는 ! 로 시작하는 셀들
            // ! 는 헤더 스타일 셀이지만 FIR 컬럼 등에서 데이터로 사용됨
            var cells = Regex.Split(row, @"\|\||\n\||\n!")
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c) && !c.StartsWith("{|") && !c.StartsWith("|}"))
                .ToList();

            if (cells.Count < 2)
                return null;

            string? itemId = null;
            string? itemName = null;
            int amount = 1;
            string requirement = "Required";
            bool foundInRaid = false;
            int? dogtagMinLevel = null;
            string? dogtagFaction = null;

            int columnIndex = 0;
            foreach (var rawCell in cells)
            {
                // Wiki 테이블 셀 스타일 처리: "style="..."| content" 형식에서 실제 내용 추출
                var cell = rawCell;
                if (cell.StartsWith("style", StringComparison.OrdinalIgnoreCase))
                {
                    // style="..."| 뒤의 내용 추출
                    var pipeIndex = cell.IndexOf('|');
                    if (pipeIndex > 0 && pipeIndex < cell.Length - 1)
                    {
                        cell = cell.Substring(pipeIndex + 1).Trim();
                    }
                    else
                    {
                        // 스타일만 있고 내용이 없으면 스킵
                        continue;
                    }
                }

                // rowspan, colspan 같은 속성만 있는 셀 건너뛰기
                if (cell.StartsWith("rowspan") || cell.StartsWith("colspan"))
                    continue;

                // 아이콘 컬럼 건너뛰기 (이미지)
                if (columnIndex == 0 && (cell.Contains("[[File:") || cell.Contains("[[Image:")))
                {
                    columnIndex++;
                    continue;
                }

                // Column 1: Name - {{itemId}} 템플릿 또는 [[Item Name]]
                if (columnIndex == 0 || columnIndex == 1)
                {
                    // {{itemId}} 템플릿 (24자 hex)
                    var templateMatch = Regex.Match(cell, @"\{\{([a-f0-9]{24})\}\}");
                    if (templateMatch.Success)
                    {
                        itemId = templateMatch.Groups[1].Value;
                        columnIndex = 2;
                        continue;
                    }

                    // [[Item Name]] 또는 [[Item Name|Display]] 형식
                    var linkMatch = Regex.Match(cell, @"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]");
                    if (linkMatch.Success)
                    {
                        var linkName = linkMatch.Groups[1].Value.Trim();
                        var displayName = linkMatch.Groups[2].Success ? linkMatch.Groups[2].Value.Trim() : null;

                        // [[Dogtag|BEAR Dogtag]] 같은 케이스
                        if (displayName != null && linkName.Equals("Dogtag", StringComparison.OrdinalIgnoreCase))
                        {
                            itemName = displayName;
                        }
                        else
                        {
                            itemName = linkName;
                        }
                        columnIndex = 2;
                        continue;
                    }

                    columnIndex++;
                    continue;
                }

                // Column 2: Quantity
                if (columnIndex == 2)
                {
                    if (int.TryParse(cell.Trim(), out var parsedAmount))
                    {
                        amount = parsedAmount;
                    }
                    columnIndex++;
                    continue;
                }

                // Column 3: Requirements (Handover item, Required, Optional)
                if (columnIndex == 3)
                {
                    if (cell.Contains("Handover", StringComparison.OrdinalIgnoreCase))
                        requirement = "Handover";
                    else if (cell.Contains("Required", StringComparison.OrdinalIgnoreCase))
                        requirement = "Required";
                    else if (cell.Contains("Optional", StringComparison.OrdinalIgnoreCase))
                        requirement = "Optional";
                    columnIndex++;
                    continue;
                }

                // Column 4: Found in Raid (Yes/No)
                if (columnIndex == 4)
                {
                    foundInRaid = cell.Contains("Yes", StringComparison.OrdinalIgnoreCase) &&
                                  !cell.Contains("N/A", StringComparison.OrdinalIgnoreCase);
                    columnIndex++;
                    continue;
                }

                // Column 5: Notes
                if (columnIndex == 5)
                {
                    effectiveNotes = cell.Trim();
                    columnIndex++;
                    continue;
                }

                columnIndex++;
            }

            // 아이템 정보가 없으면 null 반환
            if (string.IsNullOrEmpty(itemId) && string.IsNullOrEmpty(itemName))
                return null;

            // 도그태그 레벨 파싱 (Notes에서)
            if (!string.IsNullOrEmpty(effectiveNotes))
            {
                var levelMatch = Regex.Match(effectiveNotes, @"level\s+(\d+)\s+or\s+higher", RegexOptions.IgnoreCase);
                if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var minLevel))
                {
                    dogtagMinLevel = minLevel;
                }
            }

            // 도그태그 진영 파싱 (아이템 이름에서)
            if (!string.IsNullOrEmpty(itemName))
            {
                if (Regex.IsMatch(itemName, @"\bBEAR\b", RegexOptions.IgnoreCase))
                {
                    dogtagFaction = "BEAR";
                }
                else if (Regex.IsMatch(itemName, @"\bUSEC\b", RegexOptions.IgnoreCase))
                {
                    dogtagFaction = "USEC";
                }
            }

            return new ParsedRequiredItem
            {
                ItemId = itemId,
                ItemName = itemName ?? "",
                Count = amount > 0 ? amount : 1,
                RequirementType = requirement,
                RequiresFIR = foundInRaid,
                DogtagMinLevel = dogtagMinLevel,
                DogtagFaction = dogtagFaction
            };
        }

        /// <summary>
        /// PageContent에서 ==Objectives== 섹션을 파싱하여 목표 목록 반환
        /// </summary>
        public static List<ParsedObjective> ExtractObjectives(string content)
        {
            var objectives = new List<ParsedObjective>();
            if (string.IsNullOrEmpty(content))
                return objectives;

            // ==Objectives== 섹션 찾기
            var objMatch = Regex.Match(content, @"==\s*Objectives\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!objMatch.Success)
                return objectives;

            var objectivesSection = objMatch.Groups[1].Value;

            // * 로 시작하는 각 라인 파싱
            var lines = objectivesSection.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("*"))
                .Select(l => l.TrimStart('*').Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            int sortOrder = 0;
            foreach (var line in lines)
            {
                var objective = ParseObjectiveLine(line);
                objective.SortOrder = sortOrder++;
                objectives.Add(objective);
            }

            return objectives;
        }

        /// <summary>
        /// 단일 Objective 라인을 파싱
        /// </summary>
        private static ParsedObjective ParseObjectiveLine(string line)
        {
            var obj = new ParsedObjective
            {
                Description = CleanWikiMarkup(line)
            };

            // 맵 이름 추출 - [[Customs]], [[Factory]], [[Shoreline]] 등
            var mapNames = new[] { "Customs", "Factory", "Shoreline", "Interchange", "Reserve", "Woods", "Lighthouse", "Streets of Tarkov", "Ground Zero", "Lab", "The Lab" };
            foreach (var mapName in mapNames)
            {
                if (Regex.IsMatch(line, $@"\[\[{Regex.Escape(mapName)}\]\]", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, $@"\bon {Regex.Escape(mapName)}\b", RegexOptions.IgnoreCase))
                {
                    obj.MapName = mapName == "The Lab" ? "Lab" : mapName;
                    break;
                }
            }

            // Found in Raid 체크
            // "found in raid" 또는 단순히 "in raid" (예: Find [[Item]] <font color="red">in raid</font>)
            obj.RequiresFIR = Regex.IsMatch(line, @"\bin\s+raid\b", RegexOptions.IgnoreCase);

            // 타입 및 상세 정보 파싱
            // Kill 패턴 0: Eliminate any X targets/enemies (일반 대상)
            // 예: "Eliminate any 20 targets"
            // 예: "Eliminate any 10 enemies"
            var killAnyMatch = Regex.Match(line, @"(?:Eliminate|Kill)\s+any\s+(\d+)\s+(?:targets?|enemies?|operators?)", RegexOptions.IgnoreCase);
            if (killAnyMatch.Success)
            {
                obj.Type = ObjectiveType.Kill;
                obj.TargetCount = int.Parse(killAnyMatch.Groups[1].Value);
                obj.TargetType = "Any"; // 일반 대상
                ExtractKillConditions(line, obj);
                return obj;
            }

            // Kill 패턴 1: Eliminate X [enemy] (숫자가 있는 경우)
            var killMatch = Regex.Match(line, @"(?:Eliminate|Kill)\s+(\d+)\s+(?:\[\[)?([^\]\s]+)", RegexOptions.IgnoreCase);
            if (killMatch.Success)
            {
                obj.Type = ObjectiveType.Kill;
                obj.TargetCount = int.Parse(killMatch.Groups[1].Value);
                obj.TargetType = ExtractWikiLinkName(killMatch.Groups[2].Value);
                ExtractKillConditions(line, obj);
                return obj;
            }

            // Kill 패턴 2: Eliminate [[Target]] (숫자 없이 특정 대상 처치)
            // 예: "Eliminate [[Cultist priest|Priest]]"
            var killNamedMatch = Regex.Match(line, @"(?:Eliminate|Kill)\s+\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", RegexOptions.IgnoreCase);
            if (killNamedMatch.Success)
            {
                obj.Type = ObjectiveType.Kill;
                obj.TargetCount = 1;
                // 링크 타겟(첫번째 그룹)을 TargetType으로 사용
                obj.TargetType = killNamedMatch.Groups[1].Value.Trim();
                ExtractKillConditions(line, obj);
                return obj;
            }

            // HandOver 패턴 1: 도그태그 특수 형식 (진영과 아이템이 별도 링크)
            // 예: "Hand over the 5 found [[Found in raid|in raid]] [[BEAR]] [[Dogtag|PMC dogtags]]"
            // 예: "Hand over the 5 found [[Found in raid|in raid]] [[USEC]] [[Dogtag|PMC dogtags]]"
            var handOverDogtagMatch = Regex.Match(line, @"Hand\s+over\s+(?:the\s+)?(\d+)?\s*(?:found\s+)?(?:\[\[Found in raid[^\]]*\]\]\s*)?\[\[(BEAR|USEC)\]\]\s*\[\[(?:Dogtag|PMC dogtags?)(?:\|[^\]]+)?\]\]", RegexOptions.IgnoreCase);
            if (handOverDogtagMatch.Success)
            {
                obj.Type = ObjectiveType.HandOver;
                if (handOverDogtagMatch.Groups[1].Success && int.TryParse(handOverDogtagMatch.Groups[1].Value, out var count))
                    obj.TargetCount = count;
                else
                    obj.TargetCount = 1;

                // 진영에 따른 도그태그 이름 설정
                var faction = handOverDogtagMatch.Groups[2].Value.Trim().ToUpper();
                obj.ItemName = $"{faction} Dogtag";
                obj.DogtagFaction = faction;

                // 레벨 추출 (예: "level 15 or higher", "level 15+", "≥15")
                var levelMatch = Regex.Match(line, @"level\s*(\d+)\s*(?:or\s+higher|\+)?", RegexOptions.IgnoreCase);
                if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var minLevel))
                    obj.DogtagMinLevel = minLevel;

                return obj;
            }

            // HandOver 패턴 2: 일반 아이템
            // 예: "Hand over the item: 8 [[Bottle of Tarkovskaya vodka]]"
            // 예: "Hand over 3 [[Item Name|Display Name]]"
            // 예: "Hand over the found [[Found in raid|...]] item: [[SSD drive]]"
            // 예: "Hand over the 3 found [[Found in raid|...]] [[Iskra ration pack|...]]"
            // 예: "Hand over the found [[Ref dirt|info]]" (Found in raid 링크 없이)
            // 중요: "found"와 "[[Found in raid|...]]"를 각각 선택적으로 처리
            // 도그태그 예시: "Hand over 20 found in raid [[Dogtag|BEAR Dogtag]]"
            var handOverMatch = Regex.Match(line, @"Hand\s+over\s+(?:the\s+)?(\d+)?\s*(?:found\s+)?(?:\[\[Found in raid[^\]]*\]\]\s*)?(?:item:?\s+)?(?:\d+)?\s*\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", RegexOptions.IgnoreCase);
            if (handOverMatch.Success)
            {
                obj.Type = ObjectiveType.HandOver;
                if (handOverMatch.Groups[1].Success && int.TryParse(handOverMatch.Groups[1].Value, out var count))
                    obj.TargetCount = count;
                else
                    obj.TargetCount = 1;

                // 아이템 이름 결정: display name이 있으면 사용, 없으면 링크 대상 사용
                var linkTarget = handOverMatch.Groups[2].Value.Trim();
                var displayName = handOverMatch.Groups[3].Success ? handOverMatch.Groups[3].Value.Trim() : null;
                obj.ItemName = displayName ?? linkTarget;

                // 도그태그 관련 정보 추출 (아이템 이름 또는 display name에서)
                var nameToCheck = displayName ?? linkTarget;
                if (nameToCheck.Contains("Dogtag", StringComparison.OrdinalIgnoreCase) ||
                    linkTarget.Equals("Dogtag", StringComparison.OrdinalIgnoreCase))
                {
                    // 진영 추출 (아이템 이름에서)
                    if (Regex.IsMatch(nameToCheck, @"\bBEAR\b", RegexOptions.IgnoreCase))
                        obj.DogtagFaction = "BEAR";
                    else if (Regex.IsMatch(nameToCheck, @"\bUSEC\b", RegexOptions.IgnoreCase))
                        obj.DogtagFaction = "USEC";

                    // 레벨 추출 (예: "level 15 or higher", "level 15+", "≥15")
                    var levelMatch = Regex.Match(line, @"level\s*(\d+)\s*(?:or\s+higher|\+)?", RegexOptions.IgnoreCase);
                    if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var minLevel))
                        obj.DogtagMinLevel = minLevel;
                }

                return obj;
            }

            // Collect/Obtain/Find 패턴: Obtain/Find X [item]
            var collectMatch = Regex.Match(line, @"(?:Obtain|Find|Acquire)\s+(?:the\s+)?(\d+)?\s*(?:\[\[([^\]|]+)(?:\|[^\]]+)?\]\])", RegexOptions.IgnoreCase);
            if (collectMatch.Success)
            {
                obj.Type = ObjectiveType.Collect;
                if (collectMatch.Groups[1].Success && int.TryParse(collectMatch.Groups[1].Value, out var count))
                    obj.TargetCount = count;
                else
                    obj.TargetCount = 1;
                obj.ItemName = collectMatch.Groups[2].Value.Trim();
                return obj;
            }

            // Stash 패턴: Stash/Place [item] at/in [location]
            var stashMatch = Regex.Match(line, @"(?:Stash|Place|Plant)\s+(?:the\s+)?(?:(\d+)\s+)?(?:\[\[([^\]|]+)(?:\|[^\]]+)?\]\])?", RegexOptions.IgnoreCase);
            if (stashMatch.Success && (line.Contains("Stash", StringComparison.OrdinalIgnoreCase) ||
                                        line.Contains("Place", StringComparison.OrdinalIgnoreCase) ||
                                        line.Contains("Plant", StringComparison.OrdinalIgnoreCase)))
            {
                obj.Type = ObjectiveType.Stash;
                if (stashMatch.Groups[1].Success && int.TryParse(stashMatch.Groups[1].Value, out var count))
                    obj.TargetCount = count;
                else
                    obj.TargetCount = 1;
                if (!string.IsNullOrEmpty(stashMatch.Groups[2].Value))
                    obj.ItemName = stashMatch.Groups[2].Value.Trim();
                ExtractLocationFromLine(line, obj);
                return obj;
            }

            // Mark 패턴: Mark [target] with [marker] 또는 Locate and mark [target] with [marker]
            // Visit 패턴보다 먼저 체크해야 "Locate and mark"가 Visit으로 분류되지 않음
            var markMatch = Regex.Match(line, @"(?:Locate\s+and\s+)?[Mm]ark\s+(?:the\s+)?(.+?)\s+with\s+(?:an?\s+)?(?:\[\[([^\]|]+)(?:\|[^\]]+)?\]\])", RegexOptions.IgnoreCase);
            if (markMatch.Success)
            {
                obj.Type = ObjectiveType.Mark;
                obj.LocationName = CleanWikiMarkup(markMatch.Groups[1].Value);
                obj.ItemName = NormalizeItemName(markMatch.Groups[2].Value); // 마커 아이템 (정규화됨)
                return obj;
            }

            // Mark 패턴 (fallback): "Mark the ..." 또는 "Locate and mark the ..." 로 시작하는 경우 (마커 아이템 없이)
            if (Regex.IsMatch(line, @"(?:Locate\s+and\s+)?[Mm]ark\s+(?:the\s+)?", RegexOptions.IgnoreCase))
            {
                obj.Type = ObjectiveType.Mark;
                ExtractLocationFromLine(line, obj);
                // 마커 아이템 추출 시도 (with 없이 [[MS2000 Marker]] 형식)
                var markerMatch = Regex.Match(line, @"\[\[(MS2000[^\]]*|Marker[^\]]*|Signal[^\]]*)\]\]", RegexOptions.IgnoreCase);
                if (markerMatch.Success)
                    obj.ItemName = NormalizeItemName(markerMatch.Groups[1].Value); // 정규화됨
                return obj;
            }

            // Visit/Locate 패턴: Locate/Visit/Find [location]
            if (Regex.IsMatch(line, @"(?:Locate|Visit|Go to|Reach)\s+", RegexOptions.IgnoreCase))
            {
                obj.Type = ObjectiveType.Visit;
                ExtractLocationFromLine(line, obj);
                return obj;
            }

            // Complete task 패턴: Complete the task [[Quest Name]]
            // 이건 퀘스트 완료 목표이므로 아이템이 아님
            var completeMatch = Regex.Match(line, @"Complete\s+(?:the\s+)?(?:task|quest)\s+\[\[([^\]|]+)(?:\|[^\]]+)?\]\]", RegexOptions.IgnoreCase);
            if (completeMatch.Success)
            {
                obj.Type = ObjectiveType.Task;
                obj.TargetType = "Quest";
                obj.LocationName = completeMatch.Groups[1].Value.Trim(); // 퀘스트 이름을 LocationName에 저장
                // ItemName은 설정하지 않음 (퀘스트는 아이템이 아님)
                return obj;
            }

            // Survive 패턴: Survive and extract
            if (Regex.IsMatch(line, @"Survive\s+and\s+extract", RegexOptions.IgnoreCase))
            {
                obj.Type = ObjectiveType.Survive;
                return obj;
            }

            // Build 패턴: Construct/Build/Upgrade [hideout module]
            if (Regex.IsMatch(line, @"(?:Construct|Build|Upgrade)\s+", RegexOptions.IgnoreCase))
            {
                obj.Type = ObjectiveType.Build;
                var buildMatch = Regex.Match(line, @"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]");
                if (buildMatch.Success)
                    obj.ItemName = buildMatch.Groups[1].Value.Trim();
                return obj;
            }

            // 기본: Custom
            obj.Type = ObjectiveType.Custom;

            // 아이템 링크가 있으면 추출
            var itemMatch = Regex.Match(line, @"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]");
            if (itemMatch.Success)
            {
                var itemName = itemMatch.Groups[1].Value.Trim();
                // 맵 이름이 아니면 아이템으로 설정
                if (!mapNames.Any(m => m.Equals(itemName, StringComparison.OrdinalIgnoreCase)))
                    obj.ItemName = itemName;
            }

            // 숫자가 있으면 추출
            var countMatch = Regex.Match(line, @"(\d+)\s+(?:\[\[|[A-Z])");
            if (countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out var objCount))
                obj.TargetCount = objCount;

            return obj;
        }

        // 아이템 이름 별칭 (Wiki에서 사용하는 짧은 이름 -> 실제 아이템 테이블 이름)
        private static readonly Dictionary<string, string> ItemNameAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "MS2000", "MS2000 Marker" },
            { "MS2000 marker", "MS2000 Marker" },
            { "Marker", "MS2000 Marker" },
            { "Signal Jammer", "Signal Jammer" },
            { "Wi-Fi Camera", "Wi-Fi Camera" },
            { "Wifi Camera", "Wi-Fi Camera" },
        };

        /// <summary>
        /// 아이템 이름 정규화 (Wiki 별칭 → 실제 아이템 테이블 이름)
        /// </summary>
        private static string NormalizeItemName(string? itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return itemName ?? string.Empty;

            // 별칭 매핑 확인
            if (ItemNameAliases.TryGetValue(itemName.Trim(), out var normalizedName))
                return normalizedName;

            return itemName.Trim();
        }

        /// <summary>
        /// Wiki 마크업에서 순수 텍스트 추출
        /// </summary>
        private static string CleanWikiMarkup(string text)
        {
            // [[Link|Display]] -> Display, [[Link]] -> Link
            text = Regex.Replace(text, @"\[\[([^\]|]+)\|([^\]]+)\]\]", "$2");
            text = Regex.Replace(text, @"\[\[([^\]]+)\]\]", "$1");
            // HTML 태그 제거
            text = Regex.Replace(text, @"<[^>]+>", "");
            // 여러 공백을 하나로
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        /// <summary>
        /// Wiki 링크에서 페이지 이름 추출
        /// </summary>
        private static string ExtractWikiLinkName(string text)
        {
            // [[로 시작하면 제거
            if (text.StartsWith("[["))
                text = text.Substring(2);
            // ]]로 끝나면 제거
            if (text.EndsWith("]]"))
                text = text.Substring(0, text.Length - 2);
            // | 이후 제거
            var pipeIndex = text.IndexOf('|');
            if (pipeIndex > 0)
                text = text.Substring(0, pipeIndex);
            return text.Trim();
        }

        /// <summary>
        /// Kill objective에서 추가 조건 추출
        /// </summary>
        private static void ExtractKillConditions(string line, ParsedObjective obj)
        {
            var conditions = new List<string>();

            // 거리 조건: from less than X meters
            var distanceMatch = Regex.Match(line, @"(?:from\s+)?(?:less\s+than\s+)?(\d+)\s*meters?", RegexOptions.IgnoreCase);
            if (distanceMatch.Success)
                conditions.Add($"distance<={distanceMatch.Groups[1].Value}m");

            // 장비 조건: while wearing, while using
            if (Regex.IsMatch(line, @"while\s+wearing", RegexOptions.IgnoreCase))
            {
                var gearMatch = Regex.Match(line, @"while\s+wearing\s+(?:a\s+)?(?:\[\[([^\]|]+)|([^\[\]\n,]+))", RegexOptions.IgnoreCase);
                if (gearMatch.Success)
                {
                    var gear = !string.IsNullOrEmpty(gearMatch.Groups[1].Value) ? gearMatch.Groups[1].Value : gearMatch.Groups[2].Value;
                    conditions.Add($"wearing:{gear.Trim()}");
                }
            }

            // 무기 조건: while using, using
            var weaponMatch = Regex.Match(line, @"(?:while\s+)?using\s+(?:\[\[)?([^\]\s,]+)", RegexOptions.IgnoreCase);
            if (weaponMatch.Success)
                conditions.Add($"weapon:{ExtractWikiLinkName(weaponMatch.Groups[1].Value)}");

            // 시간 조건: during nighttime, at night
            if (Regex.IsMatch(line, @"(?:during\s+)?(?:night(?:time)?|at\s+night)", RegexOptions.IgnoreCase))
                conditions.Add("time:night");
            if (Regex.IsMatch(line, @"(?:during\s+)?(?:day(?:time)?|at\s+day)", RegexOptions.IgnoreCase))
                conditions.Add("time:day");

            // 부위 조건: headshot
            if (Regex.IsMatch(line, @"headshot", RegexOptions.IgnoreCase))
                conditions.Add("headshot");

            // 맵 제외 조건: Excluding [Map]
            var excludeMatch = Regex.Match(line, @"Excluding\s+(?:\[\[)?([^\]\)]+)", RegexOptions.IgnoreCase);
            if (excludeMatch.Success)
                conditions.Add($"exclude:{ExtractWikiLinkName(excludeMatch.Groups[1].Value)}");

            if (conditions.Count > 0)
                obj.Conditions = string.Join(";", conditions);
        }

        /// <summary>
        /// 라인에서 위치 정보 추출
        /// </summary>
        private static void ExtractLocationFromLine(string line, ParsedObjective obj)
        {
            // 맵 이름은 이미 ParseObjectiveLine에서 추출됨
            // 구체적 위치 설명 추출 시도

            // "at/in/on the [location]" 패턴
            var locMatch = Regex.Match(line, @"(?:at|in|on)\s+the\s+([^,\.\[\]]+?)(?:\s+(?:at|in|on|$))", RegexOptions.IgnoreCase);
            if (locMatch.Success)
            {
                var loc = locMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(loc) && loc.Length > 3)
                    obj.LocationName = loc;
            }

            // "near [location]" 패턴
            if (string.IsNullOrEmpty(obj.LocationName))
            {
                locMatch = Regex.Match(line, @"near\s+(?:the\s+)?([^,\.\[\]]+?)(?:\s+on|\s*$)", RegexOptions.IgnoreCase);
                if (locMatch.Success)
                    obj.LocationName = locMatch.Groups[1].Value.Trim();
            }
        }

        /// <summary>
        /// PageContent에서 ==Requirements== 섹션의 MinScavKarma 추출
        /// 패턴:
        /// - [[Scavs#Scav karma|Scav karma]] of at least {+N}
        /// - [[Scavs#Scav karma|Scav karma]] of {-N} 또는 {+N}
        /// </summary>
        public static int? ExtractMinScavKarma(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            // ==Requirements== 또는 == Requirements== 섹션 찾기
            var reqMatch = Regex.Match(content, @"==\s*Requirements\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!reqMatch.Success)
                return null;

            var requirementsSection = reqMatch.Groups[1].Value;

            // [[Scavs#Scav karma|Scav karma]] of at least +{N}
            var karmaMatch = Regex.Match(requirementsSection, @"\[\[Scavs#Scav karma\|Scav karma\]\]\s*of at least\s*\+?(\d+)", RegexOptions.IgnoreCase);
            if (karmaMatch.Success && int.TryParse(karmaMatch.Groups[1].Value, out var karma))
            {
                return karma;
            }

            // [[Scavs#Scav karma|Scav karma]] of {-N} 또는 {+N} (음수/양수)
            karmaMatch = Regex.Match(requirementsSection, @"\[\[Scavs#Scav karma\|Scav karma\]\]\s*of\s*([+-]?\d+)", RegexOptions.IgnoreCase);
            if (karmaMatch.Success && int.TryParse(karmaMatch.Groups[1].Value, out karma))
            {
                return karma;
            }

            return null;
        }

        /// <summary>
        /// PageContent에서 ==Requirements== 섹션의 Faction 정보 추출
        /// 패턴: "This quest is only obtainable by [[BEAR]] PMCs" 또는 "This quest is only obtainable by [[USEC]] PMCs"
        /// Wiki 원문은 [[BEAR]] 형태로 링크되어 있음
        /// </summary>
        public static string? ExtractFaction(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            // ==Requirements== 섹션 찾기
            var reqMatch = Regex.Match(content, @"==\s*Requirements\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!reqMatch.Success)
                return null;

            var requirementsSection = reqMatch.Groups[1].Value;

            // "This quest is only obtainable by [[BEAR]] PMCs" - Wiki 링크 형태
            // \[\[BEAR\]\] 또는 plain BEAR 둘 다 매칭
            if (Regex.IsMatch(requirementsSection, @"only\s+obtainable\s+by\s+(?:\[\[)?BEAR(?:\]\])?\s+PMCs?", RegexOptions.IgnoreCase))
            {
                return "Bear";
            }

            // "This quest is only obtainable by [[USEC]] PMCs" - Wiki 링크 형태
            if (Regex.IsMatch(requirementsSection, @"only\s+obtainable\s+by\s+(?:\[\[)?USEC(?:\]\])?\s+PMCs?", RegexOptions.IgnoreCase))
            {
                return "Usec";
            }

            return null;
        }

        /// <summary>
        /// PageContent에서 ==Requirements== 섹션의 게임 에디션 요구사항 추출 (긍정적 요구사항)
        /// 패턴: "This quest is only available to buyers of the "Edge of Darkness" edition of the game."
        /// 패턴: "This quest is only available to owners of the "Unheard Edition" of the game."
        /// </summary>
        public static string? ExtractRequiredEdition(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            // ==Requirements== 섹션 찾기
            var reqMatch = Regex.Match(content, @"==\s*Requirements\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!reqMatch.Success)
                return null;

            var requirementsSection = reqMatch.Groups[1].Value;

            // "only available to" 패턴만 매칭 (긍정적 요구사항)
            // "Edge of Darkness" 에디션
            if (Regex.IsMatch(requirementsSection, @"only\s+available\s+to\s+(?:buyers|owners)\s+of\s+(?:the\s+)?[""']?Edge of Darkness[""']?", RegexOptions.IgnoreCase))
            {
                return "EOD";
            }

            // "Unheard Edition" (긍정적 요구사항 - 이 에디션만 가능)
            if (Regex.IsMatch(requirementsSection, @"only\s+available\s+to\s+(?:buyers|owners)\s+of\s+(?:the\s+)?[""']?(?:The\s+)?Unheard(?:\s+Edition)?[""']?", RegexOptions.IgnoreCase))
            {
                return "Unheard";
            }

            return null;
        }

        /// <summary>
        /// PageContent에서 게임 에디션 제외 조건 추출 (부정적 요구사항)
        /// Requirements 또는 Notes 섹션에서 검색
        /// 패턴: "This quest is not available to owners of the 'The Unheard' edition of the game."
        /// </summary>
        public static string? ExtractExcludedEdition(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            // ==Requirements== 섹션과 ==Notes== 섹션 모두 검색
            var sectionsToSearch = new List<string>();

            // Requirements 섹션
            var reqMatch = Regex.Match(content, @"==\s*Requirements\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (reqMatch.Success)
                sectionsToSearch.Add(reqMatch.Groups[1].Value);

            // Notes 섹션 (에디션 제외 정보가 여기 있는 경우가 많음)
            var notesMatch = Regex.Match(content, @"==\s*Notes\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (notesMatch.Success)
                sectionsToSearch.Add(notesMatch.Groups[1].Value);

            foreach (var section in sectionsToSearch)
            {
                // "not available to" 패턴 매칭 (부정적 요구사항 - 이 에디션은 불가)
                // 패턴: "This quest is not available to owners of the 'The Unheard' edition of the game."
                if (Regex.IsMatch(section, @"not\s+available\s+to\s+(?:buyers|owners)\s+of\s+(?:the\s+)?[""']?(?:The\s+)?Unheard[""']?(?:\s+edition)?", RegexOptions.IgnoreCase))
                {
                    return "Unheard";
                }

                // EOD 제외 (혹시 있을 경우 대비)
                if (Regex.IsMatch(section, @"not\s+available\s+to\s+(?:buyers|owners)\s+of\s+(?:the\s+)?[""']?Edge of Darkness[""']?", RegexOptions.IgnoreCase))
                {
                    return "EOD";
                }
            }

            return null;
        }

        /// <summary>
        /// PageContent에서 ==Requirements== 섹션의 DSP 라디오 해독 필요 횟수 추출
        /// 패턴: "Getting the Digital secure DSP radio transmitter decoded the first time."
        /// 패턴: "Getting the Digital secure DSP radio transmitter decoded the second time."
        /// 패턴: "Getting the Digital secure DSP radio transmitter decoded the third time."
        /// </summary>
        public static int? ExtractRequiredDecodeCount(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            // ==Requirements== 섹션 찾기
            var reqMatch = Regex.Match(content, @"==\s*Requirements\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!reqMatch.Success)
                return null;

            var requirementsSection = reqMatch.Groups[1].Value;

            // "DSP radio transmitter decoded the first/second/third time" 패턴 매칭
            // Wiki 형식: [[Digital secure DSP radio transmitter]] <font color="red">decoded</font> the first time
            // <font> 태그와 다양한 공백/줄바꿈을 고려한 패턴
            var decodeMatch = Regex.Match(requirementsSection,
                @"(?:\[\[)?Digital\s+secure\s+DSP\s+radio\s+transmitter(?:\]\])?\s*(?:<[^>]*>)?\s*decoded\s*(?:</[^>]*>)?\s+(?:the\s+)?(first|second|third|fourth|fifth|\d+(?:st|nd|rd|th)?)\s+time",
                RegexOptions.IgnoreCase);

            if (!decodeMatch.Success)
                return null;

            var ordinal = decodeMatch.Groups[1].Value.ToLowerInvariant();
            return ordinal switch
            {
                "first" or "1st" => 1,
                "second" or "2nd" => 2,
                "third" or "3rd" => 3,
                "fourth" or "4th" => 4,
                "fifth" or "5th" => 5,
                _ when int.TryParse(Regex.Match(ordinal, @"\d+").Value, out var num) => num,
                _ => null
            };
        }

        /// <summary>
        /// PageContent에서 ==Requirements== 섹션의 Prestige 레벨 요구사항 추출
        /// 패턴: "Must have [[Prestige]] level 1" 또는 "[[Prestige]] level 2" 등
        /// </summary>
        public static int? ExtractRequiredPrestigeLevel(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            // ==Requirements== 섹션 찾기
            var reqMatch = Regex.Match(content, @"==\s*Requirements\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!reqMatch.Success)
                return null;

            var requirementsSection = reqMatch.Groups[1].Value;

            // "[[Prestige]] level N" 또는 "Prestige level N" 패턴 매칭
            // Wiki 형식: Must have [[Prestige]] level 1
            var prestigeMatch = Regex.Match(requirementsSection,
                @"(?:\[\[)?Prestige(?:\]\])?\s+level\s+(\d+)",
                RegexOptions.IgnoreCase);

            if (!prestigeMatch.Success)
                return null;

            if (int.TryParse(prestigeMatch.Groups[1].Value, out var level))
                return level;

            return null;
        }

        /// <summary>
        /// PageContent에서 |related 필드를 파싱하여 대체 퀘스트(Other Choices) 목록 반환
        /// 주의: |related2, |related3 등은 다른 용도이므로 |related만 정확히 매칭해야 함
        /// </summary>
        public static List<string> ExtractRelatedQuests(string content)
        {
            var relatedQuests = new List<string>();
            if (string.IsNullOrEmpty(content))
                return relatedQuests;

            // |related = 만 매칭 (|related2, |related3 등은 제외)
            // (?!\d) - 숫자가 뒤따르지 않는 경우만 매칭 (negative lookahead)
            // 다음 필드(|로 시작하는 줄) 또는 템플릿 종료(}})까지의 내용을 가져옴
            // [ \t]* - 줄바꿈 제외한 공백만 매칭 (= 뒤의 줄바꿈을 남겨둠)
            var relatedMatch = Regex.Match(content, @"\|related(?!\d)[ \t]*=[ \t]*(.*?)(?=\n\||\n[ \t]*\}\}|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!relatedMatch.Success)
                return relatedQuests;

            var relatedValue = relatedMatch.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(relatedValue))
                return relatedQuests;

            // [[Quest Name]] 패턴으로 퀘스트 이름 추출
            var linkMatches = Regex.Matches(relatedValue, @"\[\[([^\]\|#]+)(?:#[^\]\|]*)?(?:\|[^\]]+)?\]\]");
            foreach (Match match in linkMatches)
            {
                var questName = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(questName))
                {
                    relatedQuests.Add(questName);
                }
            }

            return relatedQuests;
        }

        /// <summary>
        /// PageContent에서 previous 필드를 파싱하여 선행 퀘스트 조건 목록 반환
        /// </summary>
        public static List<ParsedQuestRequirement> ExtractPreviousQuests(string content)
        {
            var requirements = new List<ParsedQuestRequirement>();
            if (string.IsNullOrEmpty(content))
                return requirements;

            // |previous = 필드 추출 (다음 |필드명 또는 }} 또는 줄바꿈+| 까지)
            // 중요: = 뒤의 공백에서 줄바꿈을 제외해야 함 ([ \t]* 사용)
            // \s*는 줄바꿈을 포함하므로 빈 필드 다음의 |leads to = 등이 잘못 캡처될 수 있음
            var previousMatch = Regex.Match(content, @"\|previous[ \t]*=[ \t]*(.*?)(?=\r?\n[ \t]*\|[a-z]|\r?\n[ \t]*\}\}|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!previousMatch.Success)
                return requirements;

            var previousValue = previousMatch.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(previousValue))
                return requirements;

            // HTML 엔티티 디코딩
            previousValue = System.Net.WebUtility.HtmlDecode(previousValue);

            // "See requirements" 패턴 체크 - Requirements 섹션에서 파싱해야 하는 경우
            // 예: [[Network Provider - Part 1#Requirements|See requirements]]
            // 예: [[Collector#Requirements|See requirements]]
            if (Regex.IsMatch(previousValue, @"\[\[[^\]]+#Requirements\|See requirements\]\]", RegexOptions.IgnoreCase))
            {
                return ExtractPreviousQuestsFromRequirementsSection(content);
            }

            // OR 그룹 분리 (or<br/>or 또는 <br/>or<br/> 패턴)
            // 먼저 전체를 <br/>, <br>, </br>, <br /> 로 분리
            var parts = Regex.Split(previousValue, @"<br\s*/?>|</br>", RegexOptions.IgnoreCase);

            int groupId = 0;
            bool nextIsOr = false;

            foreach (var part in parts)
            {
                var trimmedPart = part.Trim();
                if (string.IsNullOrEmpty(trimmedPart))
                    continue;

                // "or" 키워드 체크
                if (trimmedPart.Equals("or", StringComparison.OrdinalIgnoreCase))
                {
                    nextIsOr = true;
                    continue;
                }

                // 현재 파트에서 퀘스트 추출
                var questsInPart = ExtractQuestsFromPart(trimmedPart);

                foreach (var quest in questsInPart)
                {
                    if (nextIsOr)
                    {
                        // 이전 항목과 같은 그룹 (OR 관계)
                        quest.GroupId = groupId;
                        nextIsOr = false;
                    }
                    else
                    {
                        // 새 그룹 시작 (AND 관계)
                        groupId++;
                        quest.GroupId = groupId;
                    }
                    requirements.Add(quest);
                }
            }

            return requirements;
        }

        /// <summary>
        /// ==Requirements== 섹션에서 선행 퀘스트 목록 파싱
        /// Network Provider - Part 1 등 |previous = 가 "See requirements"로 링크된 경우 사용
        /// </summary>
        private static List<ParsedQuestRequirement> ExtractPreviousQuestsFromRequirementsSection(string content)
        {
            var requirements = new List<ParsedQuestRequirement>();

            // ==Requirements== 섹션 추출
            var reqSectionMatch = Regex.Match(content, @"==\s*Requirements\s*==\s*(.*?)(?===|\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (!reqSectionMatch.Success)
                return requirements;

            var reqSection = reqSectionMatch.Groups[1].Value;

            // ** [[Quest Name]] 형태의 bullet point 추출 (중첩된 불릿 포인트)
            // 각 라인에서 ** 로 시작하는 것들만 추출
            var lines = reqSection.Split('\n');
            int groupId = 0;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // ** 로 시작하는 불릿 포인트만 처리 (중첩 레벨 2)
                if (!trimmedLine.StartsWith("**") || trimmedLine.StartsWith("***"))
                    continue;

                // ** 제거
                var questLine = trimmedLine.Substring(2).Trim();
                if (string.IsNullOrEmpty(questLine))
                    continue;

                // 인라인 OR 조건 처리: [[A]] or [[B]] or [[C]]
                // " or " 로 분리
                var orParts = Regex.Split(questLine, @"\s+or\s+", RegexOptions.IgnoreCase);

                if (orParts.Length > 1)
                {
                    // OR 그룹 - 모두 같은 GroupId
                    groupId++;
                    foreach (var orPart in orParts)
                    {
                        var quests = ExtractQuestsFromPart(orPart.Trim());
                        foreach (var quest in quests)
                        {
                            quest.GroupId = groupId;
                            requirements.Add(quest);
                        }
                    }
                }
                else
                {
                    // 단일 퀘스트 - 새 그룹
                    var quests = ExtractQuestsFromPart(questLine);
                    foreach (var quest in quests)
                    {
                        groupId++;
                        quest.GroupId = groupId;
                        requirements.Add(quest);
                    }
                }
            }

            return requirements;
        }

        private static List<ParsedQuestRequirement> ExtractQuestsFromPart(string part)
        {
            var results = new List<ParsedQuestRequirement>();

            // RequirementType 판별
            string requirementType = "Complete";
            var typeMatch = Regex.Match(part, @"^(Accept|Fail)\s+", RegexOptions.IgnoreCase);
            if (typeMatch.Success)
            {
                requirementType = typeMatch.Groups[1].Value;
                requirementType = char.ToUpper(requirementType[0]) + requirementType.Substring(1).ToLower();
                part = part.Substring(typeMatch.Length);
            }

            // 시간 지연 추출 (+Xhr, +X-Yhr, +Xmin 등)
            int? delayMinutes = null;
            var delayMatch = Regex.Match(part, @"\(\+(\d+)(?:-\d+)?hr\)", RegexOptions.IgnoreCase);
            if (delayMatch.Success)
            {
                delayMinutes = int.Parse(delayMatch.Groups[1].Value) * 60;
                part = Regex.Replace(part, @"\s*\(\+\d+(?:-\d+)?hr\)", "", RegexOptions.IgnoreCase);
            }
            else
            {
                var minMatch = Regex.Match(part, @"\(\+(\d+)min\)", RegexOptions.IgnoreCase);
                if (minMatch.Success)
                {
                    delayMinutes = int.Parse(minMatch.Groups[1].Value);
                    part = Regex.Replace(part, @"\s*\(\+\d+min\)", "", RegexOptions.IgnoreCase);
                }
            }

            // [[Quest Name]], [[Quest Name#Section]], [[Page Name|Display Name]] 패턴 추출
            // 예: [[Immunity]], [[Immunity#section]], [[Immunity (quest)|Immunity]]
            var linkMatches = Regex.Matches(part, @"\[\[([^\]\|#]+)(?:#[^\]\|]*)?(?:\|[^\]]+)?\]\]");
            foreach (Match match in linkMatches)
            {
                var questName = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(questName))
                {
                    results.Add(new ParsedQuestRequirement
                    {
                        QuestName = questName,
                        RequirementType = requirementType,
                        DelayMinutes = delayMinutes
                    });
                }
            }

            // 링크가 없는 경우도 처리 (예: 불완전한 위키 마크업)
            if (results.Count == 0 && !string.IsNullOrWhiteSpace(part))
            {
                // [[ 로 시작하지만 닫히지 않은 경우
                var incompleteMatch = Regex.Match(part, @"\[\[([^\]#]+)");
                if (incompleteMatch.Success)
                {
                    var questName = incompleteMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(questName))
                    {
                        results.Add(new ParsedQuestRequirement
                        {
                            QuestName = questName,
                            RequirementType = requirementType,
                            DelayMinutes = delayMinutes
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 캐시에서 퀘스트 정보 가져오기 (Trader 포함)
        /// </summary>
        public Dictionary<string, CachedQuestInfo> GetCachedQuests()
        {
            return _questCache;
        }

        #endregion

        #region tarkov.dev API

        /// <summary>
        /// tarkov.dev에서 퀘스트 다국어 정보 가져오기
        /// </summary>
        public async Task<Dictionary<string, TarkovDevQuest>> FetchTarkovDevQuestsAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke("Fetching quests from tarkov.dev API...");

            var query = @"
            {
                tasks(lang: en) {
                    id
                    tarkovDataId
                    name
                    normalizedName
                    wikiLink
                    trader { name }
                }
                ko: tasks(lang: ko) { id name }
                ja: tasks(lang: ja) { id name }
            }";

            var requestBody = JsonSerializer.Serialize(new { query });
            var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(TarkovDevApiUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var result = new Dictionary<string, TarkovDevQuest>(StringComparer.OrdinalIgnoreCase);

            if (!doc.RootElement.TryGetProperty("data", out var data))
                return result;

            // 한국어, 일본어 맵 생성
            var koNames = new Dictionary<string, string>();
            var jaNames = new Dictionary<string, string>();

            if (data.TryGetProperty("ko", out var koTasks))
            {
                foreach (var task in koTasks.EnumerateArray())
                {
                    var id = task.GetProperty("id").GetString();
                    var name = task.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        koNames[id] = name;
                }
            }

            if (data.TryGetProperty("ja", out var jaTasks))
            {
                foreach (var task in jaTasks.EnumerateArray())
                {
                    var id = task.GetProperty("id").GetString();
                    var name = task.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        jaNames[id] = name;
                }
            }

            // 영어 기준으로 병합
            if (data.TryGetProperty("tasks", out var tasks))
            {
                foreach (var task in tasks.EnumerateArray())
                {
                    var id = task.GetProperty("id").GetString();
                    var name = task.GetProperty("name").GetString();
                    var normalizedName = task.TryGetProperty("normalizedName", out var nn) ? nn.GetString() : null;
                    var wikiLink = task.TryGetProperty("wikiLink", out var wl) ? wl.GetString() : null;
                    var tarkovDataId = task.TryGetProperty("tarkovDataId", out var tdid) && tdid.ValueKind == JsonValueKind.Number ? tdid.GetInt32() : (int?)null;
                    var trader = task.TryGetProperty("trader", out var tr) && tr.ValueKind == JsonValueKind.Object && tr.TryGetProperty("name", out var tn) ? tn.GetString() : null;

                    if (string.IsNullOrEmpty(id))
                        continue;

                    var quest = new TarkovDevQuest
                    {
                        Id = id,
                        TarkovDataId = tarkovDataId,
                        NameEN = name ?? "",
                        NormalizedName = normalizedName,
                        NameKO = koNames.TryGetValue(id, out var ko) ? ko : name ?? "",
                        NameJA = jaNames.TryGetValue(id, out var ja) ? ja : name ?? "",
                        Trader = trader,
                        WikiLink = wikiLink
                    };

                    // wikiLink가 있으면 키로 사용
                    if (!string.IsNullOrEmpty(wikiLink))
                    {
                        result[wikiLink] = quest;
                    }
                }
            }

            progress?.Invoke($"Fetched {result.Count} quests from tarkov.dev");
            WriteLog($"tarkov.dev quests: {result.Count}");

            return result;
        }

        private string NormalizeWikiLink(string wikiLink)
        {
            if (string.IsNullOrEmpty(wikiLink))
                return "";

            // URL 디코딩
            try
            {
                wikiLink = Uri.UnescapeDataString(wikiLink);
            }
            catch { }

            // 위키 페이지 이름만 추출
            var prefix = "https://escapefromtarkov.fandom.com/wiki/";
            if (wikiLink.StartsWith(prefix))
            {
                wikiLink = wikiLink.Substring(prefix.Length);
            }

            return wikiLink.Replace("_", " ");
        }

        /// <summary>
        /// Wiki 퀘스트 이름을 tarkov.dev normalizedName 형식으로 변환
        /// (소문자, 공백->하이픈, 특수문자 제거)
        /// </summary>
        private string NormalizeQuestName(string questName)
        {
            // 소문자로 변환
            var normalized = questName.ToLowerInvariant();

            // "(quest)" 접미사 제거 (Wiki에서 동명이인 구분용)
            if (normalized.EndsWith(" (quest)"))
            {
                normalized = normalized.Substring(0, normalized.Length - 8);
            }

            // 공백을 하이픈으로
            normalized = normalized.Replace(" ", "-");

            // 특수문자 제거 (알파벳, 숫자, 하이픈만 유지)
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9\-]", "");

            // 연속 하이픈 단일화
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"-+", "-");

            // 앞뒤 하이픈 제거
            normalized = normalized.Trim('-');

            return normalized;
        }

        #endregion

        #region 내보내기

        /// <summary>
        /// 퀘스트 데이터를 JSON으로 내보내기
        /// </summary>
        public async Task<QuestExportResult> ExportQuestsAsync(
            string outputPath,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new QuestExportResult();

            // tarkov.dev 데이터 가져오기
            var devQuests = await FetchTarkovDevQuestsAsync(progress, cancellationToken);

            // tarkov.dev 데이터 저장 (디버깅용)
            var devQuestsPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "tarkov_dev_quests.json");
            var devQuestsList = devQuests.Values.Select(q => new
            {
                q.Id,
                q.NameEN,
                q.NameKO,
                q.NameJA,
                q.WikiLink,
                q.Trader
            }).OrderBy(q => q.NameEN).ToList();
            var devJson = JsonSerializer.Serialize(devQuestsList, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(devQuestsPath, devJson, cancellationToken);
            progress?.Invoke($"Saved {devQuestsList.Count} tarkov.dev quests to tarkov_dev_quests.json");

            progress?.Invoke("Matching quests with tarkov.dev data...");

            var enrichedQuests = new List<EnrichedQuest>();
            var missingQuests = new List<MissingQuest>();

            // tarkov.dev wikiLink를 키로 하는 역인덱스 생성
            var devQuestsByWikiLink = new Dictionary<string, TarkovDevQuest>(StringComparer.OrdinalIgnoreCase);
            // normalizedName을 키로 하는 역인덱스 생성 (fallback용)
            var devQuestsByNormalizedName = new Dictionary<string, TarkovDevQuest>(StringComparer.OrdinalIgnoreCase);
            foreach (var dq in devQuests.Values)
            {
                if (!string.IsNullOrEmpty(dq.WikiLink))
                {
                    devQuestsByWikiLink[dq.WikiLink] = dq;
                }
                if (!string.IsNullOrEmpty(dq.NormalizedName))
                {
                    devQuestsByNormalizedName[dq.NormalizedName] = dq;
                }
            }

            foreach (var kvp in _questCache)
            {
                var questName = kvp.Key;
                var cached = kvp.Value;

                // tarkov.dev와 동일한 형식으로 wikiLink 생성
                // 공백 -> 언더스코어, 특수문자는 URL 인코딩 (단, 괄호는 인코딩하지 않음 - 별도 처리)
                var encodedName = Uri.EscapeDataString(questName.Replace(" ", "_"))
                    .Replace("%28", "(").Replace("%29", ")");  // 괄호는 인코딩 안함
                var wikiPageLink = $"https://escapefromtarkov.fandom.com/wiki/{encodedName}";
                var id = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(wikiPageLink));

                var enriched = new EnrichedQuest
                {
                    Id = id,
                    Name = questName,
                    WikiPageLink = wikiPageLink
                };

                // tarkov.dev 매칭 시도 (wikiLink로 매칭)
                TarkovDevQuest? devQuest = null;
                string matchMethod = "";

                if (devQuestsByWikiLink.TryGetValue(wikiPageLink, out devQuest))
                {
                    matchMethod = "wikiLink";
                }
                else
                {
                    // Fallback: normalizedName으로 매칭 시도
                    // Wiki 퀘스트 이름에서 normalizedName 생성 (소문자, 공백->하이픈, 특수문자 제거)
                    var wikiNormalized = NormalizeQuestName(questName);
                    if (devQuestsByNormalizedName.TryGetValue(wikiNormalized, out devQuest))
                    {
                        matchMethod = "normalizedName";
                    }
                }

                if (devQuest != null)
                {
                    enriched.BsgId = devQuest.Id;
                    enriched.NameEN = devQuest.NameEN;
                    enriched.NameKO = devQuest.NameKO;
                    enriched.NameJA = devQuest.NameJA;
                    result.MatchedCount++;
                    if (matchMethod == "normalizedName")
                    {
                        WriteLog($"Matched by normalizedName: '{questName}' -> '{devQuest.NameEN}' (id={devQuest.Id})");
                    }
                }
                else
                {
                    // 매칭 실패 - Name을 다국어로 사용
                    enriched.NameEN = questName;
                    enriched.NameKO = questName;
                    enriched.NameJA = questName;

                    missingQuests.Add(new MissingQuest
                    {
                        WikiName = questName
                    });
                    result.MissingCount++;
                }

                enrichedQuests.Add(enriched);
            }

            result.TotalCount = enrichedQuests.Count;

            // JSON 저장
            var questList = new EnrichedQuestList
            {
                ExportedAt = DateTime.UtcNow,
                TotalQuests = result.TotalCount,
                MatchedQuests = result.MatchedCount,
                MissingQuests = result.MissingCount,
                Quests = enrichedQuests.OrderBy(q => q.Name).ToList()
            };

            var json = JsonSerializer.Serialize(questList, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputPath, json, cancellationToken);

            // Missing 퀘스트 저장
            if (missingQuests.Count > 0)
            {
                var missingPath = Path.Combine(Path.GetDirectoryName(outputPath)!, "quest_missing.json");
                var missingJson = JsonSerializer.Serialize(missingQuests, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(missingPath, missingJson, cancellationToken);
            }

            progress?.Invoke($"Exported {result.TotalCount} quests ({result.MatchedCount} matched, {result.MissingCount} missing)");
            WriteLog($"Export complete: {result.TotalCount} total, {result.MatchedCount} matched, {result.MissingCount} missing");

            return result;
        }

        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    #region Models

    public class QuestCacheFile
    {
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonPropertyName("quests")]
        public Dictionary<string, CachedQuestInfo> Quests { get; set; } = new();
    }

    public class CachedQuestInfo
    {
        [JsonPropertyName("questName")]
        public string QuestName { get; set; } = "";

        [JsonPropertyName("revisionId")]
        public long? RevisionId { get; set; }

        [JsonPropertyName("pageContent")]
        public string? PageContent { get; set; }

        [JsonPropertyName("trader")]
        public string? Trader { get; set; }

        [JsonPropertyName("minLevel")]
        public int? MinLevel { get; set; }

        [JsonPropertyName("minScavKarma")]
        public int? MinScavKarma { get; set; }

        [JsonPropertyName("faction")]
        public string? Faction { get; set; }

        [JsonPropertyName("requiredEdition")]
        public string? RequiredEdition { get; set; }

        [JsonPropertyName("excludedEdition")]
        public string? ExcludedEdition { get; set; }

        [JsonPropertyName("requiredDecodeCount")]
        public int? RequiredDecodeCount { get; set; }

        [JsonPropertyName("cachedAt")]
        public DateTime CachedAt { get; set; }

        [JsonPropertyName("contentFetchedAt")]
        public DateTime? ContentFetchedAt { get; set; }
    }

    public class QuestCacheUpdateResult
    {
        public int TotalQuests { get; set; }
        public int NewQuests { get; set; }
        public int Updated { get; set; }
        public int UpToDate { get; set; }
        public int Failed { get; set; }
    }

    public class TarkovDevQuest
    {
        public string Id { get; set; } = "";
        public int? TarkovDataId { get; set; }
        public string NameEN { get; set; } = "";
        public string? NormalizedName { get; set; }
        public string NameKO { get; set; } = "";
        public string NameJA { get; set; } = "";
        public string? Trader { get; set; }
        public string? WikiLink { get; set; }
    }

    public class EnrichedQuest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("bsgId")]
        public string? BsgId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("nameEN")]
        public string? NameEN { get; set; }

        [JsonPropertyName("nameKO")]
        public string? NameKO { get; set; }

        [JsonPropertyName("nameJA")]
        public string? NameJA { get; set; }

        [JsonPropertyName("wikiPageLink")]
        public string WikiPageLink { get; set; } = "";
    }

    public class EnrichedQuestList
    {
        [JsonPropertyName("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonPropertyName("totalQuests")]
        public int TotalQuests { get; set; }

        [JsonPropertyName("matchedQuests")]
        public int MatchedQuests { get; set; }

        [JsonPropertyName("missingQuests")]
        public int MissingQuests { get; set; }

        [JsonPropertyName("quests")]
        public List<EnrichedQuest> Quests { get; set; } = new();
    }

    public class MissingQuest
    {
        [JsonPropertyName("wikiName")]
        public string WikiName { get; set; } = "";
    }

    public class QuestExportResult
    {
        public int TotalCount { get; set; }
        public int MatchedCount { get; set; }
        public int MissingCount { get; set; }
    }

    /// <summary>
    /// 파싱된 퀘스트 선행 조건 (Wiki 퀘스트 이름 기준)
    /// </summary>
    public class ParsedQuestRequirement
    {
        public string QuestName { get; set; } = "";
        public string RequirementType { get; set; } = "Complete"; // Complete, Accept, Fail
        public int? DelayMinutes { get; set; }
        public int GroupId { get; set; } // OR 그룹 ID
    }

    /// <summary>
    /// Objective 타입 열거형
    /// </summary>
    public enum ObjectiveType
    {
        Kill,       // 적 처치
        Collect,    // 아이템 수집 (FIR 또는 일반)
        HandOver,   // 아이템 제출
        Visit,      // 위치 방문/탐색
        Mark,       // 마커 설치
        Stash,      // 아이템 숨기기/배치
        Survive,    // 생존 탈출
        Build,      // 하이드아웃 제작
        Task,       // 다른 퀘스트 완료
        Custom      // 기타 특수 조건
    }

    /// <summary>
    /// 파싱된 퀘스트 목표
    /// </summary>
    public class ParsedObjective
    {
        public int SortOrder { get; set; }
        public ObjectiveType Type { get; set; } = ObjectiveType.Custom;
        public string Description { get; set; } = "";

        // 타겟 정보
        public string? TargetType { get; set; }  // Scav, PMC, Boss, Item 등
        public int? TargetCount { get; set; }

        // 아이템 정보
        public string? ItemName { get; set; }    // Wiki 아이템 이름
        public bool RequiresFIR { get; set; }    // Found in Raid 필요 여부

        // 맵/위치 정보
        public string? MapName { get; set; }     // Customs, Factory, Shoreline 등
        public string? LocationName { get; set; } // 위치 설명 텍스트

        // 추가 조건
        public string? Conditions { get; set; }  // 추가 조건 (JSON 또는 텍스트)

        // 도그태그 관련 정보
        public int? DogtagMinLevel { get; set; }   // 도그태그 최소 레벨 (예: 15레벨 이상)
        public string? DogtagFaction { get; set; } // 도그태그 진영: "BEAR", "USEC", or null
    }

    /// <summary>
    /// 파싱된 퀘스트 필요 아이템 (Related Quest Items 테이블에서 추출)
    /// </summary>
    public class ParsedRequiredItem
    {
        public int SortOrder { get; set; }
        public string? ItemId { get; set; }             // Wiki {{itemId}} 템플릿의 24자 hex ID
        public string ItemName { get; set; } = "";      // Wiki 아이템 이름
        public int Count { get; set; } = 1;             // 필요 수량
        public bool RequiresFIR { get; set; }           // Found in Raid 필요 여부
        public string RequirementType { get; set; } = "Required";  // Handover, Required, Optional
        public int? DogtagMinLevel { get; set; }        // 도그태그 최소 레벨 (도그태그 아이템만)
        public string? DogtagFaction { get; set; }      // 도그태그 진영: "BEAR", "USEC", or null
    }

    #endregion
}
