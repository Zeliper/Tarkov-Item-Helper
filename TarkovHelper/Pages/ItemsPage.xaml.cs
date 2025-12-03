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
        public BitmapImage? IconSource { get; set; }
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

            Loaded += ItemsPage_Loaded;
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

        private async void ItemsPage_Loaded(object sender, RoutedEventArgs e)
        {
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
                // Load items lookup for quest item names
                var apiService = TarkovDevApiService.Instance;
                var items = await apiService.LoadItemsFromJsonAsync();
                if (items != null)
                {
                    _itemLookup = TarkovDevApiService.BuildItemLookup(items);
                }

                await LoadItemsAsync();
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
                // Hide loading overlay
                LoadingOverlay.Visibility = Visibility.Collapsed;
                MainContent.Visibility = Visibility.Visible;
            }
        }

        private void OnLanguageChanged(object? sender, AppLanguage e)
        {
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
                UpdateDetailPanel();
            });
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                await LoadItemsAsync();
                ApplyFilters();
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

            // Load icons and inventory data
            foreach (var vm in _allItemViewModels)
            {
                if (!string.IsNullOrEmpty(vm.IconLink))
                {
                    vm.IconSource = await _imageCache.GetItemIconAsync(vm.IconLink);
                }

                // Load inventory quantities
                var inventory = _inventoryService.GetInventory(vm.ItemNormalizedName);
                vm.OwnedFirQuantity = inventory.FirQuantity;
                vm.OwnedNonFirQuantity = inventory.NonFirQuantity;
            }
        }

        private Dictionary<string, QuestItemAggregate> GetQuestItemRequirements()
        {
            var result = new Dictionary<string, QuestItemAggregate>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in _questProgressService.AllTasks)
            {
                // Skip completed quests
                var status = _questProgressService.GetStatus(task);
                if (status == QuestStatus.Done)
                    continue;

                if (task.RequiredItems == null)
                    continue;

                foreach (var questItem in task.RequiredItems)
                {
                    // Look up item info from items.json
                    TarkovItem? itemInfo = null;
                    if (_itemLookup != null)
                    {
                        _itemLookup.TryGetValue(questItem.ItemNormalizedName, out itemInfo);
                    }

                    var itemName = itemInfo?.Name ?? questItem.ItemNormalizedName;
                    var iconLink = itemInfo?.IconLink;
                    var wikiLink = itemInfo?.WikiLink;

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
                // Skip completed quests
                var status = _questProgressService.GetStatus(task);
                if (status == QuestStatus.Done)
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
                                FoundInRaid = itemReq.FoundInRaid
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

        #endregion
    }
}
