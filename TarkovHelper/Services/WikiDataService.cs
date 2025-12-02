using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    public class WikiDataService : IDisposable
    {
        private static WikiDataService? _instance;
        public static WikiDataService Instance => _instance ??= new WikiDataService();

        private readonly HttpClient _httpClient;
        private const string QuestsPageUrl = "https://escapefromtarkov.fandom.com/wiki/Quests";
        private const string MediaWikiApiUrl = "https://escapefromtarkov.fandom.com/api.php";
        private const string QuestPagesCacheDir = "QuestPages";
        private const string CacheIndexFileName = "cache_index.json";
        private const int MaxTitlesPerRequest = 50; // MediaWiki API limit

        // Trader order matching the tab order in the Wiki page (tpt-1 to tpt-11)
        private static readonly string[] Traders =
        {
            "Prapor",
            "Therapist",
            "Fence",
            "Skier",
            "Peacekeeper",
            "Mechanic",
            "Ragman",
            "Jaeger",
            "Ref",
            "Lightkeeper",
            "BTR Driver"
        };

        public WikiDataService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<string> FetchQuestsPageAsync()
        {
            var response = await _httpClient.GetAsync(QuestsPageUrl);
            response.EnsureSuccessStatusCode();
            // Read as bytes and decode as UTF-8 to handle special characters correctly
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return Encoding.UTF8.GetString(bytes);
        }

        public async Task SaveWikiQuestPage()
        {
            var data = await FetchQuestsPageAsync();
            var filePath = Path.Combine(AppEnv.CachePath, "WikiQuestPage.html");
            Directory.CreateDirectory(AppEnv.CachePath);
            await File.WriteAllTextAsync(filePath, data, Encoding.UTF8);
        }

        /// <summary>
        /// Fetch quests page, parse HTML, and save as JSON grouped by trader.
        /// </summary>
        public async Task<WikiQuestsByTrader> FetchAndParseQuestsAsync()
        {
            var html = await FetchQuestsPageAsync();
            return ParseQuestsFromHtml(html);
        }

        /// <summary>
        /// Load cached HTML file and parse quests.
        /// </summary>
        public WikiQuestsByTrader ParseQuestsFromCache()
        {
            var filePath = Path.Combine(AppEnv.CachePath, "WikiQuestPage.html");
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Cached Wiki quest page not found.", filePath);
            }

            var html = File.ReadAllText(filePath, Encoding.UTF8);
            return ParseQuestsFromHtml(html);
        }

        /// <summary>
        /// Fix mojibake (incorrectly encoded UTF-8 characters) in quest names.
        /// The Wiki page has encoding issues in data-tpt-row-id attributes.
        /// Double-encoded UTF-8: E2 80 99 -> âÂ\x80Â\x99 pattern
        /// </summary>
        private static string FixMojibake(string text)
        {
            // Fix double-encoded UTF-8 patterns
            // Original ' (U+2019) = E2 80 99 in UTF-8
            // Double-encoded becomes: â (E2) + Â (C2) + 80 + Â (C2) + 99
            return text
                .Replace("\u00e2\u00c2\u0080\u00c2\u0099", "\u2019")  // ' RIGHT SINGLE QUOTATION MARK
                .Replace("\u00e2\u00c2\u0080\u00c2\u009c", "\u201c")  // " LEFT DOUBLE QUOTATION MARK
                .Replace("\u00e2\u00c2\u0080\u00c2\u009d", "\u201d")  // " RIGHT DOUBLE QUOTATION MARK
                .Replace("\u00e2\u00c2\u0080\u00c2\u0093", "\u2013")  // – EN DASH
                .Replace("\u00e2\u00c2\u0080\u00c2\u0094", "\u2014"); // — EM DASH
        }

        /// <summary>
        /// Parse HTML content and extract quests grouped by trader.
        /// </summary>
        public WikiQuestsByTrader ParseQuestsFromHtml(string html)
        {
            var result = new WikiQuestsByTrader();

            for (int i = 0; i < Traders.Length; i++)
            {
                var trader = Traders[i];
                var tableId = $"tpt-{i + 1}";
                var quests = new List<WikiQuest>();

                // Find table content
                var tablePattern = $@"<table id=""{tableId}""[^>]*>(.*?)</table>";
                var tableMatch = Regex.Match(html, tablePattern, RegexOptions.Singleline);

                if (tableMatch.Success)
                {
                    var tableContent = tableMatch.Groups[1].Value;

                    // Extract quest names from data-tpt-row-id attributes
                    var questPattern = @"data-tpt-row-id=""([^""]+)""";
                    var matches = Regex.Matches(tableContent, questPattern);

                    var seen = new HashSet<string>();
                    foreach (Match match in matches)
                    {
                        var questName = FixMojibake(match.Groups[1].Value);
                        if (!seen.Contains(questName))
                        {
                            seen.Add(questName);
                            quests.Add(new WikiQuest
                            {
                                Name = questName,
                                WikiPath = $"/wiki/{Uri.EscapeDataString(questName.Replace(" ", "_"))}"
                            });
                        }
                    }
                }

                result[trader] = quests;
            }

            return result;
        }

        /// <summary>
        /// Save quests data to JSON file.
        /// </summary>
        public async Task SaveQuestsToJsonAsync(WikiQuestsByTrader quests, string? fileName = null)
        {
            fileName ??= "quests_by_trader.json";
            var filePath = Path.Combine(AppEnv.CachePath, fileName);
            Directory.CreateDirectory(AppEnv.CachePath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(quests, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Load quests from JSON file.
        /// </summary>
        public async Task<WikiQuestsByTrader?> LoadQuestsFromJsonAsync(string? fileName = null)
        {
            fileName ??= "quests_by_trader.json";
            var filePath = Path.Combine(AppEnv.CachePath, fileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<WikiQuestsByTrader>(json);
        }

        /// <summary>
        /// Fetch, parse, and save quests in one call.
        /// </summary>
        public async Task<WikiQuestsByTrader> RefreshQuestsDataAsync()
        {
            // Save HTML cache
            await SaveWikiQuestPage();

            // Parse from cache
            var quests = ParseQuestsFromCache();

            // Save as JSON
            await SaveQuestsToJsonAsync(quests);

            return quests;
        }

        /// <summary>
        /// Get quest counts by trader and total count from cached JSON file.
        /// </summary>
        /// <returns>Tuple of (trader counts dictionary, total count)</returns>
        public async Task<(Dictionary<string, int> TraderCounts, int TotalCount)?> GetQuestCountsAsync()
        {
            var quests = await LoadQuestsFromJsonAsync();
            if (quests == null)
            {
                return null;
            }

            var traderCounts = new Dictionary<string, int>();
            int totalCount = 0;

            foreach (var trader in Traders)
            {
                if (quests.TryGetValue(trader, out var questList))
                {
                    traderCounts[trader] = questList.Count;
                    totalCount += questList.Count;
                }
                else
                {
                    traderCounts[trader] = 0;
                }
            }

            return (traderCounts, totalCount);
        }

        #region MediaWiki API Methods

        /// <summary>
        /// Get the cache directory path for quest pages
        /// </summary>
        private string GetQuestPagesCachePath() => Path.Combine(AppEnv.CachePath, QuestPagesCacheDir);

        /// <summary>
        /// Get the cache index file path
        /// </summary>
        private string GetCacheIndexPath() => Path.Combine(GetQuestPagesCachePath(), CacheIndexFileName);

        /// <summary>
        /// Load the cache index from disk
        /// </summary>
        public async Task<WikiCacheIndex> LoadCacheIndexAsync()
        {
            var path = GetCacheIndexPath();
            if (!File.Exists(path))
            {
                return new WikiCacheIndex();
            }

            var json = await File.ReadAllTextAsync(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<WikiCacheIndex>(json) ?? new WikiCacheIndex();
        }

        /// <summary>
        /// Save the cache index to disk
        /// </summary>
        public async Task SaveCacheIndexAsync(WikiCacheIndex index)
        {
            var path = GetCacheIndexPath();
            Directory.CreateDirectory(GetQuestPagesCachePath());

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(index, options);
            await File.WriteAllTextAsync(path, json, Encoding.UTF8);
        }

        /// <summary>
        /// Query MediaWiki API for revision info of multiple pages
        /// </summary>
        public async Task<Dictionary<string, (int PageId, int RevisionId, DateTime Timestamp)>> GetPagesRevisionInfoAsync(IEnumerable<string> titles)
        {
            var result = new Dictionary<string, (int, int, DateTime)>();
            var titleList = titles.ToList();

            // Process in batches due to API limit
            for (int i = 0; i < titleList.Count; i += MaxTitlesPerRequest)
            {
                var batch = titleList.Skip(i).Take(MaxTitlesPerRequest);
                var titlesParam = string.Join("|", batch);
                var url = $"{MediaWikiApiUrl}?action=query&titles={Uri.EscapeDataString(titlesParam)}&prop=revisions&rvprop=ids|timestamp&format=json";

                var responseBytes = await _httpClient.GetByteArrayAsync(url);
                var response = Encoding.UTF8.GetString(responseBytes);
                var doc = JsonDocument.Parse(response);

                if (doc.RootElement.TryGetProperty("query", out var query) &&
                    query.TryGetProperty("pages", out var pages))
                {
                    foreach (var page in pages.EnumerateObject())
                    {
                        if (page.Value.TryGetProperty("missing", out _))
                            continue;

                        var title = page.Value.GetProperty("title").GetString() ?? "";
                        var pageId = page.Value.GetProperty("pageid").GetInt32();

                        if (page.Value.TryGetProperty("revisions", out var revisions) &&
                            revisions.GetArrayLength() > 0)
                        {
                            var rev = revisions[0];
                            var revId = rev.GetProperty("revid").GetInt32();
                            var timestamp = DateTime.Parse(rev.GetProperty("timestamp").GetString() ?? "");
                            result[title] = (pageId, revId, timestamp);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Download a single page content using MediaWiki API
        /// </summary>
        public async Task<(string Content, int PageId, int RevisionId, DateTime Timestamp)?> DownloadPageAsync(string title)
        {
            var url = $"{MediaWikiApiUrl}?action=query&titles={Uri.EscapeDataString(title)}&prop=revisions&rvprop=ids|timestamp|content&rvslots=main&format=json";

            var responseBytes = await _httpClient.GetByteArrayAsync(url);
            var response = Encoding.UTF8.GetString(responseBytes);
            var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("query", out var query) &&
                query.TryGetProperty("pages", out var pages))
            {
                foreach (var page in pages.EnumerateObject())
                {
                    if (page.Value.TryGetProperty("missing", out _))
                        return null;

                    var pageId = page.Value.GetProperty("pageid").GetInt32();

                    if (page.Value.TryGetProperty("revisions", out var revisions) &&
                        revisions.GetArrayLength() > 0)
                    {
                        var rev = revisions[0];
                        var revId = rev.GetProperty("revid").GetInt32();
                        var timestamp = DateTime.Parse(rev.GetProperty("timestamp").GetString() ?? "");

                        if (rev.TryGetProperty("slots", out var slots) &&
                            slots.TryGetProperty("main", out var main) &&
                            main.TryGetProperty("*", out var content))
                        {
                            return (content.GetString() ?? "", pageId, revId, timestamp);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Get file path for cached quest page
        /// </summary>
        private string GetQuestPageFilePath(string questName)
        {
            var safeName = string.Join("_", questName.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(GetQuestPagesCachePath(), $"{safeName}.wiki");
        }

        /// <summary>
        /// Download all quest pages in parallel with revision checking
        /// </summary>
        /// <param name="forceDownload">If true, download all pages regardless of cache</param>
        /// <param name="progress">Progress callback (current, total, questName)</param>
        /// <returns>Download result summary with failed quest names</returns>
        public async Task<(int Downloaded, int Skipped, int Failed, List<string> FailedQuests)> DownloadAllQuestPagesAsync(
            bool forceDownload = false,
            Action<int, int, string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Ensure quest list exists
            var quests = await LoadQuestsFromJsonAsync();
            if (quests == null)
            {
                throw new InvalidOperationException("Quest list not found. Please download quest data first.");
            }

            // Get all quest names
            var allQuests = quests.Values.SelectMany(q => q).Select(q => q.Name).Distinct().ToList();
            int total = allQuests.Count;
            int downloaded = 0;
            int skipped = 0;
            int failed = 0;
            int processed = 0;
            var failedQuests = new List<string>();

            // Load cache index
            var cacheIndex = await LoadCacheIndexAsync();
            Directory.CreateDirectory(GetQuestPagesCachePath());

            // Get current revision info for all quests from API
            var currentRevisions = await GetPagesRevisionInfoAsync(allQuests);

            // Determine which pages need downloading
            var toDownload = new List<string>();
            foreach (var questName in allQuests)
            {
                if (forceDownload)
                {
                    toDownload.Add(questName);
                    continue;
                }

                // Check if we have cached version with same revision
                if (cacheIndex.TryGetValue(questName, out var cached) &&
                    currentRevisions.TryGetValue(questName, out var current) &&
                    cached.RevisionId == current.RevisionId &&
                    File.Exists(GetQuestPageFilePath(questName)))
                {
                    skipped++;
                    processed++;
                    progress?.Invoke(processed, total, $"[Skipped] {questName}");
                }
                else
                {
                    toDownload.Add(questName);
                }
            }

            // Download pages in parallel with throttling
            var semaphore = new SemaphoreSlim(10); // Max 10 concurrent requests
            var tasks = toDownload.Select(async questName =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var result = await DownloadPageAsync(questName);
                    if (result.HasValue)
                    {
                        var (content, pageId, revId, timestamp) = result.Value;

                        // Save content to file
                        await File.WriteAllTextAsync(GetQuestPageFilePath(questName), content, Encoding.UTF8, cancellationToken);

                        // Update cache index
                        lock (cacheIndex)
                        {
                            cacheIndex[questName] = new WikiPageCacheEntry
                            {
                                PageId = pageId,
                                RevisionId = revId,
                                Timestamp = timestamp,
                                CachedAt = DateTime.UtcNow
                            };
                        }

                        Interlocked.Increment(ref downloaded);
                        var proc = Interlocked.Increment(ref processed);
                        progress?.Invoke(proc, total, $"[Downloaded] {questName}");
                    }
                    else
                    {
                        Interlocked.Increment(ref failed);
                        lock (failedQuests) { failedQuests.Add(questName); }
                        var proc = Interlocked.Increment(ref processed);
                        progress?.Invoke(proc, total, $"[Not Found] {questName}");
                    }
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failed);
                    lock (failedQuests) { failedQuests.Add(questName); }
                    var proc = Interlocked.Increment(ref processed);
                    progress?.Invoke(proc, total, $"[Failed] {questName}");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Save updated cache index
            await SaveCacheIndexAsync(cacheIndex);

            return (downloaded, skipped, failed, failedQuests);
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public async Task<(int TotalCached, int TotalQuests, DateTime? OldestCache, DateTime? NewestCache)> GetCacheStatsAsync()
        {
            var quests = await LoadQuestsFromJsonAsync();
            var totalQuests = quests?.Values.SelectMany(q => q).Select(q => q.Name).Distinct().Count() ?? 0;

            var cacheIndex = await LoadCacheIndexAsync();
            var totalCached = cacheIndex.Count;

            DateTime? oldest = null;
            DateTime? newest = null;

            if (cacheIndex.Count > 0)
            {
                oldest = cacheIndex.Values.Min(c => c.CachedAt);
                newest = cacheIndex.Values.Max(c => c.CachedAt);
            }

            return (totalCached, totalQuests, oldest, newest);
        }

        #endregion

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
