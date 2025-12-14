using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - Navigation (Zoom/Pan/Mouse) partial class
/// </summary>
public partial class MapPage : UserControl
{
    #region Zoom and Pan

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        var nextPreset = ZoomPresets.FirstOrDefault(p => p > _zoomLevel);
        var newZoom = nextPreset > 0 ? nextPreset : _zoomLevel * 1.25;
        ZoomToCenterAnimated(newZoom);
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        var prevPreset = ZoomPresets.LastOrDefault(p => p < _zoomLevel);
        var newZoom = prevPreset > 0 ? prevPreset : _zoomLevel * 0.8;
        ZoomToCenterAnimated(newZoom);
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        ResetViewAnimated();
    }

    /// <summary>
    /// 중앙 기준 줌 (애니메이션)
    /// </summary>
    private void ZoomToCenterAnimated(double newZoom)
    {
        var viewerCenterX = MapViewerGrid.ActualWidth / 2;
        var viewerCenterY = MapViewerGrid.ActualHeight / 2;
        ZoomToPointAnimated(newZoom, new Point(viewerCenterX, viewerCenterY));
    }

    /// <summary>
    /// 특정 지점 기준 줌 (애니메이션)
    /// </summary>
    private void ZoomToPointAnimated(double newZoom, Point viewerPoint)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        // 현재 값 캡처
        var currentTranslateX = MapTranslate.X;
        var currentTranslateY = MapTranslate.Y;
        var currentZoom = MapScale.ScaleX;

        // 진행 중인 애니메이션 중지
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        MapScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MapScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        // 값 복원
        MapTranslate.X = currentTranslateX;
        MapTranslate.Y = currentTranslateY;
        MapScale.ScaleX = currentZoom;
        MapScale.ScaleY = currentZoom;

        if (Math.Abs(newZoom - currentZoom) < 0.001) return;

        // 목표 위치 계산 (마우스 포인트 기준 줌)
        var canvasX = (viewerPoint.X - currentTranslateX) / currentZoom;
        var canvasY = (viewerPoint.Y - currentTranslateY) / currentZoom;
        var targetTranslateX = viewerPoint.X - canvasX * newZoom;
        var targetTranslateY = viewerPoint.Y - canvasY * newZoom;

        // 부드러운 애니메이션 적용
        AnimateMapTo(newZoom, targetTranslateX, targetTranslateY);
    }

    /// <summary>
    /// 뷰 리셋 (애니메이션)
    /// </summary>
    private void ResetViewAnimated()
    {
        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        if (viewerWidth <= 0 || viewerHeight <= 0) return;

        var targetZoom = 1.0;
        var scaledMapWidth = MapCanvas.Width * targetZoom;
        var scaledMapHeight = MapCanvas.Height * targetZoom;
        var targetTranslateX = (viewerWidth - scaledMapWidth) / 2;
        var targetTranslateY = (viewerHeight - scaledMapHeight) / 2;

        AnimateMapTo(targetZoom, targetTranslateX, targetTranslateY);
    }

    private void ZoomToCenter(double newZoom)
    {
        var mousePos = new Point(MapViewerGrid.ActualWidth / 2, MapViewerGrid.ActualHeight / 2);
        ZoomToPoint(newZoom, mousePos);
    }

    private void ZoomToPoint(double newZoom, Point viewerPoint)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        // 현재 애니메이션 값 캡처 (애니메이션 중이면 현재 값, 아니면 기본값)
        var currentTranslateX = MapTranslate.X;
        var currentTranslateY = MapTranslate.Y;
        var currentZoom = MapScale.ScaleX;

        // 애니메이션 해제
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        MapScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MapScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        // 캡처한 값으로 복원
        MapTranslate.X = currentTranslateX;
        MapTranslate.Y = currentTranslateY;
        MapScale.ScaleX = currentZoom;
        MapScale.ScaleY = currentZoom;
        _zoomLevel = currentZoom;

        if (Math.Abs(newZoom - currentZoom) < 0.001) return;

        var canvasX = (viewerPoint.X - currentTranslateX) / currentZoom;
        var canvasY = (viewerPoint.Y - currentTranslateY) / currentZoom;

        MapTranslate.X = viewerPoint.X - canvasX * newZoom;
        MapTranslate.Y = viewerPoint.Y - canvasY * newZoom;

        SetZoom(newZoom);
    }

    private void SetZoom(double zoom)
    {
        _zoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);

        // 현재 값 캡처 (애니메이션 중일 수 있음)
        var currentTranslateX = MapTranslate.X;
        var currentTranslateY = MapTranslate.Y;

        // 애니메이션 해제
        MapScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MapScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        // 값 복원/설정
        MapTranslate.X = currentTranslateX;
        MapTranslate.Y = currentTranslateY;
        MapScale.ScaleX = _zoomLevel;
        MapScale.ScaleY = _zoomLevel;

        var zoomPercent = $"{_zoomLevel * 100:F0}%";
        ZoomText.Text = zoomPercent;

        RedrawAll();
        UpdateMinimapViewport();
    }

    private void CenterMapInView()
    {
        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        if (viewerWidth <= 0 || viewerHeight <= 0)
        {
            Dispatcher.BeginInvoke(new Action(CenterMapInView), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        var scaledMapWidth = MapCanvas.Width * _zoomLevel;
        var scaledMapHeight = MapCanvas.Height * _zoomLevel;

        MapTranslate.X = (viewerWidth - scaledMapWidth) / 2;
        MapTranslate.Y = (viewerHeight - scaledMapHeight) / 2;
    }

    #endregion

    #region Mouse Events

    private void MapViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 현재 애니메이션 값 캡처
        var currentTranslateX = MapTranslate.X;
        var currentTranslateY = MapTranslate.Y;
        var currentZoom = MapScale.ScaleX;

        // 애니메이션 해제
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        MapScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        MapScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        // 캡처한 값으로 복원
        MapTranslate.X = currentTranslateX;
        MapTranslate.Y = currentTranslateY;
        MapScale.ScaleX = currentZoom;
        MapScale.ScaleY = currentZoom;
        _zoomLevel = currentZoom;

        _isDragging = true;
        _dragStartPoint = e.GetPosition(MapViewerGrid);
        _dragStartTranslateX = currentTranslateX;
        _dragStartTranslateY = currentTranslateY;
        MapViewerGrid.CaptureMouse();
    }

    private void MapViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        MapViewerGrid.ReleaseMouseCapture();
        MapCanvas.Cursor = Cursors.Arrow;
    }

    private void MapViewer_MouseMove(object sender, MouseEventArgs e)
    {
        // Update coordinate display
        if (_currentMapConfig != null)
        {
            var canvasPos = e.GetPosition(MapCanvas);

            // Reverse transform: screen -> game coordinates
            if (_currentMapConfig.CalibratedTransform != null && _currentMapConfig.CalibratedTransform.Length >= 6)
            {
                var transform = _currentMapConfig.CalibratedTransform;
                var a = transform[0];
                var b = transform[1];
                var c = transform[2];
                var d = transform[3];
                var tx = transform[4];
                var ty = transform[5];

                // Inverse matrix calculation
                var det = a * d - b * c;
                if (Math.Abs(det) > 0.0001)
                {
                    var screenX = canvasPos.X;
                    var screenY = canvasPos.Y;

                    var gameX = (d * (screenX - tx) - b * (screenY - ty)) / det;
                    var gameZ = (-c * (screenX - tx) + a * (screenY - ty)) / det;

                    _currentGameX = gameX;
                    _currentGameZ = gameZ;
                    _hasValidCoordinates = true;

                    GameCoordsText.Text = $"X: {gameX:F1}, Z: {gameZ:F1}";
                }
            }
        }

        if (!_isDragging) return;

        var currentPt = e.GetPosition(MapViewerGrid);
        var deltaX = currentPt.X - _dragStartPoint.X;
        var deltaY = currentPt.Y - _dragStartPoint.Y;

        MapTranslate.X = _dragStartTranslateX + deltaX;
        MapTranslate.Y = _dragStartTranslateY + deltaY;
        MapCanvas.Cursor = Cursors.ScrollAll;

        // Update minimap viewport during drag
        UpdateMinimapViewport();
    }

    private void MapViewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(MapViewerGrid);
        var zoomFactor = e.Delta > 0 ? 1.15 : 0.87;
        var newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);

        ZoomToPoint(newZoom, mousePos);
        e.Handled = true;
    }

    #endregion
}
