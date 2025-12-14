using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for managing user's item inventory quantities (FIR/Non-FIR)
    /// </summary>
    public class ItemInventoryService
    {
        private static ItemInventoryService? _instance;
        public static ItemInventoryService Instance => _instance ??= new ItemInventoryService();

        private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

        private ItemInventoryData _inventoryData = new();
        private readonly object _lock = new();

        // Debounce save timer
        private System.Timers.Timer? _saveTimer;
        private readonly HashSet<string> _pendingSaves = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler? InventoryChanged;

        private ItemInventoryService()
        {
            LoadInventory();
            InitializeSaveTimer();
        }

        private void InitializeSaveTimer()
        {
            _saveTimer = new System.Timers.Timer(500); // 500ms debounce
            _saveTimer.AutoReset = false;
            _saveTimer.Elapsed += (s, e) =>
            {
                SavePendingItems();
            };
        }

        private void SavePendingItems()
        {
            List<string> itemsToSave;
            lock (_lock)
            {
                if (_pendingSaves.Count == 0) return;
                itemsToSave = _pendingSaves.ToList();
                _pendingSaves.Clear();
            }

            Task.Run(async () =>
            {
                foreach (var itemName in itemsToSave)
                {
                    try
                    {
                        int firQty, nonFirQty;
                        lock (_lock)
                        {
                            if (_inventoryData.Items.TryGetValue(itemName, out var inv))
                            {
                                firQty = inv.FirQuantity;
                                nonFirQty = inv.NonFirQuantity;
                            }
                            else
                            {
                                firQty = 0;
                                nonFirQty = 0;
                            }
                        }
                        await _userDataDb.SaveItemInventoryAsync(itemName, firQty, nonFirQty);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ItemInventoryService] Save failed for {itemName}: {ex.Message}");
                    }
                }
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get inventory for a specific item
        /// </summary>
        public ItemInventory GetInventory(string itemNormalizedName)
        {
            lock (_lock)
            {
                if (_inventoryData.Items.TryGetValue(itemNormalizedName, out var inventory))
                {
                    return inventory;
                }

                return new ItemInventory { ItemNormalizedName = itemNormalizedName };
            }
        }

        /// <summary>
        /// Get FIR quantity for an item
        /// </summary>
        public int GetFirQuantity(string itemNormalizedName)
        {
            return GetInventory(itemNormalizedName).FirQuantity;
        }

        /// <summary>
        /// Get Non-FIR quantity for an item
        /// </summary>
        public int GetNonFirQuantity(string itemNormalizedName)
        {
            return GetInventory(itemNormalizedName).NonFirQuantity;
        }

        /// <summary>
        /// Get total quantity for an item
        /// </summary>
        public int GetTotalQuantity(string itemNormalizedName)
        {
            return GetInventory(itemNormalizedName).TotalQuantity;
        }

        /// <summary>
        /// Set FIR quantity for an item
        /// </summary>
        public void SetFirQuantity(string itemNormalizedName, int quantity)
        {
            quantity = Math.Max(0, quantity);

            lock (_lock)
            {
                if (!_inventoryData.Items.TryGetValue(itemNormalizedName, out var inventory))
                {
                    inventory = new ItemInventory { ItemNormalizedName = itemNormalizedName };
                    _inventoryData.Items[itemNormalizedName] = inventory;
                }

                if (inventory.FirQuantity != quantity)
                {
                    inventory.FirQuantity = quantity;
                    CleanupEmptyInventory(itemNormalizedName);
                    ScheduleSave(itemNormalizedName);
                    InventoryChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Set Non-FIR quantity for an item
        /// </summary>
        public void SetNonFirQuantity(string itemNormalizedName, int quantity)
        {
            quantity = Math.Max(0, quantity);

            lock (_lock)
            {
                if (!_inventoryData.Items.TryGetValue(itemNormalizedName, out var inventory))
                {
                    inventory = new ItemInventory { ItemNormalizedName = itemNormalizedName };
                    _inventoryData.Items[itemNormalizedName] = inventory;
                }

                if (inventory.NonFirQuantity != quantity)
                {
                    inventory.NonFirQuantity = quantity;
                    CleanupEmptyInventory(itemNormalizedName);
                    ScheduleSave(itemNormalizedName);
                    InventoryChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Adjust FIR quantity by delta (can be positive or negative)
        /// </summary>
        public void AdjustFirQuantity(string itemNormalizedName, int delta)
        {
            var current = GetFirQuantity(itemNormalizedName);
            SetFirQuantity(itemNormalizedName, current + delta);
        }

        /// <summary>
        /// Adjust Non-FIR quantity by delta (can be positive or negative)
        /// </summary>
        public void AdjustNonFirQuantity(string itemNormalizedName, int delta)
        {
            var current = GetNonFirQuantity(itemNormalizedName);
            SetNonFirQuantity(itemNormalizedName, current + delta);
        }

        /// <summary>
        /// Remove inventory entry if both quantities are 0
        /// </summary>
        private void CleanupEmptyInventory(string itemNormalizedName)
        {
            if (_inventoryData.Items.TryGetValue(itemNormalizedName, out var inventory))
            {
                if (inventory.FirQuantity == 0 && inventory.NonFirQuantity == 0)
                {
                    _inventoryData.Items.Remove(itemNormalizedName);
                }
            }
        }

        /// <summary>
        /// Calculate fulfillment info for an item
        /// </summary>
        public ItemFulfillmentInfo GetFulfillmentInfo(string itemNormalizedName, int requiredTotal, int requiredFir)
        {
            var inventory = GetInventory(itemNormalizedName);

            return new ItemFulfillmentInfo
            {
                ItemNormalizedName = itemNormalizedName,
                RequiredTotal = requiredTotal,
                RequiredFir = requiredFir,
                OwnedFir = inventory.FirQuantity,
                OwnedNonFir = inventory.NonFirQuantity
            };
        }

        /// <summary>
        /// Get all items in inventory
        /// </summary>
        public IReadOnlyDictionary<string, ItemInventory> GetAllInventory()
        {
            lock (_lock)
            {
                return new Dictionary<string, ItemInventory>(_inventoryData.Items, StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Get inventory statistics
        /// </summary>
        public (int TotalItems, int TotalFirCount, int TotalNonFirCount) GetStatistics()
        {
            lock (_lock)
            {
                var totalFir = _inventoryData.Items.Values.Sum(i => i.FirQuantity);
                var totalNonFir = _inventoryData.Items.Values.Sum(i => i.NonFirQuantity);
                return (_inventoryData.Items.Count, totalFir, totalNonFir);
            }
        }

        /// <summary>
        /// Reset all inventory data
        /// </summary>
        public void ResetAllInventory()
        {
            lock (_lock)
            {
                _inventoryData = new ItemInventoryData();
            }

            Task.Run(async () =>
            {
                try
                {
                    await _userDataDb.ClearAllItemInventoryAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ItemInventoryService] Reset failed: {ex.Message}");
                }
            }).GetAwaiter().GetResult();

            InventoryChanged?.Invoke(this, EventArgs.Empty);
        }

        #region Persistence

        private void ScheduleSave(string itemNormalizedName)
        {
            lock (_lock)
            {
                _pendingSaves.Add(itemNormalizedName);
            }
            _inventoryData.LastUpdated = DateTime.UtcNow;
            _saveTimer?.Stop();
            _saveTimer?.Start();
        }

        private void LoadInventory()
        {
            // Task.Run으로 데드락 방지
            // 마이그레이션은 MainWindow에서 먼저 수행됨
            Task.Run(async () =>
            {
                await LoadInventoryFromDbAsync();
            }).GetAwaiter().GetResult();
        }

        private async Task LoadInventoryFromDbAsync()
        {
            try
            {
                var items = await _userDataDb.LoadItemInventoryAsync();
                var newData = new ItemInventoryData
                {
                    LastUpdated = DateTime.UtcNow,
                    Items = new Dictionary<string, ItemInventory>(StringComparer.OrdinalIgnoreCase)
                };

                foreach (var kvp in items)
                {
                    newData.Items[kvp.Key] = new ItemInventory
                    {
                        ItemNormalizedName = kvp.Key,
                        FirQuantity = kvp.Value.FirQuantity,
                        NonFirQuantity = kvp.Value.NonFirQuantity
                    };
                }

                lock (_lock)
                {
                    _inventoryData = newData;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ItemInventoryService] Load failed: {ex.Message}");
                _inventoryData = new ItemInventoryData();
            }
        }

        #endregion
    }
}
