using System.IO;
using System.Text.Json;
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

        private const string ProgressFileName = "quest_progress.json";
        private const string ObjectiveProgressFileName = "objective_progress.json";

        private Dictionary<string, QuestStatus> _questProgress = new();
        private Dictionary<string, TarkovTask> _tasksByNormalizedName = new();
        private List<TarkovTask> _allTasks = new();

        // Objective progress: key = "questNormalizedName:objectiveIndex", value = completed
        private Dictionary<string, bool> _objectiveProgress = new();

        public event EventHandler? ProgressChanged;
        public event EventHandler<ObjectiveProgressChangedEventArgs>? ObjectiveProgressChanged;

        /// <summary>
        /// Initialize service with task data
        /// </summary>
        public void Initialize(List<TarkovTask> tasks)
        {
            _allTasks = tasks;

            // Build dictionary, handling duplicates by keeping the first occurrence
            _tasksByNormalizedName = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in tasks.Where(t => !string.IsNullOrEmpty(t.NormalizedName)))
            {
                if (!_tasksByNormalizedName.ContainsKey(task.NormalizedName!))
                {
                    _tasksByNormalizedName[task.NormalizedName!] = task;
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
        /// Get task by normalized name
        /// </summary>
        public TarkovTask? GetTask(string normalizedName)
        {
            return _tasksByNormalizedName.TryGetValue(normalizedName, out var task) ? task : null;
        }

        // Thread-local visited set for GetStatus to prevent circular reference during status check
        [ThreadStatic]
        private static HashSet<string>? _getStatusVisited;

        /// <summary>
        /// Get quest status for a task
        /// </summary>
        public QuestStatus GetStatus(TarkovTask task)
        {
            if (task.NormalizedName == null) return QuestStatus.Active;

            // Check if manually set to Done or Failed
            if (_questProgress.TryGetValue(task.NormalizedName, out var status))
            {
                if (status == QuestStatus.Done || status == QuestStatus.Failed)
                    return status;
            }

            // Circular reference protection for prerequisite checking
            bool isTopLevel = _getStatusVisited == null;
            _getStatusVisited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // If already checking this task (circular reference), treat as Active to break the cycle
            if (!_getStatusVisited.Add(task.NormalizedName))
            {
                return QuestStatus.Active;
            }

            try
            {
                // Check DSP Decode Count for Make Amends quests
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
                _getStatusVisited.Remove(task.NormalizedName);
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

        // Make Amends quest normalized names for DSP Decode filtering
        private static readonly string MakeAmendsBuyout = "make-amends-buyout";
        private static readonly string MakeAmendsSecurity = "make-amends-security";
        private static readonly string MakeAmendsSoftware = "make-amends-software";

        /// <summary>
        /// Check if DSP Decode Count requirement is met for Make Amends quests
        /// - 0 Decodes: No Make Amends quest available
        /// - 1 Decode: Buyout is available
        /// - 2 Decodes: Security is available
        /// - 3 Decodes: Software is available
        /// </summary>
        public bool IsDspRequirementMet(TarkovTask task)
        {
            if (task.NormalizedName == null) return true;

            var normalizedName = task.NormalizedName.ToLowerInvariant();
            var dspCount = SettingsService.Instance.DspDecodeCount;

            // Check if this is a Make Amends quest
            if (normalizedName == MakeAmendsBuyout)
            {
                return dspCount == 1;
            }
            else if (normalizedName == MakeAmendsSecurity)
            {
                return dspCount == 2;
            }
            else if (normalizedName == MakeAmendsSoftware)
            {
                return dspCount == 3;
            }

            // Not a Make Amends quest, no DSP requirement
            return true;
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
        /// Check if all prerequisites are completed
        /// </summary>
        public bool ArePrerequisitesMet(TarkovTask task)
        {
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
        /// Mark quest as completed, optionally completing prerequisites
        /// Also automatically fails alternative quests (mutually exclusive quests)
        /// </summary>
        public void CompleteQuest(TarkovTask task, bool completePrerequisites = true)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool anyChanged = CompleteQuestInternal(task, completePrerequisites, visited);

            // Fail alternative quests (mutually exclusive)
            if (task.AlternativeQuests != null && task.AlternativeQuests.Count > 0)
            {
                foreach (var altQuestName in task.AlternativeQuests)
                {
                    var altTask = GetTask(altQuestName);
                    if (altTask != null)
                    {
                        var altStatus = GetStatus(altTask);
                        // Only fail if not already done or failed
                        if (altStatus != QuestStatus.Done && altStatus != QuestStatus.Failed)
                        {
                            _questProgress[altQuestName] = QuestStatus.Failed;
                            anyChanged = true;
                        }
                    }
                }
            }

            // Save and notify only once after all recursive completions
            if (anyChanged)
            {
                SaveProgress();
                ProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Internal method to complete quest with circular reference prevention
        /// Returns true if any quest was changed
        /// </summary>
        private bool CompleteQuestInternal(TarkovTask task, bool completePrerequisites, HashSet<string> visited)
        {
            if (task.NormalizedName == null) return false;

            // Prevent circular reference - if already visiting this quest, skip
            if (!visited.Add(task.NormalizedName)) return false;

            // Skip if already done
            if (_questProgress.TryGetValue(task.NormalizedName, out var currentStatus) && currentStatus == QuestStatus.Done)
                return false;

            // Complete prerequisites first (recursive)
            if (completePrerequisites && task.Previous != null)
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

            _questProgress[task.NormalizedName] = QuestStatus.Done;
            return true;
        }

        /// <summary>
        /// Mark quest as failed
        /// </summary>
        public void FailQuest(TarkovTask task)
        {
            if (task.NormalizedName == null) return;

            _questProgress[task.NormalizedName] = QuestStatus.Failed;
            SaveProgress();
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Reset quest to active state
        /// </summary>
        public void ResetQuest(TarkovTask task)
        {
            if (task.NormalizedName == null) return;

            _questProgress.Remove(task.NormalizedName);
            SaveProgress();
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Reset all quest progress
        /// </summary>
        public void ResetAllProgress()
        {
            _questProgress.Clear();
            SaveProgress();
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
        public (int Total, int Locked, int Active, int Done, int Failed, int LevelLocked) GetStatistics()
        {
            int locked = 0, active = 0, done = 0, failed = 0, levelLocked = 0;

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
                }
            }

            return (_allTasks.Count, locked, active, done, failed, levelLocked);
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
        /// Set objective completion status
        /// </summary>
        public void SetObjectiveCompleted(string questNormalizedName, int objectiveIndex, bool completed)
        {
            var key = $"{questNormalizedName}:{objectiveIndex}";

            if (completed)
            {
                _objectiveProgress[key] = true;
            }
            else
            {
                _objectiveProgress.Remove(key);
            }

            SaveObjectiveProgress();
            ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(questNormalizedName, objectiveIndex, completed));
        }

        /// <summary>
        /// Set objective completion status by objective ID
        /// </summary>
        public void SetObjectiveCompletedById(string objectiveId, bool completed)
        {
            var key = $"id:{objectiveId}";

            if (completed)
            {
                _objectiveProgress[key] = true;
            }
            else
            {
                _objectiveProgress.Remove(key);
            }

            SaveObjectiveProgress();
            ObjectiveProgressChanged?.Invoke(this, new ObjectiveProgressChangedEventArgs(objectiveId, -1, completed));
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

        private void SaveProgress()
        {
            try
            {
                var filePath = Path.Combine(AppEnv.ConfigPath, ProgressFileName);
                Directory.CreateDirectory(AppEnv.ConfigPath);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                // Convert enum to string for JSON
                var progressData = _questProgress.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToString()
                );

                var json = JsonSerializer.Serialize(progressData, options);
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // Ignore save failures
            }
        }

        private void LoadProgress()
        {
            try
            {
                var filePath = Path.Combine(AppEnv.ConfigPath, ProgressFileName);

                if (!File.Exists(filePath))
                    return;

                var json = File.ReadAllText(filePath);
                var progressData = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (progressData != null)
                {
                    _questProgress.Clear();
                    foreach (var kvp in progressData)
                    {
                        if (Enum.TryParse<QuestStatus>(kvp.Value, out var status))
                        {
                            _questProgress[kvp.Key] = status;
                        }
                    }
                }
            }
            catch
            {
                // Use empty progress on load failure
                _questProgress.Clear();
            }
        }

        private void SaveObjectiveProgress()
        {
            try
            {
                var filePath = Path.Combine(AppEnv.ConfigPath, ObjectiveProgressFileName);
                Directory.CreateDirectory(AppEnv.ConfigPath);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(_objectiveProgress, options);
                File.WriteAllText(filePath, json);
            }
            catch
            {
                // Ignore save failures
            }
        }

        private void LoadObjectiveProgress()
        {
            try
            {
                var filePath = Path.Combine(AppEnv.ConfigPath, ObjectiveProgressFileName);

                if (!File.Exists(filePath))
                    return;

                var json = File.ReadAllText(filePath);
                var progressData = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);

                if (progressData != null)
                {
                    _objectiveProgress = progressData;
                }
            }
            catch
            {
                // Use empty progress on load failure
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
