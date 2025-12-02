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

        private Dictionary<string, QuestStatus> _questProgress = new();
        private Dictionary<string, TarkovTask> _tasksByNormalizedName = new();
        private List<TarkovTask> _allTasks = new();

        public event EventHandler? ProgressChanged;

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

            // Check prerequisites
            if (!ArePrerequisitesMet(task))
                return QuestStatus.Locked;

            return QuestStatus.Active;
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
        /// </summary>
        public void CompleteQuest(TarkovTask task, bool completePrerequisites = true)
        {
            if (task.NormalizedName == null) return;

            // Complete prerequisites first (recursive)
            if (completePrerequisites && task.Previous != null)
            {
                foreach (var prevName in task.Previous)
                {
                    var prevTask = GetTask(prevName);
                    if (prevTask != null && GetStatus(prevTask) != QuestStatus.Done)
                    {
                        CompleteQuest(prevTask, true);
                    }
                }
            }

            _questProgress[task.NormalizedName] = QuestStatus.Done;
            SaveProgress();
            ProgressChanged?.Invoke(this, EventArgs.Empty);
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
        /// Get count statistics for quest statuses
        /// </summary>
        public (int Total, int Locked, int Active, int Done, int Failed) GetStatistics()
        {
            int locked = 0, active = 0, done = 0, failed = 0;

            foreach (var task in _allTasks)
            {
                var status = GetStatus(task);
                switch (status)
                {
                    case QuestStatus.Locked: locked++; break;
                    case QuestStatus.Active: active++; break;
                    case QuestStatus.Done: done++; break;
                    case QuestStatus.Failed: failed++; break;
                }
            }

            return (_allTasks.Count, locked, active, done, failed);
        }

        #region Persistence

        private void SaveProgress()
        {
            try
            {
                var filePath = Path.Combine(AppEnv.DataPath, ProgressFileName);
                Directory.CreateDirectory(AppEnv.DataPath);

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
                var filePath = Path.Combine(AppEnv.DataPath, ProgressFileName);

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

        #endregion
    }
}
