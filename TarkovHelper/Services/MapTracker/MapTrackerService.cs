using System.IO;
using System.Text.Json;
using TarkovHelper.Debug;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// 맵 위치 추적 기능의 메인 서비스.
/// 스크린샷 감시, 좌표 파싱, 좌표 변환, 설정 관리를 통합합니다.
/// 설정은 user_data.db (UserSettings 테이블)에 저장됩니다.
/// </summary>
public sealed class MapTrackerService : IDisposable
{
    private static MapTrackerService? _instance;
    public static MapTrackerService Instance => _instance ??= new MapTrackerService();

    private readonly ScreenshotCoordinateParser _parser;
    private readonly MapCoordinateTransformer _transformer;
    private readonly ScreenshotWatcherService _watcher;
    private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

    private const string KeyMapTrackerSettings = "mapTracker.settings";

    private MapTrackerSettings _settings;
    private readonly List<ScreenPosition> _trailPositions = new();
    private ScreenPosition? _currentPosition;
    private string? _currentMapKey;
    private bool _isDisposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Legacy JSON path for migration
    private static string LegacySettingsPath => Path.Combine(AppEnv.DataPath, "map_tracker_settings.json");

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
    /// 감시 중 여부
    /// </summary>
    public bool IsWatching => _watcher.IsWatching;

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

    private MapTrackerService()
    {
        _settings = LoadSettingsInternal();
        _parser = new ScreenshotCoordinateParser(_settings.FileNamePattern);
        _transformer = new MapCoordinateTransformer(_settings.Maps);
        _watcher = new ScreenshotWatcherService(_parser)
        {
            DebounceDelayMs = _settings.DebounceDelayMs
        };

        // 이벤트 연결
        _watcher.PositionDetected += OnPositionDetected;
        _watcher.ParsingFailed += OnParsingFailed;
        _watcher.StateChanged += OnWatcherStateChanged;
        _watcher.Error += OnWatcherError;
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
    /// 스크린샷 감시 시 이 맵의 좌표로 변환됩니다.
    /// </summary>
    public void SetCurrentMap(string mapKey)
    {
        _currentMapKey = mapKey;
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
        SaveSettings();

        if (IsWatching)
        {
            return _watcher.ChangeWatchPath(newPath);
        }
        return true;
    }

    /// <summary>
    /// 파일명 패턴을 변경합니다.
    /// </summary>
    public bool ChangeFileNamePattern(string pattern)
    {
        if (_parser.UpdatePattern(pattern))
        {
            _settings.FileNamePattern = pattern;
            SaveSettings();
            OnStatusMessage("파일명 패턴 변경됨");
            return true;
        }
        OnError("유효하지 않은 정규식 패턴입니다");
        return false;
    }

    /// <summary>
    /// 맵 설정을 업데이트합니다.
    /// </summary>
    public void UpdateMapConfig(MapConfig config)
    {
        var existingIndex = _settings.Maps.FindIndex(m =>
            m.Key.Equals(config.Key, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            _settings.Maps[existingIndex] = config;
        }
        else
        {
            _settings.Maps.Add(config);
        }

        _transformer.UpdateMaps(_settings.Maps);
        SaveSettings();
        OnStatusMessage($"맵 설정 업데이트: {config.Key}");
    }

    /// <summary>
    /// 전체 설정을 저장합니다.
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            _userDataDb.SetSetting(KeyMapTrackerSettings, json);
        }
        catch (Exception ex)
        {
            OnError($"설정 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 설정을 다시 로드합니다.
    /// </summary>
    public void ReloadSettings()
    {
        _settings = LoadSettingsInternal();
        _parser.UpdatePattern(_settings.FileNamePattern);
        _transformer.UpdateMaps(_settings.Maps);
        _watcher.DebounceDelayMs = _settings.DebounceDelayMs;
        OnStatusMessage("설정 다시 로드됨");
    }

    /// <summary>
    /// 특정 맵의 설정을 가져옵니다.
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
    /// 수동으로 좌표를 테스트합니다. (스크린샷 좌표용)
    /// </summary>
    public ScreenPosition? TestCoordinate(string mapKey, double x, double y, double? angle = null)
    {
        if (_transformer.TryTransform(mapKey, x, y, angle, out var screenPos))
        {
            return screenPos;
        }
        return null;
    }

    /// <summary>
    /// 스크린샷 파일명에서 좌표를 파싱하여 테스트합니다.
    /// 지정된 맵에 대해 좌표 변환을 수행합니다.
    /// </summary>
    /// <param name="filePath">스크린샷 파일 경로</param>
    /// <param name="mapKey">대상 맵 키</param>
    /// <returns>변환된 화면 좌표, 실패 시 null</returns>
    public ScreenPosition? ProcessScreenshotFile(string filePath, string mapKey)
    {
        var fileName = Path.GetFileName(filePath);

        if (!_parser.TryParse(fileName, out var parsedPosition) || parsedPosition == null)
        {
            OnError($"파일명 파싱 실패: {fileName}");
            return null;
        }

        // 맵 키를 포함한 새 위치 객체 생성
        var position = new EftPosition
        {
            MapName = mapKey,
            X = parsedPosition.X,
            Y = parsedPosition.Y,
            Z = parsedPosition.Z,
            Angle = parsedPosition.Angle,
            Timestamp = parsedPosition.Timestamp,
            OriginalFileName = parsedPosition.OriginalFileName
        };

        if (_transformer.TryTransform(position, out var screenPos) && screenPos != null)
        {
            _currentPosition = screenPos;

            // 이동 경로에 추가
            if (_settings.ShowTrail)
            {
                _trailPositions.Add(screenPos);
                if (_trailPositions.Count > _settings.MaxTrailPoints)
                {
                    _trailPositions.RemoveAt(0);
                }
            }

            OnStatusMessage($"테스트 위치: X={screenPos.X:F1}, Y={screenPos.Y:F1}, Angle={screenPos.Angle:F1}°");
            PositionUpdated?.Invoke(this, screenPos);
            return screenPos;
        }

        OnError($"맵 '{mapKey}'의 좌표 변환 실패");
        return null;
    }

    /// <summary>
    /// tarkov.dev API 좌표를 화면 좌표로 변환합니다.
    /// Transform 배열과 CoordinateRotation을 사용합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="apiX">API position.x 좌표</param>
    /// <param name="apiY">API position.y 좌표 (높이)</param>
    /// <param name="apiZ">API position.z 좌표</param>
    public ScreenPosition? TransformApiCoordinate(string mapKey, double apiX, double apiY, double? apiZ)
    {
        if (_transformer.TryTransformApiCoordinate(mapKey, apiX, apiY, apiZ, out var screenPos))
        {
            return screenPos;
        }
        return null;
    }

    private void OnPositionDetected(object? sender, PositionDetectedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[MapTracker] OnPositionDetected: X={e.Position.X:F2}, Y={e.Position.Y:F2}, Z={e.Position.Z:F2}");

        // 현재 선택된 맵 사용 (파싱된 맵 이름이 Unknown이거나 비어있는 경우)
        var mapKey = string.IsNullOrEmpty(e.Position.MapName) || e.Position.MapName == "Unknown"
            ? _currentMapKey
            : e.Position.MapName;

        System.Diagnostics.Debug.WriteLine($"[MapTracker] Using mapKey: {mapKey} (currentMapKey: {_currentMapKey})");

        if (string.IsNullOrEmpty(mapKey))
        {
            OnError("맵이 선택되지 않았습니다. 맵을 먼저 선택하세요.");
            System.Diagnostics.Debug.WriteLine("[MapTracker] ERROR: No map selected");
            return;
        }

        // 맵 키를 포함한 새 위치 객체 생성
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

        if (_transformer.TryTransform(position, out var screenPos) && screenPos != null)
        {
            System.Diagnostics.Debug.WriteLine($"[MapTracker] Transform SUCCESS: Screen X={screenPos.X:F1}, Y={screenPos.Y:F1}");
            _currentPosition = screenPos;

            // 이동 경로에 추가
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
            System.Diagnostics.Debug.WriteLine($"[MapTracker] Transform FAILED for map '{mapKey}'");
            OnError($"맵 '{mapKey}'의 좌표 변환 실패");
        }
    }

    private void OnParsingFailed(object? sender, ParsingFailedEventArgs e)
    {
        // 파싱 실패는 로그만 남기고 사용자에게 표시하지 않음
        // (일반 스크린샷 파일도 있을 수 있으므로)
        System.Diagnostics.Debug.WriteLine($"[MapTracker] 파싱 실패: {e.FileName} - {e.Reason}");
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

    private MapTrackerSettings LoadSettingsInternal()
    {
        try
        {
            // First check if JSON migration is needed
            MigrateFromJsonIfNeeded();

            // Load from DB
            var json = _userDataDb.GetSetting(KeyMapTrackerSettings);
            if (!string.IsNullOrEmpty(json))
            {
                var settings = JsonSerializer.Deserialize<MapTrackerSettings>(json, JsonOptions);
                if (settings != null)
                {
                    // 저장된 설정에 Floors 정보가 없는 맵에 기본값 병합
                    MergeDefaultFloors(settings);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapTrackerService] Load settings failed: {ex.Message}");
        }

        return new MapTrackerSettings();
    }

    /// <summary>
    /// Migrate from legacy map_tracker_settings.json if it exists
    /// </summary>
    private void MigrateFromJsonIfNeeded()
    {
        if (!File.Exists(LegacySettingsPath)) return;

        try
        {
            var json = File.ReadAllText(LegacySettingsPath);
            var settings = JsonSerializer.Deserialize<MapTrackerSettings>(json, JsonOptions);

            if (settings != null)
            {
                // Save to DB
                var newJson = JsonSerializer.Serialize(settings, JsonOptions);
                _userDataDb.SetSetting(KeyMapTrackerSettings, newJson);
            }

            // Delete the JSON file after migration
            File.Delete(LegacySettingsPath);
            System.Diagnostics.Debug.WriteLine($"[MapTrackerService] Migrated and deleted: {LegacySettingsPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapTrackerService] Migration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 저장된 설정에 기본 Floors 정보가 없는 맵에 기본값을 병합합니다.
    /// </summary>
    private static void MergeDefaultFloors(MapTrackerSettings settings)
    {
        var defaultSettings = new MapTrackerSettings();
        var defaultMaps = defaultSettings.Maps;

        foreach (var map in settings.Maps)
        {
            // 저장된 맵에 Floors가 없는 경우 기본값에서 복사
            if (map.Floors == null || map.Floors.Count == 0)
            {
                var defaultMap = defaultMaps.FirstOrDefault(m =>
                    string.Equals(m.Key, map.Key, StringComparison.OrdinalIgnoreCase));

                if (defaultMap?.Floors != null && defaultMap.Floors.Count > 0)
                {
                    map.Floors = defaultMap.Floors;
                }
            }
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
