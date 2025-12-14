using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for managing quest dependency graph and traversal
    /// Provides methods to find prerequisites, follow-ups, and optimal paths
    /// </summary>
    public class QuestGraphService
    {
        private static QuestGraphService? _instance;
        public static QuestGraphService Instance => _instance ??= new QuestGraphService();

        private List<TarkovTask>? _tasks;
        private Dictionary<string, TarkovTask>? _taskLookup;

        /// <summary>
        /// Initialize the service with task data from DB
        /// </summary>
        public async Task InitializeAsync()
        {
            var questDbService = QuestDbService.Instance;
            if (!questDbService.IsLoaded)
            {
                await questDbService.LoadQuestsAsync();
            }
            _tasks = questDbService.AllQuests.ToList();
            BuildLookup();
        }

        /// <summary>
        /// Initialize the service with provided tasks
        /// </summary>
        public void Initialize(List<TarkovTask> tasks)
        {
            _tasks = tasks;
            BuildLookup();
        }

        private void BuildLookup()
        {
            if (_tasks == null) return;

            _taskLookup = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
            foreach (var task in _tasks)
            {
                if (!string.IsNullOrEmpty(task.NormalizedName))
                {
                    _taskLookup[task.NormalizedName] = task;
                }
            }
        }

        /// <summary>
        /// Get a task by its normalized name
        /// </summary>
        public TarkovTask? GetTask(string normalizedName)
        {
            EnsureInitialized();
            return _taskLookup!.TryGetValue(normalizedName, out var task) ? task : null;
        }

        /// <summary>
        /// Get all tasks
        /// </summary>
        public List<TarkovTask> GetAllTasks()
        {
            EnsureInitialized();
            return _tasks!;
        }

        /// <summary>
        /// Get all direct prerequisites for a quest (non-recursive)
        /// </summary>
        public List<TarkovTask> GetDirectPrerequisites(string questNormalizedName)
        {
            EnsureInitialized();

            var task = GetTask(questNormalizedName);
            if (task?.Previous == null || task.Previous.Count == 0)
                return new List<TarkovTask>();

            return task.Previous
                .Select(p => GetTask(p))
                .Where(t => t != null)
                .Cast<TarkovTask>()
                .ToList();
        }

        /// <summary>
        /// Get all prerequisites for a quest recursively (full prerequisite chain)
        /// </summary>
        /// <param name="questNormalizedName">Target quest normalized name</param>
        /// <returns>List of all prerequisite quests in topological order (earliest first)</returns>
        public List<TarkovTask> GetAllPrerequisites(string questNormalizedName)
        {
            EnsureInitialized();

            var result = new List<TarkovTask>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectPrerequisites(questNormalizedName, visited, visiting, result);

            return result;
        }

        private void CollectPrerequisites(string questName, HashSet<string> visited, HashSet<string> visiting, List<TarkovTask> result)
        {
            if (visited.Contains(questName) || visiting.Contains(questName))
                return;

            visiting.Add(questName);

            var task = GetTask(questName);
            if (task?.Previous != null)
            {
                foreach (var prev in task.Previous)
                {
                    CollectPrerequisites(prev, visited, visiting, result);
                }
            }

            visiting.Remove(questName);
            visited.Add(questName);

            // Add the task itself only if it's a prerequisite (not the target)
            if (task != null)
            {
                result.Add(task);
            }
        }

        /// <summary>
        /// Get all direct follow-up quests (non-recursive)
        /// </summary>
        public List<TarkovTask> GetDirectFollowUps(string questNormalizedName)
        {
            EnsureInitialized();

            var task = GetTask(questNormalizedName);
            if (task?.LeadsTo == null || task.LeadsTo.Count == 0)
                return new List<TarkovTask>();

            return task.LeadsTo
                .Select(l => GetTask(l))
                .Where(t => t != null)
                .Cast<TarkovTask>()
                .ToList();
        }

        /// <summary>
        /// Get all follow-up quests recursively (full follow-up chain)
        /// </summary>
        /// <param name="questNormalizedName">Starting quest normalized name</param>
        /// <returns>List of all follow-up quests</returns>
        public List<TarkovTask> GetAllFollowUps(string questNormalizedName)
        {
            EnsureInitialized();

            var result = new List<TarkovTask>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            CollectFollowUps(questNormalizedName, visited, visiting, result);

            return result;
        }

        private void CollectFollowUps(string questName, HashSet<string> visited, HashSet<string> visiting, List<TarkovTask> result)
        {
            if (visited.Contains(questName) || visiting.Contains(questName))
                return;

            visiting.Add(questName);

            var task = GetTask(questName);
            if (task != null)
            {
                result.Add(task);
            }

            if (task?.LeadsTo != null)
            {
                foreach (var next in task.LeadsTo)
                {
                    CollectFollowUps(next, visited, visiting, result);
                }
            }

            visiting.Remove(questName);
            visited.Add(questName);
        }

        /// <summary>
        /// Get the optimal path to complete a target quest
        /// Returns quests in order they should be completed
        /// </summary>
        /// <param name="targetQuestNormalizedName">Target quest to complete</param>
        /// <returns>List of quests in optimal completion order</returns>
        public List<TarkovTask> GetOptimalPath(string targetQuestNormalizedName)
        {
            EnsureInitialized();

            var target = GetTask(targetQuestNormalizedName);
            if (target == null)
                return new List<TarkovTask>();

            var result = new List<TarkovTask>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tempMark = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TopologicalSort(targetQuestNormalizedName, visited, tempMark, result);

            return result;
        }

        private void TopologicalSort(string questName, HashSet<string> visited, HashSet<string> tempMark, List<TarkovTask> result)
        {
            if (visited.Contains(questName))
                return;

            if (tempMark.Contains(questName))
            {
                // Circular dependency detected - skip to avoid infinite loop
                return;
            }

            tempMark.Add(questName);

            var task = GetTask(questName);
            if (task?.Previous != null)
            {
                foreach (var prev in task.Previous)
                {
                    TopologicalSort(prev, visited, tempMark, result);
                }
            }

            tempMark.Remove(questName);
            visited.Add(questName);

            if (task != null)
            {
                result.Add(task);
            }
        }

        /// <summary>
        /// Detect circular dependencies in the quest graph
        /// </summary>
        /// <returns>List of quests involved in circular dependencies</returns>
        public List<CircularDependency> DetectCircularDependencies()
        {
            EnsureInitialized();

            var result = new List<CircularDependency>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in _tasks!)
            {
                if (string.IsNullOrEmpty(task.NormalizedName)) continue;
                if (visited.Contains(task.NormalizedName)) continue;

                var path = new List<string>();
                if (DetectCycleFromNode(task.NormalizedName, visited, recStack, path))
                {
                    result.Add(new CircularDependency
                    {
                        StartQuest = task.NormalizedName,
                        Cycle = new List<string>(path)
                    });
                }
            }

            return result;
        }

        private bool DetectCycleFromNode(string questName, HashSet<string> visited, HashSet<string> recStack, List<string> path)
        {
            visited.Add(questName);
            recStack.Add(questName);
            path.Add(questName);

            var task = GetTask(questName);
            if (task?.Previous != null)
            {
                foreach (var prev in task.Previous)
                {
                    if (!visited.Contains(prev))
                    {
                        if (DetectCycleFromNode(prev, visited, recStack, path))
                            return true;
                    }
                    else if (recStack.Contains(prev))
                    {
                        path.Add(prev); // Complete the cycle
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            recStack.Remove(questName);
            return false;
        }

        /// <summary>
        /// Get quest statistics
        /// </summary>
        public QuestGraphStats GetStats()
        {
            EnsureInitialized();

            var stats = new QuestGraphStats
            {
                TotalQuests = _tasks!.Count,
                QuestsWithPrerequisites = _tasks.Count(t => t.Previous != null && t.Previous.Count > 0),
                QuestsWithFollowUps = _tasks.Count(t => t.LeadsTo != null && t.LeadsTo.Count > 0),
                QuestsWithItemRequirements = _tasks.Count(t => t.RequiredItems != null && t.RequiredItems.Count > 0),
                QuestsWithSkillRequirements = _tasks.Count(t => t.RequiredSkills != null && t.RequiredSkills.Count > 0),
                QuestsWithLevelRequirements = _tasks.Count(t => t.RequiredLevel.HasValue),
                KappaQuests = _tasks.Count(t => t.ReqKappa)
            };

            // Find starter quests (no prerequisites)
            stats.StarterQuests = _tasks
                .Where(t => t.Previous == null || t.Previous.Count == 0)
                .Select(t => t.NormalizedName ?? t.Name)
                .ToList();

            // Find terminal quests (no follow-ups)
            stats.TerminalQuests = _tasks
                .Where(t => t.LeadsTo == null || t.LeadsTo.Count == 0)
                .Select(t => t.NormalizedName ?? t.Name)
                .ToList();

            return stats;
        }

        /// <summary>
        /// Get quest chains by trader
        /// </summary>
        public Dictionary<string, List<TarkovTask>> GetQuestsByTrader()
        {
            EnsureInitialized();

            return _tasks!
                .GroupBy(t => t.Trader)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        /// <summary>
        /// Get Collector quest progress statistics
        /// Returns the count and percentage of completed reqKappa quests
        /// </summary>
        /// <param name="isQuestCompleted">Function to check if a quest is completed by its normalizedName</param>
        /// <returns>Tuple of (completed count, total count, percentage)</returns>
        public (int Completed, int Total, int Percentage) GetCollectorProgress(Func<string, bool> isQuestCompleted)
        {
            EnsureInitialized();

            var kappaQuests = _tasks!
                .Where(t => t.ReqKappa && !string.IsNullOrEmpty(t.NormalizedName))
                .ToList();

            var completedCount = kappaQuests.Count(t => isQuestCompleted(t.NormalizedName!));
            var total = kappaQuests.Count;
            var percentage = total > 0 ? (completedCount * 100 / total) : 0;

            return (completedCount, total, percentage);
        }

        /// <summary>
        /// Get all reqKappa quests with their completion status
        /// </summary>
        /// <param name="isQuestCompleted">Function to check if a quest is completed</param>
        /// <returns>List of tuples (quest, isCompleted)</returns>
        public List<(TarkovTask Quest, bool IsCompleted)> GetKappaRequiredQuestsWithStatus(Func<string, bool> isQuestCompleted)
        {
            EnsureInitialized();

            return _tasks!
                .Where(t => t.ReqKappa && !string.IsNullOrEmpty(t.NormalizedName))
                .Select(t => (t, isQuestCompleted(t.NormalizedName!)))
                .OrderBy(x => x.Item2) // Incomplete first
                .ThenBy(x => x.t.Trader)
                .ThenBy(x => x.t.Name)
                .ToList();
        }

        /// <summary>
        /// Check if a quest is the Collector quest
        /// </summary>
        public bool IsCollectorQuest(TarkovTask task)
        {
            return task.NormalizedName?.Equals("collector", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Get all Kappa-required quests in optimal completion order
        /// </summary>
        public List<TarkovTask> GetKappaPath()
        {
            EnsureInitialized();

            var kappaQuests = _tasks!.Where(t => t.ReqKappa).ToList();
            var allRequired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Collect all prerequisites for Kappa quests
            foreach (var quest in kappaQuests)
            {
                if (!string.IsNullOrEmpty(quest.NormalizedName))
                {
                    allRequired.Add(quest.NormalizedName);
                    var prereqs = GetAllPrerequisites(quest.NormalizedName);
                    foreach (var prereq in prereqs)
                    {
                        if (!string.IsNullOrEmpty(prereq.NormalizedName))
                            allRequired.Add(prereq.NormalizedName);
                    }
                }
            }

            // Sort by optimal completion order
            var result = new List<TarkovTask>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tempMark = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var questName in allRequired.OrderBy(q => q))
            {
                TopologicalSort(questName, visited, tempMark, result);
            }

            return result.Where(t => allRequired.Contains(t.NormalizedName ?? "")).ToList();
        }

        private void EnsureInitialized()
        {
            if (_tasks == null || _taskLookup == null)
            {
                throw new InvalidOperationException("QuestGraphService not initialized. Call InitializeAsync() first.");
            }
        }
    }

    /// <summary>
    /// Represents a circular dependency in the quest graph
    /// </summary>
    public class CircularDependency
    {
        public string StartQuest { get; set; } = string.Empty;
        public List<string> Cycle { get; set; } = new();
    }

    /// <summary>
    /// Quest graph statistics
    /// </summary>
    public class QuestGraphStats
    {
        public int TotalQuests { get; set; }
        public int QuestsWithPrerequisites { get; set; }
        public int QuestsWithFollowUps { get; set; }
        public int QuestsWithItemRequirements { get; set; }
        public int QuestsWithSkillRequirements { get; set; }
        public int QuestsWithLevelRequirements { get; set; }
        public int KappaQuests { get; set; }
        public List<string> StarterQuests { get; set; } = new();
        public List<string> TerminalQuests { get; set; } = new();
    }
}
