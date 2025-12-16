using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for managing quest progress state
    /// </summary>
    public class QuestProgressService
    {
        private static QuestProgressService? _instance;
        public static QuestProgressService Instance => _instance ??= new QuestProgressService();

        private Dictionary<string, QuestStatus> _questProgress = new();
        private Dictionary<string, TarkovTask> _tasksByNormalizedName = new();
        private Dictionary<string, TarkovTask> _tasksByBsgId = new();
        private Dictionary<string, TarkovTask> _tasksById = new();
        private List<TarkovTask> _allTasks = new();

        // V2 진행 데이터 (이중 키 저장)
        private QuestProgressDataV2 _progressDataV2 = new();

        // Objective progress: key = "questNormalizedName:objectiveIndex", value = completed
        private Dictionary<string, bool> _objectiveProgress = new();

        /// <summary>
        /// 데이터 소스 (JSON 또는 DB)
        /// </summary>
        public bool IsLoadedFromDb { get; private set; }

        public event EventHandler? ProgressChanged;
        public event EventHandler<ObjectiveProgressChangedEventArgs>? ObjectiveProgressChanged;

        /// <summary>
        /// DB에서 퀘스트 데이터를 로드하고 초기화합니다.
        /// </summary>
        public async Task<bool> InitializeFromDbAsync()
        {
            var dbService = QuestDbService.Instance;

            if (!await dbService.LoadQuestsAsync())
            {
                System.Diagnostics.Debug.WriteLine("[QuestProgressService] Failed to load quests from DB, falling back to JSON");
                return false;
            }

            var tasks = dbService.AllQuests.ToList();
            Initialize(tasks);
            IsLoadedFromDb = true;

            // V2 형식으로 진행 데이터 reconcile
            ReconcileProgressWithDb();

            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Initialized from DB with {tasks.Count} quests");
            return true;
        }

        /// <summary>
        /// DB 업데이트 후 진행 데이터를 reconcile합니다.
        /// ID 또는 NormalizedName으로 매핑하고 누락된 키를 채웁니다.
        /// </summary>
        private void ReconcileProgressWithDb()
        {
            var changed = false;

            // 완료 퀘스트 reconcile
            foreach (var entry in _progressDataV2.CompletedQuests)
            {
                TarkovTask? matched = null;

                // 1차: ID로 매핑
                if (!string.IsNullOrEmpty(entry.Id) && _tasksById.TryGetValue(entry.Id, out matched))
                {
                    // ID 매핑 성공 - NormalizedName 업데이트
                    if (matched.NormalizedName != null && entry.NormalizedName != matched.NormalizedName)
                    {
                        entry.NormalizedName = matched.NormalizedName;
                        changed = true;
                    }
                }
                // 2차: NormalizedName으로 폴백
                else if (!string.IsNullOrEmpty(entry.NormalizedName) &&
                         _tasksByNormalizedName.TryGetValue(entry.NormalizedName, out matched))
                {
                    // NormalizedName 매핑 성공 - ID 업데이트
                    var matchedId = matched.Ids?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(matchedId) && entry.Id != matchedId)
                    {
                        entry.Id = matchedId;
                        changed = true;
                    }
                }
                else
                {
                    // 매핑 실패 - 고아 레코드로 유지 (삭제하지 않음)
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Orphan completed entry: {entry.Id ?? entry.NormalizedName}");
                }
            }

            // 실패 퀘스트 reconcile
            foreach (var entry in _progressDataV2.FailedQuests)
            {
                TarkovTask? matched = null;

                if (!string.IsNullOrEmpty(entry.Id) && _tasksById.TryGetValue(entry.Id, out matched))
                {
                    if (matched.NormalizedName != null && entry.NormalizedName != matched.NormalizedName)
                    {
                        entry.NormalizedName = matched.NormalizedName;
                        changed = true;
                    }
                }
                else if (!string.IsNullOrEmpty(entry.NormalizedName) &&
                         _tasksByNormalizedName.TryGetValue(entry.NormalizedName, out matched))
                {
                    var matchedId = matched.Ids?.FirstOrDefault();
                    if (!string.IsNullOrEmpty(matchedId) && entry.Id != matchedId)
                    {
                        entry.Id = matchedId;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                SaveProgress();
            }
        }

        /// <summary>
        /// Initialize service with task data
        /// </summary>
        public void Initialize(List<TarkovTask> tasks)
        {
            _allTasks = tasks;

            // Build dictionaries, handling duplicates by keeping the first occurrence
            _tasksByNormalizedName = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
            _tasksByBsgId = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
            _tasksById = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks.Where(t => !string.IsNullOrEmpty(t.NormalizedName)))
            {
                if (!_tasksByNormalizedName.ContainsKey(task.NormalizedName!))
                {
                    _tasksByNormalizedName[task.NormalizedName!] = task;
                }

                // Build Id/BsgId lookup (task.Ids contains IDs)
                if (task.Ids != null)
                {
                    foreach (var id in task.Ids)
                    {
                        if (!string.IsNullOrEmpty(id))
                        {
                            if (!_tasksByBsgId.ContainsKey(id))
                            {
                                _tasksByBsgId[id] = task;
                            }
                            if (!_tasksById.ContainsKey(id))
                            {
                                _tasksById[id] = task;
                            }
                        }
                    }
                }
            }

            LoadProgress();
            LoadObjectiveProgress();
        }

        /// <summary>
        /// Get all tasks
        /// </summary>
        public IReadOnlyList<TarkovTask> AllTasks => _allTasks;

        /// <summary>
        /// Get task by normalized name (deprecated, use GetTaskById instead)
        /// </summary>
        public TarkovTask? GetTask(string normalizedName)
        {
            return _tasksByNormalizedName.TryGetValue(normalizedName, out var task) ? task : null;
        }

        /// <summary>
        /// Get task by database ID (primary lookup method)
        /// </summary>
        public TarkovTask? GetTaskById(string id)
        {
            return _tasksById.TryGetValue(id, out var task) ? task : null;
        }

        /// <summary>
        /// Get task by BSG ID (used for tarkov-market marker matching)
        /// </summary>
        public TarkovTask? GetTaskByBsgId(string bsgId)
        {
            return _tasksByBsgId.TryGetValue(bsgId, out var task) ? task : null;
        }

        /// <summary>
        /// Check if a task has alternative quests (mutually exclusive choices)
        /// These quests should not be auto-completed as user must choose one
        /// </summary>
        public bool HasAlternativeQuests(TarkovTask task)
        {
            return task.AlternativeQuests != null && task.AlternativeQuests.Count > 0;
        }

        /// <summary>
        /// Get all alternative quest groups (for sync selection UI)
        /// Returns groups of mutually exclusive quests that need user selection
        /// </summary>
        public List<List<TarkovTask>> GetAlternativeQuestGroups()
        {
            var groups = new List<List<TarkovTask>>();
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in _allTasks)
            {
                if (task.NormalizedName == null) continue;
                if (processed.Contains(task.NormalizedName)) continue;
                if (!HasAlternativeQuests(task)) continue;

                // Build a group of mutually exclusive quests
                var group = new List<TarkovTask> { task };
                processed.Add(task.NormalizedName);

                foreach (var altName in task.AlternativeQuests!)
                {
                    if (processed.Contains(altName)) continue;

                    var altTask = GetTask(altName) ?? GetTaskById(altName);
                    if (altTask != null)
                    {
                        group.Add(altTask);
                        if (altTask.NormalizedName != null)
                            processed.Add(altTask.NormalizedName);
                    }
                }

                if (group.Count > 1)
                {
                    groups.Add(group);
                }
            }

            return groups;
        }

        // Thread-local visited set for GetStatus to prevent circular reference during status check
        [ThreadStatic]
        private static HashSet<string>? _getStatusVisited;

        /// <summary>
        /// Get quest status for a task
        /// </summary>
        public QuestStatus GetStatus(TarkovTask task)
        {
            var taskId = task.Ids?.FirstOrDefault();
            var taskKey = taskId ?? task.NormalizedName;

            if (string.IsNullOrEmpty(taskKey)) return QuestStatus.Active;

            // Check if manually set to Done or Failed
            // Try by Id first, then by NormalizedName for backwards compatibility
            if (!string.IsNullOrEmpty(taskId) && _questProgress.TryGetValue(taskId, out var statusById))
            {
                if (statusById == QuestStatus.Done || statusById == QuestStatus.Failed)
                    return statusById;
            }
            else if (!string.IsNullOrEmpty(task.NormalizedName) && _questProgress.TryGetValue(task.NormalizedName, out var statusByName))
            {
                if (statusByName == QuestStatus.Done || statusByName == QuestStatus.Failed)
                    return statusByName;
            }

            // Circular reference protection for prerequisite checking
            bool isTopLevel = _getStatusVisited == null;
            _getStatusVisited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // If already checking this task (circular reference), treat as Active to break the cycle
            if (!_getStatusVisited.Add(taskKey))
            {
                return QuestStatus.Active;
            }

            try
            {
                // Check edition requirements first (Unavailable takes precedence)
                if (!IsEditionRequirementMet(task))
                    return QuestStatus.Unavailable;

                // Check prestige level requirement (also Unavailable)
                if (!IsPrestigeLevelRequirementMet(task))
                    return QuestStatus.Unavailable;

                // Check DSP Decode Count requirement (Locked, not Unavailable)
                if (!IsDspRequirementMet(task))
                    return QuestStatus.Locked;

                // Check prerequisites
                if (!ArePrerequisitesMet(task))
                    return QuestStatus.Locked;

                // Check level requirement
                if (!IsLevelRequirementMet(task))
                    return QuestStatus.LevelLocked;

                // Check Scav Karma requirement
                if (!IsScavKarmaRequirementMet(task))
                    return QuestStatus.LevelLocked;  // Use LevelLocked status for karma-locked quests too

                return QuestStatus.Active;
            }
            finally
            {
                _getStatusVisited.Remove(taskKey);
                if (isTopLevel)
                {
                    _getStatusVisited = null;
                }
            }
        }

        /// <summary>
        /// Check if player level meets quest requirement
        /// </summary>
        public bool IsLevelRequirementMet(TarkovTask task)
        {
            // If no level requirement, always met
            if (!task.RequiredLevel.HasValue || task.RequiredLevel.Value <= 0)
                return true;

            var playerLevel = SettingsService.Instance.PlayerLevel;
            return playerLevel >= task.RequiredLevel.Value;
        }

        /// <summary>
        /// Check if Scav Karma (Fence reputation) meets quest requirement
        /// </summary>
        public bool IsScavKarmaRequirementMet(TarkovTask task)
        {
            // If no karma requirement, always met
            if (!task.RequiredScavKarma.HasValue)
                return true;

            var playerScavRep = SettingsService.Instance.ScavRep;
            var requiredKarma = task.RequiredScavKarma.Value;

            // Negative requirement means player karma must be <= that value (bad karma quests)
            // Positive requirement means player karma must be >= that value (good karma quests)
            if (requiredKarma < 0)
            {
                return playerScavRep <= requiredKarma;
            }
            else
            {
                return playerScavRep >= requiredKarma;
            }
        }

        /// <summary>
        /// Check if edition requirements are met for the quest
        /// Returns false if quest is unavailable due to edition restrictions
        /// </summary>
        public bool IsEditionRequirementMet(TarkovTask task)
        {
            var settings = SettingsService.Instance;

            // Check required edition (EOD and Unheard are independent)
            if (!string.IsNullOrEmpty(task.RequiredEdition))
            {
                var requiredEdition = task.RequiredEdition.ToLowerInvariant();

                // EOD edition requirement - only EOD checkbox matters
                if (requiredEdition == "eod" || requiredEdition == "edge_of_darkness")
                {
                    if (!settings.HasEodEdition)
                        return false;
                }
                // Unheard edition requirement - only Unheard checkbox matters
                else if (requiredEdition == "unheard" || requiredEdition == "the_unheard")
                {
                    if (!settings.HasUnheardEdition)
                        return false;
                }
            }

            // Check excluded edition
            if (!string.IsNullOrEmpty(task.ExcludedEdition))
            {
                var excludedEdition = task.ExcludedEdition.ToLowerInvariant();

                // Excluded from EOD edition
                if (excludedEdition == "eod" || excludedEdition == "edge_of_darkness")
                {
                    if (settings.HasEodEdition)
                        return false;
                }
                // Excluded from Unheard edition
                else if (excludedEdition == "unheard" || excludedEdition == "the_unheard")
                {
                    if (settings.HasUnheardEdition)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if prestige level requirement is met for the quest
        /// </summary>
        public bool IsPrestigeLevelRequirementMet(TarkovTask task)
        {
            // If no prestige level requirement, always met
            if (!task.RequiredPrestigeLevel.HasValue || task.RequiredPrestigeLevel.Value <= 0)
                return true;

            var playerPrestige = SettingsService.Instance.PrestigeLevel;
            return playerPrestige >= task.RequiredPrestigeLevel.Value;
        }

        /// <summary>
        /// Check if DSP Decode Count requirement is met for the quest.
        /// Uses the RequiredDecodeCount field from the database.
        /// </summary>
        public bool IsDspRequirementMet(TarkovTask task)
        {
            // If no decode count requirement, always met
            if (!task.RequiredDecodeCount.HasValue)
                return true;

            var dspCount = SettingsService.Instance.DspDecodeCount;

            // RequiredDecodeCount specifies the exact DSP decode count needed
            return dspCount == task.RequiredDecodeCount.Value;
        }

        /// <summary>
        /// Check if a quest is completed by its normalized name
        /// Used for Collector quest progress calculation
        /// </summary>
        public bool IsQuestCompleted(string normalizedName)
        {
            var task = GetTask(normalizedName);
            if (task == null) return false;

            return GetStatus(task) == QuestStatus.Done;
        }

        /// <summary>
        /// Check if all prerequisites are met based on taskRequirements or legacy Previous field.
        /// Supports OR groups: GroupId = 0 means AND condition, GroupId > 0 means OR condition within the same group.
        /// </summary>
        public bool ArePrerequisitesMet(TarkovTask task)
        {
            // Use taskRequirements if available (more accurate status conditions with OR group support)
            if (task.TaskRequirements != null && task.TaskRequirements.Count > 0)
            {
                // Group requirements by GroupId
                var andRequirements = task.TaskRequirements.Where(r => r.GroupId == 0).ToList();
                var orGroups = task.TaskRequirements
                    .Where(r => r.GroupId > 0)
                    .GroupBy(r => r.GroupId)
                    .ToList();


                // Check AND requirements (GroupId = 0): ALL must be satisfied
                foreach (var req in andRequirements)
                {
                    // Primary: lookup by TaskId, fallback to TaskNormalizedName for backwards compatibility
                    var reqTask = !string.IsNullOrEmpty(req.TaskId)
                        ? GetTaskById(req.TaskId)
                        : GetTask(req.TaskNormalizedName);

                    if (reqTask == null)
                        continue;

                    var reqStatus = GetStatus(reqTask);
                    var satisfied = IsStatusSatisfied(reqStatus, req.Status);
                    if (!satisfied)
                        return false;
                }

                // Check OR groups (GroupId > 0): ANY ONE in each group must be satisfied
                foreach (var group in orGroups)
                {
                    bool anyInGroupSatisfied = false;

                    foreach (var req in group)
                    {
                        // Primary: lookup by TaskId, fallback to TaskNormalizedName for backwards compatibility
                        var reqTask = !string.IsNullOrEmpty(req.TaskId)
                            ? GetTaskById(req.TaskId)
                            : GetTask(req.TaskNormalizedName);

                        if (reqTask == null)
                            continue;

                        var reqStatus = GetStatus(reqTask);
                        var satisfied = IsStatusSatisfied(reqStatus, req.Status);
                        if (satisfied)
                        {
                            anyInGroupSatisfied = true;
                            break;
                        }
                    }

                    // If no requirement in this OR group is satisfied, prerequisites are not met
                    if (!anyInGroupSatisfied)
                        return false;
                }

                return true;
            }

            // Fallback to legacy Previous field (assumes 'complete' required)
            if (task.Previous == null || task.Previous.Count == 0)
                return true;

            foreach (var prevName in task.Previous)
            {
                var prevTask = GetTask(prevName);
                if (prevTask == null) continue;

                var prevStatus = GetStatus(prevTask);
                if (prevStatus != QuestStatus.Done)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if the current quest status satisfies the required status conditions.
        /// Handles both tarkov.dev API values ("active", "complete", "failed") and
        /// DB/Wiki values ("Start", "Accept", "Complete", "Fail").
        /// </summary>
        private bool IsStatusSatisfied(QuestStatus currentStatus, List<string>? requiredStatuses)
        {
            if (requiredStatuses == null || requiredStatuses.Count == 0)
            {
                // Default: require 'complete'
                return currentStatus == QuestStatus.Done;
            }

            // Check each required status
            foreach (var required in requiredStatuses)
            {
                switch (required.ToLowerInvariant())
                {
                    case "active":
                    case "start":    // DB value: RequirementType = "Start"
                    case "accept":   // DB value: RequirementType = "Accept"
                        // Quest is active (started but not completed)
                        if (currentStatus == QuestStatus.Active)
                            return true;
                        // Also satisfied if quest is done (was active before completion)
                        if (currentStatus == QuestStatus.Done)
                            return true;
                        break;

                    case "complete":
                        if (currentStatus == QuestStatus.Done)
                            return true;
                        break;

                    case "failed":
                    case "fail":     // DB value: RequirementType = "Fail"
                        if (currentStatus == QuestStatus.Failed)
                            return true;
                        break;
                }
            }

            return false;
        }

        /// <summary>
        /// Mark quest as completed, optionally completing prerequisites
        /// Also automatically fails alternative quests (mutually exclusive quests)
        /// </summary>
        public void CompleteQuest(TarkovTask task, bool completePrerequisites = true)
        {
            var taskId = task.Ids?.FirstOrDefault();
            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] CompleteQuest: {taskId} ({task.Name}), prerequisites: {completePrerequisites}");

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changedQuests = new List<(string Id, string? NormalizedName, QuestStatus Status)>();

            CompleteQuestInternalOptimized(task, completePrerequisites, visited, changedQuests);

            // Fail alternative quests (mutually exclusive)
            if (task.AlternativeQuests != null && task.AlternativeQuests.Count > 0)
            {
                foreach (var altQuestName in task.AlternativeQuests)
                {
                    // Try to find by NormalizedName (current data format) or by Id
                    var altTask = GetTask(altQuestName) ?? GetTaskById(altQuestName);
                    if (altTask != null)
                    {
                        var altStatus = GetStatus(altTask);
                        // Only fail if not already done or failed
                        if (altStatus != QuestStatus.Done && altStatus != QuestStatus.Failed)
                        {
                            var altId = altTask.Ids?.FirstOrDefault();
                            var altKey = altId ?? altQuestName;
                            _questProgress[altKey] = QuestStatus.Failed;
                            changedQuests.Add((altId ?? altKey, altTask.NormalizedName, QuestStatus.Failed));
                            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Auto-failed alternative quest: {altKey} ({altTask.Name})");
                        }
                    }
                }
            }

            // Save and notify only once after all recursive completions
            if (changedQuests.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Saving {changedQuests.Count} changed quests (batch)");
                // Fire-and-forget async save - don't block UI
                _ = SaveProgressBatchAsync(changedQuests);
                System.Diagnostics.Debug.WriteLine("[QuestProgressService] Progress save initiated");
                ProgressChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[QuestProgressService] No changes to save");
            }
        }

        /// <summary>
        /// Optimized internal method to complete quest - collects changes without saving
        /// Skips alternative quests (mutually exclusive) when completing prerequisites
        /// </summary>
        private void CompleteQuestInternalOptimized(TarkovTask task, bool completePrerequisites,
            HashSet<string> visited, List<(string Id, string? NormalizedName, QuestStatus Status)> changedQuests,
            bool skipAlternativeQuests = true)
        {
            var taskId = task.Ids?.FirstOrDefault();
            var taskKey = taskId ?? task.NormalizedName;

            if (string.IsNullOrEmpty(taskKey)) return;

            // Prevent circular reference - if already visiting this quest, skip
            if (!visited.Add(taskKey)) return;

            // Skip if already done (check by both Id and NormalizedName)
            if (_questProgress.TryGetValue(taskKey, out var currentStatus) && currentStatus == QuestStatus.Done)
                return;
            if (!string.IsNullOrEmpty(task.NormalizedName) &&
                _questProgress.TryGetValue(task.NormalizedName, out var statusByName) && statusByName == QuestStatus.Done)
                return;

            // Complete prerequisites first (recursive) using TaskRequirements
            if (completePrerequisites && task.TaskRequirements != null)
            {
                foreach (var req in task.TaskRequirements)
                {
                    var prevTask = !string.IsNullOrEmpty(req.TaskId)
                        ? GetTaskById(req.TaskId)
                        : GetTask(req.TaskNormalizedName);

                    if (prevTask != null && GetStatus(prevTask) != QuestStatus.Done)
                    {
                        // Skip alternative quests - user must choose which one to complete
                        if (skipAlternativeQuests && HasAlternativeQuests(prevTask))
                        {
                            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Skipping alternative quest: {prevTask.Name}");
                            continue;
                        }
                        CompleteQuestInternalOptimized(prevTask, true, visited, changedQuests, skipAlternativeQuests);
                    }
                }
            }
            // Fallback to Previous list
            else if (completePrerequisites && task.Previous != null)
            {
                foreach (var prevName in task.Previous)
                {
                    var prevTask = GetTask(prevName);
                    if (prevTask != null && GetStatus(prevTask) != QuestStatus.Done)
                    {
                        // Skip alternative quests - user must choose which one to complete
                        if (skipAlternativeQuests && HasAlternativeQuests(prevTask))
                        {
                            System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Skipping alternative quest: {prevTask.Name}");
                            continue;
                        }
                        CompleteQuestInternalOptimized(prevTask, true, visited, changedQuests, skipAlternativeQuests);
                    }
                }
            }

            _questProgress[taskKey] = QuestStatus.Done;
            changedQuests.Add((taskId ?? taskKey, task.NormalizedName, QuestStatus.Done));
        }

        /// <summary>
        /// Save changed quests in batch (fire-and-forget, doesn't block UI)
        /// </summary>
        private async Task SaveProgressBatchAsync(List<(string Id, string? NormalizedName, QuestStatus Status)> changedQuests)
        {
            try
            {
                await _userDataDb.SaveQuestProgressBatchAsync(changedQuests);
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Batch saved {changedQuests.Count} quest changes");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Batch save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Complete multiple quests in batch (for log sync - started quest prerequisites)
        /// Single DB transaction, single UI update
        /// Skips alternative quests (mutually exclusive) by default
        /// </summary>
        public void CompleteQuestsBatch(IEnumerable<TarkovTask> tasks, bool skipAlternativeQuests = true)
        {
            var changedQuests = new List<(string Id, string? NormalizedName, QuestStatus Status)>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks)
            {
                var taskId = task.Ids?.FirstOrDefault();
                var taskKey = taskId ?? task.NormalizedName;

                if (string.IsNullOrEmpty(taskKey)) continue;
                if (!visited.Add(taskKey)) continue;

                // Skip alternative quests - user must choose which one to complete
                if (skipAlternativeQuests && HasAlternativeQuests(task))
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Skipping alternative quest in batch: {task.Name}");
                    continue;
                }

                // Skip if already done
                if (_questProgress.TryGetValue(taskKey, out var currentStatus) && currentStatus == QuestStatus.Done)
                    continue;
                if (!string.IsNullOrEmpty(task.NormalizedName) &&
                    _questProgress.TryGetValue(task.NormalizedName, out var statusByName) && statusByName == QuestStatus.Done)
                    continue;

                _questProgress[taskKey] = QuestStatus.Done;
                changedQuests.Add((taskId ?? taskKey, task.NormalizedName, QuestStatus.Done));
            }

            if (changedQuests.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Batch completing {changedQuests.Count} quests");
                _ = SaveProgressBatchAsync(changedQuests);
                ProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Internal method to complete quest with circular reference prevention
        /// Returns true if any quest was changed
        /// </summary>
        private bool CompleteQuestInternal(TarkovTask task, bool completePrerequisites, HashSet<string> visited)
        {
            var taskId = task.Ids?.FirstOrDefault();
            var taskKey = taskId ?? task.NormalizedName;

            if (string.IsNullOrEmpty(taskKey)) return false;

            // Prevent circular reference - if already visiting this quest, skip
            if (!visited.Add(taskKey)) return false;

            // Skip if already done (check by both Id and NormalizedName)
            if (_questProgress.TryGetValue(taskKey, out var currentStatus) && currentStatus == QuestStatus.Done)
                return false;
            if (!string.IsNullOrEmpty(task.NormalizedName) &&
                _questProgress.TryGetValue(task.NormalizedName, out var statusByName) && statusByName == QuestStatus.Done)
                return false;

            // Complete prerequisites first (recursive) using TaskRequirements
            if (completePrerequisites && task.TaskRequirements != null)
            {
                foreach (var req in task.TaskRequirements)
                {
                    var prevTask = !string.IsNullOrEmpty(req.TaskId)
                        ? GetTaskById(req.TaskId)
                        : GetTask(req.TaskNormalizedName);

                    if (prevTask != null && GetStatus(prevTask) != QuestStatus.Done)
                    {
                        CompleteQuestInternal(prevTask, true, visited);
                    }
                }
            }
            // Fallback to Previous list
            else if (completePrerequisites && task.Previous != null)
            {
                foreach (var prevName in task.Previous)
                {
                    var prevTask = GetTask(prevName);
                    if (prevTask != null && GetStatus(prevTask) != QuestStatus.Done)
                    {
                        CompleteQuestInternal(prevTask, true, visited);
                    }
                }
            }

            _questProgress[taskKey] = QuestStatus.Done;
            return true;
        }

        /// <summary>
        /// Apply multiple quest changes in batch (for sync operations)
        /// Saves to DB once after all changes are applied
        /// </summary>
        public async Task ApplyQuestChangesBatchAsync(IEnumerable<(TarkovTask Task, QuestStatus Status)> changes)
        {
            var changedItems = new List<(string Id, string? NormalizedName, QuestStatus Status)>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (task, status) in changes)
            {
                var taskId = task.Ids?.FirstOrDefault();
                var taskKey = taskId ?? task.NormalizedName;

                if (string.IsNullOrEmpty(taskKey)) continue;

                switch (status)
                {
                    case QuestStatus.Done:
                        // Complete without recursive save
                        if (CompleteQuestBatchInternal(task, visited, changedItems))
                        {
                            // Handle alternative quests (mutually exclusive)
                            if (task.AlternativeQuests != null)
                            {
                                foreach (var altQuestName in task.AlternativeQuests)
                                {
                                    var altTask = GetTask(altQuestName) ?? GetTaskById(altQuestName);
                                    if (altTask != null)
                                    {
                                        var altStatus = GetStatus(altTask);
                                        if (altStatus != QuestStatus.Done && altStatus != QuestStatus.Failed)
                                        {
                                            var altId = altTask.Ids?.FirstOrDefault();
                                            var altKey = altId ?? altQuestName;
                                            _questProgress[altKey] = QuestStatus.Failed;
                                            changedItems.Add((altId ?? altKey, altTask.NormalizedName, QuestStatus.Failed));
                                        }
                                    }
                                }
                            }
                        }
                        break;

                    case QuestStatus.Failed:
                        if (!_questProgress.TryGetValue(taskKey, out var currentStatus) || currentStatus != QuestStatus.Failed)
                        {
                            _questProgress[taskKey] = QuestStatus.Failed;
                            changedItems.Add((taskId ?? taskKey, task.NormalizedName, QuestStatus.Failed));
                        }
                        break;
                }
            }

            if (changedItems.Count > 0)
            {
                // Save all changes in one batch transaction
                await _userDataDb.SaveQuestProgressBatchAsync(changedItems);
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Batch saved {changedItems.Count} quest changes");
                ProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Internal batch completion - updates memory state and collects changes without saving
        /// </summary>
        private bool CompleteQuestBatchInternal(TarkovTask task, HashSet<string> visited, List<(string Id, string? NormalizedName, QuestStatus Status)> changedItems)
        {
            var taskId = task.Ids?.FirstOrDefault();
            var taskKey = taskId ?? task.NormalizedName;

            if (string.IsNullOrEmpty(taskKey)) return false;
            if (!visited.Add(taskKey)) return false;

            // Skip if already done
            if (_questProgress.TryGetValue(taskKey, out var currentStatus) && currentStatus == QuestStatus.Done)
                return false;
            if (!string.IsNullOrEmpty(task.NormalizedName) &&
                _questProgress.TryGetValue(task.NormalizedName, out var statusByName) && statusByName == QuestStatus.Done)
                return false;

            _questProgress[taskKey] = QuestStatus.Done;
            changedItems.Add((taskId ?? taskKey, task.NormalizedName, QuestStatus.Done));
            return true;
        }

        /// <summary>
        /// Mark quest as failed
        /// </summary>
        public void FailQuest(TarkovTask task)
        {
            var taskId = task.Ids?.FirstOrDefault();
            var taskKey = taskId ?? task.NormalizedName;

            if (string.IsNullOrEmpty(taskKey)) return;

            _questProgress[taskKey] = QuestStatus.Failed;
            // Fire-and-forget async save - don't block UI
            _ = Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.SaveQuestProgressAsync(taskId ?? taskKey, task.NormalizedName, QuestStatus.Failed);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save failed quest: {ex.Message}");
                }
            });
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Reset quest to active state
        /// </summary>
        public void ResetQuest(TarkovTask task)
        {
            var taskId = task.Ids?.FirstOrDefault();
            var taskKey = taskId ?? task.NormalizedName;

            if (string.IsNullOrEmpty(taskKey)) return;

            // Remove by both Id and NormalizedName for clean migration
            _questProgress.Remove(taskKey);
            if (!string.IsNullOrEmpty(task.NormalizedName) && task.NormalizedName != taskKey)
            {
                _questProgress.Remove(task.NormalizedName);
            }

            // Fire-and-forget async delete - don't block UI
            _ = Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.DeleteQuestProgressAsync(taskId ?? taskKey);
                    // Also delete by NormalizedName for clean migration
                    if (!string.IsNullOrEmpty(task.NormalizedName) && task.NormalizedName != taskKey)
                    {
                        await _userDataDb.DeleteQuestProgressAsync(task.NormalizedName);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to delete quest progress: {ex.Message}");
                }
            });
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Reset all quest progress
        /// </summary>
        public void ResetAllProgress()
        {
            _questProgress.Clear();
            _objectiveProgress.Clear();

            // DB에서 모든 퀘스트 진행 데이터 삭제
            Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.ClearAllQuestProgressAsync();
                    await _userDataDb.ClearAllObjectiveProgressAsync();
                    System.Diagnostics.Debug.WriteLine("[QuestProgressService] All progress cleared from DB");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Reset failed: {ex.Message}");
                }
            }).GetAwaiter().GetResult();

            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Get prerequisite quest chain for a task
        /// </summary>
        public List<TarkovTask> GetPrerequisiteChain(TarkovTask task)
        {
            var chain = new List<TarkovTask>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectPrerequisites(task, chain, visited);

            return chain;
        }

        private void CollectPrerequisites(TarkovTask task, List<TarkovTask> chain, HashSet<string> visited)
        {
            if (task.Previous == null) return;

            foreach (var prevName in task.Previous)
            {
                if (visited.Contains(prevName)) continue;
                visited.Add(prevName);

                var prevTask = GetTask(prevName);
                if (prevTask != null)
                {
                    CollectPrerequisites(prevTask, chain, visited);
                    chain.Add(prevTask);
                }
            }
        }

        /// <summary>
        /// Get follow-up quests for a task
        /// </summary>
        public List<TarkovTask> GetFollowUpQuests(TarkovTask task)
        {
            var followUps = new List<TarkovTask>();

            if (task.LeadsTo != null)
            {
                foreach (var nextName in task.LeadsTo)
                {
                    var nextTask = GetTask(nextName);
                    if (nextTask != null)
                    {
                        followUps.Add(nextTask);
                    }
                }
            }

            return followUps;
        }

        /// <summary>
        /// Get alternative quests (mutually exclusive) for a task
        /// </summary>
        public List<TarkovTask> GetAlternativeQuests(TarkovTask task)
        {
            var alternatives = new List<TarkovTask>();

            if (task.AlternativeQuests != null)
            {
                foreach (var altName in task.AlternativeQuests)
                {
                    var altTask = GetTask(altName);
                    if (altTask != null)
                    {
                        alternatives.Add(altTask);
                    }
                }
            }

            return alternatives;
        }

        /// <summary>
        /// Get count statistics for quest statuses
        /// </summary>
        public (int Total, int Locked, int Active, int Done, int Failed, int LevelLocked, int Unavailable) GetStatistics()
        {
            int locked = 0, active = 0, done = 0, failed = 0, levelLocked = 0, unavailable = 0;

            foreach (var task in _allTasks)
            {
                var status = GetStatus(task);
                switch (status)
                {
                    case QuestStatus.Locked: locked++; break;
                    case QuestStatus.Active: active++; break;
                    case QuestStatus.Done: done++; break;
                    case QuestStatus.Failed: failed++; break;
                    case QuestStatus.LevelLocked: levelLocked++; break;
                    case QuestStatus.Unavailable: unavailable++; break;
                }
            }

            return (_allTasks.Count, locked, active, done, failed, levelLocked, unavailable);
        }

        #region Objective Progress

        /// <summary>
        /// Get objective completion status
        /// </summary>
        public bool IsObjectiveCompleted(string questNormalizedName, int objectiveIndex)
        {
            var key = $"{questNormalizedName}:{objectiveIndex}";
            return _objectiveProgress.TryGetValue(key, out var completed) && completed;
        }

        /// <summary>
        /// Get objective completion status by objective ID
        /// </summary>
        public bool IsObjectiveCompletedById(string objectiveId)
        {
            var key = $"id:{objectiveId}";
            return _objectiveProgress.TryGetValue(key, out var completed) && completed;
        }

        /// <summary>
        /// Set objective completion status (index 기반 - Quests 탭용)
        /// ObjectiveId도 함께 저장하여 Map Tracker와 동기화
        /// </summary>
        public void SetObjectiveCompleted(string questNormalizedName, int objectiveIndex, bool completed, string? objectiveId = null)
        {
            var indexKey = $"{questNormalizedName}:{objectiveIndex}";
            var keysToSave = new List<(string Key, string? QuestId, bool IsCompleted)>();

            if (completed)
            {
                _objectiveProgress[indexKey] = true;
                keysToSave.Add((indexKey, questNormalizedName, true));
                // ObjectiveId도 함께 저장 (동기화)
                if (!string.IsNullOrEmpty(objectiveId))
                {
                    _objectiveProgress[$"id:{objectiveId}"] = true;
                    keysToSave.Add(($"id:{objectiveId}", null, true));
                }
            }
            else
            {
                _objectiveProgress.Remove(indexKey);
                keysToSave.Add((indexKey, questNormalizedName, false));
                // ObjectiveId도 함께 제거 (동기화)
                if (!string.IsNullOrEmpty(objectiveId))
                {
                    _objectiveProgress.Remove($"id:{objectiveId}");
                    keysToSave.Add(($"id:{objectiveId}", null, false));
                }
            }

            // Fire-and-forget async save - don't block UI
            _ = SaveObjectiveProgressBatchAsync(keysToSave);
            ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(questNormalizedName, objectiveIndex, completed));
        }

        /// <summary>
        /// Set objective completion status by objective ID (Map Tracker용)
        /// Index 기반 키도 함께 저장하여 Quests 탭과 동기화
        /// </summary>
        public void SetObjectiveCompletedById(string objectiveId, bool completed, string? questNormalizedName = null, int objectiveIndex = -1)
        {
            var idKey = $"id:{objectiveId}";
            var keysToSave = new List<(string Key, string? QuestId, bool IsCompleted)>();

            if (completed)
            {
                _objectiveProgress[idKey] = true;
                keysToSave.Add((idKey, null, true));
                // Index 기반 키도 함께 저장 (동기화)
                if (!string.IsNullOrEmpty(questNormalizedName) && objectiveIndex >= 0)
                {
                    _objectiveProgress[$"{questNormalizedName}:{objectiveIndex}"] = true;
                    keysToSave.Add(($"{questNormalizedName}:{objectiveIndex}", questNormalizedName, true));
                }
            }
            else
            {
                _objectiveProgress.Remove(idKey);
                keysToSave.Add((idKey, null, false));
                // Index 기반 키도 함께 제거 (동기화)
                if (!string.IsNullOrEmpty(questNormalizedName) && objectiveIndex >= 0)
                {
                    _objectiveProgress.Remove($"{questNormalizedName}:{objectiveIndex}");
                    keysToSave.Add(($"{questNormalizedName}:{objectiveIndex}", questNormalizedName, false));
                }
            }

            // Fire-and-forget async save - don't block UI
            _ = SaveObjectiveProgressBatchAsync(keysToSave);
            ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(objectiveId, objectiveIndex, completed));
        }

        /// <summary>
        /// Save objective progress in batch (fire-and-forget, doesn't block UI)
        /// </summary>
        private async Task SaveObjectiveProgressBatchAsync(List<(string Key, string? QuestId, bool IsCompleted)> items)
        {
            try
            {
                foreach (var item in items)
                {
                    if (item.IsCompleted)
                    {
                        await _userDataDb.SaveObjectiveProgressAsync(item.Key, item.QuestId, true);
                    }
                    else
                    {
                        await _userDataDb.DeleteObjectiveProgressAsync(item.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save objective progress: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all completed objective indices for a quest
        /// </summary>
        public HashSet<int> GetCompletedObjectives(string questNormalizedName)
        {
            var result = new HashSet<int>();
            var prefix = $"{questNormalizedName}:";

            foreach (var kvp in _objectiveProgress)
            {
                if (kvp.Key.StartsWith(prefix) && kvp.Value)
                {
                    var indexStr = kvp.Key.Substring(prefix.Length);
                    if (int.TryParse(indexStr, out var index))
                    {
                        result.Add(index);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Clear all objective progress for a quest
        /// </summary>
        public void ClearObjectiveProgress(string questNormalizedName)
        {
            var prefix = $"{questNormalizedName}:";
            var keysToRemove = _objectiveProgress.Keys.Where(k => k.StartsWith(prefix)).ToList();

            foreach (var key in keysToRemove)
            {
                _objectiveProgress.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                SaveObjectiveProgress();
                ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(questNormalizedName, -1, false));
            }
        }

        /// <summary>
        /// Get objective completion count for a quest
        /// </summary>
        public (int Completed, int Total) GetObjectiveProgress(TarkovTask task)
        {
            if (task.NormalizedName == null || task.Objectives == null)
                return (0, 0);

            var completedSet = GetCompletedObjectives(task.NormalizedName);
            return (completedSet.Count, task.Objectives.Count);
        }

        #endregion

        #region Persistence

        private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

        private void SaveProgress()
        {
            // DB에 저장 (Task.Run으로 데드락 방지)
            Task.Run(async () => await SaveProgressToDbAsync()).GetAwaiter().GetResult();
        }

        private async Task SaveProgressToDbAsync()
        {
            try
            {
                foreach (var kvp in _questProgress)
                {
                    var normalizedName = kvp.Key;
                    var status = kvp.Value;

                    // ID 조회
                    string id = normalizedName;
                    if (_tasksByNormalizedName.TryGetValue(normalizedName, out var task))
                    {
                        id = task.Ids?.FirstOrDefault() ?? normalizedName;
                    }

                    await _userDataDb.SaveQuestProgressAsync(id, normalizedName, status);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save progress to DB: {ex.Message}");
            }
        }

        /// <summary>
        /// 단일 퀘스트 진행 상태를 DB에 저장
        /// </summary>
        private void SaveSingleQuestProgress(string normalizedName, QuestStatus status)
        {
            Task.Run(async () =>
            {
                try
                {
                    string id = normalizedName;
                    if (_tasksByNormalizedName.TryGetValue(normalizedName, out var task))
                    {
                        id = task.Ids?.FirstOrDefault() ?? normalizedName;
                    }

                    await _userDataDb.SaveQuestProgressAsync(id, normalizedName, status);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save single quest progress: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 단일 퀘스트 진행 상태를 DB에서 삭제
        /// </summary>
        private void DeleteSingleQuestProgress(string normalizedName)
        {
            Task.Run(async () =>
            {
                try
                {
                    string id = normalizedName;
                    if (_tasksByNormalizedName.TryGetValue(normalizedName, out var task))
                    {
                        id = task.Ids?.FirstOrDefault() ?? normalizedName;
                    }

                    await _userDataDb.DeleteQuestProgressAsync(id);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to delete quest progress: {ex.Message}");
                }
            });
        }

        private void LoadProgress()
        {
            // Task.Run으로 데드락 방지
            // 마이그레이션은 MainWindow에서 먼저 수행됨
            Task.Run(async () =>
            {
                // DB에서 로드
                await LoadProgressFromDbAsync();
            }).GetAwaiter().GetResult();
        }

        private async Task LoadProgressFromDbAsync()
        {
            try
            {
                var dbProgress = await _userDataDb.LoadQuestProgressAsync();

                _questProgress.Clear();
                foreach (var kvp in dbProgress)
                {
                    _questProgress[kvp.Key] = kvp.Value;
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Loaded progress: {kvp.Key} = {kvp.Value}");
                }

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Loaded {_questProgress.Count} quest progress from DB");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to load progress from DB: {ex.Message}");
                _questProgress.Clear();
            }
        }

        private void SaveObjectiveProgress()
        {
            // DB에 저장 (Task.Run으로 데드락 방지)
            Task.Run(async () => await SaveObjectiveProgressToDbAsync()).GetAwaiter().GetResult();
        }

        private async Task SaveObjectiveProgressToDbAsync()
        {
            try
            {
                foreach (var kvp in _objectiveProgress)
                {
                    // 키 형식: "questName:index" 또는 "id:objectiveId"
                    string? questId = null;
                    if (kvp.Key.Contains(':'))
                    {
                        var parts = kvp.Key.Split(':');
                        if (parts[0] != "id")
                        {
                            questId = parts[0];
                        }
                    }

                    await _userDataDb.SaveObjectiveProgressAsync(kvp.Key, questId, kvp.Value);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save objective progress to DB: {ex.Message}");
            }
        }

        /// <summary>
        /// 단일 목표 진행 상태를 DB에 저장
        /// </summary>
        private void SaveSingleObjectiveProgress(string key, bool isCompleted, string? questId = null)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.SaveObjectiveProgressAsync(key, questId, isCompleted);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to save single objective progress: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 단일 목표 진행 상태를 DB에서 삭제
        /// </summary>
        private void DeleteSingleObjectiveProgress(string key)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.DeleteObjectiveProgressAsync(key);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to delete objective progress: {ex.Message}");
                }
            });
        }

        private void LoadObjectiveProgress()
        {
            // DB에서 로드 (Task.Run으로 데드락 방지)
            Task.Run(async () => await LoadObjectiveProgressFromDbAsync()).GetAwaiter().GetResult();
        }

        private async Task LoadObjectiveProgressFromDbAsync()
        {
            try
            {
                var dbProgress = await _userDataDb.LoadObjectiveProgressAsync();

                _objectiveProgress.Clear();
                foreach (var kvp in dbProgress)
                {
                    _objectiveProgress[kvp.Key] = kvp.Value;
                }

                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Loaded {_objectiveProgress.Count} objective progress from DB");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[QuestProgressService] Failed to load objective progress from DB: {ex.Message}");
                _objectiveProgress.Clear();
            }
        }

        #endregion
    }

    /// <summary>
    /// Event args for objective progress changes
    /// </summary>
    public class ObjectiveProgressChangedEventArgs : EventArgs
    {
        public string QuestNormalizedName { get; }
        public int ObjectiveIndex { get; }
        public bool IsCompleted { get; }

        public ObjectiveProgressChangedEventArgs(string questNormalizedName, int objectiveIndex, bool isCompleted)
        {
            QuestNormalizedName = questNormalizedName;
            ObjectiveIndex = objectiveIndex;
            IsCompleted = isCompleted;
        }
    }
}
