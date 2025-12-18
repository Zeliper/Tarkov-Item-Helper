using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TarkovHelper.Pages.Map.Components;

/// <summary>
/// 맵 페이지의 줌/팬 기능을 담당하는 클래스.
/// 줌 레벨 조정, 마우스 드래그, 마우스 휠 줌 등의 로직을 캡슐화합니다.
/// </summary>
public class MapZoomPanController
{
    // 상수
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;
    private static readonly double[] ZoomPresets = { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0 };

    // UI 요소 참조
    private readonly Grid _mapViewerGrid;
    private readonly ScaleTransform _mapScale;
    private readonly TranslateTransform _mapTranslate;
    private readonly Canvas _mapCanvas;
    private readonly ComboBox _zoomComboBox;

    // 상태 필드
    private double _zoomLevel = 1.0;
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;

    // 이벤트
    /// <summary>
    /// 줌 레벨이 변경될 때 발생하는 이벤트 (마커 스케일 업데이트용).
    /// </summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>
    /// 현재 줌 레벨을 반환합니다.
    /// </summary>
    public double CurrentZoom => _zoomLevel;

    public MapZoomPanController(
        Grid mapViewerGrid,
        ScaleTransform mapScale,
        TranslateTransform mapTranslate,
        Canvas mapCanvas,
        ComboBox zoomComboBox)
    {
        _mapViewerGrid = mapViewerGrid ?? throw new ArgumentNullException(nameof(mapViewerGrid));
        _mapScale = mapScale ?? throw new ArgumentNullException(nameof(mapScale));
        _mapTranslate = mapTranslate ?? throw new ArgumentNullException(nameof(mapTranslate));
        _mapCanvas = mapCanvas ?? throw new ArgumentNullException(nameof(mapCanvas));
        _zoomComboBox = zoomComboBox ?? throw new ArgumentNullException(nameof(zoomComboBox));
    }

    #region Public Methods - Initialization

    /// <summary>
    /// 줌 콤보박스를 초기화합니다.
    /// </summary>
    public void InitializeZoomComboBox()
    {
        _zoomComboBox.Items.Clear();
        foreach (var preset in ZoomPresets)
        {
            _zoomComboBox.Items.Add($"{preset * 100:F0}%");
        }
        _zoomComboBox.Text = "100%";
    }

    /// <summary>
    /// 이벤트 핸들러를 UI 요소에 연결합니다.
    /// </summary>
    public void AttachEventHandlers()
    {
        _zoomComboBox.SelectionChanged += CmbZoomLevel_SelectionChanged;
        _zoomComboBox.KeyDown += CmbZoomLevel_KeyDown;
        _mapViewerGrid.MouseLeftButtonDown += MapViewer_MouseLeftButtonDown;
        _mapViewerGrid.MouseLeftButtonUp += MapViewer_MouseLeftButtonUp;
        _mapViewerGrid.MouseMove += MapViewer_MouseMove;
        _mapViewerGrid.MouseWheel += MapViewer_MouseWheel;
    }

    /// <summary>
    /// 이벤트 핸들러를 UI 요소에서 분리합니다.
    /// </summary>
    public void DetachEventHandlers()
    {
        _zoomComboBox.SelectionChanged -= CmbZoomLevel_SelectionChanged;
        _zoomComboBox.KeyDown -= CmbZoomLevel_KeyDown;
        _mapViewerGrid.MouseLeftButtonDown -= MapViewer_MouseLeftButtonDown;
        _mapViewerGrid.MouseLeftButtonUp -= MapViewer_MouseLeftButtonUp;
        _mapViewerGrid.MouseMove -= MapViewer_MouseMove;
        _mapViewerGrid.MouseWheel -= MapViewer_MouseWheel;
    }

    #endregion

    #region Public Methods - Zoom Control

    /// <summary>
    /// 줌 인 (다음 프리셋으로 이동).
    /// </summary>
    public void ZoomIn()
    {
        var nextPreset = ZoomPresets.FirstOrDefault(p => p > _zoomLevel);
        var newZoom = nextPreset > 0 ? nextPreset : _zoomLevel * 1.25;
        ZoomToMouse(newZoom);
    }

    /// <summary>
    /// 줌 아웃 (이전 프리셋으로 이동).
    /// </summary>
    public void ZoomOut()
    {
        var prevPreset = ZoomPresets.LastOrDefault(p => p < _zoomLevel);
        var newZoom = prevPreset > 0 ? prevPreset : _zoomLevel * 0.8;
        ZoomToMouse(newZoom);
    }

    /// <summary>
    /// 줌을 100%로 초기화하고 맵을 중앙에 배치합니다.
    /// </summary>
    public void ResetView()
    {
        SetZoom(1.0);
        CenterMapInView();
    }

    /// <summary>
    /// 맵을 뷰어 중앙에 배치합니다.
    /// </summary>
    public void CenterMapInView()
    {
        // 뷰어 영역의 크기 가져오기
        var viewerWidth = _mapViewerGrid.ActualWidth;
        var viewerHeight = _mapViewerGrid.ActualHeight;

        // 맵 크기 가져오기
        var mapWidth = _mapCanvas.Width;
        var mapHeight = _mapCanvas.Height;

        // 뷰어가 아직 렌더링되지 않은 경우 Dispatcher로 지연 호출
        if (viewerWidth <= 0 || viewerHeight <= 0)
        {
            _mapViewerGrid.Dispatcher.BeginInvoke(new Action(CenterMapInView),
                System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        // 줌 레벨을 고려하여 중앙 위치 계산
        var scaledMapWidth = mapWidth * _zoomLevel;
        var scaledMapHeight = mapHeight * _zoomLevel;

        // 맵을 뷰어 중앙에 배치하기 위한 이동량 계산
        var translateX = (viewerWidth - scaledMapWidth) / 2;
        var translateY = (viewerHeight - scaledMapHeight) / 2;

        _mapTranslate.X = translateX;
        _mapTranslate.Y = translateY;
    }

    #endregion

    #region Private Methods - Zoom

    /// <summary>
    /// 마우스 위치를 중심으로 줌합니다.
    /// </summary>
    private void ZoomToMouse(double newZoom)
    {
        var oldZoom = _zoomLevel;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        // 마우스 위치 가져오기 (MapViewerGrid 기준)
        var mousePos = Mouse.GetPosition(_mapViewerGrid);

        // 마우스 위치에서 캔버스상의 실제 좌표 계산
        var canvasX = (mousePos.X - _mapTranslate.X) / oldZoom;
        var canvasY = (mousePos.Y - _mapTranslate.Y) / oldZoom;

        // 줌 후에도 마우스 위치가 동일한 캔버스 좌표를 가리키도록 translate 조정
        _mapTranslate.X = mousePos.X - canvasX * newZoom;
        _mapTranslate.Y = mousePos.Y - canvasY * newZoom;

        SetZoom(newZoom);
    }

    /// <summary>
    /// 줌 레벨을 설정하고 UI를 업데이트합니다.
    /// </summary>
    private void SetZoom(double zoom)
    {
        // 줌 범위 제한
        _zoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);
        _mapScale.ScaleX = _zoomLevel;
        _mapScale.ScaleY = _zoomLevel;

        // 콤보박스 텍스트 업데이트 (이벤트 트리거 방지)
        _zoomComboBox.SelectionChanged -= CmbZoomLevel_SelectionChanged;
        _zoomComboBox.Text = $"{_zoomLevel * 100:F0}%";
        _zoomComboBox.SelectionChanged += CmbZoomLevel_SelectionChanged;

        // 줌 변경 이벤트 발생 (마커 스케일 업데이트용)
        ZoomChanged?.Invoke(this, _zoomLevel);
    }

    /// <summary>
    /// 줌 텍스트를 파싱하여 줌 레벨을 설정합니다.
    /// </summary>
    private void ParseAndSetZoom(string zoomText)
    {
        // "100%" 형식에서 숫자 추출
        var text = zoomText.Trim().TrimEnd('%');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            SetZoom(percent / 100.0);
        }
    }

    #endregion

    #region Event Handlers - Zoom ComboBox

    private void CmbZoomLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_zoomComboBox.SelectedItem is string selected)
        {
            ParseAndSetZoom(selected);
        }
    }

    private void CmbZoomLevel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ParseAndSetZoom(_zoomComboBox.Text);
            e.Handled = true;
        }
    }

    #endregion

    #region Event Handlers - Mouse Drag

    private void MapViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStartPoint = e.GetPosition(_mapViewerGrid);
        _dragStartTranslateX = _mapTranslate.X;
        _dragStartTranslateY = _mapTranslate.Y;
        _mapViewerGrid.CaptureMouse();
        _mapCanvas.Cursor = Cursors.ScrollAll;
    }

    private void MapViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            _mapViewerGrid.ReleaseMouseCapture();
            _mapCanvas.Cursor = Cursors.Arrow;
        }
    }

    private void MapViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var currentPoint = e.GetPosition(_mapViewerGrid);
        var deltaX = currentPoint.X - _dragStartPoint.X;
        var deltaY = currentPoint.Y - _dragStartPoint.Y;

        _mapTranslate.X = _dragStartTranslateX + deltaX;
        _mapTranslate.Y = _dragStartTranslateY + deltaY;
    }

    #endregion

    #region Event Handlers - Mouse Wheel

    private void MapViewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 마우스 위치를 중심으로 줌 (MapViewerGrid 기준)
        var mousePos = e.GetPosition(_mapViewerGrid);
        var oldZoom = _zoomLevel;

        // 줌 계산
        var zoomFactor = e.Delta > 0 ? 1.15 : 0.87;
        var newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        // 마우스 위치에서 캔버스상의 실제 좌표 계산
        // mousePos = canvasPos * oldZoom + translate
        // canvasPos = (mousePos - translate) / oldZoom
        var canvasX = (mousePos.X - _mapTranslate.X) / oldZoom;
        var canvasY = (mousePos.Y - _mapTranslate.Y) / oldZoom;

        // 줌 후에도 마우스 위치가 동일한 캔버스 좌표를 가리키도록 translate 조정
        // mousePos = canvasPos * newZoom + newTranslate
        // newTranslate = mousePos - canvasPos * newZoom
        _mapTranslate.X = mousePos.X - canvasX * newZoom;
        _mapTranslate.Y = mousePos.Y - canvasY * newZoom;

        SetZoom(newZoom);
        e.Handled = true;
    }

    #endregion
}
