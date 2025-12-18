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
using TarkovHelper.Pages.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Logging;
using TarkovHelper.Windows;

namespace TarkovHelper;

public partial class MainWindow : Window
{
    private static readonly ILogger _log = Log.For<MainWindow>();
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly HideoutProgressService _hideoutProgressService = HideoutProgressService.Instance;
    private readonly SettingsService _settingsService = SettingsService.Instance;
    private readonly LogSyncService _logSyncService = LogSyncService.Instance;
    private bool _isLoading;
    private QuestListPage? _questListPage;
    private HideoutPage? _hideoutPage;
    private ItemsPage? _itemsPage;
    private CollectorPage? _collectorPage;
    private MapPage? _mapTrackerPage;
    private List<HideoutModule>? _hideoutModules;
    private bool _isFullScreen;

    // Windows API for dark title bar
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();
        _loc.LanguageChanged += OnLanguageChanged;
        _settingsService.PlayerLevelChanged += OnPlayerLevelChanged;
        _settingsService.ScavRepChanged += OnScavRepChanged;
        _settingsService.DspDecodeCountChanged += OnDspDecodeCountChanged;
        _settingsService.HasEodEditionChanged += OnEditionChanged;
        _settingsService.HasUnheardEditionChanged += OnEditionChanged;
        _settingsService.PrestigeLevelChanged += OnPrestigeLevelChanged;

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

        // Initialize Scav Rep UI
        UpdateScavRepUI();

        // Initialize DSP Decode Count UI
        UpdateDspDecodeUI();

        // Initialize Edition and Prestige Level UI
        UpdateEditionUI();
        UpdatePrestigeLevelUI();

        UpdateAllLocalizedText();

        _isLoading = false;

        // Start database update check (initial check + background updates every 5 minutes)
        StartDatabaseUpdateService();

        // Start app update service (check every 3 minutes)
        StartAppUpdateService();

        // Load and show quest data from DB
        await CheckAndRefreshDataAsync();

        // Auto-start log monitoring if enabled
        AutoStartLogMonitoring();
    }

    /// <summary>
    /// 데이터베이스 업데이트 서비스 시작
    /// </summary>
    private void StartDatabaseUpdateService()
    {
        var dbUpdateService = DatabaseUpdateService.Instance;

        // 업데이트 완료 이벤트 구독 (UI 새로고침용)
        dbUpdateService.DatabaseUpdated += OnDatabaseUpdated;

        // 백그라운드 업데이트 체크 시작 (5분마다)
        dbUpdateService.StartBackgroundUpdates();

        _log.Info("Database update service started");
    }

    /// <summary>
    /// 데이터베이스 업데이트 완료 시 UI 새로고침
    /// </summary>
    private void OnDatabaseUpdated(object? sender, EventArgs e)
    {
        _log.Info("Database updated, all services will reload data automatically");

        // 서비스들이 이미 DatabaseUpdated 이벤트를 구독하고 있으므로
        // 각 서비스의 RefreshAsync()가 자동으로 호출됨
        // UI 페이지들은 서비스의 새로운 데이터를 사용하게 됨

        // 필요시 사용자에게 알림 표시 가능
        Dispatcher.Invoke(() =>
        {
            // 상태 표시줄이나 토스트 메시지로 업데이트 완료 알림 가능
            _log.Debug("Database update notification displayed");
        });
    }

    /// <summary>
    /// Automatically start log monitoring on app launch if enabled
    /// </summary>
    private void AutoStartLogMonitoring()
    {
        if (!_settingsService.LogMonitoringEnabled)
            return;

        // Try to get log folder path (auto-detect if not set)
        var logPath = _settingsService.LogFolderPath;

        // If no path and auto-detect failed, try to save auto-detected path
        if (string.IsNullOrEmpty(logPath))
        {
            logPath = _settingsService.AutoDetectLogFolder();
            if (!string.IsNullOrEmpty(logPath))
            {
                _settingsService.LogFolderPath = logPath;
            }
        }

        if (!string.IsNullOrEmpty(logPath) && Directory.Exists(logPath))
        {
            _logSyncService.StartMonitoring(logPath);
            _logSyncService.QuestEventDetected -= OnQuestEventDetected;
            _logSyncService.QuestEventDetected += OnQuestEventDetected;
            _log.Info($"Auto-started log monitoring: {logPath}");
        }

        UpdateQuestSyncUI();
    }

    /// <summary>
    /// Load and show quest data from DB
    /// </summary>
    private async Task CheckAndRefreshDataAsync()
    {
        // Quest data is now bundled in tarkov_data.db, load directly
        await LoadAndShowQuestListAsync();
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
    /// 마이그레이션 진행 상황 업데이트
    /// </summary>
    private void OnMigrationProgress(string message)
    {
        // BeginInvoke를 사용하여 비동기로 UI 업데이트 (데드락 방지)
        Dispatcher.BeginInvoke(() => UpdateLoadingStatus(message));
    }

    /// <summary>
    /// Load task data and show Quest List page
    /// </summary>
    private async Task LoadAndShowQuestListAsync()
    {
        var progressService = QuestProgressService.Instance;
        var migrationService = ConfigMigrationService.Instance;

        List<TarkovTask>? tasks = null;
        ConfigMigrationService.MigrationResult? migrationResult = null;

        // 자동 마이그레이션 필요 여부 확인 (3.5 버전 등에서 업데이트 시)
        bool needsMigration = migrationService.NeedsAutoMigration();
        if (needsMigration)
        {
            ShowLoadingOverlay(_loc.CurrentLanguage switch
            {
                AppLanguage.KO => "데이터 마이그레이션 중...",
                AppLanguage.JA => "データ移行中...",
                _ => "Migrating data..."
            });

            try
            {
                var progress = new Progress<string>(message =>
                {
                    Dispatcher.BeginInvoke(() => UpdateLoadingStatus(message));
                });

                // ConfigMigrationService를 사용하여 마이그레이션 수행
                migrationResult = await migrationService.MigrateFromCurrentConfigAsync(progress);

                // 마이그레이션 결과 로깅
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "migration_log.txt");
                var logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Migration completed\n" +
                                 $"  Success: {migrationResult?.Success}\n" +
                                 $"  QuestProgress: {migrationResult?.QuestProgressCount}\n" +
                                 $"  HideoutProgress: {migrationResult?.HideoutProgressCount}\n" +
                                 $"  ItemInventory: {migrationResult?.ItemInventoryCount}\n" +
                                 $"  Settings: {migrationResult?.SettingsCount}\n" +
                                 $"  TotalCount: {migrationResult?.TotalCount}\n" +
                                 $"  Warnings: {string.Join(", ", migrationResult?.Warnings ?? [])}\n" +
                                 $"  Errors: {string.Join(", ", migrationResult?.Errors ?? [])}\n\n";
                File.AppendAllText(logPath, logContent);
            }
            catch (Exception ex)
            {
                // 마이그레이션 실패 시 로그 파일에 기록
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "migration_error.log");
                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Migration failed:\n{ex}\n\nStack trace:\n{ex.StackTrace}");
                _log.Error($"Migration failed: {ex.Message}");
            }
            finally
            {
                // LoadingOverlay만 숨기고, Blur는 마이그레이션 결과 팝업 표시 여부에 따라 처리
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        try
        {
            // DB에서 퀘스트 데이터 로드
            if (await progressService.InitializeFromDbAsync())
            {
                tasks = progressService.AllTasks.ToList();
                _log.Debug($"Loaded {tasks.Count} quests from DB");
            }
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load quests: {ex.Message}");
        }

        // Load hideout data from DB
        var hideoutDbService = HideoutDbService.Instance;
        var hideoutLoaded = await hideoutDbService.LoadStationsAsync();
        _log.Debug($"Hideout DB loaded: {hideoutLoaded}, StationCount: {hideoutDbService.StationCount}");
        if (hideoutLoaded)
        {
            _hideoutModules = hideoutDbService.AllStations.ToList();
            _log.Debug($"Hideout modules count: {_hideoutModules.Count}");
        }
        else
        {
            _log.Warning($"Hideout loading failed. DB exists: {hideoutDbService.DatabaseExists}");
        }

        _log.Debug($"Tasks count: {tasks?.Count ?? 0}");

        // Log diagnostic info to file
        try
        {
            var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_log.txt");
            var logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Startup Diagnostics\n" +
                             $"  Hideout DB Loaded: {hideoutLoaded}\n" +
                             $"  Hideout Stations: {hideoutDbService.StationCount}\n" +
                             $"  Hideout Modules: {_hideoutModules?.Count ?? 0}\n" +
                             $"  Tasks Count: {tasks?.Count ?? 0}\n" +
                             $"  Database Path: {hideoutDbService.DatabaseExists}\n\n";
            System.IO.File.AppendAllText(logPath, logContent);
        }
        catch { /* Ignore logging errors */ }

        if (tasks != null && tasks.Count > 0)
        {
            // Initialize quest graph service for dependency tracking
            QuestGraphService.Instance.Initialize(tasks);

            // Initialize hideout progress service
            if (_hideoutModules != null && _hideoutModules.Count > 0)
            {
                _hideoutProgressService.Initialize(_hideoutModules);
            }

            // Check if pages already exist (refresh scenario)
            if (_questListPage != null)
            {
                // Reload data in existing pages to pick up new translations
                await _questListPage.ReloadDataAsync();
            }
            else
            {
                // Create pages for the first time
                _questListPage = new QuestListPage();
            }

            // Debug: Show hideout module status
            _log.Debug($"Creating HideoutPage: modules={_hideoutModules?.Count ?? 0}");
            _hideoutPage = _hideoutModules != null && _hideoutModules.Count > 0
                ? new HideoutPage()
                : null;
            _log.Debug($"HideoutPage created: {_hideoutPage != null}");
            _itemsPage = new ItemsPage();
            _collectorPage = new CollectorPage();

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

        // 마이그레이션 결과가 있으면 팝업 표시 (자동 마이그레이션 후)
        if (migrationResult != null && migrationResult.TotalCount > 0)
        {
            ShowMigrationResultDialog(migrationResult);
        }
        else if (needsMigration)
        {
            // 마이그레이션이 필요했지만 결과가 없는 경우 Blur 해제
            var blurAnimation = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(200));
            BlurEffect.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnimation);
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
        else if (sender == TabCollector && _collectorPage != null)
        {
            PageContent.Content = _collectorPage;
        }
        else if (sender == TabMap)
        {
            _mapTrackerPage ??= new MapPage();
            PageContent.Content = _mapTrackerPage;
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

    /// <summary>
    /// Only allow numeric input for player level
    /// </summary>
    private void TxtPlayerLevel_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !int.TryParse(e.Text, out _);
    }

    /// <summary>
    /// Apply level when losing focus
    /// </summary>
    private void TxtPlayerLevel_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyPlayerLevelFromTextBox();
    }

    /// <summary>
    /// Apply level when pressing Enter
    /// </summary>
    private void TxtPlayerLevel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyPlayerLevelFromTextBox();
            Keyboard.ClearFocus();
        }
    }

    /// <summary>
    /// Parse and apply player level from TextBox input
    /// </summary>
    private void ApplyPlayerLevelFromTextBox()
    {
        if (int.TryParse(TxtPlayerLevel.Text, out var level))
        {
            // Clamp to valid range
            level = Math.Clamp(level, SettingsService.MinPlayerLevel, SettingsService.MaxPlayerLevel);
            _settingsService.PlayerLevel = level;
        }
        else
        {
            // Reset to current value if invalid
            TxtPlayerLevel.Text = _settingsService.PlayerLevel.ToString();
        }
    }

    #endregion

    #region Scav Rep

    /// <summary>
    /// Update Scav Rep UI
    /// </summary>
    private void UpdateScavRepUI()
    {
        var scavRep = _settingsService.ScavRep;
        TxtScavRep.Text = scavRep.ToString("0.0");

        // Disable buttons at min/max Scav Rep
        BtnScavRepDown.IsEnabled = scavRep > SettingsService.MinScavRep;
        BtnScavRepUp.IsEnabled = scavRep < SettingsService.MaxScavRep;
    }

    /// <summary>
    /// Handle Scav Rep decrease
    /// </summary>
    private void BtnScavRepDown_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.ScavRep -= SettingsService.ScavRepStep;
    }

    /// <summary>
    /// Handle Scav Rep increase
    /// </summary>
    private void BtnScavRepUp_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.ScavRep += SettingsService.ScavRepStep;
    }

    /// <summary>
    /// Handle Scav Rep change from settings service
    /// </summary>
    private void OnScavRepChanged(object? sender, double newScavRep)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateScavRepUI();

            // Refresh quest list if visible
            _questListPage?.RefreshDisplay();
        });
    }

    /// <summary>
    /// Allow numeric input including decimal point and minus sign for Scav Rep
    /// </summary>
    private void TxtScavRep_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = sender as TextBox;
        var currentText = textBox?.Text ?? "";
        var newChar = e.Text;

        // Allow minus sign only at the beginning
        if (newChar == "-")
        {
            e.Handled = currentText.Contains('-') || (textBox?.CaretIndex ?? 0) != 0;
            return;
        }

        // Allow decimal point only once
        if (newChar == "." || newChar == ",")
        {
            e.Handled = currentText.Contains('.') || currentText.Contains(',');
            return;
        }

        // Allow digits
        e.Handled = !char.IsDigit(newChar[0]);
    }

    /// <summary>
    /// Apply Scav Rep when losing focus
    /// </summary>
    private void TxtScavRep_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyScavRepFromTextBox();
    }

    /// <summary>
    /// Apply Scav Rep when pressing Enter
    /// </summary>
    private void TxtScavRep_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyScavRepFromTextBox();
            Keyboard.ClearFocus();
        }
    }

    /// <summary>
    /// Parse and apply Scav Rep from TextBox input
    /// </summary>
    private void ApplyScavRepFromTextBox()
    {
        var text = TxtScavRep.Text.Replace(',', '.');
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var scavRep))
        {
            // Clamp to valid range and round to 1 decimal place
            scavRep = Math.Round(Math.Clamp(scavRep, SettingsService.MinScavRep, SettingsService.MaxScavRep), 1);
            _settingsService.ScavRep = scavRep;
        }
        else
        {
            // Reset to current value if invalid
            TxtScavRep.Text = _settingsService.ScavRep.ToString("0.0");
        }
    }

    #endregion

    #region DSP Decode Count

    /// <summary>
    /// Update DSP Decode Count UI - highlight the selected button
    /// </summary>
    private void UpdateDspDecodeUI()
    {
        var dspCount = _settingsService.DspDecodeCount;

        // Reset all buttons to default style
        var buttons = new[] { BtnDsp0, BtnDsp1, BtnDsp2, BtnDsp3 };
        foreach (var btn in buttons)
        {
            btn.Background = (Brush)FindResource("BackgroundMediumBrush");
            btn.Foreground = (Brush)FindResource("TextPrimaryBrush");
        }

        // Highlight the selected button
        var selectedBtn = buttons[dspCount];
        selectedBtn.Background = (Brush)FindResource("AccentBrush");
        selectedBtn.Foreground = (Brush)FindResource("BackgroundDarkBrush");
    }

    /// <summary>
    /// Handle DSP Decode button click
    /// </summary>
    private void BtnDsp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out var count))
        {
            _settingsService.DspDecodeCount = count;
        }
    }

    /// <summary>
    /// Handle DSP Decode Count change from settings service
    /// </summary>
    private void OnDspDecodeCountChanged(object? sender, int newCount)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateDspDecodeUI();

            // Refresh quest list if visible
            _questListPage?.RefreshDisplay();
        });
    }

    #endregion

    #region Edition Settings

    /// <summary>
    /// Update Edition UI checkboxes
    /// </summary>
    private void UpdateEditionUI()
    {
        ChkEodEdition.IsChecked = _settingsService.HasEodEdition;
        ChkUnheardEdition.IsChecked = _settingsService.HasUnheardEdition;
    }

    /// <summary>
    /// Handle edition checkbox change
    /// </summary>
    private void ChkEdition_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        if (sender == ChkEodEdition)
        {
            _settingsService.HasEodEdition = ChkEodEdition.IsChecked == true;
        }
        else if (sender == ChkUnheardEdition)
        {
            _settingsService.HasUnheardEdition = ChkUnheardEdition.IsChecked == true;
        }
    }

    /// <summary>
    /// Handle edition change from settings service
    /// </summary>
    private void OnEditionChanged(object? sender, bool value)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateEditionUI();

            // Refresh quest list if visible
            _questListPage?.RefreshDisplay();
        });
    }

    #endregion

    #region Prestige Level

    /// <summary>
    /// Update Prestige Level UI
    /// </summary>
    private void UpdatePrestigeLevelUI()
    {
        var prestigeLevel = _settingsService.PrestigeLevel;
        TxtPrestigeLevel.Text = prestigeLevel.ToString();

        // Disable buttons at min/max prestige level
        BtnPrestigeDown.IsEnabled = prestigeLevel > SettingsService.MinPrestigeLevel;
        BtnPrestigeUp.IsEnabled = prestigeLevel < SettingsService.MaxPrestigeLevel;
    }

    /// <summary>
    /// Handle prestige level decrease
    /// </summary>
    private void BtnPrestigeDown_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.PrestigeLevel--;
    }

    /// <summary>
    /// Handle prestige level increase
    /// </summary>
    private void BtnPrestigeUp_Click(object sender, RoutedEventArgs e)
    {
        _settingsService.PrestigeLevel++;
    }

    /// <summary>
    /// Handle prestige level change from settings service
    /// </summary>
    private void OnPrestigeLevelChanged(object? sender, int newLevel)
    {
        Dispatcher.Invoke(() =>
        {
            UpdatePrestigeLevelUI();

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

    #region Profile Drawer

    private bool _isProfileDrawerOpen = false;

    /// <summary>
    /// Toggle profile drawer visibility
    /// </summary>
    private void BtnProfile_Click(object sender, RoutedEventArgs e)
    {
        _isProfileDrawerOpen = !_isProfileDrawerOpen;
        ProfileDrawer.Visibility = _isProfileDrawerOpen ? Visibility.Visible : Visibility.Collapsed;
        BtnProfile.Content = _isProfileDrawerOpen ? "▲ Profile" : "▼ Profile";
    }

    #endregion

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

        // Update cache size display
        UpdateCacheSizeDisplay();

        // Update font size display
        UpdateFontSizeDisplay();

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
    /// Navigate to Items tab and select a specific item by its ID
    /// </summary>
    public void NavigateToItem(string itemId)
    {
        // Switch to Items tab
        TabItems.IsChecked = true;
        PageContent.Content = _itemsPage;

        // Request item selection by ID
        _itemsPage?.SelectItem(itemId);
    }

    /// <summary>
    /// Navigate to Hideout tab and select a specific module
    /// </summary>
    public void NavigateToHideout(string stationId)
    {
        // Switch to Hideout tab
        TabHideout.IsChecked = true;
        PageContent.Content = _hideoutPage;

        // Request module selection
        _hideoutPage?.SelectModule(stationId);
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
            if (!WipeWarningDialog.ShowWarning(logPath, this))
            {
                return; // User cancelled
            }
        }

        // Proceed with sync
        PerformQuestSync(logPath);
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

            // Immediately hide LoadingOverlay to prevent animation collision
            // (HideLoadingOverlay animation may be cancelled by ShowSyncResultDialog's blur animation)
            LoadingOverlay.Visibility = Visibility.Collapsed;
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
                        // For started quests, complete all prerequisites in batch
                        var graphService = QuestGraphService.Instance;
                        if (!string.IsNullOrEmpty(task.NormalizedName))
                        {
                            var prereqs = graphService.GetAllPrerequisites(task.NormalizedName);
                            var prereqsToComplete = prereqs
                                .Where(p => progressService.GetStatus(p) != QuestStatus.Done)
                                .ToList();

                            if (prereqsToComplete.Count > 0)
                            {
                                // Use batch completion for better performance
                                progressService.CompleteQuestsBatch(prereqsToComplete);
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
    /// Show sync result confirmation dialog and apply changes
    /// </summary>
    private async void ShowSyncResultDialog(SyncResult result)
    {
        var selectedChanges = SyncResultDialog.ShowResult(result, this, out int alternativeCount);

        if (selectedChanges == null || selectedChanges.Count == 0)
        {
            return;
        }

        ShowLoadingOverlay(_loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 진행도 업데이트 중...",
            AppLanguage.JA => "クエスト進捗を更新中...",
            _ => "Updating quest progress..."
        });

        await _logSyncService.ApplyQuestChangesAsync(selectedChanges);

        HideLoadingOverlay();

        // Refresh quest list
        await LoadAndShowQuestListAsync();

        var totalUpdated = selectedChanges.Count;

        MessageBox.Show(
            _loc.CurrentLanguage switch
            {
                AppLanguage.KO => alternativeCount > 0
                    ? $"{totalUpdated}개의 퀘스트가 업데이트되었습니다.\n(선택 퀘스트 {alternativeCount}개 그룹 포함)"
                    : $"{totalUpdated}개의 퀘스트가 업데이트되었습니다.",
                AppLanguage.JA => alternativeCount > 0
                    ? $"{totalUpdated}件のクエストが更新されました。\n(選択クエスト {alternativeCount}グループ含む)"
                    : $"{totalUpdated}件のクエストが更新されました。",
                _ => alternativeCount > 0
                    ? $"{totalUpdated} quests have been updated.\n(Including {alternativeCount} optional quest groups)"
                    : $"{totalUpdated} quests have been updated."
            },
            _loc.CurrentLanguage switch { AppLanguage.KO => "동기화 완료", AppLanguage.JA => "同期完了", _ => "Sync Complete" },
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion

    #region In-Progress Quest Input

    /// <summary>
    /// Open in-progress quest input dialog
    /// </summary>
    private void BtnInProgressQuestInput_Click(object sender, RoutedEventArgs e)
    {
        HideSettingsOverlay();

        var result = InProgressQuestInputDialog.ShowDialog(this);
        if (result == null) return;

        // Apply the result
        ApplyInProgressQuestResult(result);
    }

    /// <summary>
    /// Apply the in-progress quest selection result
    /// </summary>
    private void ApplyInProgressQuestResult(InProgressQuestInputResult result)
    {
        var progressService = QuestProgressService.Instance;

        // Complete all prerequisites
        var completedCount = 0;
        foreach (var prereqName in result.PrerequisitesToComplete)
        {
            var prereqTask = progressService.GetTask(prereqName);
            if (prereqTask != null && progressService.GetStatus(prereqTask) != QuestStatus.Done)
            {
                progressService.CompleteQuest(prereqTask, completePrerequisites: false);
                completedCount++;
            }
        }

        // Refresh quest list
        _questListPage?.RefreshDisplay();

        // Show success message
        MessageBox.Show(
            string.Format(_loc.QuestsAppliedSuccess, result.SelectedQuests.Count, completedCount),
            _loc.CurrentLanguage switch { AppLanguage.KO => "Applied", AppLanguage.JA => "Applied", _ => "Applied" },
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    #endregion

    #region Cache Management

    /// <summary>
    /// Calculate total cache size
    /// </summary>
    private long CalculateCacheSize()
    {
        long totalSize = 0;

        // Cache directory (wiki pages, images, etc.)
        var cachePath = AppEnv.CachePath;
        if (Directory.Exists(cachePath))
        {
            totalSize += GetDirectorySize(cachePath);
        }

        return totalSize;
    }

    /// <summary>
    /// Calculate total data size (JSON files)
    /// </summary>
    private long CalculateDataSize()
    {
        long totalSize = 0;

        // Data directory (JSON files)
        var dataPath = AppEnv.DataPath;
        if (Directory.Exists(dataPath))
        {
            totalSize += GetDirectorySize(dataPath);
        }

        return totalSize;
    }

    /// <summary>
    /// Get directory size recursively
    /// </summary>
    private long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
            {
                size += file.Length;
            }
        }
        catch
        {
            // Ignore errors (access denied, etc.)
        }
        return size;
    }

    /// <summary>
    /// Format bytes to human readable string
    /// </summary>
    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Update cache size display
    /// </summary>
    private void UpdateCacheSizeDisplay()
    {
        var cacheSize = CalculateCacheSize();
        var dataSize = CalculateDataSize();
        var totalSize = cacheSize + dataSize;
        TxtCacheSize.Text = $"{FormatBytes(totalSize)} (Cache: {FormatBytes(cacheSize)}, Data: {FormatBytes(dataSize)})";
    }

    /// <summary>
    /// Clear cache button click handler
    /// </summary>
    private void BtnClearCache_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "캐시를 삭제하시겠습니까?\n(Wiki 페이지, 이미지 등이 삭제됩니다)",
                AppLanguage.JA => "キャッシュを削除しますか？\n（Wikiページ、画像などが削除されます）",
                _ => "Clear cache?\n(Wiki pages, images, etc. will be deleted)"
            },
            _loc.CurrentLanguage switch { AppLanguage.KO => "캐시 삭제", AppLanguage.JA => "キャッシュ削除", _ => "Clear Cache" },
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            BtnClearCache.IsEnabled = false;
            BtnClearAllData.IsEnabled = false;

            var cachePath = AppEnv.CachePath;
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
            }

            UpdateCacheSizeDisplay();

            MessageBox.Show(
                _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "캐시가 삭제되었습니다.\n데이터를 다시 가져오려면 Refresh 버튼을 누르세요.",
                    AppLanguage.JA => "キャッシュが削除されました。\nデータを再取得するにはRefreshボタンを押してください。",
                    _ => "Cache cleared.\nPress Refresh to re-download data."
                },
                _loc.CurrentLanguage switch { AppLanguage.KO => "완료", AppLanguage.JA => "完了", _ => "Done" },
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error clearing cache: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BtnClearCache.IsEnabled = true;
            BtnClearAllData.IsEnabled = true;
        }
    }

    /// <summary>
    /// Clear all data button click handler
    /// </summary>
    private async void BtnClearAllData_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "모든 데이터를 삭제하시겠습니까?\n(캐시, 퀘스트 데이터, 아이템 데이터 등이 삭제됩니다)\n\n⚠️ 퀘스트 진행 상태는 유지됩니다.",
                AppLanguage.JA => "すべてのデータを削除しますか？\n（キャッシュ、クエストデータ、アイテムデータなどが削除されます）\n\n⚠️ クエスト進行状況は保持されます。",
                _ => "Clear all data?\n(Cache, quest data, item data, etc. will be deleted)\n\n⚠️ Quest progress will be preserved."
            },
            _loc.CurrentLanguage switch { AppLanguage.KO => "데이터 초기화", AppLanguage.JA => "データ初期化", _ => "Clear All Data" },
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            BtnClearCache.IsEnabled = false;
            BtnClearAllData.IsEnabled = false;

            // Clear cache
            var cachePath = AppEnv.CachePath;
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
            }

            // Clear data files (user data is now in Config/user_data.db, safe to delete all)
            var dataPath = AppEnv.DataPath;
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, true);
            }

            UpdateCacheSizeDisplay();

            // Hide settings overlay
            HideSettingsOverlay();

            // Show confirmation
            MessageBox.Show(
                _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "캐시가 삭제되었습니다.",
                    AppLanguage.JA => "キャッシュが削除されました。",
                    _ => "Cache cleared."
                },
                _loc.CurrentLanguage switch { AppLanguage.KO => "완료", AppLanguage.JA => "完了", _ => "Done" },
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error clearing data: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BtnClearCache.IsEnabled = true;
            BtnClearAllData.IsEnabled = true;
        }
    }

    private void BtnFontSizeDown_Click(object sender, RoutedEventArgs e)
    {
        var currentSize = SettingsService.Instance.BaseFontSize;
        if (currentSize > SettingsService.MinFontSize)
        {
            SettingsService.Instance.BaseFontSize = currentSize - 1;
            UpdateFontSizeDisplay();
        }
    }

    private void BtnFontSizeUp_Click(object sender, RoutedEventArgs e)
    {
        var currentSize = SettingsService.Instance.BaseFontSize;
        if (currentSize < SettingsService.MaxFontSize)
        {
            SettingsService.Instance.BaseFontSize = currentSize + 1;
            UpdateFontSizeDisplay();
        }
    }

    private void BtnResetFontSize_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Instance.BaseFontSize = SettingsService.DefaultBaseFontSize;
        UpdateFontSizeDisplay();
    }

    private void UpdateFontSizeDisplay()
    {
        TxtCurrentFontSize.Text = SettingsService.Instance.BaseFontSize.ToString("0");
    }

    #endregion

    #region Full Screen Mode

    /// <summary>
    /// 전체화면 모드를 설정합니다.
    /// Map 페이지에서 호출됩니다.
    /// </summary>
    /// <param name="fullScreen">true이면 전체화면 모드 진입, false이면 해제</param>
    public void SetFullScreenMode(bool fullScreen)
    {
        _isFullScreen = fullScreen;

        if (fullScreen)
        {
            // 타이틀 바와 탭 네비게이션 숨기기
            TitleBar.Visibility = Visibility.Collapsed;
            TabNavigation.Visibility = Visibility.Collapsed;

            // 전체화면 모드 진입
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
        }
        else
        {
            // 타이틀 바와 탭 네비게이션 다시 표시
            TitleBar.Visibility = Visibility.Visible;
            TabNavigation.Visibility = Visibility.Visible;

            // 전체화면 모드 해제
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
        }
    }

    #endregion

    #region Data Migration

    /// <summary>
    /// Open folder dialog to select Config folder for migration
    /// </summary>
    private async void BtnDataMigration_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "이전 버전 Config 폴더 선택",
                AppLanguage.JA => "以前のバージョンのConfigフォルダを選択",
                _ => "Select Previous Version Config Folder"
            }
        };

        if (dialog.ShowDialog() != true) return;

        var selectedPath = dialog.FolderName;
        var migrationService = ConfigMigrationService.Instance;

        // Validate folder
        if (!migrationService.IsValidConfigFolder(selectedPath))
        {
            MessageBox.Show(
                _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "유효한 Config 폴더가 아닙니다.\nquest_progress.json, hideout_progress.json, item_inventory.json 또는 app_settings.json 파일이 필요합니다.",
                    AppLanguage.JA => "有効なConfigフォルダではありません。\nquest_progress.json、hideout_progress.json、item_inventory.json、またはapp_settings.jsonファイルが必要です。",
                    _ => "Invalid Config folder.\nMust contain quest_progress.json, hideout_progress.json, item_inventory.json, or app_settings.json."
                },
                _loc.CurrentLanguage switch { AppLanguage.KO => "오류", AppLanguage.JA => "エラー", _ => "Error" },
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Preview migration
        var preview = migrationService.PreviewMigration(selectedPath);

        // Show confirmation
        var confirmMessage = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => $"다음 데이터를 가져올 수 있습니다:\n\n" +
                              $"- 퀘스트 진행: {preview.QuestProgressCount}개\n" +
                              $"- 하이드아웃 진행: {preview.HideoutProgressCount}개\n" +
                              $"- 아이템 인벤토리: {preview.ItemInventoryCount}개\n" +
                              $"- 설정: {preview.SettingsCount}개\n\n" +
                              "가져오기를 진행하시겠습니까?\n(기존 데이터를 덮어씁니다)",
            AppLanguage.JA => $"以下のデータをインポートできます:\n\n" +
                              $"- クエスト進行: {preview.QuestProgressCount}件\n" +
                              $"- ハイドアウト進行: {preview.HideoutProgressCount}件\n" +
                              $"- アイテムインベントリ: {preview.ItemInventoryCount}件\n" +
                              $"- 設定: {preview.SettingsCount}件\n\n" +
                              "インポートを続行しますか？\n(既存のデータは上書きされます)",
            _ => $"The following data can be imported:\n\n" +
                 $"- Quest Progress: {preview.QuestProgressCount}\n" +
                 $"- Hideout Progress: {preview.HideoutProgressCount}\n" +
                 $"- Item Inventory: {preview.ItemInventoryCount}\n" +
                 $"- Settings: {preview.SettingsCount}\n\n" +
                 "Do you want to proceed?\n(Existing data will be overwritten)"
        };

        var confirmResult = MessageBox.Show(
            confirmMessage,
            _loc.CurrentLanguage switch { AppLanguage.KO => "데이터 가져오기", AppLanguage.JA => "データのインポート", _ => "Import Data" },
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmResult != MessageBoxResult.Yes) return;

        // Hide settings overlay
        HideSettingsOverlay();

        // Show loading overlay
        ShowLoadingOverlay(_loc.CurrentLanguage switch
        {
            AppLanguage.KO => "데이터 마이그레이션 중...",
            AppLanguage.JA => "データ移行中...",
            _ => "Migrating data..."
        });

        try
        {
            var progress = new Progress<string>(message =>
            {
                Dispatcher.Invoke(() => UpdateLoadingStatus(message));
            });

            var result = await migrationService.MigrateFromConfigFolderAsync(selectedPath, progress);

            // 즉시 LoadingOverlay 숨기기 (애니메이션 충돌 방지)
            LoadingOverlay.Visibility = Visibility.Collapsed;

            // Show result popup
            ShowMigrationResultDialog(result);

            // Reload pages to reflect new data
            await LoadAndShowQuestListAsync();
        }
        catch (Exception ex)
        {
            HideLoadingOverlay();
            MessageBox.Show(
                $"Migration failed: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Show migration result dialog
    /// </summary>
    private void ShowMigrationResultDialog(ConfigMigrationService.MigrationResult result)
    {
        MigrationResultDialog.Show(result, this);
    }

    #endregion

    #region App Update

    /// <summary>
    /// Start app update service
    /// </summary>
    private void StartAppUpdateService()
    {
        var updateService = UpdateService.Instance;

        // Initialize version display
        TxtCurrentVersion.Text = $"v{updateService.CurrentVersion.ToString(3)}";

        // Subscribe to update events
        updateService.UpdateCheckStarted += OnUpdateCheckStarted;
        updateService.UpdateCheckCompleted += OnUpdateCheckCompleted;

        // Start automatic update checking (every 3 minutes)
        updateService.StartAutoCheck();

        _log.Info("App update service started");
    }

    /// <summary>
    /// Update check started event handler
    /// </summary>
    private void OnUpdateCheckStarted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            TxtUpdateChecking.Visibility = Visibility.Visible;
            BtnCheckUpdate.IsEnabled = false;
        });
    }

    /// <summary>
    /// Update check completed event handler
    /// </summary>
    private void OnUpdateCheckCompleted(object? sender, UpdateCheckEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            TxtUpdateChecking.Visibility = Visibility.Collapsed;
            BtnCheckUpdate.IsEnabled = true;

            // Update last check time
            UpdateLastCheckTimeDisplay();

            if (e.IsUpdateAvailable && e.UpdateInfo != null)
            {
                // Show "Update to vX.X.X" button
                TxtUpdateVersion.Text = $"v{e.UpdateInfo.Version}";
                BtnUpdateAvailable.Visibility = Visibility.Visible;
                BtnCheckUpdate.Visibility = Visibility.Collapsed;

                // Update status to show update available
                TxtUpdateStatus.Text = _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "업데이트 있음",
                    AppLanguage.JA => "更新あり",
                    _ => "Update available"
                };
                TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA726"));

                _log.Info($"Update available: {e.UpdateInfo.Version}");
            }
            else if (e.Error != null)
            {
                // Show error status
                TxtUpdateStatus.Text = _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "확인 실패",
                    AppLanguage.JA => "確認失敗",
                    _ => "Check failed"
                };
                TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF5350"));

                _log.Warning($"Update check failed: {e.Error.Message}");
            }
            else
            {
                // No update available, keep showing "Check Update" button
                BtnUpdateAvailable.Visibility = Visibility.Collapsed;
                BtnCheckUpdate.Visibility = Visibility.Visible;

                // Update status to show up to date
                TxtUpdateStatus.Text = _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "최신 버전",
                    AppLanguage.JA => "最新版",
                    _ => "Up to date"
                };
                TxtUpdateStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
            }
        });
    }

    /// <summary>
    /// Update the last check time display
    /// </summary>
    private void UpdateLastCheckTimeDisplay()
    {
        var lastCheck = UpdateService.Instance.LastCheckTime;
        if (lastCheck.HasValue)
        {
            var timeAgo = DateTime.Now - lastCheck.Value;
            string timeText;

            if (timeAgo.TotalSeconds < 60)
            {
                timeText = _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "방금 전",
                    AppLanguage.JA => "たった今",
                    _ => "just now"
                };
            }
            else if (timeAgo.TotalMinutes < 60)
            {
                var mins = (int)timeAgo.TotalMinutes;
                timeText = _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => $"{mins}분 전",
                    AppLanguage.JA => $"{mins}分前",
                    _ => $"{mins}m ago"
                };
            }
            else
            {
                timeText = lastCheck.Value.ToString("HH:mm");
            }

            TxtLastCheckTime.Text = $"({timeText})";
        }
        else
        {
            TxtLastCheckTime.Text = "";
        }
    }

    /// <summary>
    /// Check Update button click
    /// </summary>
    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        _log.Debug("Manual update check triggered");
        await UpdateService.Instance.CheckForUpdateAsync();
    }

    /// <summary>
    /// Update Available button click - starts the update
    /// </summary>
    private void BtnUpdateAvailable_Click(object sender, RoutedEventArgs e)
    {
        var updateInfo = UpdateService.Instance.AvailableUpdate;
        if (updateInfo != null)
        {
            _log.Info($"User initiated update to version {updateInfo.Version}");
            UpdateService.Instance.StartUpdate();
        }
    }

    #endregion
}
