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

namespace TarkovDBEditor.Services
{
    /// <summary>
    /// tarkov.dev GraphQL API를 사용하여 아이템 데이터를 가져오는 서비스
    /// wikiPageLink 기준으로 매칭하여 bsgId, nameEN, nameKO, nameJA를 채움
    /// </summary>
    public class TarkovDevDataService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private const string GraphQLEndpoint = "https://api.tarkov.dev/graphql";

        public TarkovDevDataService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TarkovDBEditor/1.0");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
        }

        /// <summary>
        /// wikiLink URL을 정규화합니다 (URL 인코딩 차이 해결)
        /// </summary>
        private static string NormalizeWikiLink(string wikiLink)
        {
            if (string.IsNullOrEmpty(wikiLink))
                return wikiLink;

            // URL 디코딩하여 통일 (%28 -> (, %29 -> ) 등)
            try
            {
                return Uri.UnescapeDataString(wikiLink);
            }
            catch
            {
                return wikiLink;
            }
        }

        /// <summary>
        /// tarkov.dev API에서 특정 언어의 모든 아이템을 가져옵니다
        /// </summary>
        public async Task<List<TarkovDevItem>> FetchAllItemsAsync(
            string lang = "en",
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke($"Fetching all items from tarkov.dev (lang: {lang})...");

            var query = @"
            {
                items(lang: " + lang + @") {
                    id
                    name
                    shortName
                    wikiLink
                }
            }";

            var requestBody = new { query };
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GraphQLEndpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var items = new List<TarkovDevItem>();

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("items", out var itemsArray))
            {
                foreach (var item in itemsArray.EnumerateArray())
                {
                    var devItem = new TarkovDevItem
                    {
                        Id = item.GetProperty("id").GetString() ?? "",
                        Name = item.GetProperty("name").GetString() ?? "",
                        ShortName = item.TryGetProperty("shortName", out var sn) ? sn.GetString() ?? "" : "",
                        WikiLink = item.TryGetProperty("wikiLink", out var wl) ? wl.GetString() ?? "" : ""
                    };
                    items.Add(devItem);
                }
            }

            progress?.Invoke($"Fetched {items.Count} items from tarkov.dev (lang: {lang})");
            return items;
        }

        /// <summary>
        /// 여러 언어의 아이템 데이터를 가져와 wikiLink로 매핑된 딕셔너리를 반환합니다
        /// </summary>
        public async Task<Dictionary<string, TarkovDevMultiLangItem>> FetchAllLanguagesAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke("Fetching items from tarkov.dev for all languages (EN, KO, JA)...");

            // 영어 데이터를 기준으로 시작
            var enItems = await FetchAllItemsAsync("en", progress, cancellationToken);
            var koItems = await FetchAllItemsAsync("ko", progress, cancellationToken);
            var jaItems = await FetchAllItemsAsync("ja", progress, cancellationToken);

            // wikiLink를 키로 사용하는 딕셔너리 생성 (정규화된 URL 사용)
            var result = new Dictionary<string, TarkovDevMultiLangItem>(StringComparer.OrdinalIgnoreCase);

            // 영어 아이템 기준으로 딕셔너리 초기화
            foreach (var item in enItems)
            {
                if (string.IsNullOrEmpty(item.WikiLink))
                    continue;

                var normalizedLink = NormalizeWikiLink(item.WikiLink);
                result[normalizedLink] = new TarkovDevMultiLangItem
                {
                    BsgId = item.Id,
                    WikiLink = item.WikiLink,  // 원본 보존
                    NameEN = item.Name,
                    ShortNameEN = item.ShortName
                };
            }

            // 한국어 이름 추가 (ID 기준 매칭)
            var koById = koItems.ToDictionary(x => x.Id, x => x);
            foreach (var kvp in result)
            {
                if (koById.TryGetValue(kvp.Value.BsgId, out var koItem))
                {
                    kvp.Value.NameKO = koItem.Name;
                    kvp.Value.ShortNameKO = koItem.ShortName;
                }
            }

            // 일본어 이름 추가 (ID 기준 매칭)
            var jaById = jaItems.ToDictionary(x => x.Id, x => x);
            foreach (var kvp in result)
            {
                if (jaById.TryGetValue(kvp.Value.BsgId, out var jaItem))
                {
                    kvp.Value.NameJA = jaItem.Name;
                    kvp.Value.ShortNameJA = jaItem.ShortName;
                }
            }

            progress?.Invoke($"Built multi-language dictionary with {result.Count} items");
            return result;
        }

        /// <summary>
        /// wiki_items.json을 읽어 tarkov.dev 데이터로 enrichment하고 저장합니다
        /// </summary>
        public async Task<EnrichmentResult> EnrichWikiItemsAsync(
            string wikiItemsPath,
            string outputPath,
            string missingOutputPath,
            string devOnlyOutputPath,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke("Loading wiki_items.json...");

            // wiki_items.json 로드
            var wikiJson = await File.ReadAllTextAsync(wikiItemsPath, cancellationToken);
            var wikiItemList = JsonSerializer.Deserialize<WikiItemList>(wikiJson);

            if (wikiItemList == null || wikiItemList.Items == null)
            {
                throw new InvalidOperationException("Failed to load wiki_items.json");
            }

            progress?.Invoke($"Loaded {wikiItemList.Items.Count} wiki items");

            // tarkov.dev에서 다국어 데이터 가져오기
            var devItems = await FetchAllLanguagesAsync(progress, cancellationToken);

            progress?.Invoke("Matching wiki items with tarkov.dev data by wikiLink...");

            var enrichedItems = new List<EnrichedWikiItem>();
            var missingItems = new List<MissingDevItem>();
            var matchedWikiLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matchedCount = 0;

            foreach (var wikiItem in wikiItemList.Items)
            {
                // wikiPageLink URL 디코딩 (%22 -> ", %28 -> ( 등)
                var decodedWikiLink = NormalizeWikiLink(wikiItem.WikiPageLink);

                var enriched = new EnrichedWikiItem
                {
                    Id = wikiItem.Id,
                    Name = wikiItem.Name,
                    WikiPageLink = decodedWikiLink,
                    IconUrl = wikiItem.IconUrl,  // 아이콘 URL 보존
                    Category = wikiItem.Category,
                    Categories = wikiItem.Categories
                };

                // 정규화된 wikiPageLink로 매칭 시도
                if (!string.IsNullOrEmpty(decodedWikiLink) &&
                    devItems.TryGetValue(decodedWikiLink, out var devItem))
                {
                    enriched.BsgId = devItem.BsgId;
                    enriched.NameEN = devItem.NameEN;
                    enriched.NameKO = devItem.NameKO;
                    enriched.NameJA = devItem.NameJA;
                    enriched.ShortNameEN = devItem.ShortNameEN;
                    enriched.ShortNameKO = devItem.ShortNameKO;
                    enriched.ShortNameJA = devItem.ShortNameJA;
                    matchedWikiLinks.Add(decodedWikiLink);
                    matchedCount++;
                }
                else
                {
                    // 매칭 실패 - Name을 다국어 이름으로 사용
                    enriched.NameEN = wikiItem.Name;
                    enriched.NameKO = wikiItem.Name;
                    enriched.NameJA = wikiItem.Name;

                    // missing 목록에 추가
                    missingItems.Add(new MissingDevItem
                    {
                        WikiId = wikiItem.Id,
                        WikiName = wikiItem.Name,
                        WikiPageLink = decodedWikiLink,
                        Category = wikiItem.Category,
                        Categories = wikiItem.Categories
                    });
                }

                enrichedItems.Add(enriched);
            }

            // tarkov.dev에만 있는 아이템 찾기
            var devOnlyItems = new List<DevOnlyItem>();
            foreach (var kvp in devItems)
            {
                if (!matchedWikiLinks.Contains(kvp.Key))
                {
                    devOnlyItems.Add(new DevOnlyItem
                    {
                        BsgId = kvp.Value.BsgId,
                        WikiLink = kvp.Value.WikiLink,
                        NameEN = kvp.Value.NameEN,
                        NameKO = kvp.Value.NameKO,
                        NameJA = kvp.Value.NameJA,
                        ShortNameEN = kvp.Value.ShortNameEN,
                        ShortNameKO = kvp.Value.ShortNameKO,
                        ShortNameJA = kvp.Value.ShortNameJA
                    });
                }
            }

            progress?.Invoke($"Matched {matchedCount}/{wikiItemList.Items.Count} items. Wiki missing: {missingItems.Count}, Dev only: {devOnlyItems.Count}");

            // 결과 저장
            var enrichedResult = new EnrichedWikiItemList
            {
                ExportedAt = DateTime.UtcNow,
                TotalItems = enrichedItems.Count,
                MatchedItems = matchedCount,
                MissingItems = missingItems.Count,
                DevOnlyItems = devOnlyItems.Count,
                Items = enrichedItems
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            // enriched wiki_items.json 저장
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var enrichedJson = JsonSerializer.Serialize(enrichedResult, options);
            await File.WriteAllTextAsync(outputPath, enrichedJson, Encoding.UTF8, cancellationToken);
            progress?.Invoke($"Saved enriched items to: {outputPath}");

            // dev_missing.json 저장 (Wiki에는 있지만 tarkov.dev에 없는 아이템)
            if (missingItems.Count > 0)
            {
                var missingResult = new MissingDevItemList
                {
                    ExportedAt = DateTime.UtcNow,
                    TotalMissing = missingItems.Count,
                    Items = missingItems
                };
                var missingJson = JsonSerializer.Serialize(missingResult, options);
                await File.WriteAllTextAsync(missingOutputPath, missingJson, Encoding.UTF8, cancellationToken);
                progress?.Invoke($"Saved wiki-only items to: {missingOutputPath}");
            }

            // dev_only.json 저장 (tarkov.dev에는 있지만 Wiki에 없는 아이템)
            if (devOnlyItems.Count > 0)
            {
                var devOnlyResult = new DevOnlyItemList
                {
                    ExportedAt = DateTime.UtcNow,
                    TotalDevOnly = devOnlyItems.Count,
                    Items = devOnlyItems
                };
                var devOnlyJson = JsonSerializer.Serialize(devOnlyResult, options);
                await File.WriteAllTextAsync(devOnlyOutputPath, devOnlyJson, Encoding.UTF8, cancellationToken);
                progress?.Invoke($"Saved dev-only items to: {devOnlyOutputPath}");
            }

            return new EnrichmentResult
            {
                TotalItems = enrichedItems.Count,
                MatchedCount = matchedCount,
                MissingCount = missingItems.Count,
                DevOnlyCount = devOnlyItems.Count,
                OutputPath = outputPath,
                MissingOutputPath = missingOutputPath,
                DevOnlyOutputPath = devOnlyOutputPath
            };
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    #region tarkov.dev Models

    /// <summary>
    /// tarkov.dev API에서 가져온 단일 언어 아이템
    /// </summary>
    public class TarkovDevItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string WikiLink { get; set; } = "";
    }

    /// <summary>
    /// 다국어 통합 아이템 (wikiLink 기준 매핑)
    /// </summary>
    public class TarkovDevMultiLangItem
    {
        public string BsgId { get; set; } = "";
        public string WikiLink { get; set; } = "";
        public string NameEN { get; set; } = "";
        public string ShortNameEN { get; set; } = "";
        public string? NameKO { get; set; }
        public string? ShortNameKO { get; set; }
        public string? NameJA { get; set; }
        public string? ShortNameJA { get; set; }
    }

    /// <summary>
    /// tarkov.dev 데이터로 enrichment된 Wiki 아이템
    /// </summary>
    public class EnrichedWikiItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("bsgId")]
        public string? BsgId { get; set; }

        [JsonPropertyName("nameEN")]
        public string? NameEN { get; set; }

        [JsonPropertyName("nameKO")]
        public string? NameKO { get; set; }

        [JsonPropertyName("nameJA")]
        public string? NameJA { get; set; }

        [JsonPropertyName("shortNameEN")]
        public string? ShortNameEN { get; set; }

        [JsonPropertyName("shortNameKO")]
        public string? ShortNameKO { get; set; }

        [JsonPropertyName("shortNameJA")]
        public string? ShortNameJA { get; set; }

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
    /// Enrichment된 Wiki 아이템 목록
    /// </summary>
    public class EnrichedWikiItemList
    {
        [JsonPropertyName("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonPropertyName("totalItems")]
        public int TotalItems { get; set; }

        [JsonPropertyName("matchedItems")]
        public int MatchedItems { get; set; }

        [JsonPropertyName("missingItems")]
        public int MissingItems { get; set; }

        [JsonPropertyName("devOnlyItems")]
        public int DevOnlyItems { get; set; }

        [JsonPropertyName("items")]
        public List<EnrichedWikiItem> Items { get; set; } = new();
    }

    /// <summary>
    /// tarkov.dev에서 매칭되지 않은 아이템
    /// </summary>
    public class MissingDevItem
    {
        [JsonPropertyName("wikiId")]
        public string WikiId { get; set; } = "";

        [JsonPropertyName("wikiName")]
        public string WikiName { get; set; } = "";

        [JsonPropertyName("wikiPageLink")]
        public string WikiPageLink { get; set; } = "";

        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        [JsonPropertyName("categories")]
        public List<string> Categories { get; set; } = new();
    }

    /// <summary>
    /// 누락 아이템 목록 (dev_missing.json)
    /// </summary>
    public class MissingDevItemList
    {
        [JsonPropertyName("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonPropertyName("totalMissing")]
        public int TotalMissing { get; set; }

        [JsonPropertyName("items")]
        public List<MissingDevItem> Items { get; set; } = new();
    }

    /// <summary>
    /// Enrichment 결과
    /// </summary>
    public class EnrichmentResult
    {
        public int TotalItems { get; set; }
        public int MatchedCount { get; set; }
        public int MissingCount { get; set; }
        public int DevOnlyCount { get; set; }
        public string OutputPath { get; set; } = "";
        public string MissingOutputPath { get; set; } = "";
        public string DevOnlyOutputPath { get; set; } = "";
    }

    /// <summary>
    /// tarkov.dev에만 있는 아이템 (Wiki에는 없음)
    /// </summary>
    public class DevOnlyItem
    {
        [JsonPropertyName("bsgId")]
        public string BsgId { get; set; } = "";

        [JsonPropertyName("wikiLink")]
        public string WikiLink { get; set; } = "";

        [JsonPropertyName("nameEN")]
        public string NameEN { get; set; } = "";

        [JsonPropertyName("nameKO")]
        public string? NameKO { get; set; }

        [JsonPropertyName("nameJA")]
        public string? NameJA { get; set; }

        [JsonPropertyName("shortNameEN")]
        public string? ShortNameEN { get; set; }

        [JsonPropertyName("shortNameKO")]
        public string? ShortNameKO { get; set; }

        [JsonPropertyName("shortNameJA")]
        public string? ShortNameJA { get; set; }
    }

    /// <summary>
    /// tarkov.dev에만 있는 아이템 목록 (dev_only.json)
    /// </summary>
    public class DevOnlyItemList
    {
        [JsonPropertyName("exportedAt")]
        public DateTime ExportedAt { get; set; }

        [JsonPropertyName("totalDevOnly")]
        public int TotalDevOnly { get; set; }

        [JsonPropertyName("items")]
        public List<DevOnlyItem> Items { get; set; } = new();
    }

    #endregion
}
