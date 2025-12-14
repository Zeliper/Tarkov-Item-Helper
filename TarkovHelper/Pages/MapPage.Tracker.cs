using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovHelper.Models.MapTracker;
using TarkovHelper.Services.MapTracker;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - Map Tracker partial class
/// </summary>
public partial class MapPage : UserControl
{
    #region Map Tracker

    private void MapPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Disconnect events to prevent memory leaks
        _trackerService.PositionUpdated -= OnPositionUpdated;
        _trackerService.WatchingStateChanged -= OnWatchingStateChanged;
        _trackerService.StatusMessage -= OnTrackerStatusMessage;
        _logMapWatcher.MapChanged -= OnLogMapChanged;
        _loc.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>
    /// 스크린샷 폴더 선택
    /// </summary>
    private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "스크린샷 폴더 선택",
            InitialDirectory = _trackerService.Settings.ScreenshotFolderPath
        };

        if (dialog.ShowDialog() == true)
        {
            _trackerService.Settings.ScreenshotFolderPath = dialog.FolderName;
            _trackerService.SaveSettings();
            StatusText.Text = $"폴더 설정: {dialog.FolderName}";
        }
    }

    /// <summary>
    /// 트래킹 시작/중지 토글
    /// </summary>
    private void BtnToggleTracking_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[MapPage] BtnToggleTracking_Click: IsWatching={_trackerService.IsWatching}");

        if (_trackerService.IsWatching)
        {
            _trackerService.StopTracking();
            System.Diagnostics.Debug.WriteLine("[MapPage] Tracking stopped");
        }
        else
        {
            if (string.IsNullOrEmpty(_trackerService.Settings.ScreenshotFolderPath))
            {
                StatusText.Text = "먼저 스크린샷 폴더를 선택하세요";
                System.Diagnostics.Debug.WriteLine("[MapPage] ERROR: No screenshot folder configured");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[MapPage] Screenshot folder: {_trackerService.Settings.ScreenshotFolderPath}");

            // Set current map for coordinate transformation
            if (_currentMapConfig != null)
            {
                _trackerService.SetCurrentMap(_currentMapConfig.Key);
                System.Diagnostics.Debug.WriteLine($"[MapPage] SetCurrentMap: {_currentMapConfig.Key}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[MapPage] WARNING: No map selected when starting tracking");
            }

            _trackerService.StartTracking();
            System.Diagnostics.Debug.WriteLine($"[MapPage] StartTracking called, IsWatching={_trackerService.IsWatching}");

            // Also start log map watcher for auto map switching
            if (!_logMapWatcher.IsWatching)
            {
                _logMapWatcher.StartWatching();
            }
        }
    }

    /// <summary>
    /// 경로 초기화
    /// </summary>
    private void BtnClearTrail_Click(object sender, RoutedEventArgs e)
    {
        _trackerService.ClearTrail();
        ClearTrailMarkers();
        ClearPlayerMarker();
        StatusText.Text = "경로 초기화됨";
    }

    /// <summary>
    /// 트래커 위치 업데이트 이벤트 핸들러
    /// TarkovDBEditor의 DrawPlayerMarker 방식을 적용하여 playerMarkerTransform을 사용합니다.
    /// </summary>
    private void OnPositionUpdated(object? sender, ScreenPosition position)
    {
        System.Diagnostics.Debug.WriteLine($"[MapPage] OnPositionUpdated: ScreenX={position.X:F1}, ScreenY={position.Y:F1}");

        Dispatcher.Invoke(() =>
        {
            if (_currentMapConfig == null)
            {
                System.Diagnostics.Debug.WriteLine("[MapPage] WARNING: _currentMapConfig is null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[MapPage] Current map: {_currentMapConfig.Key}");

            // Use playerMarkerTransform for player marker position (TarkovDBEditor 방식)
            // This is separate from quest marker coordinate system
            double svgX = position.X;
            double svgY = position.Y;

            if (position.OriginalPosition != null)
            {
                var gameX = position.OriginalPosition.X;
                var gameZ = position.OriginalPosition.Z ?? 0;
                System.Diagnostics.Debug.WriteLine($"[MapPage] Game coords: X={gameX:F2}, Z={gameZ:F2}");

                // Map2와 동일하게 CalibratedTransform만 사용 (정확한 위치 표시)
                // CalibratedTransform [a, b, c, d, tx, ty]
                // screenX = a * gameX + b * gameZ + tx
                // screenY = c * gameX + d * gameZ + ty
                var transform = _currentMapConfig.CalibratedTransform;

                if (transform != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MapPage] CalibratedTransform: [{string.Join(", ", transform)}]");
                }

                if (transform != null && transform.Length >= 6)
                {
                    var a = transform[0];
                    var b = transform[1];
                    var c = transform[2];
                    var d = transform[3];
                    var tx = transform[4];
                    var ty = transform[5];

                    svgX = a * gameX + b * gameZ + tx;
                    svgY = c * gameX + d * gameZ + ty;
                    System.Diagnostics.Debug.WriteLine($"[MapPage] Transformed: ScreenX={svgX:F1}, ScreenY={svgY:F1}");
                }
                else
                {
                    // Fallback: GameToScreen 사용
                    var screenCoords = _currentMapConfig.GameToScreen(gameX, gameZ);
                    if (screenCoords.HasValue)
                    {
                        svgX = screenCoords.Value.screenX;
                        svgY = screenCoords.Value.screenY;
                        System.Diagnostics.Debug.WriteLine($"[MapPage] GameToScreen fallback: ScreenX={svgX:F1}, ScreenY={svgY:F1}");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"[MapPage] MapSize: {_currentMapConfig.ImageWidth}x{_currentMapConfig.ImageHeight}");

            // Add trail marker for previous position
            if (_playerMarkerCircle != null && _showTrail)
            {
                var prevX = Canvas.GetLeft(_playerMarkerCircle) + 7; // Center of circle (14/2)
                var prevY = Canvas.GetTop(_playerMarkerCircle) + 7;
                AddTrailMarker(prevX, prevY);
            }

            // Update player marker with direction
            var angle = position.Angle ?? position.OriginalPosition?.Angle;
            UpdatePlayerMarker(svgX, svgY, angle);

            // Show original game coordinates in status
            if (position.OriginalPosition != null)
            {
                _currentGameX = position.OriginalPosition.X;
                _currentGameZ = position.OriginalPosition.Z ?? 0;
                _hasValidCoordinates = true;
                StatusText.Text = $"위치: X={position.OriginalPosition.X:F0}, Z={position.OriginalPosition.Z:F0}";
            }
            else
            {
                StatusText.Text = $"위치: Screen X={svgX:F0}, Y={svgY:F0}";
            }

            // Update minimap player position
            UpdateMinimapPlayerPosition();
        });
    }

    /// <summary>
    /// 트래킹 상태 변경 이벤트 핸들러
    /// </summary>
    private void OnWatchingStateChanged(object? sender, bool isWatching)
    {
        Dispatcher.Invoke(() =>
        {
            BtnToggleTracking.Content = isWatching ? "⏹" : "▶";
            var statusBrush = isWatching
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // Green
                : new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray

            TrackingStatusBorder.Background = statusBrush;
            if (TrackingStatusText != null)
                TrackingStatusText.Text = isWatching ? "추적 중" : "대기";

            // 상태바의 트래킹 인디케이터도 업데이트
            if (StatusTrackingIndicator != null)
                StatusTrackingIndicator.Background = statusBrush;
        });
    }

    /// <summary>
    /// 트래커 상태 메시지 이벤트 핸들러
    /// </summary>
    private void OnTrackerStatusMessage(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = message;
        });
    }

    /// <summary>
    /// 로그 맵 변경 이벤트 핸들러 (자동 맵 전환)
    /// </summary>
    private void OnLogMapChanged(object? sender, MapChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Find matching map in selector
            var mapKey = e.NewMapKey.ToLowerInvariant().Replace(" ", "-");

            foreach (var item in MapSelector.Items)
            {
                if (item is DbMapConfig config)
                {
                    if (config.Key.Equals(mapKey, StringComparison.OrdinalIgnoreCase) ||
                        config.DisplayName.Equals(e.NewMapKey, StringComparison.OrdinalIgnoreCase) ||
                        (config.Aliases?.Any(a => a.Equals(e.NewMapKey, StringComparison.OrdinalIgnoreCase)) == true))
                    {
                        MapSelector.SelectedItem = config;
                        _trackerService.SetCurrentMap(config.Key);
                        StatusText.Text = $"맵 자동 전환: {config.DisplayName}";
                        break;
                    }
                }
            }
        });
    }

    /// <summary>
    /// 플레이어 마커 업데이트 (원형 + 방향 삼각형)
    /// </summary>
    private void UpdatePlayerMarker(double svgX, double svgY, double? angle = null)
    {
        if (!_showPlayerMarker) return;

        const double circleSize = 14;
        const double arrowLength = 20;
        const double arrowWidth = 12;

        // Create circle marker if not exists
        if (_playerMarkerCircle == null)
        {
            _playerMarkerCircle = new Ellipse
            {
                Width = circleSize,
                Height = circleSize,
                Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243)), // Blue
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            _playerMarkerCircle.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 4,
                ShadowDepth = 2,
                Opacity = 0.5
            };
            PlayerMarkerCanvas.Children.Add(_playerMarkerCircle);
        }

        // Create arrow marker if not exists
        if (_playerMarkerArrow == null)
        {
            _playerMarkerArrow = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromRgb(255, 193, 7)), // Yellow/Gold
                Stroke = Brushes.White,
                StrokeThickness = 1
            };
            _playerMarkerArrow.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 2,
                ShadowDepth = 1,
                Opacity = 0.5
            };
            PlayerMarkerCanvas.Children.Add(_playerMarkerArrow);
        }

        // Position circle at center
        Canvas.SetLeft(_playerMarkerCircle, svgX - circleSize / 2);
        Canvas.SetTop(_playerMarkerCircle, svgY - circleSize / 2);

        // Update arrow position and rotation
        if (angle.HasValue)
        {
            _playerMarkerArrow.Visibility = Visibility.Visible;

            // Calculate arrow tip position based on angle
            // EFT: 0° = North (up), clockwise
            var angleRad = (angle.Value - 90) * Math.PI / 180.0; // Convert to standard math angle

            var tipX = svgX + Math.Cos(angleRad) * (circleSize / 2 + arrowLength);
            var tipY = svgY + Math.Sin(angleRad) * (circleSize / 2 + arrowLength);

            // Arrow base points (perpendicular to direction)
            var baseAngle = angleRad + Math.PI; // Opposite direction
            var perpAngle1 = baseAngle + Math.PI / 2;
            var perpAngle2 = baseAngle - Math.PI / 2;

            var baseX = svgX + Math.Cos(angleRad) * (circleSize / 2);
            var baseY = svgY + Math.Sin(angleRad) * (circleSize / 2);

            var base1X = baseX + Math.Cos(perpAngle1) * (arrowWidth / 2);
            var base1Y = baseY + Math.Sin(perpAngle1) * (arrowWidth / 2);

            var base2X = baseX + Math.Cos(perpAngle2) * (arrowWidth / 2);
            var base2Y = baseY + Math.Sin(perpAngle2) * (arrowWidth / 2);

            _playerMarkerArrow.Points = new PointCollection
            {
                new Point(tipX, tipY),      // Arrow tip
                new Point(base1X, base1Y),  // Base left
                new Point(base2X, base2Y)   // Base right
            };
        }
        else
        {
            _playerMarkerArrow.Visibility = Visibility.Collapsed;
        }

        System.Diagnostics.Debug.WriteLine($"[MapPage] Player marker placed at: ({svgX:F1}, {svgY:F1}), Angle: {angle?.ToString("F1") ?? "null"}");
    }

    /// <summary>
    /// 트레일 마커 추가
    /// </summary>
    private void AddTrailMarker(double svgX, double svgY)
    {
        var trailDot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = new SolidColorBrush(Color.FromArgb(180, 33, 150, 243)), // Semi-transparent blue
            Opacity = Math.Max(0.3, 1.0 - (_trailMarkers.Count * 0.02)) // Fade out older markers
        };

        Canvas.SetLeft(trailDot, svgX - 3); // Center the 6px dot at the position
        Canvas.SetTop(trailDot, svgY - 3);
        TrailCanvas.Children.Add(trailDot);
        _trailMarkers.Add(trailDot);

        // Limit trail length
        if (_trailMarkers.Count > 100)
        {
            var oldest = _trailMarkers[0];
            TrailCanvas.Children.Remove(oldest);
            _trailMarkers.RemoveAt(0);
        }
    }

    /// <summary>
    /// 트레일 마커 모두 제거
    /// </summary>
    private void ClearTrailMarkers()
    {
        TrailCanvas.Children.Clear();
        _trailMarkers.Clear();
    }

    /// <summary>
    /// 플레이어 마커 제거
    /// </summary>
    private void ClearPlayerMarker()
    {
        if (_playerMarkerCircle != null)
        {
            PlayerMarkerCanvas.Children.Remove(_playerMarkerCircle);
            _playerMarkerCircle = null;
        }
        if (_playerMarkerArrow != null)
        {
            PlayerMarkerCanvas.Children.Remove(_playerMarkerArrow);
            _playerMarkerArrow = null;
        }
    }

    /// <summary>
    /// EFT 게임 좌표를 SVG 좌표로 변환
    /// </summary>
    private Point? TransformEftToSvg(double eftX, double eftZ)
    {
        if (_currentMapConfig == null) return null;

        // Use MapTrackerService's TestCoordinate for transformation
        var screenPos = _trackerService.TestCoordinate(_currentMapConfig.Key, eftX, eftZ);
        if (screenPos != null)
        {
            return new Point(screenPos.X, screenPos.Y);
        }

        // Fallback: use DbMapConfig's GameToScreen method
        var result = _currentMapConfig.GameToScreen(eftX, eftZ);
        if (result != null)
        {
            return new Point(result.Value.screenX, result.Value.screenY);
        }

        return null;
    }

    #endregion
}
