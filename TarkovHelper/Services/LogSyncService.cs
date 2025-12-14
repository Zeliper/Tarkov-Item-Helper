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

        private FileSystemWatcher? _logWatcher;
        private readonly object _watcherLock = new();
        private DateTime _lastEventTime = DateTime.MinValue;
        private string? _lastModifiedFile;
        private bool _isWatching;

        /// <summary>
        /// Event fired when a quest event is detected from logs
        /// </summary>
        public event EventHandler<QuestLogEvent>? QuestEventDetected;

        /// <summary>
        /// Event fired when log monitoring status changes
        /// </summary>
        public event EventHandler<bool>? MonitoringStatusChanged;

        /// <summary>
        /// Whether log monitoring is currently active
        /// </summary>
        public bool IsMonitoring => _isWatching;

        // Message type codes from logs
        private const int MSG_TYPE_STARTED = 10;
        private const int MSG_TYPE_FAILED = 11;
        private const int MSG_TYPE_COMPLETED = 12;

        private LogSyncService() { }

        #region Log File Monitoring

        /// <summary>
        /// Start monitoring log folder for quest events
        /// </summary>
        public void StartMonitoring(string logFolderPath)
        {
            lock (_watcherLock)
            {
                StopMonitoring();

                if (string.IsNullOrEmpty(logFolderPath) || !Directory.Exists(logFolderPath))
                    return;

                try
                {
                    _logWatcher = new FileSystemWatcher(logFolderPath)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        Filter = "*push-notifications*.log",
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    _logWatcher.Changed += OnLogFileChanged;
                    _logWatcher.Created += OnLogFileChanged;

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

                // STEP 3: Complete prerequisites for ANY quest that was started/completed/failed
                // (If a quest was started, failed, or completed, its prerequisites must be done)
                var prereqs = graphService.GetAllPrerequisites(normalizedName);
                foreach (var prereq in prereqs)
                {
                    if (prereq.NormalizedName == null) continue;
                    if (processedPrereqs.Contains(prereq.NormalizedName)) continue;

                    // Skip if this prereq will have a terminal state from logs
                    if (terminalStateQuests.Contains(prereq.NormalizedName)) continue;

                    // Skip if prereq is started but not in terminal state (still in progress)
                    if (startedQuests.Contains(prereq.NormalizedName) && !terminalStateQuests.Contains(prereq.NormalizedName))
                        continue;

                    var prereqStatus = progressService.GetStatus(prereq);
                    if (prereqStatus != QuestStatus.Done)
                    {
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
        /// Apply quest changes after user confirmation
        /// </summary>
        public void ApplyQuestChanges(List<QuestChangeInfo> changes)
        {
            var progressService = QuestProgressService.Instance;

            System.Diagnostics.Debug.WriteLine($"[LogSyncService] ApplyQuestChanges: {changes.Count} total changes, {changes.Count(c => c.IsSelected)} selected");

            foreach (var change in changes.Where(c => c.IsSelected))
            {
                var task = progressService.GetTask(change.NormalizedName);
                if (task == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogSyncService] Task not found for NormalizedName: {change.NormalizedName}");
                    continue;
                }

                System.Diagnostics.Debug.WriteLine($"[LogSyncService] Applying change: {change.NormalizedName} -> {change.ChangeType}");

                switch (change.ChangeType)
                {
                    case QuestEventType.Completed:
                        progressService.CompleteQuest(task, completePrerequisites: false);
                        System.Diagnostics.Debug.WriteLine($"[LogSyncService] Completed quest: {task.Name}");
                        break;
                    case QuestEventType.Failed:
                        progressService.FailQuest(task);
                        System.Diagnostics.Debug.WriteLine($"[LogSyncService] Failed quest: {task.Name}");
                        break;
                }
            }

            System.Diagnostics.Debug.WriteLine("[LogSyncService] ApplyQuestChanges completed");
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

        #endregion

        public void Dispose()
        {
            StopMonitoring();
            GC.SuppressFinalize(this);
        }
    }
}
