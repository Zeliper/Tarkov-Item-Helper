using System.IO;
using System.Text.RegularExpressions;

namespace TarkovHelper.Services.Map;

/// <summary>
/// 게임 로그 파일을 감시하여 맵 변경을 감지하는 서비스.
/// application.log 파일에서 맵 로딩 이벤트를 실시간으로 감지합니다.
/// </summary>
public sealed class LogMapWatcherService : IDisposable
{
    private static LogMapWatcherService? _instance;
    public static LogMapWatcherService Instance => _instance ??= new LogMapWatcherService();

    private FileSystemWatcher? _logWatcher;
    private readonly object _watcherLock = new();
    private string? _currentLogFolderPath;
    private string? _lastDetectedMap;
    private readonly Dictionary<string, long> _filePositions = new();
    private DateTime _lastEventTime = DateTime.MinValue;
    private bool _isWatching;
    private bool _isDisposed;

    // 맵 이름 매핑 (게임 내부 이름 -> 맵 설정 키)
    // 맵 설정 키는 map_configs.json의 "key" 필드와 일치해야 함
    private static readonly Dictionary<string, string> MapNameMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // Woods
        { "woods", "Woods" },
        { "woods_preset", "Woods" },

        // Customs
        { "customs", "Customs" },
        { "customs_preset", "Customs" },
        { "bigmap", "Customs" },

        // Shoreline
        { "shoreline", "Shoreline" },
        { "shoreline_preset", "Shoreline" },

        // Interchange
        { "interchange", "Interchange" },
        { "shopping_mall", "Interchange" },

        // Reserve
        { "reserve", "Reserve" },
        { "rezervbase", "Reserve" },
        { "rezerv_base_preset", "Reserve" },

        // Lighthouse
        { "lighthouse", "Lighthouse" },
        { "lighthouse_preset", "Lighthouse" },

        // Streets of Tarkov
        { "tarkovstreets", "StreetsOfTarkov" },
        { "streets", "StreetsOfTarkov" },
        { "city_preset", "StreetsOfTarkov" },

        // Factory - Day/Night are same map
        { "factory", "Factory" },
        { "factory4_day", "Factory" },
        { "factory4_night", "Factory" },
        { "factory_day_preset", "Factory" },
        { "factory_night_preset", "Factory" },

        // Ground Zero - All levels are same map
        { "groundzero", "GroundZero" },
        { "sandbox", "GroundZero" },
        { "sandbox_high", "GroundZero" },
        { "sandbox_start", "GroundZero" },
        { "sandbox_preset", "GroundZero" },
        { "sandbox_high_preset", "GroundZero" },

        // Labs
        { "laboratory", "Labs" },
        { "laboratory_preset", "Labs" },
        { "labs", "Labs" },

        // Labyrinth
        { "labyrinth", "Labyrinth" },
        { "labyrinth_preset", "Labyrinth" },
    };

    // 로그에서 맵 로딩을 감지하는 정규식 패턴들
    // 실제 로그 형식 예시:
    // - scene preset path:maps/laboratory_preset.bundle rcid:laboratory.ScenesPreset.asset
    // - Location: laboratory, Sid: ...
    private static readonly Regex[] MapDetectionPatterns =
    [
        // 맵 프리셋 번들 로딩 (가장 신뢰할 수 있는 패턴)
        new Regex(@"maps/(\w+)_preset\.bundle", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        // TRACE-NetworkGameCreate의 Location 필드 (쉼표로 구분)
        new Regex(@"Location:\s*(\w+),", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    ];

    /// <summary>
    /// 맵 변경 감지 이벤트
    /// </summary>
    public event EventHandler<MapChangedEventArgs>? MapChanged;

    /// <summary>
    /// 감시 상태 변경 이벤트
    /// </summary>
    public event EventHandler<bool>? WatchingStateChanged;

    /// <summary>
    /// 오류 발생 이벤트
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 현재 감시 중인지 여부
    /// </summary>
    public bool IsWatching => _isWatching;

    /// <summary>
    /// 마지막으로 감지된 맵
    /// </summary>
    public string? LastDetectedMap => _lastDetectedMap;

    private LogMapWatcherService() { }

    /// <summary>
    /// 로그 폴더 감시를 시작합니다.
    /// </summary>
    /// <param name="logFolderPath">로그 폴더 경로. null이면 기본 경로 사용.</param>
    public bool StartWatching(string? logFolderPath = null)
    {
        lock (_watcherLock)
        {
            StopWatching();

            // 경로가 지정되지 않으면 기본 경로 사용
            var folderPath = logFolderPath ?? GetDefaultLogFolderPath();

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                ErrorOccurred?.Invoke(this, "로그 폴더가 설정되지 않았거나 존재하지 않습니다.");
                return false;
            }

            _currentLogFolderPath = folderPath;

            try
            {
                // 기본 로그 폴더를 감시 (모든 하위 폴더 포함)
                // 새 게임 세션이 시작되면 새 폴더가 생성될 수 있으므로
                _logWatcher = new FileSystemWatcher(folderPath)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    Filter = "*.log",
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };

                _logWatcher.Changed += OnLogFileChanged;
                _logWatcher.Created += OnLogFileCreated;

                _isWatching = true;
                _filePositions.Clear();

                // 초기 스캔 - 최신 로그에서 마지막 맵 찾기
                var latestFolder = FindLatestLogSubfolder(folderPath);
                ScanLatestLogForMap(latestFolder);

                WatchingStateChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"로그 감시 시작 실패: {ex.Message}");
                _isWatching = false;
                WatchingStateChanged?.Invoke(this, false);
                return false;
            }
        }
    }

    /// <summary>
    /// 기본 EFT 로그 폴더 경로를 반환합니다.
    /// </summary>
    private static string? GetDefaultLogFolderPath()
    {
        // 기본 EFT 로그 경로: %LOCALAPPDATA%\Battlestate Games\EFT\Logs
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var eftLogsPath = Path.Combine(localAppData, "Battlestate Games", "EFT", "Logs");

        if (Directory.Exists(eftLogsPath))
            return eftLogsPath;

        return null;
    }

    /// <summary>
    /// 감시를 중지합니다.
    /// </summary>
    public void StopWatching()
    {
        lock (_watcherLock)
        {
            if (_logWatcher != null)
            {
                _logWatcher.EnableRaisingEvents = false;
                _logWatcher.Changed -= OnLogFileChanged;
                _logWatcher.Created -= OnLogFileCreated;
                _logWatcher.Dispose();
                _logWatcher = null;
            }

            _isWatching = false;
            _filePositions.Clear();
            WatchingStateChanged?.Invoke(this, false);
        }
    }

    /// <summary>
    /// 최신 로그 서브폴더를 찾습니다 (날짜별 폴더가 있는 경우).
    /// </summary>
    private string FindLatestLogSubfolder(string basePath)
    {
        try
        {
            // 로그 폴더 형식 찾기 (예: log_2025.12.04_20-43-45_1.0.0.2.42157)
            var subdirs = Directory.GetDirectories(basePath)
                .Where(d => Regex.IsMatch(Path.GetFileName(d), @"^log_\d{4}\.\d{2}\.\d{2}"))
                .OrderByDescending(d => Directory.GetLastWriteTime(d))
                .ToList();

            if (subdirs.Count > 0)
            {
                return subdirs[0];
            }
        }
        catch
        {
            // 서브폴더 탐색 실패 시 기본 경로 사용
        }

        return basePath;
    }

    /// <summary>
    /// 최신 로그 파일에서 마지막 맵을 스캔합니다.
    /// </summary>
    private void ScanLatestLogForMap(string folderPath)
    {
        try
        {
            // application.log 파일 찾기
            var logFiles = Directory.GetFiles(folderPath, "*application*.log", SearchOption.AllDirectories)
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();

            if (logFiles.Count == 0) return;

            var latestLog = logFiles[0];
            var fileInfo = new FileInfo(latestLog);

            // 파일 끝에서 일정량만 읽기 (최근 이벤트)
            var bytesToRead = Math.Min(fileInfo.Length, 100000); // 100KB
            _filePositions[latestLog] = fileInfo.Length;

            using var stream = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (bytesToRead < fileInfo.Length)
            {
                stream.Seek(-bytesToRead, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream);
            if (bytesToRead < fileInfo.Length)
            {
                reader.ReadLine(); // 첫 줄은 잘릴 수 있으므로 스킵
            }

            string? lastMap = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var map = TryExtractMapFromLine(line);
                if (map != null)
                {
                    lastMap = map;
                }
            }

            if (lastMap != null)
            {
                var previousMap = _lastDetectedMap;
                _lastDetectedMap = lastMap;

                // 초기 스캔에서도 맵 변경 이벤트 발생 (맵 탭 진입 시 현재 맵 자동 선택)
                MapChanged?.Invoke(this, new MapChangedEventArgs
                {
                    NewMapKey = lastMap,
                    PreviousMapKey = previousMap,
                    Timestamp = DateTime.Now
                });
            }
        }
        catch
        {
            // 스캔 실패는 무시
        }
    }

    private void OnLogFileCreated(object sender, FileSystemEventArgs e)
    {
        // 새 로그 파일 생성 시 위치 초기화
        _filePositions[e.FullPath] = 0;
    }

    private void OnLogFileChanged(object sender, FileSystemEventArgs e)
    {
        // 디바운스 (파일 시스템이 여러 이벤트를 발생시킬 수 있음)
        var now = DateTime.Now;
        if ((now - _lastEventTime).TotalMilliseconds < 200)
            return;

        _lastEventTime = now;

        // application.log 파일만 처리
        var fileName = Path.GetFileName(e.FullPath);
        if (!fileName.Contains("application", StringComparison.OrdinalIgnoreCase) &&
            !fileName.StartsWith("log", StringComparison.OrdinalIgnoreCase))
            return;

        Task.Run(() => ProcessLogFileChanges(e.FullPath));
    }

    private void ProcessLogFileChanges(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists) return;

            // 이 파일의 마지막 읽은 위치 가져오기
            var lastPosition = _filePositions.GetValueOrDefault(filePath, 0);

            // 파일이 줄어들었으면 새 파일로 간주
            if (fileInfo.Length < lastPosition)
            {
                lastPosition = 0;
            }

            // 새로운 내용만 읽기
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(lastPosition, SeekOrigin.Begin);

            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var map = TryExtractMapFromLine(line);
                if (map != null && !string.Equals(map, _lastDetectedMap, StringComparison.OrdinalIgnoreCase))
                {
                    var previousMap = _lastDetectedMap;
                    _lastDetectedMap = map;

                    // 맵 변경 이벤트 발생
                    MapChanged?.Invoke(this, new MapChangedEventArgs
                    {
                        NewMapKey = map,
                        PreviousMapKey = previousMap,
                        Timestamp = DateTime.Now
                    });
                }
            }

            _filePositions[filePath] = stream.Position;
        }
        catch
        {
            // 읽기 실패는 무시
        }
    }

    /// <summary>
    /// 로그 라인에서 맵 이름을 추출합니다.
    /// </summary>
    private string? TryExtractMapFromLine(string line)
    {
        foreach (var pattern in MapDetectionPatterns)
        {
            var match = pattern.Match(line);
            if (match.Success && match.Groups.Count > 1)
            {
                var rawMapName = match.Groups[1].Value;
                return NormalizeMapName(rawMapName);
            }
        }

        return null;
    }

    /// <summary>
    /// 게임 내부 맵 이름을 표시용 키로 변환합니다.
    /// </summary>
    private string? NormalizeMapName(string rawMapName)
    {
        if (string.IsNullOrEmpty(rawMapName))
            return null;

        // 매핑된 이름이 있으면 반환
        if (MapNameMapping.TryGetValue(rawMapName, out var mappedName))
        {
            return mappedName;
        }

        // 매핑이 없으면 소문자로 변환하여 반환
        return rawMapName.ToLowerInvariant();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopWatching();
    }
}

/// <summary>
/// 맵 변경 이벤트 인자
/// </summary>
public class MapChangedEventArgs : EventArgs
{
    /// <summary>
    /// 새로 감지된 맵 키
    /// </summary>
    public required string NewMapKey { get; init; }

    /// <summary>
    /// 이전 맵 키 (처음 감지된 경우 null)
    /// </summary>
    public string? PreviousMapKey { get; init; }

    /// <summary>
    /// 감지 시간
    /// </summary>
    public DateTime Timestamp { get; init; }
}
