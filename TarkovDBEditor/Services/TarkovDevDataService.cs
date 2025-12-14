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

        private readonly string _cacheDir;
        private readonly string _itemsCachePath;
        private readonly string _questsCachePath;
        private readonly string _hideoutCachePath;
        private readonly string _tradersCachePath;

        public TarkovDevDataService(string? basePath = null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "TarkovDBEditor/1.0");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);

            basePath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wiki_data");
            _cacheDir = Path.Combine(basePath, "cache");
            _itemsCachePath = Path.Combine(_cacheDir, "tarkov_dev_items.json");
            _questsCachePath = Path.Combine(_cacheDir, "tarkov_dev_quests.json");
            _hideoutCachePath = Path.Combine(_cacheDir, "tarkov_dev_hideout.json");
            _tradersCachePath = Path.Combine(_cacheDir, "tarkov_dev_traders.json");

            Directory.CreateDirectory(_cacheDir);
        }

        #region Cache Management

        /// <summary>
        /// 캐시된 아이템 데이터가 있는지 확인
        /// </summary>
        public bool HasCachedItems()
        {
            return File.Exists(_itemsCachePath);
        }

        /// <summary>
        /// 캐시된 퀘스트 데이터가 있는지 확인
        /// </summary>
        public bool HasCachedQuests()
        {
            return File.Exists(_questsCachePath);
        }

        /// <summary>
        /// 캐시된 Hideout 데이터가 있는지 확인
        /// </summary>
        public bool HasCachedHideout()
        {
            return File.Exists(_hideoutCachePath);
        }

        /// <summary>
        /// 캐시된 Traders 데이터가 있는지 확인
        /// </summary>
        public bool HasCachedTraders()
        {
            return File.Exists(_tradersCachePath);
        }

        /// <summary>
        /// 캐시 정보 가져오기 (캐시 날짜, 아이템 수, 퀘스트 수, Hideout 수, Traders 수)
        /// </summary>
        public TarkovDevCacheInfo GetCacheInfo()
        {
            var info = new TarkovDevCacheInfo();

            if (File.Exists(_itemsCachePath))
            {
                try
                {
                    var fileInfo = new FileInfo(_itemsCachePath);
                    info.ItemsCachedAt = fileInfo.LastWriteTime;

                    var json = File.ReadAllText(_itemsCachePath);
                    var cache = JsonSerializer.Deserialize<TarkovDevItemsCache>(json);
                    info.ItemsCount = cache?.Items?.Count ?? 0;
                }
                catch { }
            }

            if (File.Exists(_questsCachePath))
            {
                try
                {
                    var fileInfo = new FileInfo(_questsCachePath);
                    info.QuestsCachedAt = fileInfo.LastWriteTime;

                    var json = File.ReadAllText(_questsCachePath);
                    var cache = JsonSerializer.Deserialize<TarkovDevQuestsCache>(json);
                    info.QuestsCount = cache?.Quests?.Count ?? 0;
                }
                catch { }
            }

            if (File.Exists(_hideoutCachePath))
            {
                try
                {
                    var fileInfo = new FileInfo(_hideoutCachePath);
                    info.HideoutCachedAt = fileInfo.LastWriteTime;

                    var json = File.ReadAllText(_hideoutCachePath);
                    var cache = JsonSerializer.Deserialize<TarkovDevHideoutCache>(json);
                    info.HideoutCount = cache?.Stations?.Count ?? 0;
                }
                catch { }
            }

            if (File.Exists(_tradersCachePath))
            {
                try
                {
                    var fileInfo = new FileInfo(_tradersCachePath);
                    info.TradersCachedAt = fileInfo.LastWriteTime;

                    var json = File.ReadAllText(_tradersCachePath);
                    var cache = JsonSerializer.Deserialize<TarkovDevTradersCache>(json);
                    info.TradersCount = cache?.Traders?.Count ?? 0;
                }
                catch { }
            }

            return info;
        }

        /// <summary>
        /// 캐시된 아이템 데이터 로드
        /// </summary>
        public async Task<Dictionary<string, TarkovDevMultiLangItem>?> LoadCachedItemsAsync(
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_itemsCachePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(_itemsCachePath, cancellationToken);
                var cache = JsonSerializer.Deserialize<TarkovDevItemsCache>(json);
                return cache?.Items;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 캐시된 퀘스트 데이터 로드
        /// </summary>
        public async Task<Dictionary<string, TarkovDevQuestCacheItem>?> LoadCachedQuestsAsync(
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_questsCachePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(_questsCachePath, cancellationToken);
                var cache = JsonSerializer.Deserialize<TarkovDevQuestsCache>(json);
                return cache?.Quests;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 캐시된 Hideout 데이터 로드
        /// </summary>
        public async Task<List<TarkovDevHideoutStation>?> LoadCachedHideoutAsync(
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_hideoutCachePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(_hideoutCachePath, cancellationToken);
                var cache = JsonSerializer.Deserialize<TarkovDevHideoutCache>(json);
                return cache?.Stations;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 캐시된 Traders 데이터 로드
        /// </summary>
        public async Task<List<TarkovDevTraderCacheItem>?> LoadCachedTradersAsync(
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(_tradersCachePath))
                return null;

            try
            {
                var json = await File.ReadAllTextAsync(_tradersCachePath, cancellationToken);
                var cache = JsonSerializer.Deserialize<TarkovDevTradersCache>(json);
                return cache?.Traders;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 아이템 데이터 캐시에 저장
        /// </summary>
        public async Task SaveItemsCacheAsync(
            Dictionary<string, TarkovDevMultiLangItem> items,
            CancellationToken cancellationToken = default)
        {
            var cache = new TarkovDevItemsCache
            {
                CachedAt = DateTime.UtcNow,
                Items = items
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(cache, options);
            await File.WriteAllTextAsync(_itemsCachePath, json, cancellationToken);
        }

        /// <summary>
        /// 퀘스트 데이터 캐시에 저장
        /// </summary>
        public async Task SaveQuestsCacheAsync(
            Dictionary<string, TarkovDevQuestCacheItem> quests,
            CancellationToken cancellationToken = default)
        {
            var cache = new TarkovDevQuestsCache
            {
                CachedAt = DateTime.UtcNow,
                Quests = quests
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(cache, options);
            await File.WriteAllTextAsync(_questsCachePath, json, cancellationToken);
        }

        /// <summary>
        /// Hideout 데이터 캐시에 저장
        /// </summary>
        public async Task SaveHideoutCacheAsync(
            List<TarkovDevHideoutStation> stations,
            CancellationToken cancellationToken = default)
        {
            var cache = new TarkovDevHideoutCache
            {
                CachedAt = DateTime.UtcNow,
                Stations = stations
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(cache, options);
            await File.WriteAllTextAsync(_hideoutCachePath, json, cancellationToken);
        }

        /// <summary>
        /// Traders 데이터 캐시에 저장
        /// </summary>
        public async Task SaveTradersCacheAsync(
            List<TarkovDevTraderCacheItem> traders,
            CancellationToken cancellationToken = default)
        {
            var cache = new TarkovDevTradersCache
            {
                CachedAt = DateTime.UtcNow,
                Traders = traders
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(cache, options);
            await File.WriteAllTextAsync(_tradersCachePath, json, cancellationToken);
        }

        /// <summary>
        /// tarkov.dev에서 모든 데이터를 다운로드하고 캐시에 저장
        /// </summary>
        public async Task<TarkovDevCacheResult> CacheAllDataAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new TarkovDevCacheResult();

            try
            {
                // 아이템 데이터 다운로드 및 캐시
                progress?.Invoke("Downloading items from tarkov.dev...");
                var items = await FetchAllLanguagesAsync(progress, cancellationToken);
                await SaveItemsCacheAsync(items, cancellationToken);
                result.ItemsCount = items.Count;
                result.ItemsSuccess = true;
                progress?.Invoke($"Cached {items.Count} items from tarkov.dev");
            }
            catch (Exception ex)
            {
                result.ItemsError = ex.Message;
                progress?.Invoke($"Failed to cache items: {ex.Message}");
            }

            try
            {
                // 퀘스트 데이터 다운로드 및 캐시
                progress?.Invoke("Downloading quests from tarkov.dev...");
                var quests = await FetchAllQuestsAsync(progress, cancellationToken);
                await SaveQuestsCacheAsync(quests, cancellationToken);
                result.QuestsCount = quests.Count;
                result.QuestsSuccess = true;
                progress?.Invoke($"Cached {quests.Count} quests from tarkov.dev");
            }
            catch (Exception ex)
            {
                result.QuestsError = ex.Message;
                progress?.Invoke($"Failed to cache quests: {ex.Message}");
            }

            try
            {
                // Hideout 데이터 다운로드 및 캐시
                progress?.Invoke("Downloading hideout stations from tarkov.dev...");
                var hideout = await FetchAllHideoutAsync(progress, cancellationToken);
                await SaveHideoutCacheAsync(hideout, cancellationToken);
                result.HideoutCount = hideout.Count;
                result.HideoutSuccess = true;
                progress?.Invoke($"Cached {hideout.Count} hideout stations from tarkov.dev");
            }
            catch (Exception ex)
            {
                result.HideoutError = ex.Message;
                progress?.Invoke($"Failed to cache hideout: {ex.Message}");
            }

            try
            {
                // Traders 데이터 다운로드 및 캐시
                progress?.Invoke("Downloading traders from tarkov.dev...");
                var traders = await FetchAllTradersAsync(progress, cancellationToken);
                await SaveTradersCacheAsync(traders, cancellationToken);
                result.TradersCount = traders.Count;
                result.TradersSuccess = true;
                progress?.Invoke($"Cached {traders.Count} traders from tarkov.dev");
            }
            catch (Exception ex)
            {
                result.TradersError = ex.Message;
                progress?.Invoke($"Failed to cache traders: {ex.Message}");
            }

            result.CachedAt = DateTime.Now;
            return result;
        }

        /// <summary>
        /// tarkov.dev에서 퀘스트 다국어 데이터 가져오기
        /// </summary>
        public async Task<Dictionary<string, TarkovDevQuestCacheItem>> FetchAllQuestsAsync(
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
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(GraphQLEndpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var result = new Dictionary<string, TarkovDevQuestCacheItem>(StringComparer.OrdinalIgnoreCase);

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

                    var quest = new TarkovDevQuestCacheItem
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

                    // wikiLink가 있으면 키로 사용, 없으면 ID 사용
                    if (!string.IsNullOrEmpty(wikiLink))
                    {
                        result[wikiLink] = quest;
                    }
                    else if (!string.IsNullOrEmpty(id))
                    {
                        result[id] = quest;
                    }
                }
            }

            progress?.Invoke($"Fetched {result.Count} quests from tarkov.dev");
            return result;
        }

        /// <summary>
        /// tarkov.dev에서 Traders 다국어 데이터 가져오기
        /// </summary>
        public async Task<List<TarkovDevTraderCacheItem>> FetchAllTradersAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke("Fetching traders from tarkov.dev API...");

            var query = @"
            {
                traders(lang: en) {
                    id
                    name
                    normalizedName
                    imageLink
                }
                ko: traders(lang: ko) { id name }
                ja: traders(lang: ja) { id name }
            }";

            var requestBody = JsonSerializer.Serialize(new { query });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(GraphQLEndpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var result = new List<TarkovDevTraderCacheItem>();

            if (!doc.RootElement.TryGetProperty("data", out var data))
                return result;

            // 한국어, 일본어 맵 생성
            var koNames = new Dictionary<string, string>();
            var jaNames = new Dictionary<string, string>();

            if (data.TryGetProperty("ko", out var koTraders))
            {
                foreach (var trader in koTraders.EnumerateArray())
                {
                    var id = trader.GetProperty("id").GetString();
                    var name = trader.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        koNames[id] = name;
                }
            }

            if (data.TryGetProperty("ja", out var jaTraders))
            {
                foreach (var trader in jaTraders.EnumerateArray())
                {
                    var id = trader.GetProperty("id").GetString();
                    var name = trader.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        jaNames[id] = name;
                }
            }

            // 영어 기준으로 병합
            if (data.TryGetProperty("traders", out var traders))
            {
                foreach (var trader in traders.EnumerateArray())
                {
                    var id = trader.GetProperty("id").GetString();
                    var name = trader.GetProperty("name").GetString();
                    var normalizedName = trader.TryGetProperty("normalizedName", out var nn) ? nn.GetString() : null;
                    var imageLink = trader.TryGetProperty("imageLink", out var il) ? il.GetString() : null;

                    if (string.IsNullOrEmpty(id))
                        continue;

                    var nameKo = koNames.TryGetValue(id, out var ko) ? ko : null;
                    var nameJa = jaNames.TryGetValue(id, out var ja) ? ja : null;

                    // 번역이 영어와 같으면 null로 처리
                    if (nameKo == name) nameKo = null;
                    if (nameJa == name) nameJa = null;

                    result.Add(new TarkovDevTraderCacheItem
                    {
                        Id = id,
                        Name = name ?? "",
                        NameKO = nameKo,
                        NameJA = nameJa,
                        NormalizedName = normalizedName,
                        ImageLink = imageLink
                    });
                }
            }

            progress?.Invoke($"Fetched {result.Count} traders from tarkov.dev");
            return result;
        }

        /// <summary>
        /// tarkov.dev에서 Hideout 다국어 데이터 가져오기
        /// </summary>
        public async Task<List<TarkovDevHideoutStation>> FetchAllHideoutAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke("Fetching hideout stations from tarkov.dev API...");

            // 영어 데이터 가져오기
            var stationsEn = await FetchHideoutStationsAsync("en", cancellationToken);
            progress?.Invoke($"Fetched {stationsEn.Count} stations (EN)");

            // 한국어 데이터 가져오기
            var stationsKo = await FetchHideoutStationsAsync("ko", cancellationToken);
            progress?.Invoke($"Fetched {stationsKo.Count} stations (KO)");

            // 일본어 데이터 가져오기
            var stationsJa = await FetchHideoutStationsAsync("ja", cancellationToken);
            progress?.Invoke($"Fetched {stationsJa.Count} stations (JA)");

            // ID 기반 룩업 생성
            var koById = stationsKo.ToDictionary(s => s.Id);
            var jaById = stationsJa.ToDictionary(s => s.Id);

            var result = new List<TarkovDevHideoutStation>();

            foreach (var stationEn in stationsEn)
            {
                var stationKo = koById.TryGetValue(stationEn.Id, out var ko) ? ko : null;
                var stationJa = jaById.TryGetValue(stationEn.Id, out var ja) ? ja : null;

                var nameKo = stationKo?.Name;
                var nameJa = stationJa?.Name;

                // 번역이 영어와 같으면 null
                if (nameKo == stationEn.Name) nameKo = null;
                if (nameJa == stationEn.Name) nameJa = null;

                var station = new TarkovDevHideoutStation
                {
                    Id = stationEn.Id,
                    Name = stationEn.Name,
                    NameKo = nameKo,
                    NameJa = nameJa,
                    NormalizedName = stationEn.NormalizedName,
                    ImageLink = stationEn.ImageLink,
                    Levels = new List<TarkovDevHideoutLevel>()
                };

                // 레벨 처리
                if (stationEn.Levels != null)
                {
                    foreach (var levelEn in stationEn.Levels)
                    {
                        var levelKo = stationKo?.Levels?.FirstOrDefault(l => l.Level == levelEn.Level);
                        var levelJa = stationJa?.Levels?.FirstOrDefault(l => l.Level == levelEn.Level);

                        var hideoutLevel = new TarkovDevHideoutLevel
                        {
                            Level = levelEn.Level,
                            ConstructionTime = levelEn.ConstructionTime,
                            ItemRequirements = new List<TarkovDevHideoutItemReq>(),
                            StationLevelRequirements = new List<TarkovDevHideoutStationReq>(),
                            TraderRequirements = new List<TarkovDevHideoutTraderReq>(),
                            SkillRequirements = new List<TarkovDevHideoutSkillReq>()
                        };

                        // 아이템 요구사항
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

                                // FIR 속성 파싱
                                var foundInRaid = false;
                                if (itemReqEn.Attributes != null)
                                {
                                    var firAttr = itemReqEn.Attributes.FirstOrDefault(a => a.Type == "foundInRaid");
                                    if (firAttr != null && firAttr.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
                                    {
                                        foundInRaid = true;
                                    }
                                }

                                hideoutLevel.ItemRequirements.Add(new TarkovDevHideoutItemReq
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

                        // 스테이션 요구사항
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

                                hideoutLevel.StationLevelRequirements.Add(new TarkovDevHideoutStationReq
                                {
                                    StationId = stationReqEn.Station.Id,
                                    StationName = stationReqEn.Station.Name,
                                    StationNameKo = stationNameKo,
                                    StationNameJa = stationNameJa,
                                    Level = stationReqEn.Level
                                });
                            }
                        }

                        // 트레이더 요구사항
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

                                hideoutLevel.TraderRequirements.Add(new TarkovDevHideoutTraderReq
                                {
                                    TraderId = traderReqEn.Trader.Id,
                                    TraderName = traderReqEn.Trader.Name,
                                    TraderNameKo = traderNameKo,
                                    TraderNameJa = traderNameJa,
                                    Level = traderReqEn.Level
                                });
                            }
                        }

                        // 스킬 요구사항
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

                                hideoutLevel.SkillRequirements.Add(new TarkovDevHideoutSkillReq
                                {
                                    Name = skillReqEn.Name,
                                    NameKo = skillNameKo,
                                    NameJa = skillNameJa,
                                    Level = skillReqEn.Level
                                });
                            }
                        }

                        station.Levels.Add(hideoutLevel);
                    }
                }

                result.Add(station);
            }

            progress?.Invoke($"Merged {result.Count} hideout stations with translations");
            return result;
        }

        /// <summary>
        /// tarkov.dev API에서 특정 언어의 Hideout 데이터 가져오기
        /// </summary>
        private async Task<List<ApiHideoutStation>> FetchHideoutStationsAsync(
            string lang,
            CancellationToken cancellationToken = default)
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

            var requestBody = JsonSerializer.Serialize(new { query });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(GraphQLEndpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var result = new List<ApiHideoutStation>();

            if (!doc.RootElement.TryGetProperty("data", out var data))
                return result;

            if (!data.TryGetProperty("hideoutStations", out var stations))
                return result;

            foreach (var station in stations.EnumerateArray())
            {
                var apiStation = new ApiHideoutStation
                {
                    Id = station.GetProperty("id").GetString() ?? "",
                    Name = station.GetProperty("name").GetString() ?? "",
                    NormalizedName = station.TryGetProperty("normalizedName", out var nn) ? nn.GetString() : null,
                    ImageLink = station.TryGetProperty("imageLink", out var il) ? il.GetString() : null,
                    Levels = new List<ApiHideoutLevel>()
                };

                if (station.TryGetProperty("levels", out var levels))
                {
                    foreach (var level in levels.EnumerateArray())
                    {
                        var apiLevel = new ApiHideoutLevel
                        {
                            Level = level.GetProperty("level").GetInt32(),
                            ConstructionTime = level.TryGetProperty("constructionTime", out var ct) ? ct.GetInt32() : 0,
                            ItemRequirements = new List<ApiHideoutItemReq>(),
                            StationLevelRequirements = new List<ApiHideoutStationReq>(),
                            TraderRequirements = new List<ApiHideoutTraderReq>(),
                            SkillRequirements = new List<ApiHideoutSkillReq>()
                        };

                        // 아이템 요구사항 파싱
                        if (level.TryGetProperty("itemRequirements", out var itemReqs))
                        {
                            foreach (var itemReq in itemReqs.EnumerateArray())
                            {
                                var apiItemReq = new ApiHideoutItemReq
                                {
                                    Count = itemReq.TryGetProperty("count", out var cnt) ? cnt.GetInt32() : 1,
                                    Attributes = new List<ApiHideoutAttribute>()
                                };

                                if (itemReq.TryGetProperty("item", out var item))
                                {
                                    apiItemReq.Item = new ApiHideoutItem
                                    {
                                        Id = item.GetProperty("id").GetString() ?? "",
                                        Name = item.GetProperty("name").GetString() ?? "",
                                        NormalizedName = item.TryGetProperty("normalizedName", out var itemNn) ? itemNn.GetString() : null,
                                        IconLink = item.TryGetProperty("iconLink", out var itemIcon) ? itemIcon.GetString() : null
                                    };
                                }

                                if (itemReq.TryGetProperty("attributes", out var attrs))
                                {
                                    foreach (var attr in attrs.EnumerateArray())
                                    {
                                        apiItemReq.Attributes.Add(new ApiHideoutAttribute
                                        {
                                            Type = attr.TryGetProperty("type", out var attrType) ? attrType.GetString() : null,
                                            Name = attr.TryGetProperty("name", out var attrName) ? attrName.GetString() : null,
                                            Value = attr.TryGetProperty("value", out var attrValue) ? attrValue.GetString() : null
                                        });
                                    }
                                }

                                apiLevel.ItemRequirements.Add(apiItemReq);
                            }
                        }

                        // 스테이션 요구사항 파싱
                        if (level.TryGetProperty("stationLevelRequirements", out var stationReqs))
                        {
                            foreach (var stationReq in stationReqs.EnumerateArray())
                            {
                                var apiStationReq = new ApiHideoutStationReq
                                {
                                    Level = stationReq.TryGetProperty("level", out var lvl) ? lvl.GetInt32() : 1
                                };

                                if (stationReq.TryGetProperty("station", out var reqStation))
                                {
                                    apiStationReq.Station = new ApiHideoutStationRef
                                    {
                                        Id = reqStation.GetProperty("id").GetString() ?? "",
                                        Name = reqStation.GetProperty("name").GetString() ?? "",
                                        NormalizedName = reqStation.TryGetProperty("normalizedName", out var sNn) ? sNn.GetString() : null
                                    };
                                }

                                apiLevel.StationLevelRequirements.Add(apiStationReq);
                            }
                        }

                        // 트레이더 요구사항 파싱
                        if (level.TryGetProperty("traderRequirements", out var traderReqs))
                        {
                            foreach (var traderReq in traderReqs.EnumerateArray())
                            {
                                var apiTraderReq = new ApiHideoutTraderReq
                                {
                                    Level = traderReq.TryGetProperty("level", out var lvl) ? lvl.GetInt32() : 1
                                };

                                if (traderReq.TryGetProperty("trader", out var trader))
                                {
                                    apiTraderReq.Trader = new ApiHideoutTrader
                                    {
                                        Id = trader.GetProperty("id").GetString() ?? "",
                                        Name = trader.GetProperty("name").GetString() ?? ""
                                    };
                                }

                                apiLevel.TraderRequirements.Add(apiTraderReq);
                            }
                        }

                        // 스킬 요구사항 파싱
                        if (level.TryGetProperty("skillRequirements", out var skillReqs))
                        {
                            foreach (var skillReq in skillReqs.EnumerateArray())
                            {
                                apiLevel.SkillRequirements.Add(new ApiHideoutSkillReq
                                {
                                    Name = skillReq.GetProperty("name").GetString() ?? "",
                                    Level = skillReq.TryGetProperty("level", out var lvl) ? lvl.GetInt32() : 1
                                });
                            }
                        }

                        apiStation.Levels.Add(apiLevel);
                    }
                }

                result.Add(apiStation);
            }

            return result;
        }

        #endregion

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

    #region Cache Models

    /// <summary>
    /// tarkov.dev 캐시 정보
    /// </summary>
    public class TarkovDevCacheInfo
    {
        public DateTime? ItemsCachedAt { get; set; }
        public int ItemsCount { get; set; }
        public DateTime? QuestsCachedAt { get; set; }
        public int QuestsCount { get; set; }
        public DateTime? HideoutCachedAt { get; set; }
        public int HideoutCount { get; set; }
        public DateTime? TradersCachedAt { get; set; }
        public int TradersCount { get; set; }

        public bool HasItemsCache => ItemsCachedAt.HasValue;
        public bool HasQuestsCache => QuestsCachedAt.HasValue;
        public bool HasHideoutCache => HideoutCachedAt.HasValue;
        public bool HasTradersCache => TradersCachedAt.HasValue;
    }

    /// <summary>
    /// tarkov.dev 캐시 결과
    /// </summary>
    public class TarkovDevCacheResult
    {
        public DateTime CachedAt { get; set; }
        public bool ItemsSuccess { get; set; }
        public int ItemsCount { get; set; }
        public string? ItemsError { get; set; }
        public bool QuestsSuccess { get; set; }
        public int QuestsCount { get; set; }
        public string? QuestsError { get; set; }
        public bool HideoutSuccess { get; set; }
        public int HideoutCount { get; set; }
        public string? HideoutError { get; set; }
        public bool TradersSuccess { get; set; }
        public int TradersCount { get; set; }
        public string? TradersError { get; set; }
    }

    /// <summary>
    /// tarkov.dev 아이템 캐시 파일
    /// </summary>
    public class TarkovDevItemsCache
    {
        [JsonPropertyName("cachedAt")]
        public DateTime CachedAt { get; set; }

        [JsonPropertyName("items")]
        public Dictionary<string, TarkovDevMultiLangItem> Items { get; set; } = new();
    }

    /// <summary>
    /// tarkov.dev 퀘스트 캐시 파일
    /// </summary>
    public class TarkovDevQuestsCache
    {
        [JsonPropertyName("cachedAt")]
        public DateTime CachedAt { get; set; }

        [JsonPropertyName("quests")]
        public Dictionary<string, TarkovDevQuestCacheItem> Quests { get; set; } = new();
    }

    /// <summary>
    /// tarkov.dev 퀘스트 캐시 아이템
    /// </summary>
    public class TarkovDevQuestCacheItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("tarkovDataId")]
        public int? TarkovDataId { get; set; }

        [JsonPropertyName("nameEN")]
        public string NameEN { get; set; } = "";

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("nameKO")]
        public string NameKO { get; set; } = "";

        [JsonPropertyName("nameJA")]
        public string NameJA { get; set; } = "";

        [JsonPropertyName("trader")]
        public string? Trader { get; set; }

        [JsonPropertyName("wikiLink")]
        public string? WikiLink { get; set; }
    }

    /// <summary>
    /// tarkov.dev Hideout 캐시 파일
    /// </summary>
    public class TarkovDevHideoutCache
    {
        [JsonPropertyName("cachedAt")]
        public DateTime CachedAt { get; set; }

        [JsonPropertyName("stations")]
        public List<TarkovDevHideoutStation> Stations { get; set; } = new();
    }

    /// <summary>
    /// tarkov.dev Traders 캐시 파일
    /// </summary>
    public class TarkovDevTradersCache
    {
        [JsonPropertyName("cachedAt")]
        public DateTime CachedAt { get; set; }

        [JsonPropertyName("traders")]
        public List<TarkovDevTraderCacheItem> Traders { get; set; } = new();
    }

    /// <summary>
    /// tarkov.dev Trader 캐시 아이템
    /// </summary>
    public class TarkovDevTraderCacheItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("nameKO")]
        public string? NameKO { get; set; }

        [JsonPropertyName("nameJA")]
        public string? NameJA { get; set; }

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("imageLink")]
        public string? ImageLink { get; set; }

        [JsonPropertyName("localIconPath")]
        public string? LocalIconPath { get; set; }
    }

    #endregion

    #region Hideout Models

    /// <summary>
    /// Hideout 스테이션 (다국어 통합)
    /// </summary>
    public class TarkovDevHideoutStation
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }

        [JsonPropertyName("imageLink")]
        public string? ImageLink { get; set; }

        [JsonPropertyName("levels")]
        public List<TarkovDevHideoutLevel> Levels { get; set; } = new();

        [JsonIgnore]
        public int MaxLevel => Levels?.Count ?? 0;
    }

    /// <summary>
    /// Hideout 레벨
    /// </summary>
    public class TarkovDevHideoutLevel
    {
        [JsonPropertyName("level")]
        public int Level { get; set; }

        [JsonPropertyName("constructionTime")]
        public int ConstructionTime { get; set; }

        [JsonPropertyName("itemRequirements")]
        public List<TarkovDevHideoutItemReq> ItemRequirements { get; set; } = new();

        [JsonPropertyName("stationLevelRequirements")]
        public List<TarkovDevHideoutStationReq> StationLevelRequirements { get; set; } = new();

        [JsonPropertyName("traderRequirements")]
        public List<TarkovDevHideoutTraderReq> TraderRequirements { get; set; } = new();

        [JsonPropertyName("skillRequirements")]
        public List<TarkovDevHideoutSkillReq> SkillRequirements { get; set; } = new();
    }

    /// <summary>
    /// Hideout 아이템 요구사항
    /// </summary>
    public class TarkovDevHideoutItemReq
    {
        [JsonPropertyName("itemId")]
        public string ItemId { get; set; } = "";

        [JsonPropertyName("itemName")]
        public string ItemName { get; set; } = "";

        [JsonPropertyName("itemNameKo")]
        public string? ItemNameKo { get; set; }

        [JsonPropertyName("itemNameJa")]
        public string? ItemNameJa { get; set; }

        [JsonPropertyName("itemNormalizedName")]
        public string? ItemNormalizedName { get; set; }

        [JsonPropertyName("iconLink")]
        public string? IconLink { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("foundInRaid")]
        public bool FoundInRaid { get; set; }
    }

    /// <summary>
    /// Hideout 스테이션 요구사항
    /// </summary>
    public class TarkovDevHideoutStationReq
    {
        [JsonPropertyName("stationId")]
        public string StationId { get; set; } = "";

        [JsonPropertyName("stationName")]
        public string StationName { get; set; } = "";

        [JsonPropertyName("stationNameKo")]
        public string? StationNameKo { get; set; }

        [JsonPropertyName("stationNameJa")]
        public string? StationNameJa { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }
    }

    /// <summary>
    /// Hideout 트레이더 요구사항
    /// </summary>
    public class TarkovDevHideoutTraderReq
    {
        [JsonPropertyName("traderId")]
        public string TraderId { get; set; } = "";

        [JsonPropertyName("traderName")]
        public string TraderName { get; set; } = "";

        [JsonPropertyName("traderNameKo")]
        public string? TraderNameKo { get; set; }

        [JsonPropertyName("traderNameJa")]
        public string? TraderNameJa { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }
    }

    /// <summary>
    /// Hideout 스킬 요구사항
    /// </summary>
    public class TarkovDevHideoutSkillReq
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }
    }

    #endregion

    #region Hideout API Models (Internal)

    internal class ApiHideoutStation
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? NormalizedName { get; set; }
        public string? ImageLink { get; set; }
        public List<ApiHideoutLevel>? Levels { get; set; }
    }

    internal class ApiHideoutLevel
    {
        public int Level { get; set; }
        public int ConstructionTime { get; set; }
        public List<ApiHideoutItemReq>? ItemRequirements { get; set; }
        public List<ApiHideoutStationReq>? StationLevelRequirements { get; set; }
        public List<ApiHideoutTraderReq>? TraderRequirements { get; set; }
        public List<ApiHideoutSkillReq>? SkillRequirements { get; set; }
    }

    internal class ApiHideoutItemReq
    {
        public ApiHideoutItem? Item { get; set; }
        public int Count { get; set; }
        public List<ApiHideoutAttribute>? Attributes { get; set; }
    }

    internal class ApiHideoutItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? NormalizedName { get; set; }
        public string? IconLink { get; set; }
    }

    internal class ApiHideoutAttribute
    {
        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? Value { get; set; }
    }

    internal class ApiHideoutStationReq
    {
        public ApiHideoutStationRef? Station { get; set; }
        public int Level { get; set; }
    }

    internal class ApiHideoutStationRef
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? NormalizedName { get; set; }
    }

    internal class ApiHideoutTraderReq
    {
        public ApiHideoutTrader? Trader { get; set; }
        public int Level { get; set; }
    }

    internal class ApiHideoutTrader
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    internal class ApiHideoutSkillReq
    {
        public string Name { get; set; } = "";
        public int Level { get; set; }
    }

    #endregion
}
