using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;
using TarkovHelper.Models;
using TarkovHelper.Models.MapTracker;
using TarkovHelper.Services;
using TarkovHelper.Services.MapTracker;

namespace TarkovHelper.Pages;

/// <summary>
/// 맵 위치 추적 페이지.
/// 스크린샷 폴더를 감시하고 플레이어 위치를 맵 위에 표시합니다.
/// </summary>
public partial class MapTrackerPage : UserControl
{
    private readonly MapTrackerService? _trackerService;
    private readonly QuestObjectiveService _objectiveService = QuestObjectiveService.Instance;
    private readonly QuestProgressService _progressService = QuestProgressService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;
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
    private QuestMarkerStyle _questMarkerStyle = QuestMarkerStyle.Default;
    private double _questNameTextSize = 12.0;
    private TaskObjectiveWithLocation? _selectedObjective;
    private FrameworkElement? _selectedMarkerElement;

    // 탈출구 마커 관련 필드
    private readonly ExtractService _extractService = ExtractService.Instance;
    private readonly List<FrameworkElement> _extractMarkerElements = new();
    private bool _showExtractMarkers = true;
    private bool _showPmcExtracts = true;
    private bool _showScavExtracts = true;
    private double _extractNameTextSize = 10.0;
    private bool _hideCompletedObjectives = false;

    public MapTrackerPage()
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

            // 저장된 맵 상태 복원
            RestoreMapState();

            UpdateUI();

            // 퀘스트 목표 데이터 로드
            await LoadQuestObjectivesAsync();

            // 탈출구 데이터 로드
            await LoadExtractsAsync();

            // 퀘스트 진행 상태 변경 이벤트 구독
            _progressService.ProgressChanged += OnQuestProgressChanged;
            _progressService.ObjectiveProgressChanged += OnObjectiveProgressChanged;
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
    }

    private void SaveMapState()
    {
        if (_trackerService == null) return;

        _trackerService.Settings.LastSelectedMapKey = _currentMapKey;
        _trackerService.Settings.LastZoomLevel = _zoomLevel;
        _trackerService.Settings.LastTranslateX = MapTranslate.X;
        _trackerService.Settings.LastTranslateY = MapTranslate.Y;
        _trackerService.SaveSettings();
    }

    private void RestoreMapState()
    {
        if (_trackerService == null) return;
        var settings = _trackerService.Settings;

        // 저장된 맵이 있으면 복원
        if (!string.IsNullOrEmpty(settings.LastSelectedMapKey))
        {
            // 맵 선택 복원
            for (int i = 0; i < CmbMapSelect.Items.Count; i++)
            {
                if (CmbMapSelect.Items[i] is ComboBoxItem item &&
                    string.Equals(item.Tag as string, settings.LastSelectedMapKey, StringComparison.OrdinalIgnoreCase))
                {
                    CmbMapSelect.SelectedIndex = i;
                    break;
                }
            }

            // 줌 레벨 복원 (맵 로드 후 Dispatcher로 지연 실행)
            var savedZoom = settings.LastZoomLevel;
            var savedTranslateX = settings.LastTranslateX;
            var savedTranslateY = settings.LastTranslateY;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (savedZoom > 0)
                {
                    SetZoom(savedZoom);
                }
                MapTranslate.X = savedTranslateX;
                MapTranslate.Y = savedTranslateY;
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnLanguageChanged(object? sender, AppLanguage e)
    {
        UpdateLocalizedText();
    }

    private void UpdateLocalizedText()
    {
        // 다국어 지원이 필요한 경우 여기서 처리
    }

    private void LoadSettings()
    {
        if (_trackerService == null) return;
        var settings = _trackerService.Settings;

        // 스크린샷 폴더 및 마커 크기 설정
        TxtScreenshotFolder.Text = settings.ScreenshotFolderPath;
        SliderMarkerSize.Value = settings.MarkerSize;
        SliderPlayerMarkerSize.Value = settings.PlayerMarkerSize;
        UpdatePlayerMarkerSize(settings.PlayerMarkerSize);

        // 탈출구 설정 로드
        _showPmcExtracts = settings.ShowPmcExtracts;
        _showScavExtracts = settings.ShowScavExtracts;
        _extractNameTextSize = settings.ExtractNameTextSize;
        _showExtractMarkers = settings.ShowExtractMarkers;
        _showQuestMarkers = settings.ShowQuestMarkers;
        _questMarkerStyle = settings.QuestMarkerStyle;
        _questNameTextSize = settings.QuestNameTextSize;

        // 완료된 목표 숨기기 설정 로드
        _hideCompletedObjectives = settings.HideCompletedObjectives;

        // UI 업데이트 (이벤트 트리거 방지를 위해 직접 설정)
        ChkShowPmcExtracts.IsChecked = _showPmcExtracts;
        ChkShowScavExtracts.IsChecked = _showScavExtracts;
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
        BtnToggleTracking.Content = isWatching ? "Stop Tracking" : "Start Tracking";

        var successBrush = TryFindResource("SuccessBrush") as Brush ?? Brushes.Green;
        var secondaryBrush = TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        StatusIndicator.Fill = isWatching ? successBrush : secondaryBrush;
        TxtStatus.Text = isWatching ? "감시 중" : "대기 중";
    }

    #region 이벤트 핸들러 - 서비스

    private void OnPositionUpdated(object? sender, ScreenPosition position)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateMarkerPosition(position);
            UpdateTrailPath();
            UpdateCoordinatesDisplay(position);
        });
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
        TxtLastUpdate.Text = "마지막 업데이트: --";
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
            if (QuestDrawerPanel != null)
            {
                CloseQuestDrawer();
            }
        }
    }

    private void BtnAutoDetect_Click(object sender, RoutedEventArgs e)
    {
        var detectedPath = MapTrackerSettings.TryDetectScreenshotFolder();
        if (!string.IsNullOrEmpty(detectedPath))
        {
            TxtScreenshotFolder.Text = detectedPath;
            _trackerService?.ChangeScreenshotFolder(detectedPath);
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
        }
    }

    private void SliderMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMarkerSize != null && _trackerService != null)
        {
            var size = (int)e.NewValue;
            TxtMarkerSize.Text = size.ToString();
            UpdateMarkerSize(size);
        }
    }

    private void SliderPlayerMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtPlayerMarkerSize != null && _trackerService != null)
        {
            var size = (int)e.NewValue;
            TxtPlayerMarkerSize.Text = size.ToString();
            UpdatePlayerMarkerSize(size);
        }
    }

    private void CmbQuestMarkerStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_trackerService == null || CmbQuestMarkerStyle.SelectedIndex < 0) return;

        _questMarkerStyle = (QuestMarkerStyle)CmbQuestMarkerStyle.SelectedIndex;
        _trackerService.Settings.QuestMarkerStyle = _questMarkerStyle;
        _trackerService.SaveSettings();

        // 마커 다시 그리기
        RefreshQuestMarkers();
    }

    private void SliderQuestNameTextSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtQuestNameTextSize != null && _trackerService != null)
        {
            var size = (int)e.NewValue;
            TxtQuestNameTextSize.Text = size.ToString();
            _questNameTextSize = size;
            _trackerService.Settings.QuestNameTextSize = size;
            _trackerService.SaveSettings();

            // 마커 다시 그리기
            RefreshQuestMarkers();
        }
    }

    private bool _isFullScreen;

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

        _isFullScreen = true;

        // MainWindow의 공통 메뉴바 숨기기
        mainWindow.SetFullScreenMode(true);

        // Exit Full Screen 버튼 표시
        BtnExitFullScreen.Visibility = Visibility.Visible;
    }

    private void ExitFullScreen()
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        if (mainWindow == null) return;

        _isFullScreen = false;

        // MainWindow의 공통 메뉴바 다시 표시
        mainWindow.SetFullScreenMode(false);

        // Exit Full Screen 버튼 숨기기
        BtnExitFullScreen.Visibility = Visibility.Collapsed;
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        // 다음 프리셋으로 줌
        var nextPreset = ZoomPresets.FirstOrDefault(p => p > _zoomLevel);
        if (nextPreset > 0)
            SetZoom(nextPreset);
        else
            SetZoom(_zoomLevel * 1.25);
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        // 이전 프리셋으로 줌
        var prevPreset = ZoomPresets.LastOrDefault(p => p < _zoomLevel);
        if (prevPreset > 0)
            SetZoom(prevPreset);
        else
            SetZoom(_zoomLevel * 0.8);
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
        // 마우스 위치를 중심으로 줌
        var mousePos = e.GetPosition(MapCanvas);
        var oldZoom = _zoomLevel;

        // 줌 계산
        var zoomFactor = e.Delta > 0 ? 1.15 : 0.87;
        var newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        // 마우스 위치 기준 줌
        var scaleChange = newZoom / oldZoom;

        // 현재 마우스 위치의 실제 캔버스 좌표
        var canvasX = (mousePos.X - MapTranslate.X / oldZoom);
        var canvasY = (mousePos.Y - MapTranslate.Y / oldZoom);

        // 새로운 오프셋 계산 (마우스 위치가 고정되도록)
        MapTranslate.X -= canvasX * (scaleChange - 1) * oldZoom;
        MapTranslate.Y -= canvasY * (scaleChange - 1) * oldZoom;

        SetZoom(newZoom);
        e.Handled = true;
    }

    #endregion

    #endregion

    #region 맵/마커 관련 메서드

    private void LoadMapImage(string mapKey)
    {
        var config = _trackerService?.GetMapConfig(mapKey);
        if (config == null)
        {
            ShowNoMapPanel(true);
            return;
        }

        var imagePath = config.ImagePath;

        // 상대 경로인 경우 앱 디렉토리 기준으로 변환
        if (!System.IO.Path.IsPathRooted(imagePath))
        {
            imagePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, imagePath);
        }

        if (!File.Exists(imagePath))
        {
            ShowNoMapPanel(true);
            return;
        }

        try
        {
            var extension = System.IO.Path.GetExtension(imagePath).ToLowerInvariant();

            if (extension == ".svg")
            {
                // SVG 전처리: CSS 클래스를 인라인 스타일로 변환
                MapSvg.Visibility = Visibility.Collapsed;
                MapImage.Visibility = Visibility.Visible;

                var pngImage = ConvertSvgToPngWithPreprocessing(imagePath, config.ImageWidth, config.ImageHeight);
                if (pngImage != null)
                {
                    MapImage.Source = pngImage;
                    MapImage.Stretch = Stretch.None;
                    MapImage.Width = config.ImageWidth;
                    MapImage.Height = config.ImageHeight;

                    MapCanvas.Width = config.ImageWidth;
                    MapCanvas.Height = config.ImageHeight;
                    Canvas.SetLeft(MapImage, 0);
                    Canvas.SetTop(MapImage, 0);
                }
                else
                {
                    // 폴백: SvgViewbox 사용
                    MapImage.Visibility = Visibility.Collapsed;
                    MapSvg.Visibility = Visibility.Visible;
                    MapSvg.Source = new Uri(imagePath, UriKind.Absolute);
                    MapCanvas.Width = config.ImageWidth;
                    MapCanvas.Height = config.ImageHeight;
                    MapSvg.Width = config.ImageWidth;
                    MapSvg.Height = config.ImageHeight;
                    Canvas.SetLeft(MapSvg, 0);
                    Canvas.SetTop(MapSvg, 0);
                }
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

            // 맵을 화면 중앙에 배치
            CenterMapInView();
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
            MarkerRotation.Angle = position.Angle ?? 0;
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

        TxtLastUpdate.Text = $"마지막 업데이트: {DateTime.Now:HH:mm:ss}";
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
    }

    /// <summary>
    /// SVG 파일을 전처리(CSS 클래스→인라인 스타일 변환) 후 BitmapSource로 변환합니다.
    /// </summary>
    private BitmapSource? ConvertSvgToPngWithPreprocessing(string svgPath, int width, int height)
    {
        try
        {
            // 1. SVG 전처리: CSS 클래스를 인라인 스타일로 변환
            var preprocessor = new SvgStylePreprocessor();
            var processedSvg = preprocessor.ProcessSvgFile(svgPath);

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

        // 설정 저장
        if (_trackerService != null)
        {
            _trackerService.Settings.ShowQuestMarkers = _showQuestMarkers;
            _trackerService.SaveSettings();
        }
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
            // 인덱스 기반으로 완료 상태 확인 (Quests 탭과 연동)
            var isCompleted = objective.ObjectiveIndex >= 0 &&
                _progressService.IsObjectiveCompleted(objective.TaskNormalizedName, objective.ObjectiveIndex);
            objective.IsCompleted = isCompleted;

            // 완료된 목표 숨기기 설정이 활성화되어 있으면 스킵
            if (_hideCompletedObjectives && isCompleted)
                continue;

            foreach (var location in objective.Locations)
            {
                // tarkov.dev API 좌표를 화면 좌표로 변환 (Transform 배열 사용)
                // API position: x, y(높이), z → tarkov.dev 방식: [z, x]
                if (_trackerService != null &&
                    _trackerService.TransformApiCoordinate(_currentMapKey, location.X, location.Y, location.Z) is ScreenPosition screenPos)
                {
                    var marker = CreateQuestMarker(objective, location, screenPos);
                    _questMarkerElements.Add(marker);
                    QuestMarkersContainer.Children.Add(marker);
                }
            }
        }
    }

    private FrameworkElement CreateQuestMarker(TaskObjectiveWithLocation objective, QuestObjectiveLocation location, ScreenPosition screenPos)
    {
        var markerColor = (Color)ColorConverter.ConvertFromString(objective.MarkerColor);
        var markerBrush = new SolidColorBrush(markerColor);
        var glowBrush = new SolidColorBrush(Color.FromArgb(64, markerColor.R, markerColor.G, markerColor.B));

        // 초록색 원 스타일용 색상
        var greenColor = (Color)ColorConverter.ConvertFromString("#4CAF50");
        var greenBrush = new SolidColorBrush(greenColor);
        var greenGlowBrush = new SolidColorBrush(Color.FromArgb(64, greenColor.R, greenColor.G, greenColor.B));

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
        if (showName)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            // Border로 감싸서 중앙 정렬 가능하게 함
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

            // 텍스트 크기를 측정하여 중앙 정렬
            nameBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var textWidth = nameBorder.DesiredSize.Width;

            // 텍스트를 마커 중앙 아래에 위치
            Canvas.SetLeft(nameBorder, -textWidth / 2);
            Canvas.SetTop(nameBorder, markerSize / 2 + 4);
            canvas.Children.Add(nameBorder);
        }

        // 위치 설정
        Canvas.SetLeft(canvas, screenPos.X);
        Canvas.SetTop(canvas, screenPos.Y);

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
        canvas.ToolTip = $"{tooltipName}\n{tooltipDesc}";

        return canvas;
    }

    private void ClearQuestMarkers()
    {
        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas c)
            {
                c.MouseLeftButtonDown -= QuestMarker_Click;
            }
        }
        _questMarkerElements.Clear();
        QuestMarkersContainer.Children.Clear();
    }

    private void QuestMarker_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            ShowQuestDrawer(objective);
            e.Handled = true;
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

        // Drawer 열기
        QuestDrawerColumn.Width = new GridLength(320);
        QuestDrawerPanel.Visibility = Visibility.Visible;

        // 현재 맵의 모든 활성 목표를 표시 (선택 상태 포함)
        // progressService에서 최신 완료 상태를 가져와서 표시
        var viewModels = _currentMapObjectives.Select(obj =>
            new QuestObjectiveViewModel(obj, _loc, _progressService, obj.ObjectiveId == _selectedObjective?.ObjectiveId)).ToList();
        QuestObjectivesList.ItemsSource = viewModels;

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
                if (marker is Canvas canvas && canvas.Tag is TaskObjectiveWithLocation obj)
                {
                    if (obj.ObjectiveId == _selectedObjective.ObjectiveId)
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

    private void CloseQuestDrawer()
    {
        // 선택 상태 초기화
        _selectedObjective = null;
        UpdateMarkerHighlight();

        QuestDrawerColumn.Width = new GridLength(0);
        QuestDrawerPanel.Visibility = Visibility.Collapsed;
    }

    private void QuestObjectiveItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is QuestObjectiveViewModel vm)
        {
            // 선택 상태 업데이트 - _currentMapObjectives에서 매칭되는 객체 찾기
            _selectedObjective = _currentMapObjectives.FirstOrDefault(o => o.ObjectiveId == vm.Objective.ObjectiveId) ?? vm.Objective;
            UpdateMarkerHighlight();

            // 사이드바 리스트 업데이트 (선택 상태 반영)
            var viewModels = _currentMapObjectives.Select(obj =>
                new QuestObjectiveViewModel(obj, _loc, _progressService, obj.ObjectiveId == _selectedObjective?.ObjectiveId)).ToList();
            QuestObjectivesList.ItemsSource = viewModels;

            // 해당 마커 위치로 맵 이동
            CenterOnObjective(_selectedObjective);
        }
    }

    private void CenterOnObjective(TaskObjectiveWithLocation objective)
    {
        if (_trackerService == null || string.IsNullOrEmpty(_currentMapKey)) return;

        // 첫 번째 위치로 이동
        var location = objective.Locations.FirstOrDefault(l =>
            l.MapName.Equals(_currentMapKey, StringComparison.OrdinalIgnoreCase));

        if (location == null) return;

        // tarkov.dev API 좌표를 화면 좌표로 변환
        var screenPos = _trackerService.TransformApiCoordinate(_currentMapKey, location.X, location.Y, location.Z);
        if (screenPos == null) return;

        // 맵 중심으로 이동
        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        MapTranslate.X = viewerWidth / 2 - screenPos.X * _zoomLevel;
        MapTranslate.Y = viewerHeight / 2 - screenPos.Y * _zoomLevel;
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

        // 현재 맵의 탈출구 가져오기 (MapConfig의 Aliases 사용)
        var extracts = _extractService.GetExtractsForMap(_currentMapKey, config);

        // 같은 위치의 탈출구 그룹화 (PMC+Scav 공용 탈출구 처리)
        var extractGroups = GroupExtractsByPosition(extracts);

        foreach (var group in extractGroups)
        {
            // 그룹의 대표 탈출구와 진영 타입 결정
            var (representativeExtract, combinedFaction) = DetermineExtractDisplay(group);

            // 진영 필터 적용
            if (!ShouldShowExtract(combinedFaction)) continue;

            // 좌표 변환
            if (_trackerService != null &&
                _trackerService.TransformApiCoordinate(_currentMapKey, representativeExtract.X, representativeExtract.Y, representativeExtract.Z) is ScreenPosition screenPos)
            {
                var marker = CreateExtractMarker(representativeExtract, screenPos, combinedFaction);
                _extractMarkerElements.Add(marker);
                ExtractMarkersContainer.Children.Add(marker);
            }
        }
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
            foreach (var other in extracts)
            {
                if (used.Contains(other.Id)) continue;

                // 거리가 가까우면 (50 유닛 이내) 같은 그룹으로
                var distance = Math.Sqrt(
                    Math.Pow(extract.X - other.X, 2) +
                    Math.Pow(extract.Z - other.Z, 2));

                if (distance < 50)
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
        // Shared는 PMC로 처리되므로 Pmc 필터 적용
        return faction switch
        {
            ExtractFaction.Pmc => _showPmcExtracts,
            ExtractFaction.Scav => _showScavExtracts,
            ExtractFaction.Shared => _showPmcExtracts, // Shared도 PMC 필터 사용
            _ => true
        };
    }

    private FrameworkElement CreateExtractMarker(MapExtract extract, ScreenPosition screenPos, ExtractFaction? overrideFaction = null)
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

        // 이름 라벨 위치 측정 및 설정
        nameLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelWidth = nameLabel.DesiredSize.Width;
        var labelHeight = nameLabel.DesiredSize.Height;
        Canvas.SetLeft(nameLabel, -labelWidth / 2);
        Canvas.SetTop(nameLabel, -markerSize - labelHeight - 4 * mapScale);
        canvas.Children.Add(nameLabel);

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

        // 툴팁
        var factionText = faction switch
        {
            ExtractFaction.Pmc => "PMC",
            ExtractFaction.Scav => "Scav",
            _ => "Extract"
        };
        canvas.ToolTip = $"[{factionText}] {displayName}";
        canvas.Cursor = Cursors.Hand;

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
                Color.FromRgb(255, 183, 77),   // Light Orange (연주황색)
                Colors.White),
            ExtractFaction.Shared => (
                Color.FromRgb(76, 175, 80),    // Green (Shared도 PMC로 처리)
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

    private void ChkShowExtractMarkers_Changed(object sender, RoutedEventArgs e)
    {
        _showExtractMarkers = ChkShowExtractMarkers?.IsChecked ?? true;
        if (ExtractMarkersContainer != null)
        {
            ExtractMarkersContainer.Visibility = _showExtractMarkers ? Visibility.Visible : Visibility.Collapsed;
        }

        // 설정 저장
        if (_trackerService != null)
        {
            _trackerService.Settings.ShowExtractMarkers = _showExtractMarkers;
            _trackerService.SaveSettings();
        }
    }

    private void ChkExtractFilter_Changed(object sender, RoutedEventArgs e)
    {
        _showPmcExtracts = ChkShowPmcExtracts?.IsChecked ?? true;
        _showScavExtracts = ChkShowScavExtracts?.IsChecked ?? true;

        // 설정 저장
        if (_trackerService != null)
        {
            _trackerService.Settings.ShowPmcExtracts = _showPmcExtracts;
            _trackerService.Settings.ShowScavExtracts = _showScavExtracts;
            _trackerService.SaveSettings();
        }

        // 마커 새로고침
        RefreshExtractMarkers();
    }

    private void SliderExtractTextSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtExtractTextSize != null)
        {
            _extractNameTextSize = e.NewValue;
            TxtExtractTextSize.Text = e.NewValue.ToString("F0");

            // 설정 저장
            if (_trackerService != null)
            {
                _trackerService.Settings.ExtractNameTextSize = _extractNameTextSize;
                _trackerService.SaveSettings();
            }

            // 마커 새로고침
            if (_extractService.IsLoaded)
            {
                RefreshExtractMarkers();
            }
        }
    }

    #endregion

    #region 퀘스트 목표 체크박스 이벤트

    private void ObjectiveCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is TaskObjectiveWithLocation objective)
        {
            if (objective.ObjectiveIndex >= 0)
            {
                _progressService.SetObjectiveCompleted(objective.TaskNormalizedName, objective.ObjectiveIndex, true);
                // 마커 새로고침
                RefreshQuestMarkers();
                RefreshQuestDrawer();
            }
        }
    }

    private void ObjectiveCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.Tag is TaskObjectiveWithLocation objective)
        {
            if (objective.ObjectiveIndex >= 0)
            {
                _progressService.SetObjectiveCompleted(objective.TaskNormalizedName, objective.ObjectiveIndex, false);
                // 마커 새로고침
                RefreshQuestMarkers();
                RefreshQuestDrawer();
            }
        }
    }

    private void ChkHideCompletedObjectives_Changed(object sender, RoutedEventArgs e)
    {
        _hideCompletedObjectives = ChkHideCompletedObjectives?.IsChecked ?? false;

        // 설정 저장
        if (_trackerService != null)
        {
            _trackerService.Settings.HideCompletedObjectives = _hideCompletedObjectives;
            _trackerService.SaveSettings();
        }

        // 마커 새로고침
        RefreshQuestMarkers();

        // Drawer 새로고침
        if (QuestDrawerPanel.Visibility == Visibility.Visible)
        {
            RefreshQuestDrawer();
        }
    }

    private void RefreshQuestDrawer()
    {
        var viewModels = _currentMapObjectives.Select(obj =>
            new QuestObjectiveViewModel(obj, _loc, _progressService, obj.ObjectiveId == _selectedObjective?.ObjectiveId)).ToList();
        QuestObjectivesList.ItemsSource = viewModels;
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

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, bool isSelected = false)
        : this(objective, loc, null, isSelected)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected = false)
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

        // 체크박스 상태 설정 (인덱스 기반 - Quests 탭과 연동)
        if (progressService != null && objective.ObjectiveIndex >= 0)
        {
            IsChecked = progressService.IsObjectiveCompleted(objective.TaskNormalizedName, objective.ObjectiveIndex);
        }
        else
        {
            IsChecked = objective.IsCompleted;
        }
        CompletedVisibility = IsChecked ? Visibility.Visible : Visibility.Collapsed;

        // 완료 시 스타일 변경
        TextDecoration = IsChecked ? TextDecorations.Strikethrough : null;
        ContentOpacity = IsChecked ? 0.5 : 1.0;

        // 선택 상태에 따른 테두리 스타일
        SelectionBorderBrush = isSelected ? new SolidColorBrush(Colors.Yellow) : Brushes.Transparent;
        SelectionBorderThickness = isSelected ? new Thickness(2) : new Thickness(2);
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
}
