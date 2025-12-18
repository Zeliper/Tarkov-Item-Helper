using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Pages
{
    /// <summary>
    /// Aggregated item view model for display with inventory tracking
    /// </summary>
    public class AggregatedItemViewModel : INotifyPropertyChanged
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SubtitleName { get; set; } = string.Empty;
        public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
        public int QuestCount { get; set; }
        public int QuestFIRCount { get; set; }
        public int HideoutCount { get; set; }
        public int HideoutFIRCount { get; set; }
        public int TotalCount { get; set; }
        public int TotalFIRCount { get; set; }
        public bool FoundInRaid { get; set; }
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;

        private BitmapImage? _iconSource;
        public BitmapImage? IconSource
        {
            get => _iconSource;
            set
            {
                if (_iconSource != value)
                {
                    _iconSource = value;
                    OnPropertyChanged(nameof(IconSource));
                }
            }
        }
        public string? IconLink { get; set; }
        public string? WikiLink { get; set; }

        // Inventory quantities (user's owned items)
        private int _ownedFirQuantity;
        private int _ownedNonFirQuantity;

        public int OwnedFirQuantity
        {
            get => _ownedFirQuantity;
            set
            {
                if (_ownedFirQuantity != value)
                {
                    _ownedFirQuantity = value;
                    OnPropertyChanged(nameof(OwnedFirQuantity));
                    OnPropertyChanged(nameof(OwnedTotalQuantity));
                    OnPropertyChanged(nameof(FulfillmentStatus));
                    OnPropertyChanged(nameof(ProgressPercent));
                    OnPropertyChanged(nameof(IsFulfilled));
                    OnPropertyChanged(nameof(FulfilledVisibility));
                    OnPropertyChanged(nameof(ItemOpacity));
                    OnPropertyChanged(nameof(NameTextDecorations));
                    OnPropertyChanged(nameof(OwnedDisplay));
                }
            }
        }

        public int OwnedNonFirQuantity
        {
            get => _ownedNonFirQuantity;
            set
            {
                if (_ownedNonFirQuantity != value)
                {
                    _ownedNonFirQuantity = value;
                    OnPropertyChanged(nameof(OwnedNonFirQuantity));
                    OnPropertyChanged(nameof(OwnedTotalQuantity));
                    OnPropertyChanged(nameof(FulfillmentStatus));
                    OnPropertyChanged(nameof(ProgressPercent));
                    OnPropertyChanged(nameof(IsFulfilled));
                    OnPropertyChanged(nameof(FulfilledVisibility));
                    OnPropertyChanged(nameof(ItemOpacity));
                    OnPropertyChanged(nameof(NameTextDecorations));
                    OnPropertyChanged(nameof(OwnedDisplay));
                }
            }
        }

        public int OwnedTotalQuantity => OwnedFirQuantity + OwnedNonFirQuantity;

        // Fulfillment calculation
        public ItemFulfillmentStatus FulfillmentStatus
        {
            get
            {
                if (TotalFIRCount > 0)
                {
                    // FIR is required
                    if (OwnedFirQuantity >= TotalFIRCount)
                        return ItemFulfillmentStatus.Fulfilled;
                    if (OwnedTotalQuantity > 0)
                        return ItemFulfillmentStatus.PartiallyFulfilled;
                    return ItemFulfillmentStatus.NotStarted;
                }
                else
                {
                    // Non-FIR OK
                    if (OwnedTotalQuantity >= TotalCount)
                        return ItemFulfillmentStatus.Fulfilled;
                    if (OwnedTotalQuantity > 0)
                        return ItemFulfillmentStatus.PartiallyFulfilled;
                    return ItemFulfillmentStatus.NotStarted;
                }
            }
        }

        public double ProgressPercent
        {
            get
            {
                if (TotalCount == 0) return 100;

                if (TotalFIRCount > 0)
                {
                    return Math.Min(100, (double)OwnedFirQuantity / TotalFIRCount * 100);
                }
                else
                {
                    return Math.Min(100, (double)OwnedTotalQuantity / TotalCount * 100);
                }
            }
        }

        public bool IsFulfilled => FulfillmentStatus == ItemFulfillmentStatus.Fulfilled;
        public Visibility FulfilledVisibility => IsFulfilled ? Visibility.Visible : Visibility.Collapsed;
        public double ItemOpacity => IsFulfilled ? 0.5 : 1.0;
        public TextDecorationCollection? NameTextDecorations => IsFulfilled ? TextDecorations.Strikethrough : null;

        // Owned display string
        public string OwnedDisplay
        {
            get
            {
                if (OwnedTotalQuantity == 0)
                    return "0";
                if (OwnedNonFirQuantity == 0)
                    return $"{OwnedFirQuantity}F";
                if (OwnedFirQuantity == 0)
                    return OwnedNonFirQuantity.ToString();
                return $"{OwnedFirQuantity}F+{OwnedNonFirQuantity}";
            }
        }

        // Display strings for UI - shows FIR/non-FIR breakdown
        public string QuestDisplay => QuestCount > 0 ? FormatCountDisplay(QuestCount, QuestFIRCount) : "0";
        public string HideoutDisplay => HideoutCount > 0 ? FormatCountDisplay(HideoutCount, HideoutFIRCount) : "0";
        public string TotalDisplay => FormatCountDisplay(TotalCount, TotalFIRCount);

        private static string FormatCountDisplay(int total, int firCount)
        {
            if (firCount == 0)
                return total.ToString();
            if (firCount == total)
                return $"{total} (FIR)";
            // Mixed: show both FIR and non-FIR counts
            var nonFirCount = total - firCount;
            return $"{firCount}F+{nonFirCount}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Quest item source - shows which quest requires this item
    /// </summary>
    public class QuestItemSourceViewModel
    {
        public string QuestName { get; set; } = string.Empty;
        public string TraderName { get; set; } = string.Empty;
        public int Amount { get; set; }
        public bool FoundInRaid { get; set; }
        public string? WikiLink { get; set; }
        public TarkovTask? Task { get; set; }
        public string AmountDisplay => $"x{Amount}";
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;
        public Visibility WikiButtonVisibility => Task != null ? Visibility.Visible : Visibility.Collapsed;

        // Navigation identifier
        public string QuestNormalizedName { get; set; } = string.Empty;

        // Dogtag level requirement
        public int? DogtagMinLevel { get; set; }
        public bool HasDogtagLevel => DogtagMinLevel.HasValue;
        public Visibility DogtagLevelVisibility => HasDogtagLevel ? Visibility.Visible : Visibility.Collapsed;
        public string DogtagLevelDisplay => DogtagMinLevel.HasValue ? $"(Lv.{DogtagMinLevel}+)" : "";
    }

    /// <summary>
    /// Hideout item source - shows which hideout module requires this item
    /// </summary>
    public class HideoutItemSourceViewModel
    {
        public string ModuleName { get; set; } = string.Empty;
        public int Level { get; set; }
        public int Amount { get; set; }
        public bool FoundInRaid { get; set; }
        public string LevelDisplay => $"Level {Level}";
        public string AmountDisplay => $"x{Amount}";
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;

        // Navigation identifier
        public string StationId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Quest item aggregate for internal processing
    /// </summary>
    internal class QuestItemAggregate
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? ItemNameKo { get; set; }
        public string? ItemNameJa { get; set; }
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string? IconLink { get; set; }
        public string? WikiLink { get; set; }
        public int QuestCount { get; set; }
        public int QuestFIRCount { get; set; }
        public bool FoundInRaid { get; set; }
    }

    public partial class ItemsPage : UserControl
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;
        private readonly QuestProgressService _questProgressService = QuestProgressService.Instance;
        private readonly HideoutProgressService _hideoutProgressService = HideoutProgressService.Instance;
        private readonly ItemInventoryService _inventoryService = ItemInventoryService.Instance;
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private List<AggregatedItemViewModel> _allItemViewModels = new();
        private Dictionary<string, TarkovItem>? _itemLookup;
        private bool _isInitializing = true;
        private bool _isDataLoaded = false;
        private bool _isUnloaded = false;
        private bool _needsRefreshOnLoad = false; // Flag to indicate data refresh needed after unload
        private string? _pendingItemSelection = null;

        // Currency items should count by reference count, not total amount
        private static readonly HashSet<string> CurrencyItems = new(StringComparer.OrdinalIgnoreCase)
        {
            "roubles", "dollars", "euros"
        };

        private static bool IsCurrency(string normalizedName) => CurrencyItems.Contains(normalizedName);

        public ItemsPage()
        {
            InitializeComponent();
            _loc.LanguageChanged += OnLanguageChanged;
            _questProgressService.ProgressChanged += OnProgressChanged;
            _hideoutProgressService.ProgressChanged += OnProgressChanged;
            _inventoryService.InventoryChanged += OnInventoryChanged;
            SettingsService.Instance.PlayerFactionChanged += OnFactionChanged;
            SettingsService.Instance.HasEodEditionChanged += OnEditionChanged;
            SettingsService.Instance.HasUnheardEditionChanged += OnEditionChanged;
            SettingsService.Instance.PrestigeLevelChanged += OnPrestigeLevelChanged;
            SettingsService.Instance.DspDecodeCountChanged += OnDspDecodeCountChanged;
            QuestDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
            HideoutDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
            ItemDbService.Instance.DataRefreshed += OnDatabaseRefreshed;

            Loaded += ItemsPage_Loaded;
            Unloaded += ItemsPage_Unloaded;
        }

        private void ItemsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = true;
            _needsRefreshOnLoad = true; // Mark for refresh on next load to catch changes
            // Unsubscribe from events to prevent memory leaks
            _loc.LanguageChanged -= OnLanguageChanged;
            _questProgressService.ProgressChanged -= OnProgressChanged;
            _hideoutProgressService.ProgressChanged -= OnProgressChanged;
            _inventoryService.InventoryChanged -= OnInventoryChanged;
            SettingsService.Instance.PlayerFactionChanged -= OnFactionChanged;
            SettingsService.Instance.HasEodEditionChanged -= OnEditionChanged;
            SettingsService.Instance.HasUnheardEditionChanged -= OnEditionChanged;
            SettingsService.Instance.PrestigeLevelChanged -= OnPrestigeLevelChanged;
            SettingsService.Instance.DspDecodeCountChanged -= OnDspDecodeCountChanged;
            QuestDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
            HideoutDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
            ItemDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
        }

        private void OnInventoryChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Update inventory quantities in view models
                foreach (var vm in _allItemViewModels)
                {
                    var inventory = _inventoryService.GetInventory(vm.ItemNormalizedName);
                    vm.OwnedFirQuantity = inventory.FirQuantity;
                    vm.OwnedNonFirQuantity = inventory.NonFirQuantity;
                }
                UpdateDetailPanel();
            });
        }

        private async void OnDatabaseRefreshed(object? sender, EventArgs e)
        {
            // DB 업데이트 후 데이터 다시 로드
            await Dispatcher.InvokeAsync(async () =>
            {
                // Item lookup 새로고침
                _itemLookup = new Dictionary<string, TarkovItem>(
                    ItemDbService.Instance.GetItemLookup(), StringComparer.OrdinalIgnoreCase);

                // Items 데이터 다시 로드
                await LoadItemsAsync();
                ApplyFilters();
                UpdateDetailPanel();

                // 아이콘 백그라운드 로드
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private async void ItemsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Re-subscribe events if page was previously unloaded
            if (_isUnloaded)
            {
                _isUnloaded = false;
                _loc.LanguageChanged += OnLanguageChanged;
                _questProgressService.ProgressChanged += OnProgressChanged;
                _hideoutProgressService.ProgressChanged += OnProgressChanged;
                _inventoryService.InventoryChanged += OnInventoryChanged;
                SettingsService.Instance.PlayerFactionChanged += OnFactionChanged;
                SettingsService.Instance.HasEodEditionChanged += OnEditionChanged;
                SettingsService.Instance.HasUnheardEditionChanged += OnEditionChanged;
                SettingsService.Instance.PrestigeLevelChanged += OnPrestigeLevelChanged;
                SettingsService.Instance.DspDecodeCountChanged += OnDspDecodeCountChanged;
                QuestDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
                HideoutDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
                ItemDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
            }

            // Check if data needs refresh (changes might have occurred while unloaded)
            if (_isDataLoaded && _needsRefreshOnLoad)
            {
                _needsRefreshOnLoad = false;
                await LoadItemsAsync();
                ApplyFilters();
                _ = LoadImagesInBackgroundAsync();
                return;
            }

            // Skip if already loaded (avoid re-initialization on tab switch)
            if (_isDataLoaded)
            {
                return;
            }

            // Show loading overlay
            LoadingOverlay.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;

            try
            {
                // Load items lookup from DB
                var itemDbService = ItemDbService.Instance;
                if (!itemDbService.IsLoaded)
                {
                    await itemDbService.LoadItemsAsync();
                }
                if (_isUnloaded) return; // Check if page was unloaded during async operation

                _itemLookup = new Dictionary<string, TarkovItem>(
                    itemDbService.GetItemLookup(), StringComparer.OrdinalIgnoreCase);
                System.Diagnostics.Debug.WriteLine($"[ItemsPage] Item lookup loaded from DB: {_itemLookup.Count} items");

                await LoadItemsAsync();
                if (_isUnloaded) return; // Check if page was unloaded during async operation

                _isInitializing = false;
                _isDataLoaded = true;
                ApplyFilters();

                // Process pending selection if any
                if (!string.IsNullOrEmpty(_pendingItemSelection))
                {
                    var pendingName = _pendingItemSelection;
                    _pendingItemSelection = null;
                    SelectItemInternal(pendingName);
                }
            }
            finally
            {
                // Hide loading overlay - show UI immediately
                LoadingOverlay.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
            }

            // Load images in background (after UI is visible)
            // Fire-and-forget, but capture exceptions
            _ = LoadImagesInBackgroundAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    System.Diagnostics.Debug.WriteLine($"Background image loading failed: {t.Exception?.Message}");
                }
            }, TaskScheduler.Default);
        }

        private void OnLanguageChanged(object? sender, AppLanguage e)
        {
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
                UpdateDetailPanel();
                // Load images in background
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
                // Load images in background
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private void OnFactionChanged(object? sender, string? e)
        {
            // Reload items when faction changes to update item counts
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
                UpdateDetailPanel();
                // Load images in background
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private void OnEditionChanged(object? sender, bool e)
        {
            // Edition change affects which quests are available (Unavailable status)
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
                UpdateDetailPanel();
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private void OnPrestigeLevelChanged(object? sender, int e)
        {
            // Prestige level change affects which quests are available (Unavailable status)
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
                UpdateDetailPanel();
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private void OnDspDecodeCountChanged(object? sender, int e)
        {
            // DSP decode count change affects which quests are available (Locked status)
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
                UpdateDetailPanel();
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private async Task LoadItemsAsync()
        {
            // Get hideout requirements
            var hideoutItems = _hideoutProgressService.GetAllRemainingItemRequirements();

            // Get quest requirements
            var questItems = GetQuestItemRequirements();

            // Merge both sources
            var mergedItems = new Dictionary<string, AggregatedItemViewModel>(StringComparer.OrdinalIgnoreCase);

            // Add hideout items
            foreach (var kvp in hideoutItems)
            {
                var hideoutItem = kvp.Value;
                var (displayName, subtitle, showSubtitle) = GetLocalizedNames(
                    hideoutItem.ItemName, hideoutItem.ItemNameKo, hideoutItem.ItemNameJa);

                // Get wiki link from item lookup
                string? wikiLink = null;
                if (_itemLookup != null && _itemLookup.TryGetValue(hideoutItem.ItemNormalizedName, out var itemInfo))
                {
                    wikiLink = itemInfo.WikiLink;
                }

                mergedItems[kvp.Key] = new AggregatedItemViewModel
                {
                    ItemId = hideoutItem.ItemId,
                    ItemNormalizedName = hideoutItem.ItemNormalizedName,
                    DisplayName = displayName,
                    SubtitleName = subtitle,
                    SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                    QuestCount = 0,
                    QuestFIRCount = 0,
                    HideoutCount = hideoutItem.HideoutCount,
                    HideoutFIRCount = hideoutItem.HideoutFIRCount,
                    TotalCount = hideoutItem.HideoutCount,
                    TotalFIRCount = hideoutItem.HideoutFIRCount,
                    FoundInRaid = hideoutItem.FoundInRaid,
                    IconLink = hideoutItem.IconLink,
                    WikiLink = wikiLink
                };
            }

            // Add/merge quest items
            foreach (var kvp in questItems)
            {
                var questItem = kvp.Value;
                if (mergedItems.TryGetValue(kvp.Key, out var existing))
                {
                    existing.QuestCount = questItem.QuestCount;
                    existing.QuestFIRCount = questItem.QuestFIRCount;
                    existing.TotalCount = existing.HideoutCount + questItem.QuestCount;
                    existing.TotalFIRCount = existing.HideoutFIRCount + questItem.QuestFIRCount;
                    if (questItem.FoundInRaid)
                        existing.FoundInRaid = true;
                    // Copy wiki link if not already set
                    if (string.IsNullOrEmpty(existing.WikiLink))
                        existing.WikiLink = questItem.WikiLink;
                }
                else
                {
                    var (displayName, subtitle, showSubtitle) = GetLocalizedNames(
                        questItem.ItemName, questItem.ItemNameKo, questItem.ItemNameJa);

                    mergedItems[kvp.Key] = new AggregatedItemViewModel
                    {
                        ItemId = questItem.ItemId,
                        ItemNormalizedName = questItem.ItemNormalizedName,
                        DisplayName = displayName,
                        SubtitleName = subtitle,
                        SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                        QuestCount = questItem.QuestCount,
                        QuestFIRCount = questItem.QuestFIRCount,
                        HideoutCount = 0,
                        HideoutFIRCount = 0,
                        TotalCount = questItem.QuestCount,
                        TotalFIRCount = questItem.QuestFIRCount,
                        FoundInRaid = questItem.FoundInRaid,
                        IconLink = questItem.IconLink,
                        WikiLink = questItem.WikiLink
                    };
                }
            }

            _allItemViewModels = mergedItems.Values.ToList();

            // Load inventory data (fast, synchronous)
            foreach (var vm in _allItemViewModels)
            {
                var inventory = _inventoryService.GetInventory(vm.ItemNormalizedName);
                vm.OwnedFirQuantity = inventory.FirQuantity;
                vm.OwnedNonFirQuantity = inventory.NonFirQuantity;
            }

            // Note: Image loading is now done separately via LoadImagesInBackgroundAsync()
        }

        /// <summary>
        /// Load item icons with priority: visible items first, then remaining items in background.
        /// </summary>
        private async Task LoadImagesInBackgroundAsync()
        {
            if (_allItemViewModels == null || _allItemViewModels.Count == 0)
                return;

            // Phase 1: Load visible items first (immediate UX improvement)
            await LoadVisibleItemImagesAsync();

            // Phase 2: Load remaining items in background
            await LoadRemainingItemImagesAsync();
        }

        /// <summary>
        /// Load images only for currently visible items in the ListBox.
        /// </summary>
        private Task LoadVisibleItemImagesAsync()
        {
            var visibleItems = GetVisibleItems();
            if (visibleItems.Count == 0)
                return Task.CompletedTask;

            var itemsNeedingIcons = visibleItems
                .Where(vm => !string.IsNullOrEmpty(vm.ItemId) && vm.IconSource == null)
                .ToList();

            if (itemsNeedingIcons.Count == 0)
                return Task.CompletedTask;

            LoadItemImages(itemsNeedingIcons);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Load images for all remaining items that haven't been loaded yet.
        /// </summary>
        private Task LoadRemainingItemImagesAsync()
        {
            if (_allItemViewModels == null)
                return Task.CompletedTask;

            var itemsNeedingIcons = _allItemViewModels
                .Where(vm => !string.IsNullOrEmpty(vm.ItemId) && vm.IconSource == null)
                .ToList();

            if (itemsNeedingIcons.Count == 0)
                return Task.CompletedTask;

            LoadItemImages(itemsNeedingIcons);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Load images for a specific list of items from local files.
        /// </summary>
        private void LoadItemImages(List<AggregatedItemViewModel> items)
        {
            if (items.Count == 0)
                return;

            foreach (var vm in items)
            {
                if (_isUnloaded) return;
                if (vm.IconSource != null) continue;

                var icon = _imageCache.GetLocalItemIcon(vm.ItemId);
                if (icon != null)
                {
                    vm.IconSource = icon;
                }
            }
        }

        /// <summary>
        /// Get the list of currently visible items in the ListBox.
        /// </summary>
        private List<AggregatedItemViewModel> GetVisibleItems()
        {
            var visibleItems = new List<AggregatedItemViewModel>();

            if (LstItems.ItemsSource == null)
                return visibleItems;

            // Get the ScrollViewer from ListBox
            var scrollViewer = GetScrollViewer(LstItems);
            if (scrollViewer == null)
                return visibleItems;

            var itemsSource = LstItems.ItemsSource as IList<AggregatedItemViewModel>;
            if (itemsSource == null || itemsSource.Count == 0)
                return visibleItems;

            // Estimate visible range based on scroll position
            // Assume each item is approximately 50 pixels tall
            const double estimatedItemHeight = 50;
            var viewportHeight = scrollViewer.ViewportHeight;
            var verticalOffset = scrollViewer.VerticalOffset;

            var startIndex = Math.Max(0, (int)(verticalOffset / estimatedItemHeight) - 2);
            var visibleCount = (int)(viewportHeight / estimatedItemHeight) + 5; // Add buffer
            var endIndex = Math.Min(itemsSource.Count - 1, startIndex + visibleCount);

            for (int i = startIndex; i <= endIndex; i++)
            {
                visibleItems.Add(itemsSource[i]);
            }

            return visibleItems;
        }

        /// <summary>
        /// Get ScrollViewer from a ListBox.
        /// </summary>
        private static ScrollViewer? GetScrollViewer(DependencyObject element)
        {
            if (element is ScrollViewer sv)
                return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        // Debounce timer for scroll events
        private System.Windows.Threading.DispatcherTimer? _scrollDebounceTimer;

        /// <summary>
        /// Handle scroll events to load images for newly visible items.
        /// </summary>
        private void LstItems_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Only process if there's actual scrolling (not just layout changes)
            if (e.VerticalChange == 0 && e.ViewportHeightChange == 0)
                return;

            // Debounce: wait for scrolling to settle before loading images
            _scrollDebounceTimer?.Stop();
            _scrollDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _scrollDebounceTimer.Tick += (s, args) =>
            {
                _scrollDebounceTimer?.Stop();
                LoadVisibleItemImagesAsync();
            };
            _scrollDebounceTimer.Start();
        }

        private Dictionary<string, QuestItemAggregate> GetQuestItemRequirements()
        {
            var result = new Dictionary<string, QuestItemAggregate>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in _questProgressService.AllTasks)
            {
                // Skip completed, failed, or unavailable quests (includes faction-restricted quests)
                var status = _questProgressService.GetStatus(task);
                if (status == QuestStatus.Done || status == QuestStatus.Failed || status == QuestStatus.Unavailable)
                    continue;

                if (task.RequiredItems == null)
                    continue;

                foreach (var questItem in task.RequiredItems)
                {
                    // Direct lookup by ItemId (QuestRequiredItems.ItemId -> Items.Id)
                    TarkovItem? itemInfo = null;
                    _itemLookup?.TryGetValue(questItem.ItemNormalizedName, out itemInfo);

                    // Skip if item not found in Items table
                    if (itemInfo == null)
                        continue;

                    // Skip quest-only items (they don't need to be tracked in the Items tab)
                    if (string.Equals(itemInfo.Category, "Quest Items", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var itemName = itemInfo.Name;
                    var iconLink = itemInfo.IconLink;
                    var wikiLink = itemInfo.WikiLink;

                    // For currency items, count by reference (1 per quest) instead of total amount
                    var countToAdd = IsCurrency(questItem.ItemNormalizedName) ? 1 : questItem.Amount;
                    var firCountToAdd = questItem.FoundInRaid ? countToAdd : 0;

                    if (result.TryGetValue(questItem.ItemNormalizedName, out var existing))
                    {
                        existing.QuestCount += countToAdd;
                        if (questItem.FoundInRaid)
                        {
                            existing.QuestFIRCount += countToAdd;
                            existing.FoundInRaid = true;
                        }
                    }
                    else
                    {
                        result[questItem.ItemNormalizedName] = new QuestItemAggregate
                        {
                            ItemId = itemInfo?.Id ?? questItem.ItemNormalizedName,
                            ItemName = itemName,
                            ItemNameKo = itemInfo?.NameKo,
                            ItemNameJa = itemInfo?.NameJa,
                            ItemNormalizedName = questItem.ItemNormalizedName,
                            IconLink = iconLink,
                            WikiLink = wikiLink,
                            QuestCount = countToAdd,
                            QuestFIRCount = firCountToAdd,
                            FoundInRaid = questItem.FoundInRaid
                        };
                    }
                }
            }

            return result;
        }

        private (string DisplayName, string Subtitle, bool ShowSubtitle) GetLocalizedNames(
            string name, string? nameKo, string? nameJa)
        {
            var lang = _loc.CurrentLanguage;

            if (lang == AppLanguage.EN)
            {
                return (name, string.Empty, false);
            }

            var localizedName = lang switch
            {
                AppLanguage.KO => nameKo,
                AppLanguage.JA => nameJa,
                _ => null
            };

            if (!string.IsNullOrEmpty(localizedName))
            {
                return (localizedName, name, true);
            }

            return (name, string.Empty, false);
        }

        private void ApplyFilters()
        {
            var searchText = TxtSearch.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            var sourceFilter = (CmbSource.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
            var fulfillmentFilter = (CmbFulfillment.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
            var firOnly = ChkFirOnly.IsChecked == true;
            var hideFulfilled = ChkHideFulfilled.IsChecked == true;
            var sortBy = (CmbSort.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Name";

            var filtered = _allItemViewModels.Where(vm =>
            {
                // Search filter
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!vm.DisplayName.ToLowerInvariant().Contains(searchText) &&
                        !vm.SubtitleName.ToLowerInvariant().Contains(searchText))
                        return false;
                }

                // Source filter
                if (sourceFilter == "Quest" && vm.QuestCount == 0)
                    return false;
                if (sourceFilter == "Hideout" && vm.HideoutCount == 0)
                    return false;

                // FIR filter
                if (firOnly && !vm.FoundInRaid)
                    return false;

                // Fulfillment filter
                if (fulfillmentFilter != "All")
                {
                    var status = vm.FulfillmentStatus;
                    if (fulfillmentFilter == "NotStarted" && status != ItemFulfillmentStatus.NotStarted)
                        return false;
                    if (fulfillmentFilter == "InProgress" && status != ItemFulfillmentStatus.PartiallyFulfilled)
                        return false;
                    if (fulfillmentFilter == "Fulfilled" && status != ItemFulfillmentStatus.Fulfilled)
                        return false;
                }

                // Hide fulfilled filter
                if (hideFulfilled && vm.IsFulfilled)
                    return false;

                return true;
            });

            // Apply sorting
            filtered = sortBy switch
            {
                "Total" => filtered.OrderByDescending(vm => vm.TotalCount).ThenBy(vm => vm.DisplayName),
                "Quest" => filtered.OrderByDescending(vm => vm.QuestCount).ThenBy(vm => vm.DisplayName),
                "Hideout" => filtered.OrderByDescending(vm => vm.HideoutCount).ThenBy(vm => vm.DisplayName),
                "Progress" => filtered.OrderByDescending(vm => vm.ProgressPercent).ThenBy(vm => vm.DisplayName),
                _ => filtered.OrderBy(vm => vm.DisplayName)
            };

            var filteredList = filtered.ToList();
            LstItems.ItemsSource = filteredList;

            // Update statistics
            var totalItems = filteredList.Count;
            var totalQuestCount = filteredList.Sum(i => i.QuestCount);
            var totalHideoutCount = filteredList.Sum(i => i.HideoutCount);
            var totalCount = filteredList.Sum(i => i.TotalCount);
            var fulfilledCount = filteredList.Count(i => i.IsFulfilled);
            var inProgressCount = filteredList.Count(i => i.FulfillmentStatus == ItemFulfillmentStatus.PartiallyFulfilled);

            TxtStats.Text = $"Showing {totalItems} items | " +
                           $"Quest: {totalQuestCount} | " +
                           $"Hideout: {totalHideoutCount} | " +
                           $"Fulfilled: {fulfilledCount} | " +
                           $"In Progress: {inProgressCount}";
        }

        private void CmbFulfillment_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void CmbSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        /// <summary>
        /// Select an item by its normalized name (for cross-tab navigation)
        /// </summary>
        public void SelectItem(string itemNormalizedName)
        {
            // If data is not loaded yet, save for later
            if (!_isDataLoaded)
            {
                _pendingItemSelection = itemNormalizedName;
                return;
            }

            SelectItemInternal(itemNormalizedName);
        }

        /// <summary>
        /// Internal method to select an item (called when data is ready)
        /// </summary>
        private void SelectItemInternal(string itemNormalizedName)
        {
            // Prevent SelectionChanged from interfering during navigation
            _isInitializing = true;

            try
            {
                // Reset filters to ensure the item is visible
                ResetFiltersForNavigationInternal();

                // Apply filters to update the list
                ApplyFilters();

                // Find the item view model from the filtered list (ItemsSource)
                var filteredItems = LstItems.ItemsSource as IEnumerable<AggregatedItemViewModel>;
                var itemVm = filteredItems?.FirstOrDefault(vm =>
                    string.Equals(vm.ItemNormalizedName, itemNormalizedName, StringComparison.OrdinalIgnoreCase));

                if (itemVm == null) return;

                // For virtualized lists: scroll first, then select
                LstItems.ScrollIntoView(itemVm);
                LstItems.UpdateLayout();

                // Now select the item
                LstItems.SelectedItem = itemVm;
                LstItems.UpdateLayout();

                // Scroll again to ensure visibility after selection
                LstItems.ScrollIntoView(itemVm);

                // Update state and detail panel directly
                _selectedItem = itemVm;
                _selectedItemNormalizedName = itemVm.ItemNormalizedName;
                ShowItemDetail(itemVm);

                // Focus the list to show selection highlight
                LstItems.Focus();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// Reset filters without changing _isInitializing (for internal use)
        /// </summary>
        private void ResetFiltersForNavigationInternal()
        {
            // Clear search text
            TxtSearch.Text = "";

            // Reset source filter to "All"
            CmbSource.SelectedIndex = 0;

            // Reset fulfillment filter to "All"
            CmbFulfillment.SelectedIndex = 0;

            // Uncheck filter checkboxes
            ChkFirOnly.IsChecked = false;
            ChkHideFulfilled.IsChecked = false;

            // Reset sort to "Name"
            CmbSort.SelectedIndex = 0;
        }

        /// <summary>
        /// Show detail panel for a specific item (used by navigation)
        /// </summary>
        private void ShowItemDetail(AggregatedItemViewModel itemVm)
        {
            if (itemVm == null)
            {
                TxtSelectItem.Visibility = Visibility.Visible;
                DetailPanel.Visibility = Visibility.Collapsed;
                return;
            }

            TxtSelectItem.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;

            // Populate header
            TxtDetailName.Text = itemVm.DisplayName;
            TxtDetailSubtitle.Text = itemVm.SubtitleName;
            TxtDetailSubtitle.Visibility = itemVm.SubtitleVisibility;
            ImgDetailIcon.Source = itemVm.IconSource;

            // Populate summary counts
            TxtDetailQuestCount.Text = itemVm.QuestDisplay;
            TxtDetailHideoutCount.Text = itemVm.HideoutDisplay;
            TxtDetailTotalCount.Text = itemVm.TotalDisplay;

            // Enable/disable wiki button
            BtnWiki.IsEnabled = !string.IsNullOrEmpty(itemVm.WikiLink);

            // Update inventory display
            TxtDetailOwnedFir.Text = itemVm.OwnedFirQuantity.ToString();
            TxtDetailOwnedNonFir.Text = itemVm.OwnedNonFirQuantity.ToString();

            // Update fulfillment status display
            var status = itemVm.FulfillmentStatus;
            var statusText = status switch
            {
                ItemFulfillmentStatus.Fulfilled => "Fulfilled",
                ItemFulfillmentStatus.PartiallyFulfilled => "In Progress",
                _ => "Not Started"
            };

            TxtDetailFulfillmentStatus.Text = statusText;
            TxtDetailFulfillmentStatus.Foreground = status switch
            {
                ItemFulfillmentStatus.Fulfilled => (Brush)FindResource("SuccessBrush"),
                ItemFulfillmentStatus.PartiallyFulfilled => (Brush)FindResource("WarningBrush"),
                _ => (Brush)FindResource("TextSecondaryBrush")
            };

            // Update progress bar
            DetailProgressBar.Value = itemVm.ProgressPercent;

            // Populate quest sources
            var questSources = GetQuestSources(itemVm.ItemNormalizedName);
            QuestRequirementsList.ItemsSource = questSources;
            QuestSection.Visibility = questSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Populate hideout sources
            var hideoutSources = GetHideoutSources(itemVm.ItemNormalizedName);
            HideoutRequirementsList.ItemsSource = hideoutSources;
            HideoutSection.Visibility = hideoutSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Reset filters for navigation to ensure target item is visible
        /// </summary>
        private void ResetFiltersForNavigation()
        {
            _isInitializing = true;

            // Clear search text
            TxtSearch.Text = "";

            // Reset source filter to "All"
            CmbSource.SelectedIndex = 0; // "All"

            // Reset fulfillment filter to "All"
            CmbFulfillment.SelectedIndex = 0; // "All Status"

            // Uncheck filter checkboxes
            ChkFirOnly.IsChecked = false;
            ChkHideFulfilled.IsChecked = false;

            // Reset sort to "Name"
            CmbSort.SelectedIndex = 0;

            _isInitializing = false;
        }

        private AggregatedItemViewModel? _selectedItem;
        private string? _selectedItemNormalizedName;

        private void LstItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            _selectedItem = LstItems.SelectedItem as AggregatedItemViewModel;
            _selectedItemNormalizedName = _selectedItem?.ItemNormalizedName;
            UpdateDetailPanel();
        }

        private void UpdateDetailPanel()
        {
            // If there was a previously selected item, try to find it again after language change
            if (_selectedItem == null && !string.IsNullOrEmpty(_selectedItemNormalizedName))
            {
                _selectedItem = _allItemViewModels.FirstOrDefault(vm =>
                    string.Equals(vm.ItemNormalizedName, _selectedItemNormalizedName, StringComparison.OrdinalIgnoreCase));
            }

            if (_selectedItem == null)
            {
                TxtSelectItem.Visibility = Visibility.Visible;
                DetailPanel.Visibility = Visibility.Collapsed;
                return;
            }

            TxtSelectItem.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;

            // Populate header
            TxtDetailName.Text = _selectedItem.DisplayName;
            TxtDetailSubtitle.Text = _selectedItem.SubtitleName;
            TxtDetailSubtitle.Visibility = _selectedItem.SubtitleVisibility;
            ImgDetailIcon.Source = _selectedItem.IconSource;

            // Populate summary counts
            TxtDetailQuestCount.Text = _selectedItem.QuestDisplay;
            TxtDetailHideoutCount.Text = _selectedItem.HideoutDisplay;
            TxtDetailTotalCount.Text = _selectedItem.TotalDisplay;

            // Enable/disable wiki button
            BtnWiki.IsEnabled = !string.IsNullOrEmpty(_selectedItem.WikiLink);

            // Update inventory display
            UpdateDetailInventoryDisplay();

            // Populate quest sources
            var questSources = GetQuestSources(_selectedItem.ItemNormalizedName);
            QuestRequirementsList.ItemsSource = questSources;
            QuestSection.Visibility = questSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Populate hideout sources
            var hideoutSources = GetHideoutSources(_selectedItem.ItemNormalizedName);
            HideoutRequirementsList.ItemsSource = hideoutSources;
            HideoutSection.Visibility = hideoutSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<QuestItemSourceViewModel> GetQuestSources(string itemNormalizedName)
        {
            var sources = new List<QuestItemSourceViewModel>();

            foreach (var task in _questProgressService.AllTasks)
            {
                // Skip completed, failed, or unavailable quests (includes faction-restricted quests)
                var status = _questProgressService.GetStatus(task);
                if (status == QuestStatus.Done || status == QuestStatus.Failed || status == QuestStatus.Unavailable)
                    continue;

                if (task.RequiredItems == null)
                    continue;

                foreach (var questItem in task.RequiredItems)
                {
                    if (string.Equals(questItem.ItemNormalizedName, itemNormalizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        var questName = GetLocalizedQuestName(task);
                        var traderName = GetLocalizedTraderName(task);
                        sources.Add(new QuestItemSourceViewModel
                        {
                            QuestName = questName,
                            TraderName = traderName,
                            Amount = questItem.Amount,
                            FoundInRaid = questItem.FoundInRaid,
                            Task = task,
                            QuestNormalizedName = task.NormalizedName ?? string.Empty, // For navigation
                            DogtagMinLevel = questItem.DogtagMinLevel
                        });
                    }
                }
            }

            return sources;
        }

        /// <summary>
        /// Handle click on quest name to navigate to Quests tab
        /// </summary>
        private void QuestName_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is QuestItemSourceViewModel vm)
            {
                if (string.IsNullOrEmpty(vm.QuestNormalizedName)) return;

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToQuest(vm.QuestNormalizedName);
            }
        }

        /// <summary>
        /// Handle click on hideout module name to navigate to Hideout tab
        /// </summary>
        private void HideoutModuleName_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is HideoutItemSourceViewModel vm)
            {
                if (string.IsNullOrEmpty(vm.StationId)) return;

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToHideout(vm.StationId);
            }
        }

        private List<HideoutItemSourceViewModel> GetHideoutSources(string itemNormalizedName)
        {
            var sources = new List<HideoutItemSourceViewModel>();

            foreach (var module in _hideoutProgressService.AllModules)
            {
                var currentLevel = _hideoutProgressService.GetCurrentLevel(module);

                foreach (var level in module.Levels.Where(l => l.Level > currentLevel))
                {
                    foreach (var itemReq in level.ItemRequirements)
                    {
                        if (string.Equals(itemReq.ItemNormalizedName, itemNormalizedName, StringComparison.OrdinalIgnoreCase))
                        {
                            var moduleName = GetLocalizedModuleName(module);
                            sources.Add(new HideoutItemSourceViewModel
                            {
                                ModuleName = moduleName,
                                Level = level.Level,
                                Amount = itemReq.Count,
                                FoundInRaid = itemReq.FoundInRaid,
                                StationId = module.Id
                            });
                        }
                    }
                }
            }

            return sources.OrderBy(s => s.ModuleName).ThenBy(s => s.Level).ToList();
        }

        private string GetLocalizedQuestName(TarkovTask task)
        {
            var lang = _loc.CurrentLanguage;
            return lang switch
            {
                AppLanguage.KO => task.NameKo ?? task.Name,
                AppLanguage.JA => task.NameJa ?? task.Name,
                _ => task.Name
            };
        }

        private string GetLocalizedModuleName(HideoutModule module)
        {
            var lang = _loc.CurrentLanguage;
            return lang switch
            {
                AppLanguage.KO => module.NameKo ?? module.Name,
                AppLanguage.JA => module.NameJa ?? module.Name,
                _ => module.Name
            };
        }

        private string GetLocalizedTraderName(TarkovTask task)
        {
            // TarkovTask doesn't have localized trader names, so return the English name
            return task.Trader;
        }

        private void BtnWiki_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || string.IsNullOrEmpty(_selectedItem.WikiLink))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _selectedItem.WikiLink,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore errors opening browser
            }
        }

        private void BtnQuestWiki_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QuestItemSourceViewModel vm && vm.Task != null)
            {
                var wikiPageName = NormalizedNameGenerator.GetWikiPageName(vm.Task.Name);
                var wikiUrl = $"https://escapefromtarkov.fandom.com/wiki/{Uri.EscapeDataString(wikiPageName.Replace(" ", "_"))}";

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = wikiUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignore errors opening browser
                }
            }
        }

        #region Inventory Quantity Controls

        private void BtnFirMinus5_Click(object sender, RoutedEventArgs e)
        {
            AdjustFirQuantity(sender, -5);
        }

        private void BtnFirMinus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustFirQuantity(sender, -1);
        }

        private void BtnFirPlus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustFirQuantity(sender, 1);
        }

        private void BtnFirPlus5_Click(object sender, RoutedEventArgs e)
        {
            AdjustFirQuantity(sender, 5);
        }

        private void BtnNonFirMinus5_Click(object sender, RoutedEventArgs e)
        {
            AdjustNonFirQuantity(sender, -5);
        }

        private void BtnNonFirMinus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustNonFirQuantity(sender, -1);
        }

        private void BtnNonFirPlus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustNonFirQuantity(sender, 1);
        }

        private void BtnNonFirPlus5_Click(object sender, RoutedEventArgs e)
        {
            AdjustNonFirQuantity(sender, 5);
        }

        private void AdjustFirQuantity(object sender, int delta)
        {
            if (sender is Button btn && btn.DataContext is AggregatedItemViewModel vm)
            {
                _inventoryService.AdjustFirQuantity(vm.ItemNormalizedName, delta);
                vm.OwnedFirQuantity = _inventoryService.GetFirQuantity(vm.ItemNormalizedName);
            }
        }

        private void AdjustNonFirQuantity(object sender, int delta)
        {
            if (sender is Button btn && btn.DataContext is AggregatedItemViewModel vm)
            {
                _inventoryService.AdjustNonFirQuantity(vm.ItemNormalizedName, delta);
                vm.OwnedNonFirQuantity = _inventoryService.GetNonFirQuantity(vm.ItemNormalizedName);
            }
        }

        // Detail panel inventory adjustments (uses selected item)
        private void BtnDetailFirMinus5_Click(object sender, RoutedEventArgs e)
        {
            AdjustDetailFirQuantity(-5);
        }

        private void BtnDetailFirMinus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustDetailFirQuantity(-1);
        }

        private void BtnDetailFirPlus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustDetailFirQuantity(1);
        }

        private void BtnDetailFirPlus5_Click(object sender, RoutedEventArgs e)
        {
            AdjustDetailFirQuantity(5);
        }

        private void BtnDetailNonFirMinus5_Click(object sender, RoutedEventArgs e)
        {
            AdjustDetailNonFirQuantity(-5);
        }

        private void BtnDetailNonFirMinus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustDetailNonFirQuantity(-1);
        }

        private void BtnDetailNonFirPlus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustDetailNonFirQuantity(1);
        }

        private void BtnDetailNonFirPlus5_Click(object sender, RoutedEventArgs e)
        {
            AdjustDetailNonFirQuantity(5);
        }

        private void AdjustDetailFirQuantity(int delta)
        {
            if (_selectedItem == null) return;
            _inventoryService.AdjustFirQuantity(_selectedItem.ItemNormalizedName, delta);
            _selectedItem.OwnedFirQuantity = _inventoryService.GetFirQuantity(_selectedItem.ItemNormalizedName);
            UpdateDetailInventoryDisplay();
        }

        private void AdjustDetailNonFirQuantity(int delta)
        {
            if (_selectedItem == null) return;
            _inventoryService.AdjustNonFirQuantity(_selectedItem.ItemNormalizedName, delta);
            _selectedItem.OwnedNonFirQuantity = _inventoryService.GetNonFirQuantity(_selectedItem.ItemNormalizedName);
            UpdateDetailInventoryDisplay();
        }

        private void UpdateDetailInventoryDisplay()
        {
            if (_selectedItem == null) return;

            TxtDetailOwnedFir.Text = _selectedItem.OwnedFirQuantity.ToString();
            TxtDetailOwnedNonFir.Text = _selectedItem.OwnedNonFirQuantity.ToString();

            // Update fulfillment status display
            var status = _selectedItem.FulfillmentStatus;
            var statusText = status switch
            {
                ItemFulfillmentStatus.Fulfilled => "Fulfilled",
                ItemFulfillmentStatus.PartiallyFulfilled => "In Progress",
                _ => "Not Started"
            };

            TxtDetailFulfillmentStatus.Text = statusText;
            TxtDetailFulfillmentStatus.Foreground = status switch
            {
                ItemFulfillmentStatus.Fulfilled => (Brush)FindResource("SuccessBrush"),
                ItemFulfillmentStatus.PartiallyFulfilled => (Brush)FindResource("WarningBrush"),
                _ => (Brush)FindResource("TextSecondaryBrush")
            };

            // Update progress bar
            DetailProgressBar.Value = _selectedItem.ProgressPercent;
        }

        /// <summary>
        /// Only allow numeric input for quantity fields
        /// </summary>
        private void TxtDetailOwned_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        /// <summary>
        /// Apply FIR quantity when losing focus
        /// </summary>
        private void TxtDetailOwnedFir_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyFirQuantityFromTextBox();
        }

        /// <summary>
        /// Apply FIR quantity when pressing Enter
        /// </summary>
        private void TxtDetailOwnedFir_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyFirQuantityFromTextBox();
                Keyboard.ClearFocus();
            }
        }

        /// <summary>
        /// Apply Non-FIR quantity when losing focus
        /// </summary>
        private void TxtDetailOwnedNonFir_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyNonFirQuantityFromTextBox();
        }

        /// <summary>
        /// Apply Non-FIR quantity when pressing Enter
        /// </summary>
        private void TxtDetailOwnedNonFir_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyNonFirQuantityFromTextBox();
                Keyboard.ClearFocus();
            }
        }

        /// <summary>
        /// Parse and apply FIR quantity from TextBox input
        /// </summary>
        private void ApplyFirQuantityFromTextBox()
        {
            if (_selectedItem == null) return;

            if (int.TryParse(TxtDetailOwnedFir.Text, out var quantity))
            {
                quantity = Math.Max(0, quantity);
                _inventoryService.SetFirQuantity(_selectedItem.ItemNormalizedName, quantity);
                _selectedItem.OwnedFirQuantity = quantity;
                UpdateDetailInventoryDisplay();
            }
            else
            {
                TxtDetailOwnedFir.Text = _selectedItem.OwnedFirQuantity.ToString();
            }
        }

        /// <summary>
        /// Parse and apply Non-FIR quantity from TextBox input
        /// </summary>
        private void ApplyNonFirQuantityFromTextBox()
        {
            if (_selectedItem == null) return;

            if (int.TryParse(TxtDetailOwnedNonFir.Text, out var quantity))
            {
                quantity = Math.Max(0, quantity);
                _inventoryService.SetNonFirQuantity(_selectedItem.ItemNormalizedName, quantity);
                _selectedItem.OwnedNonFirQuantity = quantity;
                UpdateDetailInventoryDisplay();
            }
            else
            {
                TxtDetailOwnedNonFir.Text = _selectedItem.OwnedNonFirQuantity.ToString();
            }
        }

        #endregion
    }
}
