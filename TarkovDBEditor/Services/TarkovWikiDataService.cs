using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace TarkovDBEditor.Services
{
    /// <summary>
    /// Wiki 데이터를 XML Export 방식으로 빠르게 가져오는 서비스
    /// </summary>
    public class TarkovWikiDataService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string SpecialExportUrl = "https://escapefromtarkov.fandom.com/wiki/Special:Export";
        private const string MediaWikiApiUrl = "https://escapefromtarkov.fandom.com/api.php";

        // 루트 카테고리 (Category:Inventory 기준)
        public const string RootCategory = "Inventory";

        // 제외할 카테고리 목록
        private static readonly string[] ExcludeCategories = new[]
        {
            "Event content",
            "Weapon camouflages",
            "Loot Containers"
        };

        public TarkovWikiDataService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TarkovDBEditor/1.0");
        }

        /// <summary>
        /// 제외할 카테고리의 모든 아이템을 가져옵니다
        /// </summary>
        public async Task<HashSet<string>> GetExcludedItemsAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var excludedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                                excludedItems.Add(title);
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

                progress?.Invoke($"Found {excludedItems.Count} items to exclude from {category}");
            }

            return excludedItems;
        }

        /// <summary>
        /// 카테고리 멤버 결과 (아이템과 서브카테고리 분리)
        /// </summary>
        public class CategoryMembersResult
        {
            public List<string> Items { get; set; } = new();
            public List<string> SubCategories { get; set; } = new();
        }

        /// <summary>
        /// 특정 카테고리의 멤버를 가져옵니다 (아이템과 서브카테고리 분리)
        /// ns: 0 = 일반 페이지 (아이템)
        /// ns: 14 = 카테고리 (서브카테고리)
        /// </summary>
        public async Task<CategoryMembersResult> GetCategoryMembersWithTypeAsync(
            string categoryName,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new CategoryMembersResult();
            string? continueToken = null;

            progress?.Invoke($"Fetching category members: {categoryName}...");

            do
            {
                var url = $"{MediaWikiApiUrl}?action=query&list=categorymembers&cmtitle=Category:{Uri.EscapeDataString(categoryName)}&cmlimit=500&format=json";
                if (!string.IsNullOrEmpty(continueToken))
                {
                    url += $"&cmcontinue={Uri.EscapeDataString(continueToken)}";
                }

                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("query", out var query) &&
                    query.TryGetProperty("categorymembers", out var categoryMembers))
                {
                    foreach (var member in categoryMembers.EnumerateArray())
                    {
                        var title = member.GetProperty("title").GetString();
                        var ns = member.GetProperty("ns").GetInt32();

                        if (string.IsNullOrEmpty(title))
                            continue;

                        if (ns == 0)
                        {
                            // 일반 페이지 (실제 아이템)
                            // 카테고리 설명 페이지 제외 (카테고리 이름과 동일한 페이지)
                            if (!title.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                            {
                                result.Items.Add(title);
                            }
                        }
                        else if (ns == 14)
                        {
                            // 카테고리 (서브카테고리) - "Category:" 접두사 제거
                            var subCatName = title.StartsWith("Category:")
                                ? title.Substring("Category:".Length)
                                : title;
                            result.SubCategories.Add(subCatName);
                        }
                    }
                }

                // Check for continuation
                continueToken = null;
                if (doc.RootElement.TryGetProperty("continue", out var cont) &&
                    cont.TryGetProperty("cmcontinue", out var cmcontinue))
                {
                    continueToken = cmcontinue.GetString();
                }

            } while (!string.IsNullOrEmpty(continueToken));

            progress?.Invoke($"Found {result.Items.Count} items, {result.SubCategories.Count} subcategories in {categoryName}");
            return result;
        }

        /// <summary>
        /// 특정 카테고리의 모든 아이템을 재귀적으로 가져옵니다 (서브카테고리 병렬 탐색, 트리 구조 추적)
        /// </summary>
        public async Task<(WikiCategoryData Data, Dictionary<string, WikiCategoryNode> TreeNodes, Dictionary<string, List<string>> DirectItemsCache)> GetCategoryItemsRecursiveAsync(
            string categoryName,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default,
            HashSet<string>? visitedCategories = null,
            string? parentCategory = null,
            int depth = 0)
        {
            visitedCategories ??= new HashSet<string>();
            var treeNodes = new Dictionary<string, WikiCategoryNode>();
            var directItemsCache = new Dictionary<string, List<string>>();

            // 순환 참조 방지
            lock (visitedCategories)
            {
                if (visitedCategories.Contains(categoryName))
                {
                    return (new WikiCategoryData
                    {
                        CategoryName = categoryName,
                        Items = new List<string>(),
                        DirectItems = new List<string>(),
                        SubCategories = new List<string>()
                    }, treeNodes, directItemsCache);
                }
                visitedCategories.Add(categoryName);
            }

            var result = new WikiCategoryData
            {
                CategoryName = categoryName,
                Items = new List<string>(),
                DirectItems = new List<string>(),
                SubCategories = new List<string>()
            };

            // 현재 카테고리의 멤버 가져오기
            var members = await GetCategoryMembersWithTypeAsync(categoryName, null, cancellationToken);

            // 직접 아이템 추가
            result.DirectItems.AddRange(members.Items);
            result.Items.AddRange(members.Items);
            result.SubCategories.AddRange(members.SubCategories);

            // 현재 카테고리의 DirectItems 캐시에 저장
            directItemsCache[categoryName] = new List<string>(members.Items);

            // 현재 카테고리 노드 생성
            var currentNode = new WikiCategoryNode
            {
                Name = categoryName,
                Parent = parentCategory,
                Children = new List<string>(members.SubCategories),
                DirectItemCount = members.Items.Count,
                Depth = depth
            };

            // 서브카테고리 병렬 탐색 (최대 5개 동시)
            if (members.SubCategories.Count > 0)
            {
                progress?.Invoke($"  -> {categoryName}: {members.Items.Count} items, {members.SubCategories.Count} subcategories...");

                var semaphore = new SemaphoreSlim(5);
                var tasks = members.SubCategories.Select(async subCat =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        return await GetCategoryItemsRecursiveAsync(
                            subCat, null, cancellationToken, visitedCategories, categoryName, depth + 1);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                var subResults = await Task.WhenAll(tasks);

                // 서브카테고리의 아이템, 트리 노드, DirectItems 캐시 병합
                foreach (var (subData, subTreeNodes, subDirectItemsCache) in subResults)
                {
                    result.Items.AddRange(subData.Items);

                    // 트리 노드 병합
                    foreach (var kvp in subTreeNodes)
                    {
                        treeNodes[kvp.Key] = kvp.Value;
                    }

                    // DirectItems 캐시 병합
                    foreach (var kvp in subDirectItemsCache)
                    {
                        directItemsCache[kvp.Key] = kvp.Value;
                    }
                }
            }

            // 중복 제거
            result.Items = result.Items.Distinct().ToList();
            result.ItemCount = result.Items.Count;
            result.DirectItemCount = result.DirectItems.Count;

            // 현재 노드의 총 아이템 수 업데이트
            currentNode.TotalItemCount = result.ItemCount;
            treeNodes[categoryName] = currentNode;

            return (result, treeNodes, directItemsCache);
        }

        /// <summary>
        /// 특정 카테고리의 모든 페이지 목록을 가져옵니다 (하위 호환성 유지)
        /// </summary>
        public async Task<List<string>> GetCategoryMembersAsync(
            string categoryName,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = await GetCategoryMembersWithTypeAsync(categoryName, progress, cancellationToken);
            return result.Items;
        }

        /// <summary>
        /// 여러 카테고리의 페이지 목록을 병렬로 가져옵니다
        /// </summary>
        public async Task<Dictionary<string, List<string>>> GetAllCategoryMembersAsync(
            IEnumerable<string> categories,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, List<string>>();
            var categoriesToFetch = categories.ToList();
            var total = categoriesToFetch.Count;
            var current = 0;

            foreach (var category in categoriesToFetch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current++;
                progress?.Invoke($"[{current}/{total}] Fetching {category}...");

                try
                {
                    var members = await GetCategoryMembersAsync(category, null, cancellationToken);
                    result[category] = members;

                    // Rate limiting
                    await Task.Delay(100, cancellationToken);
                }
                catch (Exception ex)
                {
                    progress?.Invoke($"Error fetching {category}: {ex.Message}");
                    result[category] = new List<string>();
                }
            }

            return result;
        }

        /// <summary>
        /// Special:Export를 사용하여 여러 페이지를 한번에 가져옵니다
        /// </summary>
        public async Task<Dictionary<string, string>> ExportPagesAsync(
            IEnumerable<string> pageNames,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>();
            var pageList = pageNames.ToList();

            if (pageList.Count == 0)
                return result;

            progress?.Invoke($"Exporting {pageList.Count} pages via Special:Export...");

            var postData = new Dictionary<string, string>
            {
                { "catname", "" },
                { "pages", string.Join("\n", pageList) },
                { "curonly", "1" },
                { "wpDownload", "1" }
            };

            var content = new FormUrlEncodedContent(postData);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(10));

            var request = new HttpRequestMessage(HttpMethod.Post, SpecialExportUrl) { Content = content };
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            progress?.Invoke($"Downloading XML ({(contentLength.HasValue ? $"{contentLength.Value / 1024}KB" : "unknown size")})...");

            string xmlContent;
            using (var stream = await response.Content.ReadAsStreamAsync(cts.Token))
            using (var memoryStream = new MemoryStream())
            {
                var buffer = new byte[81920];
                int bytesRead;
                long totalBytesRead = 0;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                {
                    await memoryStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                    totalBytesRead += bytesRead;
                }

                memoryStream.Position = 0;
                using var reader = new StreamReader(memoryStream, Encoding.UTF8);
                xmlContent = await reader.ReadToEndAsync();
            }

            progress?.Invoke($"Parsing XML ({xmlContent.Length / 1024}KB)...");
            result = ParseMediaWikiExportXml(xmlContent);

            progress?.Invoke($"Exported {result.Count} pages successfully");
            return result;
        }

        /// <summary>
        /// MediaWiki XML Export 형식을 파싱합니다
        /// </summary>
        private Dictionary<string, string> ParseMediaWikiExportXml(string xmlContent)
        {
            var result = new Dictionary<string, string>();

            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            var namespaces = new[]
            {
                "http://www.mediawiki.org/xml/export-0.11/",
                "http://www.mediawiki.org/xml/export-0.10/",
                "http://www.mediawiki.org/xml/export-0.9/"
            };

            XmlNodeList? pageNodes = null;
            XmlNamespaceManager? nsmgr = null;

            foreach (var ns in namespaces)
            {
                nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("mw", ns);
                pageNodes = doc.SelectNodes("//mw:page", nsmgr);
                if (pageNodes != null && pageNodes.Count > 0)
                    break;
            }

            if (pageNodes == null || pageNodes.Count == 0)
            {
                pageNodes = doc.SelectNodes("//page");
            }

            if (pageNodes == null)
                return result;

            foreach (XmlNode pageNode in pageNodes)
            {
                var titleNode = nsmgr != null
                    ? pageNode.SelectSingleNode("mw:title", nsmgr) ?? pageNode.SelectSingleNode("title")
                    : pageNode.SelectSingleNode("title");
                var textNode = nsmgr != null
                    ? pageNode.SelectSingleNode("mw:revision/mw:text", nsmgr) ?? pageNode.SelectSingleNode("revision/text")
                    : pageNode.SelectSingleNode("revision/text");

                if (titleNode != null && textNode != null)
                {
                    result[titleNode.InnerText] = textNode.InnerText;
                }
            }

            return result;
        }

        /// <summary>
        /// 모든 카테고리 데이터를 수집하고 통계를 생성합니다 (병렬 처리, 트리 구조 추적)
        /// Category:Inventory를 루트로 하여 모든 하위 카테고리를 탐색합니다.
        /// </summary>
        public async Task<(WikiItemExportResult Result, WikiCategoryTree Tree, Dictionary<string, List<string>> AllCategoryDirectItems)> ExportAllCategoryDataAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new WikiItemExportResult
            {
                ExportedAt = DateTime.UtcNow,
                Categories = new Dictionary<string, WikiCategoryData>()
            };

            progress?.Invoke($"Fetching root category: {RootCategory}...");

            // 1. Category:Inventory의 직접 서브카테고리 가져오기
            var rootMembers = await GetCategoryMembersWithTypeAsync(RootCategory, progress, cancellationToken);
            var rootSubCategories = rootMembers.SubCategories;

            progress?.Invoke($"Found {rootSubCategories.Count} root subcategories under {RootCategory}");

            var categoryTree = new WikiCategoryTree
            {
                ExportedAt = DateTime.UtcNow,
                RootCategory = RootCategory,
                RootCategories = rootSubCategories,
                Tree = new Dictionary<string, WikiCategoryNode>()
            };

            // 루트 카테고리 노드 추가
            categoryTree.Tree[RootCategory] = new WikiCategoryNode
            {
                Name = RootCategory,
                Parent = null,
                Children = rootSubCategories,
                DirectItemCount = rootMembers.Items.Count,
                Depth = 0
            };

            var categoriesToFetch = rootSubCategories;
            var total = categoriesToFetch.Count;
            var completed = 0;
            var lockObj = new object();

            // 전체 아이템 목록
            var allItems = new HashSet<string>();

            // 아이템 -> 해당 아이템이 직접 속한 카테고리들 (DirectItems 기준)
            var itemToDirectCategories = new Dictionary<string, HashSet<string>>();

            // 모든 카테고리의 DirectItems 캐시
            var allCategoryDirectItems = new Dictionary<string, List<string>>();

            // 루트 카테고리들을 병렬로 처리 (최대 3개 동시)
            var semaphore = new SemaphoreSlim(3);
            var tasks = categoriesToFetch.Select(async category =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var (categoryData, treeNodes, directItemsCache) = await GetCategoryItemsRecursiveAsync(
                        category, progress, cancellationToken);

                    lock (lockObj)
                    {
                        completed++;
                        progress?.Invoke($"[{completed}/{total}] {category}: {categoryData.ItemCount} items found");
                    }

                    return (category, categoryData, treeNodes, directItemsCache);
                }
                catch (Exception ex)
                {
                    lock (lockObj)
                    {
                        completed++;
                        progress?.Invoke($"[{completed}/{total}] Error fetching {category}: {ex.Message}");
                    }
                    return (category, new WikiCategoryData { CategoryName = category, Items = new List<string>() },
                        new Dictionary<string, WikiCategoryNode>(),
                        new Dictionary<string, List<string>>());
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);

            // 결과 병합 및 트리 구조 수집
            foreach (var (category, categoryData, treeNodes, directItemsCache) in results)
            {
                result.Categories[category] = categoryData;

                // 트리 노드 병합
                foreach (var kvp in treeNodes)
                {
                    categoryTree.Tree[kvp.Key] = kvp.Value;
                }

                // DirectItems 캐시 병합
                foreach (var kvp in directItemsCache)
                {
                    allCategoryDirectItems[kvp.Key] = kvp.Value;
                }

                // 전체 아이템 수집
                foreach (var item in categoryData.Items)
                {
                    allItems.Add(item);
                }
            }

            progress?.Invoke("Analyzing category relationships for duplicate detection...");

            // 모든 카테고리의 DirectItems에서 아이템->카테고리 매핑 수집
            foreach (var (catName, directItems) in allCategoryDirectItems)
            {
                foreach (var item in directItems)
                {
                    if (!itemToDirectCategories.ContainsKey(item))
                        itemToDirectCategories[item] = new HashSet<string>();
                    itemToDirectCategories[item].Add(catName);
                }
            }

            // 진짜 중복 계산: 같은 트리 내의 상위-하위 관계가 아닌 경우만
            var trueDuplicates = new Dictionary<string, List<string>>();

            foreach (var (item, directCategories) in itemToDirectCategories)
            {
                if (directCategories.Count <= 1)
                    continue;

                // 각 카테고리 쌍에 대해 상위-하위 관계인지 확인
                var independentCategories = GetIndependentCategories(directCategories.ToList(), categoryTree.Tree);

                if (independentCategories.Count > 1)
                {
                    trueDuplicates[item] = independentCategories;
                }
            }

            // 결과 집계
            result.TotalItemCount = allItems.Count;
            result.TotalCategoryCount = categoryTree.Tree.Count;
            result.DuplicateItemCount = trueDuplicates.Count;
            result.DuplicateItems = trueDuplicates;

            // 카테고리별 진짜 중복 수량 계산
            foreach (var (category, data) in result.Categories)
            {
                data.DuplicateCount = data.Items.Count(item =>
                    trueDuplicates.ContainsKey(item) && trueDuplicates[item].Contains(category));
            }

            progress?.Invoke($"Export complete: {result.TotalItemCount} items, {result.TotalCategoryCount} categories, {result.DuplicateItemCount} true duplicates");

            return (result, categoryTree, allCategoryDirectItems);
        }

        /// <summary>
        /// 상위-하위 관계가 아닌 독립적인 카테고리들만 반환
        /// </summary>
        private List<string> GetIndependentCategories(List<string> categories, Dictionary<string, WikiCategoryNode> tree)
        {
            var independent = new List<string>();

            foreach (var cat in categories)
            {
                bool isDescendantOfAnother = false;

                foreach (var otherCat in categories)
                {
                    if (cat == otherCat) continue;

                    // cat이 otherCat의 하위 카테고리인지 확인
                    if (IsDescendantOf(cat, otherCat, tree))
                    {
                        isDescendantOfAnother = true;
                        break;
                    }
                }

                // 다른 카테고리의 하위가 아닌 경우만 추가
                if (!isDescendantOfAnother)
                {
                    independent.Add(cat);
                }
            }

            return independent;
        }

        /// <summary>
        /// category가 ancestor의 하위 카테고리인지 확인 (재귀적으로)
        /// </summary>
        private bool IsDescendantOf(string category, string ancestor, Dictionary<string, WikiCategoryNode> tree)
        {
            if (!tree.TryGetValue(category, out var node))
                return false;

            var current = node.Parent;
            var visited = new HashSet<string>();

            while (!string.IsNullOrEmpty(current) && !visited.Contains(current))
            {
                visited.Add(current);

                if (current == ancestor)
                    return true;

                if (tree.TryGetValue(current, out var parentNode))
                {
                    current = parentNode.Parent;
                }
                else
                {
                    break;
                }
            }

            return false;
        }

        /// <summary>
        /// 결과를 JSON 파일로 저장합니다
        /// </summary>
        public async Task SaveResultToJsonAsync(
            WikiItemExportResult result,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(result, options);
            await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// 카테고리 트리를 JSON 파일로 저장합니다
        /// </summary>
        public async Task SaveTreeToJsonAsync(
            WikiCategoryTree tree,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(tree, options);
            await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// 트리 구조에서 카테고리 구조를 생성합니다
        /// DirectItems가 있는 모든 카테고리를 포함합니다 (Leaf가 아니어도 직접 아이템이 있으면 포함)
        /// </summary>
        public WikiCategoryStructure BuildCategoryStructure(
            WikiCategoryTree tree,
            Dictionary<string, List<string>> allCategoryDirectItems)
        {
            var structure = new WikiCategoryStructure
            {
                ExportedAt = DateTime.UtcNow,
                RootCategory = tree.RootCategory,
                LeafCategories = new Dictionary<string, WikiLeafCategory>(),
                DuplicateItems = new Dictionary<string, List<string>>()
            };

            // 1. 모든 카테고리에 대해 부모 목록 수집
            var categoryParents = new Dictionary<string, HashSet<string>>();

            foreach (var (catName, node) in tree.Tree)
            {
                if (!string.IsNullOrEmpty(node.Parent))
                {
                    if (!categoryParents.ContainsKey(catName))
                        categoryParents[catName] = new HashSet<string>();
                    categoryParents[catName].Add(node.Parent);
                }
            }

            // 2. DirectItems가 있는 모든 카테고리 찾기 (Leaf가 아니어도 포함)
            var categoriesWithItems = allCategoryDirectItems
                .Where(kvp => kvp.Value.Count > 0)
                .Select(kvp => kvp.Key)
                .ToList();

            // 3. 아이템 -> 카테고리 매핑 (중복 계산용)
            var itemToCategories = new Dictionary<string, List<string>>();
            var allItems = new HashSet<string>();

            foreach (var catName in categoriesWithItems)
            {
                var items = allCategoryDirectItems[catName];
                foreach (var item in items)
                {
                    allItems.Add(item);

                    if (!itemToCategories.ContainsKey(item))
                        itemToCategories[item] = new List<string>();

                    if (!itemToCategories[item].Contains(catName))
                        itemToCategories[item].Add(catName);
                }
            }

            // 4. 중복 아이템 계산 (부모-자식 관계가 아닌 독립적인 카테고리에 속하는 경우만)
            var duplicateItems = new Dictionary<string, List<string>>();
            foreach (var (itemName, categories) in itemToCategories.Where(kvp => kvp.Value.Count > 1))
            {
                // 부모-자식 관계를 제외한 독립적인 카테고리만 필터링
                var independentCategories = GetIndependentCategories(categories, tree.Tree);
                if (independentCategories.Count > 1)
                {
                    duplicateItems[itemName] = independentCategories;
                }
            }

            // 5. 각 카테고리 정보 구성
            foreach (var catName in categoriesWithItems)
            {
                var items = allCategoryDirectItems[catName];
                var isLeaf = tree.Tree.TryGetValue(catName, out var node) && node.Children.Count == 0;

                var leafCategory = new WikiLeafCategory
                {
                    Name = catName,
                    ItemCount = items.Count,
                    Items = items,
                    DuplicateCount = items.Count(item => duplicateItems.ContainsKey(item)),
                    ParentPaths = new Dictionary<string, List<string>>(),
                    IsLeaf = isLeaf
                };

                // 직접 부모들에 대해 경로 수집
                if (categoryParents.TryGetValue(catName, out var directParents))
                {
                    foreach (var directParent in directParents)
                    {
                        var ancestorPath = GetAncestorPath(directParent, categoryParents, tree.RootCategory);
                        leafCategory.ParentPaths[directParent] = ancestorPath;
                    }
                }

                structure.LeafCategories[catName] = leafCategory;
            }

            // 6. 통계 집계
            structure.TotalLeafCategories = structure.LeafCategories.Count;
            structure.TotalItems = allItems.Count;
            structure.DuplicateItemCount = duplicateItems.Count;
            structure.DuplicateItems = duplicateItems;

            return structure;
        }

        /// <summary>
        /// 특정 카테고리의 상위 경로를 재귀적으로 수집합니다
        /// </summary>
        private List<string> GetAncestorPath(
            string category,
            Dictionary<string, HashSet<string>> categoryParents,
            string rootCategory)
        {
            var path = new List<string>();
            var visited = new HashSet<string>();
            CollectAncestors(category, categoryParents, rootCategory, path, visited);
            return path;
        }

        private void CollectAncestors(
            string category,
            Dictionary<string, HashSet<string>> categoryParents,
            string rootCategory,
            List<string> path,
            HashSet<string> visited)
        {
            if (visited.Contains(category) || category == rootCategory)
                return;

            visited.Add(category);

            if (categoryParents.TryGetValue(category, out var parents))
            {
                foreach (var parent in parents)
                {
                    if (!path.Contains(parent))
                        path.Add(parent);

                    CollectAncestors(parent, categoryParents, rootCategory, path, visited);
                }
            }
        }

        /// <summary>
        /// 카테고리 구조를 JSON 파일로 저장합니다
        /// </summary>
        public async Task SaveStructureToJsonAsync(
            WikiCategoryStructure structure,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(structure, options);
            await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// 페이지 소스에 Infobox가 없는 페이지 목록을 가져옵니다 (카테고리 설명 페이지 필터링용)
        /// </summary>
        public async Task<HashSet<string>> GetPagesWithoutInfoboxAsync(
            IEnumerable<string> pageNames,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var pagesWithoutInfobox = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pageList = pageNames.ToList();

            if (pageList.Count == 0)
                return pagesWithoutInfobox;

            // Special:Export로 페이지 소스 가져오기 (배치 처리)
            const int batchSize = 50;
            var totalBatches = (int)Math.Ceiling(pageList.Count / (double)batchSize);

            for (int i = 0; i < totalBatches; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = pageList.Skip(i * batchSize).Take(batchSize).ToList();
                progress?.Invoke($"Checking Infobox in pages batch [{i + 1}/{totalBatches}] ({batch.Count} pages)...");

                try
                {
                    var pageContents = await ExportPagesAsync(batch, null, cancellationToken);

                    foreach (var (pageName, content) in pageContents)
                    {
                        // Infobox가 없는 페이지 식별
                        // {{Infobox 로 시작하는 템플릿이 없으면 카테고리 설명 페이지로 간주
                        if (!content.Contains("{{Infobox", StringComparison.OrdinalIgnoreCase))
                        {
                            pagesWithoutInfobox.Add(pageName);
                        }
                    }

                    // Rate limiting
                    await Task.Delay(200, cancellationToken);
                }
                catch (Exception ex)
                {
                    progress?.Invoke($"Error checking Infobox batch {i + 1}: {ex.Message}");
                }
            }

            progress?.Invoke($"Found {pagesWithoutInfobox.Count} pages without Infobox (category description pages)");
            return pagesWithoutInfobox;
        }

        /// <summary>
        /// Leaf 카테고리 기준으로 모든 아이템 목록을 생성합니다
        /// </summary>
        public WikiItemList BuildItemList(WikiCategoryStructure structure, WikiCategoryTree tree, HashSet<string>? excludedItems = null, HashSet<string>? pagesWithoutInfobox = null)
        {
            var itemList = new WikiItemList
            {
                ExportedAt = DateTime.UtcNow,
                Items = new List<WikiItem>()
            };

            excludedItems ??= new HashSet<string>();
            pagesWithoutInfobox ??= new HashSet<string>();

            // 전체 카테고리 트리에서 모든 카테고리 이름 수집 (카테고리 설명 페이지 필터링용)
            var allCategoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var catName in tree.Tree.Keys)
            {
                allCategoryNames.Add(catName);
            }

            // 아이템 -> 카테고리 매핑
            var itemToCategories = new Dictionary<string, List<string>>();

            foreach (var (catName, leafCat) in structure.LeafCategories)
            {
                foreach (var itemName in leafCat.Items)
                {
                    // 카테고리 설명 페이지 필터링: 아이템 이름이 카테고리 이름과 동일한 경우 제외
                    if (allCategoryNames.Contains(itemName))
                        continue;

                    // 제외 카테고리(Event content 등)에 속한 아이템 제외
                    if (excludedItems.Contains(itemName))
                        continue;

                    // Infobox가 없는 페이지 제외 (카테고리 설명 페이지)
                    if (pagesWithoutInfobox.Contains(itemName))
                        continue;

                    if (!itemToCategories.ContainsKey(itemName))
                        itemToCategories[itemName] = new List<string>();

                    if (!itemToCategories[itemName].Contains(catName))
                        itemToCategories[itemName].Add(catName);
                }
            }

            // 아이템 목록 생성
            foreach (var (itemName, categories) in itemToCategories.OrderBy(x => x.Key))
            {
                var wikiPageName = itemName.Replace(" ", "_");
                var wikiPageLink = $"https://escapefromtarkov.fandom.com/wiki/{Uri.EscapeDataString(wikiPageName)}";
                var wikiId = GenerateWikiId(wikiPageLink);

                var wikiItem = new WikiItem
                {
                    Id = wikiId,
                    Name = itemName,
                    WikiPageLink = wikiPageLink,
                    Category = categories.First(), // 첫 번째 카테고리를 메인으로
                    Categories = categories
                };

                itemList.Items.Add(wikiItem);
            }

            itemList.TotalItems = itemList.Items.Count;
            return itemList;
        }

        /// <summary>
        /// wikiPageLink를 Base64 인코딩하여 고유한 wikiId를 생성합니다.
        /// URL-safe Base64를 사용하여 파일명이나 ID로 안전하게 사용할 수 있습니다.
        /// </summary>
        public static string GenerateWikiId(string wikiPageLink)
        {
            var bytes = Encoding.UTF8.GetBytes(wikiPageLink);
            var base64 = Convert.ToBase64String(bytes);
            // URL-safe Base64: + -> -, / -> _, padding(=) 제거
            return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        /// <summary>
        /// wikiId를 디코딩하여 원래 wikiPageLink를 복원합니다.
        /// </summary>
        public static string DecodeWikiId(string wikiId)
        {
            // URL-safe Base64 복원: - -> +, _ -> /
            var base64 = wikiId.Replace('-', '+').Replace('_', '/');
            // padding 복원
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// 아이템 목록을 JSON 파일로 저장합니다
        /// </summary>
        public async Task SaveItemListToJsonAsync(
            WikiItemList itemList,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(itemList, options);
            await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    #region Models

    public class WikiItemExportResult
    {
        [JsonPropertyName("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonPropertyName("totalCategoryCount")]
        public int TotalCategoryCount { get; set; }

        [JsonPropertyName("totalItemCount")]
        public int TotalItemCount { get; set; }

        [JsonPropertyName("duplicateItemCount")]
        public int DuplicateItemCount { get; set; }

        [JsonPropertyName("categories")]
        public Dictionary<string, WikiCategoryData> Categories { get; set; } = new();

        [JsonPropertyName("duplicateItems")]
        public Dictionary<string, List<string>> DuplicateItems { get; set; } = new();
    }

    public class WikiCategoryData
    {
        [JsonPropertyName("categoryName")]
        public string CategoryName { get; set; } = "";

        [JsonPropertyName("itemCount")]
        public int ItemCount { get; set; }

        [JsonPropertyName("directItemCount")]
        public int DirectItemCount { get; set; }

        [JsonPropertyName("duplicateCount")]
        public int DuplicateCount { get; set; }

        [JsonPropertyName("subCategories")]
        public List<string> SubCategories { get; set; } = new();

        [JsonPropertyName("items")]
        public List<string> Items { get; set; } = new();

        [JsonPropertyName("directItems")]
        public List<string> DirectItems { get; set; } = new();
    }

    /// <summary>
    /// 카테고리 트리 구조
    /// </summary>
    public class WikiCategoryTree
    {
        [JsonPropertyName("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonPropertyName("rootCategory")]
        public string RootCategory { get; set; } = "";

        [JsonPropertyName("rootCategories")]
        public List<string> RootCategories { get; set; } = new();

        [JsonPropertyName("tree")]
        public Dictionary<string, WikiCategoryNode> Tree { get; set; } = new();
    }

    public class WikiCategoryNode
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("parent")]
        public string? Parent { get; set; }

        [JsonPropertyName("children")]
        public List<string> Children { get; set; } = new();

        [JsonPropertyName("directItemCount")]
        public int DirectItemCount { get; set; }

        [JsonPropertyName("totalItemCount")]
        public int TotalItemCount { get; set; }

        [JsonPropertyName("depth")]
        public int Depth { get; set; }
    }

    /// <summary>
    /// Wiki 아이템 정보
    /// </summary>
    public class WikiItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("wikiPageLink")]
        public string WikiPageLink { get; set; } = "";

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();
    }

    /// <summary>
    /// Wiki 아이템 목록 Export 결과
    /// </summary>
    public class WikiItemList
    {
        [JsonPropertyName("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonPropertyName("totalItems")]
        public int TotalItems { get; set; }

        [JsonPropertyName("items")]
        public List<WikiItem> Items { get; set; } = new();
    }

    /// <summary>
    /// 최하위 카테고리 기준 상위 경로 구조
    /// </summary>
    public class WikiCategoryStructure
    {
        [JsonPropertyName("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonPropertyName("rootCategory")]
        public string RootCategory { get; set; } = "";

        [JsonPropertyName("totalLeafCategories")]
        public int TotalLeafCategories { get; set; }

        [JsonPropertyName("totalItems")]
        public int TotalItems { get; set; }

        [JsonPropertyName("duplicateItemCount")]
        public int DuplicateItemCount { get; set; }

        [JsonPropertyName("leafCategories")]
        public Dictionary<string, WikiLeafCategory> LeafCategories { get; set; } = new();

        [JsonPropertyName("duplicateItems")]
        public Dictionary<string, List<string>> DuplicateItems { get; set; } = new();
    }

    public class WikiLeafCategory
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("isLeaf")]
        public bool IsLeaf { get; set; }

        [JsonPropertyName("itemCount")]
        public int ItemCount { get; set; }

        [JsonPropertyName("duplicateCount")]
        public int DuplicateCount { get; set; }

        [JsonPropertyName("items")]
        public List<string> Items { get; set; } = new();

        [JsonPropertyName("parentPaths")]
        public Dictionary<string, List<string>> ParentPaths { get; set; } = new();
    }

    #endregion
}
