using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for fetching and managing task data from tarkov.dev API
    /// Translations come from tarkov.dev, but reqKappa is parsed from wiki .wiki files
    /// </summary>
    public class TarkovDataService : IDisposable
    {
        private static TarkovDataService? _instance;
        public static TarkovDataService Instance => _instance ??= new TarkovDataService();

        private readonly HttpClient _httpClient;
        private const string GraphQLEndpoint = "https://api.tarkov.dev/graphql";
        private const string QuestPagesCacheDir = "QuestPages";
        private const string CacheMetadataFileName = "cache_metadata.json";
        private const int CacheValidityMinutes = 30;

        public TarkovDataService()
        {
            _httpClient = new HttpClient();
        }

        #region GraphQL Response DTOs

        private class GraphQLResponse<T>
        {
            [JsonPropertyName("data")]
            public T? Data { get; set; }

            [JsonPropertyName("errors")]
            public List<GraphQLError>? Errors { get; set; }
        }

        private class GraphQLError
        {
            [JsonPropertyName("message")]
            public string Message { get; set; } = string.Empty;
        }

        private class TasksData
        {
            [JsonPropertyName("tasks")]
            public List<ApiTask>? Tasks { get; set; }
        }

        private class ApiTask
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;

            [JsonPropertyName("kappaRequired")]
            public bool? KappaRequired { get; set; }

            [JsonPropertyName("trader")]
            public ApiTrader? Trader { get; set; }

            [JsonPropertyName("factionName")]
            public string? FactionName { get; set; }

            [JsonPropertyName("taskRequirements")]
            public List<ApiTaskRequirement>? TaskRequirements { get; set; }
        }

        private class ApiTrader
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }

        private class ApiTaskRequirement
        {
            [JsonPropertyName("task")]
            public ApiTaskRef? Task { get; set; }

            [JsonPropertyName("status")]
            public List<string>? Status { get; set; }
        }

        private class ApiTaskRef
        {
            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;
        }

        #endregion

        #region Missing Task DTO

        /// <summary>
        /// Record of a task that couldn't be matched with wiki
        /// </summary>
        public class MissingTask
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("nameWithoutPvp")]
            public string NameWithoutPvp { get; set; } = string.Empty;

            [JsonPropertyName("trader")]
            public string Trader { get; set; } = string.Empty;

            [JsonPropertyName("reason")]
            public string Reason { get; set; } = string.Empty;
        }

        #endregion

        /// <summary>
        /// Remove [PVP ZONE] suffix from task name for wiki matching
        /// </summary>
        private static string RemovePvpZoneSuffix(string name)
        {
            // Pattern: " [PVP ZONE]" at the end (case insensitive)
            return Regex.Replace(name, @"\s*\[PVP ZONE\]\s*$", "", RegexOptions.IgnoreCase).Trim();
        }

        /// <summary>
        /// Generate normalizedName from wiki quest name
        /// Uses NormalizedNameGenerator for consistency with previous/leadsTo references
        /// </summary>
        private static string GenerateNormalizedName(string wikiName)
        {
            return NormalizedNameGenerator.Generate(wikiName);
        }

        /// <summary>
        /// Normalize quest name for file matching
        /// Handles special characters that may be encoded differently
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
        /// Get wiki file path for a quest name with fallback matching
        /// </summary>
        private string? GetWikiFilePath(string questName)
        {
            var cacheDir = Path.Combine(AppEnv.CachePath, QuestPagesCacheDir);
            if (!Directory.Exists(cacheDir))
                return null;

            // Normalize the quest name
            var normalizedName = NormalizeQuestName(questName);

            // Try exact match with normalized name first
            var exactPath = Path.Combine(cacheDir, $"{normalizedName}.wiki");
            if (File.Exists(exactPath))
                return exactPath;

            // Try with HTML entity encoding for apostrophe (&#39;)
            var htmlEncodedName = normalizedName.Replace("'", "&#39;");
            var htmlEncodedPath = Path.Combine(cacheDir, $"{htmlEncodedName}.wiki");
            if (File.Exists(htmlEncodedPath))
                return htmlEncodedPath;

            // Try replacing hyphens with spaces (Half-Empty -> Half Empty)
            var noHyphenName = normalizedName.Replace("-", " ");
            var noHyphenPath = Path.Combine(cacheDir, $"{noHyphenName}.wiki");
            if (File.Exists(noHyphenPath))
                return noHyphenPath;

            // Try replacing spaces with hyphens
            var withHyphenName = normalizedName.Replace(" ", "-");
            var withHyphenPath = Path.Combine(cacheDir, $"{withHyphenName}.wiki");
            if (File.Exists(withHyphenPath))
                return withHyphenPath;

            return null;
        }

        /// <summary>
        /// Parse reqkappa from a .wiki file content
        /// Returns true if |reqkappa contains "Yes", false otherwise
        /// </summary>
        private bool ParseReqKappaFromWiki(string wikiContent)
        {
            // Pattern: |reqkappa = ... Yes ...
            // Example: |reqkappa     =<font color="red">Yes</font>
            var match = Regex.Match(wikiContent, @"\|reqkappa\s*=([^\|]*)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var value = match.Groups[1].Value;
                return value.Contains("Yes", StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        /// <summary>
        /// Execute a GraphQL query against tarkov.dev API
        /// </summary>
        private async Task<T?> ExecuteQueryAsync<T>(string query) where T : class
        {
            var requestBody = new { query };
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(GraphQLEndpoint, content);
            response.EnsureSuccessStatusCode();

            var responseBytes = await response.Content.ReadAsByteArrayAsync();
            var responseJson = Encoding.UTF8.GetString(responseBytes);

            var result = JsonSerializer.Deserialize<GraphQLResponse<T>>(responseJson);

            if (result?.Errors != null && result.Errors.Count > 0)
            {
                throw new Exception($"GraphQL Error: {string.Join(", ", result.Errors.Select(e => e.Message))}");
            }

            return result?.Data;
        }

        /// <summary>
        /// Fetch tasks in a specific language
        /// </summary>
        private async Task<List<ApiTask>> FetchTasksAsync(string lang)
        {
            var query = $@"{{
                tasks(lang: {lang}) {{
                    id
                    name
                    normalizedName
                    kappaRequired
                    factionName
                    trader {{ name }}
                    taskRequirements {{
                        task {{ normalizedName }}
                        status
                    }}
                }}
            }}";

            var data = await ExecuteQueryAsync<TasksData>(query);
            return data?.Tasks ?? new List<ApiTask>();
        }

        /// <summary>
        /// Fetch tasks from tarkov.dev API in EN, KO, JA, merge with wiki reqkappa data
        /// Also includes wiki-only quests that don't exist in tarkov.dev API
        /// </summary>
        /// <param name="progressCallback">Progress callback</param>
        /// <returns>Tuple of (matched tasks, missing tasks)</returns>
        public async Task<(List<TarkovTask> Tasks, List<MissingTask> MissingTasks)> FetchAndMergeTasksAsync(
            Action<string>? progressCallback = null)
        {
            progressCallback?.Invoke("Fetching English tasks...");
            var tasksEn = await FetchTasksAsync("en");

            progressCallback?.Invoke("Fetching Korean tasks...");
            var tasksKo = await FetchTasksAsync("ko");

            progressCallback?.Invoke("Fetching Japanese tasks...");
            var tasksJa = await FetchTasksAsync("ja");

            progressCallback?.Invoke("Loading items for item ID resolution...");
            var items = await TarkovDevApiService.Instance.LoadItemsFromJsonAsync();
            var itemIdToNormalizedName = items?
                .Where(i => !string.IsNullOrEmpty(i.Id) && !string.IsNullOrEmpty(i.NormalizedName))
                .ToDictionary(i => i.Id, i => i.NormalizedName)
                ?? new Dictionary<string, string>();

            progressCallback?.Invoke("Loading wiki quest list...");
            var wikiQuests = await WikiDataService.Instance.LoadQuestsFromJsonAsync();

            progressCallback?.Invoke("Merging task data with wiki reqkappa...");

            // Create lookup dictionaries for KO and JA by ID
            var koById = tasksKo.ToDictionary(t => t.Id, t => t.Name);
            var jaById = tasksJa.ToDictionary(t => t.Id, t => t.Name);

            var result = new List<TarkovTask>();
            var missingTasks = new List<MissingTask>();

            // Track which wiki quests were matched to API tasks
            var matchedWikiQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Group API tasks by wikiMatchName to handle BEAR/USEC variants with same quest
            // Some quests (e.g., Textile, Battery Change) have different IDs for same content
            var tasksByWikiName = tasksEn
                .Select(t => new { Task = t, WikiMatchName = RemovePvpZoneSuffix(t.Name) })
                .GroupBy(t => t.WikiMatchName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var taskGroup in tasksByWikiName)
            {
                var wikiMatchName = taskGroup.Key;
                var groupTasks = taskGroup.Select(g => g.Task).ToList();

                // Determine faction from API: prefer BEAR/USEC over Any
                // If all variants are "Any", the quest is for both factions
                // If mixed (e.g., one BEAR, one USEC), they're faction-specific duplicates - use API faction
                string? apiFaction = null;
                var factionNames = groupTasks
                    .Select(t => t.FactionName)
                    .Where(f => !string.IsNullOrEmpty(f) && !f.Equals("Any", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (factionNames.Count == 1)
                {
                    // All variants have same faction (e.g., all BEAR) - this is a faction-specific quest
                    apiFaction = factionNames[0].ToLowerInvariant();
                }
                else if (factionNames.Count > 1)
                {
                    // Mixed factions (BEAR and USEC variants) - this shouldn't happen for same wiki content
                    // Treat as faction-neutral (both factions can do it)
                    apiFaction = null;
                }
                // If factionNames.Count == 0, all are "Any" - faction is null (for both factions)

                // Use the first task for base data, but collect all IDs
                var firstTask = groupTasks[0];
                var allIds = groupTasks.Select(t => t.Id).Distinct().ToList();

                // Get translations from first task (they should be the same for all variants)
                var nameKo = koById.TryGetValue(firstTask.Id, out var ko) ? ko : null;
                var nameJa = jaById.TryGetValue(firstTask.Id, out var ja) ? ja : null;

                // Check if translation is same as English (means no translation available)
                if (nameKo == firstTask.Name) nameKo = null;
                if (nameJa == firstTask.Name) nameJa = null;

                var wikiFilePath = GetWikiFilePath(wikiMatchName);

                bool reqKappa = false;

                if (wikiFilePath != null)
                {
                    var wikiContent = await File.ReadAllTextAsync(wikiFilePath, Encoding.UTF8);
                    reqKappa = ParseReqKappaFromWiki(wikiContent);
                    matchedWikiQuests.Add(wikiMatchName);

                    // Parse additional data from wiki
                    var wikiData = WikiQuestParser.ParseAll(wikiContent);

                    // Use API faction if available, otherwise fall back to wiki faction
                    var finalFaction = apiFaction ?? wikiData.Faction;

                    result.Add(new TarkovTask
                    {
                        Ids = allIds,  // Merged IDs from all variants
                        Name = WebUtility.HtmlDecode(wikiMatchName),
                        NameKo = nameKo,
                        NameJa = nameJa,
                        ReqKappa = reqKappa,
                        Trader = firstTask.Trader?.Name ?? string.Empty,
                        NormalizedName = NormalizedNameGenerator.ApplyQuestOverride(
                            GenerateNormalizedName(wikiMatchName)),
                        Maps = wikiData.Maps,
                        Previous = wikiData.Previous,
                        LeadsTo = wikiData.LeadsTo,
                        RequiredLevel = wikiData.RequiredLevel,
                        RequiredScavKarma = wikiData.RequiredScavKarma,
                        RequiredSkills = wikiData.RequiredSkills,
                        RequiredItems = ResolveItemIds(wikiData.RequiredItems, itemIdToNormalizedName),
                        Objectives = wikiData.Objectives,
                        GuideText = wikiData.GuideText,
                        GuideImages = wikiData.GuideImages?.Select(g => new GuideImage
                        {
                            FileName = g.FileName,
                            Caption = g.Caption
                        }).ToList(),
                        Faction = finalFaction,
                        AlternativeQuests = wikiData.AlternativeQuests,
                        // API taskRequirements with status conditions (active/complete)
                        TaskRequirements = ConvertTaskRequirements(firstTask.TaskRequirements)
                    });
                }
                else
                {
                    // Add missing task for each ID in the group
                    foreach (var task in groupTasks)
                    {
                        missingTasks.Add(new MissingTask
                        {
                            Id = task.Id,
                            Name = task.Name,
                            NameWithoutPvp = wikiMatchName,
                            Trader = task.Trader?.Name ?? string.Empty,
                            Reason = $"Wiki file not found: {wikiMatchName}.wiki"
                        });
                    }
                }
            }

            // Add wiki-only quests that don't exist in tarkov.dev API
            if (wikiQuests != null)
            {
                progressCallback?.Invoke("Adding wiki-only quests...");
                int wikiOnlyCount = 0;

                foreach (var (trader, quests) in wikiQuests)
                {
                    foreach (var quest in quests)
                    {
                        // Skip if already matched with API task
                        if (matchedWikiQuests.Contains(quest.Name))
                            continue;

                        // Check if wiki file exists for this quest
                        var wikiFilePath = GetWikiFilePath(quest.Name);
                        bool reqKappa = false;
                        WikiQuestData? wikiData = null;

                        if (wikiFilePath != null)
                        {
                            var wikiContent = await File.ReadAllTextAsync(wikiFilePath, Encoding.UTF8);
                            reqKappa = ParseReqKappaFromWiki(wikiContent);
                            wikiData = WikiQuestParser.ParseAll(wikiContent);
                        }

                        // Add as wiki-only task (no API id, no translations)
                        result.Add(new TarkovTask
                        {
                            Ids = null, // No API ID
                            Name = WebUtility.HtmlDecode(quest.Name),
                            NameKo = null, // No translation without API
                            NameJa = null,
                            ReqKappa = reqKappa,
                            Trader = trader,
                            NormalizedName = NormalizedNameGenerator.ApplyQuestOverride(
                                GenerateNormalizedName(quest.Name)),  // Generate from wiki name with quest overrides
                            Maps = wikiData?.Maps,
                            Previous = wikiData?.Previous,
                            LeadsTo = wikiData?.LeadsTo,
                            RequiredLevel = wikiData?.RequiredLevel,
                            RequiredScavKarma = wikiData?.RequiredScavKarma,
                            RequiredSkills = wikiData?.RequiredSkills,
                            RequiredItems = ResolveItemIds(wikiData?.RequiredItems, itemIdToNormalizedName),
                            Objectives = wikiData?.Objectives,
                            GuideText = wikiData?.GuideText,
                            GuideImages = wikiData?.GuideImages?.Select(g => new GuideImage
                            {
                                FileName = g.FileName,
                                Caption = g.Caption
                            }).ToList(),
                            Faction = wikiData?.Faction,
                            AlternativeQuests = wikiData?.AlternativeQuests
                        });

                        wikiOnlyCount++;
                        matchedWikiQuests.Add(quest.Name);
                    }
                }

                progressCallback?.Invoke($"Added {wikiOnlyCount} wiki-only quests");
            }

            progressCallback?.Invoke($"Merged {result.Count} tasks total, {missingTasks.Count} API tasks without wiki");

            // Build bidirectional relationships (leadsTo -> previous)
            BuildBidirectionalRelationships(result);
            progressCallback?.Invoke("Built bidirectional quest relationships");

            // Setup Collector quest prerequisites (reqKappa quests)
            SetupCollectorQuestPrerequisites(result);
            progressCallback?.Invoke("Setup Collector quest prerequisites");

            return (result, missingTasks);
        }

        /// <summary>
        /// Convert API taskRequirements to model TaskRequirements
        /// </summary>
        private static List<TaskRequirement>? ConvertTaskRequirements(List<ApiTaskRequirement>? apiRequirements)
        {
            if (apiRequirements == null || apiRequirements.Count == 0)
                return null;

            var result = new List<TaskRequirement>();
            foreach (var req in apiRequirements)
            {
                if (req.Task == null || string.IsNullOrEmpty(req.Task.NormalizedName))
                    continue;

                result.Add(new TaskRequirement
                {
                    TaskNormalizedName = req.Task.NormalizedName,
                    Status = req.Status
                });
            }

            return result.Count > 0 ? result : null;
        }

        /// <summary>
        /// Resolve item IDs in required items to normalized names
        /// WikiQuestParser stores item IDs as "id:itemId" when wiki uses {{itemId}} template format
        /// </summary>
        private static List<QuestItem>? ResolveItemIds(List<QuestItem>? items, Dictionary<string, string> itemIdToNormalizedName)
        {
            if (items == null || items.Count == 0)
                return items;

            var resolved = new List<QuestItem>();
            foreach (var item in items)
            {
                if (item.ItemNormalizedName.StartsWith("id:"))
                {
                    // Extract item ID and look up normalized name
                    var itemId = item.ItemNormalizedName.Substring(3);
                    if (itemIdToNormalizedName.TryGetValue(itemId, out var normalizedName))
                    {
                        resolved.Add(new QuestItem
                        {
                            ItemNormalizedName = normalizedName,
                            Amount = item.Amount,
                            Requirement = item.Requirement,
                            FoundInRaid = item.FoundInRaid
                        });
                    }
                    // Skip items with unknown IDs
                }
                else
                {
                    resolved.Add(item);
                }
            }

            return resolved.Count > 0 ? resolved : null;
        }

        /// <summary>
        /// Setup Collector quest prerequisites dynamically
        /// Collector quest requires all reqKappa == true quests to be completed
        /// </summary>
        private static void SetupCollectorQuestPrerequisites(List<TarkovTask> tasks)
        {
            // Find Collector quest
            var collectorQuest = tasks.FirstOrDefault(t =>
                t.NormalizedName?.Equals("collector", StringComparison.OrdinalIgnoreCase) == true);

            if (collectorQuest == null)
                return;

            // Collect all reqKappa quests (excluding Collector itself)
            var kappaRequiredQuests = tasks
                .Where(t => t.ReqKappa &&
                            !t.NormalizedName?.Equals("collector", StringComparison.OrdinalIgnoreCase) == true)
                .Select(t => t.NormalizedName!)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            // Set Previous field (merge with existing)
            collectorQuest.Previous ??= new List<string>();
            foreach (var questName in kappaRequiredQuests)
            {
                if (!collectorQuest.Previous.Contains(questName, StringComparer.OrdinalIgnoreCase))
                {
                    collectorQuest.Previous.Add(questName);
                }
            }
        }

        /// <summary>
        /// Build bidirectional relationships between quests
        /// If A.leadsTo contains B, then B.previous should contain A
        /// This handles cases where wiki uses "related2" (Requirement for) instead of proper previous/leadsTo
        /// </summary>
        private static void BuildBidirectionalRelationships(List<TarkovTask> tasks)
        {
            // Create lookup dictionary (handle duplicates by keeping first occurrence)
            var tasksByName = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in tasks.Where(t => !string.IsNullOrEmpty(t.NormalizedName)))
            {
                tasksByName.TryAdd(task.NormalizedName!, task);
            }

            foreach (var task in tasks)
            {
                if (task.LeadsTo == null || string.IsNullOrEmpty(task.NormalizedName))
                    continue;

                foreach (var nextQuestName in task.LeadsTo)
                {
                    if (tasksByName.TryGetValue(nextQuestName, out var nextTask))
                    {
                        // Add current task to nextTask's previous list if not already there
                        nextTask.Previous ??= new List<string>();
                        if (!nextTask.Previous.Contains(task.NormalizedName, StringComparer.OrdinalIgnoreCase))
                        {
                            nextTask.Previous.Add(task.NormalizedName);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Save tasks to JSON file
        /// </summary>
        public async Task SaveTasksToJsonAsync(List<TarkovTask> tasks, string? fileName = null)
        {
            fileName ??= "tasks.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);
            Directory.CreateDirectory(AppEnv.DataPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(tasks, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Save missing tasks to JSON file
        /// </summary>
        public async Task SaveMissingTasksToJsonAsync(List<MissingTask> missingTasks, string? fileName = null)
        {
            fileName ??= "tasks_missing.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);
            Directory.CreateDirectory(AppEnv.DataPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(missingTasks, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Load tasks from JSON file
        /// </summary>
        public async Task<List<TarkovTask>?> LoadTasksFromJsonAsync(string? fileName = null)
        {
            fileName ??= "tasks.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<TarkovTask>>(json);
        }

        /// <summary>
        /// Load missing tasks from JSON file
        /// </summary>
        public async Task<List<MissingTask>?> LoadMissingTasksFromJsonAsync(string? fileName = null)
        {
            fileName ??= "tasks_missing.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<MissingTask>>(json);
        }

        #region Cache Lifecycle Management

        /// <summary>
        /// Load cache metadata from JSON file
        /// </summary>
        public async Task<DataCacheMetadata?> LoadCacheMetadataAsync()
        {
            var filePath = Path.Combine(AppEnv.DataPath, CacheMetadataFileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                return JsonSerializer.Deserialize<DataCacheMetadata>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Save cache metadata to JSON file
        /// </summary>
        public async Task SaveCacheMetadataAsync(DataCacheMetadata metadata)
        {
            var filePath = Path.Combine(AppEnv.DataPath, CacheMetadataFileName);
            Directory.CreateDirectory(AppEnv.DataPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(metadata, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Check if cached data is still valid (within 30 minutes)
        /// </summary>
        /// <returns>Tuple of (isValid, metadata, remainingMinutes)</returns>
        public async Task<(bool IsValid, DataCacheMetadata? Metadata, int RemainingMinutes)> IsCacheValidAsync()
        {
            var metadata = await LoadCacheMetadataAsync();

            if (metadata == null)
            {
                return (false, null, 0);
            }

            var elapsed = DateTime.UtcNow - metadata.LastRefreshTime;
            var remainingMinutes = CacheValidityMinutes - (int)elapsed.TotalMinutes;

            if (remainingMinutes > 0)
            {
                // Cache is still valid
                return (true, metadata, remainingMinutes);
            }

            // Cache has expired
            return (false, metadata, 0);
        }

        /// <summary>
        /// Get cache status information for display
        /// </summary>
        public async Task<string> GetCacheStatusAsync()
        {
            var (isValid, metadata, remainingMinutes) = await IsCacheValidAsync();

            if (metadata == null)
            {
                return "No cached data available";
            }

            var localTime = metadata.LastRefreshTime.ToLocalTime();
            var ageMinutes = (int)(DateTime.UtcNow - metadata.LastRefreshTime).TotalMinutes;

            if (isValid)
            {
                return $"Cache valid ({remainingMinutes}min remaining) - Last refresh: {localTime:HH:mm:ss}";
            }
            else
            {
                return $"Cache expired ({ageMinutes}min ago) - Last refresh: {localTime:HH:mm:ss}";
            }
        }

        #endregion

        /// <summary>
        /// Fetch, merge, and save tasks in one call
        /// </summary>
        public async Task<(List<TarkovTask> Tasks, List<MissingTask> MissingTasks)> RefreshTasksDataAsync(
            Action<string>? progressCallback = null)
        {
            var (tasks, missingTasks) = await FetchAndMergeTasksAsync(progressCallback);

            await SaveTasksToJsonAsync(tasks);
            progressCallback?.Invoke($"Saved {tasks.Count} tasks to tasks.json");

            if (missingTasks.Count > 0)
            {
                await SaveMissingTasksToJsonAsync(missingTasks);
                progressCallback?.Invoke($"Saved {missingTasks.Count} missing tasks to tasks_missing.json");
            }

            return (tasks, missingTasks);
        }

        /// <summary>
        /// Complete data refresh: WikiQuestData download, Quest Pages fetch, and API merge
        /// This is the main entry point for refreshing all task data
        /// </summary>
        /// <param name="progressCallback">Progress callback for UI updates</param>
        /// <param name="forceRefresh">Force refresh even if cache is valid</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result summary</returns>
        public async Task<RefreshDataResult> RefreshAllDataAsync(
            Action<string>? progressCallback = null,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var result = new RefreshDataResult();
            var wikiService = WikiDataService.Instance;
            var masterDataService = TarkovDevApiService.Instance;

            try
            {
                // Check cache validity first (unless force refresh)
                if (!forceRefresh)
                {
                    var (isCacheValid, cacheMetadata, remainingMinutes) = await IsCacheValidAsync();

                    if (isCacheValid && cacheMetadata != null)
                    {
                        progressCallback?.Invoke($"[Cache] Using cached data ({remainingMinutes}min remaining until expiry)");

                        // Load cached data and return
                        var cachedTasks = await LoadTasksFromJsonAsync();
                        var cachedItems = await TarkovDevApiService.Instance.LoadItemsFromJsonAsync();
                        var cachedSkills = await TarkovDevApiService.Instance.LoadSkillsFromJsonAsync();

                        if (cachedTasks != null)
                        {
                            result.Success = true;
                            result.UsedCache = true;
                            result.CacheTimestamp = cacheMetadata.LastRefreshTime;
                            result.TotalTasksMerged = cachedTasks.Count;
                            result.TasksWithApiId = cachedTasks.Count(t => t.Ids != null && t.Ids.Count > 0);
                            result.WikiOnlyTasks = cachedTasks.Count(t => t.Ids == null || t.Ids.Count == 0);
                            result.KappaRequiredTasks = cachedTasks.Count(t => t.ReqKappa);
                            result.ItemCount = cachedItems?.Count ?? 0;
                            result.SkillCount = cachedSkills?.Count ?? 0;

                            var localTime = cacheMetadata.LastRefreshTime.ToLocalTime();
                            progressCallback?.Invoke($"[Cache] Loaded {result.TotalTasksMerged} tasks, {result.ItemCount} items (Last refresh: {localTime:HH:mm:ss})");
                            return result;
                        }

                        // Cache file missing, proceed with refresh
                        progressCallback?.Invoke("[Cache] Cache metadata exists but data files missing, refreshing...");
                    }
                    else
                    {
                        var reason = cacheMetadata == null ? "No cache" : "Cache expired";
                        progressCallback?.Invoke($"[Cache] {reason}, fetching fresh data...");
                    }
                }
                else
                {
                    progressCallback?.Invoke("[Cache] Force refresh requested...");
                }

                // Step 1: Fetch master data (items, skills) from tarkov.dev API
                progressCallback?.Invoke("[1/4] Fetching master data from tarkov.dev API...");
                var masterDataResult = await masterDataService.RefreshMasterDataAsync(msg =>
                {
                    progressCallback?.Invoke($"[1/4] {msg}");
                });

                if (!masterDataResult.Success)
                {
                    throw new Exception($"Failed to fetch master data: {masterDataResult.ErrorMessage}");
                }

                result.ItemCount = masterDataResult.ItemCount;
                result.ItemsWithKorean = masterDataResult.ItemsWithKorean;
                result.ItemsWithJapanese = masterDataResult.ItemsWithJapanese;
                result.SkillCount = masterDataResult.SkillCount;
                result.SkillsWithKorean = masterDataResult.SkillsWithKorean;
                result.SkillsWithJapanese = masterDataResult.SkillsWithJapanese;
                progressCallback?.Invoke($"[1/4] Fetched {masterDataResult.ItemCount} items, {masterDataResult.SkillCount} skills");

                cancellationToken.ThrowIfCancellationRequested();

                // Step 2: Download WikiQuestData (quest list from wiki)
                progressCallback?.Invoke("[2/4] Downloading Wiki quest list...");
                var quests = await wikiService.RefreshQuestsDataAsync();
                result.TotalQuestsInWiki = quests.Values.Sum(q => q.Count);
                progressCallback?.Invoke($"[2/4] Downloaded {result.TotalQuestsInWiki} quests from wiki");

                cancellationToken.ThrowIfCancellationRequested();

                // Step 3: Fetch all Quest Pages (.wiki files) using Special:Export (fast, no 503 errors)
                progressCallback?.Invoke("[3/4] Downloading quest pages via Special:Export...");
                var (downloaded, skipped, failed, failedQuests) = await wikiService.DownloadAllQuestPagesViaExportAsync(
                    forceDownload: false,
                    progress: (current, total, message) =>
                    {
                        progressCallback?.Invoke($"[3/4] ({current}/{total}) {message}");
                    },
                    cancellationToken: cancellationToken);

                progressCallback?.Invoke($"[3/4] Export: {downloaded} downloaded, {skipped} cached, {failed} not found");

                // Step 3b: Retry failed pages using MediaWiki API (Special:Export has page limits on Fandom)
                if (failedQuests.Count > 0)
                {
                    progressCallback?.Invoke($"[3/4] Retrying {failedQuests.Count} pages via MediaWiki API...");
                    var (apiDownloaded, apiFailed, apiFailedQuests) = await wikiService.DownloadSpecificQuestPagesAsync(
                        failedQuests,
                        progress: (current, total, message) =>
                        {
                            progressCallback?.Invoke($"[3/4] API ({current}/{total}) {message}");
                        },
                        cancellationToken: cancellationToken);

                    downloaded += apiDownloaded;
                    failed = apiFailed;  // Update to final failure count
                    failedQuests = apiFailedQuests;  // Update to final failed list
                    progressCallback?.Invoke($"[3/4] API retry: {apiDownloaded} downloaded, {apiFailed} not found");
                }

                result.QuestPagesDownloaded = downloaded;
                result.QuestPagesSkipped = skipped;
                result.QuestPagesFailed = failed;
                result.FailedQuestPages = failedQuests;
                progressCallback?.Invoke($"[3/4] Quest pages: {downloaded} downloaded, {skipped} skipped, {failed} failed");

                cancellationToken.ThrowIfCancellationRequested();

                // Step 4: Merge with tarkov.dev API
                progressCallback?.Invoke("[4/4] Fetching and merging with tarkov.dev API...");
                var (tasks, missingTasks) = await RefreshTasksDataAsync(message =>
                {
                    progressCallback?.Invoke($"[4/4] {message}");
                });

                result.TotalTasksMerged = tasks.Count;
                result.TasksWithApiId = tasks.Count(t => t.Ids != null && t.Ids.Count > 0);
                result.WikiOnlyTasks = tasks.Count(t => t.Ids == null || t.Ids.Count == 0);
                result.KappaRequiredTasks = tasks.Count(t => t.ReqKappa);
                result.MissingApiTasks = missingTasks.Count;

                // Save cache metadata for lifecycle management
                var newCacheMetadata = new DataCacheMetadata
                {
                    LastRefreshTime = DateTime.UtcNow,
                    TaskCount = result.TotalTasksMerged,
                    ItemCount = result.ItemCount,
                    SkillCount = result.SkillCount
                };
                await SaveCacheMetadataAsync(newCacheMetadata);
                result.CacheTimestamp = newCacheMetadata.LastRefreshTime;

                progressCallback?.Invoke($"[Complete] {result.TotalTasksMerged} tasks, {result.ItemCount} items, {result.SkillCount} skills");
                result.Success = true;
            }
            catch (OperationCanceledException)
            {
                progressCallback?.Invoke("[Cancelled] Data refresh was cancelled");
                result.ErrorMessage = "Operation cancelled";
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"[Error] {ex.Message}");
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Result of RefreshAllDataAsync operation
    /// </summary>
    public class RefreshDataResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        // Cache status
        public bool UsedCache { get; set; }
        public DateTime? CacheTimestamp { get; set; }

        // Master data stats (items/skills)
        public int ItemCount { get; set; }
        public int ItemsWithKorean { get; set; }
        public int ItemsWithJapanese { get; set; }
        public int SkillCount { get; set; }
        public int SkillsWithKorean { get; set; }
        public int SkillsWithJapanese { get; set; }

        // Wiki quest list stats
        public int TotalQuestsInWiki { get; set; }

        // Quest pages download stats
        public int QuestPagesDownloaded { get; set; }
        public int QuestPagesSkipped { get; set; }
        public int QuestPagesFailed { get; set; }
        public List<string> FailedQuestPages { get; set; } = new();

        // Merged tasks stats
        public int TotalTasksMerged { get; set; }
        public int TasksWithApiId { get; set; }
        public int WikiOnlyTasks { get; set; }
        public int KappaRequiredTasks { get; set; }
        public int MissingApiTasks { get; set; }
    }

    /// <summary>
    /// Metadata for data cache lifecycle management
    /// </summary>
    public class DataCacheMetadata
    {
        [JsonPropertyName("lastRefreshTime")]
        public DateTime LastRefreshTime { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("taskCount")]
        public int TaskCount { get; set; }

        [JsonPropertyName("itemCount")]
        public int ItemCount { get; set; }

        [JsonPropertyName("skillCount")]
        public int SkillCount { get; set; }
    }
}
