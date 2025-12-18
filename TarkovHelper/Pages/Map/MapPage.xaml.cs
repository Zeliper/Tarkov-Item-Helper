using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Logging;
using TarkovHelper.Services.Map;
using LegacyMapConfig = TarkovHelper.Models.Map.MapConfig;
using SvgStylePreprocessor = TarkovHelper.Services.Map.SvgStylePreprocessor;

namespace TarkovHelper.Pages.Map;

/// <summary>
/// 맵 위치 추적 페이지.
/// 스크린샷 폴더를 감시하고 플레이어 위치를 맵 위에 표시합니다.
/// </summary>
public partial class MapPage : UserControl
{
    private static readonly ILogger _log = Log.For<MapPage>();
    private readonly MapTrackerService? _trackerService;
    private readonly QuestObjectiveService _objectiveService = QuestObjectiveService.Instance;
    private readonly QuestProgressService _progressService = QuestProgressService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly OverlayMiniMapService _overlayService = OverlayMiniMapService.Instance;
    private string? _currentMapKey;
    private double _zoomLevel = 1.0;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;

    // 드래그 관련 필드
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;

    // 줌 레벨 프리셋
    private static readonly double[] ZoomPresets = { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };

    // 퀘스트 마커 관련 필드
    private readonly List<FrameworkElement> _questMarkerElements = new();
    private List<TaskObjectiveWithLocation> _currentMapObjectives = new();
    private bool _showQuestMarkers = true;
    private QuestMarkerStyle _questMarkerStyle = QuestMarkerStyle.DefaultWithName;
    private double _questNameTextSize = 12.0;
    private TaskObjectiveWithLocation? _selectedObjective;
    private FrameworkElement? _selectedMarkerElement;

    // 탈출구 마커 관련 필드
    private readonly ExtractService _extractService = ExtractService.Instance;
    private readonly List<FrameworkElement> _extractMarkerElements = new();
    private bool _showExtractMarkers = true;
    private bool _showPmcExtracts = true;
    private bool _showScavExtracts = true;
    private bool _showTransitExtracts = true;
    private double _extractNameTextSize = 10.0;
    private bool _hideCompletedObjectives = true;

    // 로그 맵 감시 서비스 (자동 맵 전환용)
    private readonly LogMapWatcherService _logMapWatcher = LogMapWatcherService.Instance;

    // 퀘스트 드로어 필터링 옵션
    private string _drawerStatusFilter = "All";
    private string _drawerTypeFilter = "All";
    private bool _drawerCurrentMapOnly = true;  // XAML 기본값과 일치
    private bool _drawerGroupByQuest = true;    // XAML 기본값과 일치

    // 겹치는 마커 그룹 관련
    private readonly List<FrameworkElement> _groupedMarkerElements = new();
    private Popup? _markerGroupPopup;

    // 보정 모드 관련 필드
    private readonly MapCalibrationService _calibrationService = MapCalibrationService.Instance;
#pragma warning disable CS0649 // 보정 모드 기능 미구현 - 추후 구현 예정
    private bool _isCalibrationMode;
#pragma warning restore CS0649
    private FrameworkElement? _draggingExtractMarker;
    private Point _extractDragStartPoint;
    private double _extractMarkerOriginalLeft;
    private double _extractMarkerOriginalTop;
    private MapExtract? _draggingExtract;

    // 층 전환 관련 필드
    private string? _currentFloorId;

    public MapPage()
    {
        try
        {
            InitializeComponent();

            _trackerService = MapTrackerService.Instance;

            // 이벤트 연결
            _trackerService.PositionUpdated += OnPositionUpdated;
            _trackerService.ErrorOccurred += OnErrorOccurred;
            _trackerService.StatusMessage += OnStatusMessage;
            _trackerService.WatchingStateChanged += OnWatchingStateChanged;
            _loc.LanguageChanged += OnLanguageChanged;
            MapMarkerDbService.Instance.DataRefreshed += OnDatabaseRefreshed;
            QuestObjectiveDbService.Instance.DataRefreshed += OnDatabaseRefreshed;

            Loaded += MapTrackerPage_Loaded;
            Unloaded += MapTrackerPage_Unloaded;

            // 줌 콤보박스 초기화
            InitializeZoomComboBox();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"MapTrackerPage initialization error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InitializeZoomComboBox()
    {
        CmbZoomLevel.Items.Clear();
        foreach (var preset in ZoomPresets)
        {
            CmbZoomLevel.Items.Add($"{preset * 100:F0}%");
        }
        CmbZoomLevel.Text = "100%";
    }

    private async void MapTrackerPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // 페이지 로드 시 Trail 초기화
            _trackerService?.ClearTrail();
            TrailPath.Points.Clear();

            LoadSettings();
            PopulateMapComboBox();

            // 로그 맵 감시 시작 (자동 맵 전환용) - RestoreMapState 전에 호출하여 마지막 플레이 맵 우선
            StartLogMapWatching();

            // 로그에서 맵이 감지되지 않은 경우에만 저장된 맵 상태 복원
            RestoreMapState();

            UpdateUI();

            // 퀘스트 목표 데이터 로드
            await LoadQuestObjectivesAsync();

            // 탈출구 데이터 로드
            await LoadExtractsAsync();

            // 층 감지 데이터 로드 (자동 층 전환용)
            await FloorDetectionService.Instance.LoadFloorRangesAsync();

            // Drawer 기본 열기 및 내용 새로고침
            OpenQuestDrawer();

            // 퀘스트 진행 상태 변경 이벤트 구독
            _progressService.ProgressChanged += OnQuestProgressChanged;
            _progressService.ObjectiveProgressChanged += OnObjectiveProgressChanged;

            // Global Keyboard Hook 시작 (NumPad 키로 층 변경)
            GlobalKeyboardHookService.Instance.FloorKeyPressed += OnFloorKeyPressed;
            GlobalKeyboardHookService.Instance.IsEnabled = true;

            // 오버레이 미니맵 서비스 초기화
            await InitializeOverlayServiceAsync();

            // 자동 Tracking 시작 (Map 탭 활성화 시)
            StartAutoTracking();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"MapTrackerPage load error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MapTrackerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // 현재 맵 상태 저장
        SaveMapState();

        // 이벤트 구독 해제
        _progressService.ProgressChanged -= OnQuestProgressChanged;
        _progressService.ObjectiveProgressChanged -= OnObjectiveProgressChanged;
        MapMarkerDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;
        QuestObjectiveDbService.Instance.DataRefreshed -= OnDatabaseRefreshed;

        // Global Keyboard Hook 중지
        GlobalKeyboardHookService.Instance.FloorKeyPressed -= OnFloorKeyPressed;
        GlobalKeyboardHookService.Instance.IsEnabled = false;

        // 오버레이 숨기기 (Map 탭 이탈 시)
        _overlayService.HideOverlay();

        // 자동 Tracking 중지 (다른 탭으로 이동 시)
        StopAutoTracking();

        // 로그 맵 감시 중지
        StopLogMapWatching();
    }

    private void SaveMapState()
    {
        // 모든 설정을 SettingsService(DB)에 저장
        var settingsService = SettingsService.Instance;

        settingsService.MapLastSelectedMap = _currentMapKey;
        settingsService.MapLastZoomLevel = _zoomLevel;
        settingsService.MapLastTranslateX = MapTranslate.X;
        settingsService.MapLastTranslateY = MapTranslate.Y;
    }

    private async void OnDatabaseRefreshed(object? sender, EventArgs e)
    {
        // DB 업데이트 후 마커 데이터 다시 로드
        await Dispatcher.InvokeAsync(async () =>
        {
            // 퀘스트 목표 데이터 다시 로드
            await LoadQuestObjectivesAsync();

            // 탈출구 데이터 다시 로드
            await LoadExtractsAsync();

            // 퀘스트 마커 갱신
            RefreshQuestMarkers();
        });
    }

    private void RestoreMapState()
    {
        // SettingsService(DB)에서 맵 상태 복원
        var settingsService = SettingsService.Instance;

        // 로그에서 이미 맵이 감지되어 선택된 경우 맵 선택 건너뜀
        // _currentMapKey가 없는 경우에만 저장된 맵 복원
        var lastSelectedMap = settingsService.MapLastSelectedMap;
        if (string.IsNullOrEmpty(_currentMapKey) && !string.IsNullOrEmpty(lastSelectedMap))
        {
            // 맵 선택 복원
            for (int i = 0; i < CmbMapSelect.Items.Count; i++)
            {
                if (CmbMapSelect.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, lastSelectedMap, StringComparison.OrdinalIgnoreCase))
                {
                    CmbMapSelect.SelectedIndex = i;
                    break;
                }
            }

            // 마지막 맵 복원 시 100% 배율로 리셋
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SetZoom(1.0);
                CenterMapInView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnLanguageChanged(object? sender, AppLanguage e)
    {
        UpdateLocalizedText();
    }

    private void UpdateLocalizedText()
    {
        // 상단 컨트롤 바
        TxtPageTitle.Text = _loc.MapPositionTracker;
        TxtMapLabel.Text = _loc.MapLabel;
        ChkShowQuestMarkers.Content = _loc.QuestMarkers;
        ChkShowExtractMarkers.Content = _loc.Extracts;
        BtnClearTrail.Content = _loc.ClearTrail;
        BtnFullScreen.Content = _loc.FullScreen;
        BtnExitFullScreen.Content = _loc.ExitFullScreen;
        BtnSettings.Content = _loc.Settings;

        // 추적 버튼 (상태에 따라)
        var isTracking = _trackerService?.IsWatching ?? false;
        BtnToggleTracking.Content = isTracking ? _loc.StopTracking : _loc.StartTracking;

        // 상태 표시 바
        if (!isTracking)
        {
            TxtStatus.Text = _loc.StatusWaiting;
        }
        TxtPositionLabel.Text = _loc.PositionLabel;
        TxtLastUpdateLabel.Text = _loc.LastUpdateLabel;

        // 퀘스트 드로어
        TxtQuestObjectivesTitle.Text = _loc.QuestObjectives;
        TxtMapProgressLabel.Text = _loc.ProgressOnThisMap;

        // 필터 콤보박스
        CmbStatusAll.Content = _loc.FilterAll;
        CmbStatusIncomplete.Content = _loc.FilterIncomplete;
        CmbStatusCompleted.Content = _loc.FilterCompleted;

        CmbTypeAll.Content = _loc.FilterAllTypes;
        CmbTypeVisit.Content = _loc.FilterVisit;
        CmbTypeMark.Content = _loc.FilterMark;
        CmbTypePlant.Content = _loc.FilterPlant;
        CmbTypeExtract.Content = _loc.FilterExtract;
        CmbTypeFind.Content = _loc.FilterFind;

        TxtCurrentMapOnly.Text = _loc.ThisMapOnly;
        TxtGroupByQuest.Text = _loc.GroupByQuest;

        // 설정 패널
        TxtSettingsTitle.Text = _loc.Settings;
        TxtScreenshotFolderLabel.Text = _loc.ScreenshotFolder;
        BtnAutoDetect.Content = _loc.AutoDetect;
        BtnBrowseFolder.Content = _loc.Browse;
        TxtMarkerSettingsLabel.Text = _loc.MarkerSettings;
        ChkHideCompletedObjectives.Content = _loc.HideCompletedObjectives;
        TxtQuestStyleLabel.Text = _loc.QuestStyle;
        TxtQuestNameSizeLabel.Text = _loc.QuestNameSize;
        TxtQuestMarkerSizeLabel.Text = _loc.QuestMarkerSize;
        TxtPlayerMarkerSizeLabel.Text = _loc.PlayerMarkerSize;
        TxtExtractSettingsLabel.Text = _loc.ExtractSettings;
        ChkShowPmcExtracts.Content = _loc.PmcExtracts;
        ChkShowScavExtracts.Content = _loc.ScavExtracts;
        TxtExtractNameSizeLabel.Text = _loc.ExtractNameSize;

        // 마커 색상 설정
        TxtMarkerColorsLabel.Text = _loc.MarkerColors;
        TxtColorVisit.Text = _loc.FilterVisit;
        TxtColorMark.Text = _loc.FilterMark;
        TxtColorPlant.Text = _loc.FilterPlant;
        TxtColorExtract.Text = _loc.FilterExtract;
        TxtColorFind.Text = _loc.FilterFind;
        BtnResetColors.Content = _loc.ResetColors;

        // 퀘스트 스타일 옵션
        CmbStyleIconOnly.Content = _loc.StyleIconOnly;
        CmbStyleGreenCircle.Content = _loc.StyleGreenCircle;
        CmbStyleIconWithName.Content = _loc.StyleIconWithName;
        CmbStyleCircleWithName.Content = _loc.StyleCircleWithName;

        // 맵 없음 안내
        TxtNoMapImage.Text = _loc.NoMapImage;
        TxtAddMapImageHint.Text = _loc.AddMapImageHint;
        TxtSetImagePathHint.Text = _loc.SetImagePathHint;

        // 줌 컨트롤
        BtnResetView.Content = _loc.ResetView;

        // 퀘스트 드로어가 열려있으면 새로고침
        if (QuestDrawerPanel?.Visibility == Visibility.Visible)
        {
            RefreshQuestDrawer();
        }
    }

    private void LoadSettings()
    {
        // 모든 설정을 SettingsService(DB)에서 로드
        var settingsService = SettingsService.Instance;

        // 스크린샷 폴더 설정 (사용자 지정 경로 또는 자동 감지)
        var screenshotPath = settingsService.MapScreenshotPath;
        if (string.IsNullOrEmpty(screenshotPath))
        {
            screenshotPath = Models.Map.MapTrackerSettings.TryDetectScreenshotFolder() ??
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\Escape from Tarkov\\Screenshots";
        }
        TxtScreenshotFolder.Text = screenshotPath;

        // 마커 크기 설정
        SliderMarkerSize.Value = settingsService.MapQuestMarkerSize;
        SliderPlayerMarkerSize.Value = settingsService.MapPlayerMarkerSize;
        UpdatePlayerMarkerSize(settingsService.MapPlayerMarkerSize);

        // 탈출구 설정 로드 (DB에서)
        _showPmcExtracts = settingsService.MapShowPmcExtracts;
        _showScavExtracts = settingsService.MapShowScavExtracts;
        _showTransitExtracts = settingsService.MapShowTransits;
        _extractNameTextSize = settingsService.MapExtractNameSize;
        _showExtractMarkers = settingsService.MapShowExtracts;
        _showQuestMarkers = settingsService.MapShowQuests;
        _questNameTextSize = settingsService.MapQuestNameSize;

        // 퀘스트 마커 스타일 및 완료 목표 숨기기 설정
        _questMarkerStyle = (QuestMarkerStyle)settingsService.MapQuestMarkerStyle;
        _hideCompletedObjectives = settingsService.MapHideCompletedObjectives;

        // UI 업데이트 (이벤트 트리거 방지를 위해 직접 설정)
        ChkShowPmcExtracts.IsChecked = _showPmcExtracts;
        ChkShowScavExtracts.IsChecked = _showScavExtracts;
        ChkShowTransitExtracts.IsChecked = _showTransitExtracts;
        SliderExtractTextSize.Value = _extractNameTextSize;
        ChkShowExtractMarkers.IsChecked = _showExtractMarkers;
        ChkShowQuestMarkers.IsChecked = _showQuestMarkers;
        CmbQuestMarkerStyle.SelectedIndex = (int)_questMarkerStyle;
        SliderQuestNameTextSize.Value = _questNameTextSize;
        ChkHideCompletedObjectives.IsChecked = _hideCompletedObjectives;

        // 컨테이너 가시성 설정
        if (ExtractMarkersContainer != null)
            ExtractMarkersContainer.Visibility = _showExtractMarkers ? Visibility.Visible : Visibility.Collapsed;
        if (QuestMarkersContainer != null)
            QuestMarkersContainer.Visibility = _showQuestMarkers ? Visibility.Visible : Visibility.Collapsed;

        // 마커 색상 UI 업데이트
        UpdateMarkerColorUI();
    }

    private void PopulateMapComboBox()
    {
        if (_trackerService == null) return;
        CmbMapSelect.Items.Clear();
        foreach (var mapKey in _trackerService.GetAllMapKeys())
        {
            var config = _trackerService.GetMapConfig(mapKey);
            CmbMapSelect.Items.Add(new ComboBoxItem
            {
                Content = config?.DisplayName ?? mapKey,
                Tag = mapKey
            });
        }

        if (CmbMapSelect.Items.Count > 0)
            CmbMapSelect.SelectedIndex = 0;
    }

    private void UpdateUI()
    {
        // 감시 상태에 따른 UI 업데이트
        var isWatching = _trackerService?.IsWatching ?? false;
        BtnToggleTracking.Content = isWatching ? _loc.StopTracking : _loc.StartTracking;

        var successBrush = TryFindResource("SuccessBrush") as Brush ?? Brushes.Green;
        var secondaryBrush = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        StatusIndicator.Fill = isWatching ? successBrush : secondaryBrush;
        TxtStatus.Text = isWatching ? _loc.StatusTracking : _loc.StatusWaiting;

        // Localization 적용
        UpdateLocalizedText();
    }

    #region 이벤트 핸들러 - 서비스

    private void OnPositionUpdated(object? sender, ScreenPosition position)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateMarkerPosition(position);
            UpdateTrailPath();
            UpdateCoordinatesDisplay(position);

            // Y 좌표(높이)를 기반으로 층 자동 전환
            TryAutoSwitchFloor(position);
        });
    }

    /// <summary>
    /// Y 좌표(높이)를 기반으로 층을 자동 전환합니다.
    /// DB의 MapFloorLocations 테이블에 설정된 Y 범위를 사용합니다.
    /// </summary>
    private void TryAutoSwitchFloor(ScreenPosition position)
    {
        // 현재 맵이 없거나 단일 층 맵이면 스킵
        if (string.IsNullOrEmpty(_currentMapKey))
            return;

        if (CmbFloorSelect.Visibility != Visibility.Visible)
            return;

        // 원본 EFT 좌표에서 Y(높이) 가져오기
        var originalPos = position.OriginalPosition;
        if (originalPos == null)
            return;

        // FloorDetectionService로 층 감지
        var detectedFloorId = FloorDetectionService.Instance.DetectFloor(_currentMapKey, originalPos.Y);
        // 어떤 Boundary에도 해당하지 않으면 main 층으로 기본 설정
        if (string.IsNullOrEmpty(detectedFloorId))
            detectedFloorId = "main";

        // 현재 층과 같으면 스킵
        if (string.Equals(_currentFloorId, detectedFloorId, StringComparison.OrdinalIgnoreCase))
            return;

        // 층 콤보박스에서 해당 층 찾아서 선택
        for (int i = 0; i < CmbFloorSelect.Items.Count; i++)
        {
            if (CmbFloorSelect.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, detectedFloorId, StringComparison.OrdinalIgnoreCase))
            {
                CmbFloorSelect.SelectedIndex = i;
                break;
            }
        }
    }

    private void OnErrorOccurred(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = $"오류: {message}";
            TxtStatus.Foreground = TryFindResource("WarningBrush") as Brush ?? Brushes.Orange;
        });
    }

    private void OnStatusMessage(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = message;
            TxtStatus.Foreground = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        });
    }

    private void OnWatchingStateChanged(object? sender, bool isWatching)
    {
        Dispatcher.Invoke(UpdateUI);
    }

    #endregion

    #region 자동 Tracking 및 맵 감시

    /// <summary>
    /// Map 탭 활성화 시 자동으로 Tracking을 시작합니다.
    /// </summary>
    private void StartAutoTracking()
    {
        if (_trackerService == null) return;

        // 이미 감시 중이면 스킵
        if (_trackerService.IsWatching) return;

        // 스크린샷 폴더가 설정되어 있으면 자동 시작
        if (!string.IsNullOrEmpty(_trackerService.Settings.ScreenshotFolderPath))
        {
            _trackerService.StartTracking();
        }
    }

    /// <summary>
    /// Map 탭 비활성화 시 Tracking을 중지합니다.
    /// </summary>
    private void StopAutoTracking()
    {
        if (_trackerService == null) return;

        if (_trackerService.IsWatching)
        {
            _trackerService.StopTracking();
        }
    }

    /// <summary>
    /// 로그 맵 감시를 시작합니다 (자동 맵 전환용).
    /// </summary>
    private void StartLogMapWatching()
    {
        // 이벤트 구독
        _logMapWatcher.MapChanged += OnLogMapChanged;

        // Settings에서 설정된 로그 폴더 경로 사용
        var logFolderPath = SettingsService.Instance.LogFolderPath;
        _logMapWatcher.StartWatching(logFolderPath);
    }

    /// <summary>
    /// 로그 맵 감시를 중지합니다.
    /// </summary>
    private void StopLogMapWatching()
    {
        // 이벤트 구독 해제
        _logMapWatcher.MapChanged -= OnLogMapChanged;

        // 로그 감시 중지
        _logMapWatcher.StopWatching();
    }

    /// <summary>
    /// 로그에서 맵 변경이 감지되었을 때 호출됩니다.
    /// 자동으로 맵을 전환하고 Trail을 초기화합니다.
    /// </summary>
    private void OnLogMapChanged(object? sender, MapChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // 현재 맵과 같으면 스킵
            if (string.Equals(_currentMapKey, e.NewMapKey, StringComparison.OrdinalIgnoreCase))
                return;

            // 맵 콤보박스에서 해당 맵 찾기
            for (int i = 0; i < CmbMapSelect.Items.Count; i++)
            {
                if (CmbMapSelect.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, e.NewMapKey, StringComparison.OrdinalIgnoreCase))
                {
                    // 새 맵 시작이므로 Trail 먼저 초기화 (맵 전환 전에)
                    _trackerService?.ClearTrail();
                    TrailPath.Points.Clear();
                    PlayerMarker.Visibility = Visibility.Collapsed;
                    PlayerDot.Visibility = Visibility.Collapsed;
                    TxtCoordinates.Text = "--";
                    TxtLastUpdateTime.Text = "--";

                    // 맵 선택 변경 (이로 인해 CmbMapSelect_SelectionChanged가 호출됨)
                    CmbMapSelect.SelectedIndex = i;

                    // 로그에서 맵 감지 시 100% 배율로 리셋
                    SetZoom(1.0);

                    TxtStatus.Text = $"맵 감지: {e.NewMapKey}";
                    break;
                }
            }
        });
    }

    #endregion

    #region 이벤트 핸들러 - UI

    private void BtnToggleTracking_Click(object sender, RoutedEventArgs e)
    {
        if (_trackerService == null) return;
        if (_trackerService.IsWatching)
        {
            _trackerService.StopTracking();
        }
        else
        {
            _trackerService.StartTracking();
        }
    }

    private void BtnClearTrail_Click(object sender, RoutedEventArgs e)
    {
        _trackerService?.ClearTrail();
        TrailPath.Points.Clear();
        PlayerMarker.Visibility = Visibility.Collapsed;
        PlayerDot.Visibility = Visibility.Collapsed;
        TxtCoordinates.Text = "--";
        TxtLastUpdateTime.Text = "--";
    }

    private async Task InitializeOverlayServiceAsync()
    {
        try
        {
            await _overlayService.InitializeAsync();
            _log.Info("Overlay MiniMap service initialized");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to initialize Overlay MiniMap service", ex);
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPanel(true);
    }

    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPanel(false);
    }

    private void ToggleSettingsPanel(bool show)
    {
        if (show)
        {
            SettingsColumn.Width = new GridLength(320);
            SettingsPanel.Visibility = Visibility.Visible;
            LoadCurrentMapSettings();
        }
        else
        {
            SettingsColumn.Width = new GridLength(0);
            SettingsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void CmbMapSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbMapSelect.SelectedItem is ComboBoxItem item && item.Tag is string mapKey)
        {
            _currentMapKey = mapKey;
            _trackerService?.SetCurrentMap(mapKey);

            // 맵 변경 시 Trail 초기화
            _trackerService?.ClearTrail();
            TrailPath.Points.Clear();
            PlayerMarker.Visibility = Visibility.Collapsed;
            PlayerDot.Visibility = Visibility.Collapsed;

            // 층 콤보박스 업데이트
            UpdateFloorComboBox(mapKey);

            LoadMapImage(mapKey);
            LoadCurrentMapSettings();

            // 맵별 마커 스케일 적용 (플레이어 마커 크기 업데이트)
            var playerMarkerSize = _trackerService?.Settings.PlayerMarkerSize ?? 16;
            UpdatePlayerMarkerSize(playerMarkerSize);

            // 초기화 완료 후에만 호출
            if (_objectiveService.IsLoaded)
            {
                RefreshQuestMarkers();
            }
            if (_extractService.IsLoaded)
            {
                RefreshExtractMarkers();
            }

            // 패널이 열려있으면 내용 갱신 (닫지 않음)
            if (QuestDrawerPanel?.Visibility == Visibility.Visible)
            {
                RefreshQuestDrawer();
            }
        }
    }

    /// <summary>
    /// Global Keyboard Hook에서 NumPad 키가 눌렸을 때 호출됩니다.
    /// NumPad 0-5로 층을 선택합니다.
    /// </summary>
    private void OnFloorKeyPressed(int floorIndex)
    {
        // 층 콤보박스가 보이지 않으면 (단일 층 맵) 무시
        if (CmbFloorSelect.Visibility != Visibility.Visible)
            return;

        // 유효한 인덱스인지 확인
        if (floorIndex < 0 || floorIndex >= CmbFloorSelect.Items.Count)
            return;

        // 층 선택
        CmbFloorSelect.SelectedIndex = floorIndex;
    }

    private void CmbFloorSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbFloorSelect.SelectedItem is ComboBoxItem floorItem && floorItem.Tag is string floorId)
        {
            if (_currentFloorId != floorId)
            {
                _currentFloorId = floorId;

                // 층이 변경되면 맵 이미지 다시 로드 (화면 위치는 유지)
                if (!string.IsNullOrEmpty(_currentMapKey))
                {
                    LoadMapImage(_currentMapKey, centerView: false);

                    // 마커들도 새로고침 (층 정보에 따른 표시 업데이트)
                    RefreshExtractMarkers();
                    RefreshQuestMarkers();
                }
            }
        }
    }

    /// <summary>
    /// 층 콤보박스를 현재 맵의 층 정보로 업데이트합니다.
    /// </summary>
    private void UpdateFloorComboBox(string mapKey)
    {
        var config = _trackerService?.GetMapConfig(mapKey);
        var floors = config?.Floors;

        CmbFloorSelect.Items.Clear();
        _currentFloorId = null;

        if (floors == null || floors.Count == 0)
        {
            // 단일 층 맵: 층 선택 UI 숨김
            TxtFloorLabel.Visibility = Visibility.Collapsed;
            CmbFloorSelect.Visibility = Visibility.Collapsed;
            return;
        }

        // 다층 맵: 층 선택 UI 표시
        TxtFloorLabel.Visibility = Visibility.Visible;
        CmbFloorSelect.Visibility = Visibility.Visible;

        // 층 목록을 Order 순으로 정렬하여 추가
        var sortedFloors = floors.OrderBy(f => f.Order).ToList();
        int defaultIndex = 0;

        for (int i = 0; i < sortedFloors.Count; i++)
        {
            var floor = sortedFloors[i];
            CmbFloorSelect.Items.Add(new ComboBoxItem
            {
                Content = floor.DisplayName,
                Tag = floor.LayerId
            });

            if (floor.IsDefault)
            {
                defaultIndex = i;
            }
        }

        // 기본 층 선택
        if (CmbFloorSelect.Items.Count > 0)
        {
            CmbFloorSelect.SelectedIndex = defaultIndex;
            _currentFloorId = sortedFloors[defaultIndex].LayerId;
        }
    }

    private void BtnAutoDetect_Click(object sender, RoutedEventArgs e)
    {
        var detectedPath = MapTrackerSettings.TryDetectScreenshotFolder();
        if (!string.IsNullOrEmpty(detectedPath))
        {
            TxtScreenshotFolder.Text = detectedPath;
            _trackerService?.ChangeScreenshotFolder(detectedPath);
            // DB에 저장
            SettingsService.Instance.MapScreenshotPath = detectedPath;
            MessageBox.Show($"스크린샷 폴더를 찾았습니다:\n{detectedPath}", "자동 탐지 성공",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            // 가능한 경로 목록 표시
            var possiblePaths = MapTrackerSettings.GetPossibleScreenshotPaths();
            if (possiblePaths.Count > 0)
            {
                var pathList = string.Join("\n", possiblePaths);
                MessageBox.Show($"스크린샷 폴더를 자동으로 찾지 못했습니다.\n\n발견된 EFT 폴더:\n{pathList}\n\n수동으로 선택해주세요.",
                    "자동 탐지 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show("EFT 스크린샷 폴더를 찾을 수 없습니다.\n수동으로 폴더를 선택해주세요.",
                    "자동 탐지 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "스크린샷 폴더 선택",
            InitialDirectory = TxtScreenshotFolder.Text
        };

        if (dialog.ShowDialog() == true)
        {
            TxtScreenshotFolder.Text = dialog.FolderName;
            _trackerService?.ChangeScreenshotFolder(dialog.FolderName);
            // DB에 저장
            SettingsService.Instance.MapScreenshotPath = dialog.FolderName;
        }
    }

    private void SliderMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMarkerSize != null)
        {
            var size = (int)e.NewValue;
            TxtMarkerSize.Text = size.ToString();
            UpdateMarkerSize(size);

            // DB에 저장
            SettingsService.Instance.MapQuestMarkerSize = size;
        }
    }

    private void SliderPlayerMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtPlayerMarkerSize != null)
        {
            var size = (int)e.NewValue;
            TxtPlayerMarkerSize.Text = size.ToString();
            UpdatePlayerMarkerSize(size);

            // DB에 저장
            SettingsService.Instance.MapPlayerMarkerSize = size;
        }
    }

    private void CmbQuestMarkerStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbQuestMarkerStyle.SelectedIndex < 0) return;

        _questMarkerStyle = (QuestMarkerStyle)CmbQuestMarkerStyle.SelectedIndex;

        // 설정 저장 (SettingsService를 통해 DB에 저장)
        SettingsService.Instance.MapQuestMarkerStyle = (int)_questMarkerStyle;

        // 마커 다시 그리기
        RefreshQuestMarkers();
    }

    private void SliderQuestNameTextSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtQuestNameTextSize != null)
        {
            var size = (int)e.NewValue;
            TxtQuestNameTextSize.Text = size.ToString();
            _questNameTextSize = size;

            // DB에 저장
            SettingsService.Instance.MapQuestNameSize = size;

            // 마커 다시 그리기
            RefreshQuestMarkers();
        }
    }

    private void BtnFullScreen_Click(object sender, RoutedEventArgs e)
    {
        EnterFullScreen();
    }

    private void BtnExitFullScreen_Click(object sender, RoutedEventArgs e)
    {
        ExitFullScreen();
    }

    private void EnterFullScreen()
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        if (mainWindow == null) return;

        // MainWindow의 공통 메뉴바 숨기기
        mainWindow.SetFullScreenMode(true);

        // Exit Full Screen 버튼 표시
        BtnExitFullScreen.Visibility = Visibility.Visible;
    }

    private void ExitFullScreen()
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        if (mainWindow == null) return;

        // MainWindow의 공통 메뉴바 다시 표시
        mainWindow.SetFullScreenMode(false);

        // Exit Full Screen 버튼 숨기기
        BtnExitFullScreen.Visibility = Visibility.Collapsed;
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        // 다음 프리셋으로 줌
        var nextPreset = ZoomPresets.FirstOrDefault(p => p > _zoomLevel);
        var newZoom = nextPreset > 0 ? nextPreset : _zoomLevel * 1.25;
        ZoomToMouse(newZoom);
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        // 이전 프리셋으로 줌
        var prevPreset = ZoomPresets.LastOrDefault(p => p < _zoomLevel);
        var newZoom = prevPreset > 0 ? prevPreset : _zoomLevel * 0.8;
        ZoomToMouse(newZoom);
    }

    /// <summary>
    /// 마우스 위치를 중심으로 줌합니다.
    /// </summary>
    private void ZoomToMouse(double newZoom)
    {
        var oldZoom = _zoomLevel;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        // 마우스 위치 가져오기 (MapViewerGrid 기준)
        var mousePos = Mouse.GetPosition(MapViewerGrid);

        // 마우스 위치에서 캔버스상의 실제 좌표 계산
        var canvasX = (mousePos.X - MapTranslate.X) / oldZoom;
        var canvasY = (mousePos.Y - MapTranslate.Y) / oldZoom;

        // 줌 후에도 마우스 위치가 동일한 캔버스 좌표를 가리키도록 translate 조정
        MapTranslate.X = mousePos.X - canvasX * newZoom;
        MapTranslate.Y = mousePos.Y - canvasY * newZoom;

        SetZoom(newZoom);
    }

    private void CmbZoomLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbZoomLevel.SelectedItem is string selected)
        {
            ParseAndSetZoom(selected);
        }
    }

    private void CmbZoomLevel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ParseAndSetZoom(CmbZoomLevel.Text);
            e.Handled = true;
        }
    }

    private void ParseAndSetZoom(string zoomText)
    {
        // "100%" 형식에서 숫자 추출
        var text = zoomText.Trim().TrimEnd('%');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            SetZoom(percent / 100.0);
        }
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        // 줌을 100%로 초기화하고 맵을 중앙에 배치
        SetZoom(1.0);
        CenterMapInView();
    }

    #region 드래그 이벤트 핸들러

    private void MapViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (NoMapPanel.Visibility == Visibility.Visible) return;

        _isDragging = true;
        _dragStartPoint = e.GetPosition(MapViewerGrid);
        _dragStartTranslateX = MapTranslate.X;
        _dragStartTranslateY = MapTranslate.Y;
        MapViewerGrid.CaptureMouse();
        MapCanvas.Cursor = Cursors.ScrollAll;
    }

    private void MapViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            MapViewerGrid.ReleaseMouseCapture();
            MapCanvas.Cursor = Cursors.Arrow;
        }
    }

    private void MapViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPoint = e.GetPosition(MapViewerGrid);
        var deltaX = currentPoint.X - _dragStartPoint.X;
        var deltaY = currentPoint.Y - _dragStartPoint.Y;

        MapTranslate.X = _dragStartTranslateX + deltaX;
        MapTranslate.Y = _dragStartTranslateY + deltaY;
    }

    private void MapViewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 마우스 위치를 중심으로 줌 (MapViewerGrid 기준)
        var mousePos = e.GetPosition(MapViewerGrid);
        var oldZoom = _zoomLevel;

        // 줌 계산
        var zoomFactor = e.Delta > 0 ? 1.15 : 0.87;
        var newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        // 마우스 위치에서 캔버스상의 실제 좌표 계산
        // mousePos = canvasPos * oldZoom + translate
        // canvasPos = (mousePos - translate) / oldZoom
        var canvasX = (mousePos.X - MapTranslate.X) / oldZoom;
        var canvasY = (mousePos.Y - MapTranslate.Y) / oldZoom;

        // 줌 후에도 마우스 위치가 동일한 캔버스 좌표를 가리키도록 translate 조정
        // mousePos = canvasPos * newZoom + newTranslate
        // newTranslate = mousePos - canvasPos * newZoom
        MapTranslate.X = mousePos.X - canvasX * newZoom;
        MapTranslate.Y = mousePos.Y - canvasY * newZoom;

        SetZoom(newZoom);
        e.Handled = true;
    }

    #endregion

    #endregion

    #region 맵/마커 관련 메서드

    private void LoadMapImage(string mapKey, bool centerView = true)
    {
        var config = _trackerService?.GetMapConfig(mapKey);
        if (config == null)
        {
            ShowNoMapPanel(true);
            return;
        }

        // ImagePath가 비어있으면 SvgFileName을 기반으로 경로 생성
        var imagePath = config.ImagePath;
        if (string.IsNullOrEmpty(imagePath) && !string.IsNullOrEmpty(config.SvgFileName))
        {
            imagePath = System.IO.Path.Combine("Assets", "DB", "Maps", config.SvgFileName);
        }

        // 상대 경로인 경우 앱 디렉토리 기준으로 변환
        if (!string.IsNullOrEmpty(imagePath) && !System.IO.Path.IsPathRooted(imagePath))
        {
            imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);
        }

        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
        {
            ShowNoMapPanel(true);
            return;
        }

        try
        {
            var extension = System.IO.Path.GetExtension(imagePath).ToLowerInvariant();

            if (extension == ".svg")
            {
                // TarkovDBEditor 방식: SvgViewbox 직접 사용 (PNG 변환 없음)
                MapImage.Visibility = Visibility.Collapsed;
                MapSvg.Visibility = Visibility.Visible;

                // 층 필터링 정보 준비
                IEnumerable<string>? visibleFloors = null;
                IEnumerable<string>? allFloors = null;
                string? backgroundFloorId = null;
                double backgroundOpacity = 0.3;

                if (config.Floors != null && config.Floors.Count > 0 && !string.IsNullOrEmpty(_currentFloorId))
                {
                    allFloors = config.Floors.Select(f => f.LayerId);
                    visibleFloors = new[] { _currentFloorId };

                    // 기본 층(main)을 배경으로 반투명하게 표시
                    var defaultFloor = config.Floors.FirstOrDefault(f => f.IsDefault);
                    var currentFloor = config.Floors.FirstOrDefault(f =>
                        string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));

                    if (defaultFloor != null && !string.Equals(_currentFloorId, defaultFloor.LayerId, StringComparison.OrdinalIgnoreCase))
                    {
                        backgroundFloorId = defaultFloor.LayerId;

                        // 지하층(Order < 0)을 선택한 경우 배경을 더 흐리게 표시
                        if (currentFloor != null && currentFloor.Order < 0)
                        {
                            backgroundOpacity = 0.15;
                        }
                    }
                }

                // 층 필터링이 필요한 경우 SVG 전처리 후 임시 파일로 로드
                if (visibleFloors != null)
                {
                    var preprocessor = new SvgStylePreprocessor();
                    var processedSvg = preprocessor.ProcessSvgFile(imagePath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

                    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"legacy_map_{Guid.NewGuid()}.svg");
                    File.WriteAllText(tempPath, processedSvg);
                    MapSvg.Source = new Uri(tempPath, UriKind.Absolute);
                }
                else
                {
                    MapSvg.Source = new Uri(imagePath, UriKind.Absolute);
                }

                MapSvg.Width = config.ImageWidth;
                MapSvg.Height = config.ImageHeight;
                MapCanvas.Width = config.ImageWidth;
                MapCanvas.Height = config.ImageHeight;
                Canvas.SetLeft(MapSvg, 0);
                Canvas.SetTop(MapSvg, 0);
            }
            else
            {
                // 비트맵 이미지 로드 (PNG, JPG 등)
                MapSvg.Visibility = Visibility.Collapsed;
                MapImage.Visibility = Visibility.Visible;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                MapImage.Source = bitmap;
                MapCanvas.Width = bitmap.PixelWidth;
                MapCanvas.Height = bitmap.PixelHeight;

                // 이미지는 (0,0)에 위치
                Canvas.SetLeft(MapImage, 0);
                Canvas.SetTop(MapImage, 0);
            }

            ShowNoMapPanel(false);

            // 맵을 화면 중앙에 배치 (층 변경 시에는 위치 유지)
            if (centerView)
            {
                CenterMapInView();
            }
        }
        catch
        {
            ShowNoMapPanel(true);
        }
    }

    private void CenterMapInView()
    {
        // 뷰어 영역의 크기 가져오기
        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        // 맵 크기 가져오기
        var mapWidth = MapCanvas.Width;
        var mapHeight = MapCanvas.Height;

        // 뷰어가 아직 렌더링되지 않은 경우 Loaded 이벤트에서 다시 호출
        if (viewerWidth <= 0 || viewerHeight <= 0)
        {
            Dispatcher.BeginInvoke(new Action(CenterMapInView), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        // 줌 레벨을 고려하여 중앙 위치 계산
        var scaledMapWidth = mapWidth * _zoomLevel;
        var scaledMapHeight = mapHeight * _zoomLevel;

        // 맵을 뷰어 중앙에 배치하기 위한 이동량 계산
        var translateX = (viewerWidth - scaledMapWidth) / 2;
        var translateY = (viewerHeight - scaledMapHeight) / 2;

        MapTranslate.X = translateX;
        MapTranslate.Y = translateY;
    }

    private void ShowNoMapPanel(bool show)
    {
        NoMapPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        // 맵이 없을 때는 둘 다 숨김, 있을 때는 LoadMapImage에서 관리
        if (show)
        {
            MapImage.Visibility = Visibility.Collapsed;
            MapSvg.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadCurrentMapSettings()
    {
        // 설정 패널에서 맵 관련 설정이 제거되어 더 이상 필요하지 않음
    }

    private void UpdateMarkerPosition(ScreenPosition position)
    {
        // 현재 선택된 맵과 다른 경우 맵 전환
        if (!string.Equals(_currentMapKey, position.MapKey, StringComparison.OrdinalIgnoreCase))
        {
            // 맵 선택 변경
            for (int i = 0; i < CmbMapSelect.Items.Count; i++)
            {
                if (CmbMapSelect.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, position.MapKey, StringComparison.OrdinalIgnoreCase))
                {
                    CmbMapSelect.SelectedIndex = i;
                    break;
                }
            }
        }

        var showDirection = (_trackerService?.Settings.ShowDirection ?? true) && position.Angle.HasValue;

        if (showDirection)
        {
            PlayerMarker.Visibility = Visibility.Visible;
            PlayerDot.Visibility = Visibility.Collapsed;

            // 마커 위치 설정 (Canvas 중심 기준)
            MarkerTranslation.X = position.X;
            MarkerTranslation.Y = position.Y;

            // 방향 화살표 회전 (화살표만 회전, 중심 원은 고정)
            var angle = position.Angle ?? 0;

            // The Lab 맵은 방향을 왼쪽으로 90도 회전
            if (string.Equals(_currentMapKey, "Labs", StringComparison.OrdinalIgnoreCase))
            {
                angle -= 90;
            }
            // Factory 맵은 방향을 오른쪽으로 90도 회전
            else if (string.Equals(_currentMapKey, "Factory", StringComparison.OrdinalIgnoreCase))
            {
                angle += 90;
            }

            MarkerRotation.Angle = angle;
        }
        else
        {
            PlayerMarker.Visibility = Visibility.Collapsed;
            PlayerDot.Visibility = Visibility.Visible;

            // 원형 마커 위치 (Canvas 중심 기준)
            DotTranslation.X = position.X;
            DotTranslation.Y = position.Y;
        }
    }

    private void UpdateTrailPath()
    {
        if (_trackerService == null) return;
        if (!_trackerService.Settings.ShowTrail) return;

        TrailPath.Points.Clear();
        foreach (var pos in _trackerService.TrailPositions)
        {
            TrailPath.Points.Add(new Point(pos.X, pos.Y));
        }
    }

    private void UpdateCoordinatesDisplay(ScreenPosition position)
    {
        var orig = position.OriginalPosition;
        if (orig != null)
        {
            var angleStr = orig.Angle.HasValue ? $", Angle: {orig.Angle:F1}°" : "";
            TxtCoordinates.Text = $"Map: {orig.MapName}, X: {orig.X:F2}, Y: {orig.Y:F2}{angleStr}";
        }
        else
        {
            TxtCoordinates.Text = $"X: {position.X:F0}, Y: {position.Y:F0}";
        }

        TxtLastUpdateTime.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private void UpdateMarkerSize(int size)
    {
        // 퀘스트 마커 크기 설정 저장
        if (_trackerService != null)
        {
            _trackerService.Settings.MarkerSize = size;
            _trackerService.SaveSettings();
            // 퀘스트 마커 새로고침
            RefreshQuestMarkers();
        }
    }

    private void UpdatePlayerMarkerSize(int size)
    {
        // 기본 크기(16)를 기준으로 스케일 계산
        var baseScale = size / 16.0;

        // 맵별 마커 스케일 적용
        var mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;

        var scale = baseScale * mapScale;

        // PlayerMarker와 PlayerDot에 스케일 적용
        MarkerScale.ScaleX = scale;
        MarkerScale.ScaleY = scale;
        DotScale.ScaleX = scale;
        DotScale.ScaleY = scale;

        // 설정 저장
        if (_trackerService != null)
        {
            _trackerService.Settings.PlayerMarkerSize = size;
            _trackerService.SaveSettings();
        }
    }

    private void UpdateMarkerVisibility()
    {
        if (_trackerService == null) return;
        var current = _trackerService.CurrentPosition;
        if (current == null)
        {
            PlayerMarker.Visibility = Visibility.Collapsed;
            PlayerDot.Visibility = Visibility.Collapsed;
            return;
        }

        var showDirection = _trackerService.Settings.ShowDirection && current.Angle.HasValue;
        PlayerMarker.Visibility = showDirection ? Visibility.Visible : Visibility.Collapsed;
        PlayerDot.Visibility = showDirection ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetZoom(double zoom)
    {
        // 줌 범위 제한
        _zoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);
        MapScale.ScaleX = _zoomLevel;
        MapScale.ScaleY = _zoomLevel;

        // 콤보박스 텍스트 업데이트 (이벤트 트리거 방지)
        CmbZoomLevel.SelectionChanged -= CmbZoomLevel_SelectionChanged;
        CmbZoomLevel.Text = $"{_zoomLevel * 100:F0}%";
        CmbZoomLevel.SelectionChanged += CmbZoomLevel_SelectionChanged;

        // 마커들의 역스케일 업데이트 (고정 크기 유지)
        UpdateMarkerScales();
    }

    /// <summary>
    /// 모든 마커의 역스케일을 현재 줌 레벨에 맞게 업데이트합니다.
    /// </summary>
    private void UpdateMarkerScales()
    {
        var inverseScale = 1.0 / _zoomLevel;

        // 퀘스트 마커 업데이트
        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                // Area 마커인 경우 내부 centerCanvas만 역스케일 적용
                if (canvas.Tag is AreaMarkerTag)
                {
                    // container 자체는 역스케일 적용 안 함 (Polygon이 맵과 함께 줌되어야 함)
                    // 내부 centerCanvas 찾아서 역스케일 적용
                    foreach (var child in canvas.Children)
                    {
                        if (child is Canvas centerCanvas && centerCanvas.Tag is TaskObjectiveWithLocation)
                        {
                            centerCanvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
                        }
                    }
                }
                else
                {
                    // 일반 마커는 전체 역스케일 적용
                    canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
                }
            }
        }

        // 탈출구 마커 업데이트
        foreach (var marker in _extractMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
            }
        }

        // 그룹 마커 업데이트
        foreach (var marker in _groupedMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
            }
        }

        // 이름을 표시하는 스타일일 때 겹침 재계산
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        if (showName)
        {
            // 줌 변경 후 레이아웃 업데이트가 필요할 수 있음
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, DetectAndGroupOverlappingMarkers);
        }

        // 플레이어 마커 업데이트
        UpdatePlayerMarkerScale(inverseScale);
    }

    /// <summary>
    /// 플레이어 마커의 역스케일을 업데이트합니다.
    /// </summary>
    private void UpdatePlayerMarkerScale(double inverseScale)
    {
        if (MarkerScale != null)
        {
            MarkerScale.ScaleX = inverseScale;
            MarkerScale.ScaleY = inverseScale;
        }
        if (DotScale != null)
        {
            DotScale.ScaleX = inverseScale;
            DotScale.ScaleY = inverseScale;
        }
    }

    /// <summary>
    /// SVG 파일을 전처리(CSS 클래스→인라인 스타일 변환) 후 BitmapSource로 변환합니다.
    /// </summary>
    private BitmapSource? ConvertSvgToPngWithPreprocessing(string svgPath, int width, int height)
    {
        return ConvertSvgToPngWithPreprocessing(svgPath, width, height, null, null);
    }

    /// <summary>
    /// SVG 파일을 전처리(CSS 클래스→인라인 스타일 변환 + 층 필터링) 후 BitmapSource로 변환합니다.
    /// </summary>
    /// <param name="svgPath">SVG 파일 경로</param>
    /// <param name="width">출력 너비</param>
    /// <param name="height">출력 높이</param>
    /// <param name="visibleFloors">표시할 층 ID 목록. null이면 모든 층 표시.</param>
    /// <param name="allFloors">맵에 정의된 모든 층 ID 목록.</param>
    /// <param name="backgroundFloorId">배경으로 반투명하게 표시할 층의 ID (예: "main"). null이면 배경 층 없음.</param>
    /// <param name="backgroundOpacity">배경 층의 투명도 (0.0 ~ 1.0). 기본값 0.3</param>
    private BitmapSource? ConvertSvgToPngWithPreprocessing(
        string svgPath,
        int width,
        int height,
        IEnumerable<string>? visibleFloors,
        IEnumerable<string>? allFloors,
        string? backgroundFloorId = null,
        double backgroundOpacity = 0.3)
    {
        try
        {
            // 1. SVG 전처리: CSS 클래스를 인라인 스타일로 변환 + 층 필터링
            var preprocessor = new SvgStylePreprocessor();
            var processedSvg = preprocessor.ProcessSvgFile(svgPath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

            // 2. 전처리된 SVG를 렌더링
            return RenderSvgContent(processedSvg, width, height);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// SVG 콘텐츠 문자열을 BitmapSource로 렌더링합니다.
    /// width, height로 확대 렌더링하여 고해상도 출력을 지원합니다.
    /// </summary>
    private BitmapSource? RenderSvgContent(string svgContent, int width, int height)
    {
        try
        {
            var settings = new WpfDrawingSettings
            {
                IncludeRuntime = true,
                TextAsGeometry = false,
                OptimizePath = true,
                CultureInfo = CultureInfo.InvariantCulture,
                EnsureViewboxSize = false,
                EnsureViewboxPosition = false,
                IgnoreRootViewbox = false
            };

            // 문자열에서 SVG 읽기
            DrawingGroup? drawing;
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgContent)))
            {
                var converter = new FileSvgReader(settings);
                drawing = converter.Read(stream);
            }

            if (drawing == null)
                return null;

            var bounds = drawing.Bounds;

            // 스케일 계산: 지정된 width/height로 확대
            var scaleX = width / bounds.Width;
            var scaleY = height / bounds.Height;

            // DrawingVisual로 렌더링 - 스케일 적용하여 확대 렌더링
            var drawingVisual = new DrawingVisual();
            using (var drawingContext = drawingVisual.RenderOpen())
            {
                // 원점 이동 후 스케일 적용
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new TranslateTransform(-bounds.X, -bounds.Y));
                transformGroup.Children.Add(new ScaleTransform(scaleX, scaleY));

                drawingContext.PushTransform(transformGroup);
                drawingContext.DrawDrawing(drawing);
                drawingContext.Pop();
            }

            // RenderTargetBitmap으로 변환 - 지정된 크기로 렌더링
            var renderTarget = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);

            renderTarget.Render(drawingVisual);
            renderTarget.Freeze();

            return renderTarget;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region 퀘스트 목표 마커

    private async Task LoadQuestObjectivesAsync()
    {
        try
        {
            TxtStatus.Text = "Loading quest objectives...";

            await _objectiveService.EnsureLoadedAsync(msg =>
            {
                Dispatcher.Invoke(() => TxtStatus.Text = msg);
            });

            var count = _objectiveService.AllObjectives.Count;
            TxtStatus.Text = $"Loaded {count} quest objectives";

            if (!string.IsNullOrEmpty(_currentMapKey))
            {
                RefreshQuestMarkers();
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error loading objectives: {ex.Message}";
        }
    }

    private void OnQuestProgressChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(RefreshQuestMarkers);
    }

    private void OnObjectiveProgressChanged(object? sender, ObjectiveProgressChangedEventArgs e)
    {
        Dispatcher.Invoke(RefreshQuestMarkers);
    }

    private void ChkShowQuestMarkers_Changed(object sender, RoutedEventArgs e)
    {
        _showQuestMarkers = ChkShowQuestMarkers?.IsChecked ?? true;
        if (QuestMarkersContainer != null)
        {
            QuestMarkersContainer.Visibility = _showQuestMarkers ? Visibility.Visible : Visibility.Collapsed;
        }

        // DB에 저장
        SettingsService.Instance.MapShowQuests = _showQuestMarkers;

        // 오버레이 맵도 새로고침
        OverlayMiniMapService.Instance.RefreshMap();
    }

    /// <summary>
    /// 위치가 현재 맵에 해당하는지 확인합니다.
    /// </summary>
    private bool IsLocationOnCurrentMap(QuestObjectiveLocation location, LegacyMapConfig config)
    {
        if (string.IsNullOrEmpty(_currentMapKey)) return false;

        // 검색할 맵 이름 목록 구성
        var mapNamesToMatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            _currentMapKey
        };

        if (config.Aliases != null)
        {
            foreach (var alias in config.Aliases)
                mapNamesToMatch.Add(alias);
        }

        if (!string.IsNullOrEmpty(config.DisplayName))
            mapNamesToMatch.Add(config.DisplayName);

        // 위치의 맵 이름이 현재 맵과 일치하는지 확인
        if (!string.IsNullOrEmpty(location.MapName) && mapNamesToMatch.Contains(location.MapName))
            return true;

        if (!string.IsNullOrEmpty(location.MapNormalizedName) && mapNamesToMatch.Contains(location.MapNormalizedName))
            return true;

        return false;
    }

    private void RefreshQuestMarkers()
    {
        if (string.IsNullOrEmpty(_currentMapKey)) return;
        if (!_objectiveService.IsLoaded) return;

        // 기존 마커 제거
        ClearQuestMarkers();

        if (!_showQuestMarkers) return;

        // 맵 설정 가져오기
        var config = _trackerService?.GetMapConfig(_currentMapKey);
        if (config == null) return;

        // 현재 맵의 활성 퀘스트 목표 가져오기 (별칭 포함하여 검색)
        var mapNamesToSearch = new List<string> { _currentMapKey };
        if (config.Aliases != null)
        {
            mapNamesToSearch.AddRange(config.Aliases);
        }
        // 표시 이름도 추가
        if (!string.IsNullOrEmpty(config.DisplayName))
        {
            mapNamesToSearch.Add(config.DisplayName);
        }

        _currentMapObjectives = new List<TaskObjectiveWithLocation>();
        foreach (var mapName in mapNamesToSearch)
        {
            var objectives = _objectiveService.GetActiveObjectivesForMap(mapName, _progressService);
            foreach (var obj in objectives)
            {
                if (!_currentMapObjectives.Any(o => o.ObjectiveId == obj.ObjectiveId))
                {
                    _currentMapObjectives.Add(obj);
                }
            }
        }

        TxtStatus.Text = $"Found {_currentMapObjectives.Count} active objectives for {_currentMapKey}";

        foreach (var objective in _currentMapObjectives)
        {
            // ObjectiveId 기반으로 완료 상태 확인 (동일 설명 목표 개별 추적)
            var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);
            objective.IsCompleted = isCompleted;

            // 완료된 목표 숨기기 설정이 활성화되어 있으면 스킵
            if (_hideCompletedObjectives && isCompleted)
                continue;

            // 현재 맵에 해당하는 위치만 필터링
            var locationsOnCurrentMap = objective.Locations
                .Where(loc => IsLocationOnCurrentMap(loc, config))
                .ToList();

            foreach (var location in locationsOnCurrentMap)
            {
                // 층 정보 확인: 현재 선택된 층과 목표 위치의 층 비교
                var isOnCurrentFloor = IsMarkerOnCurrentFloor(location.FloorId);

                // Area(Polygon) 표시: Outline이 있으면 다각형으로 그리기
                if (location.Outline != null && location.Outline.Count >= 3)
                {
                    var areaMarker = CreateAreaMarker(objective, location, config, isOnCurrentFloor);
                    _questMarkerElements.Add(areaMarker);
                    QuestMarkersContainer.Children.Add(areaMarker);
                }
                else
                {
                    // 단일 포인트 마커
                    // TarkovDBEditor 방식: config.GameToScreen 직접 사용
                    // QuestObjectiveLocation: X = game X, Y = game Z (수평면), Z = game Y (높이)
                    var (screenX, screenY) = config.GameToScreen(location.X, location.Y);
                    var screenPos = new ScreenPosition
                    {
                        MapKey = _currentMapKey!,
                        X = screenX,
                        Y = screenY
                    };

                    // OR Pointer 여부 확인 (location.Id에 _opt_ 포함)
                    var isOrPointer = location.Id.Contains("_opt_");

                    var marker = isOrPointer
                        ? CreateOrPointerMarker(objective, location, screenPos, isOnCurrentFloor)
                        : CreateQuestMarker(objective, location, screenPos, isOnCurrentFloor);
                    _questMarkerElements.Add(marker);
                    QuestMarkersContainer.Children.Add(marker);
                }
            }
        }

        // 이름을 표시하는 스타일일 때만 겹침 감지 수행
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        if (showName)
        {
            // 레이아웃 완료 후 그룹화 수행 (Measure가 정확한 크기를 반환하도록)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, DetectAndGroupOverlappingMarkers);
        }
    }

    private FrameworkElement CreateQuestMarker(TaskObjectiveWithLocation objective, QuestObjectiveLocation location, ScreenPosition screenPos, bool isOnCurrentFloor = true)
    {
        // 설정에서 커스텀 색상 가져오기
        var colorHex = _trackerService?.Settings.GetMarkerColor(objective.Type) ?? objective.MarkerColor;
        var markerColor = (Color)ColorConverter.ConvertFromString(colorHex);

        // 다른 층의 마커는 색상을 흐리게 처리
        if (!isOnCurrentFloor)
        {
            markerColor = Color.FromArgb(100, markerColor.R, markerColor.G, markerColor.B);
        }

        var markerBrush = new SolidColorBrush(markerColor);
        var glowBrush = new SolidColorBrush(Color.FromArgb((byte)(isOnCurrentFloor ? 64 : 30), markerColor.R, markerColor.G, markerColor.B));

        // 초록색 원 스타일용 색상
        var greenColor = (Color)ColorConverter.ConvertFromString("#4CAF50");
        if (!isOnCurrentFloor)
        {
            greenColor = Color.FromArgb(100, greenColor.R, greenColor.G, greenColor.B);
        }
        var greenBrush = new SolidColorBrush(greenColor);
        var greenGlowBrush = new SolidColorBrush(Color.FromArgb((byte)(isOnCurrentFloor ? 64 : 30), greenColor.R, greenColor.G, greenColor.B));

        // 설정에서 마커 크기 가져오기
        var baseMarkerSize = _trackerService?.Settings.MarkerSize ?? 16;

        // 맵별 마커 스케일 적용
        var mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;

        var markerSize = baseMarkerSize * mapScale;
        var glowSize = markerSize * 1.75;
        var centerSize = markerSize * 0.875;

        var canvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = objective
        };

        // 스타일에 따라 다르게 렌더링
        var useGreenCircle = _questMarkerStyle == QuestMarkerStyle.GreenCircle ||
                             _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;

        if (useGreenCircle)
        {
            // 초록색 원 (테두리만)
            var circleOuter = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Stroke = greenBrush,
                StrokeThickness = 3,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(circleOuter, -markerSize / 2);
            Canvas.SetTop(circleOuter, -markerSize / 2);
            canvas.Children.Add(circleOuter);
        }
        else
        {
            // 기본 스타일: 외곽 글로우 + 중심 원
            var glow = new Ellipse
            {
                Width = glowSize,
                Height = glowSize,
                Fill = glowBrush
            };
            Canvas.SetLeft(glow, -glowSize / 2);
            Canvas.SetTop(glow, -glowSize / 2);
            canvas.Children.Add(glow);

            var center = new Ellipse
            {
                Width = centerSize,
                Height = centerSize,
                Fill = markerBrush,
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            Canvas.SetLeft(center, -centerSize / 2);
            Canvas.SetTop(center, -centerSize / 2);
            canvas.Children.Add(center);
        }

        // 완료 상태 표시 (큰 체크마크 + 취소선)
        if (objective.IsCompleted)
        {
            // 완료 배경 오버레이 (반투명 회색)
            var completedOverlay = new Ellipse
            {
                Width = useGreenCircle ? markerSize : glowSize,
                Height = useGreenCircle ? markerSize : glowSize,
                Fill = new SolidColorBrush(Color.FromArgb(180, 50, 50, 50))
            };
            var overlaySize = useGreenCircle ? markerSize : glowSize;
            Canvas.SetLeft(completedOverlay, -overlaySize / 2);
            Canvas.SetTop(completedOverlay, -overlaySize / 2);
            canvas.Children.Add(completedOverlay);

            // 큰 체크마크
            var checkMarkSize = markerSize * 0.8;
            var checkMark = new TextBlock
            {
                Text = "✓",
                FontSize = checkMarkSize,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)) // 밝은 초록
            };
            // 체크마크 중앙 정렬
            checkMark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(checkMark, -checkMark.DesiredSize.Width / 2);
            Canvas.SetTop(checkMark, -checkMark.DesiredSize.Height / 2);
            canvas.Children.Add(checkMark);

            // 완료된 마커는 약간 반투명
            canvas.Opacity = 0.7;
        }

        // 퀘스트명 표시
        Border? floorBadgeBorder = null;  // 층 배지를 별도로 관리
        if (showName)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            // 층 정보 가져오기
            var floorInfo = GetFloorIndicator(location.FloorId);

            // 퀘스트명과 층 배지를 함께 담는 StackPanel (수직 중앙 정렬을 위해)
            var contentPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 퀘스트명 텍스트
            var nameText = new TextBlock
            {
                Text = questName,
                FontSize = _questNameTextSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Child = nameText,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 텍스트 클릭 이벤트 추가
            nameBorder.Tag = objective;
            nameBorder.MouseLeftButtonDown += QuestMarkerText_Click;
            nameBorder.Cursor = Cursors.Hand;

            contentPanel.Children.Add(nameBorder);

            // 다른 층일 때 층 배지 추가
            if (floorInfo.HasValue)
            {
                var (arrow, floorText, indicatorColor) = floorInfo.Value;

                // 층 배지 (화살표 + 층 표시) - 불투명하게 유지하기 위해 별도 Border
                var floorBadgeText = new TextBlock
                {
                    Text = $"{arrow}{floorText}",
                    FontSize = _questNameTextSize * 0.85,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };

                floorBadgeBorder = new Border
                {
                    Background = new SolidColorBrush(indicatorColor),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 1, 3, 1),
                    Margin = new Thickness(4, 0, 0, 0),  // 왼쪽 여백으로 간격 확보
                    Child = floorBadgeText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = "FloorBadge"  // 식별용 태그
                };

                contentPanel.Children.Add(floorBadgeBorder);
            }

            // 전체 컨테이너 크기를 측정하여 중앙 정렬
            contentPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var totalWidth = contentPanel.DesiredSize.Width;

            // 컨테이너를 마커 중앙 아래에 위치
            Canvas.SetLeft(contentPanel, -totalWidth / 2);
            Canvas.SetTop(contentPanel, markerSize / 2 + 4);

            canvas.Children.Add(contentPanel);
        }

        // 위치 설정
        Canvas.SetLeft(canvas, screenPos.X);
        Canvas.SetTop(canvas, screenPos.Y);

        // 줌에 상관없이 고정 크기 유지를 위한 역스케일 적용
        var inverseScale = 1.0 / _zoomLevel;
        canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        canvas.RenderTransformOrigin = new Point(0, 0);

        // 다른 층의 마커는 반투명 처리 (층 배지 제외)
        if (!isOnCurrentFloor)
        {
            // canvas 전체 대신 개별 요소에 반투명 적용
            foreach (var child in canvas.Children)
            {
                if (child is FrameworkElement element)
                {
                    // StackPanel인 경우 내부 요소들을 개별 처리
                    if (element is StackPanel stackPanel)
                    {
                        foreach (var stackChild in stackPanel.Children)
                        {
                            if (stackChild is Border border)
                            {
                                // 층 배지는 불투명하게 유지
                                if (border.Tag as string == "FloorBadge")
                                {
                                    border.Opacity = 1.0;
                                }
                                else
                                {
                                    border.Opacity = 0.5;
                                }
                            }
                        }
                    }
                    // 층 배지는 불투명하게 유지
                    else if (element is Border border && border.Tag as string == "FloorBadge")
                    {
                        element.Opacity = 1.0;
                    }
                    else
                    {
                        element.Opacity = 0.5;
                    }
                }
            }
        }

        // 클릭 이벤트
        canvas.MouseLeftButtonDown += QuestMarker_Click;
        canvas.Cursor = Cursors.Hand;

        // 툴팁
        var tooltipDesc = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;
        var tooltipName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;
        var floorIndicator = !isOnCurrentFloor ? " ⬆⬇" : "";
        canvas.ToolTip = $"{tooltipName}\n{tooltipDesc}{floorIndicator}";

        return canvas;
    }

    /// <summary>
    /// Area(다각형) 마커를 생성합니다. Outline이 있는 퀘스트 목표에 사용됩니다.
    /// Polygon은 맵과 함께 줌되고, 중앙 마커/텍스트만 역스케일 적용됩니다.
    /// </summary>
    private FrameworkElement CreateAreaMarker(TaskObjectiveWithLocation objective, QuestObjectiveLocation location, MapConfig config, bool isOnCurrentFloor = true)
    {
        // 설정에서 커스텀 색상 가져오기
        var colorHex = _trackerService?.Settings.GetMarkerColor(objective.Type) ?? objective.MarkerColor;
        var markerColor = (Color)ColorConverter.ConvertFromString(colorHex);

        // 다른 층의 마커는 색상을 흐리게 처리
        byte alpha = isOnCurrentFloor ? (byte)60 : (byte)20;
        byte strokeAlpha = isOnCurrentFloor ? (byte)255 : (byte)100;

        // Area 마커임을 표시하는 태그
        var areaTag = new AreaMarkerTag { Objective = objective, IsArea = true };

        // 최상위 컨테이너 (역스케일 적용 안 함)
        var container = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = areaTag
        };

        // Polygon 그리기 (맵과 함께 줌됨)
        var polygon = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(alpha, markerColor.R, markerColor.G, markerColor.B)),
            Stroke = new SolidColorBrush(Color.FromArgb(strokeAlpha, markerColor.R, markerColor.G, markerColor.B)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Tag = "AreaPolygon" // UpdateMarkerScales에서 제외하기 위한 태그
        };

        // Outline 좌표를 스크린 좌표로 변환
        double sumX = 0, sumY = 0;
        foreach (var point in location.Outline!)
        {
            var (sx, sy) = config.GameToScreen(point.X, point.Y);
            polygon.Points.Add(new Point(sx, sy));
            sumX += sx;
            sumY += sy;
        }

        container.Children.Add(polygon);

        // 중심점 계산
        var centroidX = sumX / location.Outline.Count;
        var centroidY = sumY / location.Outline.Count;

        // 설정에서 마커 크기 가져오기
        var baseMarkerSize = _trackerService?.Settings.MarkerSize ?? 16;
        var mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;
        var markerSize = baseMarkerSize * mapScale;

        // 중앙 마커용 캔버스 (역스케일 적용됨)
        var centerCanvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = objective
        };

        // 다이아몬드 마커
        var diamond = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb((byte)(isOnCurrentFloor ? 200 : 100), markerColor.R, markerColor.G, markerColor.B)),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Points = new PointCollection
            {
                new Point(0, -markerSize / 2),
                new Point(markerSize / 2, 0),
                new Point(0, markerSize / 2),
                new Point(-markerSize / 2, 0)
            }
        };
        centerCanvas.Children.Add(diamond);

        // 퀘스트명 표시
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        if (showName)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            var nameText = new TextBlock
            {
                Text = questName,
                FontSize = _questNameTextSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center
            };

            var nameBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Child = nameText
            };

            nameBorder.Tag = objective;
            nameBorder.MouseLeftButtonDown += QuestMarkerText_Click;
            nameBorder.Cursor = Cursors.Hand;

            nameBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(nameBorder, -nameBorder.DesiredSize.Width / 2);
            Canvas.SetTop(nameBorder, markerSize / 2 + 4);
            centerCanvas.Children.Add(nameBorder);
        }

        // 완료 상태 표시
        if (objective.IsCompleted)
        {
            var checkMarkSize = markerSize * 0.8;
            var checkMark = new TextBlock
            {
                Text = "✓",
                FontSize = checkMarkSize,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80))
            };
            checkMark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(checkMark, -checkMark.DesiredSize.Width / 2);
            Canvas.SetTop(checkMark, -checkMark.DesiredSize.Height / 2);
            centerCanvas.Children.Add(checkMark);
            centerCanvas.Opacity = 0.7;
        }

        // 중앙 캔버스 위치 설정 및 역스케일 적용
        Canvas.SetLeft(centerCanvas, centroidX);
        Canvas.SetTop(centerCanvas, centroidY);
        var inverseScale = 1.0 / _zoomLevel;
        centerCanvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        centerCanvas.RenderTransformOrigin = new Point(0, 0);

        container.Children.Add(centerCanvas);

        // 다른 층의 마커는 반투명 처리
        if (!isOnCurrentFloor)
        {
            container.Opacity = 0.5;
        }

        // 클릭 이벤트
        container.MouseLeftButtonDown += QuestMarker_Click;
        container.Cursor = Cursors.Hand;

        // 툴팁
        var tooltipDesc = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;
        var tooltipName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;
        var floorIndicator = !isOnCurrentFloor ? " ⬆⬇" : "";
        container.ToolTip = $"[Area] {tooltipName}\n{tooltipDesc}{floorIndicator}";

        return container;
    }

    /// <summary>
    /// Area 마커용 태그 클래스
    /// </summary>
    private class AreaMarkerTag
    {
        public TaskObjectiveWithLocation? Objective { get; set; }
        public bool IsArea { get; set; }
    }

    /// <summary>
    /// 마커의 Tag에서 TaskObjectiveWithLocation을 추출합니다.
    /// </summary>
    private static TaskObjectiveWithLocation? GetObjectiveFromTag(object? tag)
    {
        return tag switch
        {
            TaskObjectiveWithLocation obj => obj,
            AreaMarkerTag areaTag => areaTag.Objective,
            _ => null
        };
    }

    /// <summary>
    /// 마커의 실제 화면 위치를 반환합니다.
    /// Area 마커의 경우 내부 centerCanvas의 위치를 반환합니다.
    /// </summary>
    private static (double X, double Y) GetMarkerPosition(Canvas markerCanvas)
    {
        // Area 마커인 경우 centerCanvas의 위치 반환
        if (markerCanvas.Tag is AreaMarkerTag)
        {
            foreach (var child in markerCanvas.Children)
            {
                if (child is Canvas centerCanvas && centerCanvas.Tag is TaskObjectiveWithLocation)
                {
                    return (Canvas.GetLeft(centerCanvas), Canvas.GetTop(centerCanvas));
                }
            }
        }

        // 일반 마커의 경우 canvas 자체의 위치 반환
        return (Canvas.GetLeft(markerCanvas), Canvas.GetTop(markerCanvas));
    }

    /// <summary>
    /// 마커의 텍스트를 숨깁니다.
    /// Area 마커의 경우 내부 centerCanvas의 텍스트도 처리합니다.
    /// </summary>
    private static void HideMarkerText(Canvas markerCanvas)
    {
        SetMarkerTextVisibility(markerCanvas, Visibility.Collapsed);
    }

    /// <summary>
    /// 마커의 텍스트를 표시합니다.
    /// Area 마커의 경우 내부 centerCanvas의 텍스트도 처리합니다.
    /// </summary>
    private static void ShowMarkerText(Canvas markerCanvas)
    {
        SetMarkerTextVisibility(markerCanvas, Visibility.Visible);
    }

    /// <summary>
    /// 마커의 텍스트 가시성을 설정합니다.
    /// Area 마커의 경우 내부 centerCanvas의 텍스트도 처리합니다.
    /// </summary>
    private static void SetMarkerTextVisibility(Canvas markerCanvas, Visibility visibility)
    {
        // Area 마커인 경우 centerCanvas 내부 검사
        Canvas targetCanvas = markerCanvas;
        if (markerCanvas.Tag is AreaMarkerTag)
        {
            foreach (var child in markerCanvas.Children)
            {
                if (child is Canvas centerCanvas && centerCanvas.Tag is TaskObjectiveWithLocation)
                {
                    targetCanvas = centerCanvas;
                    break;
                }
            }
        }

        foreach (var child in targetCanvas.Children)
        {
            // 새 방식: StackPanel (퀘스트명 + 층 배지)
            if (child is StackPanel stackPanel)
            {
                stackPanel.Visibility = visibility;
            }
            // 이전 방식 호환: 직접 Border
            else if (child is Border border && border.Tag is TaskObjectiveWithLocation)
            {
                border.Visibility = visibility;
            }
        }
    }

    /// <summary>
    /// OR Pointer 마커를 생성합니다. OptionalPoints에서 생성된 위치에 사용됩니다.
    /// </summary>
    private FrameworkElement CreateOrPointerMarker(TaskObjectiveWithLocation objective, QuestObjectiveLocation location, ScreenPosition screenPos, bool isOnCurrentFloor = true)
    {
        // OR 마커는 주황색 (#FF5722)
        var orColor = Color.FromRgb(255, 87, 34);

        // 다른 층의 마커는 색상을 흐리게 처리
        if (!isOnCurrentFloor)
        {
            orColor = Color.FromArgb(100, orColor.R, orColor.G, orColor.B);
        }

        var markerBrush = new SolidColorBrush(orColor);

        // 설정에서 마커 크기 가져오기
        var baseMarkerSize = _trackerService?.Settings.MarkerSize ?? 16;
        var mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;
        var markerSize = baseMarkerSize * mapScale;

        var canvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = objective
        };

        // 주황색 원형 마커
        var orCircle = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = markerBrush,
            Stroke = Brushes.White,
            StrokeThickness = 3
        };
        Canvas.SetLeft(orCircle, -markerSize / 2);
        Canvas.SetTop(orCircle, -markerSize / 2);
        canvas.Children.Add(orCircle);

        // OR 인덱스 추출 (예: "_opt_1" -> 1, "_opt_0" -> 1)
        var orIndex = 1;
        var idxMatch = System.Text.RegularExpressions.Regex.Match(location.Id, @"_opt_(\d+)");
        if (idxMatch.Success && int.TryParse(idxMatch.Groups[1].Value, out var parsedIdx))
        {
            // 0-indexed인 경우 1-indexed로 변환
            orIndex = parsedIdx + 1;
        }

        // OR 레이블
        var orLabel = new TextBlock
        {
            Text = $"OR{orIndex}",
            FontSize = _questNameTextSize * 0.9,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        };

        var orLabelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 255, 87, 34)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = orLabel
        };

        orLabelBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(orLabelBorder, markerSize / 2 + 4);
        Canvas.SetTop(orLabelBorder, -orLabelBorder.DesiredSize.Height / 2);
        canvas.Children.Add(orLabelBorder);

        // 퀘스트명 표시
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        if (showName)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            var nameText = new TextBlock
            {
                Text = questName,
                FontSize = _questNameTextSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center
            };

            var nameBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Child = nameText
            };

            nameBorder.Tag = objective;
            nameBorder.MouseLeftButtonDown += QuestMarkerText_Click;
            nameBorder.Cursor = Cursors.Hand;

            nameBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(nameBorder, -nameBorder.DesiredSize.Width / 2);
            Canvas.SetTop(nameBorder, markerSize / 2 + 4);
            canvas.Children.Add(nameBorder);
        }

        // 완료 상태 표시
        if (objective.IsCompleted)
        {
            var checkMarkSize = markerSize * 0.8;
            var checkMark = new TextBlock
            {
                Text = "✓",
                FontSize = checkMarkSize,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80))
            };
            checkMark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(checkMark, -checkMark.DesiredSize.Width / 2);
            Canvas.SetTop(checkMark, -checkMark.DesiredSize.Height / 2);
            canvas.Children.Add(checkMark);
            canvas.Opacity = 0.7;
        }

        // 위치 설정
        Canvas.SetLeft(canvas, screenPos.X);
        Canvas.SetTop(canvas, screenPos.Y);

        // 줌에 상관없이 고정 크기 유지를 위한 역스케일 적용
        var inverseScale = 1.0 / _zoomLevel;
        canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        canvas.RenderTransformOrigin = new Point(0, 0);

        // 다른 층의 마커는 반투명 처리
        if (!isOnCurrentFloor)
        {
            canvas.Opacity = 0.5;
        }

        // 클릭 이벤트
        canvas.MouseLeftButtonDown += QuestMarker_Click;
        canvas.Cursor = Cursors.Hand;

        // 툴팁
        var tooltipDesc = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;
        var tooltipName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;
        var floorIndicator = !isOnCurrentFloor ? " ⬆⬇" : "";
        canvas.ToolTip = $"[OR{orIndex}] {tooltipName}\n{tooltipDesc}{floorIndicator}";

        return canvas;
    }

    /// <summary>
    /// 마커의 텍스트가 겹치는지 감지하고 그룹화합니다.
    /// </summary>
    private void DetectAndGroupOverlappingMarkers()
    {
        // 기존 그룹 마커 제거
        ClearGroupedMarkers();

        // 모든 텍스트 컨테이너(StackPanel)를 먼저 보이게 복원
        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                ShowMarkerText(canvas);
            }
        }

        if (_questMarkerElements.Count < 2) return;

        // 마커와 텍스트 경계 정보 수집
        var markerInfos = new List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)>();

        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                var objective = GetObjectiveFromTag(canvas.Tag);
                if (objective == null) continue;

                // Area 마커인 경우 내부 centerCanvas에서 텍스트 찾기
                Canvas? targetCanvas = canvas;
                if (canvas.Tag is AreaMarkerTag)
                {
                    foreach (var child in canvas.Children)
                    {
                        if (child is Canvas innerCanvas && innerCanvas.Tag is TaskObjectiveWithLocation)
                        {
                            targetCanvas = innerCanvas;
                            break;
                        }
                    }
                }

                // 텍스트 컨테이너 찾기 (StackPanel 또는 Border)
                FrameworkElement? textContainer = null;
                foreach (var child in targetCanvas.Children)
                {
                    // 새 방식: StackPanel (퀘스트명 + 층 배지)
                    if (child is StackPanel stackPanel)
                    {
                        textContainer = stackPanel;
                        break;
                    }
                    // 이전 방식 호환: 직접 Border
                    if (child is Border border && border.Tag is TaskObjectiveWithLocation)
                    {
                        textContainer = border;
                        break;
                    }
                }

                if (textContainer != null)
                {
                    // 마커의 화면 위치 (Area 마커는 centerCanvas의 위치 사용)
                    var markerX = Canvas.GetLeft(targetCanvas);
                    var markerY = Canvas.GetTop(targetCanvas);

                    // 텍스트 컨테이너의 상대 위치
                    var textLeft = Canvas.GetLeft(textContainer);
                    var textTop = Canvas.GetTop(textContainer);

                    // 줌 레벨에 따른 역스케일 고려
                    var textInverseScale = 1.0 / _zoomLevel;

                    // 텍스트 컨테이너의 실제 크기
                    textContainer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var textWidth = textContainer.DesiredSize.Width;
                    var textHeight = textContainer.DesiredSize.Height;

                    // 실제 화면상의 텍스트 경계 계산 (줌 적용)
                    var bounds = new Rect(
                        markerX + textLeft * textInverseScale,
                        markerY + textTop * textInverseScale,
                        textWidth * textInverseScale,
                        textHeight * textInverseScale
                    );

                    markerInfos.Add((marker, bounds, objective));
                }
            }
        }

        // 겹치는 마커 그룹 찾기 (Union-Find 방식)
        var groups = FindOverlappingGroups(markerInfos);

        // 마커 크기 계산 (그룹 리스트 위치 예측용)
        var baseMarkerSize = _trackerService?.Settings.MarkerSize ?? 16;
        var mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;
        var markerSize = baseMarkerSize * mapScale;
        var inverseScale = 1.0 / _zoomLevel;

        // 2개 이상 겹치는 그룹만 처리
        foreach (var group in groups.Where(g => g.Count >= 2).ToList())
        {
            // 그룹 리스트가 표시될 영역과 겹치는 텍스트를 반복적으로 추가
            var expandedGroup = ExpandGroupToIncludeOverlappingTexts(group, markerInfos, markerSize, inverseScale);

            // 마커들의 위치 정보 수집
            var markerPositions = new List<(double X, double Y)>();
            foreach (var info in expandedGroup)
            {
                if (info.Marker is Canvas c)
                {
                    // Area 마커의 경우 centerCanvas의 위치 사용
                    var (posX, posY) = GetMarkerPosition(c);
                    markerPositions.Add((posX, posY));
                }
            }

            // 마커들의 중심 X와 가장 아래 Y 계산
            var centerX = markerPositions.Average(p => p.X);
            var bottomY = markerPositions.Max(p => p.Y);

            // 그룹 내 각 마커의 텍스트만 숨기기 (마커 원은 그대로 유지)
            foreach (var info in expandedGroup)
            {
                if (info.Marker is Canvas c)
                {
                    HideMarkerText(c);
                }

                // markerInfos에서 제거 (다른 그룹에서 중복 처리 방지)
                markerInfos.RemoveAll(m => m.Objective.ObjectiveId == info.Objective.ObjectiveId);
            }

            // 텍스트 그룹 인디케이터 생성 (마커 아래에 위치)
            var groupIndicator = CreateTextGroupIndicator(expandedGroup, centerX, bottomY);
            _groupedMarkerElements.Add(groupIndicator);
            QuestMarkersContainer.Children.Add(groupIndicator);
        }
    }

    /// <summary>
    /// 그룹 리스트가 표시될 영역과 겹치는 텍스트들을 그룹에 추가합니다.
    /// </summary>
    private List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> ExpandGroupToIncludeOverlappingTexts(
        List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> initialGroup,
        List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> allMarkers,
        double markerSize, double inverseScale)
    {
        var expandedGroup = new List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)>(initialGroup);
        var groupObjectiveIds = new HashSet<string>(initialGroup.Select(g => g.Objective.ObjectiveId));

        bool changed;
        do
        {
            changed = false;

            // 현재 그룹의 마커 위치로 그룹 리스트 영역 계산
            var markerPositions = expandedGroup
                .Where(info => info.Marker is Canvas)
                .Select(info => (Canvas.GetLeft((Canvas)info.Marker), Canvas.GetTop((Canvas)info.Marker)))
                .ToList();

            if (markerPositions.Count == 0) break;

            var centerX = markerPositions.Average(p => p.Item1);
            var bottomY = markerPositions.Max(p => p.Item2);

            // 그룹 리스트의 예상 크기 계산 (항목당 약 20픽셀 높이, 최대 너비 200픽셀)
            var estimatedListHeight = expandedGroup.Count * 20 * inverseScale;
            var estimatedListWidth = 200 * inverseScale;

            // 그룹 리스트가 표시될 영역 (마커 아래)
            var groupListBounds = new Rect(
                centerX - estimatedListWidth / 2,
                bottomY + (markerSize / 2 + 4) * inverseScale,
                estimatedListWidth,
                estimatedListHeight
            );

            // 그룹에 포함되지 않은 마커들 중 그룹 리스트 영역과 겹치는 것 찾기
            foreach (var marker in allMarkers.ToList())
            {
                if (groupObjectiveIds.Contains(marker.Objective.ObjectiveId))
                    continue;

                if (groupListBounds.IntersectsWith(marker.TextBounds))
                {
                    expandedGroup.Add(marker);
                    groupObjectiveIds.Add(marker.Objective.ObjectiveId);
                    changed = true;
                }
            }
        } while (changed);

        return expandedGroup;
    }

    /// <summary>
    /// 겹치는 마커 그룹을 찾습니다 (Union-Find 알고리즘).
    /// </summary>
    private List<List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)>> FindOverlappingGroups(
        List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> markerInfos)
    {
        var n = markerInfos.Count;
        var parent = Enumerable.Range(0, n).ToArray();

        int Find(int x)
        {
            if (parent[x] != x) parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(int x, int y)
        {
            var px = Find(x);
            var py = Find(y);
            if (px != py) parent[px] = py;
        }

        // 겹치는 마커끼리 연결
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (markerInfos[i].TextBounds.IntersectsWith(markerInfos[j].TextBounds))
                {
                    Union(i, j);
                }
            }
        }

        // 그룹화
        var groups = new Dictionary<int, List<(FrameworkElement, Rect, TaskObjectiveWithLocation)>>();
        for (int i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.ContainsKey(root))
                groups[root] = new List<(FrameworkElement, Rect, TaskObjectiveWithLocation)>();
            groups[root].Add(markerInfos[i]);
        }

        return groups.Values.ToList();
    }

    /// <summary>
    /// 겹치는 텍스트 그룹의 인디케이터를 생성합니다.
    /// 마커 원은 그대로 두고 텍스트만 그룹화하여 리스트 형태로 표시합니다.
    /// </summary>
    private FrameworkElement CreateTextGroupIndicator(
        List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> group,
        double centerX, double bottomY)
    {
        var canvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = group.Select(g => g.Objective).ToList()
        };

        // 리스트 형태로 퀘스트 이름들을 표시하는 StackPanel
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        foreach (var item in group)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(item.Objective.TaskNameKo)
                ? item.Objective.TaskNameKo
                : item.Objective.TaskName;

            // StackPanel으로 이름과 층 배지를 나란히 배치
            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1)
            };

            var itemText = new TextBlock
            {
                Text = $"• {questName}",
                FontSize = _questNameTextSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 완료 상태 표시
            if (item.Objective.IsCompleted)
            {
                itemText.TextDecorations = TextDecorations.Strikethrough;
                itemText.Foreground = new SolidColorBrush(Colors.LightGray);
            }

            itemPanel.Children.Add(itemText);

            // 층 정보 추가 (위치가 있는 경우)
            if (item.Objective.Locations.Count > 0)
            {
                var location = item.Objective.Locations[0];
                var floorInfo = GetFloorIndicator(location.FloorId);
                if (floorInfo.HasValue)
                {
                    var (arrow, floorText, indicatorColor) = floorInfo.Value;
                    var floorBadge = new Border
                    {
                        Background = new SolidColorBrush(indicatorColor),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(3, 1, 3, 1),
                        Margin = new Thickness(4, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var floorBadgeText = new TextBlock
                    {
                        Text = $"{arrow}{floorText}",
                        FontSize = _questNameTextSize * 0.85,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White
                    };
                    floorBadge.Child = floorBadgeText;
                    itemPanel.Children.Add(floorBadge);
                }
            }

            // 개별 항목에 Tag 설정 (클릭 시 사용)
            var itemBorder = new Border
            {
                Child = itemPanel,
                Tag = item.Objective,
                Cursor = Cursors.Hand,
                Padding = new Thickness(2, 0, 2, 0)
            };

            // 호버 효과
            itemBorder.MouseEnter += (s, e) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            };
            itemBorder.MouseLeave += (s, e) =>
            {
                itemBorder.Background = Brushes.Transparent;
            };

            // 개별 클릭 이벤트
            itemBorder.MouseLeftButtonDown += GroupListItem_Click;

            stackPanel.Children.Add(itemBorder);
        }

        var groupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 4),
            Child = stackPanel,
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 70, 130, 180)),
            BorderThickness = new Thickness(1)
        };

        // 크기 측정
        groupBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = groupBorder.DesiredSize.Width;

        // 마커 크기 계산 (마커 아래에 배치하기 위함)
        var baseMarkerSize = _trackerService?.Settings.MarkerSize ?? 16;
        var mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;
        var markerSize = baseMarkerSize * mapScale;

        // 마커 아래에 위치 (X는 중앙 정렬, Y는 마커 아래)
        Canvas.SetLeft(groupBorder, -textWidth / 2);
        Canvas.SetTop(groupBorder, markerSize / 2 + 4);  // 마커 반경 + 여백
        canvas.Children.Add(groupBorder);

        // 위치 설정 (마커들의 중심 X, 가장 아래 마커의 Y)
        Canvas.SetLeft(canvas, centerX);
        Canvas.SetTop(canvas, bottomY);

        // 줌에 상관없이 고정 크기 유지
        var inverseScale = 1.0 / _zoomLevel;
        canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        canvas.RenderTransformOrigin = new Point(0, 0);

        return canvas;
    }

    /// <summary>
    /// 그룹 리스트의 개별 항목 클릭 시 해당 퀘스트를 선택합니다.
    /// </summary>
    private void GroupListItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is TaskObjectiveWithLocation objective)
        {
            ShowQuestDrawer(objective);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 그룹 인디케이터 클릭 시 퀘스트 목록 팝업을 표시합니다.
    /// </summary>
    private void GroupIndicator_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Canvas canvas && canvas.Tag is List<TaskObjectiveWithLocation> objectives)
        {
            ShowGroupPopup(canvas, objectives);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 겹치는 퀘스트 목록을 팝업으로 표시합니다.
    /// </summary>
    private void ShowGroupPopup(Canvas indicator, List<TaskObjectiveWithLocation> objectives)
    {
        // 기존 팝업 닫기
        CloseGroupPopup();

        var stackPanel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
            MinWidth = 200
        };

        foreach (var objective in objectives)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            var itemBorder = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Tag = objective,
                Cursor = Cursors.Hand
            };

            var itemText = new TextBlock
            {
                Text = questName,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };

            // 완료 상태 표시
            if (objective.IsCompleted)
            {
                itemText.TextDecorations = TextDecorations.Strikethrough;
                itemText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            itemBorder.Child = itemText;

            // 호버 효과
            itemBorder.MouseEnter += (s, ev) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            };
            itemBorder.MouseLeave += (s, ev) =>
            {
                itemBorder.Background = Brushes.Transparent;
            };

            // 클릭 시 퀘스트 선택 및 층 변경
            itemBorder.MouseLeftButtonDown += (s, ev) =>
            {
                if (itemBorder.Tag is TaskObjectiveWithLocation obj)
                {
                    CloseGroupPopup();
                    // 해당 Objective의 층으로 변경
                    SelectFloorForObjective(obj);
                    ShowQuestDrawer(obj);
                    ev.Handled = true;
                }
            };

            stackPanel.Children.Add(itemBorder);
        }

        var popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 70, 130, 180)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = stackPanel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 3,
                Opacity = 0.5
            }
        };

        _markerGroupPopup = new Popup
        {
            Child = popupBorder,
            PlacementTarget = indicator,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            IsOpen = true
        };

        // 팝업 외부 클릭 시 닫기
        _markerGroupPopup.Closed += (s, ev) => _markerGroupPopup = null;
    }

    /// <summary>
    /// 그룹 팝업을 닫습니다.
    /// </summary>
    private void CloseGroupPopup()
    {
        if (_markerGroupPopup != null)
        {
            _markerGroupPopup.IsOpen = false;
            _markerGroupPopup = null;
        }
    }

    /// <summary>
    /// 그룹 마커들을 제거합니다.
    /// </summary>
    private void ClearGroupedMarkers()
    {
        foreach (var marker in _groupedMarkerElements)
        {
            if (marker is Canvas c)
            {
                // 리스트 내 개별 항목의 이벤트 핸들러 해제
                foreach (var child in c.Children)
                {
                    if (child is Border groupBorder && groupBorder.Child is StackPanel stackPanel)
                    {
                        foreach (var item in stackPanel.Children)
                        {
                            if (item is Border itemBorder)
                            {
                                itemBorder.MouseLeftButtonDown -= GroupListItem_Click;
                            }
                        }
                    }
                }
            }
            QuestMarkersContainer.Children.Remove(marker);
        }
        _groupedMarkerElements.Clear();
        CloseGroupPopup();
    }

    private void ClearQuestMarkers()
    {
        // 그룹 마커 먼저 제거
        ClearGroupedMarkers();

        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas c)
            {
                c.MouseLeftButtonDown -= QuestMarker_Click;
                // 텍스트 Border의 클릭 이벤트도 해제
                foreach (var child in c.Children)
                {
                    if (child is Border border)
                    {
                        border.MouseLeftButtonDown -= QuestMarkerText_Click;
                    }
                }
            }
        }
        _questMarkerElements.Clear();
        QuestMarkersContainer.Children.Clear();
    }

    private void QuestMarker_Click(object sender, MouseButtonEventArgs e)
    {
        TaskObjectiveWithLocation? objective = null;

        if (sender is FrameworkElement element)
        {
            // 일반 마커 또는 OR 마커
            if (element.Tag is TaskObjectiveWithLocation obj)
            {
                objective = obj;
            }
            // Area 마커
            else if (element.Tag is AreaMarkerTag areaTag)
            {
                objective = areaTag.Objective;
            }
        }

        if (objective != null)
        {
            // 해당 Objective의 층으로 변경
            SelectFloorForObjective(objective);
            ShowQuestDrawer(objective);
            e.Handled = true;
        }
    }

    private void QuestMarkerText_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            // 해당 Objective의 층으로 변경
            SelectFloorForObjective(objective);
            ShowQuestDrawer(objective);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Objective의 층으로 맵 층을 변경합니다.
    /// </summary>
    private void SelectFloorForObjective(TaskObjectiveWithLocation objective)
    {
        // 다층 맵이 아니면 무시
        if (CmbFloorSelect.Visibility != Visibility.Visible)
            return;

        // Objective의 첫 번째 위치에서 FloorId 가져오기
        var location = objective.Locations.FirstOrDefault(l =>
            !string.IsNullOrEmpty(_currentMapKey) &&
            (l.MapName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) ||
             l.MapNormalizedName?.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) == true));

        if (location == null)
            location = objective.Locations.FirstOrDefault();

        if (location == null || string.IsNullOrEmpty(location.FloorId))
            return;

        // 현재 층과 같으면 무시
        if (string.Equals(_currentFloorId, location.FloorId, StringComparison.OrdinalIgnoreCase))
            return;

        // 층 콤보박스에서 해당 층 찾아서 선택
        for (int i = 0; i < CmbFloorSelect.Items.Count; i++)
        {
            if (CmbFloorSelect.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, location.FloorId, StringComparison.OrdinalIgnoreCase))
            {
                CmbFloorSelect.SelectedIndex = i;
                break;
            }
        }
    }

    #endregion

    #region 퀘스트 Drawer

    private void ShowQuestDrawer(TaskObjectiveWithLocation? selectedObjective = null)
    {
        // 선택 상태 저장 - ObjectiveId로 매칭하여 현재 _currentMapObjectives에서 찾기
        if (selectedObjective != null)
        {
            var matchingObjective = _currentMapObjectives.FirstOrDefault(o => o.ObjectiveId == selectedObjective.ObjectiveId);
            _selectedObjective = matchingObjective ?? selectedObjective;
        }
        else
        {
            _selectedObjective = null;
        }

        // 맵의 마커 하이라이트 업데이트
        UpdateMarkerHighlight();

        // Drawer 열기 (리사이즈 가능)
        QuestDrawerColumn.Width = new GridLength(320);
        QuestDrawerColumn.MinWidth = 250;
        QuestDrawerPanel.Visibility = Visibility.Visible;
        DrawerSplitter.Visibility = Visibility.Visible;

        // RefreshQuestDrawer()의 로직을 사용하여 다중 위치 지원
        RefreshQuestDrawer();

        // 선택된 목표가 있으면 해당 위치로 맵 이동
        if (_selectedObjective != null)
        {
            CenterOnObjective(_selectedObjective);
        }
    }

    private void UpdateMarkerHighlight()
    {
        // 이전 선택 마커 하이라이트 제거
        if (_selectedMarkerElement is Canvas prevCanvas)
        {
            RemoveMarkerHighlight(prevCanvas);
        }
        _selectedMarkerElement = null;

        // 새로운 선택 마커 하이라이트 추가
        if (_selectedObjective != null)
        {
            foreach (var marker in _questMarkerElements)
            {
                if (marker is Canvas canvas)
                {
                    var obj = GetObjectiveFromTag(canvas.Tag);
                    if (obj != null && obj.ObjectiveId == _selectedObjective.ObjectiveId)
                    {
                        AddMarkerHighlight(canvas);
                        _selectedMarkerElement = canvas;
                        break;
                    }
                }
            }
        }
    }

    private void AddMarkerHighlight(Canvas markerCanvas)
    {
        // 강조 표시용 외곽 링 추가
        var baseMarkerSize = _trackerService?.Settings.MarkerSize ?? 16;

        // 맵별 마커 스케일 적용
        var mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;

        var markerSize = baseMarkerSize * mapScale;
        var highlightSize = markerSize * 2.5;

        var highlightRing = new Ellipse
        {
            Width = highlightSize,
            Height = highlightSize,
            Stroke = new SolidColorBrush(Colors.Yellow),
            StrokeThickness = 3,
            Fill = Brushes.Transparent,
            Tag = "HighlightRing"
        };
        Canvas.SetLeft(highlightRing, -highlightSize / 2);
        Canvas.SetTop(highlightRing, -highlightSize / 2);
        Panel.SetZIndex(highlightRing, -1);
        markerCanvas.Children.Insert(0, highlightRing);

        // 펄스 애니메이션 추가
        var scaleTransform = new ScaleTransform(1, 1, highlightSize / 2, highlightSize / 2);
        highlightRing.RenderTransform = scaleTransform;

        var pulseAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.8,
            To = 1.2,
            Duration = TimeSpan.FromMilliseconds(800),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
    }

    private void RemoveMarkerHighlight(Canvas markerCanvas)
    {
        // HighlightRing 태그가 있는 요소 찾아 제거
        var highlightRing = markerCanvas.Children.OfType<Ellipse>()
            .FirstOrDefault(e => e.Tag as string == "HighlightRing");
        if (highlightRing != null)
        {
            markerCanvas.Children.Remove(highlightRing);
        }
    }

    private void BtnCloseQuestDrawer_Click(object sender, RoutedEventArgs e)
    {
        CloseQuestDrawer();
    }

    private void BtnToggleDrawer_Click(object sender, RoutedEventArgs e)
    {
        if (QuestDrawerPanel.Visibility == Visibility.Visible)
        {
            CloseQuestDrawer();
        }
        else
        {
            OpenQuestDrawer();
        }
    }

    private void OpenQuestDrawer()
    {
        QuestDrawerColumn.Width = new GridLength(320);
        QuestDrawerColumn.MinWidth = 250;
        QuestDrawerPanel.Visibility = Visibility.Visible;
        DrawerSplitter.Visibility = Visibility.Visible;
        TxtDrawerToggleIcon.Text = "<<";

        // Drawer 내용 새로고침
        RefreshQuestDrawer();
    }

    private void CloseQuestDrawer()
    {
        // 선택 상태 초기화
        _selectedObjective = null;
        UpdateMarkerHighlight();

        QuestDrawerColumn.Width = new GridLength(0);
        QuestDrawerColumn.MinWidth = 0;
        QuestDrawerPanel.Visibility = Visibility.Collapsed;
        DrawerSplitter.Visibility = Visibility.Collapsed;
        TxtDrawerToggleIcon.Text = ">>";
    }

    private void QuestObjectiveItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is QuestObjectiveViewModel vm)
        {
            // 선택 상태 업데이트 - _currentMapObjectives에서 매칭되는 객체 찾기
            _selectedObjective = _currentMapObjectives.FirstOrDefault(o => o.ObjectiveId == vm.Objective.ObjectiveId) ?? vm.Objective;

            // 해당 Objective의 층으로 변경
            SelectFloorForObjective(_selectedObjective);

            UpdateMarkerHighlight();

            // 사이드바 리스트 업데이트 (선택 상태 반영) - RefreshQuestDrawer()를 호출하여 그룹화 상태 유지
            RefreshQuestDrawer();

            // 해당 마커 위치로 맵 이동
            CenterOnObjective(_selectedObjective);
        }
    }

    private void WikiLinkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string wikiLink && !string.IsNullOrEmpty(wikiLink))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = wikiLink,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open wiki link: {ex.Message}");
            }
        }
    }

    private void CenterOnObjective(TaskObjectiveWithLocation objective)
    {
        if (string.IsNullOrEmpty(_currentMapKey)) return;

        var config = _trackerService?.GetMapConfig(_currentMapKey);
        if (config == null) return;

        // 현재 맵과 층에 맞는 위치 찾기 (MapNormalizedName도 확인)
        var location = objective.Locations.FirstOrDefault(l =>
            (l.MapName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) ||
             l.MapNormalizedName?.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) == true) &&
            (string.IsNullOrEmpty(_currentFloorId) || string.IsNullOrEmpty(l.FloorId) ||
             string.Equals(_currentFloorId, l.FloorId, StringComparison.OrdinalIgnoreCase)));

        // 현재 층에서 못 찾으면 같은 맵의 아무 위치나 찾기
        if (location == null)
        {
            location = objective.Locations.FirstOrDefault(l =>
                l.MapName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) ||
                l.MapNormalizedName?.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (location == null) return;

        // TarkovDBEditor 방식: config.GameToScreen 직접 사용
        var (screenX, screenY) = config.GameToScreen(location.X, location.Z ?? 0);

        // 맵 중심으로 이동 (현재 줌 레벨 고려)
        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        // 화면 중심을 마커 위치로 이동
        MapTranslate.X = viewerWidth / 2 - screenX * _zoomLevel;
        MapTranslate.Y = viewerHeight / 2 - screenY * _zoomLevel;
    }

    #endregion

    #region 탈출구 마커

    private async Task LoadExtractsAsync()
    {
        try
        {
            TxtStatus.Text = "Loading extract data...";

            await _extractService.EnsureLoadedAsync(msg =>
            {
                Dispatcher.Invoke(() => TxtStatus.Text = msg);
            });

            var count = _extractService.AllExtracts.Count;
            TxtStatus.Text = $"Loaded {count} extracts";

            if (!string.IsNullOrEmpty(_currentMapKey))
            {
                RefreshExtractMarkers();
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error loading extracts: {ex.Message}";
        }
    }

    private void RefreshExtractMarkers()
    {
        if (string.IsNullOrEmpty(_currentMapKey)) return;
        if (!_extractService.IsLoaded) return;

        // 기존 마커 제거
        ClearExtractMarkers();

        if (!_showExtractMarkers) return;

        // 맵 설정 가져오기
        var config = _trackerService?.GetMapConfig(_currentMapKey);
        if (config == null) return;

        // 디버그: CalibratedTransform 확인
        if (config.CalibratedTransform == null)
        {
            System.Diagnostics.Debug.WriteLine($"[MapPage] WARNING: CalibratedTransform is NULL for map '{_currentMapKey}'");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[MapPage] CalibratedTransform for '{_currentMapKey}': [{string.Join(", ", config.CalibratedTransform)}]");
        }

        // 현재 맵의 탈출구 가져오기 (MapConfig의 Aliases 사용)
        var extracts = _extractService.GetExtractsForMap(_currentMapKey, config);

        // 디버그: 로드된 탈출구 목록 출력
        System.Diagnostics.Debug.WriteLine($"[MapPage] Loaded {extracts.Count} extracts for '{_currentMapKey}':");
        foreach (var e in extracts)
        {
            System.Diagnostics.Debug.WriteLine($"  - {e.Name} ({e.Faction}) FloorId={e.FloorId ?? "null"}");
        }

        // 모든 탈출구를 개별적으로 표시 (그룹화 없음)
        foreach (var extract in extracts)
        {
            // 진영 필터 적용
            if (!ShouldShowExtract(extract.Faction)) continue;

            // TarkovDBEditor 방식: config.GameToScreen 직접 사용
            var (screenX, screenY) = config.GameToScreen(extract.X, extract.Z);

            // 디버그: 탈출구 좌표 변환 결과 출력
            System.Diagnostics.Debug.WriteLine($"[MapPage] Extract '{extract.Name}': Game({extract.X:F2}, {extract.Z:F2}) -> Screen({screenX:F2}, {screenY:F2})");

            var screenPos = new ScreenPosition
            {
                MapKey = _currentMapKey!,
                X = screenX,
                Y = screenY
            };

            // 층 정보 확인: 현재 선택된 층과 탈출구의 층 비교
            var isOnCurrentFloor = IsMarkerOnCurrentFloor(extract.FloorId);

            var marker = CreateExtractMarker(extract, screenPos, null, isOnCurrentFloor);
            _extractMarkerElements.Add(marker);
            ExtractMarkersContainer.Children.Add(marker);
        }
    }

    /// <summary>
    /// 마커가 현재 선택된 층에 있는지 확인합니다.
    /// </summary>
    /// <param name="markerFloorId">마커의 FloorId</param>
    /// <returns>현재 층에 있으면 true, 다른 층이면 false</returns>
    private bool IsMarkerOnCurrentFloor(string? markerFloorId)
    {
        // 단일 층 맵이거나 층 선택이 없는 경우: 모든 마커를 현재 층으로 간주
        if (string.IsNullOrEmpty(_currentFloorId))
            return true;

        // 마커에 층 정보가 없는 경우: 기본 층(main)으로 간주
        if (string.IsNullOrEmpty(markerFloorId))
        {
            // 현재 선택된 층이 main이면 표시, 아니면 다른 층으로 처리
            return string.Equals(_currentFloorId, "main", StringComparison.OrdinalIgnoreCase);
        }

        // 층 ID 비교 (대소문자 무시)
        return string.Equals(_currentFloorId, markerFloorId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 층 정보를 가져옵니다 (화살표 방향, 짧은 표시 문자, 색상).
    /// </summary>
    /// <param name="markerFloorId">마커의 FloorId</param>
    /// <returns>(화살표, 층 표시, 색상) - 현재 층이면 null 반환</returns>
    private (string arrow, string floorText, Color color)? GetFloorIndicator(string? markerFloorId)
    {
        if (string.IsNullOrEmpty(_currentMapKey) || string.IsNullOrEmpty(_currentFloorId))
            return null;

        var config = _trackerService?.GetMapConfig(_currentMapKey);
        if (config?.Floors == null || config.Floors.Count == 0)
            return null;

        // 현재 층의 Order 가져오기
        var currentFloor = config.Floors.FirstOrDefault(f =>
            string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));
        var currentOrder = currentFloor?.Order ?? 0;

        // 마커 층의 Order 가져오기 (FloorId가 없으면 main으로 간주)
        var effectiveFloorId = string.IsNullOrEmpty(markerFloorId) ? "main" : markerFloorId;
        var markerFloor = config.Floors.FirstOrDefault(f =>
            string.Equals(f.LayerId, effectiveFloorId, StringComparison.OrdinalIgnoreCase));
        var markerOrder = markerFloor?.Order ?? 0;

        // 같은 층이면 표시 안함
        if (currentOrder == markerOrder)
            return null;

        // 화살표 방향 결정 (마커가 현재 층보다 위에 있으면 ↑, 아래면 ↓)
        var isAbove = markerOrder > currentOrder;
        var arrow = isAbove ? "↑" : "↓";

        // 색상 결정 (위: 하늘색, 아래: 주황색)
        var color = isAbove
            ? Color.FromRgb(100, 181, 246)  // Light Blue
            : Color.FromRgb(255, 167, 38);  // Orange

        // 층 표시 문자 결정 (B: 지하, G: 기본층, 2/3: 2층/3층)
        string floorText;
        if (markerOrder < 0)
        {
            floorText = "B";
        }
        else if (markerOrder == 0)
        {
            floorText = "G";
        }
        else
        {
            floorText = (markerOrder + 1).ToString(); // Order 1 = 2층
        }

        return (arrow, floorText, color);
    }

    private List<List<MapExtract>> GroupExtractsByPosition(List<MapExtract> extracts)
    {
        var groups = new List<List<MapExtract>>();
        var used = new HashSet<string>();

        foreach (var extract in extracts)
        {
            if (used.Contains(extract.Id)) continue;

            var group = new List<MapExtract> { extract };
            used.Add(extract.Id);

            // 같은 위치(근접)의 다른 탈출구 찾기
            // 단, PMC+Scav 공용 탈출구만 그룹화 (같은 이름 또는 다른 진영이면서 매우 가까운 경우)
            foreach (var other in extracts)
            {
                if (used.Contains(other.Id)) continue;

                // 거리 계산
                var distance = Math.Sqrt(
                    Math.Pow(extract.X - other.X, 2) +
                    Math.Pow(extract.Z - other.Z, 2));

                // 그룹화 조건:
                // 1. 같은 이름이고 10유닛 이내 (PMC/Scav 공용 탈출구)
                // 2. 다른 진영이고 10유닛 이내 (PMC+Scav 겹치는 경우)
                var sameName = string.Equals(extract.Name, other.Name, StringComparison.OrdinalIgnoreCase);
                var differentFaction = extract.Faction != other.Faction;

                if (distance < 10 && (sameName || differentFaction))
                {
                    group.Add(other);
                    used.Add(other.Id);
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private (MapExtract extract, ExtractFaction faction) DetermineExtractDisplay(List<MapExtract> group)
    {
        if (group.Count == 1)
        {
            // Shared 탈출구는 PMC로 처리
            var faction = group[0].Faction == ExtractFaction.Shared ? ExtractFaction.Pmc : group[0].Faction;
            return (group[0], faction);
        }

        // PMC와 Scav 둘 다 있으면 PMC로 표시 (Shared도 PMC로 처리)
        var hasPmc = group.Any(e => e.Faction == ExtractFaction.Pmc || e.Faction == ExtractFaction.Shared);
        var hasScav = group.Any(e => e.Faction == ExtractFaction.Scav);

        if (hasPmc && hasScav)
        {
            // PMC 탈출구 정보를 기준으로, PMC로 표시
            var representative = group.FirstOrDefault(e => e.Faction == ExtractFaction.Pmc)
                ?? group.FirstOrDefault(e => e.Faction == ExtractFaction.Shared)
                ?? group[0];
            return (representative, ExtractFaction.Pmc);
        }

        // Shared는 PMC로 처리
        var resultFaction = group[0].Faction == ExtractFaction.Shared ? ExtractFaction.Pmc : group[0].Faction;
        return (group[0], resultFaction);
    }

    private bool ShouldShowExtract(ExtractFaction faction)
    {
        return faction switch
        {
            ExtractFaction.Pmc => _showPmcExtracts,
            ExtractFaction.Scav => _showScavExtracts,
            ExtractFaction.Shared => _showPmcExtracts, // Shared도 PMC 필터 사용
            ExtractFaction.Transit => _showTransitExtracts,
            _ => true
        };
    }

    private FrameworkElement CreateExtractMarker(MapExtract extract, ScreenPosition screenPos, ExtractFaction? overrideFaction = null, bool isOnCurrentFloor = true)
    {
        // 맵별 마커 스케일 적용
        var mapConfig = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;

        var baseSize = 20.0;
        var markerSize = baseSize * mapScale;

        // 진영 결정 (오버라이드 또는 기본)
        var faction = overrideFaction ?? extract.Faction;

        // 진영별 색상 설정
        var (fillColor, strokeColor) = GetExtractStyle(faction);

        // 다른 층의 마커는 색상을 흐리게 처리
        if (!isOnCurrentFloor)
        {
            fillColor = Color.FromArgb(100, fillColor.R, fillColor.G, fillColor.B);
            strokeColor = Color.FromArgb(150, strokeColor.R, strokeColor.G, strokeColor.B);
        }

        var canvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = extract
        };

        // 탈출구 이름 텍스트 (마커 위에 표시)
        var displayName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(extract.NameKo)
            ? extract.NameKo
            : extract.Name;

        var textSize = _extractNameTextSize * mapScale;

        // 이름과 층 뱃지를 담을 StackPanel
        var labelStackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // 이름 라벨
        var nameLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
            CornerRadius = new CornerRadius(3 * mapScale),
            Padding = new Thickness(4 * mapScale, 2 * mapScale, 4 * mapScale, 2 * mapScale),
            Child = new TextBlock
            {
                Text = displayName,
                FontSize = textSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fillColor),
                TextAlignment = TextAlignment.Center
            }
        };
        labelStackPanel.Children.Add(nameLabel);

        // 층 뱃지 (다층 맵에서 다른 층일 때만 표시) - GetFloorIndicator 사용하여 화살표 포함
        var floorInfo = GetFloorIndicator(extract.FloorId);
        if (floorInfo.HasValue)
        {
            var (arrow, floorText, indicatorColor) = floorInfo.Value;

            var floorBadge = new Border
            {
                Background = new SolidColorBrush(indicatorColor),
                CornerRadius = new CornerRadius(3 * mapScale),
                Padding = new Thickness(4 * mapScale, 2 * mapScale, 4 * mapScale, 2 * mapScale),
                Margin = new Thickness(3 * mapScale, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = $"{arrow}{floorText}",
                    FontSize = textSize * 0.9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center
                }
            };
            labelStackPanel.Children.Add(floorBadge);
        }

        // 이름 라벨 위치 측정 및 설정
        labelStackPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelWidth = labelStackPanel.DesiredSize.Width;
        var labelHeight = labelStackPanel.DesiredSize.Height;
        Canvas.SetLeft(labelStackPanel, -labelWidth / 2);
        Canvas.SetTop(labelStackPanel, -markerSize - labelHeight - 4 * mapScale);
        canvas.Children.Add(labelStackPanel);

        // 배경 원 (글로우 효과)
        var glowSize = markerSize * 1.5;
        var glow = new Ellipse
        {
            Width = glowSize,
            Height = glowSize,
            Fill = new SolidColorBrush(Color.FromArgb(80, fillColor.R, fillColor.G, fillColor.B))
        };
        Canvas.SetLeft(glow, -glowSize / 2);
        Canvas.SetTop(glow, -glowSize / 2);
        canvas.Children.Add(glow);

        // 메인 원
        var mainCircle = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = new SolidColorBrush(fillColor),
            Stroke = new SolidColorBrush(strokeColor),
            StrokeThickness = 2 * mapScale
        };
        Canvas.SetLeft(mainCircle, -markerSize / 2);
        Canvas.SetTop(mainCircle, -markerSize / 2);
        canvas.Children.Add(mainCircle);

        // 탈출구 아이콘 (비상대피 아이콘)
        var iconSize = markerSize * 0.7;
        var iconPath = CreateExtractIcon(iconSize, strokeColor);
        Canvas.SetLeft(iconPath, -iconSize / 2);
        Canvas.SetTop(iconPath, -iconSize / 2);
        canvas.Children.Add(iconPath);

        // 위치 설정
        Canvas.SetLeft(canvas, screenPos.X);
        Canvas.SetTop(canvas, screenPos.Y);

        // 줌에 상관없이 고정 크기 유지를 위한 역스케일 적용
        var inverseScale = 1.0 / _zoomLevel;
        canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        canvas.RenderTransformOrigin = new Point(0, 0);

        // 다른 층의 마커는 반투명 처리
        if (!isOnCurrentFloor)
        {
            canvas.Opacity = 0.5;
        }

        // 툴팁
        var factionText = faction switch
        {
            ExtractFaction.Pmc => "PMC",
            ExtractFaction.Scav => "Scav",
            _ => "Extract"
        };
        var tooltipFloorText = !string.IsNullOrEmpty(extract.FloorId) ? $" ({extract.FloorId})" : "";
        var differentFloorIndicator = !isOnCurrentFloor ? " ⬆⬇" : "";
        canvas.ToolTip = $"[{factionText}] {displayName}{tooltipFloorText}{differentFloorIndicator}";
        canvas.Cursor = Cursors.Hand;

        // 보정 모드용 드래그 이벤트 설정
        SetupExtractMarkerForCalibration(canvas, extract);

        return canvas;
    }

    private static (Color fill, Color stroke) GetExtractStyle(ExtractFaction faction)
    {
        return faction switch
        {
            ExtractFaction.Pmc => (
                Color.FromRgb(76, 175, 80),    // Green
                Colors.White),
            ExtractFaction.Scav => (
                Color.FromRgb(158, 158, 158),  // Gray (회색)
                Colors.White),
            ExtractFaction.Shared => (
                Color.FromRgb(76, 175, 80),    // Green (Shared도 PMC로 처리)
                Colors.White),
            ExtractFaction.Transit => (
                Color.FromRgb(255, 152, 0),    // Orange (주황색)
                Colors.White),
            _ => (
                Color.FromRgb(158, 158, 158),  // Gray
                Colors.White)
        };
    }

    private static FrameworkElement CreateExtractIcon(double size, Color strokeColor)
    {
        // 비상대피 아이콘 (uxwing.com emergency-exit-icon)
        // 원본 viewBox: 0 0 108.01 122.88
        var path = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(strokeColor),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size
        };

        // uxwing.com emergency exit icon path data
        // 뛰는 사람 + 문 아이콘
        var pathData = "M.5,0H15a.51.51,0,0,1,.5.5V83.38L35.16,82h.22l.24,0c2.07-.14,3.65-.26,4.73-1.23l1.86-2.17a1.12,1.12,0,0,1,1.49-.18l9.35,6.28a1.15,1.15,0,0,1,.49,1c0,.55-.19.7-.61,1.08A11.28,11.28,0,0,0,51.78,88a27.27,27.27,0,0,1-3,3.1,15.84,15.84,0,0,1-3.68,2.45c-2.8,1.36-5.45,1.54-8.59,1.76l-.24,0-.21,0L15.5,96.77v25.61a.52.52,0,0,1-.5.5H.5a.51.51,0,0,1-.5-.5V.5A.5.5,0,0,1,.5,0ZM46,59.91l9-19.12-.89-.25a12.43,12.43,0,0,0-4.77-.82c-1.9.28-3.68,1.42-5.67,2.7-.83.53-1.69,1.09-2.62,1.63-.7.33-1.51.86-2.19,1.25l-8.7,5a1.11,1.11,0,0,1-1.51-.42l-5.48-9.64a1.1,1.1,0,0,1,.42-1.51c3.43-2,7.42-4,10.75-6.14,4-2.49,7.27-4.48,11.06-5.42s8-.8,13.89,1c2.12.59,4.55,1.48,6.55,2.2,1,.35,1.8.66,2.44.87,9.86,3.29,13.19,9.66,15.78,14.6,1.12,2.13,2.09,4,3.34,5,.51.42,1.67.27,3,.09a21.62,21.62,0,0,1,2.64-.23c4.32-.41,8.66-.66,13-1a1.1,1.1,0,0,1,1.18,1L108,61.86A1.11,1.11,0,0,1,107,63L95,63.9c-5.33.38-9.19.66-15-2.47l-.12-.07a23.23,23.23,0,0,1-7.21-8.5l0,0L65.73,68.4a63.9,63.9,0,0,0,5.85,5.32c6,5,11,9.21,9.38,20.43a23.89,23.89,0,0,1-.65,2.93c-.27,1-.56,1.9-.87,2.84-2.29,6.54-4.22,13.5-6.29,20.13a1.1,1.1,0,0,1-1,.81l-11.66.78a1,1,0,0,1-.39,0,1.12,1.12,0,0,1-.75-1.38c2.45-8.12,5-16.25,7.39-24.38a29,29,0,0,0,.87-3,7,7,0,0,0,.08-2.65l0-.24a4.16,4.16,0,0,0-.73-2.22,53.23,53.23,0,0,0-8.76-5.57c-3.75-2.07-7.41-4.08-10.25-7a12.15,12.15,0,0,1-3.59-7.36A14.76,14.76,0,0,1,46,59.91ZM80.07,6.13a12.29,12.29,0,0,1,13.1,11.39v0a12.29,12.29,0,0,1-24.52,1.72v0A12.3,12.3,0,0,1,80,6.13ZM3.34,35H6.69V51.09H3.34V35Z";

        path.Data = Geometry.Parse(pathData);

        return path;
    }

    private void ClearExtractMarkers()
    {
        _extractMarkerElements.Clear();
        ExtractMarkersContainer.Children.Clear();
    }

    #region Calibration Mode

    private void SaveCalibrationAndRefresh()
    {
        if (_trackerService == null) return;

        var config = _trackerService.GetMapConfig(_currentMapKey ?? "");
        if (config?.CalibrationPoints != null && config.CalibrationPoints.Count >= 3)
        {
            // 변환 행렬 재계산
            config.CalibratedTransform = _calibrationService.CalculateAffineTransform(config.CalibrationPoints);

            if (config.CalibratedTransform != null)
            {
                TxtStatus.Text = $"Calibration saved! ({config.CalibrationPoints.Count} points)";

                // 설정 저장
                _trackerService.SaveSettings();

                // 마커 새로고침
                RefreshExtractMarkers();
                RefreshQuestMarkers();
            }
            else
            {
                TxtStatus.Text = "Calibration calculation failed.";
            }
        }
        else
        {
            _trackerService.SaveSettings();
        }
    }

    private void SetupExtractMarkerForCalibration(FrameworkElement marker, MapExtract extract)
    {
        marker.Tag = extract;
        marker.Cursor = Cursors.SizeAll;
        marker.MouseLeftButtonDown += ExtractMarker_CalibrationMouseDown;
        marker.MouseMove += ExtractMarker_CalibrationMouseMove;
        marker.MouseLeftButtonUp += ExtractMarker_CalibrationMouseUp;
    }

    private void ExtractMarker_CalibrationMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isCalibrationMode) return;
        if (sender is not FrameworkElement marker) return;

        _draggingExtractMarker = marker;
        _draggingExtract = marker.Tag as MapExtract;
        _extractDragStartPoint = e.GetPosition(ExtractMarkersContainer);
        _extractMarkerOriginalLeft = Canvas.GetLeft(marker);
        _extractMarkerOriginalTop = Canvas.GetTop(marker);

        marker.CaptureMouse();
        e.Handled = true;
    }

    private void ExtractMarker_CalibrationMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isCalibrationMode || _draggingExtractMarker == null) return;

        var currentPoint = e.GetPosition(ExtractMarkersContainer);
        var deltaX = currentPoint.X - _extractDragStartPoint.X;
        var deltaY = currentPoint.Y - _extractDragStartPoint.Y;

        Canvas.SetLeft(_draggingExtractMarker, _extractMarkerOriginalLeft + deltaX);
        Canvas.SetTop(_draggingExtractMarker, _extractMarkerOriginalTop + deltaY);
    }

    private void ExtractMarker_CalibrationMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isCalibrationMode || _draggingExtractMarker == null || _draggingExtract == null) return;

        _draggingExtractMarker.ReleaseMouseCapture();

        // 최종 위치 계산 (마커 중심 기준)
        var finalLeft = Canvas.GetLeft(_draggingExtractMarker);
        var finalTop = Canvas.GetTop(_draggingExtractMarker);

        // 마커 크기의 절반을 더해 중심점 계산
        var markerWidth = _draggingExtractMarker.ActualWidth > 0 ? _draggingExtractMarker.ActualWidth : 20;
        var markerHeight = _draggingExtractMarker.ActualHeight > 0 ? _draggingExtractMarker.ActualHeight : 20;
        var centerX = finalLeft + markerWidth / 2;
        var centerY = finalTop + markerHeight / 2;

        // 보정 포인트 추가
        var config = _trackerService?.GetMapConfig(_currentMapKey ?? "");
        if (config != null)
        {
            var calibrationPoint = new CalibrationPoint
            {
                Id = _draggingExtract.Id,
                Name = _draggingExtract.Name,
                GameX = _draggingExtract.X,
                GameZ = _draggingExtract.Z,
                ScreenX = centerX,
                ScreenY = centerY
            };

            var hasEnough = _calibrationService.AddCalibrationPoint(config, calibrationPoint);
            var pointCount = config.CalibrationPoints?.Count ?? 0;

            TxtStatus.Text = $"Calibration point set: {_draggingExtract.Name} ({pointCount} points)";
            if (pointCount >= 3)
            {
                TxtStatus.Text += " - Ready to apply!";
            }
            else
            {
                TxtStatus.Text += $" - Need {3 - pointCount} more points";
            }
        }

        _draggingExtractMarker = null;
        _draggingExtract = null;
        e.Handled = true;
    }

    #endregion

    private void ChkShowExtractMarkers_Changed(object sender, RoutedEventArgs e)
    {
        _showExtractMarkers = ChkShowExtractMarkers?.IsChecked ?? true;
        if (ExtractMarkersContainer != null)
        {
            ExtractMarkersContainer.Visibility = _showExtractMarkers ? Visibility.Visible : Visibility.Collapsed;
        }

        // DB에 저장
        SettingsService.Instance.MapShowExtracts = _showExtractMarkers;

        // 오버레이 맵도 새로고침
        OverlayMiniMapService.Instance.RefreshMap();
    }

    private void ChkExtractFilter_Changed(object sender, RoutedEventArgs e)
    {
        _showPmcExtracts = ChkShowPmcExtracts?.IsChecked ?? true;
        _showScavExtracts = ChkShowScavExtracts?.IsChecked ?? true;
        _showTransitExtracts = ChkShowTransitExtracts?.IsChecked ?? true;

        // DB에 저장
        var settingsService = SettingsService.Instance;
        settingsService.MapShowPmcExtracts = _showPmcExtracts;
        settingsService.MapShowScavExtracts = _showScavExtracts;
        settingsService.MapShowTransits = _showTransitExtracts;

        // 마커 새로고침
        RefreshExtractMarkers();

        // 오버레이 맵도 새로고침
        OverlayMiniMapService.Instance.RefreshMap();
    }

    private void SliderExtractTextSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtExtractTextSize != null)
        {
            _extractNameTextSize = e.NewValue;
            TxtExtractTextSize.Text = e.NewValue.ToString("F0");

            // DB에 저장
            SettingsService.Instance.MapExtractNameSize = _extractNameTextSize;

            // 마커 새로고침
            if (_extractService.IsLoaded)
            {
                RefreshExtractMarkers();
            }
        }
    }

    private void MarkerColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is string objectiveType)
        {
            // Windows 색상 선택 대화상자 열기
            var colorDialog = new System.Windows.Forms.ColorDialog();

            // 현재 색상 설정
            if (border.Background is SolidColorBrush currentBrush)
            {
                var c = currentBrush.Color;
                colorDialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            }

            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var selectedColor = colorDialog.Color;
                var hexColor = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";

                // UI 업데이트
                border.Background = new SolidColorBrush(Color.FromRgb(selectedColor.R, selectedColor.G, selectedColor.B));

                // 설정 저장
                if (_trackerService != null)
                {
                    _trackerService.Settings.SetMarkerColor(objectiveType, hexColor);
                    _trackerService.SaveSettings();

                    // 마커 새로고침
                    RefreshQuestMarkers();
                }
            }
        }
    }

    private void BtnResetColors_Click(object sender, RoutedEventArgs e)
    {
        // 기본 색상으로 복원
        var defaultColors = new Dictionary<string, string>
        {
            { "visit", "#4CAF50" },
            { "mark", "#FF9800" },
            { "plantItem", "#9C27B0" },
            { "extract", "#2196F3" },
            { "findItem", "#FFEB3B" }
        };

        if (_trackerService != null)
        {
            _trackerService.Settings.MarkerColors = defaultColors;
            _trackerService.SaveSettings();
        }

        // UI 업데이트
        UpdateMarkerColorUI();

        // 마커 새로고침
        RefreshQuestMarkers();
    }

    private void UpdateMarkerColorUI()
    {
        if (_trackerService == null) return;
        var colors = _trackerService.Settings.MarkerColors;

        if (colors.TryGetValue("visit", out var visit))
            ColorVisit.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(visit));
        if (colors.TryGetValue("mark", out var mark))
            ColorMark.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(mark));
        if (colors.TryGetValue("plantItem", out var plant))
            ColorPlant.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(plant));
        if (colors.TryGetValue("extract", out var extract))
            ColorExtract.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(extract));
        if (colors.TryGetValue("findItem", out var find))
            ColorFind.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(find));
    }

    #endregion

    #region 퀘스트 목표 체크박스 이벤트

    private void ObjectiveCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is TaskObjectiveWithLocation objective)
        {
            // ObjectiveId 기반 추적 + Quests 탭과 동기화를 위해 index도 함께 저장
            _progressService.SetObjectiveCompletedById(
                objective.ObjectiveId,
                true,
                objective.TaskNormalizedName,
                objective.ObjectiveIndex);
            // 마커 새로고침
            RefreshQuestMarkers();
            RefreshQuestDrawer();
        }
    }

    private void ObjectiveCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is TaskObjectiveWithLocation objective)
        {
            // ObjectiveId 기반 추적 + Quests 탭과 동기화를 위해 index도 함께 저장
            _progressService.SetObjectiveCompletedById(
                objective.ObjectiveId,
                false,
                objective.TaskNormalizedName,
                objective.ObjectiveIndex);
            // 마커 새로고침
            RefreshQuestMarkers();
            RefreshQuestDrawer();
        }
    }

    private void ChkHideCompletedObjectives_Changed(object sender, RoutedEventArgs e)
    {
        _hideCompletedObjectives = ChkHideCompletedObjectives?.IsChecked ?? true;

        // 설정 저장 (SettingsService를 통해 DB에 저장)
        SettingsService.Instance.MapHideCompletedObjectives = _hideCompletedObjectives;

        // 마커 새로고침
        RefreshQuestMarkers();

        // Drawer 새로고침
        if (QuestDrawerPanel.Visibility == Visibility.Visible)
        {
            RefreshQuestDrawer();
        }

        // 오버레이 맵도 새로고침
        OverlayMiniMapService.Instance.RefreshMap();
    }

    private void RefreshQuestDrawer()
    {
        // UI 요소가 아직 초기화되지 않았으면 스킵
        if (QuestObjectivesList == null) return;

        // 현재 맵의 목표들과 다른 맵의 목표들을 구분하여 표시
        var viewModels = new List<QuestObjectiveViewModel>();

        // 현재 맵의 활성 퀘스트들에 대해 모든 위치 정보 수집 (다른 맵 목표 포함)
        var processedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allObjectivesForDrawer = new List<TaskObjectiveWithLocation>();

        foreach (var obj in _currentMapObjectives)
        {
            // 이 퀘스트의 다른 맵 목표도 가져오기 (QuestId 기반)
            if (!processedQuestIds.Contains(obj.QuestId))
            {
                processedQuestIds.Add(obj.QuestId);

                // 이 퀘스트의 모든 목표 가져오기 (QuestId 기반)
                var allTaskObjectives = _objectiveService.GetObjectivesForTaskById(obj.QuestId);

                foreach (var taskObj in allTaskObjectives)
                {
                    // 퀘스트 상태 확인 (ID 기반 조회 우선, NormalizedName은 fallback)
                    var task = _progressService.GetTaskById(taskObj.QuestId)
                        ?? _progressService.GetTask(taskObj.TaskNormalizedName);

                    if (task == null)
                        continue;

                    var status = _progressService.GetStatus(task);

                    if (status == QuestStatus.Active)
                    {
                        // 목표 인덱스 및 완료 상태 설정
                        taskObj.ObjectiveIndex = GetObjectiveIndex(task, taskObj.Description);
                        taskObj.IsCompleted = _progressService.IsObjectiveCompletedById(taskObj.ObjectiveId);
                        allObjectivesForDrawer.Add(taskObj);
                    }
                }
            }
        }

        // 중복 제거 후 정렬 (현재 맵 목표 먼저, 그다음 다른 맵 목표)
        var uniqueObjectives = allObjectivesForDrawer
            .GroupBy(o => o.ObjectiveId)
            .Select(g => g.First())
            .ToList();

        // 현재 맵에 있는 목표 먼저, 그 다음 다른 맵 목표 (퀘스트명으로 정렬)
        var sortedObjectives = uniqueObjectives
            .OrderBy(obj =>
            {
                var isOnCurrentMap = obj.Locations.Any(loc =>
                    loc.MapNormalizedName?.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) == true ||
                    loc.MapName?.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) == true);
                return isOnCurrentMap ? 0 : 1;
            })
            .ThenBy(obj => obj.TaskName)
            .ToList();

        foreach (var obj in sortedObjectives)
        {
            // 현재 맵에 해당하는 위치의 FloorId로 층 표시 정보 가져오기
            var location = obj.Locations.FirstOrDefault(l =>
                !string.IsNullOrEmpty(_currentMapKey) &&
                (l.MapName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) ||
                 l.MapNormalizedName?.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase) == true));
            var floorIndicator = location != null ? GetFloorIndicator(location.FloorId) : null;

            viewModels.Add(new QuestObjectiveViewModel(
                obj, _loc, _progressService,
                obj.ObjectiveId == _selectedObjective?.ObjectiveId,
                _currentMapKey, _currentFloorId, floorIndicator));
        }

        // 맵별 진행률 업데이트 (필터 적용 전 전체 목표 기준)
        UpdateMapProgress(viewModels);

        // 필터 적용
        var filteredViewModels = ApplyDrawerFilters(viewModels);

        // 그룹화 적용
        object? itemToScrollTo = null;

        if (_drawerGroupByQuest)
        {
            var groupedItems = ApplyQuestGrouping(filteredViewModels);
            QuestObjectivesList.ItemsSource = groupedItems;

            // 선택된 목표 찾기
            if (_selectedObjective != null)
            {
                itemToScrollTo = groupedItems.FirstOrDefault(item =>
                    item is QuestObjectiveViewModel vm && vm.Objective.ObjectiveId == _selectedObjective.ObjectiveId);
            }
        }
        else
        {
            QuestObjectivesList.ItemsSource = filteredViewModels;

            // 선택된 목표 찾기
            if (_selectedObjective != null)
            {
                itemToScrollTo = filteredViewModels.FirstOrDefault(vm =>
                    vm.Objective.ObjectiveId == _selectedObjective.ObjectiveId);
            }
        }

        // 선택된 항목으로 스크롤
        if (itemToScrollTo != null)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                ScrollToItemInItemsControl(QuestObjectivesList, itemToScrollTo);
            });
        }
    }

    /// <summary>
    /// ItemsControl 내의 특정 항목으로 스크롤합니다.
    /// </summary>
    private void ScrollToItemInItemsControl(ItemsControl itemsControl, object item)
    {
        // ItemsControl의 부모 ScrollViewer 찾기
        var scrollViewer = FindVisualParent<ScrollViewer>(itemsControl);
        if (scrollViewer == null) return;

        // 항목의 컨테이너 찾기
        var container = itemsControl.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
        if (container == null) return;

        // 항목의 위치 계산
        var transform = container.TransformToAncestor(scrollViewer);
        var position = transform.Transform(new Point(0, 0));

        // 스크롤 (항목이 보이는 영역 중앙에 오도록)
        var targetOffset = scrollViewer.VerticalOffset + position.Y - scrollViewer.ViewportHeight / 3;
        scrollViewer.ScrollToVerticalOffset(Math.Max(0, targetOffset));
    }

    /// <summary>
    /// 시각적 트리에서 부모 요소를 찾습니다.
    /// </summary>
    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T found)
                return found;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    /// <summary>
    /// 퀘스트별로 목표를 그룹화하여 표시합니다.
    /// </summary>
    private List<object> ApplyQuestGrouping(List<QuestObjectiveViewModel> viewModels)
    {
        var result = new List<object>();
        var groupedByQuest = viewModels.GroupBy(vm => vm.Objective.TaskNormalizedName);

        foreach (var group in groupedByQuest.OrderBy(g => g.First().QuestName))
        {
            // 퀘스트 헤더 추가
            var firstObj = group.First();
            var completedCount = group.Count(vm => vm.IsChecked);
            var totalCount = group.Count();
            // DB에서 WikiPageLink 가져오기
            var quest = QuestDbService.Instance.GetQuestById(firstObj.Objective.QuestId);
            result.Add(new QuestGroupHeader
            {
                QuestName = firstObj.QuestName,
                QuestId = firstObj.Objective.QuestId,
                Progress = $"{completedCount}/{totalCount}",
                IsFullyCompleted = completedCount == totalCount,
                WikiLink = quest?.WikiPageLink
            });

            // 해당 퀘스트의 목표들 추가 (그룹화 플래그 설정)
            foreach (var vm in group)
            {
                vm.IsGrouped = true;
                result.Add(vm);
            }
        }

        return result;
    }

    /// <summary>
    /// 맵별 퀘스트 진행률을 업데이트합니다.
    /// </summary>
    private void UpdateMapProgress(List<QuestObjectiveViewModel> viewModels)
    {
        // UI 요소가 아직 초기화되지 않았으면 스킵
        if (TxtMapProgressCount == null || MapProgressBar == null) return;

        // 현재 맵의 목표만 카운트
        var currentMapObjectives = viewModels.Where(vm => vm.IsOnCurrentMap).ToList();
        var totalCount = currentMapObjectives.Count;
        var completedCount = currentMapObjectives.Count(vm => vm.IsChecked);

        // 진행률 텍스트 업데이트
        TxtMapProgressCount.Text = $"{completedCount}/{totalCount}";

        // 진행률 바 업데이트
        if (totalCount > 0)
        {
            var progressPercent = (double)completedCount / totalCount;
            // 부모 Border의 실제 너비를 계산하여 적용
            var parentBorder = MapProgressBar.Parent as Border;
            if (parentBorder != null)
            {
                // 레이아웃 업데이트 후 너비 계산
                parentBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var availableWidth = parentBorder.ActualWidth > 0 ? parentBorder.ActualWidth : 280; // 기본값 280
                MapProgressBar.Width = availableWidth * progressPercent;
            }
        }
        else
        {
            MapProgressBar.Width = 0;
        }
    }

    /// <summary>
    /// 목표 설명으로 목표 인덱스를 찾습니다.
    /// </summary>
    private static int GetObjectiveIndex(TarkovTask task, string description)
    {
        if (task.Objectives == null || task.Objectives.Count == 0) return -1;
        if (string.IsNullOrEmpty(description)) return -1;

        for (int i = 0; i < task.Objectives.Count; i++)
        {
            if (task.Objectives[i].Equals(description, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // 부분 매칭 시도
        for (int i = 0; i < task.Objectives.Count; i++)
        {
            if (task.Objectives[i].Contains(description, StringComparison.OrdinalIgnoreCase) ||
                description.Contains(task.Objectives[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    #endregion

    #region 퀘스트 드로어 필터링

    private void CmbStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 초기화 중에는 스킵
        if (!IsLoaded) return;

        if (CmbStatusFilter?.SelectedItem is ComboBoxItem item && item.Tag is string filter)
        {
            _drawerStatusFilter = filter;
            if (QuestDrawerPanel?.Visibility == Visibility.Visible)
            {
                RefreshQuestDrawer();
            }
        }
    }

    private void CmbTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 초기화 중에는 스킵
        if (!IsLoaded) return;

        if (CmbTypeFilter?.SelectedItem is ComboBoxItem item && item.Tag is string filter)
        {
            _drawerTypeFilter = filter;
            if (QuestDrawerPanel?.Visibility == Visibility.Visible)
            {
                RefreshQuestDrawer();
            }
        }
    }

    private void ChkCurrentMapOnly_Changed(object sender, RoutedEventArgs e)
    {
        // 초기화 중에는 스킵
        if (!IsLoaded) return;

        _drawerCurrentMapOnly = ChkCurrentMapOnly?.IsChecked ?? false;
        if (QuestDrawerPanel?.Visibility == Visibility.Visible)
        {
            RefreshQuestDrawer();
        }
    }

    private void ChkGroupByQuest_Changed(object sender, RoutedEventArgs e)
    {
        // 초기화 중에는 스킵
        if (!IsLoaded) return;

        _drawerGroupByQuest = ChkGroupByQuest?.IsChecked ?? false;
        if (QuestDrawerPanel?.Visibility == Visibility.Visible)
        {
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 필터를 적용하여 목표 목록을 필터링합니다.
    /// </summary>
    private List<QuestObjectiveViewModel> ApplyDrawerFilters(List<QuestObjectiveViewModel> viewModels)
    {
        var result = viewModels.AsEnumerable();

        // 완료/미완료 필터
        if (_drawerStatusFilter == "Incomplete")
        {
            result = result.Where(vm => !vm.IsChecked);
        }
        else if (_drawerStatusFilter == "Completed")
        {
            result = result.Where(vm => vm.IsChecked);
        }

        // 타입별 필터
        if (_drawerTypeFilter != "All")
        {
            result = result.Where(vm => vm.Objective.Type == _drawerTypeFilter);
        }

        // 현재 맵만 보기
        if (_drawerCurrentMapOnly)
        {
            result = result.Where(vm => vm.IsOnCurrentMap);
        }

        return result.ToList();
    }

    #endregion
}

/// <summary>
/// 퀘스트 목표 표시용 ViewModel
/// </summary>
public class QuestObjectiveViewModel
{
    public TaskObjectiveWithLocation Objective { get; }

    public string QuestName { get; }
    public string Description { get; }
    public string TypeDisplay { get; }
    public Brush TypeBrush { get; }
    public Visibility CompletedVisibility { get; }
    public bool IsSelected { get; }
    public Brush SelectionBorderBrush { get; }
    public Thickness SelectionBorderThickness { get; }

    // 체크박스용 프로퍼티
    public string ObjectiveId { get; }
    public bool IsChecked { get; set; }
    public TextDecorationCollection? TextDecoration { get; }
    public double ContentOpacity { get; }

    // 다른 맵 목표 표시용 프로퍼티
    public bool IsOnCurrentMap { get; }
    public string? OtherMapName { get; }
    public Visibility OtherMapBadgeVisibility { get; }
    public bool IsEnabled { get; }

    // 그룹화 표시용 프로퍼티
    public bool IsGrouped { get; set; }
    public Visibility QuestNameVisibility => IsGrouped ? Visibility.Collapsed : Visibility.Visible;
    public Thickness ItemMargin => IsGrouped ? new Thickness(16, 0, 0, 8) : new Thickness(0, 0, 0, 8);

    // 층 표시용 프로퍼티
    public string? FloorDisplay { get; }
    public Brush? FloorBrush { get; }
    public Visibility FloorBadgeVisibility { get; }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, bool isSelected = false)
        : this(objective, loc, null, isSelected, null, null, null)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected = false)
        : this(objective, loc, progressService, isSelected, null, null, null)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected, string? currentMapKey)
        : this(objective, loc, progressService, isSelected, currentMapKey, null, null)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected, string? currentMapKey, string? currentFloorId)
        : this(objective, loc, progressService, isSelected, currentMapKey, currentFloorId, null)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected, string? currentMapKey, string? currentFloorId, (string arrow, string floorText, Color color)? floorIndicator)
    {
        Objective = objective;
        IsSelected = isSelected;
        ObjectiveId = objective.ObjectiveId;

        QuestName = loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;

        Description = loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;

        TypeDisplay = GetTypeDisplay(objective.Type);
        TypeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(objective.MarkerColor));

        // 체크박스 상태 설정 (ObjectiveId 기반 - 동일 설명 목표 개별 추적)
        if (progressService != null)
        {
            IsChecked = progressService.IsObjectiveCompletedById(objective.ObjectiveId);
        }
        else
        {
            IsChecked = objective.IsCompleted;
        }
        CompletedVisibility = IsChecked ? Visibility.Visible : Visibility.Collapsed;

        // 완료 시 스타일 변경
        TextDecoration = IsChecked ? TextDecorations.Strikethrough : null;
        ContentOpacity = IsChecked ? 0.5 : 1.0;

        // 선택 상태에 따른 테두리 스타일 (선택 시 1px 노란 테두리, 미선택 시 투명)
        SelectionBorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(255, 215, 0)) : Brushes.Transparent;
        SelectionBorderThickness = isSelected ? new Thickness(1.5) : new Thickness(0);

        // 현재 맵에 있는 목표인지 확인 (공백/하이픈 차이 무시)
        if (!string.IsNullOrEmpty(currentMapKey))
        {
            IsOnCurrentMap = objective.Locations.Any(loc =>
                MatchesMapKey(loc.MapName, currentMapKey) ||
                MatchesMapKey(loc.MapNormalizedName, currentMapKey));

            if (!IsOnCurrentMap && objective.Locations.Count > 0)
            {
                // 다른 맵 이름 표시
                var otherLocation = objective.Locations.FirstOrDefault();
                OtherMapName = otherLocation?.MapName ?? "Other Map";
                OtherMapBadgeVisibility = Visibility.Visible;
                IsEnabled = false;
            }
            else
            {
                OtherMapBadgeVisibility = Visibility.Collapsed;
                IsEnabled = true;
            }
        }
        else
        {
            IsOnCurrentMap = true;
            OtherMapBadgeVisibility = Visibility.Collapsed;
            IsEnabled = true;
        }

        // 층 정보 초기화 - floorIndicator가 있으면 화살표 포함 표시
        if (floorIndicator.HasValue)
        {
            var (arrow, floorText, indicatorColor) = floorIndicator.Value;
            FloorDisplay = $"{arrow}{floorText}";
            FloorBrush = new SolidColorBrush(indicatorColor);
            FloorBadgeVisibility = Visibility.Visible;
        }
        else if (objective.Locations.Count > 0)
        {
            var floorId = objective.Locations[0].FloorId;
            if (!string.IsNullOrEmpty(floorId) && !string.IsNullOrEmpty(currentFloorId) &&
                !string.Equals(floorId, currentFloorId, StringComparison.OrdinalIgnoreCase))
            {
                // 다른 층이지만 floorIndicator가 없는 경우 (fallback)
                FloorDisplay = GetFloorDisplayText(floorId);
                FloorBrush = new SolidColorBrush(GetFloorColor(floorId, currentFloorId));
                FloorBadgeVisibility = Visibility.Visible;
            }
            else
            {
                FloorBadgeVisibility = Visibility.Collapsed;
            }
        }
        else
        {
            FloorBadgeVisibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// FloorId를 표시용 텍스트로 변환합니다.
    /// B = basement (main보다 아래), G = Ground (main), 2F, 3F 등
    /// </summary>
    public static string GetFloorDisplayText(string floorId)
    {
        // FloorId 패턴: "basement", "main", "first", "second", "third", "roof" 등
        return floorId.ToLowerInvariant() switch
        {
            "basement" or "basement1" or "basement-1" or "b1" => "B",
            "basement2" or "basement-2" or "b2" => "B2",
            "basement3" or "basement-3" or "b3" => "B3",
            "main" or "ground" or "1" or "first" => "G",
            "second" or "2" => "2F",
            "third" or "3" => "3F",
            "roof" or "rooftop" => "RF",
            _ => floorId.Length <= 3 ? floorId.ToUpperInvariant() : floorId.Substring(0, 2).ToUpperInvariant()
        };
    }

    /// <summary>
    /// 층에 따른 색상을 반환합니다.
    /// </summary>
    public static Color GetFloorColor(string floorId, string? currentFloorId)
    {
        // 현재 층과 같으면 회색, 다르면 강조색
        if (!string.IsNullOrEmpty(currentFloorId) &&
            string.Equals(floorId, currentFloorId, StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromRgb(128, 128, 128); // 회색
        }

        // 지하층은 파란색, 기본층은 초록색, 상층은 주황색
        var lowerFloorId = floorId.ToLowerInvariant();
        if (lowerFloorId.Contains("basement") || lowerFloorId.StartsWith("b"))
        {
            return Color.FromRgb(33, 150, 243); // 파란색
        }
        if (lowerFloorId == "main" || lowerFloorId == "ground" || lowerFloorId == "1" || lowerFloorId == "first")
        {
            return Color.FromRgb(76, 175, 80); // 초록색
        }
        return Color.FromRgb(255, 152, 0); // 주황색
    }

    private static string GetTypeDisplay(string type) => type switch
    {
        "visit" => "Visit",
        "mark" => "Mark",
        "plantItem" => "Plant",
        "extract" => "Extract",
        "findItem" => "Find",
        _ => type
    };

    /// <summary>
    /// 맵 이름 비교 (공백, 하이픈, 대소문자 차이 무시)
    /// </summary>
    private static bool MatchesMapKey(string? mapName, string mapKey)
    {
        if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(mapKey))
            return false;

        // 공백, 하이픈 제거 후 소문자로 비교
        var normalizedMapName = mapName.Replace(" ", "").Replace("-", "").ToLowerInvariant();
        var normalizedMapKey = mapKey.Replace(" ", "").Replace("-", "").ToLowerInvariant();
        return normalizedMapName == normalizedMapKey;
    }
}

/// <summary>
/// 퀘스트 그룹 헤더 (그룹화 시 사용)
/// </summary>
public class QuestGroupHeader
{
    public string QuestName { get; set; } = string.Empty;
    public string QuestId { get; set; } = string.Empty;
    public string Progress { get; set; } = string.Empty;
    public bool IsFullyCompleted { get; set; }
    public Brush HeaderBrush => IsFullyCompleted
        ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // Green
        : new SolidColorBrush(Color.FromRgb(197, 168, 74)); // Accent
    public TextDecorationCollection? TextDecoration => IsFullyCompleted ? TextDecorations.Strikethrough : null;
    public double Opacity => IsFullyCompleted ? 0.6 : 1.0;

    /// <summary>
    /// Wiki 링크 (DB의 WikiPageLink 사용)
    /// </summary>
    public string? WikiLink { get; set; }

    public bool HasWikiLink => !string.IsNullOrEmpty(WikiLink);
}

/// <summary>
/// 퀘스트 드로어용 DataTemplateSelector
/// </summary>
public class QuestDrawerTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupHeaderTemplate { get; set; }
    public DataTemplate? ObjectiveTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is QuestGroupHeader)
            return GroupHeaderTemplate;
        if (item is QuestObjectiveViewModel)
            return ObjectiveTemplate;
        return base.SelectTemplate(item, container);
    }
}
