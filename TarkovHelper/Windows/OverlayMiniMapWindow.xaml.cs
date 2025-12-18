using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Logging;
using TarkovHelper.Services.Map;

namespace TarkovHelper.Windows;

/// <summary>
/// 오버레이 미니맵 윈도우 - 심플 버전 (컨트롤 없음)
/// </summary>
public partial class OverlayMiniMapWindow : Window
{
    #region Win32 API for Click-Through

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    #endregion

    private static readonly ILogger _log = Log.For<OverlayMiniMapWindow>();

    private readonly OverlayMiniMapSettings _settings;
    private MapTrackerService? _trackerService;
    private string? _currentMapKey;
    private MapConfig? _currentMapConfig;

    private IntPtr _hwnd;
    private bool _isClickThrough;

    // 휠 클릭 드래그용 필드
    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartOffsetX;
    private double _panStartOffsetY;

    /// <summary>
    /// 설정 변경 이벤트
    /// </summary>
    public event Action<OverlayMiniMapSettings>? SettingsChanged;

    /// <summary>
    /// 윈도우 닫힘 이벤트
    /// </summary>
    public event Action? OverlayClosed;

    public OverlayMiniMapWindow(OverlayMiniMapSettings settings)
    {
        InitializeComponent();

        _settings = settings;
        ApplySettings();

        Loaded += OnLoaded;
        Closing += OnClosing;
        SizeChanged += OnSizeChanged;
        LocationChanged += OnLocationChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // 초기 위치 설정
        if (_settings.PositionX < 0 || _settings.PositionY < 0)
        {
            PositionToTopRight();
        }

        // Click-through 상태 적용
        if (_settings.ClickThrough)
        {
            EnableClickThrough();
        }

        // MapTrackerService 연결
        ConnectToMapTracker();

        _log.Info("OverlayMiniMapWindow loaded");
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveSettings();
        OverlayClosed?.Invoke();
        _log.Info("OverlayMiniMapWindow closing");
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _settings.Width = ActualWidth;
        _settings.Height = ActualHeight;
        UpdateMapView();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        _settings.PositionX = Left;
        _settings.PositionY = Top;
    }

    #region Settings

    private void ApplySettings()
    {
        Width = _settings.Width;
        Height = _settings.Height;

        if (_settings.PositionX >= 0 && _settings.PositionY >= 0)
        {
            Left = _settings.PositionX;
            Top = _settings.PositionY;
        }

        // 투명도 적용
        MainBorder.Opacity = _settings.Opacity;
    }

    private void SaveSettings()
    {
        _settings.PositionX = Left;
        _settings.PositionY = Top;
        _settings.Width = ActualWidth;
        _settings.Height = ActualHeight;
        SettingsChanged?.Invoke(_settings);
    }

    private void PositionToTopRight()
    {
        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 20;
        Top = screen.Top + 20;
        _settings.PositionX = Left;
        _settings.PositionY = Top;
    }

    #endregion

    #region MapTracker Integration

    private void ConnectToMapTracker()
    {
        try
        {
            _trackerService = MapTrackerService.Instance;
            _trackerService.PositionUpdated += OnPositionUpdated;
            _trackerService.MapChanged += OnMapChanged;

            // 현재 맵 로드
            var currentMap = _trackerService.CurrentMapKey;
            _log.Info($"ConnectToMapTracker: CurrentMapKey = '{currentMap}'");

            if (!string.IsNullOrEmpty(currentMap))
            {
                LoadMap(currentMap);
            }
            else
            {
                _log.Warning("No current map set in MapTrackerService");
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to connect to MapTrackerService", ex);
        }
    }

    private void OnPositionUpdated(object? sender, ScreenPosition position)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdatePlayerMarker(position);

            if (_settings.ViewMode == MiniMapViewMode.PlayerTracking)
            {
                CenterOnPlayer(position);
            }
        });
    }

    private void OnMapChanged(string mapKey)
    {
        Dispatcher.BeginInvoke(() =>
        {
            LoadMap(mapKey);
        });
    }

    private void LoadMap(string mapKey)
    {
        try
        {
            _log.Info($"LoadMap called with mapKey: '{mapKey}'");

            _currentMapKey = mapKey;
            _currentMapConfig = _trackerService?.GetMapConfig(mapKey);

            _log.Info($"MapConfig: {(_currentMapConfig != null ? $"found, SvgFileName={_currentMapConfig.SvgFileName}, Size={_currentMapConfig.ImageWidth}x{_currentMapConfig.ImageHeight}" : "null")}");

            if (_currentMapConfig == null)
            {
                TxtNoMap.Visibility = Visibility.Visible;
                MapSvg.Source = null;
                return;
            }

            TxtNoMap.Visibility = Visibility.Collapsed;

            // SVG 맵 로드
            if (string.IsNullOrEmpty(_currentMapConfig.SvgFileName))
            {
                TxtNoMap.Text = $"No SVG file for: {mapKey}";
                TxtNoMap.Visibility = Visibility.Visible;
                return;
            }

            var svgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "Assets", "DB", "Maps", _currentMapConfig.SvgFileName);

            _log.Info($"SVG path: {svgPath}, exists: {System.IO.File.Exists(svgPath)}");

            if (System.IO.File.Exists(svgPath))
            {
                LoadSvgMap(svgPath);
            }
            else
            {
                _log.Warning($"Map SVG not found: {svgPath}");
                TxtNoMap.Text = $"Map not found: {mapKey}";
                TxtNoMap.Visibility = Visibility.Visible;
            }

            // 맵 뷰 업데이트 먼저
            UpdateMapView();

            // 마커 로드 (비동기, 테스트 마커 포함)
            _ = LoadMarkersAsync();

            _log.Info($"Map loaded successfully: {mapKey}");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load map: {mapKey}", ex);
        }
    }

    private void LoadSvgMap(string svgPath)
    {
        try
        {
            _log.Info($"LoadSvgMap: Loading {svgPath}");

            // MapPage와 동일한 방식: SvgViewbox 사용
            MapSvg.Source = new Uri(svgPath, UriKind.Absolute);

            // 중요: MapPage처럼 명시적으로 Visibility 설정
            MapSvg.Visibility = Visibility.Visible;

            // config에서 맵 크기 설정
            if (_currentMapConfig != null)
            {
                MapSvg.Width = _currentMapConfig.ImageWidth;
                MapSvg.Height = _currentMapConfig.ImageHeight;
                MapCanvas.Width = _currentMapConfig.ImageWidth;
                MapCanvas.Height = _currentMapConfig.ImageHeight;

                // MapPage처럼 (0,0)에 위치 설정
                Canvas.SetLeft(MapSvg, 0);
                Canvas.SetTop(MapSvg, 0);

                _log.Debug($"MapSvg: Size={MapSvg.Width}x{MapSvg.Height}, Visibility={MapSvg.Visibility}");
                _log.Debug($"MapCanvas: Size={MapCanvas.Width}x{MapCanvas.Height}");
            }

            // 맵을 창에 맞게 자동 줌 계산
            FitMapToWindow();

            _log.Info($"SVG loaded, Zoom={_settings.ZoomLevel:F4}, Offset=({_settings.MapOffsetX:F1}, {_settings.MapOffsetY:F1})");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to load SVG: {svgPath}", ex);
        }
    }

    private void FitMapToWindow()
    {
        if (_currentMapConfig == null) return;

        var mapWidth = _currentMapConfig.ImageWidth;
        var mapHeight = _currentMapConfig.ImageHeight;
        var viewWidth = ActualWidth > 0 ? ActualWidth : 300;
        var viewHeight = ActualHeight > 0 ? ActualHeight : 300;

        // 맵이 창에 맞도록 줌 레벨 계산
        var scaleX = viewWidth / mapWidth;
        var scaleY = viewHeight / mapHeight;
        var fitZoom = Math.Min(scaleX, scaleY) * 0.95; // 5% 여백

        _settings.ZoomLevel = Math.Max(OverlayMiniMapSettings.MinZoom, Math.Min(fitZoom, OverlayMiniMapSettings.MaxZoom));

        // 맵을 중앙에 배치
        var scaledWidth = mapWidth * _settings.ZoomLevel;
        var scaledHeight = mapHeight * _settings.ZoomLevel;
        _settings.MapOffsetX = (viewWidth - scaledWidth) / 2;
        _settings.MapOffsetY = (viewHeight - scaledHeight) / 2;

        _log.Debug($"Auto-fit zoom: {_settings.ZoomLevel:F3}, offset: ({_settings.MapOffsetX:F0}, {_settings.MapOffsetY:F0})");
    }

    /// <summary>
    /// 테스트 마커 추가 (디버깅용)
    /// 맵 중앙에 빨간색 원이 표시되어야 함
    /// </summary>
    private void AddTestMarker()
    {
        if (_currentMapConfig == null) return;

        // 맵 중앙 좌표에 테스트 마커 추가
        var centerX = _currentMapConfig.ImageWidth / 2.0;
        var centerY = _currentMapConfig.ImageHeight / 2.0;

        // 줌에 반비례하는 마커 크기
        var markerSize = 15.0 / _settings.ZoomLevel;
        var strokeThickness = 3.0 / _settings.ZoomLevel;

        var testMarker = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = new SolidColorBrush(Colors.Red),
            Stroke = new SolidColorBrush(Colors.Yellow),
            StrokeThickness = strokeThickness,
            Opacity = 1.0
        };

        Canvas.SetLeft(testMarker, centerX - markerSize / 2);
        Canvas.SetTop(testMarker, centerY - markerSize / 2);
        ExtractMarkersContainer.Children.Add(testMarker);

        _log.Info($"AddTestMarker: Added test marker at map center ({centerX:F0}, {centerY:F0}), size={markerSize:F1}");
    }

    private async Task LoadMarkersAsync()
    {
        ExtractMarkersContainer.Children.Clear();
        QuestMarkersContainer.Children.Clear();

        if (_currentMapKey == null || _currentMapConfig == null)
        {
            _log.Debug("LoadMarkersAsync: mapKey or mapConfig is null, skipping");
            return;
        }

        try
        {
            _log.Info($"LoadMarkersAsync START: map={_currentMapKey}, ShowExtract={_settings.ShowExtractMarkers}, ShowQuest={_settings.ShowQuestMarkers}");

            // 탈출구 마커 로드
            if (_settings.ShowExtractMarkers)
            {
                await LoadExtractMarkersAsync();
            }

            // 퀘스트 마커는 일단 비활성화 (사용자 요청)
            // if (_settings.ShowQuestMarkers)
            // {
            //     await LoadQuestMarkersAsync();
            // }

            _log.Info($"LoadMarkersAsync DONE: Extract={ExtractMarkersContainer.Children.Count}, Quest={QuestMarkersContainer.Children.Count}");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to load markers", ex);
        }
    }

    private async Task LoadExtractMarkersAsync()
    {
        var extractService = ExtractService.Instance;
        if (extractService == null)
        {
            _log.Warning("LoadExtractMarkersAsync: ExtractService.Instance is null");
            return;
        }

        // 데이터 로드 보장
        if (!extractService.IsLoaded)
        {
            _log.Info("LoadExtractMarkersAsync: Loading extract data...");
            var loadResult = await extractService.LoadAsync();
            _log.Info($"LoadExtractMarkersAsync: Load result = {loadResult}");
        }

        // MapConfig를 전달하여 별칭(aliases) 지원
        var extracts = extractService.GetExtractsForMap(_currentMapKey!, _currentMapConfig!);
        if (extracts == null || extracts.Count == 0)
        {
            _log.Warning($"LoadExtractMarkersAsync: No extracts found for map: {_currentMapKey}");
            return;
        }

        _log.Info($"LoadExtractMarkersAsync: Found {extracts.Count} extracts for {_currentMapKey}");

        // SettingsService에서 필터 설정 가져오기 (MapPage와 동기화)
        var settingsService = SettingsService.Instance;
        var showPmc = settingsService.MapShowPmcExtracts;
        var showScav = settingsService.MapShowScavExtracts;
        var showTransit = settingsService.MapShowTransits;

        var addedCount = 0;
        foreach (var extract in extracts)
        {
            // 팩션 필터 적용
            var shouldShow = extract.Faction switch
            {
                ExtractFaction.Pmc => showPmc,
                ExtractFaction.Scav => showScav,
                ExtractFaction.Transit => showTransit,
                ExtractFaction.Shared => showPmc || showScav, // Shared는 PMC/Scav 중 하나라도 켜져 있으면 표시
                _ => true
            };

            if (!shouldShow) continue;

            var (screenX, screenY) = _currentMapConfig!.GameToScreen(extract.X, extract.Z);
            _log.Debug($"  Extract '{extract.Name}': Game({extract.X:F1}, {extract.Z:F1}) -> Screen({screenX:F1}, {screenY:F1})");

            // 줌에 반비례하는 마커 크기 (줌 후에도 일정한 화면 크기 유지)
            // 줌 0.1에서 baseSize 10 -> 실제 크기 100 -> 화면에서 10px로 보임
            var baseScreenSize = 10.0;
            var markerSize = baseScreenSize / _settings.ZoomLevel;
            var marker = CreateMarkerEllipse(GetExtractColor(extract.Faction), markerSize);
            Canvas.SetLeft(marker, screenX - markerSize / 2);
            Canvas.SetTop(marker, screenY - markerSize / 2);
            ExtractMarkersContainer.Children.Add(marker);
            addedCount++;
        }

        _log.Info($"LoadExtractMarkersAsync: Added {addedCount} extract markers (PMC={showPmc}, Scav={showScav}, Transit={showTransit})");
    }

    private async Task LoadQuestMarkersAsync()
    {
        if (_currentMapConfig == null)
        {
            _log.Warning("LoadQuestMarkersAsync: _currentMapConfig is null");
            return;
        }

        var objectiveService = QuestObjectiveService.Instance;
        if (objectiveService == null)
        {
            _log.Warning("LoadQuestMarkersAsync: QuestObjectiveService.Instance is null");
            return;
        }

        // 데이터 로드 보장
        if (!objectiveService.IsLoaded)
        {
            _log.Info("LoadQuestMarkersAsync: Loading quest objectives...");
            var loadResult = await objectiveService.LoadAsync();
            _log.Info($"LoadQuestMarkersAsync: Load result = {loadResult}");
        }

        var objectives = objectiveService.GetObjectivesForMap(_currentMapKey!, _currentMapConfig);
        if (objectives == null || objectives.Count == 0)
        {
            _log.Warning($"LoadQuestMarkersAsync: No quest objectives found for map: {_currentMapKey}");
            return;
        }

        _log.Info($"LoadQuestMarkersAsync: Found {objectives.Count} quest objectives for {_currentMapKey}");

        var addedCount = 0;
        foreach (var obj in objectives)
        {
            // 위치 정보가 있는 경우에만 마커 생성
            if (obj.Locations == null || obj.Locations.Count == 0) continue;

            var firstLocation = obj.Locations[0];
            var (screenX, screenY) = _currentMapConfig.GameToScreen(firstLocation.X, firstLocation.Y);

            // 줌에 반비례하는 마커 크기
            var baseScreenSize = 8.0;
            var markerSize = baseScreenSize / _settings.ZoomLevel;
            var marker = CreateMarkerEllipse(GetQuestMarkerColor(obj.Type), markerSize);
            Canvas.SetLeft(marker, screenX - markerSize / 2);
            Canvas.SetTop(marker, screenY - markerSize / 2);
            QuestMarkersContainer.Children.Add(marker);
            addedCount++;
        }

        _log.Info($"LoadQuestMarkersAsync: Added {addedCount} quest markers");
    }

    private Ellipse CreateMarkerEllipse(Color color, double size)
    {
        // 테두리 두께도 줌에 반비례하게 조정
        var strokeThickness = Math.Max(1, 2 / _settings.ZoomLevel);

        return new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = strokeThickness,
            Opacity = 0.9
        };
    }

    private static Color GetExtractColor(ExtractFaction faction)
    {
        return faction switch
        {
            ExtractFaction.Pmc => Color.FromRgb(0x4C, 0xAF, 0x50),      // Green
            ExtractFaction.Scav => Color.FromRgb(0x8B, 0xC3, 0x4A),     // Light Green
            ExtractFaction.Shared => Color.FromRgb(0x00, 0xBC, 0xD4),   // Cyan
            ExtractFaction.Transit => Color.FromRgb(0x21, 0x96, 0xF3),  // Blue
            _ => Color.FromRgb(0x4C, 0xAF, 0x50)
        };
    }

    private static Color GetQuestMarkerColor(string? objectiveType)
    {
        return objectiveType?.ToLower() switch
        {
            "visit" => Color.FromRgb(0x4C, 0xAF, 0x50),      // Green
            "mark" => Color.FromRgb(0xFF, 0x98, 0x00),       // Orange
            "plantitem" => Color.FromRgb(0x9C, 0x27, 0xB0),  // Purple
            "extract" => Color.FromRgb(0x21, 0x96, 0xF3),    // Blue
            "finditem" => Color.FromRgb(0xFF, 0xEB, 0x3B),   // Yellow
            _ => Color.FromRgb(0x4C, 0xAF, 0x50)
        };
    }

    private void UpdatePlayerMarker(ScreenPosition position)
    {
        if (_currentMapConfig == null) return;

        PlayerMarkerContainer.Visibility = Visibility.Visible;
        Canvas.SetLeft(PlayerMarkerContainer, position.X - 10);
        Canvas.SetTop(PlayerMarkerContainer, position.Y - 10);

        if (position.Angle.HasValue)
        {
            PlayerRotation.Angle = position.Angle.Value;
            PlayerDirectionArrow.Visibility = Visibility.Visible;
        }
        else
        {
            PlayerDirectionArrow.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateMapView()
    {
        if (_currentMapConfig == null)
        {
            _log.Debug("UpdateMapView: _currentMapConfig is null, skipping");
            return;
        }

        var zoom = _settings.ZoomLevel;
        MapScale.ScaleX = zoom;
        MapScale.ScaleY = zoom;

        // 항상 오프셋 적용 (팬 기능이 모든 모드에서 작동하도록)
        MapTranslate.X = _settings.MapOffsetX;
        MapTranslate.Y = _settings.MapOffsetY;

        _log.Debug($"UpdateMapView: Scale=({MapScale.ScaleX:F4}, {MapScale.ScaleY:F4}), Translate=({MapTranslate.X:F1}, {MapTranslate.Y:F1}), ViewMode={_settings.ViewMode}");
    }

    private void CenterOnPlayer(ScreenPosition? position = null)
    {
        if (position == null && _trackerService?.LastPosition != null)
        {
            position = _trackerService.LastPosition;
        }

        if (position == null || _currentMapConfig == null) return;

        var viewWidth = MapContainer.ActualWidth;
        var viewHeight = MapContainer.ActualHeight;
        var zoom = _settings.ZoomLevel;

        MapTranslate.X = (viewWidth / 2) - (position.X * zoom);
        MapTranslate.Y = (viewHeight / 2) - (position.Y * zoom);

        if (_settings.ViewMode == MiniMapViewMode.Fixed)
        {
            _settings.MapOffsetX = MapTranslate.X;
            _settings.MapOffsetY = MapTranslate.Y;
        }
    }

    #endregion

    #region Click-Through Mode

    private void EnableClickThrough()
    {
        if (_hwnd == IntPtr.Zero) return;

        var extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);

        _isClickThrough = true;
        _settings.ClickThrough = true;

        _log.Debug("Click-through mode enabled");
    }

    private void DisableClickThrough()
    {
        if (_hwnd == IntPtr.Zero) return;

        var extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);

        _isClickThrough = false;
        _settings.ClickThrough = false;

        _log.Debug("Click-through mode disabled");
    }

    public void ToggleClickThrough()
    {
        if (_isClickThrough)
            DisableClickThrough();
        else
            EnableClickThrough();
    }

    #endregion

    #region UI Event Handlers

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough) return;

        if (e.ClickCount == 2)
        {
            // 더블클릭: 기본 위치로 이동
            PositionToTopRight();
        }
        else
        {
            // 드래그 시작
            DragMove();
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClickThrough) return;

        // 휠 클릭 (중간 버튼)으로 맵 팬 시작
        if (e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(MapContainer);
            _panStartOffsetX = _settings.MapOffsetX;
            _panStartOffsetY = _settings.MapOffsetY;
            Mouse.Capture(MapContainer);
            Cursor = Cursors.ScrollAll;
            e.Handled = true;
            _log.Info($"Pan START: point=({_panStartPoint.X:F0}, {_panStartPoint.Y:F0}), offset=({_panStartOffsetX:F0}, {_panStartOffsetY:F0})");
        }
    }

    private void Window_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPanning)
        {
            _isPanning = false;
            Mouse.Capture(null);
            Cursor = Cursors.Arrow;
            e.Handled = true;
            _log.Debug("Pan ended");
        }
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var currentPoint = e.GetPosition(MapContainer);
            var deltaX = currentPoint.X - _panStartPoint.X;
            var deltaY = currentPoint.Y - _panStartPoint.Y;

            _settings.MapOffsetX = _panStartOffsetX + deltaX;
            _settings.MapOffsetY = _panStartOffsetY + deltaY;

            // 디버깅: 실제 오프셋 값 로깅 (너무 많으면 주석 처리)
            // _log.Debug($"Pan MOVE: delta=({deltaX:F0}, {deltaY:F0}), newOffset=({_settings.MapOffsetX:F0}, {_settings.MapOffsetY:F0})");

            UpdateMapView();
            e.Handled = true;
        }
    }

    #endregion

    #region Public Methods

    public void ZoomIn()
    {
        _settings.ZoomIn();
        UpdateMapView();
    }

    public void ZoomOut()
    {
        _settings.ZoomOut();
        UpdateMapView();
    }

    public void ToggleViewMode()
    {
        _settings.ToggleViewMode();

        if (_settings.ViewMode == MiniMapViewMode.PlayerTracking)
        {
            CenterOnPlayer();
        }
    }

    public void RefreshMap()
    {
        if (!string.IsNullOrEmpty(_currentMapKey))
        {
            LoadMap(_currentMapKey);
        }
    }

    #endregion

    #region Cleanup

    protected override void OnClosed(EventArgs e)
    {
        if (_trackerService != null)
        {
            _trackerService.PositionUpdated -= OnPositionUpdated;
            _trackerService.MapChanged -= OnMapChanged;
        }

        base.OnClosed(e);
    }

    #endregion
}
