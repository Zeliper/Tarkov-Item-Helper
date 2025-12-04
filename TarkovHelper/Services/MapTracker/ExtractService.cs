using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TarkovHelper.Debug;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// tarkov.dev API에서 탈출구 데이터를 가져오고 관리하는 서비스.
/// </summary>
public sealed class ExtractService : IDisposable
{
    private static ExtractService? _instance;
    public static ExtractService Instance => _instance ??= new ExtractService();

    private readonly HttpClient _httpClient;
    private const string GraphQLEndpoint = "https://api.tarkov.dev/graphql";
    private const string CacheFileName = "map_extracts.json";

    private List<MapExtract> _allExtracts = new();
    private Dictionary<string, List<MapExtract>> _extractsByMap = new();
    private bool _isLoaded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public bool IsLoaded => _isLoaded;
    public IReadOnlyList<MapExtract> AllExtracts => _allExtracts;

    public ExtractService()
    {
        _httpClient = new HttpClient();
    }

    #region GraphQL DTOs

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

        [JsonPropertyName("extracts")]
        public List<ApiExtract>? Extracts { get; set; }
    }

    private class ApiExtract
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("faction")]
        public string Faction { get; set; } = string.Empty;

        [JsonPropertyName("position")]
        public ApiPosition? Position { get; set; }

        [JsonPropertyName("outline")]
        public List<ApiOutlinePoint>? Outline { get; set; }

        [JsonPropertyName("top")]
        public double? Top { get; set; }

        [JsonPropertyName("bottom")]
        public double? Bottom { get; set; }
    }

    private class ApiPosition
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("z")]
        public double Z { get; set; }
    }

    private class ApiOutlinePoint
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }
    }

    #endregion

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
    /// tarkov.dev API에서 탈출구 데이터를 가져옵니다.
    /// </summary>
    public async Task<List<MapExtract>> FetchExtractsAsync(Action<string>? progressCallback = null)
    {
        progressCallback?.Invoke("Fetching English extract data...");
        var mapsEn = await FetchMapsWithExtractsAsync("en");

        progressCallback?.Invoke("Fetching Korean extract data...");
        var mapsKo = await FetchMapsWithExtractsAsync("ko");

        // 한국어 번역 lookup
        var koExtractLookup = new Dictionary<string, string>();
        foreach (var map in mapsKo)
        {
            if (map.Extracts != null)
            {
                foreach (var extract in map.Extracts)
                {
                    koExtractLookup[extract.Id] = extract.Name;
                }
            }
        }

        var result = new List<MapExtract>();

        foreach (var map in mapsEn)
        {
            if (map.Extracts == null) continue;

            foreach (var extract in map.Extracts)
            {
                // 위치 정보가 없는 탈출구 스킵
                if (extract.Position == null) continue;

                var nameKo = koExtractLookup.TryGetValue(extract.Id, out var ko) ? ko : null;
                if (nameKo == extract.Name) nameKo = null;

                var mapExtract = new MapExtract
                {
                    Id = extract.Id,
                    Name = extract.Name,
                    NameKo = nameKo,
                    MapId = map.Id,
                    MapName = map.NormalizedName,
                    Faction = ParseFaction(extract.Faction),
                    X = extract.Position.X,
                    Y = extract.Position.Y,
                    Z = extract.Position.Z,
                    Top = extract.Top,
                    Bottom = extract.Bottom
                };

                if (extract.Outline != null && extract.Outline.Count > 0)
                {
                    mapExtract.Outline = extract.Outline.Select(p => new OutlinePoint
                    {
                        X = p.X,
                        Y = p.Y
                    }).ToList();
                }

                result.Add(mapExtract);
            }
        }

        progressCallback?.Invoke($"Found {result.Count} extracts");
        return result;
    }

    private static ExtractFaction ParseFaction(string faction)
    {
        return faction.ToLowerInvariant() switch
        {
            "pmc" => ExtractFaction.Pmc,
            "scav" => ExtractFaction.Scav,
            "shared" => ExtractFaction.Shared,
            _ => ExtractFaction.Shared
        };
    }

    private async Task<List<ApiMap>> FetchMapsWithExtractsAsync(string lang)
    {
        // lang 파라미터 없이 기본 쿼리 사용 (한국어는 별도 처리)
        var query = @"{
            maps {
                id
                name
                normalizedName
                extracts {
                    id
                    name
                    faction
                    position {
                        x
                        y
                        z
                    }
                    outline {
                        x
                        y
                    }
                    top
                    bottom
                }
            }
        }";

        var data = await ExecuteQueryAsync<MapsData>(query);
        return data?.Maps ?? new List<ApiMap>();
    }

    public async Task<bool> LoadFromCacheAsync()
    {
        try
        {
            var filePath = Path.Combine(AppEnv.DataPath, CacheFileName);
            if (!File.Exists(filePath))
                return false;

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var extracts = JsonSerializer.Deserialize<List<MapExtract>>(json, JsonOptions);

            if (extracts != null)
            {
                _allExtracts = extracts;
                BuildLookups();
                _isLoaded = true;
                return true;
            }
        }
        catch
        {
            // 로드 실패 시 false 반환
        }

        return false;
    }

    public async Task SaveToCacheAsync(List<MapExtract> extracts)
    {
        try
        {
            Directory.CreateDirectory(AppEnv.DataPath);
            var filePath = Path.Combine(AppEnv.DataPath, CacheFileName);
            var json = JsonSerializer.Serialize(extracts, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }
        catch
        {
            // 저장 실패 무시
        }
    }

    public async Task RefreshDataAsync(Action<string>? progressCallback = null)
    {
        var extracts = await FetchExtractsAsync(progressCallback);
        await SaveToCacheAsync(extracts);

        _allExtracts = extracts;
        BuildLookups();
        _isLoaded = true;

        progressCallback?.Invoke($"Loaded {_allExtracts.Count} extracts");
    }

    public async Task EnsureLoadedAsync(Action<string>? progressCallback = null)
    {
        if (_isLoaded) return;

        if (await LoadFromCacheAsync())
        {
            progressCallback?.Invoke($"Loaded {_allExtracts.Count} extracts from cache");
            return;
        }

        await RefreshDataAsync(progressCallback);
    }

    private void BuildLookups()
    {
        _extractsByMap.Clear();

        foreach (var extract in _allExtracts)
        {
            var mapKey = extract.MapName.ToLowerInvariant();

            if (!_extractsByMap.TryGetValue(mapKey, out var list))
            {
                list = new List<MapExtract>();
                _extractsByMap[mapKey] = list;
            }

            list.Add(extract);
        }
    }

    /// <summary>
    /// 특정 맵의 탈출구 목록을 반환합니다.
    /// mapKey 또는 Aliases를 사용하여 매칭합니다.
    /// </summary>
    public List<MapExtract> GetExtractsForMap(string mapName, MapConfig? mapConfig = null)
    {
        var mapKey = mapName.ToLowerInvariant();

        // 직접 매칭 시도
        if (_extractsByMap.TryGetValue(mapKey, out var extracts))
            return extracts;

        // MapConfig의 Aliases를 사용하여 매칭 시도
        if (mapConfig?.Aliases != null)
        {
            foreach (var alias in mapConfig.Aliases)
            {
                var aliasKey = alias.ToLowerInvariant();
                if (_extractsByMap.TryGetValue(aliasKey, out extracts))
                    return extracts;
            }
        }

        // 모든 맵의 키와 비교하여 매칭 시도 (부분 일치)
        foreach (var kvp in _extractsByMap)
        {
            // mapKey가 저장된 키를 포함하거나, 저장된 키가 mapKey를 포함하는 경우
            if (kvp.Key.Contains(mapKey) || mapKey.Contains(kvp.Key))
                return kvp.Value;
        }

        return new List<MapExtract>();
    }

    /// <summary>
    /// 특정 맵의 특정 진영 탈출구 목록을 반환합니다.
    /// </summary>
    public List<MapExtract> GetExtractsForMap(string mapName, ExtractFaction? faction)
    {
        var extracts = GetExtractsForMap(mapName);

        if (faction.HasValue)
            return extracts.Where(e => e.Faction == faction.Value).ToList();

        return extracts;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
