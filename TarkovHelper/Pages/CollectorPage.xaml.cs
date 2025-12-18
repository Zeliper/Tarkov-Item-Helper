using System.ComponentModel;
using System.Diagnostics;
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
    /// Aggregated item view model for Collector page display with inventory tracking
    /// </summary>
    public class CollectorItemViewModel : INotifyPropertyChanged
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemNormalizedName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SubtitleName { get; set; } = string.Empty;
        public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
        public int QuestCount { get; set; }
        public int QuestFIRCount { get; set; }
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

        // Display strings for UI
        public string QuestCountDisplay => QuestCount > 0 ? FormatCountDisplay(QuestCount, QuestFIRCount) : "0";
        public string TotalDisplay => FormatCountDisplay(TotalCount, TotalFIRCount);

        private static string FormatCountDisplay(int total, int firCount)
        {
            if (firCount == 0)
                return total.ToString();
            if (firCount == total)
                return $"{total} (FIR)";
            var nonFirCount = total - firCount;
            return $"{firCount}F+{nonFirCount}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Quest item source for Collector page - shows which quest requires this item
    /// </summary>
    public class CollectorQuestItemSourceViewModel
    {
        public string QuestName { get; set; } = string.Empty;
        public string TraderName { get; set; } = string.Empty;
        public int Amount { get; set; }
        public bool FoundInRaid { get; set; }
        public bool IsKappaRequired { get; set; }
        public string? WikiLink { get; set; }
        public TarkovTask? Task { get; set; }
        public string AmountDisplay => $"x{Amount}";
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;
        public Visibility KappaVisibility => IsKappaRequired ? Visibility.Visible : Visibility.Collapsed;
        public Visibility WikiButtonVisibility => Task != null ? Visibility.Visible : Visibility.Collapsed;
        public string QuestNormalizedName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Internal class for aggregating collector quest items
    /// </summary>
    internal class CollectorQuestItemAggregate
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

    public partial class CollectorPage : UserControl
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;
        private readonly QuestProgressService _questProgressService = QuestProgressService.Instance;
        private readonly QuestGraphService _questGraphService = QuestGraphService.Instance;
        private readonly ItemInventoryService _inventoryService = ItemInventoryService.Instance;
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private List<CollectorItemViewModel> _allItemViewModels = new();
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

        public CollectorPage()
        {
            InitializeComponent();
            _loc.LanguageChanged += OnLanguageChanged;
            _questProgressService.ProgressChanged += OnProgressChanged;
            _inventoryService.InventoryChanged += OnInventoryChanged;
            QuestDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
            ItemDbService.Instance.DataRefreshed += OnDatabaseRefreshed;

            Loaded += CollectorPage_Loaded;
            Unloaded += CollectorPage_Unloaded;
        }

        private void CollectorPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = true;
            _needsRefreshOnLoad = true; // Mark for refresh on next load to catch changes
            _loc.LanguageChanged -= OnLanguageChanged;
            _questProgressService.ProgressChanged -= OnProgressChanged;
            _inventoryService.InventoryChanged -= OnInventoryChanged;
            QuestDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
            ItemDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
        }

        private void OnInventoryChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
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
                _itemLookup = ItemDbService.Instance.GetItemLookup();

                // Collector items 데이터 다시 로드
                await LoadItemsAsync();
                ApplyFilters();
                UpdateDetailPanel();

                // 아이콘 백그라운드 로드
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private async void CollectorPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isUnloaded)
            {
                _isUnloaded = false;
                _loc.LanguageChanged += OnLanguageChanged;
                _questProgressService.ProgressChanged += OnProgressChanged;
                _inventoryService.InventoryChanged += OnInventoryChanged;
                QuestDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
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

            if (_isDataLoaded)
            {
                return;
            }

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
                if (_isUnloaded) return;

                _itemLookup = itemDbService.GetItemLookup();

                await LoadItemsAsync();
                if (_isUnloaded) return;

                _isInitializing = false;
                _isDataLoaded = true;
                ApplyFilters();

                if (!string.IsNullOrEmpty(_pendingItemSelection))
                {
                    var pendingName = _pendingItemSelection;
                    _pendingItemSelection = null;
                    SelectItemInternal(pendingName);
                }
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
            }

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
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
                _ = LoadImagesInBackgroundAsync();
            });
        }

        private async Task LoadItemsAsync()
        {
            var includePreQuest = ChkIncludePreQuest.IsChecked == true;
            var collectorItems = GetCollectorItemRequirements(includePreQuest);

            _allItemViewModels = collectorItems.Values.Select(item =>
            {
                var (displayName, subtitle, showSubtitle) = GetLocalizedNames(
                    item.ItemName, item.ItemNameKo, item.ItemNameJa);

                return new CollectorItemViewModel
                {
                    ItemId = item.ItemId,
                    ItemNormalizedName = item.ItemNormalizedName,
                    DisplayName = displayName,
                    SubtitleName = subtitle,
                    SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                    QuestCount = item.QuestCount,
                    QuestFIRCount = item.QuestFIRCount,
                    TotalCount = item.QuestCount,
                    TotalFIRCount = item.QuestFIRCount,
                    FoundInRaid = item.FoundInRaid,
                    IconLink = item.IconLink,
                    WikiLink = item.WikiLink
                };
            }).ToList();

            // Load inventory data
            foreach (var vm in _allItemViewModels)
            {
                var inventory = _inventoryService.GetInventory(vm.ItemNormalizedName);
                vm.OwnedFirQuantity = inventory.FirQuantity;
                vm.OwnedNonFirQuantity = inventory.NonFirQuantity;
            }
        }

        /// <summary>
        /// Get items required for Collector quest and optionally its prerequisites
        /// </summary>
        private Dictionary<string, CollectorQuestItemAggregate> GetCollectorItemRequirements(bool includePreQuests)
        {
            var result = new Dictionary<string, CollectorQuestItemAggregate>(StringComparer.OrdinalIgnoreCase);
            var questsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Find the Collector quest
            var collectorQuest = _questProgressService.AllTasks
                .FirstOrDefault(t => string.Equals(t.NormalizedName, "collector", StringComparison.OrdinalIgnoreCase));

            if (collectorQuest != null && !string.IsNullOrEmpty(collectorQuest.NormalizedName))
            {
                // Always include Collector quest itself (unless completed, failed, or unavailable)
                var status = _questProgressService.GetStatus(collectorQuest);
                if (status != QuestStatus.Done && status != QuestStatus.Failed && status != QuestStatus.Unavailable)
                {
                    questsToInclude.Add(collectorQuest.NormalizedName);
                }

                // If include pre-quests, add all prerequisites of Collector (which are Kappa quests)
                if (includePreQuests)
                {
                    var prereqs = _questGraphService.GetAllPrerequisites(collectorQuest.NormalizedName);
                    foreach (var prereq in prereqs)
                    {
                        if (string.IsNullOrEmpty(prereq.NormalizedName))
                            continue;

                        // Skip completed, failed, or unavailable quests
                        var prereqStatus = _questProgressService.GetStatus(prereq);
                        if (prereqStatus == QuestStatus.Done || prereqStatus == QuestStatus.Failed || prereqStatus == QuestStatus.Unavailable)
                            continue;

                        questsToInclude.Add(prereq.NormalizedName);
                    }
                }
            }

            // Collect items from all included quests
            foreach (var task in _questProgressService.AllTasks)
            {
                if (string.IsNullOrEmpty(task.NormalizedName))
                    continue;

                if (!questsToInclude.Contains(task.NormalizedName))
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

                    var itemName = itemInfo.Name;
                    var iconLink = itemInfo.IconLink;
                    var wikiLink = itemInfo.WikiLink;

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
                        result[questItem.ItemNormalizedName] = new CollectorQuestItemAggregate
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

        private async Task LoadImagesInBackgroundAsync()
        {
            if (_allItemViewModels == null || _allItemViewModels.Count == 0)
                return;

            await LoadVisibleItemImagesAsync();
            await LoadRemainingItemImagesAsync();
        }

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
        private void LoadItemImages(List<CollectorItemViewModel> items)
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

        private List<CollectorItemViewModel> GetVisibleItems()
        {
            var visibleItems = new List<CollectorItemViewModel>();

            if (LstItems.ItemsSource == null)
                return visibleItems;

            var scrollViewer = GetScrollViewer(LstItems);
            if (scrollViewer == null)
                return visibleItems;

            var itemsSource = LstItems.ItemsSource as IList<CollectorItemViewModel>;
            if (itemsSource == null || itemsSource.Count == 0)
                return visibleItems;

            const double estimatedItemHeight = 50;
            var viewportHeight = scrollViewer.ViewportHeight;
            var verticalOffset = scrollViewer.VerticalOffset;

            var startIndex = Math.Max(0, (int)(verticalOffset / estimatedItemHeight) - 2);
            var visibleCount = (int)(viewportHeight / estimatedItemHeight) + 5;
            var endIndex = Math.Min(itemsSource.Count - 1, startIndex + visibleCount);

            for (int i = startIndex; i <= endIndex; i++)
            {
                visibleItems.Add(itemsSource[i]);
            }

            return visibleItems;
        }

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

        private System.Windows.Threading.DispatcherTimer? _scrollDebounceTimer;

        private void LstItems_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0 && e.ViewportHeightChange == 0)
                return;

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
            var fulfillmentFilter = (CmbFulfillment.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
            var firOnly = ChkFirOnly.IsChecked == true;
            var hideFulfilled = ChkHideFulfilled.IsChecked == true;
            var sortBy = (CmbSort.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Name";

            var filtered = _allItemViewModels.Where(vm =>
            {
                if (!string.IsNullOrEmpty(searchText))
                {
                    if (!vm.DisplayName.ToLowerInvariant().Contains(searchText) &&
                        !vm.SubtitleName.ToLowerInvariant().Contains(searchText))
                        return false;
                }

                if (firOnly && !vm.FoundInRaid)
                    return false;

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

                if (hideFulfilled && vm.IsFulfilled)
                    return false;

                return true;
            });

            filtered = sortBy switch
            {
                "Total" => filtered.OrderByDescending(vm => vm.TotalCount).ThenBy(vm => vm.DisplayName),
                "Quest" => filtered.OrderByDescending(vm => vm.QuestCount).ThenBy(vm => vm.DisplayName),
                "Progress" => filtered.OrderByDescending(vm => vm.ProgressPercent).ThenBy(vm => vm.DisplayName),
                _ => filtered.OrderBy(vm => vm.DisplayName)
            };

            var filteredList = filtered.ToList();
            LstItems.ItemsSource = filteredList;

            var totalItems = filteredList.Count;
            var totalCount = filteredList.Sum(i => i.TotalCount);
            var fulfilledCount = filteredList.Count(i => i.IsFulfilled);
            var inProgressCount = filteredList.Count(i => i.FulfillmentStatus == ItemFulfillmentStatus.PartiallyFulfilled);
            var includePreQuest = ChkIncludePreQuest.IsChecked == true;

            TxtStats.Text = $"Showing {totalItems} items | " +
                           $"Total: {totalCount} | " +
                           $"Fulfilled: {fulfilledCount} | " +
                           $"In Progress: {inProgressCount}" +
                           (includePreQuest ? " | Including Pre-Quests" : " | Kappa Quests Only");
        }

        private async void ChkIncludePreQuest_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            // Reload items when Include Pre-Quest changes
            await LoadItemsAsync();
            ApplyFilters();
            _ = LoadImagesInBackgroundAsync();
        }

        private void CmbFulfillment_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
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

        public void SelectItem(string itemNormalizedName)
        {
            if (!_isDataLoaded)
            {
                _pendingItemSelection = itemNormalizedName;
                return;
            }

            SelectItemInternal(itemNormalizedName);
        }

        private void SelectItemInternal(string itemNormalizedName)
        {
            _isInitializing = true;

            try
            {
                ResetFiltersForNavigationInternal();
                ApplyFilters();

                var filteredItems = LstItems.ItemsSource as IEnumerable<CollectorItemViewModel>;
                var itemVm = filteredItems?.FirstOrDefault(vm =>
                    string.Equals(vm.ItemNormalizedName, itemNormalizedName, StringComparison.OrdinalIgnoreCase));

                if (itemVm == null) return;

                LstItems.ScrollIntoView(itemVm);
                LstItems.UpdateLayout();
                LstItems.SelectedItem = itemVm;
                LstItems.UpdateLayout();
                LstItems.ScrollIntoView(itemVm);

                _selectedItem = itemVm;
                _selectedItemNormalizedName = itemVm.ItemNormalizedName;
                ShowItemDetail(itemVm);

                LstItems.Focus();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void ResetFiltersForNavigationInternal()
        {
            TxtSearch.Text = "";
            CmbFulfillment.SelectedIndex = 0;
            ChkFirOnly.IsChecked = false;
            ChkHideFulfilled.IsChecked = false;
            CmbSort.SelectedIndex = 0;
        }

        private void ShowItemDetail(CollectorItemViewModel itemVm)
        {
            if (itemVm == null)
            {
                TxtSelectItem.Visibility = Visibility.Visible;
                DetailPanel.Visibility = Visibility.Collapsed;
                return;
            }

            TxtSelectItem.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;

            TxtDetailName.Text = itemVm.DisplayName;
            TxtDetailSubtitle.Text = itemVm.SubtitleName;
            TxtDetailSubtitle.Visibility = itemVm.SubtitleVisibility;
            ImgDetailIcon.Source = itemVm.IconSource;

            TxtDetailQuestCount.Text = itemVm.QuestCountDisplay;
            TxtDetailTotalCount.Text = itemVm.TotalDisplay;

            BtnWiki.IsEnabled = !string.IsNullOrEmpty(itemVm.WikiLink);

            TxtDetailOwnedFir.Text = itemVm.OwnedFirQuantity.ToString();
            TxtDetailOwnedNonFir.Text = itemVm.OwnedNonFirQuantity.ToString();

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

            DetailProgressBar.Value = itemVm.ProgressPercent;

            var questSources = GetQuestSources(itemVm.ItemNormalizedName);
            QuestRequirementsList.ItemsSource = questSources;
            QuestSection.Visibility = questSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private CollectorItemViewModel? _selectedItem;
        private string? _selectedItemNormalizedName;

        private void LstItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            _selectedItem = LstItems.SelectedItem as CollectorItemViewModel;
            _selectedItemNormalizedName = _selectedItem?.ItemNormalizedName;
            UpdateDetailPanel();
        }

        private void UpdateDetailPanel()
        {
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

            TxtDetailName.Text = _selectedItem.DisplayName;
            TxtDetailSubtitle.Text = _selectedItem.SubtitleName;
            TxtDetailSubtitle.Visibility = _selectedItem.SubtitleVisibility;
            ImgDetailIcon.Source = _selectedItem.IconSource;

            TxtDetailQuestCount.Text = _selectedItem.QuestCountDisplay;
            TxtDetailTotalCount.Text = _selectedItem.TotalDisplay;

            BtnWiki.IsEnabled = !string.IsNullOrEmpty(_selectedItem.WikiLink);

            UpdateDetailInventoryDisplay();

            var questSources = GetQuestSources(_selectedItem.ItemNormalizedName);
            QuestRequirementsList.ItemsSource = questSources;
            QuestSection.Visibility = questSources.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private List<CollectorQuestItemSourceViewModel> GetQuestSources(string itemNormalizedName)
        {
            var sources = new List<CollectorQuestItemSourceViewModel>();
            var includePreQuest = ChkIncludePreQuest.IsChecked == true;
            var questsToInclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Find the Collector quest
            var collectorQuest = _questProgressService.AllTasks
                .FirstOrDefault(t => string.Equals(t.NormalizedName, "collector", StringComparison.OrdinalIgnoreCase));

            if (collectorQuest != null && !string.IsNullOrEmpty(collectorQuest.NormalizedName))
            {
                var status = _questProgressService.GetStatus(collectorQuest);
                if (status != QuestStatus.Done && status != QuestStatus.Failed && status != QuestStatus.Unavailable)
                {
                    questsToInclude.Add(collectorQuest.NormalizedName);
                }

                // Add prerequisites if needed
                if (includePreQuest)
                {
                    var prereqs = _questGraphService.GetAllPrerequisites(collectorQuest.NormalizedName);
                    foreach (var prereq in prereqs)
                    {
                        if (string.IsNullOrEmpty(prereq.NormalizedName))
                            continue;
                        var prereqStatus = _questProgressService.GetStatus(prereq);
                        if (prereqStatus == QuestStatus.Done || prereqStatus == QuestStatus.Failed || prereqStatus == QuestStatus.Unavailable)
                            continue;
                        questsToInclude.Add(prereq.NormalizedName);
                    }
                }
            }

            foreach (var task in _questProgressService.AllTasks)
            {
                if (string.IsNullOrEmpty(task.NormalizedName))
                    continue;

                if (!questsToInclude.Contains(task.NormalizedName))
                    continue;

                if (task.RequiredItems == null)
                    continue;

                foreach (var questItem in task.RequiredItems)
                {
                    if (string.Equals(questItem.ItemNormalizedName, itemNormalizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        var questName = GetLocalizedQuestName(task);
                        var traderName = task.Trader;
                        sources.Add(new CollectorQuestItemSourceViewModel
                        {
                            QuestName = questName,
                            TraderName = traderName,
                            Amount = questItem.Amount,
                            FoundInRaid = questItem.FoundInRaid,
                            IsKappaRequired = task.ReqKappa,
                            Task = task,
                            QuestNormalizedName = task.NormalizedName ?? string.Empty
                        });
                    }
                }
            }

            return sources;
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

        private void QuestName_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is CollectorQuestItemSourceViewModel vm)
            {
                if (string.IsNullOrEmpty(vm.QuestNormalizedName)) return;

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToQuest(vm.QuestNormalizedName);
            }
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
            }
        }

        private void BtnQuestWiki_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CollectorQuestItemSourceViewModel vm && vm.Task != null)
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
                }
            }
        }

        #region Inventory Quantity Controls

        private void BtnFirMinus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustFirQuantity(sender, -1);
        }

        private void BtnFirPlus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustFirQuantity(sender, 1);
        }

        private void BtnNonFirMinus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustNonFirQuantity(sender, -1);
        }

        private void BtnNonFirPlus1_Click(object sender, RoutedEventArgs e)
        {
            AdjustNonFirQuantity(sender, 1);
        }

        private void AdjustFirQuantity(object sender, int delta)
        {
            if (sender is Button btn && btn.DataContext is CollectorItemViewModel vm)
            {
                _inventoryService.AdjustFirQuantity(vm.ItemNormalizedName, delta);
                vm.OwnedFirQuantity = _inventoryService.GetFirQuantity(vm.ItemNormalizedName);
            }
        }

        private void AdjustNonFirQuantity(object sender, int delta)
        {
            if (sender is Button btn && btn.DataContext is CollectorItemViewModel vm)
            {
                _inventoryService.AdjustNonFirQuantity(vm.ItemNormalizedName, delta);
                vm.OwnedNonFirQuantity = _inventoryService.GetNonFirQuantity(vm.ItemNormalizedName);
            }
        }

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

            DetailProgressBar.Value = _selectedItem.ProgressPercent;
        }

        private void TxtDetailOwned_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void TxtDetailOwnedFir_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyFirQuantityFromTextBox();
        }

        private void TxtDetailOwnedFir_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyFirQuantityFromTextBox();
                Keyboard.ClearFocus();
            }
        }

        private void TxtDetailOwnedNonFir_LostFocus(object sender, RoutedEventArgs e)
        {
            ApplyNonFirQuantityFromTextBox();
        }

        private void TxtDetailOwnedNonFir_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyNonFirQuantityFromTextBox();
                Keyboard.ClearFocus();
            }
        }

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
