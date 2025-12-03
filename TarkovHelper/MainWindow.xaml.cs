using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TarkovHelper.Debug;
using TarkovHelper.Models;
using TarkovHelper.Pages;
using TarkovHelper.Services;

namespace TarkovHelper;

public partial class MainWindow : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly WikiDataService _wikiService = WikiDataService.Instance;
    private readonly HideoutProgressService _hideoutProgressService = HideoutProgressService.Instance;
    private readonly SettingsService _settingsService = SettingsService.Instance;
    private readonly LogSyncService _logSyncService = LogSyncService.Instance;
    private bool _isLoading;
    private bool _isRefreshing;
    private QuestListPage? _questListPage;
    private HideoutPage? _hideoutPage;
    private ItemsPage? _itemsPage;
    private List<HideoutModule>? _hideoutModules;
    private ObservableCollection<QuestChangeInfo>? _pendingSyncChanges;

    // Windows API for dark title bar
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();
        _loc.LanguageChanged += OnLanguageChanged;
        _settingsService.PlayerLevelChanged += OnPlayerLevelChanged;

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
        UpdateAllLocalizedText();
    }

    private void UpdateAllLocalizedText()
    {
        TxtWelcome.Text = _loc.Welcome;
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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;

        // Apply saved language setting to UI
        CmbLanguage.SelectedIndex = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => 1,
            AppLanguage.JA => 2,
            _ => 0
        };

        // Initialize player level UI
        UpdatePlayerLevelUI();

        UpdateAllLocalizedText();

        _isLoading = false;

        // Check if data needs to be refreshed (tasks.json doesn't exist)
        await CheckAndRefreshDataAsync();
    }

    /// <summary>
    /// Check if task data exists, if not run RefreshData automatically
    /// </summary>
    private async Task CheckAndRefreshDataAsync()
    {
        var tasksFilePath = Path.Combine(AppEnv.DataPath, "tasks.json");

        if (!File.Exists(tasksFilePath))
        {
            await RefreshDataWithOverlayAsync();
        }
        else
        {
            // Data exists, load and show Quest List
            await LoadAndShowQuestListAsync();
        }
    }

    /// <summary>
    /// Refresh data with loading overlay
    /// </summary>
    public async Task RefreshDataWithOverlayAsync()
    {
        if (_isRefreshing) return;
        _isRefreshing = true;

        ShowLoadingOverlay("Initializing...");

        try
        {
            var tarkovService = TarkovDataService.Instance;
            var result = await tarkovService.RefreshAllDataAsync(message =>
            {
                Dispatcher.Invoke(() =>
                {
                    UpdateLoadingStatus(message);
                });
            });

            if (result.Success)
            {
                await LoadAndShowQuestListAsync();
            }
            else
            {
                TxtWelcome.Text = $"Failed to load data: {result.ErrorMessage}";
                MessageBox.Show(
                    $"Failed to refresh data:\n{result.ErrorMessage}",
                    "Data Refresh Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            TxtWelcome.Text = "Failed to load data";
            MessageBox.Show(
                $"Error refreshing data:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            HideLoadingOverlay();
            _isRefreshing = false;
        }
    }

    /// <summary>
    /// Show loading overlay with blur effect
    /// </summary>
    public void ShowLoadingOverlay(string status = "Loading...")
    {
        LoadingStatusText.Text = status;
        LoadingOverlay.Visibility = Visibility.Visible;

        var blurAnimation = new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(200));
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Hide loading overlay
    /// </summary>
    public void HideLoadingOverlay()
    {
        var blurAnimation = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200));
        blurAnimation.Completed += (s, e) =>
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        };
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Update loading status text
    /// </summary>
    public void UpdateLoadingStatus(string status)
    {
        LoadingStatusText.Text = status;
    }

    /// <summary>
    /// Load task data and show Quest List page
    /// </summary>
    private async Task LoadAndShowQuestListAsync()
    {
        var tarkovService = TarkovDataService.Instance;
        var progressService = QuestProgressService.Instance;
        var apiService = TarkovDevApiService.Instance;

        // Load tasks from JSON
        var tasks = await tarkovService.LoadTasksFromJsonAsync();

        // Load hideout data from JSON
        _hideoutModules = await apiService.LoadHideoutStationsFromJsonAsync();

        if (tasks != null && tasks.Count > 0)
        {
            // Initialize progress service with tasks
            progressService.Initialize(tasks);

            // Initialize quest graph service for dependency tracking
            QuestGraphService.Instance.Initialize(tasks);

            // Initialize hideout progress service
            if (_hideoutModules != null && _hideoutModules.Count > 0)
            {
                _hideoutProgressService.Initialize(_hideoutModules);
            }

            // Create pages
            _questListPage = new QuestListPage();
            _hideoutPage = _hideoutModules != null && _hideoutModules.Count > 0
                ? new HideoutPage()
                : null;
            _itemsPage = new ItemsPage();

            // Show tab area with Quests selected
            TxtWelcome.Visibility = Visibility.Collapsed;
            TabContentArea.Visibility = Visibility.Visible;
            TabQuests.IsChecked = true;
            PageContent.Content = _questListPage;
        }
        else
        {
            TxtWelcome.Text = "No quest data available. Please refresh data.";
            TxtWelcome.Visibility = Visibility.Visible;
            TabContentArea.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Handle tab selection change
    /// </summary>
    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        if (sender == TabQuests && _questListPage != null)
        {
            PageContent.Content = _questListPage;
        }
        else if (sender == TabHideout)
        {
            if (_hideoutPage != null)
            {
                PageContent.Content = _hideoutPage;
            }
            else
            {
                // Hideout data not available, show message or load it
                PageContent.Content = new TextBlock
                {
                    Text = "Hideout data not available. Please refresh data.",
                    Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush,
                    FontSize = 16,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }
        else if (sender == TabItems && _itemsPage != null)
        {
            PageContent.Content = _itemsPage;
        }
    }

    #region Player Level

    /// <summary>
    /// Update player level UI
    /// </summary>
    private void UpdatePlayerLevelUI()
    {
        var level = _settingsService.PlayerLevel;
        TxtPlayerLevel.Text = level.ToString();

        // Disable buttons at min/max level
        BtnLevelDown.IsEnabled = level > SettingsService.MinPlayerLevel;
        BtnLevelUp.IsEnabled = level < SettingsService.MaxPlayerLevel;
    }

    /// <summary>
    /// Handle player level decrease
    /// </summary>
    private void BtnLevelDown_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.PlayerLevel--;
    }

    /// <summary>
    /// Handle player level increase
    /// </summary>
    private void BtnLevelUp_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.PlayerLevel++;
    }

    /// <summary>
    /// Handle player level change from settings service
    /// </summary>
    private void OnPlayerLevelChanged(object? sender, int newLevel)
    {
        Dispatcher.Invoke(() =>
        {
            UpdatePlayerLevelUI();

            // Refresh quest list if visible
            _questListPage?.RefreshDisplay();
        });
    }

    #endregion

    /// <summary>
    /// Open Buy me a coffee page
    /// </summary>
    private void BtnCoffee_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://buymeacoffee.com/zeliperstap",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening browser
        }
    }

    /// <summary>
    /// Refresh data from API
    /// </summary>
    private async void BtnRefreshData_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDataWithOverlayAsync();
    }

    /// <summary>
    /// Reset all progress with confirmation
    /// </summary>
    private async void BtnResetProgress_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "정말 진행도를 초기화 하시겠습니까?\nAre you sure you want to reset all progress?\n\nThis will reset:\n- Quest progress\n- Hideout progress",
            "Reset Progress / 진행도 초기화",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            // Reset quest progress
            QuestProgressService.Instance.ResetAllProgress();

            // Reset hideout progress
            _hideoutProgressService.ResetAllProgress();

            // Reload pages
            await LoadAndShowQuestListAsync();

            MessageBox.Show(
                "진행도가 초기화되었습니다.\nAll progress has been reset.",
                "Reset Complete / 초기화 완료",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    #region Settings

    /// <summary>
    /// Open settings dialog
    /// </summary>
    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsOverlay();
    }

    /// <summary>
    /// Show settings overlay
    /// </summary>
    private void ShowSettingsOverlay()
    {
        UpdateSettingsUI();
        SettingsOverlay.Visibility = Visibility.Visible;

        var blurAnimation = new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(200));
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Hide settings overlay
    /// </summary>
    private void HideSettingsOverlay()
    {
        var blurAnimation = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200));
        blurAnimation.Completed += (s, e) =>
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
        };
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Update settings UI with current values
    /// </summary>
    private void UpdateSettingsUI()
    {
        var logPath = _settingsService.LogFolderPath;
        var isValid = _settingsService.IsLogFolderValid;
        var method = _settingsService.DetectionMethod;

        // Update localized text
        UpdateSettingsLocalizedText();

        // Update quest sync section
        UpdateQuestSyncUI();

        // Update path display
        if (!string.IsNullOrEmpty(logPath))
        {
            TxtCurrentLogPath.Text = logPath;
            TxtCurrentLogPath.Foreground = (Brush)FindResource("TextPrimaryBrush");
        }
        else
        {
            TxtCurrentLogPath.Text = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "설정되지 않음",
                AppLanguage.JA => "未設定",
                _ => "Not configured"
            };
            TxtCurrentLogPath.Foreground = (Brush)FindResource("TextSecondaryBrush");
        }

        // Update detection method
        if (!string.IsNullOrEmpty(method))
        {
            TxtDetectionMethod.Text = $"({method})";
        }
        else
        {
            TxtDetectionMethod.Text = "";
        }

        // Update status indicator
        if (isValid)
        {
            LogFolderStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            TxtLogFolderStatus.Text = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "유효한 경로",
                AppLanguage.JA => "有効なパス",
                _ => "Valid path"
            };
        }
        else
        {
            LogFolderStatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
            TxtLogFolderStatus.Text = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "유효하지 않은 경로",
                AppLanguage.JA => "無効なパス",
                _ => "Invalid path"
            };
        }
    }

    /// <summary>
    /// Update settings dialog localized text
    /// </summary>
    private void UpdateSettingsLocalizedText()
    {
        TxtSettingsTitle.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "설정",
            AppLanguage.JA => "設定",
            _ => "Settings"
        };

        TxtLogFolderLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "Tarkov 로그 폴더",
            AppLanguage.JA => "Tarkovログフォルダ",
            _ => "Tarkov Log Folder"
        };

        TxtLogFolderDesc.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "자동 퀘스트 완료 추적을 위해 Tarkov의 Logs 폴더 경로를 설정하세요.",
            AppLanguage.JA => "自動クエスト完了追跡のために、TarkovのLogsフォルダのパスを設定してください。",
            _ => "Set the path to Tarkov's Logs folder for automatic quest completion tracking."
        };

        BtnAutoDetect.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "자동 감지",
            AppLanguage.JA => "自動検出",
            _ => "Auto Detect"
        };

        BtnBrowseLogFolder.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "찾아보기...",
            AppLanguage.JA => "参照...",
            _ => "Browse..."
        };

        BtnResetLogFolder.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "초기화",
            AppLanguage.JA => "リセット",
            _ => "Reset"
        };
    }

    /// <summary>
    /// Close settings overlay when clicking outside the dialog
    /// </summary>
    private void SettingsOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == SettingsOverlay)
        {
            HideSettingsOverlay();
        }
    }

    /// <summary>
    /// Close settings button click
    /// </summary>
    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
    {
        HideSettingsOverlay();
    }

    /// <summary>
    /// Auto detect Tarkov log folder
    /// </summary>
    private void BtnAutoDetect_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.ResetLogFolderPath();
        var detectedPath = _settingsService.AutoDetectLogFolder();

        if (!string.IsNullOrEmpty(detectedPath))
        {
            _settingsService.LogFolderPath = detectedPath;
            UpdateSettingsUI();

            var message = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => $"로그 폴더를 찾았습니다:\n{detectedPath}",
                AppLanguage.JA => $"ログフォルダが見つかりました:\n{detectedPath}",
                _ => $"Log folder detected:\n{detectedPath}"
            };

            MessageBox.Show(message,
                _loc.CurrentLanguage switch { AppLanguage.KO => "자동 감지", AppLanguage.JA => "自動検出", _ => "Auto Detect" },
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            UpdateSettingsUI();

            var message = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "Tarkov 설치를 찾을 수 없습니다.\n수동으로 로그 폴더를 선택해주세요.",
                AppLanguage.JA => "Tarkovのインストールが見つかりませんでした。\n手動でログフォルダを選択してください。",
                _ => "Could not detect Tarkov installation.\nPlease select the log folder manually."
            };

            MessageBox.Show(message,
                _loc.CurrentLanguage switch { AppLanguage.KO => "자동 감지 실패", AppLanguage.JA => "自動検出失敗", _ => "Auto Detect Failed" },
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Browse for log folder
    /// </summary>
    private void BtnBrowseLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "Tarkov Logs 폴더 선택",
                AppLanguage.JA => "Tarkov Logsフォルダを選択",
                _ => "Select Tarkov Logs Folder"
            }
        };

        if (dialog.ShowDialog() == true)
        {
            var selectedPath = dialog.FolderName;

            // Check if it looks like a valid logs folder
            if (Directory.Exists(selectedPath))
            {
                _settingsService.LogFolderPath = selectedPath;
                UpdateSettingsUI();
            }
        }
    }

    /// <summary>
    /// Reset log folder setting
    /// </summary>
    private void BtnResetLogFolder_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.ResetLogFolderPath();
        UpdateSettingsUI();
    }

    #endregion

    #region Cross-Tab Navigation

    /// <summary>
    /// Navigate to Quests tab and select a specific quest
    /// </summary>
    public void NavigateToQuest(string questNormalizedName)
    {
        // Switch to Quests tab
        TabQuests.IsChecked = true;
        PageContent.Content = _questListPage;

        // Request quest selection
        _questListPage?.SelectQuest(questNormalizedName);
    }

    /// <summary>
    /// Navigate to Items tab and select a specific item
    /// </summary>
    public void NavigateToItem(string itemNormalizedName)
    {
        // Switch to Items tab
        TabItems.IsChecked = true;
        PageContent.Content = _itemsPage;

        // Request item selection
        _itemsPage?.SelectItem(itemNormalizedName);
    }

    #endregion

    #region Quest Log Sync

    /// <summary>
    /// Update quest sync UI elements
    /// </summary>
    private void UpdateQuestSyncUI()
    {
        // Update localized text
        TxtQuestSyncLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 로그 동기화",
            AppLanguage.JA => "クエストログ同期",
            _ => "Quest Log Sync"
        };

        TxtQuestSyncDesc.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "게임 로그 파일에서 퀘스트 진행 상태를 동기화합니다. Tarkov 로그를 분석하여 완료된 퀘스트를 업데이트합니다.",
            AppLanguage.JA => "ゲームログファイルからクエストの進行状況を同期します。Tarkovログを分析して完了したクエストを更新します。",
            _ => "Synchronize quest progress from game log files. This will analyze your Tarkov logs and update completed quests."
        };

        BtnSyncQuest.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 동기화",
            AppLanguage.JA => "クエスト同期",
            _ => "Sync Quest"
        };

        // Update monitoring status
        var isMonitoring = _logSyncService.IsMonitoring;
        MonitoringStatusIndicator.Fill = isMonitoring
            ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) // Green
            : new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red

        TxtMonitoringStatus.Text = isMonitoring
            ? _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "모니터링 중",
                AppLanguage.JA => "監視中",
                _ => "Monitoring"
            }
            : _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "모니터링 안함",
                AppLanguage.JA => "監視していない",
                _ => "Not monitoring"
            };

        BtnToggleMonitoring.Content = isMonitoring
            ? _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "모니터링 중지",
                AppLanguage.JA => "監視停止",
                _ => "Stop Monitoring"
            }
            : _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "모니터링 시작",
                AppLanguage.JA => "監視開始",
                _ => "Start Monitoring"
            };

        // Disable sync button if log folder is not valid
        BtnSyncQuest.IsEnabled = _settingsService.IsLogFolderValid;
        BtnToggleMonitoring.IsEnabled = _settingsService.IsLogFolderValid;
    }

    /// <summary>
    /// Sync quest progress from logs
    /// </summary>
    private void BtnSyncQuest_Click(object sender, RoutedEventArgs e)
    {
        var logPath = _settingsService.LogFolderPath;
        if (string.IsNullOrEmpty(logPath) || !Directory.Exists(logPath))
        {
            MessageBox.Show(
                _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "로그 폴더가 설정되지 않았거나 존재하지 않습니다.",
                    AppLanguage.JA => "ログフォルダが設定されていないか、存在しません。",
                    _ => "Log folder is not configured or does not exist."
                },
                _loc.CurrentLanguage switch { AppLanguage.KO => "오류", AppLanguage.JA => "エラー", _ => "Error" },
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Hide settings overlay
        HideSettingsOverlay();

        // Show wipe warning if not hidden
        if (!_settingsService.HideWipeWarning)
        {
            ShowWipeWarningDialog(logPath);
        }
        else
        {
            // Proceed directly with sync
            PerformQuestSync(logPath);
        }
    }

    /// <summary>
    /// Show wipe warning dialog
    /// </summary>
    private void ShowWipeWarningDialog(string logPath)
    {
        // Update localized text
        UpdateWipeWarningLocalizedText();

        // Set log path
        TxtWipeWarningLogPath.Text = logPath;

        // Reset checkbox
        ChkHideWipeWarning.IsChecked = false;

        WipeWarningOverlay.Visibility = Visibility.Visible;

        var blurAnimation = new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(200));
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Hide wipe warning dialog
    /// </summary>
    private void HideWipeWarningDialog()
    {
        var blurAnimation = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200));
        blurAnimation.Completed += (s, e) =>
        {
            WipeWarningOverlay.Visibility = Visibility.Collapsed;
        };
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Close wipe warning dialog when clicking outside
    /// </summary>
    private void WipeWarningOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == WipeWarningOverlay)
        {
            HideWipeWarningDialog();
        }
    }

    /// <summary>
    /// Close wipe warning dialog button click
    /// </summary>
    private void BtnCloseWipeWarning_Click(object sender, RoutedEventArgs e)
    {
        HideWipeWarningDialog();
    }

    /// <summary>
    /// Update wipe warning dialog localized text
    /// </summary>
    private void UpdateWipeWarningLocalizedText()
    {
        TxtWipeWarningTitle.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 동기화 전 확인",
            AppLanguage.JA => "クエスト同期前の確認",
            _ => "Before Quest Sync"
        };

        TxtWipeWarningMessage.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "최근 계정 초기화(와이프)를 진행하셨나요?",
            AppLanguage.JA => "最近アカウントをリセット（ワイプ）しましたか？",
            _ => "Have you recently reset your account (wipe)?"
        };

        TxtWipeWarningDesc.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "계정 초기화 후 동기화를 진행하면 이전 시즌의 로그가 섞여 퀘스트 진행 상태가 올바르지 않게 표시될 수 있습니다.",
            AppLanguage.JA => "アカウントリセット後に同期すると、以前のシーズンのログが混在し、クエストの進行状況が正しく表示されない場合があります。",
            _ => "If you sync after a wipe, logs from the previous season may mix and quest progress may be displayed incorrectly."
        };

        TxtLogFolderPathLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "📁 로그 폴더 위치:",
            AppLanguage.JA => "📁 ログフォルダの場所:",
            _ => "📁 Log folder location:"
        };

        TxtWipeWarningRecommendation.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "💡 권장 조치: 계정 초기화 이전 날짜의 로그 폴더를 삭제하거나 다른 위치로 백업해 주세요.",
            AppLanguage.JA => "💡 推奨: ワイプ前の日付のログフォルダを削除するか、別の場所にバックアップしてください。",
            _ => "💡 Recommended: Delete or backup log folders dated before the wipe."
        };

        ChkHideWipeWarning.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "이 안내를 다시 보지 않기",
            AppLanguage.JA => "この案内を再び表示しない",
            _ => "Don't show this again"
        };

        BtnOpenLogFolder.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "폴더 열기",
            AppLanguage.JA => "フォルダを開く",
            _ => "Open Folder"
        };

        BtnContinueSync.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "계속 진행",
            AppLanguage.JA => "続行",
            _ => "Continue"
        };
    }

    /// <summary>
    /// Open log folder in explorer
    /// </summary>
    private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var logPath = _settingsService.LogFolderPath;
        if (string.IsNullOrEmpty(logPath))
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", logPath);
        }
        catch (Exception)
        {
            // Copy path to clipboard if can't open
            try
            {
                Clipboard.SetText(logPath);
                MessageBox.Show(
                    _loc.CurrentLanguage switch
                    {
                        AppLanguage.KO => "폴더를 열 수 없습니다. 경로가 클립보드에 복사되었습니다.",
                        AppLanguage.JA => "フォルダを開けませんでした。パスがクリップボードにコピーされました。",
                        _ => "Could not open folder. Path has been copied to clipboard."
                    },
                    _loc.CurrentLanguage switch { AppLanguage.KO => "알림", AppLanguage.JA => "通知", _ => "Notice" },
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch
            {
                // Ignore clipboard errors
            }
        }
    }

    /// <summary>
    /// Continue with sync after wipe warning
    /// </summary>
    private void BtnContinueSync_Click(object sender, RoutedEventArgs e)
    {
        // Save hide warning preference
        if (ChkHideWipeWarning.IsChecked == true)
        {
            _settingsService.HideWipeWarning = true;
        }

        HideWipeWarningDialog();

        var logPath = _settingsService.LogFolderPath;
        if (!string.IsNullOrEmpty(logPath))
        {
            PerformQuestSync(logPath);
        }
    }

    /// <summary>
    /// Perform the actual quest sync
    /// </summary>
    private async void PerformQuestSync(string logPath)
    {
        ShowLoadingOverlay(_loc.CurrentLanguage switch
        {
            AppLanguage.KO => "로그 파일 스캔 중...",
            AppLanguage.JA => "ログファイルをスキャン中...",
            _ => "Scanning log files..."
        });

        try
        {
            var progress = new Progress<string>(message =>
            {
                Dispatcher.Invoke(() => UpdateLoadingStatus(message));
            });

            var result = await _logSyncService.SyncFromLogsAsync(logPath, progress);

            HideLoadingOverlay();

            // Show result dialog even if no quests to complete (to show in-progress quests)
            if (result.QuestsToComplete.Count == 0 && result.InProgressQuests.Count == 0)
            {
                MessageBox.Show(
                    _loc.CurrentLanguage switch
                    {
                        AppLanguage.KO => result.TotalEventsFound > 0
                            ? $"퀘스트 이벤트 {result.TotalEventsFound}개를 찾았지만, 업데이트할 퀘스트가 없습니다."
                            : "로그에서 퀘스트 이벤트를 찾지 못했습니다.",
                        AppLanguage.JA => result.TotalEventsFound > 0
                            ? $"{result.TotalEventsFound}件のクエストイベントが見つかりましたが、更新するクエストはありません。"
                            : "ログにクエストイベントが見つかりませんでした。",
                        _ => result.TotalEventsFound > 0
                            ? $"Found {result.TotalEventsFound} quest events, but no quests need to be updated."
                            : "No quest events found in logs."
                    },
                    _loc.CurrentLanguage switch { AppLanguage.KO => "동기화 완료", AppLanguage.JA => "同期完了", _ => "Sync Complete" },
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Show confirmation dialog
            ShowSyncResultDialog(result);
        }
        catch (Exception ex)
        {
            HideLoadingOverlay();
            MessageBox.Show(
                $"Error: {ex.Message}",
                "Sync Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Toggle log monitoring
    /// </summary>
    private void BtnToggleMonitoring_Click(object sender, RoutedEventArgs e)
    {
        if (_logSyncService.IsMonitoring)
        {
            _logSyncService.StopMonitoring();
        }
        else
        {
            var logPath = _settingsService.LogFolderPath;
            if (!string.IsNullOrEmpty(logPath) && Directory.Exists(logPath))
            {
                _logSyncService.StartMonitoring(logPath);

                // Subscribe to quest events
                _logSyncService.QuestEventDetected -= OnQuestEventDetected;
                _logSyncService.QuestEventDetected += OnQuestEventDetected;
            }
        }

        UpdateQuestSyncUI();
    }

    /// <summary>
    /// Handle real-time quest event detection
    /// </summary>
    private void OnQuestEventDetected(object? sender, QuestLogEvent evt)
    {
        Dispatcher.Invoke(() =>
        {
            // Find the task
            var progressService = QuestProgressService.Instance;
            var tasksByQuestId = BuildQuestIdLookup(progressService.AllTasks);

            if (tasksByQuestId.TryGetValue(evt.QuestId, out var task))
            {
                var message = evt.EventType switch
                {
                    QuestEventType.Started => _loc.CurrentLanguage switch
                    {
                        AppLanguage.KO => $"퀘스트 시작: {task.Name}",
                        AppLanguage.JA => $"クエスト開始: {task.Name}",
                        _ => $"Quest Started: {task.Name}"
                    },
                    QuestEventType.Completed => _loc.CurrentLanguage switch
                    {
                        AppLanguage.KO => $"퀘스트 완료: {task.Name}",
                        AppLanguage.JA => $"クエスト完了: {task.Name}",
                        _ => $"Quest Completed: {task.Name}"
                    },
                    QuestEventType.Failed => _loc.CurrentLanguage switch
                    {
                        AppLanguage.KO => $"퀘스트 실패: {task.Name}",
                        AppLanguage.JA => $"クエスト失敗: {task.Name}",
                        _ => $"Quest Failed: {task.Name}"
                    },
                    _ => ""
                };

                // Auto-update progress based on event
                switch (evt.EventType)
                {
                    case QuestEventType.Completed:
                        progressService.CompleteQuest(task, completePrerequisites: true);
                        break;
                    case QuestEventType.Failed:
                        progressService.FailQuest(task);
                        break;
                    case QuestEventType.Started:
                        // For started quests, complete all prerequisites
                        var graphService = QuestGraphService.Instance;
                        if (!string.IsNullOrEmpty(task.NormalizedName))
                        {
                            var prereqs = graphService.GetAllPrerequisites(task.NormalizedName);
                            foreach (var prereq in prereqs)
                            {
                                if (progressService.GetStatus(prereq) != QuestStatus.Done)
                                {
                                    progressService.CompleteQuest(prereq, completePrerequisites: false);
                                }
                            }
                        }
                        break;
                }

                // Refresh quest list if visible
                _questListPage?.RefreshDisplay();
            }
        });
    }

    /// <summary>
    /// Build quest ID lookup dictionary
    /// </summary>
    private Dictionary<string, TarkovTask> BuildQuestIdLookup(IReadOnlyList<TarkovTask> tasks)
    {
        var lookup = new Dictionary<string, TarkovTask>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            if (task.Ids != null)
            {
                foreach (var id in task.Ids)
                {
                    if (!string.IsNullOrEmpty(id) && !lookup.ContainsKey(id))
                    {
                        lookup[id] = task;
                    }
                }
            }
        }
        return lookup;
    }

    /// <summary>
    /// Show sync result confirmation dialog
    /// </summary>
    private void ShowSyncResultDialog(SyncResult result)
    {
        _pendingSyncChanges = new ObservableCollection<QuestChangeInfo>(result.QuestsToComplete);

        // Update dialog text
        TxtSyncResultTitle.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 동기화 완료",
            AppLanguage.JA => "クエスト同期完了",
            _ => "Quest Sync Complete"
        };

        var prereqCount = result.QuestsToComplete.Count(q => q.IsPrerequisite);
        var directCount = result.QuestsToComplete.Count - prereqCount;
        var inProgressCount = result.InProgressQuests.Count;

        // Update column headers
        TxtCompletedQuestsHeader.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => $"완료된 퀘스트 ({result.QuestsToComplete.Count})",
            AppLanguage.JA => $"完了したクエスト ({result.QuestsToComplete.Count})",
            _ => $"Completed Quests ({result.QuestsToComplete.Count})"
        };

        TxtInProgressQuestsHeader.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => $"진행중 퀘스트 ({inProgressCount})",
            AppLanguage.JA => $"進行中のクエスト ({inProgressCount})",
            _ => $"In Progress ({inProgressCount})"
        };

        TxtSyncSummary.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "요약:",
            AppLanguage.JA => "概要:",
            _ => "Summary:"
        };

        TxtSyncStats.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => $"├─ 로그에서 발견된 이벤트: {result.TotalEventsFound}\n├─ 자동 완료된 선행 퀘스트: {prereqCount}\n└─ 매칭 실패한 퀘스트 ID: {result.UnmatchedQuestIds.Count}",
            AppLanguage.JA => $"├─ ログで見つかったイベント: {result.TotalEventsFound}\n├─ 自動完了した前提クエスト: {prereqCount}\n└─ マッチング失敗したクエストID: {result.UnmatchedQuestIds.Count}",
            _ => $"├─ Events found in logs: {result.TotalEventsFound}\n├─ Prerequisites auto-completed: {prereqCount}\n└─ Unmatched quest IDs: {result.UnmatchedQuestIds.Count}"
        };

        BtnCancelSync.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "취소",
            AppLanguage.JA => "キャンセル",
            _ => "Cancel"
        };

        BtnConfirmSync.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "확인",
            AppLanguage.JA => "確認",
            _ => "Confirm"
        };

        // Set data sources
        SyncQuestList.ItemsSource = _pendingSyncChanges;
        InProgressQuestList.ItemsSource = result.InProgressQuests;

        SyncResultOverlay.Visibility = Visibility.Visible;

        var blurAnimation = new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(200));
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Hide sync result dialog
    /// </summary>
    private void HideSyncResultDialog()
    {
        var blurAnimation = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200));
        blurAnimation.Completed += (s, e) =>
        {
            SyncResultOverlay.Visibility = Visibility.Collapsed;
        };
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Close sync result dialog
    /// </summary>
    private void BtnCloseSyncResult_Click(object sender, RoutedEventArgs e)
    {
        HideSyncResultDialog();
    }

    /// <summary>
    /// Cancel sync
    /// </summary>
    private void BtnCancelSync_Click(object sender, RoutedEventArgs e)
    {
        HideSyncResultDialog();
    }

    /// <summary>
    /// Confirm sync changes
    /// </summary>
    private async void BtnConfirmSync_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingSyncChanges == null) return;

        var selectedChanges = _pendingSyncChanges.Where(c => c.IsSelected).ToList();

        if (selectedChanges.Count == 0)
        {
            HideSyncResultDialog();
            return;
        }

        HideSyncResultDialog();
        ShowLoadingOverlay(_loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 진행도 업데이트 중...",
            AppLanguage.JA => "クエスト進捗を更新中...",
            _ => "Updating quest progress..."
        });

        await Task.Run(() =>
        {
            _logSyncService.ApplyQuestChanges(selectedChanges);
        });

        HideLoadingOverlay();

        // Refresh quest list
        await LoadAndShowQuestListAsync();

        MessageBox.Show(
            _loc.CurrentLanguage switch
            {
                AppLanguage.KO => $"{selectedChanges.Count}개의 퀘스트가 업데이트되었습니다.",
                AppLanguage.JA => $"{selectedChanges.Count}件のクエストが更新されました。",
                _ => $"{selectedChanges.Count} quests have been updated."
            },
            _loc.CurrentLanguage switch { AppLanguage.KO => "동기화 완료", AppLanguage.JA => "同期完了", _ => "Sync Complete" },
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion

    #region In-Progress Quest Input

    private List<QuestSelectionItem>? _allQuestItems;
    private List<QuestSelectionItem>? _filteredQuestItems;
    private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
    private List<TarkovTrader>? _cachedTraders;

    /// <summary>
    /// Open in-progress quest input button click
    /// </summary>
    private async void BtnInProgressQuestInput_Click(object sender, RoutedEventArgs e)
    {
        HideSettingsOverlay();
        await ShowInProgressQuestInputOverlayAsync();
    }

    /// <summary>
    /// Show in-progress quest input overlay
    /// </summary>
    private async Task ShowInProgressQuestInputOverlayAsync()
    {
        var graphService = QuestGraphService.Instance;
        var progressService = QuestProgressService.Instance;

        // Check if quest data is loaded
        if (graphService.GetAllTasks() == null || graphService.GetAllTasks().Count == 0)
        {
            MessageBox.Show(
                _loc.QuestDataNotLoaded,
                _loc.CurrentLanguage switch { AppLanguage.KO => "오류", AppLanguage.JA => "エラー", _ => "Error" },
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Load traders data asynchronously (cache it)
        var apiService = TarkovDevApiService.Instance;
        _cachedTraders = await apiService.LoadTradersFromJsonAsync();

        // Initialize quest list
        LoadQuestSelectionList();

        // Initialize trader filter
        LoadTraderFilter();

        // Clear search
        TxtQuestSearch.Text = string.Empty;

        // Update localized text
        UpdateInProgressQuestInputLocalizedText();

        // Clear prerequisites preview
        PrerequisitesList.ItemsSource = null;
        UpdateSummaryCounts();

        InProgressQuestInputOverlay.Visibility = Visibility.Visible;

        var blurAnimation = new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(200));
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Hide in-progress quest input overlay
    /// </summary>
    private void HideInProgressQuestInputOverlay()
    {
        var blurAnimation = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200));
        blurAnimation.Completed += (s, e) =>
        {
            InProgressQuestInputOverlay.Visibility = Visibility.Collapsed;
        };
        BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
    }

    /// <summary>
    /// Load quest selection list
    /// </summary>
    private void LoadQuestSelectionList()
    {
        var graphService = QuestGraphService.Instance;
        var progressService = QuestProgressService.Instance;
        var tasks = graphService.GetAllTasks();

        _allQuestItems = tasks
            .Where(t => !string.IsNullOrEmpty(t.NormalizedName))
            .Select(t => new QuestSelectionItem
            {
                Quest = t,
                DisplayName = GetLocalizedQuestName(t),
                TraderName = GetLocalizedTraderName(t.Trader),
                IsCompleted = progressService.GetStatus(t) == QuestStatus.Done,
                IsSelected = false
            })
            .OrderBy(q => q.TraderName)
            .ThenBy(q => q.DisplayName)
            .ToList();

        _filteredQuestItems = _allQuestItems.ToList();
        QuestSelectionList.ItemsSource = _filteredQuestItems;
    }

    /// <summary>
    /// Load trader filter combobox
    /// </summary>
    private void LoadTraderFilter()
    {
        var graphService = QuestGraphService.Instance;
        var tasks = graphService.GetAllTasks();

        var traders = tasks
            .Select(t => t.Trader)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        CmbQuestTraderFilter.Items.Clear();
        CmbQuestTraderFilter.Items.Add(new ComboBoxItem { Content = _loc.AllTraders, Tag = "All" });

        foreach (var trader in traders)
        {
            CmbQuestTraderFilter.Items.Add(new ComboBoxItem
            {
                Content = GetLocalizedTraderName(trader),
                Tag = trader
            });
        }

        CmbQuestTraderFilter.SelectedIndex = 0;
    }

    /// <summary>
    /// Get localized quest name
    /// </summary>
    private string GetLocalizedQuestName(TarkovTask task)
    {
        return _loc.CurrentLanguage switch
        {
            AppLanguage.KO => task.NameKo ?? task.Name,
            AppLanguage.JA => task.NameJa ?? task.Name,
            _ => task.Name
        };
    }

    /// <summary>
    /// Get localized trader name using cached traders data
    /// </summary>
    private string GetLocalizedTraderName(string? trader)
    {
        if (string.IsNullOrEmpty(trader)) return string.Empty;

        // Use cached traders data
        if (_cachedTraders != null)
        {
            var traderData = _cachedTraders.FirstOrDefault(t =>
                string.Equals(t.Name, trader, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.NormalizedName, trader, StringComparison.OrdinalIgnoreCase));

            if (traderData != null)
            {
                return _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => traderData.NameKo ?? traderData.Name,
                    AppLanguage.JA => traderData.NameJa ?? traderData.Name,
                    _ => traderData.Name
                };
            }
        }

        return trader;
    }

    /// <summary>
    /// Update in-progress quest input overlay localized text
    /// </summary>
    private void UpdateInProgressQuestInputLocalizedText()
    {
        TxtInProgressQuestTitle.Text = _loc.InProgressQuestInputTitle;
        TxtQuestSelectionHeader.Text = _loc.QuestSelection;
        TxtTraderFilterLabel.Text = _loc.TraderFilter;
        TxtPrerequisitesHeader.Text = _loc.PrerequisitesPreview;
        TxtPrerequisitesDesc.Text = _loc.PrerequisitesDescription;
        BtnCancelInProgressInput.Content = _loc.Cancel;
        BtnApplyInProgressInput.Content = _loc.Apply;
        BtnInProgressQuestInput.Content = _loc.InProgressQuestInputButton;

        // Update "All" item in trader filter
        if (CmbQuestTraderFilter.Items.Count > 0 && CmbQuestTraderFilter.Items[0] is ComboBoxItem allItem)
        {
            allItem.Content = _loc.AllTraders;
        }
    }

    /// <summary>
    /// Filter quests based on search text and trader filter
    /// </summary>
    private void FilterQuests()
    {
        if (_allQuestItems == null) return;

        var searchText = TxtQuestSearch.Text?.Trim() ?? string.Empty;
        var selectedTrader = (CmbQuestTraderFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";

        _filteredQuestItems = _allQuestItems
            .Where(q =>
            {
                // Search filter
                var matchesSearch = string.IsNullOrEmpty(searchText) ||
                    q.Quest.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (q.Quest.NameKo?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (q.Quest.NameJa?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);

                // Trader filter
                var matchesTrader = selectedTrader == "All" ||
                    string.Equals(q.Quest.Trader, selectedTrader, StringComparison.OrdinalIgnoreCase);

                return matchesSearch && matchesTrader;
            })
            .ToList();

        QuestSelectionList.ItemsSource = _filteredQuestItems;
    }

    /// <summary>
    /// Update prerequisite preview based on selected quests
    /// </summary>
    private void UpdatePrerequisitePreview()
    {
        if (_allQuestItems == null) return;

        var progressService = QuestProgressService.Instance;
        var graphService = QuestGraphService.Instance;

        var selectedQuests = _allQuestItems
            .Where(q => q.IsSelected)
            .Select(q => q.Quest)
            .ToList();

        var allPrereqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var quest in selectedQuests)
        {
            if (string.IsNullOrEmpty(quest.NormalizedName)) continue;

            var prereqs = graphService.GetAllPrerequisites(quest.NormalizedName);
            foreach (var prereq in prereqs)
            {
                // Exclude already completed quests
                if (progressService.GetStatus(prereq) != QuestStatus.Done &&
                    !string.IsNullOrEmpty(prereq.NormalizedName))
                {
                    allPrereqs.Add(prereq.NormalizedName);
                }
            }
        }

        // Remove selected quests from prerequisites
        foreach (var quest in selectedQuests)
        {
            if (!string.IsNullOrEmpty(quest.NormalizedName))
            {
                allPrereqs.Remove(quest.NormalizedName);
            }
        }

        var prereqItems = allPrereqs
            .Select(name => graphService.GetTask(name))
            .Where(t => t != null)
            .Select(t => new PrerequisitePreviewItem
            {
                Quest = t!,
                DisplayName = GetLocalizedQuestName(t!),
                TraderName = GetLocalizedTraderName(t!.Trader)
            })
            .OrderBy(p => p.TraderName)
            .ThenBy(p => p.DisplayName)
            .ToList();

        PrerequisitesList.ItemsSource = prereqItems;
        UpdateSummaryCounts();
    }

    /// <summary>
    /// Update summary counts
    /// </summary>
    private void UpdateSummaryCounts()
    {
        var selectedCount = _allQuestItems?.Count(q => q.IsSelected) ?? 0;
        var prereqCount = (PrerequisitesList.ItemsSource as IEnumerable<PrerequisitePreviewItem>)?.Count() ?? 0;

        TxtSelectedQuestsCount.Text = string.Format(_loc.SelectedQuestsCount, selectedCount);
        TxtPrerequisitesCount.Text = string.Format(_loc.PrerequisitesToComplete, prereqCount);

        // Enable/disable Apply button
        BtnApplyInProgressInput.IsEnabled = selectedCount > 0;
    }

    /// <summary>
    /// Search text changed with debounce
    /// </summary>
    private void TxtQuestSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchDebounceTimer.Tick += (s, args) =>
        {
            _searchDebounceTimer.Stop();
            FilterQuests();
        };
        _searchDebounceTimer.Start();
    }

    /// <summary>
    /// Trader filter selection changed
    /// </summary>
    private void CmbQuestTraderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_allQuestItems == null) return;
        FilterQuests();
    }

    /// <summary>
    /// Quest selection checkbox changed
    /// </summary>
    private void QuestSelection_CheckChanged(object sender, RoutedEventArgs e)
    {
        UpdatePrerequisitePreview();
    }

    /// <summary>
    /// Close button click
    /// </summary>
    private void BtnCloseInProgressQuestInput_Click(object sender, RoutedEventArgs e)
    {
        HideInProgressQuestInputOverlay();
    }

    /// <summary>
    /// Cancel button click
    /// </summary>
    private void BtnCancelInProgressInput_Click(object sender, RoutedEventArgs e)
    {
        HideInProgressQuestInputOverlay();
    }

    /// <summary>
    /// Click outside overlay to close
    /// </summary>
    private void InProgressQuestInputOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == InProgressQuestInputOverlay)
        {
            HideInProgressQuestInputOverlay();
        }
    }

    /// <summary>
    /// Apply button click - set selected quests to Active and complete prerequisites
    /// </summary>
    private void BtnApplyInProgressInput_Click(object sender, RoutedEventArgs e)
    {
        if (_allQuestItems == null) return;

        var selectedQuests = _allQuestItems
            .Where(q => q.IsSelected)
            .Select(q => q.Quest)
            .ToList();

        if (selectedQuests.Count == 0)
        {
            MessageBox.Show(
                _loc.NoQuestsSelected,
                _loc.CurrentLanguage switch { AppLanguage.KO => "알림", AppLanguage.JA => "通知", _ => "Notice" },
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var progressService = QuestProgressService.Instance;
        var graphService = QuestGraphService.Instance;

        // Collect all prerequisites to complete
        var prerequisitesToComplete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var quest in selectedQuests)
        {
            if (string.IsNullOrEmpty(quest.NormalizedName)) continue;

            var prereqs = graphService.GetAllPrerequisites(quest.NormalizedName);
            foreach (var prereq in prereqs)
            {
                if (progressService.GetStatus(prereq) != QuestStatus.Done &&
                    !string.IsNullOrEmpty(prereq.NormalizedName))
                {
                    prerequisitesToComplete.Add(prereq.NormalizedName);
                }
            }
        }

        // Remove selected quests from prerequisites (they will be set to Active, not Done)
        foreach (var quest in selectedQuests)
        {
            if (!string.IsNullOrEmpty(quest.NormalizedName))
            {
                prerequisitesToComplete.Remove(quest.NormalizedName);
            }
        }

        // Complete all prerequisites
        var completedCount = 0;
        foreach (var prereqName in prerequisitesToComplete)
        {
            var prereqTask = progressService.GetTask(prereqName);
            if (prereqTask != null && progressService.GetStatus(prereqTask) != QuestStatus.Done)
            {
                progressService.CompleteQuest(prereqTask, completePrerequisites: false);
                completedCount++;
            }
        }

        // Note: Selected quests are left as Active (their prerequisites are now complete)
        // The QuestProgressService.GetStatus will return Active since:
        // 1. They are not marked as Done or Failed
        // 2. All prerequisites are now Done
        // 3. Level requirement check (if any) will determine final status

        HideInProgressQuestInputOverlay();

        // Refresh quest list
        _questListPage?.RefreshDisplay();

        // Show success message
        MessageBox.Show(
            string.Format(_loc.QuestsAppliedSuccess, selectedQuests.Count, completedCount),
            _loc.CurrentLanguage switch { AppLanguage.KO => "적용 완료", AppLanguage.JA => "適用完了", _ => "Applied" },
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion
}
