using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for aggregating and querying item requirements across quests
    /// </summary>
    public class ItemRequirementService
    {
        private static ItemRequirementService? _instance;
        public static ItemRequirementService Instance => _instance ??= new ItemRequirementService();

        private List<TarkovTask>? _tasks;
        private List<TarkovItem>? _items;
        private Dictionary<string, TarkovItem>? _itemLookup;
        private Dictionary<string, TarkovTask>? _taskLookup;

        /// <summary>
        /// Initialize the service with task and item data from DB
        /// </summary>
        public async Task InitializeAsync()
        {
            var questDbService = QuestDbService.Instance;
            if (!questDbService.IsLoaded)
            {
                await questDbService.LoadQuestsAsync();
            }
            _tasks = questDbService.AllQuests.ToList();

            // Load items from DB
            var itemDbService = ItemDbService.Instance;
            if (!itemDbService.IsLoaded)
            {
                await itemDbService.LoadItemsAsync();
            }
            _items = itemDbService.AllItems.ToList();
            BuildLookups();
        }

        /// <summary>
        /// Initialize the service with provided data
        /// </summary>
        public void Initialize(List<TarkovTask> tasks, List<TarkovItem>? items = null)
        {
            _tasks = tasks;
            _items = items;
            BuildLookups();
        }

        private void BuildLookups()
        {
            if (_items != null)
            {
                _itemLookup = new Dictionary<string, TarkovItem>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in _items)
                {
                    if (!string.IsNullOrEmpty(item.NormalizedName))
                    {
                        _itemLookup[item.NormalizedName] = item;
                    }
                }

                // Add dogtag aliases for wiki-parsed names -> API names
                // Wiki parses "BEAR Dogtag" -> "bear-dogtag", API has "dogtag-bear"
                if (_itemLookup.TryGetValue("dogtag-bear", out var bearDogtag))
                {
                    _itemLookup["bear-dogtag"] = bearDogtag;
                }
                if (_itemLookup.TryGetValue("dogtag-usec", out var usecDogtag))
                {
                    _itemLookup["usec-dogtag"] = usecDogtag;
                }
            }

            if (_tasks != null)
            {
                _taskLookup = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
                foreach (var task in _tasks)
                {
                    if (!string.IsNullOrEmpty(task.NormalizedName))
                    {
                        _taskLookup[task.NormalizedName] = task;
                    }
                }
            }
        }

        /// <summary>
        /// Get all required items across all quests
        /// </summary>
        /// <returns>Aggregated item requirements with total counts</returns>
        public List<AggregatedItemRequirement> GetAllRequiredItems()
        {
            EnsureInitialized();

            var itemTotals = new Dictionary<string, AggregatedItemRequirement>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in _tasks!)
            {
                if (task.RequiredItems == null) continue;

                foreach (var questItem in task.RequiredItems)
                {
                    var normalizedName = questItem.ItemNormalizedName;

                    if (!itemTotals.TryGetValue(normalizedName, out var aggregated))
                    {
                        aggregated = new AggregatedItemRequirement
                        {
                            ItemNormalizedName = normalizedName,
                            Item = _itemLookup?.TryGetValue(normalizedName, out var item) == true ? item : null,
                            Quests = new List<QuestItemReference>()
                        };
                        itemTotals[normalizedName] = aggregated;
                    }

                    aggregated.TotalAmount += questItem.Amount;
                    if (questItem.FoundInRaid)
                    {
                        aggregated.TotalFIRAmount += questItem.Amount;
                    }

                    aggregated.Quests.Add(new QuestItemReference
                    {
                        QuestNormalizedName = task.NormalizedName ?? "",
                        QuestName = task.Name,
                        Amount = questItem.Amount,
                        Requirement = questItem.Requirement,
                        FoundInRaid = questItem.FoundInRaid,
                        DogtagMinLevel = questItem.DogtagMinLevel
                    });
                }
            }

            return itemTotals.Values
                .OrderByDescending(i => i.TotalAmount)
                .ToList();
        }

        /// <summary>
        /// Get required items for a specific quest
        /// </summary>
        public List<ItemRequirementDetail> GetRequiredItems(string questNormalizedName)
        {
            EnsureInitialized();

            if (!_taskLookup!.TryGetValue(questNormalizedName, out var task))
                return new List<ItemRequirementDetail>();

            if (task.RequiredItems == null)
                return new List<ItemRequirementDetail>();

            return task.RequiredItems.Select(qi => new ItemRequirementDetail
            {
                ItemNormalizedName = qi.ItemNormalizedName,
                Item = _itemLookup?.TryGetValue(qi.ItemNormalizedName, out var item) == true ? item : null,
                Amount = qi.Amount,
                Requirement = qi.Requirement,
                FoundInRaid = qi.FoundInRaid,
                DogtagMinLevel = qi.DogtagMinLevel
            }).ToList();
        }

        /// <summary>
        /// Get all quests that require a specific item
        /// </summary>
        public List<QuestItemReference> GetQuestsRequiringItem(string itemNormalizedName)
        {
            EnsureInitialized();

            var result = new List<QuestItemReference>();

            foreach (var task in _tasks!)
            {
                if (task.RequiredItems == null) continue;

                var matchingItems = task.RequiredItems
                    .Where(qi => qi.ItemNormalizedName.Equals(itemNormalizedName, StringComparison.OrdinalIgnoreCase));

                foreach (var qi in matchingItems)
                {
                    result.Add(new QuestItemReference
                    {
                        QuestNormalizedName = task.NormalizedName ?? "",
                        QuestName = task.Name,
                        Amount = qi.Amount,
                        Requirement = qi.Requirement,
                        FoundInRaid = qi.FoundInRaid,
                        DogtagMinLevel = qi.DogtagMinLevel
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// Get all Found-in-Raid items required
        /// </summary>
        public List<AggregatedItemRequirement> GetFIRItems()
        {
            return GetAllRequiredItems()
                .Where(i => i.TotalFIRAmount > 0)
                .Select(i => new AggregatedItemRequirement
                {
                    ItemNormalizedName = i.ItemNormalizedName,
                    Item = i.Item,
                    TotalAmount = i.TotalFIRAmount,
                    TotalFIRAmount = i.TotalFIRAmount,
                    Quests = i.Quests.Where(q => q.FoundInRaid).ToList()
                })
                .OrderByDescending(i => i.TotalAmount)
                .ToList();
        }

        /// <summary>
        /// Get items required for handover (submission to trader)
        /// </summary>
        public List<AggregatedItemRequirement> GetHandoverItems()
        {
            return GetAllRequiredItems()
                .Select(i => new AggregatedItemRequirement
                {
                    ItemNormalizedName = i.ItemNormalizedName,
                    Item = i.Item,
                    TotalAmount = i.Quests.Where(q => q.Requirement == "Handover").Sum(q => q.Amount),
                    TotalFIRAmount = i.Quests.Where(q => q.Requirement == "Handover" && q.FoundInRaid).Sum(q => q.Amount),
                    Quests = i.Quests.Where(q => q.Requirement == "Handover").ToList()
                })
                .Where(i => i.TotalAmount > 0)
                .OrderByDescending(i => i.TotalAmount)
                .ToList();
        }

        /// <summary>
        /// Get items for a quest path (target quest + all prerequisites)
        /// </summary>
        public List<AggregatedItemRequirement> GetItemsForQuestPath(string targetQuestNormalizedName)
        {
            EnsureInitialized();

            var graphService = QuestGraphService.Instance;
            if (graphService.GetTask(targetQuestNormalizedName) == null)
            {
                // Initialize graph service if needed
                graphService.Initialize(_tasks!);
            }

            var path = graphService.GetOptimalPath(targetQuestNormalizedName);
            var pathNormalizedNames = new HashSet<string>(
                path.Select(t => t.NormalizedName ?? ""),
                StringComparer.OrdinalIgnoreCase
            );

            var itemTotals = new Dictionary<string, AggregatedItemRequirement>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in path)
            {
                if (task.RequiredItems == null) continue;

                foreach (var questItem in task.RequiredItems)
                {
                    var normalizedName = questItem.ItemNormalizedName;

                    if (!itemTotals.TryGetValue(normalizedName, out var aggregated))
                    {
                        aggregated = new AggregatedItemRequirement
                        {
                            ItemNormalizedName = normalizedName,
                            Item = _itemLookup?.TryGetValue(normalizedName, out var item) == true ? item : null,
                            Quests = new List<QuestItemReference>()
                        };
                        itemTotals[normalizedName] = aggregated;
                    }

                    aggregated.TotalAmount += questItem.Amount;
                    if (questItem.FoundInRaid)
                    {
                        aggregated.TotalFIRAmount += questItem.Amount;
                    }

                    aggregated.Quests.Add(new QuestItemReference
                    {
                        QuestNormalizedName = task.NormalizedName ?? "",
                        QuestName = task.Name,
                        Amount = questItem.Amount,
                        Requirement = questItem.Requirement,
                        FoundInRaid = questItem.FoundInRaid,
                        DogtagMinLevel = questItem.DogtagMinLevel
                    });
                }
            }

            return itemTotals.Values
                .OrderByDescending(i => i.TotalAmount)
                .ToList();
        }

        /// <summary>
        /// Get items required for all Kappa quests
        /// </summary>
        public List<AggregatedItemRequirement> GetKappaItems()
        {
            EnsureInitialized();

            var graphService = QuestGraphService.Instance;
            graphService.Initialize(_tasks!);

            var kappaPath = graphService.GetKappaPath();
            var kappaQuestNames = new HashSet<string>(
                kappaPath.Select(t => t.NormalizedName ?? ""),
                StringComparer.OrdinalIgnoreCase
            );

            var itemTotals = new Dictionary<string, AggregatedItemRequirement>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in kappaPath)
            {
                if (task.RequiredItems == null) continue;

                foreach (var questItem in task.RequiredItems)
                {
                    var normalizedName = questItem.ItemNormalizedName;

                    if (!itemTotals.TryGetValue(normalizedName, out var aggregated))
                    {
                        aggregated = new AggregatedItemRequirement
                        {
                            ItemNormalizedName = normalizedName,
                            Item = _itemLookup?.TryGetValue(normalizedName, out var item) == true ? item : null,
                            Quests = new List<QuestItemReference>()
                        };
                        itemTotals[normalizedName] = aggregated;
                    }

                    aggregated.TotalAmount += questItem.Amount;
                    if (questItem.FoundInRaid)
                    {
                        aggregated.TotalFIRAmount += questItem.Amount;
                    }

                    aggregated.Quests.Add(new QuestItemReference
                    {
                        QuestNormalizedName = task.NormalizedName ?? "",
                        QuestName = task.Name,
                        Amount = questItem.Amount,
                        Requirement = questItem.Requirement,
                        FoundInRaid = questItem.FoundInRaid,
                        DogtagMinLevel = questItem.DogtagMinLevel
                    });
                }
            }

            return itemTotals.Values
                .OrderByDescending(i => i.TotalAmount)
                .ToList();
        }

        /// <summary>
        /// Get item requirement statistics
        /// </summary>
        public ItemRequirementStats GetStats()
        {
            EnsureInitialized();

            var allItems = GetAllRequiredItems();

            return new ItemRequirementStats
            {
                TotalUniqueItems = allItems.Count,
                TotalItemCount = allItems.Sum(i => i.TotalAmount),
                TotalFIRItemCount = allItems.Sum(i => i.TotalFIRAmount),
                QuestsWithItemRequirements = _tasks!.Count(t => t.RequiredItems != null && t.RequiredItems.Count > 0),
                TopRequiredItems = allItems.Take(10).ToList()
            };
        }

        /// <summary>
        /// Search for items by name (partial match)
        /// </summary>
        public List<TarkovItem> SearchItems(string query)
        {
            if (_items == null) return new List<TarkovItem>();

            var lowerQuery = query.ToLowerInvariant();

            return _items
                .Where(i =>
                    i.Name.ToLowerInvariant().Contains(lowerQuery) ||
                    i.NormalizedName.ToLowerInvariant().Contains(lowerQuery) ||
                    (i.ShortName?.ToLowerInvariant().Contains(lowerQuery) == true) ||
                    (i.NameKo?.Contains(query) == true) ||
                    (i.NameJa?.Contains(query) == true))
                .Take(20)
                .ToList();
        }

        private void EnsureInitialized()
        {
            if (_tasks == null)
            {
                throw new InvalidOperationException("ItemRequirementService not initialized. Call InitializeAsync() first.");
            }
        }
    }

    /// <summary>
    /// Aggregated item requirement across multiple quests
    /// </summary>
    public class AggregatedItemRequirement
    {
        public string ItemNormalizedName { get; set; } = string.Empty;
        public TarkovItem? Item { get; set; }
        public int TotalAmount { get; set; }
        public int TotalFIRAmount { get; set; }
        public List<QuestItemReference> Quests { get; set; } = new();

        /// <summary>
        /// Get display name (from Item if available, otherwise normalized name)
        /// </summary>
        public string DisplayName => Item?.Name ?? ItemNormalizedName;
    }

    /// <summary>
    /// Reference to a quest that requires an item
    /// </summary>
    public class QuestItemReference
    {
        public string QuestNormalizedName { get; set; } = string.Empty;
        public string QuestName { get; set; } = string.Empty;
        public int Amount { get; set; }
        public string Requirement { get; set; } = string.Empty;
        public bool FoundInRaid { get; set; }
        /// <summary>
        /// Minimum dogtag level required (for dogtag items only)
        /// </summary>
        public int? DogtagMinLevel { get; set; }
    }

    /// <summary>
    /// Detailed item requirement for a single quest
    /// </summary>
    public class ItemRequirementDetail
    {
        public string ItemNormalizedName { get; set; } = string.Empty;
        public TarkovItem? Item { get; set; }
        public int Amount { get; set; }
        public string Requirement { get; set; } = string.Empty;
        public bool FoundInRaid { get; set; }
        /// <summary>
        /// Minimum dogtag level required (for dogtag items only)
        /// </summary>
        public int? DogtagMinLevel { get; set; }

        /// <summary>
        /// Get display name (from Item if available, otherwise normalized name)
        /// </summary>
        public string DisplayName => Item?.Name ?? ItemNormalizedName;
    }

    /// <summary>
    /// Item requirement statistics
    /// </summary>
    public class ItemRequirementStats
    {
        public int TotalUniqueItems { get; set; }
        public int TotalItemCount { get; set; }
        public int TotalFIRItemCount { get; set; }
        public int QuestsWithItemRequirements { get; set; }
        public List<AggregatedItemRequirement> TopRequiredItems { get; set; } = new();
    }
}
