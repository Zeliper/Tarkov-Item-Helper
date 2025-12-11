using TarkovHelper.Models;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// QuestListPage와 MapTrackerPage 간의 양방향 연동을 담당하는 브릿지 서비스.
/// TarkovTask ↔ TarkovMarketMarker 매핑 및 이벤트 전파를 처리합니다.
/// </summary>
public sealed class MarkerQuestBridgeService
{
    private static MarkerQuestBridgeService? _instance;
    public static MarkerQuestBridgeService Instance => _instance ??= new MarkerQuestBridgeService();

    private readonly TarkovMarketMarkerService _markerService;
    private readonly QuestProgressService _progressService;

    // 캐시된 매핑 데이터
    private Dictionary<string, List<TarkovMarketMarker>> _taskToMarkersMap = new();
    private Dictionary<string, TarkovTask?> _markerToTaskMap = new();
    private bool _isMappingBuilt;

    /// <summary>
    /// 퀘스트가 선택되었을 때 발생하는 이벤트
    /// MapTrackerPage에서 구독하여 해당 퀘스트의 마커를 하이라이트
    /// </summary>
    public event Action<QuestSelectedEventArgs>? QuestSelected;

    /// <summary>
    /// 마커가 클릭되었을 때 발생하는 이벤트
    /// QuestListPage에서 구독하여 해당 퀘스트 상세 정보 표시
    /// </summary>
    public event Action<MarkerClickedEventArgs>? MarkerClicked;

    /// <summary>
    /// 퀘스트 진행 상태가 변경되었을 때 발생하는 이벤트
    /// 양쪽 페이지에서 구독하여 UI 업데이트
    /// </summary>
    public event Action<QuestProgressChangedEventArgs>? QuestProgressChanged;

    /// <summary>
    /// 맵 포커스 요청 이벤트
    /// QuestListPage에서 "맵에서 보기" 클릭 시 발생
    /// </summary>
    public event Action<MapFocusRequestEventArgs>? MapFocusRequested;

    public MarkerQuestBridgeService()
    {
        _markerService = TarkovMarketMarkerService.Instance;
        _progressService = QuestProgressService.Instance;

        // 진행상황 변경 이벤트 구독
        _progressService.ProgressChanged += OnProgressChanged;
    }

    /// <summary>
    /// 매핑 데이터 빌드 (서비스 초기화 시 또는 데이터 갱신 시 호출)
    /// </summary>
    public void BuildMappings()
    {
        if (!_markerService.IsLoaded)
        {
            System.Diagnostics.Debug.WriteLine("[MarkerQuestBridgeService] MarkerService not loaded, skipping mapping build");
            return;
        }

        _taskToMarkersMap.Clear();
        _markerToTaskMap.Clear();

        var allTasks = _progressService.AllTasks;

        foreach (var task in allTasks)
        {
            if (task.Ids == null || task.Ids.Count == 0) continue;

            var bsgId = task.Ids[0]; // 첫 번째 ID 사용
            var tmQuest = _markerService.GetQuestByBsgId(bsgId);

            if (tmQuest == null) continue;

            // Task의 TarkovMarketQuestUid 설정 (나중에 직접 접근용)
            task.TarkovMarketQuestUid = tmQuest.Uid;

            // 모든 맵에서 해당 퀘스트의 마커 찾기
            var markers = GetAllMarkersForQuestUid(tmQuest.Uid);

            if (markers.Count > 0)
            {
                var taskKey = task.NormalizedName ?? bsgId;
                _taskToMarkersMap[taskKey] = markers;

                // 역방향 매핑
                foreach (var marker in markers)
                {
                    _markerToTaskMap[marker.Uid] = task;
                }
            }
        }

        _isMappingBuilt = true;

        System.Diagnostics.Debug.WriteLine($"[MarkerQuestBridgeService] Mapping built: {_taskToMarkersMap.Count} tasks with markers, {_markerToTaskMap.Count} markers mapped");
    }

    /// <summary>
    /// 특정 questUid에 해당하는 모든 맵의 마커 반환
    /// </summary>
    private List<TarkovMarketMarker> GetAllMarkersForQuestUid(string questUid)
    {
        var result = new List<TarkovMarketMarker>();
        var maps = new[] { "customs", "woods", "factory", "interchange", "reserve",
                          "shoreline", "labs", "lighthouse", "streets", "ground-zero" };

        foreach (var map in maps)
        {
            var markers = _markerService.GetQuestMarkersForMap(map);
            result.AddRange(markers.Where(m => m.QuestUid == questUid));
        }

        return result;
    }

    /// <summary>
    /// TarkovTask에 해당하는 마커 목록 반환
    /// </summary>
    public List<TarkovMarketMarker> GetMarkersForTask(TarkovTask task)
    {
        if (!_isMappingBuilt) BuildMappings();

        var taskKey = task.NormalizedName ?? task.Ids?.FirstOrDefault() ?? string.Empty;

        if (_taskToMarkersMap.TryGetValue(taskKey, out var markers))
        {
            return markers;
        }

        // 캐시에 없으면 직접 조회
        if (task.Ids != null && task.Ids.Count > 0)
        {
            var bsgId = task.Ids[0];
            var tmQuest = _markerService.GetQuestByBsgId(bsgId);
            if (tmQuest != null)
            {
                return GetAllMarkersForQuestUid(tmQuest.Uid);
            }
        }

        return new List<TarkovMarketMarker>();
    }

    /// <summary>
    /// TarkovTask에 해당하는 특정 맵의 마커 목록 반환
    /// </summary>
    public List<TarkovMarketMarker> GetMarkersForTaskOnMap(TarkovTask task, string mapKey)
    {
        var allMarkers = GetMarkersForTask(task);
        var apiMapName = _markerService.GetApiMapName(mapKey);

        if (string.IsNullOrEmpty(apiMapName))
            return new List<TarkovMarketMarker>();

        return allMarkers.Where(m =>
            string.Equals(m.Map, apiMapName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// TarkovMarketMarker에 해당하는 TarkovTask 반환
    /// </summary>
    public TarkovTask? GetTaskForMarker(TarkovMarketMarker marker)
    {
        if (!_isMappingBuilt) BuildMappings();

        if (_markerToTaskMap.TryGetValue(marker.Uid, out var task))
        {
            return task;
        }

        // 캐시에 없으면 직접 조회
        if (!string.IsNullOrEmpty(marker.QuestUid))
        {
            var tmQuest = _markerService.GetQuestByUid(marker.QuestUid);
            if (tmQuest != null && !string.IsNullOrEmpty(tmQuest.BsgId))
            {
                return _progressService.GetTaskByBsgId(tmQuest.BsgId);
            }
        }

        return null;
    }

    /// <summary>
    /// 특정 퀘스트가 마커를 가지고 있는지 확인
    /// </summary>
    public bool HasMarkers(TarkovTask task)
    {
        return GetMarkersForTask(task).Count > 0;
    }

    /// <summary>
    /// 특정 퀘스트가 특정 맵에 마커를 가지고 있는지 확인
    /// </summary>
    public bool HasMarkersOnMap(TarkovTask task, string mapKey)
    {
        return GetMarkersForTaskOnMap(task, mapKey).Count > 0;
    }

    /// <summary>
    /// 퀘스트의 마커가 있는 맵 목록 반환
    /// </summary>
    public List<string> GetMapsWithMarkers(TarkovTask task)
    {
        var markers = GetMarkersForTask(task);
        return markers.Select(m => m.Map).Distinct().ToList();
    }

    #region Event Triggers (QuestListPage에서 호출)

    /// <summary>
    /// 퀘스트 선택 이벤트 발생 (QuestListPage → MapTrackerPage)
    /// </summary>
    public void SelectQuest(TarkovTask task, string? targetMap = null)
    {
        var markers = string.IsNullOrEmpty(targetMap)
            ? GetMarkersForTask(task)
            : GetMarkersForTaskOnMap(task, targetMap);

        var maps = markers.Select(m => m.Map).Distinct().ToList();

        System.Diagnostics.Debug.WriteLine($"[MarkerQuestBridgeService] Quest selected: {task.Name}, {markers.Count} markers on {maps.Count} maps");

        QuestSelected?.Invoke(new QuestSelectedEventArgs
        {
            Task = task,
            Markers = markers,
            TargetMap = targetMap,
            AvailableMaps = maps
        });
    }

    /// <summary>
    /// 퀘스트 선택 해제
    /// </summary>
    public void DeselectQuest()
    {
        QuestSelected?.Invoke(new QuestSelectedEventArgs
        {
            Task = null,
            Markers = new List<TarkovMarketMarker>(),
            TargetMap = null,
            AvailableMaps = new List<string>()
        });
    }

    /// <summary>
    /// 맵 포커스 요청 (QuestListPage에서 "맵에서 보기" 클릭)
    /// </summary>
    public void RequestMapFocus(TarkovTask task, string mapKey)
    {
        var markers = GetMarkersForTaskOnMap(task, mapKey);

        if (markers.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[MarkerQuestBridgeService] No markers for {task.Name} on {mapKey}");
            return;
        }

        // 첫 번째 마커 위치로 포커스
        var firstMarker = markers[0];

        MapFocusRequested?.Invoke(new MapFocusRequestEventArgs
        {
            Task = task,
            MapKey = mapKey,
            Markers = markers,
            FocusMarker = firstMarker,
            ZoomLevel = 1.5 // 기본 줌 레벨
        });
    }

    #endregion

    #region Event Triggers (MapTrackerPage에서 호출)

    /// <summary>
    /// 마커 클릭 이벤트 발생 (MapTrackerPage → QuestListPage)
    /// </summary>
    public void ClickMarker(TarkovMarketMarker marker)
    {
        var task = GetTaskForMarker(marker);
        var tmQuest = !string.IsNullOrEmpty(marker.QuestUid)
            ? _markerService.GetQuestByUid(marker.QuestUid)
            : null;

        System.Diagnostics.Debug.WriteLine($"[MarkerQuestBridgeService] Marker clicked: {marker.Name}, Task: {task?.Name ?? "N/A"}");

        MarkerClicked?.Invoke(new MarkerClickedEventArgs
        {
            Marker = marker,
            Task = task,
            TarkovMarketQuest = tmQuest,
            QuestStatus = task != null ? _progressService.GetStatus(task) : null
        });
    }

    /// <summary>
    /// 마커에서 목표 완료 처리
    /// </summary>
    public void CompleteObjectiveFromMarker(TarkovMarketMarker marker)
    {
        var task = GetTaskForMarker(marker);
        if (task == null) return;

        // 목표 인덱스 찾기 (마커 이름과 목표 텍스트 매칭)
        // TODO: 더 정교한 매칭 로직 필요
        var objectiveIndex = FindObjectiveIndexForMarker(task, marker);

        if (objectiveIndex >= 0 && !string.IsNullOrEmpty(task.NormalizedName))
        {
            _progressService.SetObjectiveCompleted(task.NormalizedName, objectiveIndex, true);
            System.Diagnostics.Debug.WriteLine($"[MarkerQuestBridgeService] Objective completed: {task.Name}[{objectiveIndex}]");
        }
    }

    private int FindObjectiveIndexForMarker(TarkovTask task, TarkovMarketMarker marker)
    {
        if (task.Objectives == null || string.IsNullOrEmpty(task.NormalizedName)) return -1;

        // 마커 이름이 목표 텍스트에 포함되어 있는지 확인
        for (int i = 0; i < task.Objectives.Count; i++)
        {
            var objective = task.Objectives[i];
            if (objective.Contains(marker.Name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // 첫 번째 미완료 목표 반환 (fallback)
        for (int i = 0; i < task.Objectives.Count; i++)
        {
            if (!_progressService.IsObjectiveCompleted(task.NormalizedName, i))
            {
                return i;
            }
        }

        return -1;
    }

    #endregion

    #region Progress Sync

    private void OnProgressChanged(object? sender, EventArgs e)
    {
        // 매핑 재구성이 필요한 경우
        // (예: 새 퀘스트가 활성화되어 마커가 추가되어야 할 때)
        QuestProgressChanged?.Invoke(new QuestProgressChangedEventArgs());
    }

    /// <summary>
    /// 퀘스트 완료 처리 (양쪽 동기화)
    /// </summary>
    public void CompleteQuestFromBridge(TarkovTask task)
    {
        _progressService.CompleteQuest(task, completePrerequisites: false);
        System.Diagnostics.Debug.WriteLine($"[MarkerQuestBridgeService] Quest completed: {task.Name}");
    }

    /// <summary>
    /// 퀘스트 진행중으로 되돌리기
    /// </summary>
    public void ReactivateQuest(TarkovTask task)
    {
        _progressService.ResetQuest(task);
        System.Diagnostics.Debug.WriteLine($"[MarkerQuestBridgeService] Quest reactivated: {task.Name}");
    }

    #endregion

    #region Statistics

    /// <summary>
    /// 매핑 통계 반환
    /// </summary>
    public MappingStatistics GetStatistics()
    {
        if (!_isMappingBuilt) BuildMappings();

        return new MappingStatistics
        {
            TotalTasksWithMarkers = _taskToMarkersMap.Count,
            TotalMappedMarkers = _markerToTaskMap.Count,
            TasksWithoutMarkers = _progressService.AllTasks.Count - _taskToMarkersMap.Count,
            MarkersByMap = GetMarkerCountByMap()
        };
    }

    private Dictionary<string, int> GetMarkerCountByMap()
    {
        var result = new Dictionary<string, int>();

        foreach (var markers in _taskToMarkersMap.Values)
        {
            foreach (var marker in markers)
            {
                if (!result.ContainsKey(marker.Map))
                    result[marker.Map] = 0;
                result[marker.Map]++;
            }
        }

        return result;
    }

    #endregion
}

#region Event Args

/// <summary>
/// 퀘스트 선택 이벤트 인자
/// </summary>
public class QuestSelectedEventArgs
{
    public TarkovTask? Task { get; set; }
    public List<TarkovMarketMarker> Markers { get; set; } = new();
    public string? TargetMap { get; set; }
    public List<string> AvailableMaps { get; set; } = new();
}

/// <summary>
/// 마커 클릭 이벤트 인자
/// </summary>
public class MarkerClickedEventArgs
{
    public TarkovMarketMarker Marker { get; set; } = null!;
    public TarkovTask? Task { get; set; }
    public TarkovMarketQuest? TarkovMarketQuest { get; set; }
    public QuestStatus? QuestStatus { get; set; }
}

/// <summary>
/// 퀘스트 진행상태 변경 이벤트 인자
/// </summary>
public class QuestProgressChangedEventArgs
{
    public TarkovTask? Task { get; set; }
    public QuestStatus? OldStatus { get; set; }
    public QuestStatus? NewStatus { get; set; }
}

/// <summary>
/// 맵 포커스 요청 이벤트 인자
/// </summary>
public class MapFocusRequestEventArgs
{
    public TarkovTask? Task { get; set; }
    public string MapKey { get; set; } = string.Empty;
    public List<TarkovMarketMarker> Markers { get; set; } = new();
    public TarkovMarketMarker? FocusMarker { get; set; }
    public double ZoomLevel { get; set; } = 1.0;
}

/// <summary>
/// 매핑 통계
/// </summary>
public class MappingStatistics
{
    public int TotalTasksWithMarkers { get; set; }
    public int TotalMappedMarkers { get; set; }
    public int TasksWithoutMarkers { get; set; }
    public Dictionary<string, int> MarkersByMap { get; set; } = new();
}

#endregion
