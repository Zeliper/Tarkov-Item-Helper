using System.IO;
using System.Text.Json;
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

        private const string InventoryFileName = "item_inventory.json";

        private ItemInventoryData _inventoryData = new();
        private readonly object _lock = new();

        // Debounce save timer
        private System.Timers.Timer? _saveTimer;
        private bool _isDirty;

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
                if (_isDirty)
                {
                    SaveInventoryImmediate();
                    _isDirty = false;
                }
            };
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
                    ScheduleSave();
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
                    ScheduleSave();
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
                SaveInventoryImmediate();
                InventoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        #region Persistence

        private void ScheduleSave()
        {
            _isDirty = true;
            _inventoryData.LastUpdated = DateTime.UtcNow;
            _saveTimer?.Stop();
            _saveTimer?.Start();
        }

        private void SaveInventoryImmediate()
        {
            try
            {
                var filePath = Path.Combine(AppEnv.ConfigPath, InventoryFileName);
                Directory.CreateDirectory(AppEnv.ConfigPath);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                string json;
                lock (_lock)
                {
                    json = JsonSerializer.Serialize(_inventoryData, options);
                }

                File.WriteAllText(filePath, json);
            }
            catch
            {
                // Ignore save failures
            }
        }

        private void LoadInventory()
        {
            try
            {
                var filePath = Path.Combine(AppEnv.ConfigPath, InventoryFileName);

                if (!File.Exists(filePath))
                    return;

                var json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var data = JsonSerializer.Deserialize<ItemInventoryData>(json, options);

                if (data != null)
                {
                    _inventoryData = data;
                    // Rebuild dictionary with case-insensitive comparer
                    var newDict = new Dictionary<string, ItemInventory>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in _inventoryData.Items)
                    {
                        newDict[kvp.Key] = kvp.Value;
                    }
                    _inventoryData.Items = newDict;
                }
            }
            catch
            {
                // Use empty inventory on load failure
                _inventoryData = new ItemInventoryData();
            }
        }

        #endregion
    }
}
