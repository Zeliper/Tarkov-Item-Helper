using System.IO;
using System.Text.Json;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for synchronizing quest progress from Tarkov game logs
    /// </summary>
    public class LogSyncService : IDisposable
    {
        private static LogSyncService? _instance;
        public static LogSyncService Instance => _instance ??= new LogSyncService();

        private static readonly string DebugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TarkovHelper", "logsync_debug.log");

        private static void DebugLog(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(DebugLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
            catch { }
        }

        private FileSystemWatcher? _logWatcher;
        private FileSystemWatcher? _applicationLogWatcher;
        private readonly object _watcherLock = new();
        private DateTime _lastEventTime = DateTime.MinValue;
        private DateTime _lastMapEventTime = DateTime.MinValue;
        private string? _lastModifiedFile;
        private string? _lastMapModifiedFile;
        private bool _isWatching;
        private long _lastApplicationLogPosition;
        private string? _currentMapKey;

        /// <summary>
        /// Event fired when a quest event is detected from logs
        /// </summary>
        public event EventHandler<QuestLogEvent>? QuestEventDetected;

        /// <summary>
        /// Event fired when a map change is detected from logs
        /// </summary>
        public event EventHandler<MapDetectedEventArgs>? MapDetected;

        /// <summary>
        /// Event fired when log monitoring status changes
        /// </summary>
        public event EventHandler<bool>? MonitoringStatusChanged;

        /// <summary>
        /// Whether log monitoring is currently active
        /// </summary>
        public bool IsMonitoring => _isWatching;

        /// <summary>
        /// Currently detected map key
        /// </summary>
        public string? CurrentMapKey => _currentMapKey;

        // Message type codes from logs
        private const int MSG_TYPE_STARTED = 10;
        private const int MSG_TYPE_FAILED = 11;
        private const int MSG_TYPE_COMPLETED = 12;

        // Map name to key mapping (EFT log name -> map_configs.json key)
        // All keys are stored in lowercase for case-insensitive matching
        // Use TryGetMapKey() method for lookups instead of direct dictionary access
        //
        // EFT uses two patterns in logs:
        // 1. scene preset path:maps/<name>.bundle (e.g., "maps/shoreline_preset.bundle")
        // 2. [Transit] Locations:<name> (e.g., "Locations:Shoreline")
        private static readonly Dictionary<string, string> MapNameToKey = new(StringComparer.OrdinalIgnoreCase)
        {
            // Woods
            // Transit: "Woods", Preset: "woods_preset"
            { "woods", "Woods" },
            { "woods_preset", "Woods" },

            // Customs
            // Transit: "bigmap", Preset: "customs_preset"
            { "customs", "Customs" },
            { "customs_preset", "Customs" },
            { "bigmap", "Customs" },
            { "bigmap_preset", "Customs" },

            // Shoreline
            // Transit: "Shoreline", Preset: "shoreline_preset"
            { "shoreline", "Shoreline" },
            { "shoreline_preset", "Shoreline" },

            // Interchange
            // Transit: "Interchange", Preset: "shopping_mall"
            { "interchange", "Interchange" },
            { "interchange_preset", "Interchange" },
            { "shopping_mall", "Interchange" },
            { "shopping_mall_preset", "Interchange" },

            // Reserve
            // Transit: "RezervBase", Preset: "rezerv_base_preset"
            { "reserve", "Reserve" },
            { "rezervbase", "Reserve" },
            { "rezerv_base", "Reserve" },
            { "rezerv_base_preset", "Reserve" },
            { "rezervbase_preset", "Reserve" },

            // Lighthouse
            // Transit: "Lighthouse", Preset: "lighthouse_preset"
            { "lighthouse", "Lighthouse" },
            { "lighthouse_preset", "Lighthouse" },

            // Streets of Tarkov
            // Transit: "TarkovStreets", Preset: "city_preset"
            { "streetsoftarkov", "StreetsOfTarkov" },
            { "streets", "StreetsOfTarkov" },
            { "tarkovstreets", "StreetsOfTarkov" },
            { "tarkovstreets_preset", "StreetsOfTarkov" },
            { "city", "StreetsOfTarkov" },
            { "city_preset", "StreetsOfTarkov" },

            // Factory (Day/Night variants)
            // Transit: "factory4_day", "factory4_night", Preset: "factory_day_preset", "factory_night_preset"
            { "factory", "Factory" },
            { "factory4", "Factory" },
            { "factory4_day", "Factory" },
            { "factory4_night", "Factory" },
            { "factory_day", "Factory" },
            { "factory_night", "Factory" },
            { "factory_day_preset", "Factory" },
            { "factory_night_preset", "Factory" },
            { "factory4_day_preset", "Factory" },
            { "factory4_night_preset", "Factory" },

            // Ground Zero (Sandbox_start for level 1-20, Sandbox_high for level 21+)
            // Transit: "Sandbox_high", "Sandbox_start", Preset: "sandbox_high_preset", "sandbox_start_preset"
            { "groundzero", "GroundZero" },
            { "sandbox", "GroundZero" },
            { "sandbox_high", "GroundZero" },
            { "sandbox_start", "GroundZero" },
            { "sandbox_preset", "GroundZero" },
            { "sandbox_high_preset", "GroundZero" },
            { "sandbox_start_preset", "GroundZero" },

            // Labs
            // Transit: "laboratory", Preset: "laboratory_preset"
            { "labs", "Labs" },
            { "lab", "Labs" },
            { "laboratory", "Labs" },
            { "thelab", "Labs" },
            { "laboratory_preset", "Labs" },

            // Labyrinth (if available)
            { "labyrinth", "Labyrinth" },
            { "thelabyrinth", "Labyrinth" },
            { "labyrinth_preset", "Labyrinth" },
        };

        /// <summary>
        /// Try to get the map key from a map name (case-insensitive)
        /// </summary>
        private static bool TryGetMapKey(string mapName, out string? mapKey)
        {
            mapKey = null;
            if (string.IsNullOrEmpty(mapName))
                return false;

            // Direct lookup (case-insensitive due to StringComparer.OrdinalIgnoreCase)
            if (MapNameToKey.TryGetValue(mapName, out mapKey))
                return true;

            // Try removing common suffixes and prefixes
            var cleanedName = mapName
                .Replace("_preset", "")
                .Replace("preset_", "")
                .Replace("_high", "")
                .Replace("_low", "")
                .Replace("_day", "")
                .Replace("_night", "")
                .Trim();

            if (!string.IsNullOrEmpty(cleanedName) && MapNameToKey.TryGetValue(cleanedName, out mapKey))
                return true;

            return false;
        }

        private LogSyncService() { }

        #region Log File Monitoring

        /// <summary>
        /// Start monitoring log folder for quest events and map detection
        /// </summary>
        public void StartMonitoring(string logFolderPath)
        {
            lock (_watcherLock)
            {
                StopMonitoring();

                DebugLog($"StartMonitoring called with path: {logFolderPath}");

                if (string.IsNullOrEmpty(logFolderPath) || !Directory.Exists(logFolderPath))
                {
                    DebugLog($"Invalid path or directory does not exist");
                    return;
                }

                try
                {
                    // Quest event watcher (push-notifications logs)
                    _logWatcher = new FileSystemWatcher(logFolderPath)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        Filter = "*push-notifications*.log",
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    _logWatcher.Changed += OnLogFileChanged;
                    _logWatcher.Created += OnLogFileChanged;

                    // Map detection watcher (application logs)
                    _applicationLogWatcher = new FileSystemWatcher(logFolderPath)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        Filter = "application*.log",
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    _applicationLogWatcher.Changed += OnApplicationLogChanged;
                    _applicationLogWatcher.Created += OnApplicationLogChanged;

                    // Initialize position for latest application log
                    InitializeLatestApplicationLogPosition(logFolderPath);

                    _isWatching = true;
                    MonitoringStatusChanged?.Invoke(this, true);
                }
                catch
                {
                    _isWatching = false;
                    MonitoringStatusChanged?.Invoke(this, false);
                }
            }
        }

        /// <summary>
        /// Initialize position to end of latest application log file
        /// </summary>
        private void InitializeLatestApplicationLogPosition(string logFolderPath)
        {
            try
            {
                var latestLog = Directory.GetFiles(logFolderPath, "application*.log", SearchOption.AllDirectories)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();

                if (latestLog != null && File.Exists(latestLog))
                {
                    var fileInfo = new FileInfo(latestLog);
                    _lastApplicationLogPosition = fileInfo.Length;
                    _lastMapModifiedFile = latestLog;
                }
            }
            catch
            {
                _lastApplicationLogPosition = 0;
            }
        }

        /// <summary>
        /// Stop monitoring log folder
        /// </summary>
        public void StopMonitoring()
        {
            lock (_watcherLock)
            {
                if (_logWatcher != null)
                {
                    _logWatcher.EnableRaisingEvents = false;
                    _logWatcher.Changed -= OnLogFileChanged;
                    _logWatcher.Created -= OnLogFileChanged;
                    _logWatcher.Dispose();
                    _logWatcher = null;
                }

                if (_applicationLogWatcher != null)
                {
                    _applicationLogWatcher.EnableRaisingEvents = false;
                    _applicationLogWatcher.Changed -= OnApplicationLogChanged;
                    _applicationLogWatcher.Created -= OnApplicationLogChanged;
                    _applicationLogWatcher.Dispose();
                    _applicationLogWatcher = null;
                }

                _lastApplicationLogPosition = 0;
                _currentMapKey = null;

                _isWatching = false;
                MonitoringStatusChanged?.Invoke(this, false);
            }
        }

        private void OnLogFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce events (file system can fire multiple events)
            var now = DateTime.Now;
            if ((now - _lastEventTime).TotalMilliseconds < 500 && e.FullPath == _lastModifiedFile)
                return;

            _lastEventTime = now;
            _lastModifiedFile = e.FullPath;

            // Process new events from the modified file
            Task.Run(() => ProcessLatestLogEvents(e.FullPath));
        }

        private void OnApplicationLogChanged(object sender, FileSystemEventArgs e)
        {
            DebugLog($"OnApplicationLogChanged: {e.FullPath}");

            // Debounce events
            var now = DateTime.Now;
            if ((now - _lastMapEventTime).TotalMilliseconds < 300 && e.FullPath == _lastMapModifiedFile)
            {
                DebugLog($"Debounced - skipping");
                return;
            }

            _lastMapEventTime = now;
            _lastMapModifiedFile = e.FullPath;

            // Process new lines for map detection
            Task.Run(() => ProcessApplicationLogForMap(e.FullPath));
        }

        private async Task ProcessApplicationLogForMap(string filePath)
        {
            try
            {
                await Task.Delay(100); // Small delay for file write completion

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var fileLength = stream.Length;

                DebugLog($"ProcessApplicationLogForMap: fileLength={fileLength}, lastPos={_lastApplicationLogPosition}");

                // Only read new content
                if (fileLength <= _lastApplicationLogPosition)
                {
                    DebugLog($"No new content to read");
                    return;
                }

                stream.Seek(_lastApplicationLogPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                var newContent = await reader.ReadToEndAsync();
                _lastApplicationLogPosition = fileLength;

                DebugLog($"Read {newContent.Length} chars of new content");

                // Parse for map loading events
                var detectedMap = ParseMapFromLogContent(newContent);
                DebugLog($"ParseMapFromLogContent result: {detectedMap ?? "null"}");

                if (!string.IsNullOrEmpty(detectedMap) && detectedMap != _currentMapKey)
                {
                    _currentMapKey = detectedMap;
                    DebugLog($"Map changed! Firing MapDetected event: {detectedMap}");
                    MapDetected?.Invoke(this, new MapDetectedEventArgs(detectedMap, DateTime.Now));
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Error reading application log: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse map name from log content using multiple detection patterns
        /// </summary>
        private string? ParseMapFromLogContent(string content)
        {
            // Pattern 1 (most reliable): [Transit] Locations:MapName ->
            // Example: "[Transit] Flag:None, RaidId:..., Locations:Shoreline ->"
            // This appears after map is fully loaded and raid starts
            var transitMatch = System.Text.RegularExpressions.Regex.Match(
                content,
                @"\[Transit\].*Locations:([a-zA-Z0-9_]+)\s*->",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (transitMatch.Success)
            {
                var mapName = transitMatch.Groups[1].Value;
                System.Diagnostics.Debug.WriteLine($"[LogSyncService] Transit pattern matched: {mapName}");
                if (TryGetMapKey(mapName, out var mapKey))
                {
                    return mapKey;
                }
            }

            // Pattern 2: scene preset path:maps/<mapname>.bundle
            // Examples:
            //   "scene preset path:maps/shoreline_preset.bundle"
            //   "scene preset path:maps/shopping_mall.bundle"
            //   "scene preset path:maps/city_preset.bundle"
            // This appears when map loading starts
            var scenePresetMatch = System.Text.RegularExpressions.Regex.Match(
                content,
                @"scene preset path:maps/([a-zA-Z0-9_]+)\.bundle",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (scenePresetMatch.Success)
            {
                var mapName = scenePresetMatch.Groups[1].Value;
                System.Diagnostics.Debug.WriteLine($"[LogSyncService] Scene preset pattern matched: {mapName}");
                if (TryGetMapKey(mapName, out var mapKey))
                {
                    return mapKey;
                }
            }

            // Pattern 3: LocationLoaded (backup pattern, less specific about which map)
            // This just confirms a location was loaded but Transit pattern is preferred

            return null;
        }

        /// <summary>
        /// Find the last map from application logs (for initial map selection)
        /// </summary>
        /// <param name="logFolderPath">Log folder path</param>
        /// <returns>Last detected map key, or null if not found</returns>
        public string? FindLastMapFromLogs(string? logFolderPath = null)
        {
            var path = logFolderPath ?? SettingsService.Instance.LogFolderPath;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return null;

            try
            {
                // Find the most recent application log file
                var logFiles = Directory.GetFiles(path, "application*.log", SearchOption.AllDirectories)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(3)  // Check last 3 log files
                    .ToList();

                if (logFiles.Count == 0)
                    return null;

                foreach (var logFile in logFiles)
                {
                    var mapKey = FindLastMapInFile(logFile);
                    if (!string.IsNullOrEmpty(mapKey))
                    {
                        System.Diagnostics.Debug.WriteLine($"[LogSyncService] Found last map from logs: {mapKey}");
                        return mapKey;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogSyncService] Error finding last map: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Find the last map mentioned in a single log file (reads from end)
        /// </summary>
        private string? FindLastMapInFile(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                // Read last 200KB of file (should be enough to find recent map)
                var readSize = Math.Min(stream.Length, 200 * 1024);
                if (readSize <= 0) return null;

                stream.Seek(-readSize, SeekOrigin.End);
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();

                // Split into lines and search from end
                var lines = content.Split('\n');
                string? lastFoundMap = null;

                // Search through all lines to find the LAST map reference
                foreach (var line in lines)
                {
                    var mapKey = ParseMapFromLogContent(line);
                    if (!string.IsNullOrEmpty(mapKey))
                    {
                        lastFoundMap = mapKey;
                    }
                }

                return lastFoundMap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogSyncService] Error reading log file {filePath}: {ex.Message}");
                return null;
            }
        }

        private async Task ProcessLatestLogEvents(string filePath)
        {
            try
            {
                // Read the last portion of the file to get recent events
                var events = await ParseLogFileAsync(filePath, tailOnly: true);

                foreach (var evt in events)
                {
                    // Only fire for recent events (within last minute)
                    if ((DateTime.Now - evt.Timestamp).TotalMinutes < 1)
                    {
                        QuestEventDetected?.Invoke(this, evt);
                    }
                }
            }
            catch
            {
                // Ignore errors during live monitoring
            }
        }

        #endregion

        #region Log Parsing

        /// <summary>
        /// Parse all log files in a directory for quest events
        /// </summary>
        public async Task<List<QuestLogEvent>> ParseLogDirectoryAsync(string logFolderPath, IProgress<string>? progress = null)
        {
            var allEvents = new List<QuestLogEvent>();

            if (!Directory.Exists(logFolderPath))
                return allEvents;

            // Find all push-notifications log files
            var logFiles = Directory.GetFiles(logFolderPath, "*push-notifications*.log", SearchOption.AllDirectories)
                .OrderBy(f => File.GetLastWriteTime(f))
                .ToList();

            progress?.Report($"Found {logFiles.Count} log files");

            int processed = 0;
            foreach (var file in logFiles)
            {
                try
                {
                    var events = await ParseLogFileAsync(file);
                    allEvents.AddRange(events);

                    processed++;
                    progress?.Report($"Parsed {processed}/{logFiles.Count} files ({allEvents.Count} events)");
                }
                catch
                {
                    // Skip files that can't be read
                }
            }

            // Sort by timestamp
            allEvents = allEvents.OrderBy(e => e.Timestamp).ToList();

            return allEvents;
        }

        /// <summary>
        /// Parse a single log file for quest events
        /// </summary>
        public async Task<List<QuestLogEvent>> ParseLogFileAsync(string filePath, bool tailOnly = false)
        {
            var events = new List<QuestLogEvent>();

            if (!File.Exists(filePath))
                return events;

            try
            {
                // Read file with shared access (game might be writing)
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                var fileName = Path.GetFileName(filePath);

                // If tailOnly, skip to last 50KB
                if (tailOnly && stream.Length > 50000)
                {
                    stream.Seek(-50000, SeekOrigin.End);
                    reader.ReadLine(); // Skip partial line
                }

                // Read entire content for multiline JSON parsing
                var content = await reader.ReadToEndAsync();
                var parsedEvents = ParseLogContent(content, fileName);
                events.AddRange(parsedEvents);
            }
            catch
            {
                // File access error, return what we have
            }

            return events;
        }

        /// <summary>
        /// Parse log content with multiline JSON support
        /// </summary>
        private List<QuestLogEvent> ParseLogContent(string content, string? sourceFile)
        {
            var events = new List<QuestLogEvent>();

            // Split into lines
            var lines = content.Split('\n');
            var jsonBuilder = new System.Text.StringBuilder();
            bool inJson = false;
            int braceCount = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');

                // Check if this line starts a JSON block (line starting with '{')
                if (!inJson && line.TrimStart().StartsWith("{"))
                {
                    inJson = true;
                    jsonBuilder.Clear();
                    braceCount = 0;
                }

                if (inJson)
                {
                    jsonBuilder.AppendLine(line);

                    // Count braces
                    foreach (char c in line)
                    {
                        if (c == '{') braceCount++;
                        else if (c == '}') braceCount--;
                    }

                    // JSON block complete
                    if (braceCount == 0)
                    {
                        inJson = false;
                        var jsonString = jsonBuilder.ToString();

                        var evt = ParseJsonBlock(jsonString, sourceFile);
                        if (evt != null)
                        {
                            events.Add(evt);
                        }
                    }
                }
            }

            return events;
        }

        /// <summary>
        /// Parse a JSON block for quest event
        /// </summary>
        private QuestLogEvent? ParseJsonBlock(string jsonString, string? sourceFile)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                // Check if this is a new_message notification
                if (!root.TryGetProperty("type", out var typeElement) ||
                    typeElement.GetString() != "new_message")
                    return null;

                // Get message element
                if (!root.TryGetProperty("message", out var messageElement))
                    return null;

                // Get message type
                if (!messageElement.TryGetProperty("type", out var msgTypeElement))
                    return null;

                var msgType = msgTypeElement.GetInt32();

                // Check if this is a quest-related message
                if (msgType != MSG_TYPE_STARTED && msgType != MSG_TYPE_COMPLETED && msgType != MSG_TYPE_FAILED)
                    return null;

                // Get templateId (contains quest ID)
                if (!messageElement.TryGetProperty("templateId", out var templateIdElement))
                    return null;

                var templateId = templateIdElement.GetString();
                if (string.IsNullOrEmpty(templateId))
                    return null;

                // Extract quest ID (first token in templateId)
                var questId = templateId.Split(' ')[0];

                // Get dialogId (trader ID)
                var traderId = "";
                if (root.TryGetProperty("dialogId", out var dialogIdElement))
                {
                    traderId = dialogIdElement.GetString() ?? "";
                }

                // Get timestamp
                var timestamp = DateTime.Now;
                if (messageElement.TryGetProperty("dt", out var dtElement))
                {
                    var unixTime = dtElement.GetInt64();
                    timestamp = DateTimeOffset.FromUnixTimeSeconds(unixTime).LocalDateTime;
                }

                // Determine event type
                var eventType = msgType switch
                {
                    MSG_TYPE_STARTED => QuestEventType.Started,
                    MSG_TYPE_COMPLETED => QuestEventType.Completed,
                    MSG_TYPE_FAILED => QuestEventType.Failed,
                    _ => QuestEventType.Started
                };

                return new QuestLogEvent
                {
                    QuestId = questId,
                    EventType = eventType,
                    TraderId = traderId,
                    Timestamp = timestamp,
                    OriginalLine = jsonString.Substring(0, Math.Min(200, jsonString.Length)),
                    SourceFile = sourceFile
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Quest Synchronization

        /// <summary>
        /// Synchronize quest progress from log files
        /// </summary>
        /// <param name="logFolderPath">Path to log folder</param>
        /// <param name="progress">Progress reporter</param>
        /// <param name="daysRange">Number of days to look back (0 = all logs)</param>
        public async Task<SyncResult> SyncFromLogsAsync(string logFolderPath, IProgress<string>? progress = null, int daysRange = 0)
        {
            var result = new SyncResult();

            System.Diagnostics.Debug.WriteLine($"[LogSyncService] Starting sync from: {logFolderPath}");
            progress?.Report("Scanning log files...");

            // Parse all log files
            var events = await ParseLogDirectoryAsync(logFolderPath, progress);
            System.Diagnostics.Debug.WriteLine($"[LogSyncService] Found {events.Count} quest events in logs");

            // Apply date filter if specified
            if (daysRange > 0)
            {
                var cutoffDate = DateTime.Now.AddDays(-daysRange);
                var originalCount = events.Count;
                events = events.Where(e => e.Timestamp >= cutoffDate).ToList();
                progress?.Report($"Filtered to {events.Count}/{originalCount} events from last {daysRange} days");
            }

            result.TotalEventsFound = events.Count;

            if (events.Count == 0)
            {
                result.Errors.Add("No quest events found in logs");
                return result;
            }

            progress?.Report($"Processing {events.Count} quest events...");

            // Get task data
            var progressService = QuestProgressService.Instance;
            var graphService = QuestGraphService.Instance;

            // Build a lookup for quest IDs
            var tasksByQuestId = BuildQuestIdLookup(progressService.AllTasks);
            System.Diagnostics.Debug.WriteLine($"[LogSyncService] Built lookup with {tasksByQuestId.Count} quest IDs from {progressService.AllTasks.Count} tasks");

            // STEP 1: Determine final state for each quest (last event wins)
            // Key: normalizedName, Value: (finalEventType, timestamp, task)
            var questFinalStates = new Dictionary<string, (QuestEventType EventType, DateTime Timestamp, TarkovTask Task)>(StringComparer.OrdinalIgnoreCase);
            var startedQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var evt in events)
            {
                var task = FindTaskByQuestId(tasksByQuestId, evt.QuestId);
                if (task == null)
                {
                    if (!result.UnmatchedQuestIds.Contains(evt.QuestId))
                        result.UnmatchedQuestIds.Add(evt.QuestId);
                    continue;
                }

                var normalizedName = task.NormalizedName ?? "";
                if (string.IsNullOrEmpty(normalizedName)) continue;

                // Track started quests (for in-progress detection)
                if (evt.EventType == QuestEventType.Started)
                {
                    startedQuests.Add(normalizedName);
                }

                // Last event for each quest determines final state
                questFinalStates[normalizedName] = (evt.EventType, evt.Timestamp, task);

                // Count events
                switch (evt.EventType)
                {
                    case QuestEventType.Started: result.QuestsStarted++; break;
                    case QuestEventType.Completed: result.QuestsCompleted++; break;
                    case QuestEventType.Failed: result.QuestsFailed++; break;
                }
            }

            // STEP 2: Build questsToComplete based on final states
            var questsToComplete = new List<QuestChangeInfo>();
            var processedPrereqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // First, collect all quests that will be in a terminal state (Completed or Failed)
            var terminalStateQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in questFinalStates)
            {
                if (kvp.Value.EventType == QuestEventType.Completed || kvp.Value.EventType == QuestEventType.Failed)
                {
                    terminalStateQuests.Add(kvp.Key);
                }
            }

            foreach (var kvp in questFinalStates)
            {
                var normalizedName = kvp.Key;
                var (eventType, timestamp, task) = kvp.Value;
                var currentStatus = progressService.GetStatus(task);

                switch (eventType)
                {
                    case QuestEventType.Started:
                        // Started quests: only complete prerequisites, not the quest itself
                        // Quest stays Active
                        break;

                    case QuestEventType.Completed:
                        // Only add if status will actually change
                        if (currentStatus != QuestStatus.Done)
                        {
                            questsToComplete.Add(new QuestChangeInfo
                            {
                                QuestName = task.Name,
                                NormalizedName = normalizedName,
                                Trader = task.Trader,
                                IsPrerequisite = false,
                                ChangeType = QuestEventType.Completed,
                                Timestamp = timestamp
                            });
                        }
                        break;

                    case QuestEventType.Failed:
                        // Only add if status will actually change
                        if (currentStatus != QuestStatus.Failed)
                        {
                            questsToComplete.Add(new QuestChangeInfo
                            {
                                QuestName = task.Name,
                                NormalizedName = normalizedName,
                                Trader = task.Trader,
                                IsPrerequisite = false,
                                ChangeType = QuestEventType.Failed,
                                Timestamp = timestamp
                            });
                        }
                        break;
                }

                // STEP 3: Complete prerequisites for quests that were COMPLETED or FAILED only
                // For Started quests, we cannot reliably determine prerequisite completion
                // because a quest can be started even if prerequisites are still in progress in some cases
                if (eventType == QuestEventType.Started)
                    continue;

                var prereqs = graphService.GetAllPrerequisites(normalizedName);
                foreach (var prereq in prereqs)
                {
                    if (prereq.NormalizedName == null) continue;
                    if (processedPrereqs.Contains(prereq.NormalizedName)) continue;

                    // Skip if this prereq will have a terminal state from logs
                    if (terminalStateQuests.Contains(prereq.NormalizedName)) continue;

                    // Skip if prereq has no event in logs (we cannot determine its state)
                    // This prevents auto-completing quests that have no log evidence
                    if (!questFinalStates.ContainsKey(prereq.NormalizedName))
                        continue;

                    // Skip if prereq is started but not in terminal state (still in progress)
                    if (startedQuests.Contains(prereq.NormalizedName) && !terminalStateQuests.Contains(prereq.NormalizedName))
                        continue;

                    var prereqStatus = progressService.GetStatus(prereq);
                    if (prereqStatus != QuestStatus.Done)
                    {
                        // Skip alternative quests - will be collected separately
                        if (progressService.HasAlternativeQuests(prereq))
                        {
                            System.Diagnostics.Debug.WriteLine($"[LogSyncService] Skipping alternative quest prereq: {prereq.Name}");
                            continue;
                        }

                        questsToComplete.Add(new QuestChangeInfo
                        {
                            QuestName = prereq.Name,
                            NormalizedName = prereq.NormalizedName,
                            Trader = prereq.Trader,
                            IsPrerequisite = true,
                            ChangeType = QuestEventType.Completed,
                            Timestamp = timestamp
                        });
                        processedPrereqs.Add(prereq.NormalizedName);
                        result.PrerequisitesAutoCompleted++;
                    }
                }
            }

            // Sort by timestamp (oldest first) for chronological display
            result.QuestsToComplete = questsToComplete.OrderBy(q => q.Timestamp).ToList();

            // STEP 4: Collect alternative quest groups that need user selection
            // These are mutually exclusive quests where user must choose which one they completed
            result.AlternativeQuestGroups = CollectAlternativeQuestGroups(progressService, questFinalStates, terminalStateQuests);

            // Build InProgressQuests list: quests whose final state is Started (not Completed/Failed)
            foreach (var kvp in questFinalStates)
            {
                var normalizedName = kvp.Key;
                var (eventType, _, task) = kvp.Value;

                // Only include quests whose FINAL state is Started
                if (eventType == QuestEventType.Started)
                {
                    // Check if already done in saved progress
                    var currentStatus = progressService.GetStatus(task);
                    if (currentStatus != QuestStatus.Done)
                    {
                        result.InProgressQuests.Add(task);
                    }
                }
            }

            // Build CompletedQuests list from QuestsToComplete
            foreach (var change in questsToComplete.Where(q => q.ChangeType == QuestEventType.Completed && !q.IsPrerequisite))
            {
                var task = progressService.GetTask(change.NormalizedName);
                if (task != null)
                {
                    result.CompletedQuests.Add(task);
                }
            }

            progress?.Report($"Found {questsToComplete.Count} quests to update");

            System.Diagnostics.Debug.WriteLine($"[LogSyncService] Sync complete: {result.TotalEventsFound} events, {result.QuestsToComplete.Count} to complete, {result.InProgressQuests.Count} in progress, {result.UnmatchedQuestIds.Count} unmatched");

            // 매칭되지 않은 ID 샘플 출력
            if (result.UnmatchedQuestIds.Count > 0)
            {
                var sampleUnmatched = result.UnmatchedQuestIds.Take(10).ToList();
                System.Diagnostics.Debug.WriteLine($"[LogSyncService] Sample unmatched IDs: {string.Join(", ", sampleUnmatched)}");

                // DB의 샘플 ID도 출력
                var sampleDbIds = tasksByQuestId.Keys.Take(10).ToList();
                System.Diagnostics.Debug.WriteLine($"[LogSyncService] Sample DB IDs: {string.Join(", ", sampleDbIds)}");
            }

            return result;
        }

        /// <summary>
        /// Apply quest changes after user confirmation (batch processing for performance)
        /// </summary>
        public async Task ApplyQuestChangesAsync(List<QuestChangeInfo> changes)
        {
            var progressService = QuestProgressService.Instance;
            var selectedChanges = changes.Where(c => c.IsSelected).ToList();

            System.Diagnostics.Debug.WriteLine($"[LogSyncService] ApplyQuestChangesAsync: {changes.Count} total changes, {selectedChanges.Count} selected");

            // Build batch of changes
            var batchChanges = new List<(TarkovTask Task, QuestStatus Status)>();

            foreach (var change in selectedChanges)
            {
                var task = progressService.GetTask(change.NormalizedName);
                if (task == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogSyncService] Task not found for NormalizedName: {change.NormalizedName}");
                    continue;
                }

                var status = change.ChangeType switch
                {
                    QuestEventType.Completed => QuestStatus.Done,
                    QuestEventType.Failed => QuestStatus.Failed,
                    _ => QuestStatus.Active
                };

                if (status != QuestStatus.Active)
                {
                    batchChanges.Add((task, status));
                    System.Diagnostics.Debug.WriteLine($"[LogSyncService] Queued change: {change.NormalizedName} -> {change.ChangeType}");
                }
            }

            // Apply all changes in one batch (single DB transaction, single UI update)
            if (batchChanges.Count > 0)
            {
                await progressService.ApplyQuestChangesBatchAsync(batchChanges);
                System.Diagnostics.Debug.WriteLine($"[LogSyncService] Batch applied {batchChanges.Count} quest changes");
            }

            System.Diagnostics.Debug.WriteLine("[LogSyncService] ApplyQuestChangesAsync completed");
        }

        /// <summary>
        /// Apply quest changes after user confirmation (legacy sync method, calls async internally)
        /// </summary>
        [Obsolete("Use ApplyQuestChangesAsync for better performance")]
        public void ApplyQuestChanges(List<QuestChangeInfo> changes)
        {
            Task.Run(async () => await ApplyQuestChangesAsync(changes)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Build a lookup dictionary for quest IDs
        /// </summary>
        private Dictionary<string, TarkovTask> BuildQuestIdLookup(IReadOnlyList<TarkovTask> tasks)
        {
            var lookup = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks)
            {
                if (task.Ids != null)
                {
                    foreach (var id in task.Ids)
                    {
                        if (!string.IsNullOrEmpty(id) && !lookup.ContainsKey(id))
                        {
                            lookup[id] = task;
                        }
                    }
                }
            }

            return lookup;
        }

        /// <summary>
        /// Find task by quest ID
        /// </summary>
        private TarkovTask? FindTaskByQuestId(Dictionary<string, TarkovTask> lookup, string questId)
        {
            return lookup.TryGetValue(questId, out var task) ? task : null;
        }

        /// <summary>
        /// Collect alternative quest groups that need user selection
        /// </summary>
        private List<AlternativeQuestGroup> CollectAlternativeQuestGroups(
            QuestProgressService progressService,
            Dictionary<string, (QuestEventType EventType, DateTime Timestamp, TarkovTask Task)> questFinalStates,
            HashSet<string> terminalStateQuests)
        {
            var groups = new List<AlternativeQuestGroup>();
            var processedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var graphService = QuestGraphService.Instance;

            // Find all alternative quest groups that are prerequisites for started/completed quests
            foreach (var kvp in questFinalStates)
            {
                var normalizedName = kvp.Key;
                var task = kvp.Value.Task;

                // Get all prerequisites
                var prereqs = graphService.GetAllPrerequisites(normalizedName);

                foreach (var prereq in prereqs)
                {
                    if (prereq.NormalizedName == null) continue;
                    if (!progressService.HasAlternativeQuests(prereq)) continue;

                    // Skip if already processed this group
                    var groupKey = GetAlternativeGroupKey(prereq, progressService);
                    if (processedGroups.Contains(groupKey)) continue;
                    processedGroups.Add(groupKey);

                    // Build the group
                    var group = new AlternativeQuestGroup { IsRequired = true };

                    // Add the main quest
                    var mainStatus = progressService.GetStatus(prereq);
                    group.Choices.Add(new AlternativeQuestChoice
                    {
                        Task = prereq,
                        IsCompleted = mainStatus == QuestStatus.Done,
                        IsFailed = mainStatus == QuestStatus.Failed,
                        IsSelected = mainStatus == QuestStatus.Done
                    });

                    // Add alternative quests
                    if (prereq.AlternativeQuests != null)
                    {
                        foreach (var altName in prereq.AlternativeQuests)
                        {
                            var altTask = progressService.GetTask(altName) ?? progressService.GetTaskById(altName);
                            if (altTask != null)
                            {
                                var altStatus = progressService.GetStatus(altTask);
                                group.Choices.Add(new AlternativeQuestChoice
                                {
                                    Task = altTask,
                                    IsCompleted = altStatus == QuestStatus.Done,
                                    IsFailed = altStatus == QuestStatus.Failed,
                                    IsSelected = altStatus == QuestStatus.Done
                                });
                            }
                        }
                    }

                    // Only add if there are multiple choices and none are completed yet
                    if (group.Choices.Count > 1 && !group.Choices.Any(c => c.IsCompleted))
                    {
                        groups.Add(group);
                    }
                }
            }

            return groups;
        }

        /// <summary>
        /// Get a unique key for an alternative quest group
        /// </summary>
        private string GetAlternativeGroupKey(TarkovTask task, QuestProgressService progressService)
        {
            var names = new List<string> { task.NormalizedName ?? "" };

            if (task.AlternativeQuests != null)
            {
                names.AddRange(task.AlternativeQuests);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join("|", names);
        }

        #endregion

        public void Dispose()
        {
            StopMonitoring();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Event arguments for map detection
    /// </summary>
    public class MapDetectedEventArgs : EventArgs
    {
        /// <summary>
        /// Detected map key (matches map_configs.json key)
        /// </summary>
        public string MapKey { get; }

        /// <summary>
        /// Time when map was detected
        /// </summary>
        public DateTime DetectedAt { get; }

        public MapDetectedEventArgs(string mapKey, DateTime detectedAt)
        {
            MapKey = mapKey;
            DetectedAt = detectedAt;
        }
    }
}
