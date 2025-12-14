using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// DB map_configs.json에서 로드되는 맵 설정
/// </summary>
public sealed class DbMapConfig
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("svgFileName")]
    public string SvgFileName { get; set; } = string.Empty;

    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }

    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }

    [JsonPropertyName("aliases")]
    public List<string>? Aliases { get; set; }

    [JsonPropertyName("calibratedTransform")]
    public double[]? CalibratedTransform { get; set; }

    /// <summary>
    /// tarkov.dev 변환 정보: [scale, offsetX, scale, offsetY]
    /// </summary>
    [JsonPropertyName("tarkovDevTransform")]
    public double[]? TarkovDevTransform { get; set; }

    /// <summary>
    /// tarkov.dev 좌표 회전 (보통 180)
    /// </summary>
    [JsonPropertyName("coordinateRotation")]
    public double CoordinateRotation { get; set; }

    /// <summary>
    /// tarkov.dev SVG bounds: [[maxX, minY], [minX, maxY]]
    /// </summary>
    [JsonPropertyName("svgBounds")]
    public double[][]? SvgBounds { get; set; }

    /// <summary>
    /// tarkov.market 변환 정보: [scaleX, offsetX, scaleY, offsetY]
    /// screenX = scaleX * gameX + offsetX
    /// screenY = scaleY * gameZ + offsetY
    /// </summary>
    [JsonPropertyName("tarkovMarketTransform")]
    public double[]? TarkovMarketTransform { get; set; }

    [JsonPropertyName("floors")]
    public List<DbMapFloorConfig>? Floors { get; set; }

    /// <summary>
    /// tarkov.dev API 좌표를 화면 좌표로 변환합니다. (퀘스트 마커용)
    /// </summary>
    public (double screenX, double screenY)? GameToScreen(double gameX, double gameZ)
    {
        if (CalibratedTransform == null || CalibratedTransform.Length < 6)
            return null;

        var a = CalibratedTransform[0];
        var b = CalibratedTransform[1];
        var c = CalibratedTransform[2];
        var d = CalibratedTransform[3];
        var tx = CalibratedTransform[4];
        var ty = CalibratedTransform[5];

        var screenX = a * gameX + b * gameZ + tx;
        var screenY = c * gameX + d * gameZ + ty;

        return (screenX, screenY);
    }

    /// <summary>
    /// 실제 게임 좌표를 화면 좌표로 변환합니다. (플레이어 위치용)
    /// tarkov.market의 간단한 변환을 우선 사용하고, 없으면 tarkov.dev 변환을 사용합니다.
    /// </summary>
    public (double screenX, double screenY)? RealGameToScreen(double gameX, double gameZ)
    {
        // tarkov.market 변환 정보가 있으면 사용 (가장 정확함)
        // [scaleX, offsetX, scaleY, offsetY]
        // screenX = scaleX * gameX + offsetX
        // screenY = scaleY * gameZ + offsetY
        if (TarkovMarketTransform != null && TarkovMarketTransform.Length >= 4)
        {
            var scaleX = TarkovMarketTransform[0];
            var offsetX = TarkovMarketTransform[1];
            var scaleY = TarkovMarketTransform[2];
            var offsetY = TarkovMarketTransform[3];

            var screenX = scaleX * gameX + offsetX;
            var screenY = scaleY * gameZ + offsetY;

            return (screenX, screenY);
        }

        // tarkov.dev 변환 정보가 있으면 사용
        if (TarkovDevTransform != null && TarkovDevTransform.Length >= 4 && SvgBounds != null && SvgBounds.Length >= 2)
        {
            var scale = TarkovDevTransform[0];
            var offsetX = TarkovDevTransform[1];
            var offsetY = TarkovDevTransform[3];

            // 180도 회전 적용 (coordinateRotation이 180이면 좌표 부호 반전)
            double rotX = gameX;
            double rotZ = gameZ;
            if (CoordinateRotation == 180)
            {
                rotX = -gameX;
                rotZ = -gameZ;
            }

            // tarkov.dev 내부 좌표로 변환
            var internalX = rotX * scale + offsetX;
            var internalY = rotZ * scale + offsetY;

            // svgBounds: [[maxX, minY], [minX, maxY]]
            var minX = SvgBounds[1][0];
            var maxX = SvgBounds[0][0];
            var minY = SvgBounds[0][1];
            var maxY = SvgBounds[1][1];

            // 내부 좌표를 SVG 좌표로 변환
            var svgX = (internalX - minX) / (maxX - minX) * ImageWidth;
            var svgY = (internalY - minY) / (maxY - minY) * ImageHeight;

            return (svgX, svgY);
        }

        // fallback: CalibratedTransform 사용
        return GameToScreen(gameX, gameZ);
    }

    /// <summary>
    /// ComboBox 표시용
    /// </summary>
    public override string ToString() => DisplayName;
}

/// <summary>
/// DB map_configs.json의 층 설정
/// </summary>
public sealed class DbMapFloorConfig
{
    [JsonPropertyName("layerId")]
    public string LayerId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }
}

/// <summary>
/// DB map_configs.json 전체 구조
/// </summary>
public sealed class DbMapConfigList
{
    [JsonPropertyName("maps")]
    public List<DbMapConfig> Maps { get; set; } = new();
}

/// <summary>
/// SQLite DB에서 맵 마커를 로드하고 관리하는 서비스.
/// tarkov_data.db의 MapMarkers 테이블과 map_configs.json을 사용합니다.
/// </summary>
public sealed class MapMarkerDbService
{
    private static MapMarkerDbService? _instance;
    public static MapMarkerDbService Instance => _instance ??= new MapMarkerDbService();

    private readonly string _databasePath;
    private readonly string _mapConfigsPath;
    private Dictionary<string, List<MapMarker>> _markersByMap = new();
    private Dictionary<string, List<DbQuestObjective>> _objectivesByMap = new();
    private Dictionary<string, DbMapConfig> _mapConfigs = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _aliasToKey = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoaded;
    private bool _configsLoaded;
    private bool _objectivesLoaded;

    /// <summary>
    /// 맵 키 이름 정규화 매핑
    /// </summary>
    private static readonly Dictionary<string, string> MapKeyNormalizer = new(StringComparer.OrdinalIgnoreCase)
    {
        { "bigmap", "Customs" },
        { "customs", "Customs" },
        { "woods", "Woods" },
        { "factory", "Factory" },
        { "factory4_day", "Factory" },
        { "factory4_night", "Factory" },
        { "interchange", "Interchange" },
        { "reserve", "Reserve" },
        { "RezervBase", "Reserve" },
        { "shoreline", "Shoreline" },
        { "labs", "Labs" },
        { "laboratory", "Labs" },
        { "the-lab", "Labs" },
        { "lighthouse", "Lighthouse" },
        { "streets", "StreetsOfTarkov" },
        { "streets-of-tarkov", "StreetsOfTarkov" },
        { "TarkovStreets", "StreetsOfTarkov" },
        { "ground-zero", "GroundZero" },
        { "ground-zero-21", "GroundZero" },
        { "Sandbox", "GroundZero" },
        { "Labyrinth", "Labyrinth" }
    };

    public bool IsLoaded => _isLoaded;
    public bool ConfigsLoaded => _configsLoaded;
    public bool ObjectivesLoaded => _objectivesLoaded;

    /// <summary>
    /// 총 마커 수
    /// </summary>
    public int TotalMarkerCount => _markersByMap.Values.Sum(m => m.Count);

    /// <summary>
    /// 총 퀘스트 목표 수
    /// </summary>
    public int TotalObjectiveCount => _objectivesByMap.Values.Sum(o => o.Count);

    /// <summary>
    /// 로드된 맵 설정 수
    /// </summary>
    public int MapConfigCount => _mapConfigs.Count;

    private MapMarkerDbService()
    {
        // Assets 폴더의 tarkov_data.db 경로
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _databasePath = Path.Combine(appDir, "Assets", "tarkov_data.db");
        _mapConfigsPath = Path.Combine(appDir, "Assets", "DB", "Data", "map_configs.json");
    }

    /// <summary>
    /// DB가 존재하는지 확인
    /// </summary>
    public bool DatabaseExists => File.Exists(_databasePath);

    /// <summary>
    /// map_configs.json이 존재하는지 확인
    /// </summary>
    public bool MapConfigsExists => File.Exists(_mapConfigsPath);

    /// <summary>
    /// DB에서 모든 마커를 로드합니다.
    /// </summary>
    public async Task<bool> LoadMarkersAsync()
    {
        if (!DatabaseExists)
        {
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] Database not found: {_databasePath}");
            return false;
        }

        try
        {
            var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // MapMarkers 테이블 존재 여부 확인
            if (!await TableExistsAsync(connection, "MapMarkers"))
            {
                System.Diagnostics.Debug.WriteLine("[MapMarkerDbService] MapMarkers table not found");
                return false;
            }

            var markers = new List<MapMarker>();
            var sql = "SELECT Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId FROM MapMarkers";

            await using var cmd = new SqliteCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var marker = new MapMarker
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    NameKo = reader.IsDBNull(2) ? null : reader.GetString(2),
                    MarkerType = Enum.TryParse<MapMarkerType>(reader.GetString(3), out var type)
                        ? type
                        : MapMarkerType.PmcExtraction,
                    MapKey = reader.GetString(4),
                    X = reader.GetDouble(5),
                    Y = reader.GetDouble(6),
                    Z = reader.GetDouble(7),
                    FloorId = reader.IsDBNull(8) ? null : reader.GetString(8)
                };
                markers.Add(marker);
            }

            // 맵별로 그룹화
            _markersByMap = markers
                .GroupBy(m => NormalizeMapKey(m.MapKey))
                .ToDictionary(g => g.Key, g => g.ToList());

            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] Loaded {markers.Count} markers from DB");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] Error loading markers: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        var sql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@name", tableName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    /// <summary>
    /// 맵 키를 정규화합니다.
    /// </summary>
    public static string NormalizeMapKey(string mapKey)
    {
        if (MapKeyNormalizer.TryGetValue(mapKey, out var normalized))
            return normalized;
        return mapKey;
    }

    /// <summary>
    /// 특정 맵의 모든 마커를 반환합니다.
    /// </summary>
    public List<MapMarker> GetMarkersForMap(string mapKey)
    {
        var normalizedKey = NormalizeMapKey(mapKey);
        return _markersByMap.TryGetValue(normalizedKey, out var markers)
            ? markers
            : new List<MapMarker>();
    }

    /// <summary>
    /// 특정 맵의 탈출구 마커를 반환합니다.
    /// </summary>
    public List<MapMarker> GetExtractMarkersForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey)
            .Where(m => m.IsExtraction)
            .ToList();
    }

    /// <summary>
    /// 특정 맵의 트랜짓 마커를 반환합니다.
    /// </summary>
    public List<MapMarker> GetTransitMarkersForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey)
            .Where(m => m.IsTransit)
            .ToList();
    }

    /// <summary>
    /// 특정 맵의 PMC 탈출구를 반환합니다.
    /// </summary>
    public List<MapMarker> GetPmcExtractsForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey)
            .Where(m => m.MarkerType == MapMarkerType.PmcExtraction ||
                        m.MarkerType == MapMarkerType.SharedExtraction)
            .ToList();
    }

    /// <summary>
    /// 특정 맵의 Scav 탈출구를 반환합니다.
    /// </summary>
    public List<MapMarker> GetScavExtractsForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey)
            .Where(m => m.MarkerType == MapMarkerType.ScavExtraction ||
                        m.MarkerType == MapMarkerType.SharedExtraction)
            .ToList();
    }

    #region Quest Objectives

    /// <summary>
    /// DB에서 퀘스트 목표를 로드합니다.
    /// </summary>
    public async Task<bool> LoadQuestObjectivesAsync()
    {
        if (!DatabaseExists)
        {
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] Database not found: {_databasePath}");
            return false;
        }

        try
        {
            var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // QuestObjectives 테이블 존재 여부 확인
            if (!await TableExistsAsync(connection, "QuestObjectives"))
            {
                System.Diagnostics.Debug.WriteLine("[MapMarkerDbService] QuestObjectives table not found");
                return false;
            }

            var objectives = new List<DbQuestObjective>();
            var sql = @"
                SELECT o.Id, o.QuestId, o.Description, o.MapName, o.LocationPoints,
                       q.Location as QuestLocation, o.OptionalPoints,
                       q.Name as QuestName, q.NameKO as QuestNameKo
                FROM QuestObjectives o
                LEFT JOIN Quests q ON o.QuestId = q.Id
                WHERE (o.LocationPoints IS NOT NULL AND o.LocationPoints != '')
                   OR (o.OptionalPoints IS NOT NULL AND o.OptionalPoints != '')";

            await using var cmd = new SqliteCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var objective = new DbQuestObjective
                {
                    Id = reader.GetString(0),
                    QuestId = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    MapName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    QuestLocation = reader.IsDBNull(5) ? null : reader.GetString(5),
                    QuestName = reader.IsDBNull(7) ? null : reader.GetString(7),
                    QuestNameKo = reader.IsDBNull(8) ? null : reader.GetString(8)
                };

                var locationJson = reader.IsDBNull(4) ? null : reader.GetString(4);
                objective.ParseLocationPoints(locationJson);

                var optionalJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                objective.ParseOptionalPoints(optionalJson);

                if (objective.HasCoordinates || objective.HasOptionalPoints)
                {
                    objectives.Add(objective);
                }
            }

            // 맵별로 그룹화
            _objectivesByMap = objectives
                .Where(o => !string.IsNullOrEmpty(o.EffectiveMapName))
                .GroupBy(o => NormalizeMapKey(o.EffectiveMapName!))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            _objectivesLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] Loaded {objectives.Count} quest objectives from DB");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] Error loading quest objectives: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 특정 맵의 퀘스트 목표를 반환합니다.
    /// </summary>
    public List<DbQuestObjective> GetObjectivesForMap(string mapKey)
    {
        var normalizedKey = NormalizeMapKey(mapKey);
        return _objectivesByMap.TryGetValue(normalizedKey, out var objectives)
            ? objectives
            : new List<DbQuestObjective>();
    }

    /// <summary>
    /// 모든 맵의 퀘스트 목표를 반환합니다.
    /// </summary>
    public List<DbQuestObjective> GetAllObjectives()
    {
        return _objectivesByMap.Values.SelectMany(o => o).ToList();
    }

    #endregion

    /// <summary>
    /// map_configs.json에서 맵 설정을 로드합니다.
    /// </summary>
    public bool LoadMapConfigs()
    {
        if (!MapConfigsExists)
        {
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] map_configs.json not found: {_mapConfigsPath}");
            return false;
        }

        try
        {
            var json = File.ReadAllText(_mapConfigsPath);
            var configList = JsonSerializer.Deserialize<DbMapConfigList>(json);

            if (configList?.Maps == null)
            {
                System.Diagnostics.Debug.WriteLine("[MapMarkerDbService] Failed to deserialize map_configs.json");
                return false;
            }

            _mapConfigs.Clear();
            _aliasToKey.Clear();

            foreach (var config in configList.Maps)
            {
                if (string.IsNullOrWhiteSpace(config.Key))
                    continue;

                _mapConfigs[config.Key] = config;
                _aliasToKey[config.Key] = config.Key;

                if (config.Aliases != null)
                {
                    foreach (var alias in config.Aliases)
                    {
                        if (!string.IsNullOrWhiteSpace(alias))
                            _aliasToKey[alias] = config.Key;
                    }
                }
            }

            _configsLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] Loaded {_mapConfigs.Count} map configs from JSON");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapMarkerDbService] Error loading map configs: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 맵 키로 DB 맵 설정을 가져옵니다.
    /// </summary>
    public DbMapConfig? GetDbMapConfig(string mapKey)
    {
        if (string.IsNullOrWhiteSpace(mapKey))
            return null;

        // 먼저 alias에서 실제 키 찾기
        if (_aliasToKey.TryGetValue(mapKey, out var actualKey))
            return _mapConfigs.GetValueOrDefault(actualKey);

        // 정규화된 키로 찾기
        var normalizedKey = NormalizeMapKey(mapKey);
        if (_aliasToKey.TryGetValue(normalizedKey, out actualKey))
            return _mapConfigs.GetValueOrDefault(actualKey);

        return _mapConfigs.GetValueOrDefault(normalizedKey);
    }

    /// <summary>
    /// 마커를 화면 좌표로 변환합니다. (DB config 사용)
    /// </summary>
    /// <param name="marker">맵 마커</param>
    /// <returns>화면 좌표 (x, y) 또는 null</returns>
    public (double ScreenX, double ScreenY)? GetMarkerScreenCoords(MapMarker marker)
    {
        var dbConfig = GetDbMapConfig(marker.MapKey);
        if (dbConfig == null)
            return null;

        return dbConfig.GameToScreen(marker.X, marker.Z);
    }

    /// <summary>
    /// 마커를 화면 좌표로 변환합니다. (기존 MapConfig 사용 - 레거시 호환)
    /// </summary>
    /// <param name="marker">맵 마커</param>
    /// <param name="mapConfig">맵 설정 (CalibratedTransform 포함)</param>
    /// <returns>화면 좌표 (x, y) 또는 null</returns>
    public (double ScreenX, double ScreenY)? GetMarkerScreenCoords(
        MapMarker marker,
        MapConfig mapConfig)
    {
        // DB config가 로드되어 있으면 우선 사용
        var dbConfig = GetDbMapConfig(marker.MapKey);
        if (dbConfig?.CalibratedTransform != null)
        {
            return dbConfig.GameToScreen(marker.X, marker.Z);
        }

        // DB 마커의 좌표는 게임 좌표 (X = gameX, Z = gameZ)
        var gameX = marker.X;
        var gameZ = marker.Z;

        // CalibratedTransform 적용: [a, b, c, d, tx, ty]
        // screenX = a * gameX + b * gameZ + tx
        // screenY = c * gameX + d * gameZ + ty
        if (mapConfig.CalibratedTransform == null || mapConfig.CalibratedTransform.Length < 6)
            return null;

        var transform = mapConfig.CalibratedTransform;
        var screenX = transform[0] * gameX + transform[1] * gameZ + transform[4];
        var screenY = transform[2] * gameX + transform[3] * gameZ + transform[5];

        return (screenX, screenY);
    }

    /// <summary>
    /// 데이터를 다시 로드합니다.
    /// </summary>
    public async Task RefreshAsync()
    {
        _isLoaded = false;
        _configsLoaded = false;
        _objectivesLoaded = false;
        _markersByMap.Clear();
        _objectivesByMap.Clear();
        _mapConfigs.Clear();
        _aliasToKey.Clear();

        LoadMapConfigs();
        await LoadMarkersAsync();
        await LoadQuestObjectivesAsync();
    }
}
