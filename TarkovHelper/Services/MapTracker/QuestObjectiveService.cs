using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TarkovHelper.Debug;
using TarkovHelper.Models;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// tarkov.dev API에서 퀘스트 목표 위치 데이터를 가져오고 관리하는 서비스.
/// 활성 퀘스트의 목표를 맵에 표시하기 위한 데이터를 제공합니다.
/// </summary>
public sealed class QuestObjectiveService : IDisposable
{
    private static QuestObjectiveService? _instance;
    public static QuestObjectiveService Instance => _instance ??= new QuestObjectiveService();

    private readonly HttpClient _httpClient;
    private const string GraphQLEndpoint = "https://api.tarkov.dev/graphql";
    private const string CacheFileName = "quest_objectives.json";

    private List<TaskObjectiveWithLocation> _allObjectives = new();
    private Dictionary<string, List<TaskObjectiveWithLocation>> _objectivesByMap = new();
    private Dictionary<string, List<TaskObjectiveWithLocation>> _objectivesByTask = new();
    private bool _isLoaded;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 데이터 로드 완료 여부
    /// </summary>
    public bool IsLoaded => _isLoaded;

    /// <summary>
    /// 모든 퀘스트 목표 위치
    /// </summary>
    public IReadOnlyList<TaskObjectiveWithLocation> AllObjectives => _allObjectives;

    public QuestObjectiveService()
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

    private class TasksWithZonesData
    {
        [JsonPropertyName("tasks")]
        public List<ApiTaskWithZones>? Tasks { get; set; }
    }

    private class ApiTaskWithZones
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("normalizedName")]
        public string NormalizedName { get; set; } = string.Empty;

        [JsonPropertyName("objectives")]
        public List<ApiObjectiveWithZone>? Objectives { get; set; }
    }

    private class ApiObjectiveWithZone
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("zones")]
        public List<ApiTaskZone>? Zones { get; set; }

        [JsonPropertyName("possibleLocations")]
        public List<ApiPossibleLocation>? PossibleLocations { get; set; }
    }

    private class ApiPossibleLocation
    {
        [JsonPropertyName("map")]
        public ApiZoneMap? Map { get; set; }

        [JsonPropertyName("positions")]
        public List<ApiZonePosition>? Positions { get; set; }
    }

    private class ApiTaskZone
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("map")]
        public ApiZoneMap? Map { get; set; }

        [JsonPropertyName("position")]
        public ApiZonePosition? Position { get; set; }

        [JsonPropertyName("outline")]
        public List<ApiOutlinePoint>? Outline { get; set; }

        [JsonPropertyName("top")]
        public double? Top { get; set; }

        [JsonPropertyName("bottom")]
        public double? Bottom { get; set; }
    }

    private class ApiZoneMap
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("normalizedName")]
        public string NormalizedName { get; set; } = string.Empty;
    }

    private class ApiZonePosition
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

    /// <summary>
    /// GraphQL 쿼리 실행
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
    /// tarkov.dev API에서 퀘스트 목표 위치 데이터를 가져옵니다.
    /// </summary>
    public async Task<List<TaskObjectiveWithLocation>> FetchObjectiveLocationsAsync(
        Action<string>? progressCallback = null)
    {
        progressCallback?.Invoke("Fetching English task objectives...");
        var tasksEn = await FetchTasksWithZonesAsync("en");

        progressCallback?.Invoke("Fetching Korean task objectives...");
        var tasksKo = await FetchTasksWithZonesAsync("ko");

        progressCallback?.Invoke($"Processing {tasksEn.Count} tasks...");

        // 한국어 번역 lookup
        var koTaskLookup = tasksKo.ToDictionary(t => t.Id);
        var koObjectiveLookup = new Dictionary<string, string>();
        foreach (var task in tasksKo)
        {
            if (task.Objectives != null)
            {
                foreach (var obj in task.Objectives)
                {
                    koObjectiveLookup[obj.Id] = obj.Description;
                }
            }
        }

        var result = new List<TaskObjectiveWithLocation>();

        foreach (var task in tasksEn)
        {
            if (task.Objectives == null) continue;

            var taskNameKo = koTaskLookup.TryGetValue(task.Id, out var koTask) ? koTask.Name : null;
            if (taskNameKo == task.Name) taskNameKo = null;

            foreach (var objective in task.Objectives)
            {
                // zones 또는 possibleLocations 중 하나라도 있어야 함
                var hasZones = objective.Zones != null && objective.Zones.Count > 0;
                var hasPossibleLocations = objective.PossibleLocations != null && objective.PossibleLocations.Count > 0;

                if (!hasZones && !hasPossibleLocations)
                    continue;

                var descKo = koObjectiveLookup.TryGetValue(objective.Id, out var ko) ? ko : null;
                if (descKo == objective.Description) descKo = null;

                var objectiveWithLocation = new TaskObjectiveWithLocation
                {
                    ObjectiveId = objective.Id,
                    Description = objective.Description,
                    DescriptionKo = descKo,
                    Type = objective.Type,
                    TaskNormalizedName = task.NormalizedName,
                    TaskName = task.Name,
                    TaskNameKo = taskNameKo,
                    Locations = new List<QuestObjectiveLocation>()
                };

                // zones에서 위치 추가
                if (hasZones)
                {
                    foreach (var zone in objective.Zones!)
                    {
                        if (zone.Map == null || zone.Position == null)
                            continue;

                        var location = new QuestObjectiveLocation
                        {
                            Id = zone.Id,
                            MapId = zone.Map.Id,
                            MapName = zone.Map.Name,
                            MapNormalizedName = zone.Map.NormalizedName,
                            X = zone.Position.X,
                            Y = zone.Position.Y,
                            Z = zone.Position.Z,
                            Top = zone.Top,
                            Bottom = zone.Bottom
                        };

                        // outline 변환
                        if (zone.Outline != null && zone.Outline.Count > 0)
                        {
                            location.Outline = zone.Outline.Select(p => new OutlinePoint
                            {
                                X = p.X,
                                Y = p.Y
                            }).ToList();
                        }

                        objectiveWithLocation.Locations.Add(location);
                    }
                }

                // possibleLocations에서 위치 추가 (findQuestItem 타입)
                if (hasPossibleLocations)
                {
                    foreach (var possibleLoc in objective.PossibleLocations!)
                    {
                        if (possibleLoc.Map == null || possibleLoc.Positions == null)
                            continue;

                        foreach (var pos in possibleLoc.Positions)
                        {
                            var location = new QuestObjectiveLocation
                            {
                                Id = $"{objective.Id}_{possibleLoc.Map.NormalizedName}_{pos.X}_{pos.Y}_{pos.Z}",
                                MapId = possibleLoc.Map.Id,
                                MapName = possibleLoc.Map.Name,
                                MapNormalizedName = possibleLoc.Map.NormalizedName,
                                X = pos.X,
                                Y = pos.Y,
                                Z = pos.Z
                            };

                            objectiveWithLocation.Locations.Add(location);
                        }
                    }
                }

                // 위치가 있는 목표만 추가
                if (objectiveWithLocation.Locations.Count > 0)
                {
                    result.Add(objectiveWithLocation);
                }
            }
        }

        progressCallback?.Invoke($"Found {result.Count} objectives with locations");
        return result;
    }

    private async Task<List<ApiTaskWithZones>> FetchTasksWithZonesAsync(string lang)
    {
        // zones 필드는 특정 TaskObjective 타입에만 존재하므로 인라인 프래그먼트 사용
        var zoneFragment = @"
            zones {
                id
                map {
                    id
                    name
                    normalizedName
                }
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
            }";

        // possibleLocations 필드 (TaskObjectiveQuestItem 전용)
        // findQuestItem 타입의 목표에서 아이템을 찾을 수 있는 위치
        var possibleLocationsFragment = @"
            possibleLocations {
                map {
                    id
                    name
                    normalizedName
                }
                positions {
                    x
                    y
                    z
                }
            }";

        // zones 필드가 있는 타입들을 쿼리
        // TaskObjectiveQuestItem: findQuestItem, plantQuestItem 등 (예: Delivery from the Past)
        // TaskObjectiveItem: plantItem 등
        // TaskObjectiveUseItem: useItem 등
        var query = $@"{{
            tasks(lang: {lang}) {{
                id
                name
                normalizedName
                objectives {{
                    id
                    description
                    type
                    ... on TaskObjectiveBasic {{ {zoneFragment} }}
                    ... on TaskObjectiveMark {{ {zoneFragment} }}
                    ... on TaskObjectiveShoot {{ {zoneFragment} }}
                    ... on TaskObjectiveQuestItem {{ {zoneFragment} {possibleLocationsFragment} }}
                    ... on TaskObjectiveItem {{ {zoneFragment} }}
                    ... on TaskObjectiveUseItem {{ {zoneFragment} }}
                }}
            }}
        }}";

        var data = await ExecuteQueryAsync<TasksWithZonesData>(query);
        return data?.Tasks ?? new List<ApiTaskWithZones>();
    }

    /// <summary>
    /// 캐시 파일에서 데이터를 로드합니다.
    /// </summary>
    public async Task<bool> LoadFromCacheAsync()
    {
        try
        {
            var filePath = Path.Combine(AppEnv.DataPath, CacheFileName);
            if (!File.Exists(filePath))
                return false;

            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var objectives = JsonSerializer.Deserialize<List<TaskObjectiveWithLocation>>(json, JsonOptions);

            if (objectives != null)
            {
                _allObjectives = objectives;
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

    /// <summary>
    /// 캐시 파일에 데이터를 저장합니다.
    /// </summary>
    public async Task SaveToCacheAsync(List<TaskObjectiveWithLocation> objectives)
    {
        try
        {
            Directory.CreateDirectory(AppEnv.DataPath);
            var filePath = Path.Combine(AppEnv.DataPath, CacheFileName);
            var json = JsonSerializer.Serialize(objectives, JsonOptions);
            await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        }
        catch
        {
            // 저장 실패 무시
        }
    }

    /// <summary>
    /// API에서 데이터를 가져와 캐시에 저장하고 로드합니다.
    /// </summary>
    public async Task RefreshDataAsync(Action<string>? progressCallback = null)
    {
        var objectives = await FetchObjectiveLocationsAsync(progressCallback);
        await SaveToCacheAsync(objectives);

        _allObjectives = objectives;
        BuildLookups();
        _isLoaded = true;

        progressCallback?.Invoke($"Loaded {_allObjectives.Count} objectives with locations");
    }

    /// <summary>
    /// 데이터 로드 (캐시 우선, 없으면 API 호출)
    /// </summary>
    public async Task EnsureLoadedAsync(Action<string>? progressCallback = null)
    {
        if (_isLoaded) return;

        if (await LoadFromCacheAsync())
        {
            progressCallback?.Invoke($"Loaded {_allObjectives.Count} objectives from cache");
            return;
        }

        await RefreshDataAsync(progressCallback);
    }

    private void BuildLookups()
    {
        _objectivesByMap.Clear();
        _objectivesByTask.Clear();

        foreach (var objective in _allObjectives)
        {
            // 퀘스트별 lookup
            if (!_objectivesByTask.TryGetValue(objective.TaskNormalizedName, out var taskList))
            {
                taskList = new List<TaskObjectiveWithLocation>();
                _objectivesByTask[objective.TaskNormalizedName] = taskList;
            }
            taskList.Add(objective);

            // 맵별 lookup (여러 키로 등록)
            foreach (var location in objective.Locations)
            {
                var mapKeys = new List<string>();

                // MapName 추가
                if (!string.IsNullOrEmpty(location.MapName))
                    mapKeys.Add(location.MapName.ToLowerInvariant());

                // MapNormalizedName 추가
                if (!string.IsNullOrEmpty(location.MapNormalizedName))
                    mapKeys.Add(location.MapNormalizedName.ToLowerInvariant());

                foreach (var mapKey in mapKeys.Distinct())
                {
                    if (!_objectivesByMap.TryGetValue(mapKey, out var mapList))
                    {
                        mapList = new List<TaskObjectiveWithLocation>();
                        _objectivesByMap[mapKey] = mapList;
                    }

                    // 같은 목표가 여러 위치에 있을 수 있으므로, 목표 자체를 추가
                    // (중복 방지)
                    if (!mapList.Contains(objective))
                    {
                        mapList.Add(objective);
                    }
                }
            }
        }

        System.Diagnostics.Debug.WriteLine($"[QuestObjectiveService] Built lookups: {_objectivesByMap.Count} maps, {_objectivesByTask.Count} tasks");
        foreach (var kvp in _objectivesByMap)
        {
            System.Diagnostics.Debug.WriteLine($"  Map '{kvp.Key}': {kvp.Value.Count} objectives");
        }
    }

    /// <summary>
    /// 특정 맵의 활성 퀘스트 목표 목록을 반환합니다.
    /// </summary>
    /// <param name="mapName">맵 이름 (예: "Customs", "Woods")</param>
    /// <param name="progressService">퀘스트 진행 상태 서비스</param>
    /// <returns>활성 퀘스트의 목표 목록</returns>
    public List<TaskObjectiveWithLocation> GetActiveObjectivesForMap(
        string mapName,
        QuestProgressService progressService)
    {
        var result = new List<TaskObjectiveWithLocation>();
        var mapKey = mapName.ToLowerInvariant();

        if (!_objectivesByMap.TryGetValue(mapKey, out var mapObjectives))
            return result;

        foreach (var objective in mapObjectives)
        {
            // 퀘스트 상태 확인
            // API와 Wiki 간 이름 불일치를 처리하기 위해 여러 정규화된 이름으로 검색
            TarkovTask? task = null;
            foreach (var normalizedName in GetNormalizedTaskNames(objective.TaskNormalizedName))
            {
                task = progressService.GetTask(normalizedName);
                if (task != null) break;
            }
            if (task == null) continue;

            var status = progressService.GetStatus(task);

            // Active 상태인 퀘스트만
            if (status != QuestStatus.Active) continue;

            // 목표 인덱스 설정 (Quests 탭 연동용 - 레거시 호환)
            var objectiveIndex = GetObjectiveIndex(task, objective.Description);
            objective.ObjectiveIndex = objectiveIndex;

            // ObjectiveId 기반으로 완료 상태 확인 (동일 설명 목표 개별 추적)
            objective.IsCompleted = progressService.IsObjectiveCompletedById(objective.ObjectiveId);

            result.Add(objective);
        }

        return result;
    }

    /// <summary>
    /// 특정 퀘스트의 목표 목록을 반환합니다.
    /// </summary>
    public List<TaskObjectiveWithLocation> GetObjectivesForTask(string taskNormalizedName)
    {
        if (_objectivesByTask.TryGetValue(taskNormalizedName, out var objectives))
            return objectives;

        return new List<TaskObjectiveWithLocation>();
    }

    /// <summary>
    /// 퀘스트 이름과 목표 인덱스로 ObjectiveId를 찾습니다.
    /// Quests 탭에서 Map Tracker와 동기화할 때 사용합니다.
    /// </summary>
    public string? GetObjectiveIdByIndex(string taskNormalizedName, int objectiveIndex, TarkovTask? task = null)
    {
        if (!_objectivesByTask.TryGetValue(taskNormalizedName, out var objectives))
            return null;

        if (task?.Objectives == null || objectiveIndex < 0 || objectiveIndex >= task.Objectives.Count)
            return null;

        var targetDescription = task.Objectives[objectiveIndex];

        // 목표 설명으로 매칭하여 ObjectiveId 찾기
        foreach (var objective in objectives)
        {
            if (GetObjectiveIndex(task, objective.Description) == objectiveIndex)
            {
                return objective.ObjectiveId;
            }
        }

        return null;
    }

    /// <summary>
    /// 목표 설명으로 목표 인덱스를 찾습니다.
    /// </summary>
    private static int GetObjectiveIndex(TarkovTask task, string description)
    {
        if (task.Objectives == null || task.Objectives.Count == 0) return -1;
        if (string.IsNullOrEmpty(description)) return -1;

        // 정규화된 설명
        var normalizedDesc = NormalizeText(description);

        // 1차: 정확한 매칭 시도
        for (int i = 0; i < task.Objectives.Count; i++)
        {
            var normalizedObj = NormalizeText(task.Objectives[i]);
            if (normalizedObj == normalizedDesc)
            {
                return i;
            }
        }

        // 2차: 부분 매칭 (한 쪽이 다른 쪽을 포함)
        for (int i = 0; i < task.Objectives.Count; i++)
        {
            var normalizedObj = NormalizeText(task.Objectives[i]);
            if (normalizedObj.Contains(normalizedDesc) || normalizedDesc.Contains(normalizedObj))
            {
                return i;
            }
        }

        // 3차: 핵심 키워드 매칭 (숫자, 아이템명 등)
        for (int i = 0; i < task.Objectives.Count; i++)
        {
            var normalizedObj = NormalizeText(task.Objectives[i]);
            // 두 문자열에서 공통 단어가 50% 이상이면 매칭
            var descWords = normalizedDesc.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var objWords = normalizedObj.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (descWords.Length == 0 || objWords.Length == 0) continue;

            var commonWords = descWords.Intersect(objWords, StringComparer.OrdinalIgnoreCase).Count();
            var minWords = Math.Min(descWords.Length, objWords.Length);

            if (minWords > 0 && (double)commonWords / minWords >= 0.5)
            {
                return i;
            }
        }

        // 4차: 위치 정보가 있는 목표면 맵 이름으로 찾기 (visit, mark 등)
        // 목표가 하나뿐이면 그것으로 반환
        if (task.Objectives.Count == 1)
        {
            return 0;
        }

        System.Diagnostics.Debug.WriteLine($"[GetObjectiveIndex] Failed to match: task={task.NormalizedName}, desc={description}");
        return -1;
    }

    /// <summary>
    /// API 퀘스트 이름을 Wiki 퀘스트 이름 형식으로 정규화합니다.
    /// API와 Wiki 간의 이름 불일치를 해결합니다.
    /// </summary>
    /// <remarks>
    /// 처리하는 케이스:
    /// - PVP ZONE 접미사: "easy-money-part-1-pvp-zone" → "easy-money-part-1"
    /// - 팩션별 퀘스트: "green-corridor-bear" → "green-corridor"
    /// - 특수문자 차이: "hindsight-2020" → "hindsight-20/20"
    /// - 이름 차이: "reserve" → "reserve-quest"
    /// - 숫자 접미사 (반복 퀘스트): "make-amends-2" → "make-amends"
    /// </remarks>
    private static IEnumerable<string> GetNormalizedTaskNames(string taskNormalizedName)
    {
        if (string.IsNullOrEmpty(taskNormalizedName))
            yield break;

        // 원본 이름
        yield return taskNormalizedName;

        // 1. PVP ZONE 접미사 제거: "easy-money-part-1-pvp-zone" → "easy-money-part-1"
        const string pvpZoneSuffix = "-pvp-zone";
        if (taskNormalizedName.EndsWith(pvpZoneSuffix, StringComparison.OrdinalIgnoreCase))
        {
            yield return taskNormalizedName[..^pvpZoneSuffix.Length];
        }

        // 2. 팩션별 퀘스트: "-bear", "-usec" 접미사 제거
        if (taskNormalizedName.EndsWith("-bear", StringComparison.OrdinalIgnoreCase))
        {
            yield return taskNormalizedName[..^5];  // "-bear".Length = 5
        }
        else if (taskNormalizedName.EndsWith("-usec", StringComparison.OrdinalIgnoreCase))
        {
            yield return taskNormalizedName[..^5];  // "-usec".Length = 5
        }

        // 3. 특수문자 차이: "hindsight-2020" → "hindsight-20/20"
        if (taskNormalizedName == "hindsight-2020")
        {
            yield return "hindsight-20/20";
        }

        // 4. 이름 차이: "reserve" → "reserve-quest"
        if (taskNormalizedName == "reserve")
        {
            yield return "reserve-quest";
        }

        // 5. 숫자 접미사 제거 (반복 퀘스트): "make-amends-2" → "make-amends"
        // "-숫자" 패턴 감지 (단, "part-1" 같은 패턴은 제외)
        var lastDashIndex = taskNormalizedName.LastIndexOf('-');
        if (lastDashIndex > 0)
        {
            var suffix = taskNormalizedName[(lastDashIndex + 1)..];
            // 순수 숫자인 경우에만 제거 (예: "-2", "-3")
            // "part-1" 같은 경우는 "1"이지만 앞에 "part-"가 있으므로 제외
            if (int.TryParse(suffix, out _))
            {
                var prefix = taskNormalizedName[..lastDashIndex];
                // "xxx-part" 패턴이 아닌 경우에만
                if (!prefix.EndsWith("-part", StringComparison.OrdinalIgnoreCase))
                {
                    yield return prefix;
                }
            }
        }
    }

    /// <summary>
    /// 텍스트 정규화 (대소문자, 특수문자 제거)
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 소문자로 변환하고 특수문자 제거
        var normalized = text.ToLowerInvariant()
            .Replace("\u2019", "'")  // Right single quotation mark
            .Replace("\u2018", "'")  // Left single quotation mark
            .Replace("\u201C", "\"") // Left double quotation mark
            .Replace("\u201D", "\""); // Right double quotation mark

        // 연속 공백 제거
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized.Trim();
    }

    /// <summary>
    /// 모든 맵 이름 목록을 반환합니다.
    /// </summary>
    public List<string> GetAllMapNames()
    {
        return _objectivesByMap.Keys.ToList();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
