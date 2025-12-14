using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for managing hideout construction progress state
    /// </summary>
    public class HideoutProgressService
    {
        private static HideoutProgressService? _instance;
        public static HideoutProgressService Instance => _instance ??= new HideoutProgressService();

        private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

        // Currency items should count by reference count, not total amount
        private static readonly HashSet<string> CurrencyItems = new(StringComparer.OrdinalIgnoreCase)
        {
            "roubles", "dollars", "euros"
        };

        private static bool IsCurrency(string normalizedName) => CurrencyItems.Contains(normalizedName);

        private HideoutProgress _progress = new();
        private Dictionary<string, HideoutModule> _modulesByNormalizedName = new();
        private List<HideoutModule> _allModules = new();

        public event EventHandler? ProgressChanged;

        /// <summary>
        /// Initialize service with hideout module data
        /// </summary>
        public void Initialize(List<HideoutModule> modules)
        {
            _allModules = modules;

            // Build dictionary by normalized name
            _modulesByNormalizedName = new Dictionary<string, HideoutModule>(StringComparer.OrdinalIgnoreCase);
            foreach (var module in modules.Where(m => !string.IsNullOrEmpty(m.NormalizedName)))
            {
                if (!_modulesByNormalizedName.ContainsKey(module.NormalizedName))
                {
                    _modulesByNormalizedName[module.NormalizedName] = module;
                }
            }

            LoadProgress();
        }

        /// <summary>
        /// Get all modules
        /// </summary>
        public IReadOnlyList<HideoutModule> AllModules => _allModules;

        /// <summary>
        /// Get module by normalized name
        /// </summary>
        public HideoutModule? GetModule(string normalizedName)
        {
            return _modulesByNormalizedName.TryGetValue(normalizedName, out var module) ? module : null;
        }

        /// <summary>
        /// Get current level for a module (0 = not built)
        /// </summary>
        public int GetCurrentLevel(HideoutModule module)
        {
            if (string.IsNullOrEmpty(module.NormalizedName))
                return 0;

            return _progress.Modules.TryGetValue(module.NormalizedName, out var level) ? level : 0;
        }

        /// <summary>
        /// Get current level for a module by normalized name
        /// </summary>
        public int GetCurrentLevel(string normalizedName)
        {
            return _progress.Modules.TryGetValue(normalizedName, out var level) ? level : 0;
        }

        /// <summary>
        /// Set current level for a module
        /// </summary>
        public void SetLevel(HideoutModule module, int level)
        {
            if (string.IsNullOrEmpty(module.NormalizedName))
                return;

            // Clamp level between 0 and max level
            level = Math.Max(0, Math.Min(level, module.MaxLevel));

            if (level == 0)
            {
                _progress.Modules.Remove(module.NormalizedName);
            }
            else
            {
                _progress.Modules[module.NormalizedName] = level;
            }

            _progress.LastUpdated = DateTime.UtcNow;
            SaveSingleModule(module.NormalizedName, level);
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SaveSingleModule(string normalizedName, int level)
        {
            Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.SaveHideoutProgressAsync(normalizedName, level);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HideoutProgressService] Save failed: {ex.Message}");
                }
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Increment level for a module
        /// </summary>
        public void IncrementLevel(HideoutModule module)
        {
            var currentLevel = GetCurrentLevel(module);
            if (currentLevel < module.MaxLevel)
            {
                SetLevel(module, currentLevel + 1);
            }
        }

        /// <summary>
        /// Decrement level for a module
        /// </summary>
        public void DecrementLevel(HideoutModule module)
        {
            var currentLevel = GetCurrentLevel(module);
            if (currentLevel > 0)
            {
                SetLevel(module, currentLevel - 1);
            }
        }

        /// <summary>
        /// Get next level requirements for a module
        /// </summary>
        public HideoutLevel? GetNextLevel(HideoutModule module)
        {
            var currentLevel = GetCurrentLevel(module);
            return module.Levels.FirstOrDefault(l => l.Level == currentLevel + 1);
        }

        /// <summary>
        /// Get total remaining item requirements for a module (all levels after current)
        /// </summary>
        public Dictionary<string, (HideoutItemRequirement Item, int TotalCount, int FIRCount)> GetRemainingItemRequirements(HideoutModule module)
        {
            var currentLevel = GetCurrentLevel(module);
            var result = new Dictionary<string, (HideoutItemRequirement Item, int TotalCount, int FIRCount)>(StringComparer.OrdinalIgnoreCase);

            foreach (var level in module.Levels.Where(l => l.Level > currentLevel))
            {
                foreach (var itemReq in level.ItemRequirements)
                {
                    if (result.TryGetValue(itemReq.ItemNormalizedName, out var existing))
                    {
                        var newFirCount = existing.FIRCount + (itemReq.FoundInRaid ? itemReq.Count : 0);
                        result[itemReq.ItemNormalizedName] = (existing.Item, existing.TotalCount + itemReq.Count, newFirCount);
                    }
                    else
                    {
                        var firCount = itemReq.FoundInRaid ? itemReq.Count : 0;
                        result[itemReq.ItemNormalizedName] = (itemReq, itemReq.Count, firCount);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Get total item requirements for all incomplete hideout modules
        /// </summary>
        public Dictionary<string, HideoutItemAggregate> GetAllRemainingItemRequirements()
        {
            var result = new Dictionary<string, HideoutItemAggregate>(StringComparer.OrdinalIgnoreCase);

            foreach (var module in _allModules)
            {
                var currentLevel = GetCurrentLevel(module);

                foreach (var level in module.Levels.Where(l => l.Level > currentLevel))
                {
                    foreach (var itemReq in level.ItemRequirements)
                    {
                        // For currency items, count by reference (1 per hideout level) instead of total amount
                        var countToAdd = IsCurrency(itemReq.ItemNormalizedName) ? 1 : itemReq.Count;
                        var firCountToAdd = itemReq.FoundInRaid ? countToAdd : 0;

                        if (result.TryGetValue(itemReq.ItemNormalizedName, out var existing))
                        {
                            existing.HideoutCount += countToAdd;
                            existing.TotalCount += countToAdd;
                            // Track FIR count separately
                            if (itemReq.FoundInRaid)
                            {
                                existing.HideoutFIRCount += countToAdd;
                                existing.TotalFIRCount += countToAdd;
                                existing.FoundInRaid = true;
                            }
                        }
                        else
                        {
                            result[itemReq.ItemNormalizedName] = new HideoutItemAggregate
                            {
                                ItemId = itemReq.ItemId,
                                ItemName = itemReq.ItemName,
                                ItemNameKo = itemReq.ItemNameKo,
                                ItemNameJa = itemReq.ItemNameJa,
                                ItemNormalizedName = itemReq.ItemNormalizedName,
                                IconLink = itemReq.IconLink,
                                HideoutCount = countToAdd,
                                HideoutFIRCount = firCountToAdd,
                                QuestCount = 0,
                                QuestFIRCount = 0,
                                TotalCount = countToAdd,
                                TotalFIRCount = firCountToAdd,
                                FoundInRaid = itemReq.FoundInRaid
                            };
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Check if all prerequisites are met for building a specific level
        /// </summary>
        public bool ArePrerequisitesMet(HideoutModule module, int targetLevel)
        {
            var level = module.Levels.FirstOrDefault(l => l.Level == targetLevel);
            if (level == null)
                return false;

            // Check station level requirements
            foreach (var stationReq in level.StationLevelRequirements)
            {
                var requiredStationLevel = GetCurrentLevel(stationReq.StationId);
                if (requiredStationLevel < stationReq.Level)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Get construction statistics
        /// </summary>
        public HideoutStatistics GetStatistics()
        {
            var stats = new HideoutStatistics();

            foreach (var module in _allModules)
            {
                var currentLevel = GetCurrentLevel(module);
                var maxLevel = module.MaxLevel;

                stats.TotalModules++;
                stats.TotalLevels += maxLevel;
                stats.CompletedLevels += currentLevel;

                if (currentLevel == 0)
                    stats.NotStarted++;
                else if (currentLevel >= maxLevel)
                    stats.FullyCompleted++;
                else
                    stats.InProgress++;
            }

            return stats;
        }

        /// <summary>
        /// Reset all hideout progress
        /// </summary>
        public void ResetAllProgress()
        {
            _progress = new HideoutProgress();
            Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.ClearAllHideoutProgressAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HideoutProgressService] Reset failed: {ex.Message}");
                }
            }).GetAwaiter().GetResult();
            ProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        #region Persistence

        private void SaveProgress()
        {
            // DB에 저장 (Task.Run으로 데드락 방지)
            Task.Run(async () => await SaveProgressToDbAsync()).GetAwaiter().GetResult();
        }

        private async Task SaveProgressToDbAsync()
        {
            try
            {
                foreach (var kvp in _progress.Modules)
                {
                    await _userDataDb.SaveHideoutProgressAsync(kvp.Key, kvp.Value);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HideoutProgressService] Save failed: {ex.Message}");
            }
        }

        private void LoadProgress()
        {
            // Task.Run으로 데드락 방지
            // 마이그레이션은 MainWindow에서 먼저 수행됨
            Task.Run(async () =>
            {
                await LoadProgressFromDbAsync();
            }).GetAwaiter().GetResult();
        }

        private async Task LoadProgressFromDbAsync()
        {
            try
            {
                var modules = await _userDataDb.LoadHideoutProgressAsync();
                _progress = new HideoutProgress
                {
                    Modules = new Dictionary<string, int>(modules, StringComparer.OrdinalIgnoreCase),
                    LastUpdated = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HideoutProgressService] Load failed: {ex.Message}");
                _progress = new HideoutProgress();
            }
        }

        #endregion
    }

    /// <summary>
    /// Aggregated item requirement from hideout
    /// </summary>
    public class HideoutItemAggregate
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? ItemNameKo { get; set; }
        public string? ItemNameJa { get; set; }
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string? IconLink { get; set; }
        public int QuestCount { get; set; }
        public int QuestFIRCount { get; set; }
        public int HideoutCount { get; set; }
        public int HideoutFIRCount { get; set; }
        public int TotalCount { get; set; }
        public int TotalFIRCount { get; set; }
        public bool FoundInRaid { get; set; }
    }

    /// <summary>
    /// Hideout construction statistics
    /// </summary>
    public class HideoutStatistics
    {
        public int TotalModules { get; set; }
        public int NotStarted { get; set; }
        public int InProgress { get; set; }
        public int FullyCompleted { get; set; }
        public int TotalLevels { get; set; }
        public int CompletedLevels { get; set; }
    }
}
