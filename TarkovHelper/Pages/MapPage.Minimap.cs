using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - Minimap partial class
/// </summary>
public partial class MapPage : UserControl
{
    #region Minimap

    /// <summary>
    /// 미니맵 헤더 클릭 (접기/펼치기)
    /// </summary>
    private void MinimapHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _isMinimapExpanded = !_isMinimapExpanded;

        if (MinimapContent != null)
            MinimapContent.Visibility = _isMinimapExpanded ? Visibility.Visible : Visibility.Collapsed;

        if (MinimapToggleIcon != null)
            MinimapToggleIcon.Text = _isMinimapExpanded ? "▼" : "▲";
    }

    /// <summary>
    /// 미니맵 클릭 시 해당 위치로 맵 이동
    /// </summary>
    private void Minimap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (MinimapContent == null || _mapSize.IsEmpty) return;

        _isMinimapDragging = true;
        MinimapContent.CaptureMouse();
        NavigateToMinimapPosition(e.GetPosition(MinimapContent));
    }

    /// <summary>
    /// 미니맵 드래그 중 이동
    /// </summary>
    private void Minimap_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMinimapDragging || MinimapContent == null) return;

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            NavigateToMinimapPosition(e.GetPosition(MinimapContent));
        }
        else
        {
            _isMinimapDragging = false;
            MinimapContent.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// 미니맵 위치로 맵 이동
    /// </summary>
    private void NavigateToMinimapPosition(Point minimapPos)
    {
        if (_mapSize.IsEmpty || MinimapContent == null) return;

        var minimapWidth = MinimapContent.ActualWidth;
        var minimapHeight = MinimapContent.ActualHeight;

        if (minimapWidth <= 0 || minimapHeight <= 0) return;

        // Convert minimap click to map coordinates (no offset needed)
        var relativeX = minimapPos.X / minimapWidth;
        var relativeY = minimapPos.Y / minimapHeight;

        // Clamp to valid range
        relativeX = Math.Clamp(relativeX, 0, 1);
        relativeY = Math.Clamp(relativeY, 0, 1);

        // Convert to map coordinates
        var mapX = relativeX * _mapSize.Width;
        var mapY = relativeY * _mapSize.Height;

        // Get viewer size
        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        // Calculate translation to center on this point
        var newTranslateX = (viewerWidth / 2) - (mapX * _zoomLevel);
        var newTranslateY = (viewerHeight / 2) - (mapY * _zoomLevel);

        // Animate to the new position
        AnimateMapTo(_zoomLevel, newTranslateX, newTranslateY);
    }

    /// <summary>
    /// 미니맵 업데이트 (맵 로드 시)
    /// </summary>
    private void UpdateMinimap()
    {
        if (MapSvg == null || MinimapImage == null || MinimapContent == null) return;

        try
        {
            // Get the actual size of the SVG map
            MapSvg.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            MapSvg.Arrange(new Rect(MapSvg.DesiredSize));

            _mapSize = MapSvg.DesiredSize;

            if (_mapSize.Width <= 0 || _mapSize.Height <= 0)
            {
                _mapSize = new Size(1000, 1000); // Fallback
            }

            // 미니맵 크기 설정 (S/M/L)에 따른 기본 크기
            double baseSize = _settings.MapMinimapSize switch
            {
                "Small" => 140,
                "Large" => 240,
                _ => 180 // Medium
            };

            // 맵 비율에 맞게 동적 크기 계산
            var mapAspect = _mapSize.Width / _mapSize.Height;
            double minimapWidth, minimapHeight;

            if (mapAspect >= 1)
            {
                // 가로가 더 긴 맵
                minimapWidth = baseSize;
                minimapHeight = baseSize / mapAspect;
            }
            else
            {
                // 세로가 더 긴 맵
                minimapHeight = baseSize;
                minimapWidth = baseSize * mapAspect;
            }

            // 미니맵 크기 설정
            MinimapContent.Width = minimapWidth;
            MinimapContent.Height = minimapHeight;
            _minimapScale = minimapWidth / _mapSize.Width;

            // Render the SVG to a bitmap for the minimap
            var dpi = 96.0;
            var renderWidth = (int)minimapWidth;
            var renderHeight = (int)minimapHeight;

            if (renderWidth > 0 && renderHeight > 0)
            {
                var renderBitmap = new RenderTargetBitmap(
                    renderWidth, renderHeight, dpi, dpi, PixelFormats.Pbgra32);

                var drawingVisual = new DrawingVisual();
                using (var drawingContext = drawingVisual.RenderOpen())
                {
                    drawingContext.PushTransform(new ScaleTransform(_minimapScale, _minimapScale));
                    var brush = new VisualBrush(MapSvg) { Stretch = Stretch.None };
                    drawingContext.DrawRectangle(brush, null, new Rect(0, 0, _mapSize.Width, _mapSize.Height));
                }

                renderBitmap.Render(drawingVisual);
                renderBitmap.Freeze();

                _minimapBitmap = renderBitmap;
                MinimapImage.Source = _minimapBitmap;
            }

            // 미니맵 맵 이름 표시
            if (MinimapMapName != null && _currentMapConfig != null)
            {
                MinimapMapName.Text = _currentMapConfig.DisplayName;
            }

            UpdateMinimapMarkers();
            UpdateMinimapViewport();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] Error updating minimap: {ex.Message}");
        }
    }

    /// <summary>
    /// 미니맵에 마커 표시
    /// </summary>
    private void UpdateMinimapMarkers()
    {
        if (MinimapMarkerCanvas == null || _mapSize.IsEmpty || _currentMapConfig == null) return;

        MinimapMarkerCanvas.Children.Clear();

        var minimapWidth = MinimapContent?.ActualWidth ?? 180;
        var minimapHeight = MinimapContent?.ActualHeight ?? 140;

        if (minimapWidth <= 0 || minimapHeight <= 0) return;

        var scaleX = minimapWidth / _mapSize.Width;
        var scaleY = minimapHeight / _mapSize.Height;

        // 탈출구 마커 표시
        if (_settings.MapShowExtracts)
        {
            var extractMarkers = _dbService.GetExtractMarkersForMap(_currentMapConfig.Key);
            foreach (var marker in extractMarkers)
            {
                // 게임 좌표 -> SVG 좌표 변환
                var screenCoords = _currentMapConfig.GameToScreen(marker.X, marker.Z);
                if (screenCoords == null) continue;

                var (sx, sy) = screenCoords.Value;
                var x = sx * scaleX;
                var y = sy * scaleY;

                var dot = new Ellipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = marker.MarkerType switch
                    {
                        MapMarkerType.PmcExtraction => Brushes.LimeGreen,
                        MapMarkerType.ScavExtraction => Brushes.DeepSkyBlue,
                        MapMarkerType.SharedExtraction => Brushes.Gold,
                        _ => Brushes.Green
                    },
                    Opacity = 0.9
                };

                Canvas.SetLeft(dot, x - 2);
                Canvas.SetTop(dot, y - 2);
                MinimapMarkerCanvas.Children.Add(dot);
            }
        }

        // 퀘스트 마커 표시 (메인 맵과 동일한 데이터 소스 사용)
        if (_settings.MapShowQuests && _currentMapQuestObjectives.Count > 0)
        {
            foreach (var objective in _currentMapQuestObjectives)
            {
                // 숨긴 퀘스트 필터링
                if (_hiddenQuestIds.Contains(objective.TaskNormalizedName)) continue;

                // 완료된 퀘스트 필터링
                var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);
                if (isCompleted) continue;

                // 현재 맵의 위치만 가져오기
                var locationsForCurrentMap = objective.Locations
                    .Where(loc => IsLocationOnCurrentMap(loc))
                    .ToList();

                foreach (var loc in locationsForCurrentMap)
                {
                    // 게임 좌표 -> SVG 좌표 변환
                    if (loc.Z == null) continue;
                    var screenCoords = _currentMapConfig.GameToScreen(loc.X, loc.Z.Value);
                    if (screenCoords == null) continue;

                    var (sx, sy) = screenCoords.Value;
                    var x = sx * scaleX;
                    var y = sy * scaleY;

                    var dot = new Ellipse
                    {
                        Width = 3,
                        Height = 3,
                        Fill = new SolidColorBrush(Color.FromRgb(255, 193, 7)), // Yellow/Orange
                        Opacity = 0.8
                    };

                    Canvas.SetLeft(dot, x - 1.5);
                    Canvas.SetTop(dot, y - 1.5);
                    MinimapMarkerCanvas.Children.Add(dot);
                }
            }
        }
    }

    /// <summary>
    /// 미니맵 뷰포트 사각형 업데이트
    /// </summary>
    private void UpdateMinimapViewport()
    {
        if (MinimapViewport == null || MinimapContent == null || _mapSize.IsEmpty) return;

        try
        {
            var minimapWidth = MinimapContent.ActualWidth;
            var minimapHeight = MinimapContent.ActualHeight;

            if (minimapWidth <= 0 || minimapHeight <= 0) return;

            // Calculate viewport in map coordinates
            var viewerWidth = MapViewerGrid.ActualWidth;
            var viewerHeight = MapViewerGrid.ActualHeight;

            var translateX = MapTranslate.X;
            var translateY = MapTranslate.Y;

            // Visible area in map coordinates
            var visibleLeft = -translateX / _zoomLevel;
            var visibleTop = -translateY / _zoomLevel;
            var visibleWidth = viewerWidth / _zoomLevel;
            var visibleHeight = viewerHeight / _zoomLevel;

            // Convert to minimap coordinates (no offset needed - minimap fills entire content)
            var vpLeft = (visibleLeft / _mapSize.Width) * minimapWidth;
            var vpTop = (visibleTop / _mapSize.Height) * minimapHeight;
            var vpWidth = (visibleWidth / _mapSize.Width) * minimapWidth;
            var vpHeight = (visibleHeight / _mapSize.Height) * minimapHeight;

            // Clamp viewport to minimap bounds
            vpLeft = Math.Max(0, Math.Min(vpLeft, minimapWidth - vpWidth));
            vpTop = Math.Max(0, Math.Min(vpTop, minimapHeight - vpHeight));
            vpWidth = Math.Min(vpWidth, minimapWidth);
            vpHeight = Math.Min(vpHeight, minimapHeight);

            // Update viewport rectangle
            Canvas.SetLeft(MinimapViewport, vpLeft);
            Canvas.SetTop(MinimapViewport, vpTop);
            MinimapViewport.Width = Math.Max(8, vpWidth);
            MinimapViewport.Height = Math.Max(8, vpHeight);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Minimap] Error updating viewport: {ex.Message}");
        }
    }

    /// <summary>
    /// 미니맵에 플레이어 위치 업데이트
    /// </summary>
    private void UpdateMinimapPlayerPosition()
    {
        if (MinimapPlayerCanvas == null || !_hasValidCoordinates || _mapSize.IsEmpty) return;

        MinimapPlayerCanvas.Children.Clear();

        var svgPos = TransformEftToSvg(_currentGameX, _currentGameZ);
        if (svgPos == null) return;

        var minimapWidth = MinimapContent?.ActualWidth ?? 180;
        var minimapHeight = MinimapContent?.ActualHeight ?? 140;

        // Convert to minimap position (no offset needed)
        var minimapX = (svgPos.Value.X / _mapSize.Width) * minimapWidth;
        var minimapY = (svgPos.Value.Y / _mapSize.Height) * minimapHeight;

        // Draw player dot on minimap (larger and more visible)
        var playerDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brushes.Cyan,
            Stroke = Brushes.White,
            StrokeThickness = 1.5
        };

        Canvas.SetLeft(playerDot, minimapX - 4);
        Canvas.SetTop(playerDot, minimapY - 4);
        MinimapPlayerCanvas.Children.Add(playerDot);

        // Draw direction indicator (optional - if we have heading info)
        var direction = new Polygon
        {
            Points = new PointCollection { new Point(0, -6), new Point(3, 2), new Point(-3, 2) },
            Fill = Brushes.Cyan,
            Stroke = Brushes.White,
            StrokeThickness = 0.5,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        Canvas.SetLeft(direction, minimapX - 3);
        Canvas.SetTop(direction, minimapY - 3);
        // Note: Could add rotation based on player heading if available
    }

    #endregion
}
