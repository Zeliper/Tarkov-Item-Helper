using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Services;
using System.Linq;

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
        public bool IsKappaRequired { get; set; }
        public Visibility KappaBadgeVisibility => IsKappaRequired ? Visibility.Visible : Visibility.Collapsed;
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

        // Navigation identifier
        public string ItemNormalizedName { get; set; } = string.Empty;

        // Fulfillment status
        public bool IsFulfilled { get; set; }
        public TextDecorationCollection? TextDecorations => IsFulfilled ? System.Windows.TextDecorations.Strikethrough : null;
        public double ItemOpacity => IsFulfilled ? 0.6 : 1.0;
        public Visibility FulfilledVisibility => IsFulfilled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Prerequisite group view model for displaying OR/AND grouped prerequisites
    /// </summary>
    public class PrerequisiteGroupViewModel
    {
        public int GroupId { get; set; }
        public bool IsOrGroup => GroupId > 0;
        public string GroupLabel => IsOrGroup ? "OR" : "";
        public Visibility OrLabelVisibility => IsOrGroup ? Visibility.Visible : Visibility.Collapsed;
        public Brush OrGroupBackground => IsOrGroup ? new SolidColorBrush(Color.FromArgb(30, 33, 150, 243)) : Brushes.Transparent;
        public List<PrerequisiteItemViewModel> Items { get; set; } = new();
    }

    /// <summary>
    /// Single prerequisite item view model
    /// </summary>
    public class PrerequisiteItemViewModel
    {
        public TarkovTask? Task { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public Brush StatusBackground { get; set; } = Brushes.Gray;
        public bool IsOrItem { get; set; }
        public string OrSeparator => IsOrItem ? " OR " : "";
        public Visibility OrSeparatorVisibility => IsOrItem ? Visibility.Visible : Visibility.Collapsed;
        public string BulletText => IsOrItem ? "" : "• ";
    }

    /// <summary>
    /// Recommendation view model for display
    /// </summary>
    public class RecommendationViewModel
    {
        public QuestRecommendation Recommendation { get; set; } = null!;
        public string QuestName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string TypeText { get; set; } = string.Empty;
        public Brush TypeBackground { get; set; } = Brushes.Gray;
        public string TraderInitial { get; set; } = string.Empty;
        public bool IsKappaRequired { get; set; }
        public Visibility KappaBadgeVisibility => IsKappaRequired ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Guide image view model with loading state
    /// </summary>
    public class GuideImageViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private BitmapImage? _imageSource;
        private bool _isLoading = true;

        public string FileName { get; set; } = string.Empty;
        public string? Caption { get; set; }

        public BitmapImage? ImageSource
        {
            get => _imageSource;
            set
            {
                _imageSource = value;
                OnPropertyChanged(nameof(ImageSource));
                OnPropertyChanged(nameof(ImageVisibility));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(ImageVisibility));
            }
        }

        public Visibility CaptionVisibility =>
            string.IsNullOrEmpty(Caption) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility LoadingVisibility =>
            IsLoading ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ImageVisibility =>
            IsLoading ? Visibility.Collapsed : Visibility.Visible;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class QuestListPage : UserControl
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;
        private readonly QuestProgressService _progressService = QuestProgressService.Instance;
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private readonly ItemInventoryService _inventoryService = ItemInventoryService.Instance;
        private List<QuestViewModel> _allQuestViewModels = new();
        private List<string> _traders = new();
        private List<string> _maps = new();
        private Dictionary<string, TarkovItem>? _itemLookup;
        private bool _isInitializing = true;
        private bool _isDataLoaded = false;
        private bool _isUnloaded = false;
        private string? _pendingQuestSelection = null;
        private List<GuideImage>? _pendingGuideImages = null;
        private bool _guideImagesLoaded = false;
        private TarkovTask? _currentDetailTask = null;

        // Status brushes
        private static readonly Brush LockedBrush = new SolidColorBrush(Color.FromRgb(102, 102, 102));
        private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        private static readonly Brush DoneBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243));
        private static readonly Brush FailedBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
        private static readonly Brush LevelLockedBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange for Level Locked
        private static readonly Brush UnavailableBrush = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray for Unavailable

        // Recommendation type brushes
        private static readonly Brush ReadyToCompleteBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
        private static readonly Brush ItemHandInBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
        private static readonly Brush KappaPriorityBrush = new SolidColorBrush(Color.FromRgb(156, 39, 176)); // Purple
        private static readonly Brush UnlocksManyBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
        private static readonly Brush EasyQuestBrush = new SolidColorBrush(Color.FromRgb(0, 188, 212)); // Cyan

        public QuestListPage()
        {
            InitializeComponent();
            _loc.LanguageChanged += OnLanguageChanged;
            _progressService.ProgressChanged += OnProgressChanged;
            SettingsService.Instance.HasEodEditionChanged += OnProfileSettingChanged;
            SettingsService.Instance.HasUnheardEditionChanged += OnProfileSettingChanged;
            SettingsService.Instance.PrestigeLevelChanged += OnPrestigeLevelChanged;
            SettingsService.Instance.DspDecodeCountChanged += OnDspDecodeCountChanged;
            SettingsService.Instance.PlayerFactionChanged += OnPlayerFactionChanged;
            QuestDbService.Instance.DataRefreshed += OnDatabaseRefreshed;

            Loaded += QuestListPage_Loaded;
            Unloaded += QuestListPage_Unloaded;
        }

        private void QuestListPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isUnloaded = true;
            // Unsubscribe from events to prevent memory leaks
            _loc.LanguageChanged -= OnLanguageChanged;
            _progressService.ProgressChanged -= OnProgressChanged;
            SettingsService.Instance.HasEodEditionChanged -= OnProfileSettingChanged;
            SettingsService.Instance.HasUnheardEditionChanged -= OnProfileSettingChanged;
            SettingsService.Instance.PrestigeLevelChanged -= OnPrestigeLevelChanged;
            SettingsService.Instance.DspDecodeCountChanged -= OnDspDecodeCountChanged;
            SettingsService.Instance.PlayerFactionChanged -= OnPlayerFactionChanged;
            QuestDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
        }

        private async void QuestListPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Re-subscribe events if page was previously unloaded
            if (_isUnloaded)
            {
                _isUnloaded = false;
                _loc.LanguageChanged += OnLanguageChanged;
                _progressService.ProgressChanged += OnProgressChanged;
                SettingsService.Instance.HasEodEditionChanged += OnProfileSettingChanged;
                SettingsService.Instance.HasUnheardEditionChanged += OnProfileSettingChanged;
                SettingsService.Instance.PrestigeLevelChanged += OnPrestigeLevelChanged;
                SettingsService.Instance.DspDecodeCountChanged += OnDspDecodeCountChanged;
                SettingsService.Instance.PlayerFactionChanged += OnPlayerFactionChanged;
                QuestDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
            }

            // Skip if already loaded (prevents re-initialization on tab switching)
            if (_isDataLoaded) return;

            await LoadItemDataAsync();
            if (_isUnloaded) return; // Check if page was unloaded during async operation

            LoadQuests();
            PopulateTraderFilter();
            PopulateMapFilter();
            LoadFactionSelection();
            _isInitializing = false;
            _isDataLoaded = true;
            ApplyFilters();
            UpdateRecommendations();

            // Process pending selection if any
            if (!string.IsNullOrEmpty(_pendingQuestSelection))
            {
                var pendingName = _pendingQuestSelection;
                _pendingQuestSelection = null;
                SelectQuestInternal(pendingName);
            }
        }

        private async Task LoadItemDataAsync()
        {
            // Load items data from DB for localized names and icons
            var itemDbService = ItemDbService.Instance;
            if (!itemDbService.IsLoaded)
            {
                await itemDbService.LoadItemsAsync();
            }
            _itemLookup = itemDbService.GetItemLookup();
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
                UpdateRecommendations();
            });
        }

        private async void OnDatabaseRefreshed(object? sender, EventArgs e)
        {
            // DB 업데이트 후 데이터 다시 로드
            await Dispatcher.InvokeAsync(async () =>
            {
                // Item lookup 새로고침
                _itemLookup = ItemDbService.Instance.GetItemLookup();

                // Quest 데이터 다시 로드
                LoadQuests();
                PopulateTraderFilter();
                PopulateMapFilter();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        private void OnProfileSettingChanged(object? sender, bool e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        private void OnPrestigeLevelChanged(object? sender, int e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        private void OnDspDecodeCountChanged(object? sender, int e)
        {
            Dispatcher.Invoke(() =>
            {
                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
            });
        }

        private void OnPlayerFactionChanged(object? sender, string? e)
        {
            Dispatcher.Invoke(() =>
            {
                // Update radio button selection to match the new faction
                _isInitializing = true;
                if (e == "bear")
                {
                    RbBear.IsChecked = true;
                    RbUsec.IsChecked = false;
                }
                else if (e == "usec")
                {
                    RbUsec.IsChecked = true;
                    RbBear.IsChecked = false;
                }
                else
                {
                    RbBear.IsChecked = false;
                    RbUsec.IsChecked = false;
                }
                _isInitializing = false;

                RefreshQuestStatuses();
                ApplyFilters();
                UpdateDetailPanel();
                UpdateRecommendations();
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

        /// <summary>
        /// Reload all quest data from QuestProgressService
        /// Call this after data has been refreshed from API
        /// </summary>
        public async Task ReloadDataAsync()
        {
            // Reload map and item data
            await LoadItemDataAsync();

            // Reload quests from the updated progress service
            LoadQuests();

            // Repopulate filters
            PopulateTraderFilter();
            PopulateMapFilter();

            // Refresh display names with current locale
            RefreshQuestDisplayNames();

            // Apply filters to update the list
            ApplyFilters();
        }

        /// <summary>
        /// Select a quest by its normalized name (for cross-tab navigation)
        /// </summary>
        public void SelectQuest(string questNormalizedName)
        {
            // If data is not loaded yet, save for later
            if (!_isDataLoaded)
            {
                _pendingQuestSelection = questNormalizedName;
                return;
            }

            SelectQuestInternal(questNormalizedName);
        }

        /// <summary>
        /// Internal method to select a quest (called when data is ready)
        /// </summary>
        private void SelectQuestInternal(string questNormalizedName)
        {
            // Reset filters to ensure the quest is visible
            ResetFiltersForNavigation();

            // Find the quest view model
            var questVm = _allQuestViewModels.FirstOrDefault(vm =>
                string.Equals(vm.Task.NormalizedName, questNormalizedName, StringComparison.OrdinalIgnoreCase));

            if (questVm == null) return;

            // Disable selection changed event BEFORE ApplyFilters to prevent
            // the detail panel from being hidden when ItemsSource changes
            LstQuests.SelectionChanged -= LstQuests_SelectionChanged;

            // Apply filters to update the list
            ApplyFilters();

            // Use Dispatcher to ensure UI is updated before selection
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Select the quest in the list
                    LstQuests.SelectedItem = questVm;

                    // Scroll to make it visible
                    LstQuests.ScrollIntoView(questVm);

                    // Force UI update
                    LstQuests.UpdateLayout();

                    // Force update detail panel with the specific quest
                    UpdateDetailPanel(questVm);
                }
                finally
                {
                    // Re-enable selection changed event
                    LstQuests.SelectionChanged += LstQuests_SelectionChanged;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Reset filters for navigation to ensure target item is visible
        /// </summary>
        private void ResetFiltersForNavigation()
        {
            _isInitializing = true;

            // Reset status filter to "All"
            CmbStatus.SelectedIndex = 1; // "All"

            // Clear search text
            TxtSearch.Text = "";

            // Reset other filters
            ChkKappaOnly.IsChecked = false;
            ChkItemRequired.IsChecked = false;
            CmbTrader.SelectedIndex = 0; // "All Traders"
            CmbMap.SelectedIndex = 0; // "All Maps"

            _isInitializing = false;
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
                StatusText = GetStatusText(status, task),
                StatusBackground = GetStatusBrush(status),
                CompleteButtonVisibility = (status == QuestStatus.Active || status == QuestStatus.Locked || status == QuestStatus.LevelLocked)
                    && status != QuestStatus.Unavailable ? Visibility.Visible : Visibility.Collapsed,
                IsKappaRequired = task.ReqKappa
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

        private string GetStatusText(QuestStatus status, TarkovTask? task = null)
        {
            if (status == QuestStatus.LevelLocked && task != null)
            {
                // Check if it's level-locked or karma-locked
                if (task.RequiredLevel.HasValue && !_progressService.IsLevelRequirementMet(task))
                {
                    return $"Lv.{task.RequiredLevel}";
                }
                if (task.RequiredScavKarma.HasValue && !_progressService.IsScavKarmaRequirementMet(task))
                {
                    return $"Rep {task.RequiredScavKarma:0.#}";
                }
            }

            if (status == QuestStatus.Unavailable && task != null)
            {
                // Show specific reason for unavailability
                if (!_progressService.IsEditionRequirementMet(task))
                {
                    // Show which edition is required
                    var requiredEdition = task.RequiredEdition?.ToLowerInvariant();
                    if (requiredEdition == "eod" || requiredEdition == "edge_of_darkness")
                        return "EOD";
                    if (requiredEdition == "unheard" || requiredEdition == "the_unheard")
                        return "Unheard";
                    // Check for excluded edition
                    var excludedEdition = task.ExcludedEdition?.ToLowerInvariant();
                    if (!string.IsNullOrEmpty(excludedEdition))
                        return "Edition";
                }
                if (!_progressService.IsPrestigeLevelRequirementMet(task))
                {
                    return $"P.{task.RequiredPrestigeLevel}";
                }
                // Show faction if quest is for different faction
                if (!_progressService.IsFactionRequirementMet(task))
                {
                    var faction = task.Faction?.ToLowerInvariant();
                    if (faction == "bear")
                        return "BEAR";
                    if (faction == "usec")
                        return "USEC";
                }
            }

            return status switch
            {
                QuestStatus.Locked => "Locked",
                QuestStatus.Active => "Active",
                QuestStatus.Done => "Done",
                QuestStatus.Failed => "Failed",
                QuestStatus.LevelLocked => "Level",
                QuestStatus.Unavailable => "N/A",
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
                QuestStatus.LevelLocked => LevelLockedBrush,
                QuestStatus.Unavailable => UnavailableBrush,
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
                vm.StatusText = GetStatusText(status, vm.Task);
                vm.StatusBackground = GetStatusBrush(status);
                vm.CompleteButtonVisibility = (status == QuestStatus.Active || status == QuestStatus.Locked || status == QuestStatus.LevelLocked)
                    && status != QuestStatus.Unavailable ? Visibility.Visible : Visibility.Collapsed;
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
            // Simple formatting: capitalize first letter of each word
            if (string.IsNullOrEmpty(normalizedName)) return normalizedName;

            return System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(normalizedName.Replace("-", " "));
        }

        private void ApplyFilters()
        {
            var searchText = TxtSearch.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            var kappaOnly = ChkKappaOnly.IsChecked == true;
            var itemRequired = ChkItemRequired.IsChecked == true;

            var selectedTrader = (CmbTrader.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var selectedMap = (CmbMap.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? string.Empty;
            var selectedStatus = (CmbStatus.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Active";

            // Get selected faction
            var selectedFaction = RbBear.IsChecked == true ? "bear" : (RbUsec.IsChecked == true ? "usec" : null);

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
                    // "Locked" filter now includes both Locked and LevelLocked
                    if (selectedStatus == "Locked")
                    {
                        if (vm.Status != QuestStatus.Locked && vm.Status != QuestStatus.LevelLocked)
                            return false;
                    }
                    else
                    {
                        var statusFilter = Enum.Parse<QuestStatus>(selectedStatus);
                        if (vm.Status != statusFilter)
                            return false;
                    }
                }

                // Faction filter - hide quests for the other faction
                // Exception: Show in Unavailable filter so users can see faction-restricted quests
                if (!string.IsNullOrEmpty(selectedFaction) && !string.IsNullOrEmpty(vm.Task.Faction))
                {
                    if (!string.Equals(vm.Task.Faction, selectedFaction, StringComparison.OrdinalIgnoreCase))
                    {
                        // Only hide if NOT viewing Unavailable status
                        if (selectedStatus != "Unavailable")
                            return false;
                    }
                }

                return true;
            }).ToList();

            LstQuests.ItemsSource = filtered;

            // Update statistics
            var stats = _progressService.GetStatistics();
            var playerLevel = SettingsService.Instance.PlayerLevel;
            var lockedTotal = stats.Locked + stats.LevelLocked;
            TxtStats.Text = $"Lv.{playerLevel} | Showing {filtered.Count} of {stats.Total} quests | " +
                           $"Active: {stats.Active} | Locked: {lockedTotal} | Done: {stats.Done} | Failed: {stats.Failed} | N/A: {stats.Unavailable}";

            // Update Kappa progress gauge
            UpdateKappaGauge();
        }

        private void UpdateKappaGauge()
        {
            try
            {
                var graphService = QuestGraphService.Instance;
                var (completed, total, percentage) = graphService.GetCollectorProgress(
                    normalizedName => _progressService.IsQuestCompleted(normalizedName));

                TxtKappaGauge.Text = $"{completed}/{total}";
                KappaGaugeBar.Width = (percentage / 100.0) * 120; // 120 is the gauge width
            }
            catch
            {
                // QuestGraphService not initialized yet
                TxtKappaGauge.Text = "0/0";
                KappaGaugeBar.Width = 0;
            }
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

        private void Faction_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            // Save faction selection (setter automatically saves and notifies listeners)
            var faction = RbBear.IsChecked == true ? "bear" : (RbUsec.IsChecked == true ? "usec" : null);
            SettingsService.Instance.PlayerFaction = faction;

            ApplyFilters();
        }

        private void LoadFactionSelection()
        {
            var savedFaction = SettingsService.Instance.PlayerFaction;
            if (savedFaction == "bear")
            {
                RbBear.IsChecked = true;
            }
            else if (savedFaction == "usec")
            {
                RbUsec.IsChecked = true;
            }
            // If null, neither is selected (default state)
        }

        private void LstQuests_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        private void UpdateDetailPanel(QuestViewModel? overrideVm = null)
        {
            var selectedVm = overrideVm ?? LstQuests.SelectedItem as QuestViewModel;

            if (selectedVm == null)
            {
                DetailPanel.Visibility = Visibility.Collapsed;
                TxtSelectQuest.Visibility = Visibility.Visible;
                return;
            }

            DetailPanel.Visibility = Visibility.Visible;
            TxtSelectQuest.Visibility = Visibility.Collapsed;

            var task = selectedVm.Task;
            _currentDetailTask = task;
            var status = _progressService.GetStatus(task);

            // Show on Map button - hidden (Map feature removed)
            BtnShowOnMap.Visibility = Visibility.Collapsed;

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

            // Kappa Progress Section (for Collector quest)
            UpdateKappaProgressSection(task);

            // Requirements - Level with current level comparison
            bool hasLevelRequirement = task.RequiredLevel.HasValue && task.RequiredLevel.Value > 0;
            bool hasScavKarmaRequirement = task.RequiredScavKarma.HasValue;

            if (hasLevelRequirement)
            {
                var playerLevel = SettingsService.Instance.PlayerLevel;
                var reqLevel = task.RequiredLevel!.Value;
                if (playerLevel >= reqLevel)
                {
                    TxtRequiredLevel.Text = $"Level {reqLevel} (Current: {playerLevel})";
                    TxtRequiredLevel.Foreground = (Brush)FindResource("TextPrimaryBrush");
                }
                else
                {
                    TxtRequiredLevel.Text = $"Level {reqLevel} (Current: {playerLevel})";
                    TxtRequiredLevel.Foreground = LevelLockedBrush;
                }
                TxtRequiredLevel.Visibility = Visibility.Visible;
            }
            else
            {
                TxtRequiredLevel.Visibility = Visibility.Collapsed;
            }

            // Requirements - Scav Karma (Fence reputation)
            if (hasScavKarmaRequirement)
            {
                var playerScavRep = SettingsService.Instance.ScavRep;
                var reqKarma = task.RequiredScavKarma!.Value;
                var isMet = _progressService.IsScavKarmaRequirementMet(task);
                var comparison = reqKarma < 0 ? "≤" : "≥";
                TxtRequiredScavKarma.Text = $"Scav Karma {comparison} {reqKarma:0.#} (Current: {playerScavRep:0.#})";
                TxtRequiredScavKarma.Foreground = isMet ? (Brush)FindResource("TextPrimaryBrush") : LevelLockedBrush;
                TxtRequiredScavKarma.Visibility = Visibility.Visible;
            }
            else
            {
                TxtRequiredScavKarma.Visibility = Visibility.Collapsed;
            }

            // Show requirements section if any requirement exists
            RequirementsSectionWrapper.Visibility = (hasLevelRequirement || hasScavKarmaRequirement)
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Prerequisites - show direct prerequisites with OR/AND grouping
            if (task.TaskRequirements != null && task.TaskRequirements.Count > 0)
            {
                var prereqGroups = new List<PrerequisiteGroupViewModel>();

                // Group by GroupId: 0 = AND (each as separate group), >0 = OR (same GroupId = same group)
                var andRequirements = task.TaskRequirements.Where(r => r.GroupId == 0).ToList();
                var orGroups = task.TaskRequirements
                    .Where(r => r.GroupId > 0)
                    .GroupBy(r => r.GroupId)
                    .ToList();

                // Add AND requirements (each as separate entry)
                foreach (var req in andRequirements)
                {
                    var reqTask = !string.IsNullOrEmpty(req.TaskId)
                        ? _progressService.GetTaskById(req.TaskId)
                        : _progressService.GetTask(req.TaskNormalizedName);

                    if (reqTask == null) continue;

                    var pStatus = _progressService.GetStatus(reqTask);
                    var (pName, _, _) = GetLocalizedNames(reqTask);

                    prereqGroups.Add(new PrerequisiteGroupViewModel
                    {
                        GroupId = 0,
                        Items = new List<PrerequisiteItemViewModel>
                        {
                            new PrerequisiteItemViewModel
                            {
                                Task = reqTask,
                                DisplayName = pName,
                                StatusText = GetStatusText(pStatus),
                                StatusBackground = GetStatusBrush(pStatus),
                                IsOrItem = false
                            }
                        }
                    });
                }

                // Add OR groups (only show as OR group if 2+ items, otherwise treat as AND)
                foreach (var orGroup in orGroups)
                {
                    var orItems = orGroup.ToList();

                    // If OR group has only 1 item, treat it as a regular AND requirement
                    if (orItems.Count == 1)
                    {
                        var req = orItems[0];
                        var reqTask = !string.IsNullOrEmpty(req.TaskId)
                            ? _progressService.GetTaskById(req.TaskId)
                            : _progressService.GetTask(req.TaskNormalizedName);

                        if (reqTask != null)
                        {
                            var pStatus = _progressService.GetStatus(reqTask);
                            var (pName, _, _) = GetLocalizedNames(reqTask);

                            prereqGroups.Add(new PrerequisiteGroupViewModel
                            {
                                GroupId = 0, // Treat as AND (not OR group)
                                Items = new List<PrerequisiteItemViewModel>
                                {
                                    new PrerequisiteItemViewModel
                                    {
                                        Task = reqTask,
                                        DisplayName = pName,
                                        StatusText = GetStatusText(pStatus),
                                        StatusBackground = GetStatusBrush(pStatus),
                                        IsOrItem = false
                                    }
                                }
                            });
                        }
                        continue;
                    }

                    // OR group with 2+ items - display as OR group
                    var groupVm = new PrerequisiteGroupViewModel
                    {
                        GroupId = orGroup.Key,
                        Items = new List<PrerequisiteItemViewModel>()
                    };

                    bool isFirst = true;
                    foreach (var req in orItems)
                    {
                        var reqTask = !string.IsNullOrEmpty(req.TaskId)
                            ? _progressService.GetTaskById(req.TaskId)
                            : _progressService.GetTask(req.TaskNormalizedName);

                        if (reqTask == null) continue;

                        var pStatus = _progressService.GetStatus(reqTask);
                        var (pName, _, _) = GetLocalizedNames(reqTask);

                        groupVm.Items.Add(new PrerequisiteItemViewModel
                        {
                            Task = reqTask,
                            DisplayName = pName,
                            StatusText = GetStatusText(pStatus),
                            StatusBackground = GetStatusBrush(pStatus),
                            IsOrItem = !isFirst  // Show "OR" separator for 2nd item onwards
                        });
                        isFirst = false;
                    }

                    if (groupVm.Items.Count > 0)
                    {
                        prereqGroups.Add(groupVm);
                    }
                }

                PrerequisitesList.ItemsSource = prereqGroups;
                PrerequisitesSectionWrapper.Visibility = prereqGroups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                PrerequisitesList.ItemsSource = null;
                PrerequisitesSectionWrapper.Visibility = Visibility.Collapsed;
            }

            // Alternative Quests Section (Mutually Exclusive)
            var alternativeQuests = _progressService.GetAlternativeQuests(task);
            if (alternativeQuests.Count > 0)
            {
                var altVms = alternativeQuests.Select(alt =>
                {
                    var altStatus = _progressService.GetStatus(alt);
                    var (displayName, _, _) = GetLocalizedNames(alt);
                    return new
                    {
                        DisplayName = displayName,
                        TraderName = alt.Trader,
                        StatusText = GetStatusText(altStatus, alt),
                        StatusBackground = GetStatusBrush(altStatus)
                    };
                }).ToList();

                AlternativeQuestsList.ItemsSource = altVms;
                AlternativeQuestsSectionWrapper.Visibility = Visibility.Visible;
            }
            else
            {
                AlternativeQuestsList.ItemsSource = null;
                AlternativeQuestsSectionWrapper.Visibility = Visibility.Collapsed;
            }

            // Objectives Section
            UpdateObjectivesSection(task);

            // Guide Section
            UpdateGuideSection(task);

            // Required Items
            if (task.RequiredItems != null && task.RequiredItems.Count > 0)
            {
                LoadRequiredItems(task.RequiredItems);
                RequiredItemsSectionWrapper.Visibility = Visibility.Visible;
            }
            else
            {
                RequiredItemsList.ItemsSource = null;
                RequiredItemsSectionWrapper.Visibility = Visibility.Collapsed;
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
            catch { /* Ignore errors opening browser */ }
        }

        private void BtnShowOnMap_Click(object sender, RoutedEventArgs e)
        {
            // Map feature removed - this button is hidden
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

        #region Kappa Progress Section

        private void UpdateKappaProgressSection(TarkovTask task)
        {
            // Check if this is the Collector quest
            var isCollector = task.NormalizedName?.Equals("collector", StringComparison.OrdinalIgnoreCase) == true;

            if (!isCollector)
            {
                KappaProgressSection.Visibility = Visibility.Collapsed;
                return;
            }

            KappaProgressSection.Visibility = Visibility.Visible;

            // Get Kappa progress
            var graphService = QuestGraphService.Instance;
            var (completed, total, percentage) = graphService.GetCollectorProgress(
                normalizedName => _progressService.IsQuestCompleted(normalizedName));

            // Update progress text
            TxtKappaProgress.Text = $"Prerequisites: ({completed}/{total} completed)";
            TxtKappaProgressPercent.Text = $"{percentage}%";

            // Update progress bar width
            KappaProgressBar.Width = (percentage / 100.0) * (KappaProgressBar.Parent as Grid)?.ActualWidth ?? 0;

            // If parent grid not yet rendered, set it after layout
            if (KappaProgressBar.Width == 0 && percentage > 0)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var parentGrid = KappaProgressBar.Parent as Grid;
                    if (parentGrid != null)
                    {
                        KappaProgressBar.Width = (percentage / 100.0) * parentGrid.ActualWidth;
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void BtnShowKappaQuests_Click(object sender, RoutedEventArgs e)
        {
            var graphService = QuestGraphService.Instance;
            var kappaQuests = graphService.GetKappaRequiredQuestsWithStatus(
                normalizedName => _progressService.IsQuestCompleted(normalizedName));

            // Create a popup window to show all Kappa required quests
            var popupWindow = new Window
            {
                Title = "Kappa Required Quests",
                Width = 500,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = (Brush)FindResource("BackgroundDarkBrush")
            };

            var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stackPanel = new StackPanel { Margin = new Thickness(16) };

            // Header
            var (completed, total, percentage) = graphService.GetCollectorProgress(
                normalizedName => _progressService.IsQuestCompleted(normalizedName));
            var headerText = new TextBlock
            {
                Text = $"Kappa Required Quests ({completed}/{total})",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentBrush"),
                Margin = new Thickness(0, 0, 0, 16)
            };
            stackPanel.Children.Add(headerText);

            // Quest list
            foreach (var (quest, isCompleted) in kappaQuests)
            {
                var (displayName, _, _) = GetLocalizedNames(quest);
                var questPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

                // Status indicator
                var statusIndicator = new TextBlock
                {
                    Text = isCompleted ? "✓" : "○",
                    FontSize = 14,
                    Foreground = isCompleted ? DoneBrush : (Brush)FindResource("TextSecondaryBrush"),
                    Width = 24,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Quest name
                var questName = new TextBlock
                {
                    Text = displayName,
                    FontSize = 13,
                    Foreground = isCompleted ? (Brush)FindResource("TextSecondaryBrush") : (Brush)FindResource("TextPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextDecorations = isCompleted ? TextDecorations.Strikethrough : null
                };

                // Trader
                var traderText = new TextBlock
                {
                    Text = $"  ({quest.Trader})",
                    FontSize = 11,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center
                };

                questPanel.Children.Add(statusIndicator);
                questPanel.Children.Add(questName);
                questPanel.Children.Add(traderText);
                stackPanel.Children.Add(questPanel);
            }

            scrollViewer.Content = stackPanel;
            popupWindow.Content = scrollViewer;
            popupWindow.ShowDialog();
        }

        #endregion

        #region Objectives Section

        private void UpdateObjectivesSection(TarkovTask task)
        {
            ObjectivesList.Children.Clear();

            if (task.Objectives != null && task.Objectives.Count > 0)
            {
                for (int i = 0; i < task.Objectives.Count; i++)
                {
                    var objective = task.Objectives[i];
                    var isCompleted = task.NormalizedName != null &&
                        _progressService.IsObjectiveCompleted(task.NormalizedName, i);
                    var objectiveElement = CreateObjectiveElement(objective, i, isCompleted);
                    ObjectivesList.Children.Add(objectiveElement);
                }
                ObjectivesSection.Visibility = Visibility.Visible;
            }
            else
            {
                ObjectivesSection.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Create an objective element with checkbox and Optional badge if needed
        /// </summary>
        private FrameworkElement CreateObjectiveElement(string objective, int objectiveIndex, bool isCompleted)
        {
            // Check for (''Optional'') pattern in wiki markup
            var optionalPattern = @"\(''Optional''\)\s*";
            var isOptional = System.Text.RegularExpressions.Regex.IsMatch(objective, optionalPattern);

            // Remove the optional marker from text
            var cleanedObjective = System.Text.RegularExpressions.Regex.Replace(objective, optionalPattern, "").Trim();

            // Main container with checkbox
            var mainContainer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 2)
            };

            // Checkbox
            var checkBox = new CheckBox
            {
                IsChecked = isCompleted,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0),
                Tag = objectiveIndex
            };
            checkBox.Checked += ObjectiveCheckBox_Changed;
            checkBox.Unchecked += ObjectiveCheckBox_Changed;
            mainContainer.Children.Add(checkBox);

            if (isOptional)
            {
                // Create horizontal layout with Optional badge + text
                var contentContainer = new StackPanel
                {
                    Orientation = Orientation.Horizontal
                };

                // Optional badge
                var badge = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(60, 255, 193, 7)), // Amber/yellow with transparency
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };

                var badgeText = new TextBlock
                {
                    Text = "Optional",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(255, 193, 7)) // Amber color
                };

                badge.Child = badgeText;
                contentContainer.Children.Add(badge);

                // Create text block without bullet (badge replaces the bullet indicator)
                var textBlock = CreateRichTextBlockWithoutBullet(cleanedObjective, isCompleted);
                contentContainer.Children.Add(textBlock);

                mainContainer.Children.Add(contentContainer);
            }
            else
            {
                // Create text block without bullet (checkbox replaces the bullet)
                var textBlock = CreateRichTextBlockWithoutBullet(cleanedObjective, isCompleted);
                mainContainer.Children.Add(textBlock);
            }

            return mainContainer;
        }

        private void ObjectiveCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_currentDetailTask?.NormalizedName == null) return;

            var checkBox = sender as CheckBox;
            if (checkBox?.Tag is int objectiveIndex)
            {
                var isCompleted = checkBox.IsChecked ?? false;

                _progressService.SetObjectiveCompleted(
                    _currentDetailTask.NormalizedName,
                    objectiveIndex,
                    isCompleted,
                    null);

                // Update the text style (strikethrough)
                var parent = checkBox.Parent as StackPanel;
                if (parent != null)
                {
                    UpdateObjectiveTextStyle(parent, isCompleted);
                }
            }
        }

        private void UpdateObjectiveTextStyle(StackPanel container, bool isCompleted)
        {
            foreach (var child in container.Children)
            {
                if (child is TextBlock textBlock)
                {
                    textBlock.TextDecorations = isCompleted ? TextDecorations.Strikethrough : null;
                    textBlock.Opacity = isCompleted ? 0.6 : 1.0;
                }
                else if (child is StackPanel innerPanel)
                {
                    UpdateObjectiveTextStyle(innerPanel, isCompleted);
                }
            }
        }

        /// <summary>
        /// Create a TextBlock with rich text but without bullet point (for checkbox items)
        /// </summary>
        private TextBlock CreateRichTextBlockWithoutBullet(string wikiText, bool isCompleted = false)
        {
            var textBlock = new TextBlock
            {
                FontFamily = (System.Windows.Media.FontFamily)FindResource("MaplestoryFont"),
                FontSize = (double)FindResource("FontSizeXSmall"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 260, // Slightly smaller to account for checkbox and badge
                VerticalAlignment = VerticalAlignment.Top,
                TextDecorations = isCompleted ? TextDecorations.Strikethrough : null,
                Opacity = isCompleted ? 0.6 : 1.0
            };

            // Parse and add content (no bullet)
            ParseWikiMarkup(wikiText, textBlock);

            return textBlock;
        }

        /// <summary>
        /// Create a TextBlock with rich text from wiki markup (links and HTML colors)
        /// </summary>
        private TextBlock CreateRichTextBlock(string wikiText)
        {
            var textBlock = new TextBlock
            {
                FontFamily = (System.Windows.Media.FontFamily)FindResource("MaplestoryFont"),
                FontSize = (double)FindResource("FontSizeXSmall"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2),
                MaxWidth = 300
            };

            // Add bullet point
            textBlock.Inlines.Add(new System.Windows.Documents.Run("• ")
            {
                Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
                FontWeight = FontWeights.Bold
            });

            // Parse and add content
            ParseWikiMarkup(wikiText, textBlock);

            return textBlock;
        }

        /// <summary>
        /// Parse wiki markup and add to TextBlock inlines
        /// Supports: [[Link|Text]], [[Link]], <font color="...">text</font>
        /// </summary>
        private void ParseWikiMarkup(string text, TextBlock textBlock)
        {
            var defaultBrush = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
            var linkBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");

            // Pattern to match wiki links and font color tags
            var pattern = @"(\[\[([^\]|]+)(?:\|([^\]]+))?\]\])|(<font\s+color=""([^""]+)"">([^<]+)</font>)";
            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            int lastIndex = 0;
            var matches = regex.Matches(text);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                // Add text before match
                if (match.Index > lastIndex)
                {
                    var beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                    textBlock.Inlines.Add(new System.Windows.Documents.Run(beforeText) { Foreground = defaultBrush });
                }

                if (match.Groups[1].Success)
                {
                    // Wiki link: [[Link|Text]] or [[Link]]
                    var linkTarget = match.Groups[2].Value;
                    var displayText = match.Groups[3].Success ? match.Groups[3].Value : linkTarget;

                    var hyperlink = new System.Windows.Documents.Hyperlink
                    {
                        Foreground = linkBrush,
                        TextDecorations = null
                    };
                    hyperlink.Tag = linkTarget;
                    hyperlink.Click += WikiLink_Click;
                    hyperlink.MouseEnter += (s, e) => ((System.Windows.Documents.Hyperlink)s).TextDecorations = TextDecorations.Underline;
                    hyperlink.MouseLeave += (s, e) => ((System.Windows.Documents.Hyperlink)s).TextDecorations = null;

                    // Parse display text for nested font tags
                    ParseHyperlinkContent(displayText, hyperlink, linkBrush);

                    textBlock.Inlines.Add(hyperlink);
                }
                else if (match.Groups[4].Success)
                {
                    // Font color: <font color="...">text</font>
                    var colorStr = match.Groups[5].Value;
                    var coloredText = match.Groups[6].Value;

                    System.Windows.Media.Brush colorBrush;
                    try
                    {
                        colorBrush = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr));
                    }
                    catch
                    {
                        colorBrush = defaultBrush;
                    }

                    textBlock.Inlines.Add(new System.Windows.Documents.Run(coloredText) { Foreground = colorBrush });
                }

                lastIndex = match.Index + match.Length;
            }

            // Add remaining text
            if (lastIndex < text.Length)
            {
                var remainingText = text.Substring(lastIndex);
                textBlock.Inlines.Add(new System.Windows.Documents.Run(remainingText) { Foreground = defaultBrush });
            }
        }

        /// <summary>
        /// Parse content for a hyperlink, handling nested font color tags
        /// </summary>
        private void ParseHyperlinkContent(string displayText, System.Windows.Documents.Hyperlink hyperlink, System.Windows.Media.Brush defaultBrush)
        {
            // Pattern to match font color tags
            var fontPattern = @"<font\s+color=""([^""]+)"">([^<]+)</font>";
            var fontRegex = new System.Text.RegularExpressions.Regex(fontPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            int lastIndex = 0;
            var matches = fontRegex.Matches(displayText);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                // Add text before match
                if (match.Index > lastIndex)
                {
                    var beforeText = displayText.Substring(lastIndex, match.Index - lastIndex);
                    hyperlink.Inlines.Add(new System.Windows.Documents.Run(beforeText) { Foreground = defaultBrush });
                }

                // Add colored text
                var colorStr = match.Groups[1].Value;
                var coloredText = match.Groups[2].Value;

                System.Windows.Media.Brush colorBrush;
                try
                {
                    colorBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr));
                }
                catch
                {
                    colorBrush = defaultBrush;
                }

                hyperlink.Inlines.Add(new System.Windows.Documents.Run(coloredText) { Foreground = colorBrush });
                lastIndex = match.Index + match.Length;
            }

            // Add remaining text
            if (lastIndex < displayText.Length)
            {
                var remainingText = displayText.Substring(lastIndex);
                hyperlink.Inlines.Add(new System.Windows.Documents.Run(remainingText) { Foreground = defaultBrush });
            }

            // If no content was added (no font tags and no plain text), add displayText as-is
            if (hyperlink.Inlines.Count == 0)
            {
                hyperlink.Inlines.Add(new System.Windows.Documents.Run(displayText) { Foreground = defaultBrush });
            }
        }

        private void WikiLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Documents.Hyperlink hyperlink && hyperlink.Tag is string linkTarget)
            {
                // Open wiki page in browser
                var wikiUrl = $"https://escapefromtarkov.fandom.com/wiki/{Uri.EscapeDataString(linkTarget.Replace(" ", "_"))}";
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = wikiUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        #endregion

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

            // Guide text - show directly in scrollable text block
            if (hasGuideText)
            {
                TxtGuideText.Text = task.GuideText;
                GuideTextSection.Visibility = Visibility.Visible;
            }
            else
            {
                GuideTextSection.Visibility = Visibility.Collapsed;
            }

            // Guide images - lazy load when expander is opened
            if (hasGuideImages)
            {
                _pendingGuideImages = task.GuideImages;
                _guideImagesLoaded = false;
                GuideImagesList.ItemsSource = null; // Clear previous images
                GuideImagesExpander.IsExpanded = false; // Reset to collapsed
                GuideImagesExpander.Visibility = Visibility.Visible;
                TxtGuideImagesHeader.Text = $"View Images ({task.GuideImages!.Count})";
            }
            else
            {
                _pendingGuideImages = null;
                GuideImagesExpander.Visibility = Visibility.Collapsed;
                GuideImagesList.ItemsSource = null;
            }
        }

        private void GuideImagesExpander_Expanded(object sender, RoutedEventArgs e)
        {
            // Load images only when expander is opened for the first time
            if (!_guideImagesLoaded && _pendingGuideImages != null)
            {
                _guideImagesLoaded = true;
                _ = LoadGuideImagesAsync(_pendingGuideImages);
            }
        }

        private async Task LoadGuideImagesAsync(List<GuideImage> guideImages)
        {
            // Create ViewModels with loading state first
            var imageVms = new System.Collections.ObjectModel.ObservableCollection<GuideImageViewModel>();

            foreach (var guideImage in guideImages)
            {
                imageVms.Add(new GuideImageViewModel
                {
                    FileName = guideImage.FileName,
                    Caption = guideImage.Caption,
                    IsLoading = true
                });
            }

            // Set ItemsSource immediately to show placeholders
            if (!_isUnloaded)
            {
                Dispatcher.Invoke(() =>
                {
                    if (!_isUnloaded)
                        GuideImagesList.ItemsSource = imageVms;
                });
            }

            // Load images in parallel for better performance
            var loadTasks = imageVms.Select(async (vm, index) =>
            {
                if (_isUnloaded) return;

                var image = await _imageCache.GetWikiImageAsync(vm.FileName);

                // Update on UI thread
                if (!_isUnloaded)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!_isUnloaded)
                        {
                            vm.ImageSource = image;
                            vm.IsLoading = false;
                        }
                    });
                }
            });

            await Task.WhenAll(loadTasks);
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

        private void LoadRequiredItems(List<QuestItem> requiredItems)
        {
            var itemVms = new List<RequiredItemViewModel>();

            foreach (var item in requiredItems)
            {
                if (_isUnloaded) return; // Check if page was unloaded

                // Calculate fulfillment status
                var requiredFir = item.FoundInRaid ? item.Amount : 0;
                var fulfillmentInfo = _inventoryService.GetFulfillmentInfo(
                    item.ItemNormalizedName, item.Amount, requiredFir);
                var isFulfilled = fulfillmentInfo.Status == Models.ItemFulfillmentStatus.Fulfilled;

                var vm = new RequiredItemViewModel
                {
                    FoundInRaid = item.FoundInRaid,
                    RequirementType = item.Requirement,
                    ItemNormalizedName = item.ItemNormalizedName, // For navigation
                    IsFulfilled = isFulfilled
                };

                // Get localized item name (with display name fallback)
                var localizedName = GetLocalizedItemName(item.ItemNormalizedName, item.ItemDisplayName);
                vm.DisplayText = $"{localizedName} x{item.Amount}";

                // Get item icon (with display name for better matching)
                var tarkovItem = GetItemByNormalizedName(item.ItemNormalizedName, item.ItemDisplayName);
                if (tarkovItem?.Id != null)
                {
                    var icon = _imageCache.GetLocalItemIcon(tarkovItem.Id);
                    vm.IconSource = icon;
                }

                itemVms.Add(vm);
            }

            RequiredItemsList.ItemsSource = itemVms;
        }

        /// <summary>
        /// Handle click on item name to navigate to Items tab
        /// </summary>
        private void ItemName_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is RequiredItemViewModel vm)
            {
                if (string.IsNullOrEmpty(vm.ItemNormalizedName)) return;

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.NavigateToItem(vm.ItemNormalizedName);
            }
        }

        private string GetLocalizedItemName(string normalizedName, string? displayNameFallback = null)
        {
            var item = GetItemByNormalizedName(normalizedName, displayNameFallback);
            if (item == null)
                return !string.IsNullOrEmpty(displayNameFallback) ? displayNameFallback : normalizedName;

            return _loc.CurrentLanguage switch
            {
                AppLanguage.KO => item.NameKo ?? item.Name,
                AppLanguage.JA => item.NameJa ?? item.Name,
                _ => item.Name
            };
        }

        private TarkovItem? GetItemByNormalizedName(string normalizedName, string? displayName = null)
        {
            if (_itemLookup == null)
                return null;

            // Strategy 1: Direct lookup
            if (_itemLookup.TryGetValue(normalizedName, out var item))
                return item;

            // Strategy 2: Try alternatives from display name
            if (!string.IsNullOrEmpty(displayName))
            {
                var displayAlternatives = NormalizedNameGenerator.GenerateAlternatives(displayName);
                foreach (var alt in displayAlternatives)
                {
                    if (_itemLookup.TryGetValue(alt, out item))
                        return item;
                }
            }

            // Strategy 3: Try with alternative names from normalized name (fuzzy match)
            var alternatives = NormalizedNameGenerator.GenerateAlternatives(normalizedName);
            foreach (var alt in alternatives)
            {
                if (_itemLookup.TryGetValue(alt, out item))
                    return item;
            }

            return null;
        }

        #endregion

        #region Nested ScrollViewer Scroll Propagation

        /// <summary>
        /// Handle mouse wheel events on nested ScrollViewers to propagate to parent when at scroll limits
        /// </summary>
        private void NestedScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer) return;

            // Check if the nested ScrollViewer can handle this scroll
            var canScrollDown = scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
            var canScrollUp = scrollViewer.VerticalOffset > 0;
            var scrollingDown = e.Delta < 0;
            var scrollingUp = e.Delta > 0;

            // If the nested ScrollViewer cannot scroll in the direction of the wheel,
            // or if there's no scrollable content, propagate to parent
            bool shouldPropagate = false;

            if (scrollViewer.ScrollableHeight <= 0)
            {
                // No scrollable content, propagate to parent
                shouldPropagate = true;
            }
            else if (scrollingDown && !canScrollDown)
            {
                // At bottom, trying to scroll down - propagate
                shouldPropagate = true;
            }
            else if (scrollingUp && !canScrollUp)
            {
                // At top, trying to scroll up - propagate
                shouldPropagate = true;
            }

            if (shouldPropagate)
            {
                // Don't handle the event, let it bubble up to parent ScrollViewer
                e.Handled = false;

                // Manually raise the event on the parent ScrollViewer
                var parentScrollViewer = DetailScrollViewer;
                if (parentScrollViewer != null)
                {
                    var newEventArgs = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                        Source = sender
                    };
                    parentScrollViewer.RaiseEvent(newEventArgs);
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region Quest Recommendations

        private void UpdateRecommendations()
        {
            try
            {
                var recommendationService = QuestRecommendationService.Instance;
                var recommendations = recommendationService.GetRecommendations(5);

                if (recommendations.Count == 0)
                {
                    RecommendationsExpander.Visibility = Visibility.Collapsed;
                    return;
                }

                // Update header text with localization
                TxtRecommendationsHeader.Text = _loc.RecommendedQuests;
                TxtRecommendationCount.Text = recommendations.Count.ToString();
                TxtNoRecommendations.Text = _loc.NoRecommendations;

                // Create view models
                var recommendationVms = recommendations.Select(r => CreateRecommendationViewModel(r)).ToList();

                RecommendationsList.ItemsSource = recommendationVms;
                TxtNoRecommendations.Visibility = Visibility.Collapsed;
                RecommendationsExpander.Visibility = Visibility.Visible;
            }
            catch
            {
                // Hide recommendations section if service is not initialized
                RecommendationsExpander.Visibility = Visibility.Collapsed;
            }
        }

        private RecommendationViewModel CreateRecommendationViewModel(QuestRecommendation rec)
        {
            var (displayName, _, _) = GetLocalizedNames(rec.Quest);

            return new RecommendationViewModel
            {
                Recommendation = rec,
                QuestName = displayName,
                Reason = rec.Reason,
                TypeText = GetRecommendationTypeText(rec.Type),
                TypeBackground = GetRecommendationTypeBrush(rec.Type),
                TraderInitial = GetTraderInitial(rec.Quest.Trader),
                IsKappaRequired = rec.Quest.ReqKappa
            };
        }

        private string GetRecommendationTypeText(RecommendationType type)
        {
            return type switch
            {
                RecommendationType.ReadyToComplete => _loc.ReadyToComplete,
                RecommendationType.ItemHandInOnly => _loc.ItemHandInOnly,
                RecommendationType.KappaPriority => _loc.KappaPriority,
                RecommendationType.UnlocksMany => _loc.UnlocksMany,
                RecommendationType.EasyQuest => _loc.EasyQuest,
                _ => "Unknown"
            };
        }

        private static Brush GetRecommendationTypeBrush(RecommendationType type)
        {
            return type switch
            {
                RecommendationType.ReadyToComplete => ReadyToCompleteBrush,
                RecommendationType.ItemHandInOnly => ItemHandInBrush,
                RecommendationType.KappaPriority => KappaPriorityBrush,
                RecommendationType.UnlocksMany => UnlocksManyBrush,
                RecommendationType.EasyQuest => EasyQuestBrush,
                _ => Brushes.Gray
            };
        }

        private void Recommendation_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is RecommendationViewModel vm)
            {
                // Navigate to the quest in the list
                var questNormalizedName = vm.Recommendation.Quest.NormalizedName;
                if (!string.IsNullOrEmpty(questNormalizedName))
                {
                    SelectQuestInternal(questNormalizedName);
                }
            }
        }

        #endregion

        #region Prerequisite Quest Navigation

        /// <summary>
        /// Handle click on prerequisite quest name to navigate to that quest
        /// </summary>
        private void PrerequisiteQuest_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is PrerequisiteItemViewModel vm)
            {
                if (vm.Task?.NormalizedName != null)
                {
                    SelectQuestInternal(vm.Task.NormalizedName);
                }
            }
        }

        #endregion
    }
}
