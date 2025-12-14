using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - Draw Markers partial class
/// </summary>
public partial class MapPage : UserControl
{
    #region Draw Markers

    private BitmapImage? GetMarkerIcon(MapMarkerType markerType)
    {
        if (_iconCache.TryGetValue(markerType, out var cachedIcon))
        {
            return cachedIcon;
        }

        try
        {
            var iconFileName = MapMarker.GetIconFileName(markerType);
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var iconPath = System.IO.Path.Combine(appDir, "Assets", "DB", "Icons", iconFileName);

            if (File.Exists(iconPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 64;
                bitmap.EndInit();
                bitmap.Freeze();
                _iconCache[markerType] = bitmap;
                return bitmap;
            }
        }
        catch
        {
            // Ignore icon loading errors
        }

        _iconCache[markerType] = null;
        return null;
    }

    private void RedrawMarkers()
    {
        if (MarkersCanvas == null) return;
        MarkersCanvas.Children.Clear();

        if (_currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var markersForMap = _dbService.GetExtractMarkersForMap(_currentMapConfig.Key);

        foreach (var marker in markersForMap)
        {
            // Use GameToScreen from config
            var screenCoords = _currentMapConfig.GameToScreen(marker.X, marker.Z);
            if (screenCoords == null) continue;

            var (sx, sy) = screenCoords.Value;

            // Determine opacity based on floor
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && marker.FloorId != null)
            {
                opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var (r, g, b) = MapMarker.GetMarkerColor(marker.MarkerType);
            var markerColor = Color.FromArgb((byte)(opacity * 255), r, g, b);

            var markerSize = 48 * inverseScale * _markerScale;
            var iconImage = GetMarkerIcon(marker.MarkerType);

            if (iconImage != null)
            {
                var image = new Image
                {
                    Source = iconImage,
                    Width = markerSize,
                    Height = markerSize,
                    Opacity = opacity
                };

                Canvas.SetLeft(image, sx - markerSize / 2);
                Canvas.SetTop(image, sy - markerSize / 2);
                MarkersCanvas.Children.Add(image);
            }
            else
            {
                // Fallback to colored circle
                var circle = new Ellipse
                {
                    Width = markerSize,
                    Height = markerSize,
                    Fill = new SolidColorBrush(markerColor),
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    StrokeThickness = 3 * inverseScale
                };

                Canvas.SetLeft(circle, sx - markerSize / 2);
                Canvas.SetTop(circle, sy - markerSize / 2);
                MarkersCanvas.Children.Add(circle);

                // Icon text
                var iconText = marker.MarkerType switch
                {
                    MapMarkerType.PmcExtraction => "P",
                    MapMarkerType.ScavExtraction => "S",
                    MapMarkerType.SharedExtraction => "E",
                    _ => "?"
                };

                var icon = new TextBlock
                {
                    Text = iconText,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    FontSize = 24 * inverseScale,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };

                icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(icon, sx - icon.DesiredSize.Width / 2);
                Canvas.SetTop(icon, sy - icon.DesiredSize.Height / 2);
                MarkersCanvas.Children.Add(icon);
            }

            // Name label
            var displayName = !string.IsNullOrEmpty(marker.NameKo) ? marker.NameKo : marker.Name;
            var nameLabel = new TextBlock
            {
                Text = displayName,
                Foreground = new SolidColorBrush(markerColor),
                FontSize = 28 * inverseScale,
                FontWeight = FontWeights.SemiBold
            };

            Canvas.SetLeft(nameLabel, sx + markerSize / 2 + 8 * inverseScale);
            Canvas.SetTop(nameLabel, sy - 14 * inverseScale);
            MarkersCanvas.Children.Add(nameLabel);

            // Floor label (if different floor)
            if (hasFloors && marker.FloorId != null && opacity < 1.0)
            {
                var floorDisplayName = _sortedFloors?
                    .FirstOrDefault(f => string.Equals(f.LayerId, marker.FloorId, StringComparison.OrdinalIgnoreCase))
                    ?.DisplayName ?? marker.FloorId;

                var floorLabel = new TextBlock
                {
                    Text = $"[{floorDisplayName}]",
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), 154, 136, 102)),
                    FontSize = 20 * inverseScale,
                    FontStyle = FontStyles.Italic
                };

                Canvas.SetLeft(floorLabel, sx + markerSize / 2 + 8 * inverseScale);
                Canvas.SetTop(floorLabel, sy + 16 * inverseScale);
                MarkersCanvas.Children.Add(floorLabel);
            }
        }
    }

    private void RedrawTransit()
    {
        if (TransitCanvas == null) return;
        TransitCanvas.Children.Clear();

        if (_currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var transitMarkers = _dbService.GetTransitMarkersForMap(_currentMapConfig.Key);

        foreach (var marker in transitMarkers)
        {
            var screenCoords = _currentMapConfig.GameToScreen(marker.X, marker.Z);
            if (screenCoords == null) continue;

            var (sx, sy) = screenCoords.Value;

            // Determine opacity based on floor
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && marker.FloorId != null)
            {
                opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var transitColor = Color.FromArgb((byte)(opacity * 255), 33, 150, 243); // #2196F3

            var markerSize = 40 * inverseScale * _markerScale;
            var iconImage = GetMarkerIcon(MapMarkerType.Transit);

            if (iconImage != null)
            {
                var image = new Image
                {
                    Source = iconImage,
                    Width = markerSize,
                    Height = markerSize,
                    Opacity = opacity
                };

                Canvas.SetLeft(image, sx - markerSize / 2);
                Canvas.SetTop(image, sy - markerSize / 2);
                TransitCanvas.Children.Add(image);
            }
            else
            {
                // Fallback to diamond shape
                var diamond = new Polygon
                {
                    Fill = new SolidColorBrush(transitColor),
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    StrokeThickness = 2 * inverseScale,
                    Points = new PointCollection
                    {
                        new Point(sx, sy - markerSize / 2),
                        new Point(sx + markerSize / 2, sy),
                        new Point(sx, sy + markerSize / 2),
                        new Point(sx - markerSize / 2, sy)
                    }
                };

                TransitCanvas.Children.Add(diamond);
            }

            // Name label
            var displayName = !string.IsNullOrEmpty(marker.NameKo) ? marker.NameKo : marker.Name;
            var nameLabel = new TextBlock
            {
                Text = displayName,
                Foreground = new SolidColorBrush(transitColor),
                FontSize = 24 * inverseScale,
                FontWeight = FontWeights.SemiBold
            };

            Canvas.SetLeft(nameLabel, sx + markerSize / 2 + 8 * inverseScale);
            Canvas.SetTop(nameLabel, sy - 12 * inverseScale);
            TransitCanvas.Children.Add(nameLabel);
        }
    }

    #endregion
}
