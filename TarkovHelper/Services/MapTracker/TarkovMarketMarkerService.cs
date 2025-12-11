using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TarkovHelper.Debug;
using TarkovHelper.Models;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// tarkov-market API에서 가져온 마커 데이터를 관리하고 좌표 변환을 수행하는 서비스.
/// QuestObjectiveService와 ExtractService를 대체합니다.
/// </summary>
public sealed class TarkovMarketMarkerService : IDisposable
{
    private static TarkovMarketMarkerService? _instance;
    public static TarkovMarketMarkerService Instance => _instance ??= new TarkovMarketMarkerService();

    private const string MarkersCacheFileName = "tarkov_market_markers.json";
    private const string QuestsCacheFileName = "tarkov_market_quests.json";
    private const int CacheExpirationDays = 7; // 캐시 유효 기간 (일)

    private Dictionary<string, List<TarkovMarketMarker>> _markersByMap = new();
    private Dictionary<string, TarkovMarketQuest> _questsByUid = new();
    private Dictionary<string, TarkovMarketQuest> _questsByBsgId = new();
    private bool _isLoaded;
    private DateTime? _cacheLastUpdated;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 맵 키 이름을 tarkov-market API 맵 이름으로 변환하는 매핑
    /// </summary>
    private static readonly Dictionary<string, string> MapKeyToApiName = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Customs", "customs" },
        { "Woods", "woods" },
        { "Factory", "factory" },
        { "Interchange", "interchange" },
        { "Reserve", "reserve" },
        { "Shoreline", "shoreline" },
        { "Labs", "labs" },
        { "Lighthouse", "lighthouse" },
        { "StreetsOfTarkov", "streets" },
        { "GroundZero", "ground-zero" },
        // Aliases
        { "bigmap", "customs" },
        { "TarkovStreets", "streets" },
        { "streets-of-tarkov", "streets" },
        { "ground-zero-21", "ground-zero" },
        { "Sandbox", "ground-zero" },
        { "laboratory", "labs" },
        { "the-lab", "labs" },
        { "RezervBase", "reserve" },
        { "factory4_day", "factory" },
        { "factory4_night", "factory" }
    };

    public bool IsLoaded => _isLoaded;

    /// <summary>
    /// 캐시가 마지막으로 업데이트된 시간
    /// </summary>
    public DateTime? CacheLastUpdated => _cacheLastUpdated;

    /// <summary>
    /// 캐시가 만료되었는지 확인
    /// </summary>
    public bool IsCacheExpired
    {
        get
        {
            if (!_cacheLastUpdated.HasValue) return true;
            return (DateTime.Now - _cacheLastUpdated.Value).TotalDays > CacheExpirationDays;
        }
    }

    /// <summary>
    /// 총 마커 수
    /// </summary>
    public int TotalMarkerCount => _markersByMap.Values.Sum(m => m.Count);

    public TarkovMarketMarkerService()
    {
    }

    /// <summary>
    /// tarkov-market 좌표를 게임 좌표로 변환합니다.
    /// TM 좌표계: TM_x = gameZ, TM_y = gameX (90° 회전)
    /// </summary>
    public static (double GameX, double GameZ) ConvertTMToGameCoords(double tmX, double tmY)
    {
        // TM_x = gameZ, TM_y = gameX
        // 따라서: gameX = TM_y, gameZ = TM_x
        return (tmY, tmX);
    }

    /// <summary>
    /// 게임 좌표를 tarkov-market 좌표로 변환합니다.
    /// </summary>
    public static (double TMX, double TMY) ConvertGameToTMCoords(double gameX, double gameZ)
    {
        // gameX = TM_y, gameZ = TM_x
        // 따라서: TM_x = gameZ, TM_y = gameX
        return (gameZ, gameX);
    }

    /// <summary>
    /// 캐시 파일에서 데이터를 로드합니다.
    /// </summary>
    public async Task<bool> LoadFromCacheAsync()
    {
        try
        {
            var markersPath = Path.Combine(AppEnv.DataPath, MarkersCacheFileName);
            var questsPath = Path.Combine(AppEnv.DataPath, QuestsCacheFileName);

            if (!File.Exists(markersPath))
            {
                System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] Markers cache not found: {markersPath}");
                return false;
            }

            // 캐시 파일 수정 시간 확인
            var fileInfo = new FileInfo(markersPath);
            _cacheLastUpdated = fileInfo.LastWriteTime;

            // 마커 로드
            var markersJson = await File.ReadAllTextAsync(markersPath, Encoding.UTF8);
            var markers = JsonSerializer.Deserialize<Dictionary<string, List<TarkovMarketMarker>>>(markersJson);
            if (markers != null)
            {
                _markersByMap = markers;
            }

            // 퀘스트 로드 (선택적)
            if (File.Exists(questsPath))
            {
                var questsJson = await File.ReadAllTextAsync(questsPath, Encoding.UTF8);
                var quests = JsonSerializer.Deserialize<List<TarkovMarketQuest>>(questsJson);
                if (quests != null)
                {
                    BuildQuestLookups(quests);
                }
            }

            _isLoaded = true;

            // 통계 로깅
            var totalMarkers = _markersByMap.Values.Sum(m => m.Count);
            var questMarkers = _markersByMap.Values.SelectMany(m => m).Count(m => m.Category == "Quests");
            var extractMarkers = _markersByMap.Values.SelectMany(m => m).Count(m => m.Category == "Extractions");
            System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] Loaded {totalMarkers} markers ({questMarkers} quests, {extractMarkers} extracts) from {_markersByMap.Count} maps");
            System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] Cache last updated: {_cacheLastUpdated:yyyy-MM-dd HH:mm:ss}");
            System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] Available map keys in cache: {string.Join(", ", _markersByMap.Keys)}");
            foreach (var kvp in _markersByMap)
            {
                System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService]   {kvp.Key}: {kvp.Value.Count} markers");
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] LoadFromCacheAsync error: {ex.Message}");
            return false;
        }
    }

    private void BuildQuestLookups(List<TarkovMarketQuest> quests)
    {
        _questsByUid.Clear();
        _questsByBsgId.Clear();

        foreach (var quest in quests)
        {
            _questsByUid[quest.Uid] = quest;

            if (!string.IsNullOrEmpty(quest.BsgId))
            {
                _questsByBsgId[quest.BsgId] = quest;
            }
        }
    }

    /// <summary>
    /// 데이터 로드 (캐시 우선, 만료 시 백그라운드 갱신)
    /// </summary>
    public async Task EnsureLoadedAsync(Action<string>? progressCallback = null)
    {
        if (_isLoaded)
        {
            // 이미 로드됨 - 캐시가 만료되었으면 백그라운드에서 갱신
            if (IsCacheExpired)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RefreshDataAsync();
                        System.Diagnostics.Debug.WriteLine("[TarkovMarketMarkerService] Background cache refresh completed");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] Background refresh failed: {ex.Message}");
                    }
                });
            }
            return;
        }

        progressCallback?.Invoke("Loading tarkov-market markers...");

        if (await LoadFromCacheAsync())
        {
            var totalMarkers = _markersByMap.Values.Sum(m => m.Count);
            progressCallback?.Invoke($"Loaded {totalMarkers} markers from cache");

            // 캐시가 만료되었으면 백그라운드에서 갱신
            if (IsCacheExpired)
            {
                progressCallback?.Invoke($"Cache expired, refreshing in background...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RefreshDataAsync();
                        System.Diagnostics.Debug.WriteLine("[TarkovMarketMarkerService] Background cache refresh completed");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] Background refresh failed: {ex.Message}");
                    }
                });
            }
            return;
        }

        // API에서 데이터 가져오기 (캐시가 없는 경우)
        progressCallback?.Invoke("Fetching markers from tarkov-market API...");
        await RefreshDataAsync(progressCallback);
    }

    /// <summary>
    /// API에서 데이터를 가져와 캐시에 저장하고 로드합니다.
    /// </summary>
    public async Task RefreshDataAsync(Action<string>? progressCallback = null)
    {
        try
        {
            var service = TarkovMarketService.Instance;

            // 마커 가져오기
            progressCallback?.Invoke("Fetching markers...");
            var allMarkers = await service.FetchAllMarkersAsync(progressCallback);

            // 마커 저장
            var markersPath = Path.Combine(AppEnv.DataPath, MarkersCacheFileName);
            Directory.CreateDirectory(AppEnv.DataPath);
            var markersJson = JsonSerializer.Serialize(allMarkers, JsonOptions);
            await File.WriteAllTextAsync(markersPath, markersJson, Encoding.UTF8);

            _markersByMap = allMarkers;

            // 퀘스트 가져오기
            progressCallback?.Invoke("Fetching quests...");
            var quests = await service.FetchQuestsAsync();
            if (quests != null)
            {
                var questsPath = Path.Combine(AppEnv.DataPath, QuestsCacheFileName);
                var questsJson = JsonSerializer.Serialize(quests, JsonOptions);
                await File.WriteAllTextAsync(questsPath, questsJson, Encoding.UTF8);
                BuildQuestLookups(quests);
            }

            _isLoaded = true;
            _cacheLastUpdated = DateTime.Now;

            var totalMarkers = _markersByMap.Values.Sum(m => m.Count);
            progressCallback?.Invoke($"Loaded {totalMarkers} markers from Tarkov Market");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] RefreshDataAsync error: {ex.Message}");
            progressCallback?.Invoke($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// 데이터 갱신 이벤트
    /// </summary>
    public event EventHandler? DataRefreshed;

    /// <summary>
    /// 맵 키를 tarkov-market API 맵 이름으로 변환합니다.
    /// </summary>
    public string? GetApiMapName(string mapKey)
    {
        System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] GetApiMapName called with: '{mapKey}'");

        if (MapKeyToApiName.TryGetValue(mapKey, out var apiName))
        {
            System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] Found mapping: '{mapKey}' -> '{apiName}'");
            return apiName;
        }

        // 직접 매칭 시도 (소문자 변환)
        var lowerKey = mapKey.ToLowerInvariant();
        if (_markersByMap.ContainsKey(lowerKey))
        {
            System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] Direct match: '{mapKey}' -> '{lowerKey}'");
            return lowerKey;
        }

        System.Diagnostics.Debug.WriteLine($"[TarkovMarketMarkerService] No mapping found for: '{mapKey}'. Available maps: {string.Join(", ", _markersByMap.Keys)}");
        return null;
    }

    /// <summary>
    /// 특정 맵의 모든 마커를 반환합니다.
    /// </summary>
    public List<TarkovMarketMarker> GetMarkersForMap(string mapKey)
    {
        var apiMapName = GetApiMapName(mapKey);
        if (apiMapName == null)
            return new List<TarkovMarketMarker>();

        if (_markersByMap.TryGetValue(apiMapName, out var markers))
            return markers;

        return new List<TarkovMarketMarker>();
    }

    /// <summary>
    /// 특정 맵의 탈출구 마커를 반환합니다.
    /// </summary>
    public List<TarkovMarketMarker> GetExtractMarkersForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey)
            .Where(m => m.Category == "Extractions")
            .ToList();
    }

    /// <summary>
    /// 특정 맵의 PMC 탈출구 마커를 반환합니다.
    /// </summary>
    public List<TarkovMarketMarker> GetPmcExtractMarkersForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey)
            .Where(m => m.Category == "Extractions" &&
                       (m.SubCategory == "PMC Extraction" || m.SubCategory == "Co-op Extraction"))
            .ToList();
    }

    /// <summary>
    /// 특정 맵의 Scav 탈출구 마커를 반환합니다.
    /// </summary>
    public List<TarkovMarketMarker> GetScavExtractMarkersForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey)
            .Where(m => m.Category == "Extractions" &&
                       (m.SubCategory == "Scav Extraction" || m.SubCategory == "Co-op Extraction"))
            .ToList();
    }

    /// <summary>
    /// 특정 맵의 퀘스트 마커를 반환합니다.
    /// </summary>
    public List<TarkovMarketMarker> GetQuestMarkersForMap(string mapKey)
    {
        return GetMarkersForMap(mapKey)
            .Where(m => m.Category == "Quests")
            .ToList();
    }

    /// <summary>
    /// 특정 맵의 활성 퀘스트 마커를 반환합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="progressService">퀘스트 진행 상태 서비스</param>
    /// <param name="hideCompleted">완료된 목표 숨기기 여부</param>
    public List<TarkovMarketMarker> GetActiveQuestMarkersForMap(
        string mapKey,
        QuestProgressService progressService,
        bool hideCompleted = false)
    {
        var questMarkers = GetQuestMarkersForMap(mapKey);
        var result = new List<TarkovMarketMarker>();

        foreach (var marker in questMarkers)
        {
            // questUid가 없으면 그냥 표시
            if (string.IsNullOrEmpty(marker.QuestUid))
            {
                result.Add(marker);
                continue;
            }

            // questUid로 퀘스트 찾기
            if (!_questsByUid.TryGetValue(marker.QuestUid, out var tmQuest))
            {
                // 퀘스트 정보가 없어도 표시
                result.Add(marker);
                continue;
            }

            // bsgId로 tarkov.dev task 매칭
            if (string.IsNullOrEmpty(tmQuest.BsgId))
            {
                result.Add(marker);
                continue;
            }

            // progressService에서 퀘스트 상태 확인
            var task = progressService.GetTaskByBsgId(tmQuest.BsgId);
            if (task == null)
            {
                // 매칭되는 태스크가 없으면 표시
                result.Add(marker);
                continue;
            }

            var status = progressService.GetStatus(task);

            // Active 상태인 퀘스트만 표시
            if (status == QuestStatus.Active)
            {
                result.Add(marker);
            }
        }

        return result;
    }

    /// <summary>
    /// 마커의 게임 좌표를 반환합니다 (TM 좌표에서 변환).
    /// </summary>
    public (double GameX, double GameZ)? GetMarkerGameCoords(TarkovMarketMarker marker)
    {
        if (marker.Geometry == null)
            return null;

        return ConvertTMToGameCoords(marker.Geometry.X, marker.Geometry.Y);
    }

    /// <summary>
    /// 마커를 화면 좌표로 변환합니다.
    /// </summary>
    /// <param name="marker">tarkov-market 마커</param>
    /// <param name="mapConfig">맵 설정 (CalibratedTransform 포함)</param>
    /// <returns>화면 좌표 (x, y) 또는 null</returns>
    public (double ScreenX, double ScreenY)? GetMarkerScreenCoords(
        TarkovMarketMarker marker,
        MapConfig mapConfig)
    {
        var gameCoords = GetMarkerGameCoords(marker);
        if (gameCoords == null)
            return null;

        var (gameX, gameZ) = gameCoords.Value;

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
    /// questUid로 퀘스트 정보를 가져옵니다.
    /// </summary>
    public TarkovMarketQuest? GetQuestByUid(string questUid)
    {
        return _questsByUid.TryGetValue(questUid, out var quest) ? quest : null;
    }

    /// <summary>
    /// bsgId로 퀘스트 정보를 가져옵니다.
    /// </summary>
    public TarkovMarketQuest? GetQuestByBsgId(string bsgId)
    {
        return _questsByBsgId.TryGetValue(bsgId, out var quest) ? quest : null;
    }

    public void Dispose()
    {
        // 정리할 리소스 없음
    }
}
