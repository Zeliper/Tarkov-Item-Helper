using System.IO;
using System.Text.Json;
using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// Legacy Map 추적 기능의 메인 서비스.
/// 스크린샷 감시, 좌표 파싱, 좌표 변환, 설정 관리를 통합합니다.
/// </summary>
public sealed class MapTrackerService : IDisposable
{
    private static MapTrackerService? _instance;
    public static MapTrackerService Instance => _instance ??= new MapTrackerService();

    private readonly ScreenshotCoordinateParser _parser;
    private readonly MapCoordinateTransformer _transformer;
    private readonly ScreenshotWatcherService _watcher;
    private readonly ExtractService _extractService;
    private readonly QuestObjectiveService _objectiveService;

    private MapTrackerSettings _settings;
    private readonly List<ScreenPosition> _trailPositions = new();
    private ScreenPosition? _currentPosition;
    private string? _currentMapKey;
    private bool _isDisposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true  // JSON 역직렬화 시 대소문자 무시
    };

    private static string MapConfigsPath => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "map_configs.json");

    /// <summary>
    /// 현재 화면 좌표
    /// </summary>
    public ScreenPosition? CurrentPosition => _currentPosition;

    /// <summary>
    /// 이동 경로 목록
    /// </summary>
    public IReadOnlyList<ScreenPosition> TrailPositions => _trailPositions.AsReadOnly();

    /// <summary>
    /// 현재 설정
    /// </summary>
    public MapTrackerSettings Settings => _settings;

    /// <summary>
    /// 현재 선택된 맵 키
    /// </summary>
    public string? CurrentMapKey => _currentMapKey;

    /// <summary>
    /// 마지막 위치
    /// </summary>
    public ScreenPosition? LastPosition => _currentPosition;

    /// <summary>
    /// 감시 중 여부
    /// </summary>
    public bool IsWatching => _watcher.IsWatching;

    /// <summary>
    /// 탈출구 서비스
    /// </summary>
    public ExtractService Extracts => _extractService;

    /// <summary>
    /// 퀘스트 목표 서비스
    /// </summary>
    public QuestObjectiveService QuestObjectives => _objectiveService;

    /// <summary>
    /// 좌표 변환기
    /// </summary>
    public MapCoordinateTransformer Transformer => _transformer;

    /// <summary>
    /// 새 위치가 감지되었을 때 발생
    /// </summary>
    public event EventHandler<ScreenPosition>? PositionUpdated;

    /// <summary>
    /// 오류 발생 시 이벤트
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 상태 메시지 이벤트
    /// </summary>
    public event EventHandler<string>? StatusMessage;

    /// <summary>
    /// 감시 상태 변경 이벤트
    /// </summary>
    public event EventHandler<bool>? WatchingStateChanged;

    /// <summary>
    /// 맵 변경 이벤트
    /// </summary>
    public event Action<string>? MapChanged;

    private MapTrackerService()
    {
        _settings = LoadSettings();
        _parser = new ScreenshotCoordinateParser(_settings.FileNamePattern);
        _transformer = new MapCoordinateTransformer(_settings.Maps);
        _watcher = new ScreenshotWatcherService(_parser)
        {
            DebounceDelayMs = _settings.DebounceDelayMs
        };

        _extractService = ExtractService.Instance;
        _objectiveService = QuestObjectiveService.Instance;

        // 이벤트 연결
        _watcher.PositionDetected += OnPositionDetected;
        _watcher.ParsingFailed += OnParsingFailed;
        _watcher.StateChanged += OnWatcherStateChanged;
        _watcher.Error += OnWatcherError;
    }

    /// <summary>
    /// 서비스 초기화 (DB 데이터 로드)
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        var extractsLoaded = await _extractService.LoadAsync();
        var objectivesLoaded = await _objectiveService.LoadAsync();

        if (!extractsLoaded)
            OnStatusMessage("탈출구 데이터 로드 실패");
        if (!objectivesLoaded)
            OnStatusMessage("퀘스트 목표 데이터 로드 실패");

        return extractsLoaded && objectivesLoaded;
    }

    /// <summary>
    /// 스크린샷 폴더 감시를 시작합니다.
    /// </summary>
    public bool StartTracking()
    {
        return StartTracking(_settings.ScreenshotFolderPath);
    }

    /// <summary>
    /// 지정된 폴더에서 스크린샷 감시를 시작합니다.
    /// </summary>
    public bool StartTracking(string folderPath)
    {
        if (_watcher.StartWatching(folderPath))
        {
            OnStatusMessage($"스크린샷 감시 시작: {folderPath}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// 감시를 중지합니다.
    /// </summary>
    public void StopTracking()
    {
        _watcher.StopWatching();
        OnStatusMessage("스크린샷 감시 중지");
    }

    /// <summary>
    /// 이동 경로를 초기화합니다.
    /// </summary>
    public void ClearTrail()
    {
        _trailPositions.Clear();
        _currentPosition = null;
        OnStatusMessage("이동 경로 초기화");
    }

    /// <summary>
    /// 현재 맵을 설정합니다.
    /// </summary>
    public void SetCurrentMap(string mapKey)
    {
        if (_currentMapKey != mapKey)
        {
            _currentMapKey = mapKey;
            MapChanged?.Invoke(mapKey);
        }
    }

    /// <summary>
    /// 맵 설정을 반환합니다.
    /// </summary>
    public MapConfig? GetMapConfig(string mapKey)
    {
        return _transformer.GetMapConfig(mapKey);
    }

    /// <summary>
    /// 모든 맵 키 목록을 반환합니다.
    /// </summary>
    public IReadOnlyList<string> GetAllMapKeys()
    {
        return _transformer.GetAllMapKeys();
    }

    /// <summary>
    /// 맵 이름 또는 별칭을 정규화된 맵 키로 변환합니다.
    /// DB의 MapName이나 다양한 형식의 맵 이름을 config key로 변환할 때 사용합니다.
    /// </summary>
    /// <param name="mapNameOrAlias">맵 이름 또는 별칭 (예: "Ground Zero", "ground-zero")</param>
    /// <returns>정규화된 맵 키 (예: "GroundZero"), 찾지 못하면 null</returns>
    public string? ResolveMapKey(string mapNameOrAlias)
    {
        return _transformer.ResolveMapKey(mapNameOrAlias);
    }

    /// <summary>
    /// 스크린샷 폴더 경로를 변경합니다.
    /// </summary>
    public bool ChangeScreenshotFolder(string newPath)
    {
        if (!Directory.Exists(newPath))
        {
            OnError($"폴더가 존재하지 않습니다: {newPath}");
            return false;
        }

        _settings.ScreenshotFolderPath = newPath;

        if (IsWatching)
        {
            return _watcher.ChangeWatchPath(newPath);
        }
        return true;
    }

    /// <summary>
    /// 게임 좌표를 화면 좌표로 변환합니다.
    /// </summary>
    public ScreenPosition? TransformGameCoordinate(string mapKey, double gameX, double gameZ, double? angle = null)
    {
        if (_transformer.TryTransform(mapKey, gameX, gameZ, angle, out var screenPos))
        {
            return screenPos;
        }
        return null;
    }

    /// <summary>
    /// API 좌표를 화면 좌표로 변환합니다.
    /// </summary>
    public ScreenPosition? TransformApiCoordinate(string mapKey, double apiX, double apiY, double? apiZ)
    {
        if (_transformer.TryTransformApiCoordinate(mapKey, apiX, apiY, apiZ, out var screenPos))
        {
            return screenPos;
        }
        return null;
    }

    /// <summary>
    /// 특정 맵의 탈출구를 화면 좌표로 변환하여 반환합니다.
    /// </summary>
    public List<(MapExtract extract, ScreenPosition position)> GetExtractPositionsForMap(string mapKey)
    {
        var result = new List<(MapExtract, ScreenPosition)>();
        var extracts = _extractService.GetExtractsForMap(mapKey);

        foreach (var extract in extracts)
        {
            var pos = TransformGameCoordinate(mapKey, extract.X, extract.Z);
            if (pos != null)
            {
                result.Add((extract, pos));
            }
        }

        return result;
    }

    /// <summary>
    /// 특정 맵의 퀘스트 목표를 화면 좌표로 변환하여 반환합니다.
    /// </summary>
    public List<(TaskObjectiveWithLocation objective, ScreenPosition position)> GetObjectivePositionsForMap(string mapKey)
    {
        var result = new List<(TaskObjectiveWithLocation, ScreenPosition)>();
        var mapConfig = GetMapConfig(mapKey);
        if (mapConfig == null) return result;

        var objectives = _objectiveService.GetObjectivesForMap(mapKey, mapConfig);

        foreach (var objective in objectives)
        {
            foreach (var location in objective.Locations)
            {
                var pos = TransformGameCoordinate(mapKey, location.X, location.Y);
                if (pos != null)
                {
                    result.Add((objective, pos));
                    break; // 첫 번째 위치만 사용
                }
            }
        }

        return result;
    }

    private void OnPositionDetected(object? sender, PositionDetectedEventArgs e)
    {
        var mapKey = string.IsNullOrEmpty(e.Position.MapName) || e.Position.MapName == "Unknown"
            ? _currentMapKey
            : e.Position.MapName;

        if (string.IsNullOrEmpty(mapKey))
        {
            OnError("맵이 선택되지 않았습니다.");
            return;
        }

        var position = new EftPosition
        {
            MapName = mapKey,
            X = e.Position.X,
            Y = e.Position.Y,
            Z = e.Position.Z,
            Angle = e.Position.Angle,
            Timestamp = e.Position.Timestamp,
            OriginalFileName = e.Position.OriginalFileName
        };

        // 플레이어 마커 전용 변환 사용 (playerMarkerTransform 우선)
        if (_transformer.TryTransformPlayerPosition(position, out var screenPos) && screenPos != null)
        {
            _currentPosition = screenPos;

            if (_settings.ShowTrail)
            {
                _trailPositions.Add(screenPos);
                if (_trailPositions.Count > _settings.MaxTrailPoints)
                {
                    _trailPositions.RemoveAt(0);
                }
            }

            OnStatusMessage($"위치 감지: X={position.X:F1}, Z={position.Z:F1}");
            PositionUpdated?.Invoke(this, screenPos);
        }
        else
        {
            OnError($"맵 '{mapKey}'의 좌표 변환 실패");
        }
    }

    private void OnParsingFailed(object? sender, ParsingFailedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[LegacyMapTracker] 파싱 실패: {e.FileName} - {e.Reason}");
    }

    private void OnWatcherStateChanged(object? sender, WatcherStateChangedEventArgs e)
    {
        WatchingStateChanged?.Invoke(this, e.IsWatching);
    }

    private void OnWatcherError(object? sender, WatcherErrorEventArgs e)
    {
        OnError(e.Message);
    }

    private void OnError(string message)
    {
        ErrorOccurred?.Invoke(this, message);
    }

    private void OnStatusMessage(string message)
    {
        StatusMessage?.Invoke(this, message);
    }

    private static MapTrackerSettings LoadSettings()
    {
        var settings = new MapTrackerSettings();

        // map_configs.json에서 맵 설정 로드
        if (File.Exists(MapConfigsPath))
        {
            try
            {
                var json = File.ReadAllText(MapConfigsPath);
                var configList = JsonSerializer.Deserialize<MapConfigList>(json, JsonOptions);
                if (configList?.Maps != null)
                {
                    settings.Maps = configList.Maps;
                    System.Diagnostics.Debug.WriteLine($"[LegacyMapTracker] {configList.Maps.Count}개 맵 설정 로드됨");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LegacyMapTracker] map_configs.json 로드 실패: {ex.Message}");
            }
        }

        return settings;
    }

    /// <summary>
    /// 설정을 저장합니다.
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            // 사용자 데이터 폴더에 설정 저장
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var tarkovHelperPath = Path.Combine(appDataPath, "TarkovHelper");
            Directory.CreateDirectory(tarkovHelperPath);

            var settingsPath = Path.Combine(tarkovHelperPath, "legacy_map_settings.json");
            var json = System.Text.Json.JsonSerializer.Serialize(_settings, JsonOptions);
            File.WriteAllText(settingsPath, json);
            OnStatusMessage("설정 저장됨");
        }
        catch (Exception ex)
        {
            OnError($"설정 저장 실패: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _watcher.PositionDetected -= OnPositionDetected;
        _watcher.ParsingFailed -= OnParsingFailed;
        _watcher.StateChanged -= OnWatcherStateChanged;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
    }
}
