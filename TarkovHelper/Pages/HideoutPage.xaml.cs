using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Pages
{
    /// <summary>
    /// Hideout module view model for display
    /// </summary>
    public class HideoutModuleViewModel
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
        public BitmapImage? IconSource { get; set; }
    }

    /// <summary>
    /// Requirement view model for display
    /// </summary>
    public class RequirementViewModel
    {
        public string DisplayText { get; set; } = string.Empty;
        public BitmapImage? IconSource { get; set; }
        public bool FoundInRaid { get; set; }
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;

        // For mixed FIR/non-FIR display
        public int TotalCount { get; set; }
        public int FIRCount { get; set; }
        public bool HasMixedFIR => FIRCount > 0 && FIRCount < TotalCount;

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
    }

    public partial class HideoutPage : UserControl
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;
        private readonly HideoutProgressService _progressService = HideoutProgressService.Instance;
        private readonly ImageCacheService _imageCache = ImageCacheService.Instance;
        private List<HideoutModuleViewModel> _allModuleViewModels = new();
        private bool _isInitializing = true;

        public HideoutPage()
        {
            InitializeComponent();
            _loc.LanguageChanged += OnLanguageChanged;
            _progressService.ProgressChanged += OnProgressChanged;

            Loaded += HideoutPage_Loaded;
        }

        private async void HideoutPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Show loading overlay
            LoadingOverlay.Visibility = Visibility.Visible;
            MainContent.Visibility = Visibility.Collapsed;

            try
            {
                await LoadModulesAsync();
                _isInitializing = false;
                ApplyFilters();
                UpdateStatistics();
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

        private async Task LoadModulesAsync()
        {
            var modules = _progressService.AllModules;

            _allModuleViewModels = new List<HideoutModuleViewModel>();

            foreach (var module in modules)
            {
                var vm = CreateModuleViewModel(module);

                // Load icon asynchronously
                if (!string.IsNullOrEmpty(module.ImageLink))
                {
                    var icon = await _imageCache.GetImageAsync(module.ImageLink, "hideout");
                    vm.IconSource = icon;
                }

                _allModuleViewModels.Add(vm);
            }
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
                        var vm = new RequirementViewModel
                        {
                            DisplayText = $"{itemName} x{itemReq.Count}",
                            FoundInRaid = itemReq.FoundInRaid
                        };

                        if (!string.IsNullOrEmpty(itemReq.IconLink))
                        {
                            vm.IconSource = await _imageCache.GetItemIconAsync(itemReq.IconLink);
                        }

                        itemVms.Add(vm);
                    }
                    NextLevelItemsList.ItemsSource = itemVms;
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

                    var vm = new RequirementViewModel
                    {
                        DisplayText = RequirementViewModel.FormatCountDisplay(itemName, totalCount, firCount),
                        TotalCount = totalCount,
                        FIRCount = firCount,
                        // Only show FIR badge if ALL items are FIR (not mixed)
                        FoundInRaid = firCount > 0 && firCount == totalCount
                    };

                    if (!string.IsNullOrEmpty(kvp.Value.Item.IconLink))
                    {
                        vm.IconSource = await _imageCache.GetItemIconAsync(kvp.Value.Item.IconLink);
                    }

                    totalVms.Add(vm);
                }
                TotalRemainingItemsList.ItemsSource = totalVms;
            }
            else
            {
                TotalRemainingItemsList.ItemsSource = new[] { new RequirementViewModel { DisplayText = "All items collected!" } };
            }
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
    }
}
