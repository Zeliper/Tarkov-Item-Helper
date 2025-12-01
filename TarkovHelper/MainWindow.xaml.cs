using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper;

public partial class MainWindow : Window
{
    private readonly UserProgressManager _progressManager = new();
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly LogsWatcher _logsWatcher = new();
    private List<RequiredItemSummary> _allRequiredItems = [];
    private bool _isLoading;
    private bool _isUpdatingHideoutLevel;
    private string? _selectedQuestWikiLink;

    // Windows API for dark title bar
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();
        _loc.LanguageChanged += OnLanguageChanged;
        _logsWatcher.QuestCompleted += OnQuestCompletedFromLog;
        _logsWatcher.StatusChanged += OnLogWatcherStatusChanged;

        // Apply dark title bar
        SourceInitialized += (s, e) => EnableDarkTitleBar();
    }

    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var useDarkMode = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
    }

    private void OnLanguageChanged(object? sender, AppLanguage e)
    {
        // Update all UI text when language changes
        UpdateAllLocalizedText();
        RefreshAllViews();
    }

    private void UpdateAllLocalizedText()
    {
        // Header
        TxtAppSubtitle.Text = _loc.AppSubtitle;
        BtnSettings.Content = _loc.Settings;
        BtnRefreshData.Content = _loc.RefreshData;
        BtnResetProgress.Content = _loc.ResetProgress;

        // Tabs
        TabQuests.Header = _loc.TabQuests;
        TabHideout.Header = _loc.TabHideout;
        TabRequiredItems.Header = _loc.TabRequiredItems;

        // Quest Tab
        TxtQuestSearch.Tag = _loc.SearchQuestsPlaceholder;
        ChkHideCompleted.Content = _loc.HideCompleted;
        TxtSearchAndComplete.Text = _loc.SearchAndComplete;
        TxtSearchAndCompleteDesc.Text = _loc.SearchAndCompleteDesc;
        BtnSearchQuest.Content = _loc.SearchQuest;

        // Hideout Tab
        TxtCurrentLevel.Text = _loc.CurrentLevel;

        // Required Items Tab
        TxtTotalItemsLabel.Text = _loc.TotalItems;
        TxtQuestItemsLabel.Text = _loc.QuestItems;
        TxtHideoutItemsLabel.Text = _loc.HideoutItems;
        TxtFirItemsLabel.Text = _loc.FirRequired;
        TxtItemSearch.Tag = _loc.SearchItemsPlaceholder;
        ChkShowFirOnly.Content = _loc.FirOnly;
        ChkShowQuestOnly.Content = _loc.QuestItemsFilter;
        ChkShowHideoutOnly.Content = _loc.HideoutItemsFilter;

        // Reset detail panels
        if (LstQuests.SelectedItem == null)
            TxtQuestDetailTitle.Text = _loc.SelectQuest;
        if (LstHideout.SelectedItem == null)
            TxtHideoutDetailTitle.Text = _loc.SelectStation;
        if (LstRequiredItems.SelectedItem == null)
            TxtItemDetailTitle.Text = _loc.SelectItem;

        // Update log status text
        UpdateLogStatusDisplay();
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            _loc.CurrentLanguage = lang switch
            {
                "KO" => AppLanguage.KO,
                "JA" => AppLanguage.JA,
                _ => AppLanguage.EN
            };
        }
    }

    private void BtnBuyMeACoffee_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://buymeacoffee.com/zeliperstap",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open link: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // 저장된 언어 설정을 UI에 반영
        CmbLanguage.SelectedIndex = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => 1,
            AppLanguage.JA => 2,
            _ => 0
        };

        await LoadAllDataAsync();

        // 게임 경로 자동 감지 및 설정
        await InitializeGamePathAsync();
    }

    private async Task InitializeGamePathAsync()
    {
        // Try to detect game path automatically
        var gameFolder = GameEnv.GameFolder;
        var isValid = GameEnv.IsValidTarkovFolder(gameFolder);

        if (!string.IsNullOrEmpty(gameFolder) && isValid)
        {
            // Path found automatically, start monitoring
            var method = GameEnv.DetectionMethod ?? "Saved";
            TxtStatus.Text = _loc.FormatGamePathDetected(method, gameFolder);
            _logsWatcher.Start();
            UpdateLogStatusDisplay();
            return;
        }

        // Path not found or invalid, show dialog
        await Task.Delay(100); // Allow UI to fully load
        ShowGamePathNotFoundDialog();
    }

    private void ShowGamePathNotFoundDialog()
    {
        var result = MessageBox.Show(
            _loc.GamePathNotFoundMessage,
            _loc.GamePathNotFoundTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            ShowGamePathSelectionDialog();
        }
        else
        {
            // User declined, show warning in status
            TxtStatus.Text = _loc.LogMonitoringDisabled;
            UpdateLogStatusDisplay();
        }
    }

    private void ShowGamePathSelectionDialog()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = _loc.SelectGameFolder
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedPath = dialog.FolderName;

            if (GameEnv.IsValidTarkovFolder(selectedPath))
            {
                GameEnv.GameFolder = selectedPath;
                TxtStatus.Text = _loc.FormatGamePathSet(selectedPath);
                _logsWatcher.Start();
                UpdateLogStatusDisplay();
            }
            else
            {
                // Selected folder is not valid
                var retry = MessageBox.Show(
                    _loc.InvalidGameFolderRetry,
                    _loc.Error,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (retry == MessageBoxResult.Yes)
                {
                    ShowGamePathSelectionDialog();
                }
                else
                {
                    TxtStatus.Text = _loc.LogMonitoringDisabled;
                    UpdateLogStatusDisplay();
                }
            }
        }
        else
        {
            TxtStatus.Text = _loc.LogMonitoringDisabled;
            UpdateLogStatusDisplay();
        }
    }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _logsWatcher.Stop();
        _logsWatcher.Dispose();
        await _progressManager.SaveProgressAsync();
    }

    private async Task LoadAllDataAsync()
    {
        _isLoading = true;
        TxtStatus.Text = _loc.LoadingData;

        try
        {
            await _progressManager.LoadAllAsync();

            UpdateAllLocalizedText();
            RefreshAllViews();
            TxtStatus.Text = _loc.DataLoadedSuccessfully;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{_loc.FailedToLoadData}: {ex.Message}", _loc.Error,
                MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = _loc.FailedToLoadData;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RefreshAllViews()
    {
        RefreshQuestList();
        RefreshHideoutList();
        RefreshRequiredItems();
        UpdateStatusBar();
    }

    #region Quest Tab

    private void RefreshQuestList()
    {
        var searchText = TxtQuestSearch?.Text?.Trim();
        var hideCompleted = ChkHideCompleted?.IsChecked ?? false;

        var quests = _progressManager.GetQuestViewModels(searchText);

        // 아이템이 필요한 퀘스트만 표시 (아이템이 없는 퀘스트는 숨김)
        quests = quests.Where(q => q.RequiredItemCount > 0).ToList();

        if (hideCompleted)
        {
            quests = quests.Where(q => !q.IsCompleted).ToList();
        }

        LstQuests.ItemsSource = quests;
    }

    private void TxtQuestSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        RefreshQuestList();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        RefreshQuestList();
    }

    private void LstQuests_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstQuests.SelectedItem is not QuestViewModel vm)
        {
            ClearQuestDetails();
            return;
        }

        ShowQuestDetails(vm.Quest);
    }

    private void ShowQuestDetails(TaskData quest)
    {
        var primaryName = _loc.GetDisplayName(quest.NameEn, quest.NameKo);
        var secondaryName = _loc.GetSecondaryName(quest.NameEn, quest.NameKo);

        TxtQuestDetailTitle.Text = primaryName;
        TxtQuestDetailSubtitle.Text = $"{secondaryName}\n{quest.TraderName} | {_loc.Level} {quest.MinPlayerLevel}\nID: {quest.Id}";

        // Wiki 버튼 표시
        _selectedQuestWikiLink = quest.WikiLink;
        BtnQuestWiki.Visibility = !string.IsNullOrEmpty(quest.WikiLink)
            ? Visibility.Visible
            : Visibility.Collapsed;

        QuestObjectivesPanel.Children.Clear();

        // Prerequisites section
        if (quest.PrerequisiteTaskIds.Count > 0)
        {
            AddSectionHeader(_loc.Prerequisites);
            foreach (var prereqId in quest.PrerequisiteTaskIds)
            {
                var prereq = _progressManager.GetQuest(prereqId);
                if (prereq != null)
                {
                    var isCompleted = _progressManager.IsQuestCompleted(prereqId);
                    AddPrerequisiteItem(prereq, isCompleted);
                }
            }
        }

        // Objectives section
        if (quest.Objectives.Count > 0)
        {
            AddSectionHeader(_loc.Objectives);
            foreach (var obj in quest.Objectives)
            {
                AddObjectiveItem(obj);
            }
        }
    }

    private void ClearQuestDetails()
    {
        TxtQuestDetailTitle.Text = _loc.SelectQuest;
        TxtQuestDetailSubtitle.Text = "";
        QuestObjectivesPanel.Children.Clear();
        _selectedQuestWikiLink = null;
        BtnQuestWiki.Visibility = Visibility.Collapsed;
    }

    private void BtnQuestWiki_Click(object sender, RoutedEventArgs e)
    {
        OpenWikiLink(_selectedQuestWikiLink);
    }

    private void BtnItemWiki_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string wikiLink)
        {
            OpenWikiLink(wikiLink);
        }
    }

    private static void OpenWikiLink(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open wiki: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddSectionHeader(string title)
    {
        var header = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 12, 0, 8)
        };
        QuestObjectivesPanel.Children.Add(header);
    }

    private void AddPrerequisiteItem(TaskData prereq, bool isCompleted)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

        var checkMark = new TextBlock
        {
            Text = isCompleted ? "[OK]" : "[ ]",
            Foreground = isCompleted ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("TextSecondaryBrush"),
            Width = 30
        };
        panel.Children.Add(checkMark);

        var primaryName = _loc.GetDisplayName(prereq.NameEn, prereq.NameKo);
        var name = new TextBlock
        {
            Text = $"{primaryName} ({prereq.TraderName})",
            TextDecorations = isCompleted ? TextDecorations.Strikethrough : null,
            Foreground = isCompleted ? (Brush)FindResource("TextSecondaryBrush") : (Brush)FindResource("TextPrimaryBrush")
        };
        panel.Children.Add(name);

        QuestObjectivesPanel.Children.Add(panel);
    }

    private void AddObjectiveItem(TaskObjective obj)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("BackgroundLightBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 4)
        };

        var panel = new StackPanel();

        // Description
        var desc = new TextBlock
        {
            Text = obj.Description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };
        panel.Children.Add(desc);

        // Items if any
        if (obj.Items.Count > 0)
        {
            foreach (var item in obj.Items)
            {
                var itemData = _progressManager.GetItem(item.ItemId);
                var itemPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };

                // Icon placeholder
                var iconBorder = new Border
                {
                    Width = 24,
                    Height = 24,
                    Background = (Brush)FindResource("BackgroundMediumBrush"),
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(0, 0, 8, 0)
                };

                if (itemData?.IconLink != null)
                {
                    var img = new Image { Stretch = Stretch.Uniform };
                    iconBorder.Child = img;
                    LoadImageAsync(img, itemData.IconLink);
                }

                itemPanel.Children.Add(iconBorder);

                var itemDisplayName = itemData != null
                    ? _loc.GetDisplayName(itemData.NameEn, itemData.NameKo)
                    : item.ItemId;
                var itemName = new TextBlock
                {
                    Text = itemDisplayName,
                    VerticalAlignment = VerticalAlignment.Center
                };
                itemPanel.Children.Add(itemName);

                var countText = new TextBlock
                {
                    Text = $" x{item.Count}",
                    Foreground = (Brush)FindResource("AccentBrush"),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                };
                itemPanel.Children.Add(countText);

                if (item.FoundInRaid)
                {
                    var firBadge = new Border
                    {
                        Background = (Brush)FindResource("FirBrush"),
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(4, 2, 4, 2),
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    firBadge.Child = new TextBlock
                    {
                        Text = "FIR",
                        FontSize = 9,
                        Foreground = Brushes.White
                    };
                    itemPanel.Children.Add(firBadge);
                }

                panel.Children.Add(itemPanel);
            }
        }

        border.Child = panel;
        QuestObjectivesPanel.Children.Add(border);
    }

    private async void QuestCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (sender is not CheckBox cb) return;
        if (cb.Tag is not string questId) return;

        if (cb.IsChecked == true)
        {
            _progressManager.CompleteQuest(questId, autoCompletePrerequites: true);
        }
        else
        {
            _progressManager.UncompleteQuest(questId);
        }

        await _progressManager.SaveProgressAsync();
        RefreshAllViews();
    }

    private void BtnQuestItemWiki_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string wikiLink)
        {
            OpenWikiLink(wikiLink);
        }
    }

    private void BtnSearchQuest_Click(object sender, RoutedEventArgs e)
    {
        var dataset = _progressManager.TaskDataset;
        if (dataset == null) return;

        // 검색 다이얼로그 생성
        var searchWindow = new Window
        {
            Title = _loc.SearchQuestTitle,
            Width = 500,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)FindResource("BackgroundDarkBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush")
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 검색창
        var searchBox = new TextBox
        {
            Margin = new Thickness(16, 16, 16, 8),
            Tag = _loc.EnterQuestName
        };
        Grid.SetRow(searchBox, 0);
        mainGrid.Children.Add(searchBox);

        // 결과 리스트
        var resultList = new ListBox
        {
            Margin = new Thickness(16, 0, 16, 8),
            Background = (Brush)FindResource("BackgroundMediumBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush")
        };
        Grid.SetRow(resultList, 1);
        mainGrid.Children.Add(resultList);

        // 버튼 영역
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 16)
        };

        var completeButton = new Button
        {
            Content = _loc.SetInProgress,
            Padding = new Thickness(16, 8, 16, 8),
            IsEnabled = false
        };
        buttonPanel.Children.Add(completeButton);

        Grid.SetRow(buttonPanel, 2);
        mainGrid.Children.Add(buttonPanel);

        searchWindow.Content = mainGrid;

        // 검색 로직 - 영어와 한국어 둘 다 검색 가능
        searchBox.TextChanged += (s, args) =>
        {
            var text = searchBox.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                resultList.ItemsSource = null;
                return;
            }

            var results = dataset.Tasks
                .Where(t =>
                    t.NameEn.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    t.NameKo.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    t.TraderName.Contains(text, StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .Select(t => new
                {
                    Quest = t,
                    Display = _loc.IsEnglish
                        ? $"{t.NameEn} ({t.NameKo}) - {t.TraderName}"
                        : $"{t.NameKo} ({t.NameEn}) - {t.TraderName}",
                    IsCompleted = _progressManager.IsQuestCompleted(t.Id)
                })
                .OrderBy(x => x.IsCompleted)
                .ThenBy(x => x.Quest.MinPlayerLevel ?? 0)
                .ToList();

            resultList.ItemsSource = results;
            resultList.DisplayMemberPath = "Display";
        };

        // 선택 변경 시
        resultList.SelectionChanged += (s, args) =>
        {
            completeButton.IsEnabled = resultList.SelectedItem != null;
        };

        // 더블 클릭 시
        resultList.MouseDoubleClick += (s, args) =>
        {
            if (resultList.SelectedItem != null)
            {
                completeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }
        };

        // 완료 버튼 클릭
        completeButton.Click += async (s, args) =>
        {
            if (resultList.SelectedItem == null) return;

            dynamic selected = resultList.SelectedItem;
            var quest = (TaskData)selected.Quest;

            // 선행 퀘스트 목록 가져오기
            var prereqs = TaskDatasetManager.GetAllPrerequisites(dataset, quest.Id);
            var incompletePrereqs = prereqs.Where(id => !_progressManager.IsQuestCompleted(id)).ToList();

            var questDisplayName = _loc.GetDisplayName(quest.NameEn, quest.NameKo);
            var message = _loc.FormatSetInProgressConfirm(quest.NameEn, quest.NameKo) + "\n\n";
            if (incompletePrereqs.Count > 0)
            {
                message += _loc.FormatPrerequisiteCompleteCount(incompletePrereqs.Count) + "\n\n";
                foreach (var prereqId in incompletePrereqs.Take(10))
                {
                    var prereq = _progressManager.GetQuest(prereqId);
                    if (prereq != null)
                    {
                        var prereqName = _loc.GetDisplayName(prereq.NameEn, prereq.NameKo);
                        message += $"  - {prereqName}\n";
                    }
                }
                if (incompletePrereqs.Count > 10)
                {
                    message += _loc.FormatAndMore(incompletePrereqs.Count - 10) + "\n";
                }
            }

            var result = MessageBox.Show(message, _loc.Confirm,
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 선행 퀘스트만 완료
                foreach (var prereqId in prereqs)
                {
                    _progressManager.CompleteQuest(prereqId, autoCompletePrerequites: false);
                }
                // 선택한 퀘스트는 진행중으로 설정
                _progressManager.SetQuestInProgress(quest.Id);

                await _progressManager.SaveProgressAsync();
                RefreshAllViews();
                TxtStatus.Text = _loc.FormatInProgressStatus(questDisplayName, incompletePrereqs.Count);
                searchWindow.Close();
            }
        };

        searchWindow.ShowDialog();
    }

    #endregion

    #region Hideout Tab

    private void RefreshHideoutList()
    {
        var hideouts = _progressManager.GetHideoutViewModels();
        LstHideout.ItemsSource = hideouts;
    }

    private void LstHideout_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstHideout.SelectedItem is not HideoutStationViewModel vm)
        {
            ClearHideoutDetails();
            return;
        }

        ShowHideoutDetails(vm);
    }

    private void ShowHideoutDetails(HideoutStationViewModel vm)
    {
        var primaryName = _loc.GetDisplayName(vm.Station.NameEn, vm.Station.NameKo);
        var secondaryName = _loc.GetSecondaryName(vm.Station.NameEn, vm.Station.NameKo);
        TxtHideoutDetailTitle.Text = $"{primaryName}";
        TxtHideoutDetailSubtitle.Text = secondaryName;

        // Populate level combobox (suppress SelectionChanged event during init)
        _isUpdatingHideoutLevel = true;
        try
        {
            CmbHideoutLevel.Items.Clear();
            for (int i = 0; i <= vm.MaxLevel; i++)
            {
                CmbHideoutLevel.Items.Add(i);
            }
            CmbHideoutLevel.SelectedItem = vm.CurrentLevel;
        }
        finally
        {
            _isUpdatingHideoutLevel = false;
        }

        ShowNextLevelRequirements(vm);
    }

    private void ShowNextLevelRequirements(HideoutStationViewModel vm)
    {
        HideoutRequirementsPanel.Children.Clear();

        if (vm.IsMaxLevel)
        {
            TxtNextLevelTitle.Text = _loc.MaxLevelReached;
            var maxText = new TextBlock
            {
                Text = _loc.StationMaxUpgraded,
                Foreground = (Brush)FindResource("SuccessBrush"),
                FontStyle = FontStyles.Italic
            };
            HideoutRequirementsPanel.Children.Add(maxText);
            return;
        }

        TxtNextLevelTitle.Text = _loc.FormatLevelRequirement(vm.CurrentLevel + 1);

        var nextLevel = vm.NextLevel;
        if (nextLevel == null) return;

        // Station requirements
        if (nextLevel.StationLevelRequirements.Count > 0)
        {
            AddHideoutSectionHeader(_loc.RequiredStations);
            foreach (var req in nextLevel.StationLevelRequirements)
            {
                var currentLevel = _progressManager.GetHideoutLevel(req.StationId);
                var isMet = currentLevel >= req.Level;
                var stationName = _loc.GetDisplayName(req.StationNameEn, req.StationNameKo);
                AddRequirementItem($"{stationName} Lv.{req.Level}", isMet);
            }
        }

        // Trader requirements
        if (nextLevel.TraderRequirements.Count > 0)
        {
            AddHideoutSectionHeader(_loc.RequiredTraders);
            foreach (var req in nextLevel.TraderRequirements)
            {
                var traderName = _loc.GetDisplayName(req.TraderNameEn, req.TraderNameKo);
                AddRequirementItem($"{traderName} LL{req.Level}", false);
            }
        }

        // Item requirements
        if (nextLevel.ItemRequirements.Count > 0)
        {
            AddHideoutSectionHeader(_loc.RequiredItems);
            foreach (var req in nextLevel.ItemRequirements)
            {
                var itemData = _progressManager.GetItem(req.ItemId);
                AddItemRequirementRow(req, itemData);
            }
        }
    }

    private void AddHideoutSectionHeader(string title)
    {
        var header = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 12, 0, 8)
        };
        HideoutRequirementsPanel.Children.Add(header);
    }

    private void AddRequirementItem(string text, bool isMet)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };

        var checkMark = new TextBlock
        {
            Text = isMet ? "[OK]" : "[ ]",
            Foreground = isMet ? (Brush)FindResource("SuccessBrush") : (Brush)FindResource("WarningBrush"),
            Width = 30
        };
        panel.Children.Add(checkMark);

        var name = new TextBlock
        {
            Text = text,
            Foreground = isMet ? (Brush)FindResource("TextSecondaryBrush") : (Brush)FindResource("TextPrimaryBrush")
        };
        panel.Children.Add(name);

        HideoutRequirementsPanel.Children.Add(panel);
    }

    private void AddItemRequirementRow(HideoutItemRequirement req, ItemData? itemData)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("BackgroundLightBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 4, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon
        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            Background = (Brush)FindResource("BackgroundMediumBrush"),
            CornerRadius = new CornerRadius(2)
        };

        if (itemData?.IconLink != null)
        {
            var img = new Image { Stretch = Stretch.Uniform };
            iconBorder.Child = img;
            LoadImageAsync(img, itemData.IconLink);
        }

        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        // Name - localized
        var primaryName = itemData != null
            ? _loc.GetDisplayName(itemData.NameEn, itemData.NameKo)
            : _loc.GetDisplayName(req.ItemNameEn, req.ItemNameKo);
        var secondaryName = itemData != null
            ? _loc.GetSecondaryName(itemData.NameEn, itemData.NameKo)
            : _loc.GetSecondaryName(req.ItemNameEn, req.ItemNameKo);

        var namePanel = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(new TextBlock { Text = primaryName, FontWeight = FontWeights.SemiBold });
        namePanel.Children.Add(new TextBlock
        {
            Text = secondaryName,
            FontSize = 10,
            Foreground = (Brush)FindResource("TextSecondaryBrush")
        });
        Grid.SetColumn(namePanel, 1);
        grid.Children.Add(namePanel);

        // Count + FIR
        var countPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        countPanel.Children.Add(new TextBlock
        {
            Text = $"x{req.Count}",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("AccentBrush")
        });

        if (req.FoundInRaid)
        {
            var firBadge = new Border
            {
                Background = (Brush)FindResource("FirBrush"),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            firBadge.Child = new TextBlock
            {
                Text = "FIR",
                FontSize = 9,
                Foreground = Brushes.White
            };
            countPanel.Children.Add(firBadge);
        }

        Grid.SetColumn(countPanel, 2);
        grid.Children.Add(countPanel);

        border.Child = grid;
        HideoutRequirementsPanel.Children.Add(border);
    }

    private void ClearHideoutDetails()
    {
        TxtHideoutDetailTitle.Text = _loc.SelectStation;
        TxtHideoutDetailSubtitle.Text = "";
        CmbHideoutLevel.Items.Clear();
        HideoutRequirementsPanel.Children.Clear();
    }

    private async void CmbHideoutLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _isUpdatingHideoutLevel) return;
        if (LstHideout.SelectedItem is not HideoutStationViewModel vm) return;
        if (CmbHideoutLevel.SelectedItem is not int level) return;

        // 현재 레벨과 같으면 저장 불필요
        if (vm.CurrentLevel == level) return;

        _progressManager.SetHideoutLevel(vm.Station.Id, level);
        await _progressManager.SaveProgressAsync();

        // 현재 선택된 스테이션 ID 저장
        var selectedStationId = vm.Station.Id;

        // Refresh the list to update display
        RefreshHideoutList();
        RefreshRequiredItems();
        UpdateStatusBar();

        // 선택 복원
        var newVm = (LstHideout.ItemsSource as IEnumerable<HideoutStationViewModel>)?
            .FirstOrDefault(h => h.Station.Id == selectedStationId);
        if (newVm != null)
        {
            LstHideout.SelectedItem = newVm;
        }
    }

    #endregion

    #region Required Items Tab

    private void RefreshRequiredItems()
    {
        _allRequiredItems = _progressManager.GetAllRequiredItems();
        ApplyItemFilters();
        UpdateItemSummary();
    }

    private void ApplyItemFilters()
    {
        var searchText = TxtItemSearch?.Text?.Trim()?.ToLowerInvariant() ?? "";
        var firOnly = ChkShowFirOnly?.IsChecked ?? false;
        var questOnly = ChkShowQuestOnly?.IsChecked ?? false;
        var hideoutOnly = ChkShowHideoutOnly?.IsChecked ?? false;

        var filtered = _allRequiredItems.AsEnumerable();

        if (!string.IsNullOrEmpty(searchText))
        {
            filtered = filtered.Where(i =>
                i.ItemNameEn.ToLowerInvariant().Contains(searchText) ||
                i.ItemNameKo.ToLowerInvariant().Contains(searchText));
        }

        if (firOnly)
        {
            filtered = filtered.Where(i => i.RequiresFir);
        }

        if (questOnly)
        {
            filtered = filtered.Where(i => i.QuestNormalCount > 0 || i.QuestFirCount > 0);
        }

        if (hideoutOnly)
        {
            filtered = filtered.Where(i => i.HideoutNormalCount > 0 || i.HideoutFirCount > 0);
        }

        LstRequiredItems.ItemsSource = filtered.ToList();
    }

    private void UpdateItemSummary()
    {
        var totalItems = _allRequiredItems.Sum(i => i.TotalCount);
        var questItems = _allRequiredItems.Sum(i => i.QuestNormalCount + i.QuestFirCount);
        var hideoutItems = _allRequiredItems.Sum(i => i.HideoutNormalCount + i.HideoutFirCount);
        var firItems = _allRequiredItems.Sum(i => i.TotalFirCount);

        TxtTotalItems.Text = totalItems.ToString();
        TxtQuestItems.Text = questItems.ToString();
        TxtHideoutItems.Text = hideoutItems.ToString();
        TxtFirItems.Text = firItems.ToString();
    }

    private void TxtItemSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading) return;
        ApplyItemFilters();
    }

    private void ItemFilterChanged(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        ApplyItemFilters();
    }

    private void LstRequiredItems_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstRequiredItems.SelectedItem is not RequiredItemSummary item)
        {
            ClearItemDetails();
            return;
        }

        ShowItemDetails(item);
    }

    private void ShowItemDetails(RequiredItemSummary item)
    {
        var primaryName = _loc.GetDisplayName(item.ItemNameEn, item.ItemNameKo);
        var secondaryName = _loc.GetSecondaryName(item.ItemNameEn, item.ItemNameKo);

        TxtItemDetailTitle.Text = primaryName;
        TxtItemDetailSubtitle.Text = $"{secondaryName}\n{_loc.FormatTotalDetails(item.TotalCount, item.QuestNormalCount + item.QuestFirCount, item.HideoutNormalCount + item.HideoutFirCount)}";

        ItemSourcesPanel.Children.Clear();

        // Quest Sources
        if (item.QuestSourceDetails.Count > 0)
        {
            AddItemSectionHeader(_loc.Quests);
            foreach (var source in item.QuestSourceDetails)
            {
                AddQuestSourceItem(source);
            }
        }

        // Hideout Sources
        if (item.HideoutSourceDetails.Count > 0)
        {
            AddItemSectionHeader(_loc.Hideout);
            foreach (var source in item.HideoutSourceDetails)
            {
                AddHideoutSourceItem(source);
            }
        }
    }

    private void ClearItemDetails()
    {
        TxtItemDetailTitle.Text = _loc.SelectItem;
        TxtItemDetailSubtitle.Text = "";
        ItemSourcesPanel.Children.Clear();
    }

    private void AddItemSectionHeader(string title)
    {
        var header = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("AccentBrush"),
            Margin = new Thickness(0, 12, 0, 8)
        };
        ItemSourcesPanel.Children.Add(header);
    }

    private void AddQuestSourceItem(ItemQuestSource source)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("BackgroundLightBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Quest info - localized
        var primaryName = _loc.GetDisplayName(source.QuestNameEn, source.QuestNameKo);
        var secondaryName = _loc.GetSecondaryName(source.QuestNameEn, source.QuestNameKo);

        var infoPanel = new StackPanel();
        infoPanel.Children.Add(new TextBlock
        {
            Text = primaryName,
            FontWeight = FontWeights.SemiBold
        });

        var subPanel = new StackPanel { Orientation = Orientation.Horizontal };
        subPanel.Children.Add(new TextBlock
        {
            Text = secondaryName,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 11
        });
        infoPanel.Children.Add(subPanel);

        var detailPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        detailPanel.Children.Add(new TextBlock
        {
            Text = source.TraderName,
            Foreground = (Brush)FindResource("AccentBrush"),
            FontSize = 10
        });
        detailPanel.Children.Add(new TextBlock
        {
            Text = $" | x{source.Count}",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 10
        });
        if (source.FoundInRaid)
        {
            var firBadge = new Border
            {
                Background = (Brush)FindResource("FirBrush"),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            firBadge.Child = new TextBlock
            {
                Text = "FIR",
                FontSize = 8,
                Foreground = Brushes.White
            };
            detailPanel.Children.Add(firBadge);
        }
        infoPanel.Children.Add(detailPanel);

        Grid.SetColumn(infoPanel, 0);
        grid.Children.Add(infoPanel);

        // Wiki button
        if (!string.IsNullOrEmpty(source.WikiLink))
        {
            var wikiBtn = new Button
            {
                Content = "Wiki",
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 10,
                Tag = source.WikiLink,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0)
            };
            wikiBtn.Click += BtnItemWiki_Click;
            Grid.SetColumn(wikiBtn, 2);
            grid.Children.Add(wikiBtn);
        }

        border.Child = grid;
        ItemSourcesPanel.Children.Add(border);
    }

    private void AddHideoutSourceItem(ItemHideoutSource source)
    {
        var border = new Border
        {
            Background = (Brush)FindResource("BackgroundLightBrush"),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Hideout info - localized
        var primaryName = _loc.GetDisplayName(source.StationNameEn, source.StationNameKo);
        var secondaryName = _loc.GetSecondaryName(source.StationNameEn, source.StationNameKo);

        var infoPanel = new StackPanel();

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
        titlePanel.Children.Add(new TextBlock
        {
            Text = $"{primaryName} Lv.{source.Level}",
            FontWeight = FontWeights.SemiBold
        });
        infoPanel.Children.Add(titlePanel);

        infoPanel.Children.Add(new TextBlock
        {
            Text = $"{secondaryName} {_loc.FormatLevelDisplay(source.Level)}",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 11
        });

        var detailPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        detailPanel.Children.Add(new TextBlock
        {
            Text = $"x{source.Count}",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 10
        });
        if (source.FoundInRaid)
        {
            var firBadge = new Border
            {
                Background = (Brush)FindResource("FirBrush"),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            firBadge.Child = new TextBlock
            {
                Text = "FIR",
                FontSize = 8,
                Foreground = Brushes.White
            };
            detailPanel.Children.Add(firBadge);
        }
        infoPanel.Children.Add(detailPanel);

        Grid.SetColumn(infoPanel, 0);
        grid.Children.Add(infoPanel);

        border.Child = grid;
        ItemSourcesPanel.Children.Add(border);
    }

    #endregion

    #region Header Buttons

    private async void BtnRefreshData_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            _loc.RefreshDataConfirm,
            _loc.RefreshData,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _isLoading = true;
        TxtStatus.Text = _loc.FetchingDataFromApi;

        try
        {
            await TaskDatasetManager.FetchAndSaveAllAsync();
            await LoadAllDataAsync();
            TxtStatus.Text = _loc.DataRefreshedSuccessfully;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{_loc.FailedToRefreshData}: {ex.Message}", _loc.Error,
                MessageBoxButton.OK, MessageBoxImage.Error);
            TxtStatus.Text = _loc.FailedToRefreshData;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async void BtnResetProgress_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            _loc.ResetProgressConfirm,
            _loc.ResetProgress,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _progressManager.ResetProgress();
        await _progressManager.SaveProgressAsync();
        RefreshAllViews();
        TxtStatus.Text = _loc.ProgressReset;
    }

    #endregion

    #region Image Loading

    /// <summary>
    /// 이미지를 비동기로 로드하여 Image 컨트롤에 설정
    /// </summary>
    private static async void LoadImageAsync(Image imageControl, string url)
    {
        try
        {
            var bitmap = await Services.ImageCacheService.GetImageAsync(url);
            if (bitmap != null)
            {
                imageControl.Dispatcher.Invoke(() =>
                {
                    imageControl.Source = bitmap;
                });
            }
        }
        catch
        {
            // 이미지 로드 실패 시 무시
        }
    }

    #endregion

    #region Status Bar

    private void UpdateStatusBar()
    {
        var dataset = _progressManager.TaskDataset;
        var hideoutDataset = _progressManager.HideoutDataset;

        if (dataset != null)
        {
            var completed = _progressManager.Progress.CompletedQuestIds.Count;
            var total = dataset.Tasks.Count;
            TxtQuestProgress.Text = _loc.FormatQuestProgress(completed, total);
        }

        if (hideoutDataset != null)
        {
            var totalLevels = hideoutDataset.Hideouts.Sum(h => h.Levels.Max(l => l.Level));
            var currentLevels = _progressManager.Progress.HideoutLevels.Values.Sum();
            TxtHideoutProgress.Text = _loc.FormatHideoutProgress(currentLevels, totalLevels);
        }
    }

    #endregion

    #region Log Monitoring

    private void OnQuestCompletedFromLog(object? sender, QuestCompletedEventArgs e)
    {
        // UI 스레드에서 실행
        Dispatcher.Invoke(async () =>
        {
            // "success" 상태인 경우만 처리
            if (!e.Status.Equals("success", StringComparison.OrdinalIgnoreCase) &&
                !e.Status.Contains("success", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var completed = _progressManager.TryCompleteQuestFromLog(e.QuestId);
            if (completed)
            {
                await _progressManager.SaveProgressAsync();
                RefreshAllViews();

                var quest = _progressManager.GetQuest(e.QuestId);
                var questName = quest != null
                    ? _loc.GetDisplayName(quest.NameEn, quest.NameKo)
                    : e.QuestId;

                TxtStatus.Text = _loc.FormatQuestCompletedFromLog(questName);
            }
        });
    }

    private void OnLogWatcherStatusChanged(object? sender, LogWatcherStatus status)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateLogStatusDisplay();
        });
    }

    private void UpdateLogStatusDisplay()
    {
        switch (_logsWatcher.Status)
        {
            case LogWatcherStatus.Monitoring:
                LogStatusIndicator.Fill = (Brush)FindResource("SuccessBrush");
                TxtLogStatus.Text = _loc.LogStatusMonitoring;
                LogStatusTooltip.Content = _loc.LogStatusTooltipMonitoring;
                break;

            case LogWatcherStatus.Error:
                LogStatusIndicator.Fill = (Brush)FindResource("FirBrush");
                TxtLogStatus.Text = _loc.LogStatusError;
                LogStatusTooltip.Content = _logsWatcher.StatusMessage ?? _loc.LogStatusTooltipError;
                break;

            case LogWatcherStatus.NotStarted:
            default:
                LogStatusIndicator.Fill = (Brush)FindResource("TextSecondaryBrush");
                TxtLogStatus.Text = _loc.LogStatusNotStarted;
                LogStatusTooltip.Content = _loc.LogStatusTooltipNotStarted;
                break;
        }
    }

    private void LogStatusBorder_Click(object sender, MouseButtonEventArgs e)
    {
        ShowSettingsDialog();
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsDialog();
    }

    private void ShowSettingsDialog()
    {
        var settingsWindow = new Window
        {
            Title = _loc.Settings,
            Width = 600,
            Height = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = (Brush)FindResource("BackgroundDarkBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            ResizeMode = ResizeMode.NoResize
        };

        var mainGrid = new Grid { Margin = new Thickness(16) };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Title
        var titleText = new TextBlock
        {
            Text = _loc.LogPathSettings,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(titleText, 0);
        mainGrid.Children.Add(titleText);

        // Game Folder
        var gameFolderGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        gameFolderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        gameFolderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        gameFolderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var gameFolderLabel = new TextBlock
        {
            Text = _loc.GameFolder,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(gameFolderLabel, 0);
        gameFolderGrid.Children.Add(gameFolderLabel);

        var gameFolderTextBox = new TextBox
        {
            Text = GameEnv.GameFolder ?? "",
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(gameFolderTextBox, 1);
        gameFolderGrid.Children.Add(gameFolderTextBox);

        var browseButton = new Button
        {
            Content = _loc.Browse,
            Padding = new Thickness(12, 6, 12, 6)
        };
        browseButton.Click += (s, e) =>
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = _loc.SelectGameFolder
            };

            if (dialog.ShowDialog() == true)
            {
                gameFolderTextBox.Text = dialog.FolderName;
            }
        };
        Grid.SetColumn(browseButton, 2);
        gameFolderGrid.Children.Add(browseButton);

        Grid.SetRow(gameFolderGrid, 1);
        mainGrid.Children.Add(gameFolderGrid);

        // Logs Folder (read-only display)
        var logsFolderGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        logsFolderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        logsFolderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var logsFolderLabel = new TextBlock
        {
            Text = _loc.LogsFolder,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(logsFolderLabel, 0);
        logsFolderGrid.Children.Add(logsFolderLabel);

        var logsFolderDisplay = new TextBlock
        {
            Text = GameEnv.LogsFolder ?? "N/A",
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(logsFolderDisplay, 1);
        logsFolderGrid.Children.Add(logsFolderDisplay);

        Grid.SetRow(logsFolderGrid, 2);
        mainGrid.Children.Add(logsFolderGrid);

        // Status indicator
        var statusPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var statusIndicator = new Ellipse
        {
            Width = 12,
            Height = 12,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var statusText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        void UpdateStatus()
        {
            var folder = gameFolderTextBox.Text;
            string? logsPath = null;

            if (!string.IsNullOrEmpty(folder))
            {
                // Check Steam version first (build/Logs)
                var steamLogsPath = System.IO.Path.Combine(folder, "build", "Logs");
                var bsgLogsPath = System.IO.Path.Combine(folder, "Logs");
                var buildFolder = System.IO.Path.Combine(folder, "build");

                if (System.IO.Directory.Exists(steamLogsPath))
                {
                    logsPath = steamLogsPath;
                }
                else if (System.IO.Directory.Exists(bsgLogsPath))
                {
                    logsPath = bsgLogsPath;
                }
                else if (System.IO.Directory.Exists(buildFolder) ||
                         folder.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ||
                         folder.Contains("Steam", StringComparison.OrdinalIgnoreCase))
                {
                    // Steam version but logs not created yet
                    logsPath = steamLogsPath;
                }
                else
                {
                    logsPath = bsgLogsPath;
                }
            }

            logsFolderDisplay.Text = logsPath ?? "N/A";

            if (string.IsNullOrEmpty(folder) || !System.IO.Directory.Exists(folder))
            {
                statusIndicator.Fill = (Brush)FindResource("FirBrush");
                statusText.Text = _loc.InvalidGameFolder;
            }
            else if (logsPath != null && System.IO.Directory.Exists(logsPath))
            {
                statusIndicator.Fill = (Brush)FindResource("SuccessBrush");
                statusText.Text = _loc.LogStatusMonitoring + " OK";
            }
            else
            {
                statusIndicator.Fill = (Brush)FindResource("WarningBrush");
                statusText.Text = _loc.LogsFolder + " - Not found";
            }
        }

        gameFolderTextBox.TextChanged += (s, e) => UpdateStatus();
        UpdateStatus();

        statusPanel.Children.Add(statusIndicator);
        statusPanel.Children.Add(statusText);
        Grid.SetRow(statusPanel, 3);
        mainGrid.Children.Add(statusPanel);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var autoDetectButton = new Button
        {
            Content = _loc.AutoDetect,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        autoDetectButton.Click += (s, e) =>
        {
            var detected = GameEnv.ForceDetect();
            gameFolderTextBox.Text = detected ?? "";

            if (!string.IsNullOrEmpty(detected))
            {
                var method = GameEnv.DetectionMethod ?? "Unknown";
                MessageBox.Show(
                    _loc.FormatAutoDetectSuccess(method, detected),
                    _loc.AutoDetect,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    _loc.AutoDetectFailed,
                    _loc.AutoDetect,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        };
        buttonPanel.Children.Add(autoDetectButton);

        var saveButton = new Button
        {
            Content = _loc.Save,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        saveButton.Click += (s, e) =>
        {
            GameEnv.GameFolder = gameFolderTextBox.Text;

            // Restart log watcher
            _logsWatcher.Restart();
            UpdateLogStatusDisplay();

            settingsWindow.Close();
        };
        buttonPanel.Children.Add(saveButton);

        var closeButton = new Button
        {
            Content = _loc.Close,
            Padding = new Thickness(12, 6, 12, 6)
        };
        closeButton.Click += (s, e) => settingsWindow.Close();
        buttonPanel.Children.Add(closeButton);

        Grid.SetRow(buttonPanel, 4);
        mainGrid.Children.Add(buttonPanel);

        settingsWindow.Content = mainGrid;
        settingsWindow.ShowDialog();
    }

    #endregion
}

#region Value Converters

public class LocalizedNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return string.Empty;

        var nameEn = values[0]?.ToString() ?? string.Empty;
        var nameKo = values[1]?.ToString() ?? string.Empty;

        return Services.LocalizationService.Instance.GetDisplayName(nameEn, nameKo);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LocalizedSecondaryNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return string.Empty;

        var nameEn = values[0]?.ToString() ?? string.Empty;
        var nameKo = values[1]?.ToString() ?? string.Empty;

        return Services.LocalizationService.Instance.GetSecondaryName(nameEn, nameKo);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i && i > 0)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class CachedImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrEmpty(url))
            return null;

        // 캐시된 이미지 반환 (없으면 비동기로 다운로드 시작)
        var cached = Services.ImageCacheService.GetImage(url);
        if (cached != null)
            return cached;

        // 캐시에 없으면 원본 URL로 직접 로드 시도 (폴백)
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(url);
            image.EndInit();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

#endregion
