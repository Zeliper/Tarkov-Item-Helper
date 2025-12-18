using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Pages
{
    /// <summary>
    /// Hideout module view model for display
    /// </summary>
    public class HideoutModuleViewModel : INotifyPropertyChanged
    {
        public HideoutModule Module { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
        public string SubtitleName { get; set; } = string.Empty;
        public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
        public int CurrentLevel { get; set; }
        public int MaxLevel { get; set; }
        public bool CanIncrement { get; set; }
        public bool CanDecrement { get; set; }
        public bool IsMaxLevel { get; set; }

        private BitmapImage? _iconSource;
        public BitmapImage? IconSource
        {
            get => _iconSource;
            set
            {
                if (_iconSource != value)
                {
                    _iconSource = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Requirement view model for display
    /// </summary>
    public class RequirementViewModel : INotifyPropertyChanged
    {
        public string DisplayText { get; set; } = string.Empty;

        private BitmapImage? _iconSource;
        public BitmapImage? IconSource
        {
            get => _iconSource;
            set
            {
                if (_iconSource != value)
                {
                    _iconSource = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSource)));
                }
            }
        }

        public bool FoundInRaid { get; set; }
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;

        // For mixed FIR/non-FIR display
        public int TotalCount { get; set; }
        public int FIRCount { get; set; }
        public bool HasMixedFIR => FIRCount > 0 && FIRCount < TotalCount;

        // Item identifier for fulfillment check and navigation
        public string ItemId { get; set; } = string.Empty;
        public string ItemNormalizedName { get; set; } = string.Empty;

        // Icon link for lazy loading
        public string? IconLink { get; set; }

        // Fulfillment status
        public bool IsFulfilled { get; set; }
        public TextDecorationCollection? TextDecorations => IsFulfilled ? System.Windows.TextDecorations.Strikethrough : null;
        public double ItemOpacity => IsFulfilled ? 0.6 : 1.0;
        public Visibility FulfilledVisibility => IsFulfilled ? Visibility.Visible : Visibility.Collapsed;

        public static string FormatCountDisplay(string itemName, int totalCount, int firCount)
        {
            if (firCount == 0)
                return $"{itemName} x{totalCount}";
            if (firCount == totalCount)
                return $"{itemName} x{totalCount}";  // FIR badge shows separately
            // Mixed: show FIR and non-FIR counts
            var nonFirCount = totalCount - firCount;
            return $"{itemName} x{firCount}(FIR) + x{nonFirCount}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class HideoutPage : UserControl
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;
        private readonly HideoutProgressService _progressService = HideoutProgressService.Instance;
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private readonly ItemInventoryService _inventoryService = ItemInventoryService.Instance;
        private List<HideoutModuleViewModel> _allModuleViewModels = new();
        private bool _isInitializing = true;
        private bool _isUnloaded = false;
        private bool _isDataLoaded = false;
        private string? _pendingModuleSelection = null;

        public HideoutPage()
        {
            InitializeComponent();
            _loc.LanguageChanged += OnLanguageChanged;
            _progressService.ProgressChanged += OnProgressChanged;
            HideoutDbService.Instance.DataRefreshed += OnDatabaseRefreshed;

            Loaded += HideoutPage_Loaded;
            Unloaded += HideoutPage_Unloaded;
        }

        private void HideoutPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = true;
            _isDataLoaded = false; // Reset so SelectModule will use pending selection on next load
            // Unsubscribe from events to prevent memory leaks
            _loc.LanguageChanged -= OnLanguageChanged;
            _progressService.ProgressChanged -= OnProgressChanged;
            HideoutDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
        }

        private async void HideoutPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Re-subscribe events if page was previously unloaded
            if (_isUnloaded)
            {
                _isUnloaded = false;
                _loc.LanguageChanged += OnLanguageChanged;
                _progressService.ProgressChanged += OnProgressChanged;
                HideoutDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
            }

            // Show loading overlay
            LoadingOverlay.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;

            try
            {
                await LoadModulesAsync();
                if (_isUnloaded) return; // Check if page was unloaded during async operation

                _isInitializing = false;
                _isDataLoaded = true;
                ApplyFilters();
                UpdateStatistics();

                // Process pending selection if any
                if (!string.IsNullOrEmpty(_pendingModuleSelection))
                {
                    var pendingId = _pendingModuleSelection;
                    _pendingModuleSelection = null;
                    SelectModuleInternal(pendingId);
                }
            }
            finally
            {
                // Hide loading overlay - show UI immediately
                if (!_isUnloaded)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    MainContent.Visibility = Visibility.Visible;
                }
            }

            // Load module icons in background (after UI is visible)
            _ = LoadModuleIconsInBackgroundAsync();
        }

        private void OnLanguageChanged(object? sender, AppLanguage e)
        {
            RefreshModuleDisplayNames();
            ApplyFilters();
            UpdateDetailPanel();
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshModuleLevels();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateStatistics();
            });
        }

        private async void OnDatabaseRefreshed(object? sender, EventArgs e)
        {
            // DB 업데이트 후 데이터 다시 로드
            await Dispatcher.InvokeAsync(async () =>
            {
                await LoadModulesAsync();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateStatistics();

                // 아이콘 백그라운드 로드
                _ = LoadModuleIconsInBackgroundAsync();
            });
        }

        private Task LoadModulesAsync()
        {
            var modules = _progressService.AllModules;

            _allModuleViewModels = new List<HideoutModuleViewModel>();

            foreach (var module in modules)
            {
                var vm = CreateModuleViewModel(module);
                _allModuleViewModels.Add(vm);
            }

            // Note: Icon loading is now done separately via LoadModuleIconsInBackgroundAsync()
            return Task.CompletedTask;
        }

        /// <summary>
        /// Load module icons in background using parallel downloads.
        /// </summary>
        private async Task LoadModuleIconsInBackgroundAsync()
        {
            if (_allModuleViewModels == null || _allModuleViewModels.Count == 0)
                return;

            var modulesNeedingIcons = _allModuleViewModels
                .Where(vm => !string.IsNullOrEmpty(vm.Module.ImageLink) && vm.IconSource == null)
                .ToList();

            if (modulesNeedingIcons.Count == 0)
                return;

            // Parallel loading with concurrency limit
            const int maxConcurrency = 5;
            using var semaphore = new SemaphoreSlim(maxConcurrency);

            var tasks = modulesNeedingIcons.Select(async vm =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (_isUnloaded) return;

                    var icon = await _imageCache.GetImageAsync(vm.Module.ImageLink!, "hideout");
                    if (icon != null && !_isUnloaded)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            vm.IconSource = icon;
                        });
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);
        }

        private HideoutModuleViewModel CreateModuleViewModel(HideoutModule module)
        {
            var currentLevel = _progressService.GetCurrentLevel(module);
            var (displayName, subtitle, showSubtitle) = GetLocalizedNames(module);

            return new HideoutModuleViewModel
            {
                Module = module,
                DisplayName = displayName,
                SubtitleName = subtitle,
                SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                CurrentLevel = currentLevel,
                MaxLevel = module.MaxLevel,
                CanIncrement = currentLevel < module.MaxLevel,
                CanDecrement = currentLevel > 0,
                IsMaxLevel = currentLevel >= module.MaxLevel
            };
        }

        private (string DisplayName, string Subtitle, bool ShowSubtitle) GetLocalizedNames(HideoutModule module)
        {
            var lang = _loc.CurrentLanguage;

            if (lang == AppLanguage.EN)
            {
                return (module.Name, string.Empty, false);
            }

            var localizedName = lang switch
            {
                AppLanguage.KO => module.NameKo,
                AppLanguage.JA => module.NameJa,
                _ => null
            };

            if (!string.IsNullOrEmpty(localizedName))
            {
                return (localizedName, module.Name, true);
            }

            return (module.Name, string.Empty, false);
        }

        private void RefreshModuleDisplayNames()
        {
            foreach (var vm in _allModuleViewModels)
            {
                var (displayName, subtitle, showSubtitle) = GetLocalizedNames(vm.Module);
                vm.DisplayName = displayName;
                vm.SubtitleName = subtitle;
                vm.SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshModuleLevels()
        {
            foreach (var vm in _allModuleViewModels)
            {
                var currentLevel = _progressService.GetCurrentLevel(vm.Module);
                vm.CurrentLevel = currentLevel;
                vm.CanIncrement = currentLevel < vm.MaxLevel;
                vm.CanDecrement = currentLevel > 0;
                vm.IsMaxLevel = currentLevel >= vm.MaxLevel;
            }
        }

        private void ApplyFilters()
        {
            var searchText = TxtSearch.Text?.Trim().ToLowerInvariant() ?? string.Empty;

            var filtered = _allModuleViewModels.Where(vm =>
            {
                if (!string.IsNullOrEmpty(searchText))
                {
                    var matchName = vm.Module.Name?.ToLowerInvariant().Contains(searchText) == true;
                    var matchKo = vm.Module.NameKo?.ToLowerInvariant().Contains(searchText) == true;
                    var matchJa = vm.Module.NameJa?.ToLowerInvariant().Contains(searchText) == true;

                    if (!matchName && !matchKo && !matchJa)
                        return false;
                }

                return true;
            }).ToList();

            LstModules.ItemsSource = filtered;
        }

        private void UpdateStatistics()
        {
            var stats = _progressService.GetStatistics();
            TxtStats.Text = $"Modules: {stats.TotalModules} | " +
                           $"Completed: {stats.FullyCompleted} | " +
                           $"In Progress: {stats.InProgress} | " +
                           $"Not Started: {stats.NotStarted} | " +
                           $"Levels: {stats.CompletedLevels}/{stats.TotalLevels}";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void LstModules_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        private void IncrementButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HideoutModuleViewModel vm)
            {
                _progressService.IncrementLevel(vm.Module);
            }
        }

        private void DecrementButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HideoutModuleViewModel vm)
            {
                _progressService.DecrementLevel(vm.Module);
            }
        }

        private async void UpdateDetailPanel()
        {
            if (_isUnloaded) return;

            var selectedVm = LstModules.SelectedItem as HideoutModuleViewModel;

            if (selectedVm == null)
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                TxtSelectModule.Visibility = Visibility.Visible;
                return;
            }

            DetailPanel.Visibility = Visibility.Visible;
            TxtSelectModule.Visibility = Visibility.Collapsed;

            var module = selectedVm.Module;
            var currentLevel = _progressService.GetCurrentLevel(module);

            // Header
            var (displayName, subtitle, showSubtitle) = GetLocalizedNames(module);
            TxtDetailName.Text = displayName;
            TxtDetailSubtitle.Text = subtitle;
            TxtDetailSubtitle.Visibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed;

            // Icon
            if (!string.IsNullOrEmpty(module.ImageLink))
            {
                var icon = await _imageCache.GetImageAsync(module.ImageLink, "hideout");
                if (_isUnloaded) return;
                ImgDetailIcon.Source = icon;
            }
            else
            {
                ImgDetailIcon.Source = null;
            }

            // Level info
            TxtCurrentLevel.Text = currentLevel.ToString();
            TxtMaxLevel.Text = module.MaxLevel.ToString();

            // Next level requirements
            var nextLevel = _progressService.GetNextLevel(module);
            if (nextLevel != null)
            {
                NextLevelSection.Visibility = Visibility.Visible;
                TxtNextLevelHeader.Text = $"Next Level Requirements (Lv.{nextLevel.Level})";

                // Items
                if (nextLevel.ItemRequirements.Count > 0)
                {
                    TxtItemsHeader.Visibility = Visibility.Visible;
                    var itemVms = new List<RequirementViewModel>();
                    foreach (var itemReq in nextLevel.ItemRequirements)
                    {
                        var itemName = GetLocalizedItemName(itemReq);

                        // Calculate fulfillment status
                        var requiredFir = itemReq.FoundInRaid ? itemReq.Count : 0;
                        var fulfillmentInfo = _inventoryService.GetFulfillmentInfo(
                            itemReq.ItemNormalizedName, itemReq.Count, requiredFir);
                        var isFulfilled = fulfillmentInfo.Status == Models.ItemFulfillmentStatus.Fulfilled;

                        var vm = new RequirementViewModel
                        {
                            DisplayText = $"{itemName} x{itemReq.Count}",
                            FoundInRaid = itemReq.FoundInRaid,
                            ItemId = itemReq.ItemId,
                            ItemNormalizedName = itemReq.ItemNormalizedName,
                            IconLink = itemReq.IconLink,
                            IsFulfilled = isFulfilled
                        };

                        itemVms.Add(vm);
                    }
                    NextLevelItemsList.ItemsSource = itemVms;
                    // Load icons in background
                    _ = LoadRequirementIconsAsync(itemVms);
                }
                else
                {
                    TxtItemsHeader.Visibility = Visibility.Collapsed;
                    NextLevelItemsList.ItemsSource = null;
                }

                // Traders
                if (nextLevel.TraderRequirements.Count > 0)
                {
                    TxtTradersHeader.Visibility = Visibility.Visible;
                    NextLevelTradersList.Visibility = Visibility.Visible;
                    NextLevelTradersList.ItemsSource = nextLevel.TraderRequirements.Select(t =>
                        new RequirementViewModel
                        {
                            DisplayText = $"- {GetLocalizedTraderName(t)} Lv.{t.Level}"
                        }).ToList();
                }
                else
                {
                    TxtTradersHeader.Visibility = Visibility.Collapsed;
                    NextLevelTradersList.Visibility = Visibility.Collapsed;
                }

                // Skills
                if (nextLevel.SkillRequirements.Count > 0)
                {
                    TxtSkillsHeader.Visibility = Visibility.Visible;
                    NextLevelSkillsList.Visibility = Visibility.Visible;
                    NextLevelSkillsList.ItemsSource = nextLevel.SkillRequirements.Select(s =>
                        new RequirementViewModel
                        {
                            DisplayText = $"- {GetLocalizedSkillName(s)} Lv.{s.Level}"
                        }).ToList();
                }
                else
                {
                    TxtSkillsHeader.Visibility = Visibility.Collapsed;
                    NextLevelSkillsList.Visibility = Visibility.Collapsed;
                }

                // Other modules
                if (nextLevel.StationLevelRequirements.Count > 0)
                {
                    TxtModulesHeader.Visibility = Visibility.Visible;
                    NextLevelModulesList.Visibility = Visibility.Visible;
                    NextLevelModulesList.ItemsSource = nextLevel.StationLevelRequirements.Select(s =>
                        new RequirementViewModel
                        {
                            DisplayText = $"- {GetLocalizedStationName(s)} Lv.{s.Level}"
                        }).ToList();
                }
                else
                {
                    TxtModulesHeader.Visibility = Visibility.Collapsed;
                    NextLevelModulesList.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                NextLevelSection.Visibility = Visibility.Collapsed;
            }

            // Total remaining items
            var remainingItems = _progressService.GetRemainingItemRequirements(module);
            if (remainingItems.Count > 0)
            {
                var totalVms = new List<RequirementViewModel>();
                foreach (var kvp in remainingItems.OrderBy(k => k.Key))
                {
                    var itemName = GetLocalizedItemName(kvp.Value.Item);
                    var totalCount = kvp.Value.TotalCount;
                    var firCount = kvp.Value.FIRCount;

                    // Calculate fulfillment status
                    var fulfillmentInfo = _inventoryService.GetFulfillmentInfo(
                        kvp.Value.Item.ItemNormalizedName, totalCount, firCount);
                    var isFulfilled = fulfillmentInfo.Status == Models.ItemFulfillmentStatus.Fulfilled;

                    var vm = new RequirementViewModel
                    {
                        DisplayText = RequirementViewModel.FormatCountDisplay(itemName, totalCount, firCount),
                        TotalCount = totalCount,
                        FIRCount = firCount,
                        // Only show FIR badge if ALL items are FIR (not mixed)
                        FoundInRaid = firCount > 0 && firCount == totalCount,
                        ItemId = kvp.Value.Item.ItemId,
                        ItemNormalizedName = kvp.Value.Item.ItemNormalizedName,
                        IconLink = kvp.Value.Item.IconLink,
                        IsFulfilled = isFulfilled
                    };

                    totalVms.Add(vm);
                }
                TotalRemainingItemsList.ItemsSource = totalVms;
                // Load icons in background
                _ = LoadRequirementIconsAsync(totalVms);
            }
            else
            {
                TotalRemainingItemsList.ItemsSource = new[] { new RequirementViewModel { DisplayText = "All items collected!" } };
            }
        }

        /// <summary>
        /// Load requirement item icons in background.
        /// </summary>
        private async Task LoadRequirementIconsAsync(List<RequirementViewModel> items)
        {
            var itemsNeedingIcons = items
                .Where(vm => !string.IsNullOrEmpty(vm.IconLink) && vm.IconSource == null)
                .ToList();

            if (itemsNeedingIcons.Count == 0)
                return;

            var tasks = itemsNeedingIcons.Select(async vm =>
            {
                if (_isUnloaded) return;

                var icon = await _imageCache.GetImageAsync(vm.IconLink!, "items");
                if (icon != null && !_isUnloaded)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        vm.IconSource = icon;
                    });
                }
            });

            await Task.WhenAll(tasks);
        }

        private string GetLocalizedItemName(HideoutItemRequirement item)
        {
            return _loc.CurrentLanguage switch
            {
                AppLanguage.KO => item.ItemNameKo ?? item.ItemName,
                AppLanguage.JA => item.ItemNameJa ?? item.ItemName,
                _ => item.ItemName
            };
        }

        private string GetLocalizedTraderName(HideoutTraderRequirement trader)
        {
            return _loc.CurrentLanguage switch
            {
                AppLanguage.KO => trader.TraderNameKo ?? trader.TraderName,
                AppLanguage.JA => trader.TraderNameJa ?? trader.TraderName,
                _ => trader.TraderName
            };
        }

        private string GetLocalizedSkillName(HideoutSkillRequirement skill)
        {
            return _loc.CurrentLanguage switch
            {
                AppLanguage.KO => skill.NameKo ?? skill.Name,
                AppLanguage.JA => skill.NameJa ?? skill.Name,
                _ => skill.Name
            };
        }

        private string GetLocalizedStationName(HideoutStationRequirement station)
        {
            return _loc.CurrentLanguage switch
            {
                AppLanguage.KO => station.StationNameKo ?? station.StationName,
                AppLanguage.JA => station.StationNameJa ?? station.StationName,
                _ => station.StationName
            };
        }

        private void BtnWiki_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstModules.SelectedItem as HideoutModuleViewModel;
            if (selectedVm?.Module.NormalizedName == null) return;

            var wikiUrl = $"https://escapefromtarkov.fandom.com/wiki/Hideout#{Uri.EscapeDataString(selectedVm.Module.Name.Replace(" ", "_"))}";

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

        #region Cross-Tab Navigation

        /// <summary>
        /// Select a hideout module by station ID (for cross-tab navigation)
        /// </summary>
        public void SelectModule(string stationId)
        {
            // If data is not loaded yet, save for later
            if (!_isDataLoaded)
            {
                _pendingModuleSelection = stationId;
                return;
            }

            SelectModuleInternal(stationId);
        }

        /// <summary>
        /// Internal method to select a module (called when data is ready)
        /// </summary>
        private void SelectModuleInternal(string stationId)
        {
            // Prevent SelectionChanged from interfering during navigation
            _isInitializing = true;

            try
            {
                // Reset filters to ensure the module is visible
                TxtSearch.Text = "";

                // Apply filters to update the list
                ApplyFilters();

                // Find the module view model from the filtered list
                var filteredModules = LstModules.ItemsSource as IEnumerable<HideoutModuleViewModel>;
                var moduleVm = filteredModules?.FirstOrDefault(vm =>
                    string.Equals(vm.Module.Id, stationId, StringComparison.OrdinalIgnoreCase));

                if (moduleVm == null) return;

                // For virtualized lists: scroll first, then select
                LstModules.ScrollIntoView(moduleVm);
                LstModules.UpdateLayout();

                // Now select the module
                LstModules.SelectedItem = moduleVm;
                LstModules.UpdateLayout();

                // Scroll again to ensure visibility after selection
                LstModules.ScrollIntoView(moduleVm);

                // Update detail panel
                UpdateDetailPanel();

                // Focus the list to show selection highlight
                LstModules.Focus();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        /// <summary>
        /// Handle click on item name to navigate to Items tab
        /// </summary>
        private void ItemName_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is RequirementViewModel vm)
            {
                if (string.IsNullOrEmpty(vm.ItemId)) return;

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToItem(vm.ItemId);
            }
        }

        #endregion
    }
}
