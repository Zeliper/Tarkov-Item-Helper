using TarkovHelper.Models;
using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// Legacy Map용 탈출구 서비스.
/// MapMarkerDbService의 데이터를 Legacy_Map.MapExtract로 변환합니다.
/// </summary>
public sealed class ExtractService
{
    private static ExtractService? _instance;
    public static ExtractService Instance => _instance ??= new ExtractService();

    private readonly MapMarkerDbService _markerService;

    private ExtractService()
    {
        _markerService = MapMarkerDbService.Instance;
    }

    /// <summary>
    /// 데이터가 로드되었는지 여부
    /// </summary>
    public bool IsLoaded => _markerService.IsLoaded;

    /// <summary>
    /// 모든 탈출구 목록 (속성)
    /// </summary>
    public IReadOnlyList<MapExtract> AllExtracts => GetAllExtracts();

    /// <summary>
    /// 탈출구 데이터를 로드합니다.
    /// </summary>
    public async Task<bool> LoadAsync()
    {
        if (!_markerService.IsLoaded)
        {
            return await _markerService.LoadMarkersAsync();
        }
        return true;
    }

    /// <summary>
    /// 데이터 로드를 보장하고 상태 콜백을 제공합니다.
    /// </summary>
    public async Task EnsureLoadedAsync(Action<string>? statusCallback = null)
    {
        if (IsLoaded)
        {
            statusCallback?.Invoke("Extract data already loaded");
            return;
        }

        statusCallback?.Invoke("Loading extract data...");
        await LoadAsync();
        statusCallback?.Invoke($"Loaded {_markerService.AllMarkers.Count} markers");
    }

    /// <summary>
    /// 특정 맵의 탈출구 목록을 반환합니다.
    /// </summary>
    /// <param name="mapKey">맵 키 (예: "Customs", "Woods")</param>
    /// <returns>탈출구 목록</returns>
    public List<MapExtract> GetExtractsForMap(string mapKey)
    {
        var markers = _markerService.GetExtractionsForMap(mapKey);
        return markers.Select(ConvertToMapExtract).ToList();
    }

    /// <summary>
    /// 특정 맵의 탈출구 목록을 반환합니다 (별칭 지원).
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="config">맵 설정 (별칭 포함)</param>
    /// <returns>탈출구 목록</returns>
    public List<MapExtract> GetExtractsForMap(string mapKey, MapConfig config)
    {
        var result = new List<MapExtract>();
        var seenIds = new HashSet<string>();

        // 메인 맵 키로 검색
        foreach (var extract in GetExtractsForMap(mapKey))
        {
            if (seenIds.Add(extract.Id))
                result.Add(extract);
        }

        // 별칭으로도 검색
        if (config.Aliases != null)
        {
            foreach (var alias in config.Aliases)
            {
                foreach (var extract in GetExtractsForMap(alias))
                {
                    if (seenIds.Add(extract.Id))
                        result.Add(extract);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 특정 맵의 PMC 탈출구만 반환합니다.
    /// </summary>
    public List<MapExtract> GetPmcExtractsForMap(string mapKey)
    {
        var markers = _markerService.GetMarkersForMapByType(mapKey, MarkerType.PmcExtraction);
        return markers.Select(ConvertToMapExtract).ToList();
    }

    /// <summary>
    /// 특정 맵의 Scav 탈출구만 반환합니다.
    /// </summary>
    public List<MapExtract> GetScavExtractsForMap(string mapKey)
    {
        var markers = _markerService.GetMarkersForMapByType(mapKey, MarkerType.ScavExtraction);
        return markers.Select(ConvertToMapExtract).ToList();
    }

    /// <summary>
    /// 특정 맵의 공용 탈출구만 반환합니다.
    /// </summary>
    public List<MapExtract> GetSharedExtractsForMap(string mapKey)
    {
        var markers = _markerService.GetMarkersForMapByType(mapKey, MarkerType.SharedExtraction);
        return markers.Select(ConvertToMapExtract).ToList();
    }

    /// <summary>
    /// 특정 맵의 Transit 마커를 반환합니다.
    /// </summary>
    public List<MapExtract> GetTransitsForMap(string mapKey)
    {
        var markers = _markerService.GetTransitsForMap(mapKey);
        return markers.Select(ConvertToMapExtract).ToList();
    }

    /// <summary>
    /// 모든 탈출구를 반환합니다.
    /// </summary>
    public List<MapExtract> GetAllExtracts()
    {
        return _markerService.AllMarkers
            .Where(m => m.IsExtraction)
            .Select(ConvertToMapExtract)
            .ToList();
    }

    /// <summary>
    /// MapMarker를 MapExtract로 변환합니다.
    /// </summary>
    private static MapExtract ConvertToMapExtract(MapMarker marker)
    {
        return new MapExtract
        {
            Id = marker.Id,
            Name = marker.Name,
            NameKo = marker.NameKo,
            MapId = marker.MapKey,
            MapName = marker.MapKey,
            Faction = ConvertToFaction(marker.Type),
            X = marker.X,
            Y = marker.Y,
            Z = marker.Z,
            // FloorId를 기반으로 Top/Bottom 설정 (필요시 맵 설정에서 가져올 수 있음)
            Top = null,
            Bottom = null
        };
    }

    /// <summary>
    /// MarkerType을 ExtractFaction으로 변환합니다.
    /// </summary>
    private static ExtractFaction ConvertToFaction(MarkerType type)
    {
        return type switch
        {
            MarkerType.PmcExtraction => ExtractFaction.Pmc,
            MarkerType.ScavExtraction => ExtractFaction.Scav,
            MarkerType.SharedExtraction => ExtractFaction.Shared,
            MarkerType.Transit => ExtractFaction.Shared, // Transit은 공용으로 처리
            _ => ExtractFaction.Shared
        };
    }
}
