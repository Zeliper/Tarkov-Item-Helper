using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// Screenshot tracking service for real-time position updates.
/// Monitors EFT screenshot folder for new files and parses coordinates from filenames.
/// </summary>
public sealed class MapTrackerService : IDisposable
{
    private static MapTrackerService? _instance;
    private static readonly object _instanceLock = new();

    public static MapTrackerService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new MapTrackerService();
                }
            }
            return _instance;
        }
    }

    private FileSystemWatcher? _watcher;
    private readonly ConcurrentDictionary<string, DateTime> _recentFiles = new();
    private readonly object _lock = new();
    private bool _isDisposed;
    private Regex _regex;

    /// <summary>
    /// Default pattern for EFT screenshot filenames.
    /// Format: "2025-12-0309-35_-123.42_2.40_114.27_-0.09544_-0.29115_0.02904_-0.95146_7.17_0.png"
    /// </summary>
    public const string DefaultPattern =
        @"\d{4}-\d{2}-\d{2}\d{2}-\d{2}_(?<x>-?\d+\.?\d*)_(?<y>-?\d+\.?\d*)_(?<z>-?\d+\.?\d*)_(?<qx>-?\d+\.?\d*)_(?<qy>-?\d+\.?\d*)_(?<qz>-?\d+\.?\d*)_(?<qw>-?\d+\.?\d*)_";

    /// <summary>
    /// Debounce delay in milliseconds
    /// </summary>
    public int DebounceDelayMs { get; set; } = 500;

    /// <summary>
    /// Current watch folder path
    /// </summary>
    public string? CurrentWatchPath { get; private set; }

    /// <summary>
    /// Whether currently watching for screenshots
    /// </summary>
    public bool IsWatching => _watcher?.EnableRaisingEvents ?? false;

    /// <summary>
    /// Last detected position
    /// </summary>
    public EftPosition? CurrentPosition { get; private set; }

    /// <summary>
    /// Fired when a new position is detected from a screenshot
    /// </summary>
    public event EventHandler<PositionDetectedEventArgs>? PositionDetected;

    /// <summary>
    /// Fired when watch state changes
    /// </summary>
    public event EventHandler<WatcherStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Fired when an error occurs
    /// </summary>
    public event EventHandler<WatcherErrorEventArgs>? Error;

    private MapTrackerService()
    {
        _regex = new Regex(DefaultPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Start watching the specified folder for new screenshots.
    /// </summary>
    public bool StartWatching(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return false;

        if (!Directory.Exists(folderPath))
        {
            OnError($"Folder does not exist: {folderPath}");
            return false;
        }

        lock (_lock)
        {
            try
            {
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
                OnError($"Failed to start watching: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Stop watching for screenshots.
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
    /// Auto-detect EFT screenshot folder.
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

        // Check OneDrive paths
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

        // Debouncing
        var now = DateTime.UtcNow;
        if (_recentFiles.TryGetValue(fileName, out var lastProcessed))
        {
            if ((now - lastProcessed).TotalMilliseconds < DebounceDelayMs)
                return;
        }

        _recentFiles[fileName] = now;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelayMs);

                if (!await WaitForFileAccessAsync(filePath, TimeSpan.FromSeconds(5)))
                {
                    OnError($"Cannot access file: {fileName}");
                    return;
                }

                if (TryParsePosition(fileName, out var position) && position != null)
                {
                    CurrentPosition = position;
                    OnPositionDetected(position, filePath);
                }
            }
            catch (Exception ex)
            {
                OnError($"Error processing file ({fileName}): {ex.Message}");
            }
            finally
            {
                CleanupRecentFiles();
            }
        });
    }

    /// <summary>
    /// Parse position from screenshot filename.
    /// </summary>
    public bool TryParsePosition(string fileName, out EftPosition? position)
    {
        position = null;

        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        try
        {
            var match = _regex.Match(fileName);
            if (!match.Success)
                return false;

            var xGroup = match.Groups["x"];
            var yGroup = match.Groups["y"];

            if (!xGroup.Success || !yGroup.Success)
                return false;

            if (!double.TryParse(xGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                return false;

            if (!double.TryParse(yGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return false;

            double z = 0;
            var zGroup = match.Groups["z"];
            if (zGroup.Success)
            {
                double.TryParse(zGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            }

            double? angle = TryParseQuaternionAngle(match);

            position = new EftPosition
            {
                X = x,
                Y = y,
                Z = z,
                Angle = angle,
                Timestamp = DateTime.Now,
                OriginalFileName = fileName
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double? TryParseQuaternionAngle(Match match)
    {
        var qxGroup = match.Groups["qx"];
        var qyGroup = match.Groups["qy"];
        var qzGroup = match.Groups["qz"];
        var qwGroup = match.Groups["qw"];

        if (!qxGroup.Success || !qyGroup.Success || !qzGroup.Success || !qwGroup.Success)
            return null;

        if (!double.TryParse(qxGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var qx) ||
            !double.TryParse(qyGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var qy) ||
            !double.TryParse(qzGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var qz) ||
            !double.TryParse(qwGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var qw))
            return null;

        // Calculate yaw from quaternion
        var sinyCosp = 2.0 * (qw * qy + qx * qz);
        var cosyCosp = 1.0 - 2.0 * (qy * qy + qz * qz);
        var yaw = Math.Atan2(sinyCosp, cosyCosp);

        // Convert to degrees and add 180 for EFT coordinate system
        var degrees = yaw * 180.0 / Math.PI + 180.0;
        return degrees;
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
        OnError($"FileSystemWatcher error: {e.GetException().Message}");

        // Auto-recovery attempt
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

    private void OnStateChanged(bool isWatching, string? path)
    {
        StateChanged?.Invoke(this, new WatcherStateChangedEventArgs(isWatching, path));
    }

    private void OnError(string message)
    {
        Error?.Invoke(this, new WatcherErrorEventArgs(message));
        System.Diagnostics.Debug.WriteLine($"[MapTrackerService] {message}");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopWatching();
    }
}

/// <summary>
/// Position detected event arguments
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
/// Watcher state changed event arguments
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
/// Watcher error event arguments
/// </summary>
public sealed class WatcherErrorEventArgs : EventArgs
{
    public string Message { get; }

    public WatcherErrorEventArgs(string message)
    {
        Message = message;
    }
}
