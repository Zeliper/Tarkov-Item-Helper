using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for fetching master data (items, skills) from tarkov.dev API
    /// Handles multilingual data fetching and JSON persistence
    /// </summary>
    public class TarkovDevApiService : IDisposable
    {
        private static TarkovDevApiService? _instance;
        public static TarkovDevApiService Instance => _instance ??= new TarkovDevApiService();

        private readonly HttpClient _httpClient;
        private const string GraphQLEndpoint = "https://api.tarkov.dev/graphql";

        public TarkovDevApiService()
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

        private class ItemsData
        {
            [JsonPropertyName("items")]
            public List<ApiItem>? Items { get; set; }
        }

        private class ApiItem
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;

            [JsonPropertyName("shortName")]
            public string? ShortName { get; set; }

            [JsonPropertyName("iconLink")]
            public string? IconLink { get; set; }

            [JsonPropertyName("gridImageLink")]
            public string? GridImageLink { get; set; }

            [JsonPropertyName("wikiLink")]
            public string? WikiLink { get; set; }
        }

        private class SkillsData
        {
            [JsonPropertyName("skills")]
            public List<ApiSkill>? Skills { get; set; }
        }

        private class ApiSkill
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("imageLink")]
            public string? ImageLink { get; set; }
        }

        private class TradersData
        {
            [JsonPropertyName("traders")]
            public List<ApiTrader>? Traders { get; set; }
        }

        private class ApiTrader
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;

            [JsonPropertyName("imageLink")]
            public string? ImageLink { get; set; }

            [JsonPropertyName("image4xLink")]
            public string? Image4xLink { get; set; }
        }

        private class MapsData
        {
            [JsonPropertyName("maps")]
            public List<ApiMap>? Maps { get; set; }
        }

        private class ApiMap
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;

            [JsonPropertyName("wiki")]
            public string? Wiki { get; set; }
        }

        private class HideoutStationsData
        {
            [JsonPropertyName("hideoutStations")]
            public List<ApiHideoutStation>? HideoutStations { get; set; }
        }

        private class ApiHideoutStation
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;

            [JsonPropertyName("imageLink")]
            public string? ImageLink { get; set; }

            [JsonPropertyName("levels")]
            public List<ApiHideoutLevel>? Levels { get; set; }
        }

        private class ApiHideoutLevel
        {
            [JsonPropertyName("level")]
            public int Level { get; set; }

            [JsonPropertyName("constructionTime")]
            public int ConstructionTime { get; set; }

            [JsonPropertyName("itemRequirements")]
            public List<ApiHideoutItemRequirement>? ItemRequirements { get; set; }

            [JsonPropertyName("stationLevelRequirements")]
            public List<ApiHideoutStationRequirement>? StationLevelRequirements { get; set; }

            [JsonPropertyName("traderRequirements")]
            public List<ApiHideoutTraderRequirement>? TraderRequirements { get; set; }

            [JsonPropertyName("skillRequirements")]
            public List<ApiHideoutSkillRequirement>? SkillRequirements { get; set; }
        }

        private class ApiHideoutItemRequirement
        {
            [JsonPropertyName("item")]
            public ApiHideoutItem? Item { get; set; }

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("attributes")]
            public List<ApiHideoutAttribute>? Attributes { get; set; }
        }

        private class ApiHideoutAttribute
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("value")]
            public string Value { get; set; } = string.Empty;
        }

        private class ApiHideoutItem
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;

            [JsonPropertyName("iconLink")]
            public string? IconLink { get; set; }
        }

        private class ApiHideoutStationRequirement
        {
            [JsonPropertyName("station")]
            public ApiHideoutStationRef? Station { get; set; }

            [JsonPropertyName("level")]
            public int Level { get; set; }
        }

        private class ApiHideoutStationRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("normalizedName")]
            public string NormalizedName { get; set; } = string.Empty;
        }

        private class ApiHideoutTraderRequirement
        {
            [JsonPropertyName("trader")]
            public ApiHideoutTraderRef? Trader { get; set; }

            [JsonPropertyName("level")]
            public int Level { get; set; }
        }

        private class ApiHideoutTraderRef
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;
        }

        private class ApiHideoutSkillRequirement
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("level")]
            public int Level { get; set; }
        }

        #endregion

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

        #region Items API

        /// <summary>
        /// Fetch items in a specific language
        /// </summary>
        private async Task<List<ApiItem>> FetchItemsAsync(string lang)
        {
            var query = $@"{{
                items(lang: {lang}) {{
                    id
                    name
                    normalizedName
                    shortName
                    iconLink
                    gridImageLink
                    wikiLink
                }}
            }}";

            var data = await ExecuteQueryAsync<ItemsData>(query);
            return data?.Items ?? new List<ApiItem>();
        }

        /// <summary>
        /// Fetch all items from tarkov.dev API with EN/KO/JA translations
        /// </summary>
        /// <param name="progressCallback">Progress callback for UI updates</param>
        /// <returns>List of items with multilingual names</returns>
        public async Task<List<TarkovItem>> FetchItemsAsync(Action<string>? progressCallback = null)
        {
            progressCallback?.Invoke("Fetching English items...");
            var itemsEn = await FetchItemsAsync("en");

            progressCallback?.Invoke("Fetching Korean items...");
            var itemsKo = await FetchItemsAsync("ko");

            progressCallback?.Invoke("Fetching Japanese items...");
            var itemsJa = await FetchItemsAsync("ja");

            progressCallback?.Invoke($"Merging {itemsEn.Count} items...");

            // Create lookup dictionaries for KO and JA by ID
            var koById = itemsKo.ToDictionary(i => i.Id, i => i.Name);
            var jaById = itemsJa.ToDictionary(i => i.Id, i => i.Name);

            var result = new List<TarkovItem>();

            foreach (var itemEn in itemsEn)
            {
                var nameKo = koById.TryGetValue(itemEn.Id, out var ko) ? ko : null;
                var nameJa = jaById.TryGetValue(itemEn.Id, out var ja) ? ja : null;

                // Check if translation is same as English (means no translation available)
                if (nameKo == itemEn.Name) nameKo = null;
                if (nameJa == itemEn.Name) nameJa = null;

                result.Add(new TarkovItem
                {
                    Id = itemEn.Id,
                    Name = itemEn.Name,
                    NameKo = nameKo,
                    NameJa = nameJa,
                    ShortName = itemEn.ShortName,
                    NormalizedName = itemEn.NormalizedName,
                    IconLink = itemEn.IconLink,
                    GridImageLink = itemEn.GridImageLink,
                    WikiLink = itemEn.WikiLink
                });
            }

            progressCallback?.Invoke($"Fetched {result.Count} items with translations");
            return result;
        }

        /// <summary>
        /// Save items to JSON file
        /// </summary>
        public async Task SaveItemsToJsonAsync(List<TarkovItem> items, string? fileName = null)
        {
            fileName ??= "items.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);
            Directory.CreateDirectory(AppEnv.DataPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(items, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Load items from JSON file
        /// </summary>
        public async Task<List<TarkovItem>?> LoadItemsFromJsonAsync(string? fileName = null)
        {
            fileName ??= "items.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<TarkovItem>>(json);
        }

        #endregion

        #region Skills API

        /// <summary>
        /// Fetch skills in a specific language
        /// </summary>
        private async Task<List<ApiSkill>> FetchSkillsApiAsync(string lang)
        {
            var query = $@"{{
                skills(lang: {lang}) {{
                    id
                    name
                    imageLink
                }}
            }}";

            var data = await ExecuteQueryAsync<SkillsData>(query);
            return data?.Skills ?? new List<ApiSkill>();
        }

        /// <summary>
        /// Fetch all skills from tarkov.dev API with EN/KO/JA translations
        /// </summary>
        /// <param name="progressCallback">Progress callback for UI updates</param>
        /// <returns>List of skills with multilingual names</returns>
        public async Task<List<TarkovSkill>> FetchSkillsAsync(Action<string>? progressCallback = null)
        {
            progressCallback?.Invoke("Fetching English skills...");
            var skillsEn = await FetchSkillsApiAsync("en");

            progressCallback?.Invoke("Fetching Korean skills...");
            var skillsKo = await FetchSkillsApiAsync("ko");

            progressCallback?.Invoke("Fetching Japanese skills...");
            var skillsJa = await FetchSkillsApiAsync("ja");

            progressCallback?.Invoke($"Merging {skillsEn.Count} skills...");

            // Create lookup dictionaries for KO and JA by ID
            var koById = skillsKo.ToDictionary(s => s.Id, s => s.Name);
            var jaById = skillsJa.ToDictionary(s => s.Id, s => s.Name);

            var result = new List<TarkovSkill>();

            foreach (var skillEn in skillsEn)
            {
                var nameKo = koById.TryGetValue(skillEn.Id, out var ko) ? ko : null;
                var nameJa = jaById.TryGetValue(skillEn.Id, out var ja) ? ja : null;

                // Check if translation is same as English (means no translation available)
                if (nameKo == skillEn.Name) nameKo = null;
                if (nameJa == skillEn.Name) nameJa = null;

                result.Add(new TarkovSkill
                {
                    Id = skillEn.Id,
                    Name = skillEn.Name,
                    NameKo = nameKo,
                    NameJa = nameJa,
                    NormalizedName = NormalizedNameGenerator.Generate(skillEn.Name),
                    ImageLink = skillEn.ImageLink
                });
            }

            progressCallback?.Invoke($"Fetched {result.Count} skills with translations");
            return result;
        }

        /// <summary>
        /// Save skills to JSON file
        /// </summary>
        public async Task SaveSkillsToJsonAsync(List<TarkovSkill> skills, string? fileName = null)
        {
            fileName ??= "skills.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);
            Directory.CreateDirectory(AppEnv.DataPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(skills, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Load skills from JSON file
        /// </summary>
        public async Task<List<TarkovSkill>?> LoadSkillsFromJsonAsync(string? fileName = null)
        {
            fileName ??= "skills.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<TarkovSkill>>(json);
        }

        #endregion

        #region Traders API

        /// <summary>
        /// Fetch traders in a specific language
        /// </summary>
        private async Task<List<ApiTrader>> FetchTradersApiAsync(string lang)
        {
            var query = $@"{{
                traders(lang: {lang}) {{
                    id
                    name
                    normalizedName
                    imageLink
                    image4xLink
                }}
            }}";

            var data = await ExecuteQueryAsync<TradersData>(query);
            return data?.Traders ?? new List<ApiTrader>();
        }

        /// <summary>
        /// Fetch all traders from tarkov.dev API with EN/KO/JA translations
        /// </summary>
        /// <param name="progressCallback">Progress callback for UI updates</param>
        /// <returns>List of traders with multilingual names</returns>
        public async Task<List<TarkovTrader>> FetchTradersAsync(Action<string>? progressCallback = null)
        {
            progressCallback?.Invoke("Fetching English traders...");
            var tradersEn = await FetchTradersApiAsync("en");

            progressCallback?.Invoke("Fetching Korean traders...");
            var tradersKo = await FetchTradersApiAsync("ko");

            progressCallback?.Invoke("Fetching Japanese traders...");
            var tradersJa = await FetchTradersApiAsync("ja");

            progressCallback?.Invoke($"Merging {tradersEn.Count} traders...");

            // Create lookup dictionaries for KO and JA by ID
            var koById = tradersKo.ToDictionary(t => t.Id, t => t.Name);
            var jaById = tradersJa.ToDictionary(t => t.Id, t => t.Name);

            var result = new List<TarkovTrader>();

            foreach (var traderEn in tradersEn)
            {
                var nameKo = koById.TryGetValue(traderEn.Id, out var ko) ? ko : null;
                var nameJa = jaById.TryGetValue(traderEn.Id, out var ja) ? ja : null;

                // Check if translation is same as English (means no translation available)
                if (nameKo == traderEn.Name) nameKo = null;
                if (nameJa == traderEn.Name) nameJa = null;

                result.Add(new TarkovTrader
                {
                    Id = traderEn.Id,
                    Name = traderEn.Name,
                    NameKo = nameKo,
                    NameJa = nameJa,
                    NormalizedName = traderEn.NormalizedName,
                    ImageLink = traderEn.ImageLink,
                    Image4xLink = traderEn.Image4xLink
                });
            }

            progressCallback?.Invoke($"Fetched {result.Count} traders with translations");
            return result;
        }

        /// <summary>
        /// Save traders to JSON file
        /// </summary>
        public async Task SaveTradersToJsonAsync(List<TarkovTrader> traders, string? fileName = null)
        {
            fileName ??= "traders.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);
            Directory.CreateDirectory(AppEnv.DataPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(traders, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Load traders from bundled Assets JSON file
        /// </summary>
        public async Task<List<TarkovTrader>?> LoadTradersFromJsonAsync(string? fileName = null)
        {
            fileName ??= "traders.json";

            // Load from bundled Assets folder
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(appDir, "Assets", fileName);

            if (!File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[TarkovDevApiService] Traders file not found: {filePath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<TarkovTrader>>(json);
        }

        #endregion

        #region Maps API

        /// <summary>
        /// Fetch maps in a specific language
        /// </summary>
        private async Task<List<ApiMap>> FetchMapsApiAsync(string lang)
        {
            var query = $@"{{
                maps(lang: {lang}) {{
                    id
                    name
                    normalizedName
                    wiki
                }}
            }}";

            var data = await ExecuteQueryAsync<MapsData>(query);
            return data?.Maps ?? new List<ApiMap>();
        }

        /// <summary>
        /// Fetch all maps from tarkov.dev API with EN/KO/JA translations
        /// </summary>
        /// <param name="progressCallback">Progress callback for UI updates</param>
        /// <returns>List of maps with multilingual names</returns>
        public async Task<List<TarkovMap>> FetchMapsAsync(Action<string>? progressCallback = null)
        {
            progressCallback?.Invoke("Fetching English maps...");
            var mapsEn = await FetchMapsApiAsync("en");

            progressCallback?.Invoke("Fetching Korean maps...");
            var mapsKo = await FetchMapsApiAsync("ko");

            progressCallback?.Invoke("Fetching Japanese maps...");
            var mapsJa = await FetchMapsApiAsync("ja");

            progressCallback?.Invoke($"Merging {mapsEn.Count} maps...");

            // Create lookup dictionaries for KO and JA by ID
            var koById = mapsKo.ToDictionary(m => m.Id, m => m.Name);
            var jaById = mapsJa.ToDictionary(m => m.Id, m => m.Name);

            var result = new List<TarkovMap>();

            foreach (var mapEn in mapsEn)
            {
                var nameKo = koById.TryGetValue(mapEn.Id, out var ko) ? ko : null;
                var nameJa = jaById.TryGetValue(mapEn.Id, out var ja) ? ja : null;

                // Check if translation is same as English (means no translation available)
                if (nameKo == mapEn.Name) nameKo = null;
                if (nameJa == mapEn.Name) nameJa = null;

                result.Add(new TarkovMap
                {
                    Id = mapEn.Id,
                    Name = mapEn.Name,
                    NameKo = nameKo,
                    NameJa = nameJa,
                    NormalizedName = mapEn.NormalizedName,
                    Wiki = mapEn.Wiki
                });
            }

            progressCallback?.Invoke($"Fetched {result.Count} maps with translations");
            return result;
        }

        /// <summary>
        /// Save maps to JSON file
        /// </summary>
        public async Task SaveMapsToJsonAsync(List<TarkovMap> maps, string? fileName = null)
        {
            fileName ??= "maps.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);
            Directory.CreateDirectory(AppEnv.DataPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(maps, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Load maps from JSON file
        /// </summary>
        public async Task<List<TarkovMap>?> LoadMapsFromJsonAsync(string? fileName = null)
        {
            fileName ??= "maps.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<TarkovMap>>(json);
        }

        #endregion

        #region Hideout API

        /// <summary>
        /// Fetch hideout stations in a specific language
        /// </summary>
        private async Task<List<ApiHideoutStation>> FetchHideoutStationsApiAsync(string lang)
        {
            var query = $@"{{
                hideoutStations(lang: {lang}) {{
                    id
                    name
                    normalizedName
                    imageLink
                    levels {{
                        level
                        constructionTime
                        itemRequirements {{
                            item {{
                                id
                                name
                                normalizedName
                                iconLink
                            }}
                            count
                            attributes {{
                                type
                                name
                                value
                            }}
                        }}
                        stationLevelRequirements {{
                            station {{
                                id
                                name
                                normalizedName
                            }}
                            level
                        }}
                        traderRequirements {{
                            trader {{
                                id
                                name
                            }}
                            level
                        }}
                        skillRequirements {{
                            name
                            level
                        }}
                    }}
                }}
            }}";

            var data = await ExecuteQueryAsync<HideoutStationsData>(query);
            return data?.HideoutStations ?? new List<ApiHideoutStation>();
        }

        /// <summary>
        /// Fetch all hideout stations from tarkov.dev API with EN/KO/JA translations
        /// </summary>
        /// <param name="progressCallback">Progress callback for UI updates</param>
        /// <returns>List of hideout modules with multilingual names</returns>
        public async Task<List<HideoutModule>> FetchHideoutStationsAsync(Action<string>? progressCallback = null)
        {
            progressCallback?.Invoke("Fetching English hideout data...");
            var stationsEn = await FetchHideoutStationsApiAsync("en");

            progressCallback?.Invoke("Fetching Korean hideout data...");
            var stationsKo = await FetchHideoutStationsApiAsync("ko");

            progressCallback?.Invoke("Fetching Japanese hideout data...");
            var stationsJa = await FetchHideoutStationsApiAsync("ja");

            progressCallback?.Invoke($"Merging {stationsEn.Count} hideout stations...");

            // Create lookup dictionaries for KO and JA by ID
            var koById = stationsKo.ToDictionary(s => s.Id);
            var jaById = stationsJa.ToDictionary(s => s.Id);

            var result = new List<HideoutModule>();

            foreach (var stationEn in stationsEn)
            {
                var stationKo = koById.TryGetValue(stationEn.Id, out var ko) ? ko : null;
                var stationJa = jaById.TryGetValue(stationEn.Id, out var ja) ? ja : null;

                var nameKo = stationKo?.Name;
                var nameJa = stationJa?.Name;

                // Check if translation is same as English (means no translation available)
                if (nameKo == stationEn.Name) nameKo = null;
                if (nameJa == stationEn.Name) nameJa = null;

                var module = new HideoutModule
                {
                    Id = stationEn.Id,
                    Name = stationEn.Name,
                    NameKo = nameKo,
                    NameJa = nameJa,
                    NormalizedName = stationEn.NormalizedName,
                    ImageLink = stationEn.ImageLink,
                    Levels = new List<HideoutLevel>()
                };

                // Process levels
                if (stationEn.Levels != null)
                {
                    foreach (var levelEn in stationEn.Levels)
                    {
                        var levelKo = stationKo?.Levels?.FirstOrDefault(l => l.Level == levelEn.Level);
                        var levelJa = stationJa?.Levels?.FirstOrDefault(l => l.Level == levelEn.Level);

                        var hideoutLevel = new HideoutLevel
                        {
                            Level = levelEn.Level,
                            ConstructionTime = levelEn.ConstructionTime,
                            ItemRequirements = new List<HideoutItemRequirement>(),
                            StationLevelRequirements = new List<HideoutStationRequirement>(),
                            TraderRequirements = new List<HideoutTraderRequirement>(),
                            SkillRequirements = new List<HideoutSkillRequirement>()
                        };

                        // Item requirements
                        if (levelEn.ItemRequirements != null)
                        {
                            foreach (var itemReqEn in levelEn.ItemRequirements)
                            {
                                if (itemReqEn.Item == null) continue;

                                var itemReqKo = levelKo?.ItemRequirements?.FirstOrDefault(i => i.Item?.Id == itemReqEn.Item.Id);
                                var itemReqJa = levelJa?.ItemRequirements?.FirstOrDefault(i => i.Item?.Id == itemReqEn.Item.Id);

                                var itemNameKo = itemReqKo?.Item?.Name;
                                var itemNameJa = itemReqJa?.Item?.Name;
                                if (itemNameKo == itemReqEn.Item.Name) itemNameKo = null;
                                if (itemNameJa == itemReqEn.Item.Name) itemNameJa = null;

                                // Parse FIR attribute
                                var foundInRaid = false;
                                if (itemReqEn.Attributes != null)
                                {
                                    var firAttr = itemReqEn.Attributes.FirstOrDefault(a => a.Type == "foundInRaid");
                                    if (firAttr != null && firAttr.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
                                    {
                                        foundInRaid = true;
                                    }
                                }

                                hideoutLevel.ItemRequirements.Add(new HideoutItemRequirement
                                {
                                    ItemId = itemReqEn.Item.Id,
                                    ItemName = itemReqEn.Item.Name,
                                    ItemNameKo = itemNameKo,
                                    ItemNameJa = itemNameJa,
                                    ItemNormalizedName = itemReqEn.Item.NormalizedName,
                                    IconLink = itemReqEn.Item.IconLink,
                                    Count = itemReqEn.Count,
                                    FoundInRaid = foundInRaid
                                });
                            }
                        }

                        // Station requirements
                        if (levelEn.StationLevelRequirements != null)
                        {
                            foreach (var stationReqEn in levelEn.StationLevelRequirements)
                            {
                                if (stationReqEn.Station == null) continue;

                                var stationReqKo = levelKo?.StationLevelRequirements?.FirstOrDefault(s => s.Station?.Id == stationReqEn.Station.Id);
                                var stationReqJa = levelJa?.StationLevelRequirements?.FirstOrDefault(s => s.Station?.Id == stationReqEn.Station.Id);

                                var stationNameKo = stationReqKo?.Station?.Name;
                                var stationNameJa = stationReqJa?.Station?.Name;
                                if (stationNameKo == stationReqEn.Station.Name) stationNameKo = null;
                                if (stationNameJa == stationReqEn.Station.Name) stationNameJa = null;

                                hideoutLevel.StationLevelRequirements.Add(new HideoutStationRequirement
                                {
                                    StationId = stationReqEn.Station.Id,
                                    StationName = stationReqEn.Station.Name,
                                    StationNameKo = stationNameKo,
                                    StationNameJa = stationNameJa,
                                    Level = stationReqEn.Level
                                });
                            }
                        }

                        // Trader requirements
                        if (levelEn.TraderRequirements != null)
                        {
                            foreach (var traderReqEn in levelEn.TraderRequirements)
                            {
                                if (traderReqEn.Trader == null) continue;

                                var traderReqKo = levelKo?.TraderRequirements?.FirstOrDefault(t => t.Trader?.Id == traderReqEn.Trader.Id);
                                var traderReqJa = levelJa?.TraderRequirements?.FirstOrDefault(t => t.Trader?.Id == traderReqEn.Trader.Id);

                                var traderNameKo = traderReqKo?.Trader?.Name;
                                var traderNameJa = traderReqJa?.Trader?.Name;
                                if (traderNameKo == traderReqEn.Trader.Name) traderNameKo = null;
                                if (traderNameJa == traderReqEn.Trader.Name) traderNameJa = null;

                                hideoutLevel.TraderRequirements.Add(new HideoutTraderRequirement
                                {
                                    TraderId = traderReqEn.Trader.Id,
                                    TraderName = traderReqEn.Trader.Name,
                                    TraderNameKo = traderNameKo,
                                    TraderNameJa = traderNameJa,
                                    Level = traderReqEn.Level
                                });
                            }
                        }

                        // Skill requirements
                        if (levelEn.SkillRequirements != null)
                        {
                            foreach (var skillReqEn in levelEn.SkillRequirements)
                            {
                                var skillReqKo = levelKo?.SkillRequirements?.FirstOrDefault(s => s.Name == skillReqEn.Name);
                                var skillReqJa = levelJa?.SkillRequirements?.FirstOrDefault(s => s.Name == skillReqEn.Name);

                                var skillNameKo = skillReqKo?.Name;
                                var skillNameJa = skillReqJa?.Name;
                                if (skillNameKo == skillReqEn.Name) skillNameKo = null;
                                if (skillNameJa == skillReqEn.Name) skillNameJa = null;

                                hideoutLevel.SkillRequirements.Add(new HideoutSkillRequirement
                                {
                                    Name = skillReqEn.Name,
                                    NameKo = skillNameKo,
                                    NameJa = skillNameJa,
                                    Level = skillReqEn.Level
                                });
                            }
                        }

                        module.Levels.Add(hideoutLevel);
                    }
                }

                result.Add(module);
            }

            progressCallback?.Invoke($"Fetched {result.Count} hideout stations with translations");
            return result;
        }

        /// <summary>
        /// Save hideout stations to JSON file
        /// </summary>
        public async Task SaveHideoutStationsToJsonAsync(List<HideoutModule> stations, string? fileName = null)
        {
            fileName ??= "hideout.json";
            var filePath = Path.Combine(AppEnv.DataPath, fileName);
            Directory.CreateDirectory(AppEnv.DataPath);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var json = JsonSerializer.Serialize(stations, options);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }

        /// <summary>
        /// Load hideout stations from bundled Assets JSON file
        /// </summary>
        public async Task<List<HideoutModule>?> LoadHideoutStationsFromJsonAsync(string? fileName = null)
        {
            fileName ??= "hideout.json";

            // Load from bundled Assets folder
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(appDir, "Assets", fileName);

            if (!File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"[TarkovDevApiService] Hideout file not found: {filePath}");
                return null;
            }

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<HideoutModule>>(json);
        }

        #endregion

        #region Refresh All Master Data

        /// <summary>
        /// Fetch and save all master data (items, skills, traders)
        /// </summary>
        /// <param name="progressCallback">Progress callback for UI updates</param>
        /// <returns>Result with counts</returns>
        public async Task<MasterDataRefreshResult> RefreshMasterDataAsync(Action<string>? progressCallback = null)
        {
            var result = new MasterDataRefreshResult();

            try
            {
                // Fetch and save items
                progressCallback?.Invoke("[Items] Starting...");
                var items = await FetchItemsAsync(msg => progressCallback?.Invoke($"[Items] {msg}"));
                await SaveItemsToJsonAsync(items);
                result.ItemCount = items.Count;
                result.ItemsWithKorean = items.Count(i => !string.IsNullOrEmpty(i.NameKo));
                result.ItemsWithJapanese = items.Count(i => !string.IsNullOrEmpty(i.NameJa));
                progressCallback?.Invoke($"[Items] Saved {items.Count} items to items.json");

                // Fetch and save skills
                progressCallback?.Invoke("[Skills] Starting...");
                var skills = await FetchSkillsAsync(msg => progressCallback?.Invoke($"[Skills] {msg}"));
                await SaveSkillsToJsonAsync(skills);
                result.SkillCount = skills.Count;
                result.SkillsWithKorean = skills.Count(s => !string.IsNullOrEmpty(s.NameKo));
                result.SkillsWithJapanese = skills.Count(s => !string.IsNullOrEmpty(s.NameJa));
                progressCallback?.Invoke($"[Skills] Saved {skills.Count} skills to skills.json");

                // Fetch and save traders
                progressCallback?.Invoke("[Traders] Starting...");
                var traders = await FetchTradersAsync(msg => progressCallback?.Invoke($"[Traders] {msg}"));
                await SaveTradersToJsonAsync(traders);
                result.TraderCount = traders.Count;
                result.TradersWithKorean = traders.Count(t => !string.IsNullOrEmpty(t.NameKo));
                result.TradersWithJapanese = traders.Count(t => !string.IsNullOrEmpty(t.NameJa));
                progressCallback?.Invoke($"[Traders] Saved {traders.Count} traders to traders.json");

                // Fetch and save maps
                progressCallback?.Invoke("[Maps] Starting...");
                var maps = await FetchMapsAsync(msg => progressCallback?.Invoke($"[Maps] {msg}"));
                await SaveMapsToJsonAsync(maps);
                result.MapCount = maps.Count;
                result.MapsWithKorean = maps.Count(m => !string.IsNullOrEmpty(m.NameKo));
                result.MapsWithJapanese = maps.Count(m => !string.IsNullOrEmpty(m.NameJa));
                progressCallback?.Invoke($"[Maps] Saved {maps.Count} maps to maps.json");

                // Fetch and save hideout stations
                progressCallback?.Invoke("[Hideout] Starting...");
                var hideout = await FetchHideoutStationsAsync(msg => progressCallback?.Invoke($"[Hideout] {msg}"));
                await SaveHideoutStationsToJsonAsync(hideout);
                result.HideoutCount = hideout.Count;
                result.HideoutWithKorean = hideout.Count(h => !string.IsNullOrEmpty(h.NameKo));
                result.HideoutWithJapanese = hideout.Count(h => !string.IsNullOrEmpty(h.NameJa));
                progressCallback?.Invoke($"[Hideout] Saved {hideout.Count} hideout stations to hideout.json");

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                progressCallback?.Invoke($"[Error] {ex.Message}");
            }

            return result;
        }

        #endregion

        #region Lookup Helpers

        /// <summary>
        /// Build a lookup dictionary from items by normalizedName
        /// </summary>
        public static Dictionary<string, TarkovItem> BuildItemLookup(List<TarkovItem> items)
        {
            var lookup = new Dictionary<string, TarkovItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                // Skip duplicates, keep first occurrence
                lookup.TryAdd(item.NormalizedName, item);
            }

            // Add dogtag aliases for wiki-parsed names -> API names
            // Wiki parses "BEAR Dogtag" -> "bear-dogtag", API has "dogtag-bear"
            if (lookup.TryGetValue("dogtag-bear", out var bearDogtag))
            {
                lookup.TryAdd("bear-dogtag", bearDogtag);
            }
            if (lookup.TryGetValue("dogtag-usec", out var usecDogtag))
            {
                lookup.TryAdd("usec-dogtag", usecDogtag);
            }

            return lookup;
        }

        /// <summary>
        /// Build a lookup dictionary from skills by normalizedName
        /// </summary>
        public static Dictionary<string, TarkovSkill> BuildSkillLookup(List<TarkovSkill> skills)
        {
            var lookup = new Dictionary<string, TarkovSkill>(StringComparer.OrdinalIgnoreCase);
            foreach (var skill in skills)
            {
                lookup.TryAdd(skill.NormalizedName, skill);
            }
            return lookup;
        }

        /// <summary>
        /// Find an item by wiki name using normalized name matching
        /// </summary>
        /// <param name="wikiName">Item name from wiki</param>
        /// <param name="itemLookup">Pre-built lookup dictionary</param>
        /// <returns>Matched item or null</returns>
        public static TarkovItem? FindItemByWikiName(string wikiName, Dictionary<string, TarkovItem> itemLookup)
        {
            var alternatives = NormalizedNameGenerator.GenerateAlternatives(wikiName);

            foreach (var normalizedName in alternatives)
            {
                if (itemLookup.TryGetValue(normalizedName, out var item))
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// Find a skill by wiki name using normalized name matching
        /// </summary>
        /// <param name="wikiName">Skill name from wiki</param>
        /// <param name="skillLookup">Pre-built lookup dictionary</param>
        /// <returns>Matched skill or null</returns>
        public static TarkovSkill? FindSkillByWikiName(string wikiName, Dictionary<string, TarkovSkill> skillLookup)
        {
            var alternatives = NormalizedNameGenerator.GenerateAlternatives(wikiName);

            foreach (var normalizedName in alternatives)
            {
                if (skillLookup.TryGetValue(normalizedName, out var skill))
                {
                    return skill;
                }
            }

            return null;
        }

        /// <summary>
        /// Build a lookup dictionary from traders by normalizedName
        /// </summary>
        public static Dictionary<string, TarkovTrader> BuildTraderLookup(List<TarkovTrader> traders)
        {
            return traders.ToDictionary(t => t.NormalizedName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Find a trader by wiki name using normalized name matching
        /// </summary>
        /// <param name="wikiName">Trader name from wiki</param>
        /// <param name="traderLookup">Pre-built lookup dictionary</param>
        /// <returns>Matched trader or null</returns>
        public static TarkovTrader? FindTraderByWikiName(string wikiName, Dictionary<string, TarkovTrader> traderLookup)
        {
            var alternatives = NormalizedNameGenerator.GenerateAlternatives(wikiName);

            foreach (var normalizedName in alternatives)
            {
                if (traderLookup.TryGetValue(normalizedName, out var trader))
                {
                    return trader;
                }
            }

            return null;
        }

        /// <summary>
        /// Build a lookup dictionary from maps by normalizedName
        /// </summary>
        public static Dictionary<string, TarkovMap> BuildMapLookup(List<TarkovMap> maps)
        {
            return maps.ToDictionary(m => m.NormalizedName, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Find a map by wiki name using normalized name matching
        /// </summary>
        /// <param name="wikiName">Map name from wiki</param>
        /// <param name="mapLookup">Pre-built lookup dictionary</param>
        /// <returns>Matched map or null</returns>
        public static TarkovMap? FindMapByWikiName(string wikiName, Dictionary<string, TarkovMap> mapLookup)
        {
            var alternatives = NormalizedNameGenerator.GenerateAlternatives(wikiName);

            foreach (var normalizedName in alternatives)
            {
                if (mapLookup.TryGetValue(normalizedName, out var map))
                {
                    return map;
                }
            }

            return null;
        }

        #endregion

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Result of master data refresh operation
    /// </summary>
    public class MasterDataRefreshResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        // Items stats
        public int ItemCount { get; set; }
        public int ItemsWithKorean { get; set; }
        public int ItemsWithJapanese { get; set; }

        // Skills stats
        public int SkillCount { get; set; }
        public int SkillsWithKorean { get; set; }
        public int SkillsWithJapanese { get; set; }

        // Traders stats
        public int TraderCount { get; set; }
        public int TradersWithKorean { get; set; }
        public int TradersWithJapanese { get; set; }

        // Maps stats
        public int MapCount { get; set; }
        public int MapsWithKorean { get; set; }
        public int MapsWithJapanese { get; set; }

        // Hideout stats
        public int HideoutCount { get; set; }
        public int HideoutWithKorean { get; set; }
        public int HideoutWithJapanese { get; set; }
    }
}
