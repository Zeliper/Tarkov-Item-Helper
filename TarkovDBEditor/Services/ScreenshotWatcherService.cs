using System.Collections.Concurrent;
using System.IO;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services;

/// <summary>
/// 스크린샷 폴더를 감시하고 새 파일 생성 시 이벤트를 발생시키는 서비스.
/// Singleton 패턴으로 구현.
/// </summary>
public sealed class ScreenshotWatcherService : IDisposable
{
    private static ScreenshotWatcherService? _instance;
    private static readonly object _instanceLock = new();

    /// <summary>
    /// Singleton 인스턴스
    /// </summary>
    public static ScreenshotWatcherService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new ScreenshotWatcherService();
                }
            }
            return _instance;
        }
    }

    private FileSystemWatcher? _watcher;
    private readonly ScreenshotCoordinateParser _parser;
    private readonly ConcurrentDictionary<string, DateTime> _recentFiles = new();
    private readonly object _lock = new();
    private bool _isDisposed;

    /// <summary>
    /// 디바운싱 대기 시간 (밀리초)
    /// </summary>
    public int DebounceDelayMs { get; set; } = 500;

    /// <summary>
    /// 현재 감시 중인 폴더 경로
    /// </summary>
    public string? CurrentWatchPath { get; private set; }

    /// <summary>
    /// 감시 중 여부
    /// </summary>
    public bool IsWatching => _watcher?.EnableRaisingEvents ?? false;

    /// <summary>
    /// 마지막으로 감지된 위치
    /// </summary>
    public EftPosition? CurrentPosition { get; private set; }

    /// <summary>
    /// 새 스크린샷에서 좌표가 감지되었을 때 발생하는 이벤트
    /// </summary>
    public event EventHandler<PositionDetectedEventArgs>? PositionDetected;

    /// <summary>
    /// 파싱 실패 시 발생하는 이벤트
    /// </summary>
    public event EventHandler<ParsingFailedEventArgs>? ParsingFailed;

    /// <summary>
    /// 감시 상태가 변경되었을 때 발생하는 이벤트
    /// </summary>
    public event EventHandler<WatcherStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// 오류 발생 시 이벤트
    /// </summary>
    public event EventHandler<WatcherErrorEventArgs>? Error;

    private ScreenshotWatcherService()
    {
        _parser = new ScreenshotCoordinateParser();
    }

    /// <summary>
    /// 현재 사용 중인 정규식 패턴
    /// </summary>
    public string CurrentPattern => _parser.CurrentPattern;

    /// <summary>
    /// 정규식 패턴을 업데이트합니다.
    /// </summary>
    public bool UpdatePattern(string pattern)
    {
        return _parser.UpdatePattern(pattern);
    }

    /// <summary>
    /// 지정된 폴더 감시를 시작합니다.
    /// </summary>
    public bool StartWatching(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        if (!Directory.Exists(folderPath))
        {
            OnError($"폴더가 존재하지 않습니다: {folderPath}");
            return false;
        }

        lock (_lock)
        {
            try
            {
                // 기존 watcher 정리
                StopWatching();

                _watcher = new FileSystemWatcher(folderPath)
                {
                    Filter = "*.png",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false
                };

                _watcher.Created += OnFileCreated;
                _watcher.Changed += OnFileChanged;
                _watcher.Error += OnWatcherError;

                _watcher.EnableRaisingEvents = true;
                CurrentWatchPath = folderPath;

                OnStateChanged(true, folderPath);
                return true;
            }
            catch (Exception ex)
            {
                OnError($"감시 시작 실패: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 감시를 중지합니다.
    /// </summary>
    public void StopWatching()
    {
        lock (_lock)
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileCreated;
                _watcher.Changed -= OnFileChanged;
                _watcher.Error -= OnWatcherError;
                _watcher.Dispose();
                _watcher = null;

                var previousPath = CurrentWatchPath;
                CurrentWatchPath = null;
                _recentFiles.Clear();

                OnStateChanged(false, previousPath);
            }
        }
    }

    /// <summary>
    /// 감시 폴더를 변경합니다.
    /// </summary>
    public bool ChangeWatchPath(string newFolderPath)
    {
        StopWatching();
        return StartWatching(newFolderPath);
    }

    /// <summary>
    /// 기본 EFT 스크린샷 폴더를 자동 감지합니다.
    /// </summary>
    public string? DetectDefaultScreenshotFolder()
    {
        var possiblePaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Escape from Tarkov", "Screenshots"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "EFT", "Screenshots"),
        };

        foreach (var path in possiblePaths)
        {
            if (Directory.Exists(path))
                return path;
        }

        // OneDrive 경로도 확인
        var oneDrivePath = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrivePath))
        {
            var oneDriveEft = Path.Combine(oneDrivePath, "Documents", "Escape from Tarkov", "Screenshots");
            if (Directory.Exists(oneDriveEft))
                return oneDriveEft;
        }

        return null;
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        ProcessFile(e.FullPath);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        ProcessFile(e.FullPath);
    }

    private void ProcessFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        // 디바운싱: 최근에 처리한 파일인지 확인
        var now = DateTime.UtcNow;
        if (_recentFiles.TryGetValue(fileName, out var lastProcessed))
        {
            if ((now - lastProcessed).TotalMilliseconds < DebounceDelayMs)
                return;
        }

        _recentFiles[fileName] = now;

        // 비동기로 처리 (파일 쓰기 완료 대기)
        Task.Run(async () =>
        {
            try
            {
                // 파일 쓰기 완료 대기
                await Task.Delay(DebounceDelayMs);

                // 파일 접근 가능 여부 확인
                if (!await WaitForFileAccessAsync(filePath, TimeSpan.FromSeconds(5)))
                {
                    OnError($"파일 접근 불가: {fileName}");
                    return;
                }

                // 좌표 파싱
                if (_parser.TryParse(fileName, out var position) && position != null)
                {
                    CurrentPosition = position;
                    OnPositionDetected(position, filePath);
                }
                else
                {
                    OnParsingFailed(fileName, "패턴이 일치하지 않습니다");
                }
            }
            catch (Exception ex)
            {
                OnError($"파일 처리 오류 ({fileName}): {ex.Message}");
            }
            finally
            {
                // 오래된 항목 정리
                CleanupRecentFiles();
            }
        });
    }

    private static async Task<bool> WaitForFileAccessAsync(string filePath, TimeSpan timeout)
    {
        var endTime = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < endTime)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return true;
            }
            catch (IOException)
            {
                await Task.Delay(100);
            }
        }
        return false;
    }

    private void CleanupRecentFiles()
    {
        var threshold = DateTime.UtcNow.AddMinutes(-1);
        var keysToRemove = _recentFiles
            .Where(kvp => kvp.Value < threshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _recentFiles.TryRemove(key, out _);
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        OnError($"FileSystemWatcher 오류: {e.GetException().Message}");

        // 자동 복구 시도
        if (CurrentWatchPath != null)
        {
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                StartWatching(CurrentWatchPath);
            });
        }
    }

    private void OnPositionDetected(EftPosition position, string filePath)
    {
        PositionDetected?.Invoke(this, new PositionDetectedEventArgs(position, filePath));
    }

    private void OnParsingFailed(string fileName, string reason)
    {
        ParsingFailed?.Invoke(this, new ParsingFailedEventArgs(fileName, reason));
    }

    private void OnStateChanged(bool isWatching, string? path)
    {
        StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(isWatching, path));
    }

    private void OnError(string message)
    {
        Error?.Invoke(this, new WatcherErrorEventArgs(message));
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopWatching();
    }
}

/// <summary>
/// 좌표 감지 이벤트 인자
/// </summary>
public sealed class PositionDetectedEventArgs : EventArgs
{
    public EftPosition Position { get; }
    public string FilePath { get; }
    public DateTime DetectedAt { get; } = DateTime.Now;

    public PositionDetectedEventArgs(EftPosition position, string filePath)
    {
        Position = position;
        FilePath = filePath;
    }
}

/// <summary>
/// 파싱 실패 이벤트 인자
/// </summary>
public sealed class ParsingFailedEventArgs : EventArgs
{
    public string FileName { get; }
    public string Reason { get; }

    public ParsingFailedEventArgs(string fileName, string reason)
    {
        FileName = fileName;
        Reason = reason;
    }
}

/// <summary>
/// 감시 상태 변경 이벤트 인자
/// </summary>
public sealed class WatcherStateChangedEventArgs : EventArgs
{
    public bool IsWatching { get; }
    public string? WatchPath { get; }

    public WatcherStateChangedEventArgs(bool isWatching, string? watchPath)
    {
        IsWatching = isWatching;
        WatchPath = watchPath;
    }
}

/// <summary>
/// 오류 이벤트 인자
/// </summary>
public sealed class WatcherErrorEventArgs : EventArgs
{
    public string Message { get; }

    public WatcherErrorEventArgs(string message)
    {
        Message = message;
    }
}
