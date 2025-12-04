using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
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
        private const string SpecialExportUrl = "https://escapefromtarkov.fandom.com/wiki/Special:Export";
        private const string QuestPagesCacheDir = "QuestPages";
        private const string CacheIndexFileName = "cache_index.json";
        private const string ExportCacheFileName = "quests_export.xml";
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
                        // Decode HTML entities (e.g., &#39; -> ')
                        questName = WebUtility.HtmlDecode(questName);
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
        /// Query MediaWiki API for revision info of multiple pages (parallel batch processing)
        /// Input: quest names (e.g., "Immunity")
        /// Output: keyed by quest names, after wiki page name mapping (e.g., "Immunity_(quest)" -> "Immunity")
        /// </summary>
        public async Task<Dictionary<string, (int PageId, int RevisionId, DateTime Timestamp)>> GetPagesRevisionInfoAsync(IEnumerable<string> questNames)
        {
            var result = new Dictionary<string, (int, int, DateTime)>();
            var questNameList = questNames.ToList();

            // Build mapping from wiki page name to quest name
            // Note: MediaWiki API normalizes underscores to spaces in responses
            var wikiPageToQuestName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var wikiPageNames = new List<string>();
            foreach (var questName in questNameList)
            {
                var wikiPageName = NormalizedNameGenerator.GetWikiPageName(questName);
                wikiPageNames.Add(wikiPageName);
                // Map both underscore and space versions (API returns spaces)
                wikiPageToQuestName[wikiPageName] = questName;
                wikiPageToQuestName[wikiPageName.Replace("_", " ")] = questName;
            }

            // Create batches using wiki page names
            var batches = new List<List<string>>();
            for (int i = 0; i < wikiPageNames.Count; i += MaxTitlesPerRequest)
            {
                batches.Add(wikiPageNames.Skip(i).Take(MaxTitlesPerRequest).ToList());
            }

            // Process batches sequentially to avoid rate limiting
            foreach (var batch in batches)
            {
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

                        var wikiPageName = page.Value.GetProperty("title").GetString() ?? "";
                        var pageId = page.Value.GetProperty("pageid").GetInt32();

                        if (page.Value.TryGetProperty("revisions", out var revisions) &&
                            revisions.GetArrayLength() > 0)
                        {
                            var rev = revisions[0];
                            var revId = rev.GetProperty("revid").GetInt32();
                            var timestamp = DateTime.Parse(rev.GetProperty("timestamp").GetString() ?? "");

                            // Map wiki page name back to original quest name
                            if (wikiPageToQuestName.TryGetValue(wikiPageName, out var questName))
                            {
                                result[questName] = (pageId, revId, timestamp);
                            }
                            else
                            {
                                result[wikiPageName] = (pageId, revId, timestamp);
                            }
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
        /// Download multiple pages in a single batch request using MediaWiki API
        /// This significantly reduces the number of HTTP requests (from ~250 to ~5)
        /// </summary>
        /// <param name="titles">List of page titles to download (max 50 per batch)</param>
        /// <returns>Dictionary of title -> (Content, PageId, RevisionId, Timestamp)</returns>
        public async Task<Dictionary<string, (string Content, int PageId, int RevisionId, DateTime Timestamp)>> DownloadPagesBatchAsync(
            IEnumerable<string> titles,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, (string, int, int, DateTime)>();
            var titleList = titles.ToList();

            if (titleList.Count == 0)
                return result;

            // MediaWiki API allows max 50 titles per request
            var titlesParam = string.Join("|", titleList);
            var url = $"{MediaWikiApiUrl}?action=query&titles={Uri.EscapeDataString(titlesParam)}&prop=revisions&rvprop=ids|timestamp|content&rvslots=main&format=json";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var responseText = Encoding.UTF8.GetString(responseBytes);
            var doc = JsonDocument.Parse(responseText);

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

                        if (rev.TryGetProperty("slots", out var slots) &&
                            slots.TryGetProperty("main", out var main) &&
                            main.TryGetProperty("*", out var content))
                        {
                            result[title] = (content.GetString() ?? "", pageId, revId, timestamp);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get recently changed quest pages using MediaWiki RecentChanges API
        /// </summary>
        /// <param name="since">Only get changes since this date</param>
        /// <param name="questNames">Filter to only these quest names</param>
        /// <returns>List of quest names that have been modified</returns>
        public async Task<List<string>> GetRecentlyChangedQuestsAsync(
            DateTime since,
            HashSet<string> questNames,
            CancellationToken cancellationToken = default)
        {
            var changedQuests = new List<string>();
            var rcstart = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Query recent changes in the main namespace (0)
            var url = $"{MediaWikiApiUrl}?action=query&list=recentchanges&rcnamespace=0&rcstart={rcstart}&rcdir=newer&rclimit=500&rcprop=title|timestamp&format=json";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var responseText = Encoding.UTF8.GetString(responseBytes);
            var doc = JsonDocument.Parse(responseText);

            if (doc.RootElement.TryGetProperty("query", out var query) &&
                query.TryGetProperty("recentchanges", out var recentChanges))
            {
                foreach (var change in recentChanges.EnumerateArray())
                {
                    var title = change.GetProperty("title").GetString() ?? "";
                    if (questNames.Contains(title) && !changedQuests.Contains(title))
                    {
                        changedQuests.Add(title);
                    }
                }
            }

            return changedQuests;
        }

        #endregion

        #region Special:Export Methods (Fast bulk download)

        /// <summary>
        /// Download all quest pages using Special:Export (single HTTP request for all pages)
        /// This is the fastest and most reliable method - avoids 503 errors completely
        /// </summary>
        /// <param name="questNames">List of quest names to export</param>
        /// <param name="progress">Progress callback</param>
        /// <returns>Dictionary of quest name -> wiki content (keyed by original quest name, not wiki page name)</returns>
        public async Task<Dictionary<string, string>> ExportQuestPagesAsync(
            IEnumerable<string> questNames,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string>();
            var questList = questNames.ToList();

            if (questList.Count == 0)
                return result;

            // Build mapping from wiki page name to original quest name
            // Some quests have disambiguation pages (e.g., "Immunity" -> "Immunity_(quest)")
            // Note: MediaWiki API normalizes underscores to spaces in responses
            var wikiPageToQuestName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var wikiPageNames = new List<string>();

            foreach (var questName in questList)
            {
                var wikiPageName = NormalizedNameGenerator.GetWikiPageName(questName);
                wikiPageNames.Add(wikiPageName);
                // Map both underscore and space versions (API returns spaces)
                wikiPageToQuestName[wikiPageName] = questName;
                wikiPageToQuestName[wikiPageName.Replace("_", " ")] = questName;
            }

            progress?.Invoke($"Requesting {wikiPageNames.Count} pages from Special:Export...");

            // Build POST data for Special:Export (use wiki page names, not quest names)
            var postData = new Dictionary<string, string>
            {
                { "catname", "" },
                { "pages", string.Join("\n", wikiPageNames) },
                { "curonly", "1" },  // Current revision only
                { "wpDownload", "1" }  // Download as file
            };

            var content = new FormUrlEncodedContent(postData);

            // Set longer timeout for large exports
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(5));

            // Use streaming to show download progress
            var request = new HttpRequestMessage(HttpMethod.Post, SpecialExportUrl) { Content = content };
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            // Get content length if available
            var contentLength = response.Content.Headers.ContentLength;
            var totalSizeText = contentLength.HasValue
                ? $"{contentLength.Value / 1024.0 / 1024.0:F1}MB"
                : "unknown size";

            progress?.Invoke($"Downloading XML ({totalSizeText})...");

            // Read with progress reporting
            string xmlContent;
            using (var stream = await response.Content.ReadAsStreamAsync(cts.Token))
            using (var memoryStream = new MemoryStream())
            {
                var buffer = new byte[81920]; // 80KB buffer
                int bytesRead;
                long totalBytesRead = 0;
                var lastProgressReport = DateTime.MinValue;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
                {
                    await memoryStream.WriteAsync(buffer, 0, bytesRead, cts.Token);
                    totalBytesRead += bytesRead;

                    // Report progress every 500ms to avoid UI flooding
                    if ((DateTime.Now - lastProgressReport).TotalMilliseconds > 500)
                    {
                        var downloadedMB = totalBytesRead / 1024.0 / 1024.0;
                        if (contentLength.HasValue)
                        {
                            var percent = (int)(totalBytesRead * 100 / contentLength.Value);
                            progress?.Invoke($"Downloading XML... {downloadedMB:F1}MB / {totalSizeText} ({percent}%)");
                        }
                        else
                        {
                            progress?.Invoke($"Downloading XML... {downloadedMB:F1}MB");
                        }
                        lastProgressReport = DateTime.Now;
                    }
                }

                memoryStream.Position = 0;
                using var reader = new StreamReader(memoryStream, Encoding.UTF8);
                xmlContent = await reader.ReadToEndAsync();
            }

            progress?.Invoke($"Parsing XML ({xmlContent.Length / 1024}KB)...");

            // Parse the MediaWiki XML export (returns wiki page name -> content)
            var wikiPageResults = ParseMediaWikiExportXml(xmlContent);

            // Map wiki page names back to original quest names
            foreach (var kvp in wikiPageResults)
            {
                var wikiPageName = kvp.Key;
                var pageContent = kvp.Value;

                // Find the original quest name for this wiki page
                if (wikiPageToQuestName.TryGetValue(wikiPageName, out var questName))
                {
                    result[questName] = pageContent;
                }
                else
                {
                    // Fallback: use wiki page name as-is (shouldn't happen normally)
                    result[wikiPageName] = pageContent;
                }
            }

            progress?.Invoke($"Successfully exported {result.Count} pages");

            return result;
        }

        /// <summary>
        /// Parse MediaWiki XML export format and extract page contents
        /// </summary>
        private Dictionary<string, string> ParseMediaWikiExportXml(string xmlContent)
        {
            var result = new Dictionary<string, string>();

            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            // MediaWiki export uses a namespace
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("mw", "http://www.mediawiki.org/xml/export-0.11/");

            // Try different namespace versions
            var namespaces = new[]
            {
                "http://www.mediawiki.org/xml/export-0.11/",
                "http://www.mediawiki.org/xml/export-0.10/",
                "http://www.mediawiki.org/xml/export-0.9/"
            };

            XmlNodeList? pageNodes = null;
            foreach (var ns in namespaces)
            {
                nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("mw", ns);
                pageNodes = doc.SelectNodes("//mw:page", nsmgr);
                if (pageNodes != null && pageNodes.Count > 0)
                    break;
            }

            // Fallback: try without namespace
            if (pageNodes == null || pageNodes.Count == 0)
            {
                pageNodes = doc.SelectNodes("//page");
            }

            if (pageNodes == null)
                return result;

            foreach (XmlNode pageNode in pageNodes)
            {
                var titleNode = pageNode.SelectSingleNode("mw:title", nsmgr) ?? pageNode.SelectSingleNode("title");
                var textNode = pageNode.SelectSingleNode("mw:revision/mw:text", nsmgr)
                            ?? pageNode.SelectSingleNode("revision/text");

                if (titleNode != null && textNode != null)
                {
                    var title = titleNode.InnerText;
                    var text = textNode.InnerText;
                    result[title] = text;
                }
            }

            return result;
        }

        /// <summary>
        /// Download all quest pages using Special:Export and save to cache
        /// This is the recommended method - single HTTP request, no 503 errors
        /// </summary>
        public async Task<(int Downloaded, int Skipped, int Failed, List<string> FailedQuests)> DownloadAllQuestPagesViaExportAsync(
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
            var failedQuests = new List<string>();

            // Load cache index
            var cacheIndex = await LoadCacheIndexAsync();
            Directory.CreateDirectory(GetQuestPagesCachePath());

            // Determine which pages need downloading
            List<string> toDownload;

            if (forceDownload)
            {
                toDownload = allQuests;
                progress?.Invoke(0, total, "Force downloading all quest pages...");
            }
            else
            {
                // Check which pages are missing from cache
                toDownload = allQuests.Where(q => !File.Exists(GetQuestPageFilePath(q))).ToList();
                skipped = allQuests.Count - toDownload.Count;

                if (toDownload.Count == 0)
                {
                    progress?.Invoke(total, total, $"All {total} quest pages are cached");
                    return (0, skipped, 0, failedQuests);
                }

                progress?.Invoke(skipped, total, $"Found {toDownload.Count} pages to download, {skipped} cached");
            }

            // Export all pages in a single request
            try
            {
                progress?.Invoke(skipped, total, $"Downloading {toDownload.Count} pages via Special:Export...");

                var exportedPages = await ExportQuestPagesAsync(
                    toDownload,
                    msg => progress?.Invoke(skipped, total, msg),
                    cancellationToken);

                // Save each page to cache
                foreach (var questName in toDownload)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (exportedPages.TryGetValue(questName, out var pageContent))
                    {
                        await File.WriteAllTextAsync(GetQuestPageFilePath(questName), pageContent, Encoding.UTF8, cancellationToken);

                        cacheIndex[questName] = new WikiPageCacheEntry
                        {
                            PageId = 0,
                            RevisionId = 0,
                            Timestamp = DateTime.UtcNow,
                            CachedAt = DateTime.UtcNow
                        };

                        downloaded++;
                        progress?.Invoke(skipped + downloaded, total, $"[Saved] {questName}");
                    }
                    else
                    {
                        failed++;
                        failedQuests.Add(questName);
                        progress?.Invoke(skipped + downloaded + failed, total, $"[Not Found] {questName}");
                    }
                }
            }
            catch (Exception ex)
            {
                progress?.Invoke(skipped, total, $"Export failed: {ex.Message}");
                // Mark all remaining as failed
                foreach (var q in toDownload.Where(q => !File.Exists(GetQuestPageFilePath(q))))
                {
                    failed++;
                    failedQuests.Add(q);
                }
            }

            // Save updated cache index
            await SaveCacheIndexAsync(cacheIndex);

            return (downloaded, skipped, failed, failedQuests);
        }

        /// <summary>
        /// Download specific quest pages using MediaWiki API with PARALLEL batch requests
        /// Uses concurrent downloads for 10-50x speed improvement
        /// </summary>
        public async Task<(int Downloaded, int Failed, List<string> FailedQuests)> DownloadSpecificQuestPagesAsync(
            IEnumerable<string> questNames,
            Action<int, int, string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var toDownload = questNames.ToList();
            if (toDownload.Count == 0)
                return (0, 0, new List<string>());

            int total = toDownload.Count;
            int downloaded = 0;
            int failed = 0;
            var failedQuests = new List<string>();
            var lockObj = new object();

            // Load cache index
            var cacheIndex = await LoadCacheIndexAsync();
            Directory.CreateDirectory(GetQuestPagesCachePath());

            progress?.Invoke(0, total, $"Downloading {total} pages via parallel API requests...");

            // Create batches of 10 pages each (smaller batches = faster individual requests)
            const int pagesPerBatch = 10;
            const int maxParallelBatches = 5; // 5 concurrent requests

            var batches = new List<List<string>>();
            for (int i = 0; i < toDownload.Count; i += pagesPerBatch)
            {
                batches.Add(toDownload.Skip(i).Take(pagesPerBatch).ToList());
            }

            // Process batches in parallel groups
            for (int groupStart = 0; groupStart < batches.Count; groupStart += maxParallelBatches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var parallelBatches = batches.Skip(groupStart).Take(maxParallelBatches).ToList();
                var groupNum = (groupStart / maxParallelBatches) + 1;
                var totalGroups = (int)Math.Ceiling(batches.Count / (double)maxParallelBatches);

                progress?.Invoke(downloaded + failed, total, $"[Parallel {groupNum}/{totalGroups}] Downloading {parallelBatches.Sum(b => b.Count)} pages...");

                // Execute batches in parallel
                var tasks = parallelBatches.Select(async batch =>
                {
                    try
                    {
                        // Build mapping from wiki page name to quest name for this batch
                        // Note: MediaWiki API normalizes underscores to spaces in responses
                        var wikiPageToQuestName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var wikiPageNames = new List<string>();
                        foreach (var questName in batch)
                        {
                            var wikiPageName = NormalizedNameGenerator.GetWikiPageName(questName);
                            wikiPageNames.Add(wikiPageName);
                            // Map both underscore and space versions (API returns spaces)
                            wikiPageToQuestName[wikiPageName] = questName;
                            wikiPageToQuestName[wikiPageName.Replace("_", " ")] = questName;
                        }

                        var batchResults = await DownloadPagesBatchAsync(wikiPageNames, cancellationToken);

                        foreach (var questName in batch)
                        {
                            var wikiPageName = NormalizedNameGenerator.GetWikiPageName(questName);
                            var wikiPageNameWithSpace = wikiPageName.Replace("_", " ");
                            if (batchResults.TryGetValue(wikiPageName, out var pageData) ||
                                batchResults.TryGetValue(wikiPageNameWithSpace, out pageData))
                            {
                                var (content, pageId, revId, timestamp) = pageData;

                                // Save content to file (using quest name, not wiki page name)
                                await File.WriteAllTextAsync(GetQuestPageFilePath(questName), content, Encoding.UTF8, cancellationToken);

                                lock (lockObj)
                                {
                                    cacheIndex[questName] = new WikiPageCacheEntry
                                    {
                                        PageId = pageId,
                                        RevisionId = revId,
                                        Timestamp = timestamp,
                                        CachedAt = DateTime.UtcNow
                                    };
                                    downloaded++;
                                }
                            }
                            else
                            {
                                lock (lockObj)
                                {
                                    failed++;
                                    failedQuests.Add(questName);
                                }
                            }
                        }
                    }
                    catch (HttpRequestException)
                    {
                        lock (lockObj)
                        {
                            foreach (var questName in batch)
                            {
                                failed++;
                                failedQuests.Add(questName);
                            }
                        }
                    }
                });

                await Task.WhenAll(tasks);

                // Brief pause between parallel groups to avoid rate limiting
                if (groupStart + maxParallelBatches < batches.Count)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            progress?.Invoke(total, total, $"Completed: {downloaded} downloaded, {failed} failed");

            // Save updated cache index
            await SaveCacheIndexAsync(cacheIndex);

            return (downloaded, failed, failedQuests);
        }

        #endregion

        /// <summary>
        /// Get file path for cached quest page
        /// Uses same normalization as TarkovDataService.NormalizeQuestName for consistency
        /// </summary>
        private string GetQuestPageFilePath(string questName)
        {
            var safeName = NormalizeQuestName(questName);
            return Path.Combine(GetQuestPagesCachePath(), $"{safeName}.wiki");
        }

        /// <summary>
        /// Normalize quest name for filesystem-safe filenames
        /// Must match TarkovDataService.NormalizeQuestName exactly
        /// </summary>
        private static string NormalizeQuestName(string name)
        {
            return name
                // Normalize various apostrophe types to standard single quote
                .Replace("'", "'")  // RIGHT SINGLE QUOTATION MARK (U+2019) -> '
                .Replace("'", "'")  // LEFT SINGLE QUOTATION MARK (U+2018) -> '
                .Replace("ʼ", "'")  // MODIFIER LETTER APOSTROPHE -> '
                // Replace invalid filename characters with underscore
                .Replace("?", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_")
                .Replace("#", "_")  // Hash causes path issues
                .Replace("/", "_")  // Forward slash
                .Replace("\\", "_"); // Backslash
        }

        /// <summary>
        /// Download all quest pages using batch API requests for efficiency
        /// Uses batched MediaWiki API calls (50 pages per request) instead of individual requests
        /// This reduces ~250 HTTP requests to ~5 requests, avoiding 503 rate limit errors
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

            // Determine which pages need downloading
            var toDownload = new List<string>();

            if (forceDownload)
            {
                toDownload = allQuests;
            }
            else
            {
                // Use incremental update: check cache and only download changed/new pages
                var cacheOldest = cacheIndex.Count > 0 ? cacheIndex.Values.Min(c => c.CachedAt) : DateTime.MinValue;

                // Get current revision info for all quests from API (batched)
                progress?.Invoke(0, total, "Checking for updates...");
                var currentRevisions = await GetPagesRevisionInfoAsync(allQuests);

                foreach (var questName in allQuests)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Check if we have cached version with same revision
                    if (cacheIndex.TryGetValue(questName, out var cached) &&
                        currentRevisions.TryGetValue(questName, out var current) &&
                        cached.RevisionId == current.RevisionId &&
                        File.Exists(GetQuestPageFilePath(questName)))
                    {
                        skipped++;
                        processed++;
                        progress?.Invoke(processed, total, $"[Cached] {questName}");
                    }
                    else
                    {
                        toDownload.Add(questName);
                    }
                }
            }

            // Download pages in batches (50 per request) to avoid 503 errors
            if (toDownload.Count > 0)
            {
                progress?.Invoke(processed, total, $"Downloading {toDownload.Count} pages in batches...");

                for (int i = 0; i < toDownload.Count; i += MaxTitlesPerRequest)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = toDownload.Skip(i).Take(MaxTitlesPerRequest).ToList();
                    var batchNum = (i / MaxTitlesPerRequest) + 1;
                    var totalBatches = (int)Math.Ceiling(toDownload.Count / (double)MaxTitlesPerRequest);

                    progress?.Invoke(processed, total, $"[Batch {batchNum}/{totalBatches}] Downloading {batch.Count} pages...");

                    try
                    {
                        // Build mapping from wiki page name to quest name for this batch
                        // Note: MediaWiki API normalizes underscores to spaces in responses
                        var wikiPageToQuestName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var wikiPageNames = new List<string>();
                        foreach (var questName in batch)
                        {
                            var wikiPageName = NormalizedNameGenerator.GetWikiPageName(questName);
                            wikiPageNames.Add(wikiPageName);
                            // Map both underscore and space versions (API returns spaces)
                            wikiPageToQuestName[wikiPageName] = questName;
                            wikiPageToQuestName[wikiPageName.Replace("_", " ")] = questName;
                        }

                        var batchResults = await DownloadPagesBatchAsync(wikiPageNames, cancellationToken);

                        foreach (var questName in batch)
                        {
                            var wikiPageName = NormalizedNameGenerator.GetWikiPageName(questName);
                            var wikiPageNameWithSpace = wikiPageName.Replace("_", " ");
                            if (batchResults.TryGetValue(wikiPageName, out var pageData) ||
                                batchResults.TryGetValue(wikiPageNameWithSpace, out pageData))
                            {
                                var (content, pageId, revId, timestamp) = pageData;

                                // Save content to file (using quest name, not wiki page name)
                                await File.WriteAllTextAsync(GetQuestPageFilePath(questName), content, Encoding.UTF8, cancellationToken);

                                // Update cache index
                                cacheIndex[questName] = new WikiPageCacheEntry
                                {
                                    PageId = pageId,
                                    RevisionId = revId,
                                    Timestamp = timestamp,
                                    CachedAt = DateTime.UtcNow
                                };

                                downloaded++;
                                processed++;
                                progress?.Invoke(processed, total, $"[Downloaded] {questName}");
                            }
                            else
                            {
                                failed++;
                                failedQuests.Add(questName);
                                processed++;
                                progress?.Invoke(processed, total, $"[Not Found] {questName}");
                            }
                        }

                        // Small delay between batches to be respectful to the API
                        if (i + MaxTitlesPerRequest < toDownload.Count)
                        {
                            await Task.Delay(500, cancellationToken);
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        // If batch fails, mark all items in batch as failed
                        foreach (var questName in batch)
                        {
                            failed++;
                            failedQuests.Add(questName);
                            processed++;
                            progress?.Invoke(processed, total, $"[Failed] {questName}: {ex.Message}");
                        }
                    }
                }
            }

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

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
