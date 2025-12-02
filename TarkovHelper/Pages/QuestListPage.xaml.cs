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
    /// Quest list view model for display
    /// </summary>
    public class QuestViewModel
    {
        public TarkovTask Task { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
        public string SubtitleName { get; set; } = string.Empty;
        public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
        public string TraderInitial { get; set; } = string.Empty;
        public QuestStatus Status { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public Brush StatusBackground { get; set; } = Brushes.Gray;
        public Visibility CompleteButtonVisibility { get; set; } = Visibility.Visible;
    }

    /// <summary>
    /// Required item view model
    /// </summary>
    public class RequiredItemViewModel
    {
        public string DisplayText { get; set; } = string.Empty;
        public bool FoundInRaid { get; set; }
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;
        public BitmapImage? IconSource { get; set; }
        public string RequirementType { get; set; } = string.Empty;
        public Visibility RequirementTypeVisibility =>
            string.IsNullOrEmpty(RequirementType) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Guide image view model
    /// </summary>
    public class GuideImageViewModel
    {
        public string FileName { get; set; } = string.Empty;
        public string? Caption { get; set; }
        public BitmapImage? ImageSource { get; set; }
        public Visibility CaptionVisibility =>
            string.IsNullOrEmpty(Caption) ? Visibility.Collapsed : Visibility.Visible;
    }

    public partial class QuestListPage : UserControl
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;
        private readonly QuestProgressService _progressService = QuestProgressService.Instance;
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private List<QuestViewModel> _allQuestViewModels = new();
        private List<string> _traders = new();
        private List<string> _maps = new();
        private List<TarkovMap>? _mapData;
        private List<TarkovItem>? _itemData;
        private Dictionary<string, TarkovItem>? _itemLookup;
        private bool _isInitializing = true;

        // Status brushes
        private static readonly Brush LockedBrush = new SolidColorBrush(Color.FromRgb(102, 102, 102));
        private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        private static readonly Brush DoneBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243));
        private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));

        public QuestListPage()
        {
            InitializeComponent();
            _loc.LanguageChanged += OnLanguageChanged;
            _progressService.ProgressChanged += OnProgressChanged;

            Loaded += QuestListPage_Loaded;
        }

        private async void QuestListPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadMapsAsync();
            LoadQuests();
            PopulateTraderFilter();
            PopulateMapFilter();
            _isInitializing = false;
            ApplyFilters();
        }

        private async Task LoadMapsAsync()
        {
            var apiService = new TarkovDevApiService();
            _mapData = await apiService.LoadMapsFromJsonAsync();

            // Also load items data for localized names and icons
            _itemData = await apiService.LoadItemsFromJsonAsync();
            if (_itemData != null)
            {
                _itemLookup = TarkovDevApiService.BuildItemLookup(_itemData);
            }
        }

        private void OnLanguageChanged(object? sender, AppLanguage e)
        {
            RefreshQuestDisplayNames();
            ApplyFilters();
            UpdateDetailPanel();
        }

        private void OnProgressChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
            });
        }

        /// <summary>
        /// Public method to refresh the display from external callers
        /// </summary>
        public void RefreshDisplay()
        {
            RefreshQuestStatuses();
            ApplyFilters();
            UpdateDetailPanel();
        }

        private void LoadQuests()
        {
            var tasks = _progressService.AllTasks;

            _allQuestViewModels = tasks.Select(t => CreateQuestViewModel(t)).ToList();
            _traders = tasks.Select(t => t.Trader).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t).ToList();
            _maps = tasks.Where(t => t.Maps != null).SelectMany(t => t.Maps!).Distinct().OrderBy(m => m).ToList();
        }

        private QuestViewModel CreateQuestViewModel(TarkovTask task)
        {
            var status = _progressService.GetStatus(task);
            var (displayName, subtitle, showSubtitle) = GetLocalizedNames(task);

            return new QuestViewModel
            {
                Task = task,
                DisplayName = displayName,
                SubtitleName = subtitle,
                SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                TraderInitial = GetTraderInitial(task.Trader),
                Status = status,
                StatusText = GetStatusText(status),
                StatusBackground = GetStatusBrush(status),
                CompleteButtonVisibility = status == QuestStatus.Active || status == QuestStatus.Locked
                    ? Visibility.Visible : Visibility.Collapsed
            };
        }

        private (string DisplayName, string Subtitle, bool ShowSubtitle) GetLocalizedNames(TarkovTask task)
        {
            var lang = _loc.CurrentLanguage;

            if (lang == AppLanguage.EN)
            {
                return (task.Name, string.Empty, false);
            }

            // For KO/JA, show localized name as main, English as subtitle
            var localizedName = lang switch
            {
                AppLanguage.KO => task.NameKo,
                AppLanguage.JA => task.NameJa,
                _ => null
            };

            if (!string.IsNullOrEmpty(localizedName))
            {
                return (localizedName, task.Name, true);
            }

            // Fallback to English only
            return (task.Name, string.Empty, false);
        }

        private static string GetTraderInitial(string trader)
        {
            if (string.IsNullOrEmpty(trader)) return "?";
            return trader.Length >= 2 ? trader[..2].ToUpper() : trader.ToUpper();
        }

        private static string GetStatusText(QuestStatus status)
        {
            return status switch
            {
                QuestStatus.Locked => "Locked",
                QuestStatus.Active => "Active",
                QuestStatus.Done => "Done",
                QuestStatus.Failed => "Failed",
                _ => "Unknown"
            };
        }

        private static Brush GetStatusBrush(QuestStatus status)
        {
            return status switch
            {
                QuestStatus.Locked => LockedBrush,
                QuestStatus.Active => ActiveBrush,
                QuestStatus.Done => DoneBrush,
                QuestStatus.Failed => FailedBrush,
                _ => Brushes.Gray
            };
        }

        private void RefreshQuestDisplayNames()
        {
            foreach (var vm in _allQuestViewModels)
            {
                var (displayName, subtitle, showSubtitle) = GetLocalizedNames(vm.Task);
                vm.DisplayName = displayName;
                vm.SubtitleName = subtitle;
                vm.SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void RefreshQuestStatuses()
        {
            foreach (var vm in _allQuestViewModels)
            {
                var status = _progressService.GetStatus(vm.Task);
                vm.Status = status;
                vm.StatusText = GetStatusText(status);
                vm.StatusBackground = GetStatusBrush(status);
                vm.CompleteButtonVisibility = status == QuestStatus.Active || status == QuestStatus.Locked
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void PopulateTraderFilter()
        {
            // Clear existing items except "All Traders"
            while (CmbTrader.Items.Count > 1)
            {
                CmbTrader.Items.RemoveAt(1);
            }

            foreach (var trader in _traders)
            {
                CmbTrader.Items.Add(new ComboBoxItem { Content = trader, Tag = trader });
            }
        }

        private void PopulateMapFilter()
        {
            // Clear existing items except "All Maps"
            while (CmbMap.Items.Count > 1)
            {
                CmbMap.Items.RemoveAt(1);
            }

            foreach (var mapNormalized in _maps)
            {
                // Get localized map name
                var mapName = GetLocalizedMapName(mapNormalized);
                CmbMap.Items.Add(new ComboBoxItem { Content = mapName, Tag = mapNormalized });
            }
        }

        private string GetLocalizedMapName(string normalizedName)
        {
            if (_mapData == null) return normalizedName;

            var map = _mapData.FirstOrDefault(m =>
                string.Equals(m.NormalizedName, normalizedName, StringComparison.OrdinalIgnoreCase));

            if (map == null) return normalizedName;

            return _loc.CurrentLanguage switch
            {
                AppLanguage.KO => map.NameKo ?? map.Name,
                AppLanguage.JA => map.NameJa ?? map.Name,
                _ => map.Name
            };
        }

        private void ApplyFilters()
        {
            var searchText = TxtSearch.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            var kappaOnly = ChkKappaOnly.IsChecked == true;
            var itemRequired = ChkItemRequired.IsChecked == true;

            var selectedTrader = (CmbTrader.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var selectedMap = (CmbMap.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var selectedStatus = (CmbStatus.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Active";

            var filtered = _allQuestViewModels.Where(vm =>
            {
                // Search filter (multi-language)
                if (!string.IsNullOrEmpty(searchText))
                {
                    var matchName = vm.Task.Name?.ToLowerInvariant().Contains(searchText) == true;
                    var matchKo = vm.Task.NameKo?.ToLowerInvariant().Contains(searchText) == true;
                    var matchJa = vm.Task.NameJa?.ToLowerInvariant().Contains(searchText) == true;

                    if (!matchName && !matchKo && !matchJa)
                        return false;
                }

                // Kappa filter
                if (kappaOnly && !vm.Task.ReqKappa)
                    return false;

                // Item required filter
                if (itemRequired && (vm.Task.RequiredItems == null || vm.Task.RequiredItems.Count == 0))
                    return false;

                // Trader filter
                if (!string.IsNullOrEmpty(selectedTrader) && vm.Task.Trader != selectedTrader)
                    return false;

                // Map filter
                if (!string.IsNullOrEmpty(selectedMap))
                {
                    if (vm.Task.Maps == null || !vm.Task.Maps.Any(m =>
                        string.Equals(m, selectedMap, StringComparison.OrdinalIgnoreCase)))
                        return false;
                }

                // Status filter
                if (selectedStatus != "All")
                {
                    var statusFilter = Enum.Parse<QuestStatus>(selectedStatus);
                    if (vm.Status != statusFilter)
                        return false;
                }

                return true;
            }).ToList();

            LstQuests.ItemsSource = filtered;

            // Update statistics
            var stats = _progressService.GetStatistics();
            TxtStats.Text = $"Showing {filtered.Count} of {stats.Total} quests | " +
                           $"Active: {stats.Active} | Done: {stats.Done} | Locked: {stats.Locked} | Failed: {stats.Failed}";
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void CmbTrader_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void CmbMap_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void CmbStatus_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitializing) ApplyFilters();
        }

        private void LstQuests_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        private void UpdateDetailPanel()
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;

            if (selectedVm == null)
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                TxtSelectQuest.Visibility = Visibility.Visible;
                return;
            }

            DetailPanel.Visibility = Visibility.Visible;
            TxtSelectQuest.Visibility = Visibility.Collapsed;

            var task = selectedVm.Task;
            var status = _progressService.GetStatus(task);

            // Title
            var (displayName, subtitle, showSubtitle) = GetLocalizedNames(task);
            TxtDetailName.Text = displayName;
            TxtDetailSubtitle.Text = subtitle;
            TxtDetailSubtitle.Visibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed;

            // Trader & Status
            TxtDetailTrader.Text = task.Trader;
            TxtDetailStatus.Text = GetStatusText(status);
            DetailStatusBadge.Background = GetStatusBrush(status);

            // Maps
            if (task.Maps != null && task.Maps.Count > 0)
            {
                var mapNames = task.Maps.Select(GetLocalizedMapName);
                TxtDetailMap.Text = string.Join(", ", mapNames);
                MapInfoPanel.Visibility = Visibility.Visible;
            }
            else
            {
                TxtDetailMap.Text = "-";
                MapInfoPanel.Visibility = Visibility.Visible;
            }

            // Requirements
            if (task.RequiredLevel.HasValue && task.RequiredLevel.Value > 0)
            {
                TxtRequiredLevel.Text = $"Level {task.RequiredLevel}";
                TxtRequiredLevel.Visibility = Visibility.Visible;
            }
            else
            {
                TxtRequiredLevel.Visibility = Visibility.Collapsed;
            }

            // Prerequisites
            var prereqs = _progressService.GetPrerequisiteChain(task);
            if (prereqs.Count > 0)
            {
                var prereqVms = prereqs.Select(p =>
                {
                    var pStatus = _progressService.GetStatus(p);
                    var (pName, _, _) = GetLocalizedNames(p);
                    return new QuestViewModel
                    {
                        DisplayName = pName,
                        StatusText = GetStatusText(pStatus),
                        StatusBackground = GetStatusBrush(pStatus)
                    };
                }).ToList();

                PrerequisitesList.ItemsSource = prereqVms;
            }
            else
            {
                PrerequisitesList.ItemsSource = new[] { new QuestViewModel { DisplayName = "None" } };
            }

            // Guide Section
            UpdateGuideSection(task);

            // Required Items
            if (task.RequiredItems != null && task.RequiredItems.Count > 0)
            {
                _ = LoadRequiredItemsAsync(task.RequiredItems);
                TxtRequiredItemsHeader.Visibility = Visibility.Visible;
                RequiredItemsSection.Visibility = Visibility.Visible;
            }
            else
            {
                RequiredItemsList.ItemsSource = null;
                TxtRequiredItemsHeader.Visibility = Visibility.Collapsed;
                RequiredItemsSection.Visibility = Visibility.Collapsed;
            }

            // Button states
            BtnComplete.Visibility = status == QuestStatus.Done ? Visibility.Collapsed : Visibility.Visible;
            BtnReset.Visibility = status == QuestStatus.Done || status == QuestStatus.Failed
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CompleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QuestViewModel vm)
            {
                _progressService.CompleteQuest(vm.Task, true);
            }
        }

        private void BtnWiki_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;
            if (selectedVm?.Task.Name == null) return;

            var wikiPageName = NormalizedNameGenerator.GetWikiPageName(selectedVm.Task.Name);
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

        private void BtnComplete_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;
            if (selectedVm != null)
            {
                _progressService.CompleteQuest(selectedVm.Task, true);
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var selectedVm = LstQuests.SelectedItem as QuestViewModel;
            if (selectedVm != null)
            {
                _progressService.ResetQuest(selectedVm.Task);
            }
        }

        #region Guide Section

        private void UpdateGuideSection(TarkovTask task)
        {
            var hasGuideText = !string.IsNullOrEmpty(task.GuideText);
            var hasGuideImages = task.GuideImages != null && task.GuideImages.Count > 0;

            if (!hasGuideText && !hasGuideImages)
            {
                GuideSection.Visibility = Visibility.Collapsed;
                return;
            }

            GuideSection.Visibility = Visibility.Visible;

            // Guide text
            if (hasGuideText)
            {
                TxtGuideText.Text = task.GuideText;
                GuideTextExpander.Visibility = Visibility.Visible;
                GuideTextExpander.IsExpanded = false; // Reset to collapsed when switching quests
            }
            else
            {
                GuideTextExpander.Visibility = Visibility.Collapsed;
            }

            // Guide images
            if (hasGuideImages)
            {
                _ = LoadGuideImagesAsync(task.GuideImages!);
            }
            else
            {
                GuideImagesList.ItemsSource = null;
            }
        }

        private async Task LoadGuideImagesAsync(List<GuideImage> guideImages)
        {
            var imageVms = new List<GuideImageViewModel>();

            foreach (var guideImage in guideImages)
            {
                var vm = new GuideImageViewModel
                {
                    FileName = guideImage.FileName,
                    Caption = guideImage.Caption
                };

                // Load image asynchronously
                var image = await _imageCache.GetWikiImageAsync(guideImage.FileName);
                vm.ImageSource = image;
                imageVms.Add(vm);
            }

            // Update on UI thread
            Dispatcher.Invoke(() =>
            {
                GuideImagesList.ItemsSource = imageVms;
            });
        }

        private void GuideImage_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is GuideImageViewModel vm)
            {
                // Open wiki image in browser
                var encodedFileName = Uri.EscapeDataString(vm.FileName.Replace(" ", "_"));
                var url = $"https://escapefromtarkov.fandom.com/wiki/File:{encodedFileName}";

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignore errors opening browser
                }
            }
        }

        #endregion

        #region Required Items with Localization

        private async Task LoadRequiredItemsAsync(List<QuestItem> requiredItems)
        {
            var itemVms = new List<RequiredItemViewModel>();

            foreach (var item in requiredItems)
            {
                var vm = new RequiredItemViewModel
                {
                    FoundInRaid = item.FoundInRaid,
                    RequirementType = item.Requirement
                };

                // Get localized item name
                var localizedName = GetLocalizedItemName(item.ItemNormalizedName);
                vm.DisplayText = $"{localizedName} x{item.Amount}";

                // Get item icon
                var tarkovItem = GetItemByNormalizedName(item.ItemNormalizedName);
                if (tarkovItem?.IconLink != null)
                {
                    var icon = await _imageCache.GetItemIconAsync(tarkovItem.IconLink);
                    vm.IconSource = icon;
                }

                itemVms.Add(vm);
            }

            // Update on UI thread
            Dispatcher.Invoke(() =>
            {
                RequiredItemsList.ItemsSource = itemVms;
            });
        }

        private string GetLocalizedItemName(string normalizedName)
        {
            var item = GetItemByNormalizedName(normalizedName);
            if (item == null)
                return normalizedName;

            return _loc.CurrentLanguage switch
            {
                AppLanguage.KO => item.NameKo ?? item.Name,
                AppLanguage.JA => item.NameJa ?? item.Name,
                _ => item.Name
            };
        }

        private TarkovItem? GetItemByNormalizedName(string normalizedName)
        {
            if (_itemLookup == null)
                return null;

            // Try direct lookup
            if (_itemLookup.TryGetValue(normalizedName, out var item))
                return item;

            // Try with alternative names (fuzzy match)
            var alternatives = NormalizedNameGenerator.GenerateAlternatives(normalizedName);
            foreach (var alt in alternatives)
            {
                if (_itemLookup.TryGetValue(alt, out item))
                    return item;
            }

            return null;
        }

        #endregion
    }
}
