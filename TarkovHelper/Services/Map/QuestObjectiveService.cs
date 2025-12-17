using TarkovHelper.Models;
using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// Legacy Map용 퀘스트 목표 서비스.
/// QuestObjectiveDbService의 데이터를 Legacy_Map.TaskObjectiveWithLocation으로 변환합니다.
/// </summary>
public sealed class QuestObjectiveService
{
    private static QuestObjectiveService? _instance;
    public static QuestObjectiveService Instance => _instance ??= new QuestObjectiveService();

    private readonly QuestObjectiveDbService _objectiveService;

    private QuestObjectiveService()
    {
        _objectiveService = QuestObjectiveDbService.Instance;
    }

    /// <summary>
    /// 데이터가 로드되었는지 여부
    /// </summary>
    public bool IsLoaded => _objectiveService.IsLoaded;

    /// <summary>
    /// 모든 목표 목록 (속성)
    /// </summary>
    public IReadOnlyList<TaskObjectiveWithLocation> AllObjectives => GetAllObjectives();

    /// <summary>
    /// 퀘스트 목표 데이터를 로드합니다.
    /// </summary>
    public async Task<bool> LoadAsync()
    {
        if (!_objectiveService.IsLoaded)
        {
            return await _objectiveService.LoadObjectivesAsync();
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
            statusCallback?.Invoke("Quest objectives already loaded");
            return;
        }

        statusCallback?.Invoke("Loading quest objectives...");
        await LoadAsync();
        statusCallback?.Invoke($"Loaded {_objectiveService.AllObjectives.Count} quest objectives");
    }

    /// <summary>
    /// 특정 맵의 퀘스트 목표를 반환합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="mapConfig">맵 설정 (별칭 매칭용)</param>
    /// <returns>퀘스트 목표 목록</returns>
    public List<TaskObjectiveWithLocation> GetObjectivesForMap(string mapKey, MapConfig mapConfig)
    {
        var objectives = _objectiveService.GetObjectivesForMap(mapKey, mapConfig);
        return objectives.Select(o => ConvertToTaskObjective(o, mapConfig)).ToList();
    }

    /// <summary>
    /// 모든 퀘스트 목표를 반환합니다.
    /// </summary>
    public List<TaskObjectiveWithLocation> GetAllObjectives()
    {
        return _objectiveService.AllObjectives
            .Select(o => ConvertToTaskObjective(o, null))
            .ToList();
    }

    /// <summary>
    /// 맵별로 그룹화된 퀘스트 목표를 반환합니다.
    /// </summary>
    public Dictionary<string, List<TaskObjectiveWithLocation>> GetObjectivesGroupedByMap()
    {
        var result = new Dictionary<string, List<TaskObjectiveWithLocation>>(StringComparer.OrdinalIgnoreCase);

        foreach (var objective in _objectiveService.AllObjectives)
        {
            var mapName = objective.EffectiveMapName ?? "Unknown";
            var converted = ConvertToTaskObjective(objective, null);

            if (!result.TryGetValue(mapName, out var list))
            {
                list = new List<TaskObjectiveWithLocation>();
                result[mapName] = list;
            }
            list.Add(converted);
        }

        return result;
    }

    /// <summary>
    /// 특정 맵의 활성(미완료) 퀘스트 목표를 반환합니다.
    /// </summary>
    /// <param name="mapName">맵 이름</param>
    /// <param name="progressService">퀘스트 진행 서비스</param>
    /// <returns>활성 퀘스트 목표 목록</returns>
    public List<TaskObjectiveWithLocation> GetActiveObjectivesForMap(string mapName, QuestProgressService progressService)
    {
        var allObjectives = GetAllObjectives();
        var activeObjectives = new List<TaskObjectiveWithLocation>();

        foreach (var obj in allObjectives)
        {
            // 맵 필터링
            bool isOnMap = false;
            foreach (var loc in obj.Locations)
            {
                if (string.Equals(loc.MapName, mapName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(loc.MapNormalizedName, mapName, StringComparison.OrdinalIgnoreCase))
                {
                    isOnMap = true;
                    break;
                }
            }

            if (!isOnMap) continue;

            // 퀘스트 활성 상태 확인
            var task = progressService.GetTask(obj.TaskNormalizedName);
            if (task != null)
            {
                var status = progressService.GetStatus(task);
                if (status == QuestStatus.Active)
                {
                    // 목표 완료 상태 확인
                    obj.IsCompleted = progressService.IsObjectiveCompletedById(obj.ObjectiveId);
                    activeObjectives.Add(obj);
                }
            }
        }

        return activeObjectives;
    }

    /// <summary>
    /// 특정 퀘스트의 모든 목표를 반환합니다.
    /// </summary>
    /// <param name="taskNormalizedName">정규화된 퀘스트 이름</param>
    /// <returns>해당 퀘스트의 목표 목록</returns>
    public List<TaskObjectiveWithLocation> GetObjectivesForTask(string taskNormalizedName)
    {
        return GetAllObjectives()
            .Where(o => string.Equals(o.TaskNormalizedName, taskNormalizedName, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// QuestObjective를 TaskObjectiveWithLocation으로 변환합니다.
    /// </summary>
    private static TaskObjectiveWithLocation ConvertToTaskObjective(QuestObjective obj, MapConfig? mapConfig)
    {
        var result = new TaskObjectiveWithLocation
        {
            ObjectiveId = obj.Id,
            Description = obj.Description,
            DescriptionKo = null, // DB에 한국어 설명이 없으면 null
            Type = ConvertObjectiveType(obj.ObjectiveType),
            TaskNormalizedName = NormalizeQuestName(obj.QuestName),
            TaskName = obj.QuestName,
            TaskNameKo = obj.QuestNameKo,
            Locations = new List<QuestObjectiveLocation>()
        };

        // LocationPoints를 QuestObjectiveLocation으로 변환
        var mapName = obj.EffectiveMapName ?? "Unknown";

        if (obj.LocationPoints.Count > 0)
        {
            // 첫 번째 포인트를 중심점으로 사용
            var firstPoint = obj.LocationPoints[0];
            var location = new QuestObjectiveLocation
            {
                Id = $"{obj.Id}_loc",
                MapId = mapName,
                MapName = mapName,
                MapNormalizedName = NormalizeMapName(mapName),
                X = firstPoint.X,
                Y = firstPoint.Z, // Game Z → Location Y (수평면)
                Z = firstPoint.Y, // Game Y → Location Z (높이)
            };

            // 여러 포인트가 있으면 Outline으로 변환
            if (obj.LocationPoints.Count > 2)
            {
                location.Outline = obj.LocationPoints
                    .Select(p => new OutlinePoint { X = p.X, Y = p.Z })
                    .ToList();
            }

            result.Locations.Add(location);
        }

        // OptionalPoints도 별도 위치로 추가
        foreach (var point in obj.OptionalPoints)
        {
            var optLoc = new QuestObjectiveLocation
            {
                Id = $"{obj.Id}_opt_{result.Locations.Count}",
                MapId = mapName,
                MapName = mapName,
                MapNormalizedName = NormalizeMapName(mapName),
                X = point.X,
                Y = point.Z,
                Z = point.Y
            };
            result.Locations.Add(optLoc);
        }

        return result;
    }

    /// <summary>
    /// QuestObjectiveType을 문자열로 변환합니다.
    /// </summary>
    private static string ConvertObjectiveType(QuestObjectiveType type)
    {
        return type switch
        {
            QuestObjectiveType.Kill => "kill",
            QuestObjectiveType.Collect => "collect",
            QuestObjectiveType.HandOver => "handover",
            QuestObjectiveType.Visit => "visit",
            QuestObjectiveType.Mark => "mark",
            QuestObjectiveType.Stash => "stash",
            QuestObjectiveType.Survive => "survive",
            QuestObjectiveType.Build => "build",
            QuestObjectiveType.Task => "task",
            _ => "custom"
        };
    }

    /// <summary>
    /// 퀘스트 이름을 정규화합니다.
    /// </summary>
    private static string NormalizeQuestName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace("\"", "");
    }

    /// <summary>
    /// 맵 이름을 정규화합니다.
    /// </summary>
    private static string NormalizeMapName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        return name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("streets of tarkov", "streets")
            .Replace("ground zero", "ground-zero")
            .Replace("the lab", "labs");
    }
}
