using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Path = System.IO.Path;
using File = System.IO.File;

namespace TarkovDBEditor.Views;

/// <summary>
/// Floor Range Editor - 지도에서 다각형을 그려 X/Z 범위를 설정
/// </summary>
public partial class FloorRangeEditorWindow : Window
{
    // Zoom settings
    private double _zoomLevel = 1.0;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 12.0;
    private static readonly double[] ZoomPresets = { 0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 12.0 };

    // Drag state
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;
    private bool _wasDragging;

    // Map configuration
    private MapConfigList? _mapConfigs;
    private MapConfig? _currentMapConfig;
    private string? _currentFloorId;

    // Polygon points (game coordinates)
    private readonly List<(double X, double Z)> _polygonPoints = new();

    // Input data
    private readonly MapFloorLocation _floorLocation;
    private readonly string _mapKey;
    private readonly string _floorId;
    private readonly string _floorDisplayName;

    // Result
    public bool WasSaved { get; private set; }
    public double ResultMinX { get; private set; }
    public double ResultMaxX { get; private set; }
    public double ResultMinZ { get; private set; }
    public double ResultMaxZ { get; private set; }

    public FloorRangeEditorWindow(MapFloorLocation floorLocation, string mapKey, string floorId, string floorDisplayName)
    {
        InitializeComponent();

        _floorLocation = floorLocation;
        _mapKey = mapKey;
        _floorId = floorId;
        _floorDisplayName = floorDisplayName;

        // Initialize with existing values if they have a range
        if (floorLocation.MinX.HasValue && floorLocation.MaxX.HasValue &&
            floorLocation.MinZ.HasValue && floorLocation.MaxZ.HasValue)
        {
            // Create polygon from existing bounding box (4 corners)
            _polygonPoints.Add((floorLocation.MinX.Value, floorLocation.MinZ.Value));
            _polygonPoints.Add((floorLocation.MaxX.Value, floorLocation.MinZ.Value));
            _polygonPoints.Add((floorLocation.MaxX.Value, floorLocation.MaxZ.Value));
            _polygonPoints.Add((floorLocation.MinX.Value, floorLocation.MaxZ.Value));
        }

        LoadMapConfigs();
        Loaded += FloorRangeEditorWindow_Loaded;
    }

    private void FloorRangeEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RegionNameText.Text = _floorLocation.RegionName;
        FloorNameText.Text = _floorDisplayName;

        // Find and load the map
        var matchingMap = _mapConfigs?.FindByMapName(_mapKey);
        if (matchingMap != null)
        {
            _currentMapConfig = matchingMap;
            _currentFloorId = _floorId;
            LoadMap(matchingMap);
        }
        else
        {
            StatusText.Text = $"Map not found: {_mapKey}";
        }

        UpdateRangeDisplay();
    }

    private void LoadMapConfigs()
    {
        try
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Data", "map_configs.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                _mapConfigs = JsonSerializer.Deserialize<MapConfigList>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading map configs: {ex.Message}";
        }
    }

    private void LoadMap(MapConfig config)
    {
        try
        {
            var svgPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Maps", config.SvgFileName);

            if (!File.Exists(svgPath))
            {
                MessageBox.Show($"Map file not found: {svgPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Floor filtering for multi-floor maps
            IEnumerable<string>? visibleFloors = null;
            IEnumerable<string>? allFloors = null;
            string? backgroundFloorId = null;
            double backgroundOpacity = 0.3;

            if (config.Floors != null && config.Floors.Count > 0 && !string.IsNullOrEmpty(_currentFloorId))
            {
                allFloors = config.Floors.Select(f => f.LayerId);
                visibleFloors = new[] { _currentFloorId };

                var defaultFloor = config.Floors.FirstOrDefault(f => f.IsDefault);
                if (defaultFloor != null && !string.Equals(_currentFloorId, defaultFloor.LayerId, StringComparison.OrdinalIgnoreCase))
                {
                    backgroundFloorId = defaultFloor.LayerId;
                    var currentFloor = config.Floors.FirstOrDefault(f =>
                        string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));
                    if (currentFloor != null && currentFloor.Order < 0)
                    {
                        backgroundOpacity = 0.15;
                    }
                }
            }

            // Load SVG with floor filtering
            if (visibleFloors != null)
            {
                var preprocessor = new SvgStylePreprocessor();
                var processedSvg = preprocessor.ProcessSvgFile(svgPath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

                var tempPath = Path.Combine(Path.GetTempPath(), $"floor_range_editor_{Guid.NewGuid()}.svg");
                File.WriteAllText(tempPath, processedSvg);
                MapSvg.Source = new Uri(tempPath, UriKind.Absolute);
            }
            else
            {
                MapSvg.Source = new Uri(svgPath, UriKind.Absolute);
            }

            MapSvg.Width = config.ImageWidth;
            MapSvg.Height = config.ImageHeight;

            MapCanvas.Width = config.ImageWidth;
            MapCanvas.Height = config.ImageHeight;
            Canvas.SetLeft(MapSvg, 0);
            Canvas.SetTop(MapSvg, 0);

            SetZoom(1.0);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CenterMapInView();
                RedrawPolygon();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            StatusText.Text = $"Loaded: {config.DisplayName} [{_floorDisplayName}]";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load map: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region Polygon Points Management

    private void AddPoint(double screenX, double screenY)
    {
        if (_currentMapConfig == null) return;

        var (gameX, gameZ) = _currentMapConfig.ScreenToGame(screenX, screenY);
        _polygonPoints.Add((gameX, gameZ));

        UpdateRangeDisplay();
        RedrawPolygon();

        StatusText.Text = $"Added point: ({gameX:F1}, {gameZ:F1})";
    }

    private void RemoveLastPoint()
    {
        if (_polygonPoints.Count > 0)
        {
            _polygonPoints.RemoveAt(_polygonPoints.Count - 1);
            UpdateRangeDisplay();
            RedrawPolygon();
            StatusText.Text = "Removed last point";
        }
    }

    private void UpdateRangeDisplay()
    {
        PointsCountText.Text = _polygonPoints.Count.ToString();

        if (_polygonPoints.Count >= 2)
        {
            var minX = _polygonPoints.Min(p => p.X);
            var maxX = _polygonPoints.Max(p => p.X);
            var minZ = _polygonPoints.Min(p => p.Z);
            var maxZ = _polygonPoints.Max(p => p.Z);

            RangeXText.Text = $"{minX:F1} ~ {maxX:F1}";
            RangeZText.Text = $"{minZ:F1} ~ {maxZ:F1}";
        }
        else
        {
            RangeXText.Text = "-- ~ --";
            RangeZText.Text = "-- ~ --";
        }
    }

    private void RedrawPolygon()
    {
        PointsCanvas.Children.Clear();
        BoundingBoxCanvas.Children.Clear();

        if (_currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var markerSize = 24 * inverseScale;

        // Draw polygon if 3+ points
        if (_polygonPoints.Count >= 3)
        {
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(80, 112, 168, 0)),
                Stroke = new SolidColorBrush(Color.FromRgb(112, 168, 0)),
                StrokeThickness = 2 * inverseScale
            };

            foreach (var point in _polygonPoints)
            {
                var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
                polygon.Points.Add(new Point(sx, sy));
            }

            PointsCanvas.Children.Add(polygon);

            // Draw bounding box
            DrawBoundingBox();
        }
        // Draw line if 2 points
        else if (_polygonPoints.Count == 2)
        {
            var (sx1, sy1) = _currentMapConfig.GameToScreen(_polygonPoints[0].X, _polygonPoints[0].Z);
            var (sx2, sy2) = _currentMapConfig.GameToScreen(_polygonPoints[1].X, _polygonPoints[1].Z);

            var line = new Line
            {
                X1 = sx1, Y1 = sy1,
                X2 = sx2, Y2 = sy2,
                Stroke = new SolidColorBrush(Color.FromRgb(112, 168, 0)),
                StrokeThickness = 2 * inverseScale
            };

            PointsCanvas.Children.Add(line);

            // Draw bounding box for line
            DrawBoundingBox();
        }

        // Draw point markers
        var index = 1;
        foreach (var point in _polygonPoints)
        {
            var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);

            var ellipse = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Fill = new SolidColorBrush(Color.FromRgb(112, 168, 0)),
                Stroke = Brushes.White,
                StrokeThickness = 2 * inverseScale
            };

            Canvas.SetLeft(ellipse, sx - markerSize / 2);
            Canvas.SetTop(ellipse, sy - markerSize / 2);
            PointsCanvas.Children.Add(ellipse);

            // Number label
            var label = new TextBlock
            {
                Text = index.ToString(),
                Foreground = Brushes.White,
                FontSize = 20 * inverseScale,
                FontWeight = FontWeights.Bold
            };

            Canvas.SetLeft(label, sx + markerSize / 2 + 4 * inverseScale);
            Canvas.SetTop(label, sy - markerSize / 2);
            PointsCanvas.Children.Add(label);

            index++;
        }
    }

    private void DrawBoundingBox()
    {
        if (_currentMapConfig == null || _polygonPoints.Count < 2) return;

        var minX = _polygonPoints.Min(p => p.X);
        var maxX = _polygonPoints.Max(p => p.X);
        var minZ = _polygonPoints.Min(p => p.Z);
        var maxZ = _polygonPoints.Max(p => p.Z);

        var inverseScale = 1.0 / _zoomLevel;

        // Convert corners to screen coordinates
        var (sMinX, sMinZ) = _currentMapConfig.GameToScreen(minX, minZ);
        var (sMaxX, sMaxZ) = _currentMapConfig.GameToScreen(maxX, maxZ);

        // Get all 4 corners
        var (sTopLeftX, sTopLeftY) = _currentMapConfig.GameToScreen(minX, minZ);
        var (sTopRightX, sTopRightY) = _currentMapConfig.GameToScreen(maxX, minZ);
        var (sBottomRightX, sBottomRightY) = _currentMapConfig.GameToScreen(maxX, maxZ);
        var (sBottomLeftX, sBottomLeftY) = _currentMapConfig.GameToScreen(minX, maxZ);

        // Draw bounding box as polygon (handles rotation from transform)
        var boundingBox = new Polygon
        {
            Points = new PointCollection
            {
                new Point(sTopLeftX, sTopLeftY),
                new Point(sTopRightX, sTopRightY),
                new Point(sBottomRightX, sBottomRightY),
                new Point(sBottomLeftX, sBottomLeftY)
            },
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromRgb(255, 152, 0)), // Orange
            StrokeThickness = 3 * inverseScale,
            StrokeDashArray = new DoubleCollection { 6, 3 }
        };

        BoundingBoxCanvas.Children.Add(boundingBox);

        // Add labels at corners showing X/Z values
        var labelSize = 16 * inverseScale;

        // MinX, MinZ corner
        var minLabel = new TextBlock
        {
            Text = $"({minX:F0}, {minZ:F0})",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
            FontSize = labelSize,
            FontWeight = FontWeights.SemiBold
        };
        Canvas.SetLeft(minLabel, sTopLeftX - 60 * inverseScale);
        Canvas.SetTop(minLabel, sTopLeftY - 20 * inverseScale);
        BoundingBoxCanvas.Children.Add(minLabel);

        // MaxX, MaxZ corner
        var maxLabel = new TextBlock
        {
            Text = $"({maxX:F0}, {maxZ:F0})",
            Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
            FontSize = labelSize,
            FontWeight = FontWeights.SemiBold
        };
        Canvas.SetLeft(maxLabel, sBottomRightX + 10 * inverseScale);
        Canvas.SetTop(maxLabel, sBottomRightY + 5 * inverseScale);
        BoundingBoxCanvas.Children.Add(maxLabel);
    }

    private void ClearPoints_Click(object sender, RoutedEventArgs e)
    {
        if (_polygonPoints.Count > 0)
        {
            var result = MessageBox.Show("Clear all points?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _polygonPoints.Clear();
                UpdateRangeDisplay();
                RedrawPolygon();
                StatusText.Text = "All points cleared";
            }
        }
    }

    private void SaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        if (_polygonPoints.Count < 2)
        {
            MessageBox.Show("At least 2 points are required to define a range.", "Insufficient Points",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultMinX = _polygonPoints.Min(p => p.X);
        ResultMaxX = _polygonPoints.Max(p => p.X);
        ResultMinZ = _polygonPoints.Min(p => p.Z);
        ResultMaxZ = _polygonPoints.Max(p => p.Z);

        WasSaved = true;
        DialogResult = true;
        Close();
    }

    #endregion

    #region Zoom and Pan

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        var nextPreset = ZoomPresets.FirstOrDefault(p => p > _zoomLevel);
        var newZoom = nextPreset > 0 ? nextPreset : _zoomLevel * 1.25;
        ZoomToCenter(newZoom);
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        var prevPreset = ZoomPresets.LastOrDefault(p => p < _zoomLevel);
        var newZoom = prevPreset > 0 ? prevPreset : _zoomLevel * 0.8;
        ZoomToCenter(newZoom);
    }

    private void BtnResetView_Click(object sender, RoutedEventArgs e)
    {
        SetZoom(1.0);
        CenterMapInView();
    }

    private void ZoomToCenter(double newZoom)
    {
        var mousePos = new Point(MapViewerGrid.ActualWidth / 2, MapViewerGrid.ActualHeight / 2);
        ZoomToPoint(newZoom, mousePos);
    }

    private void ZoomToPoint(double newZoom, Point viewerPoint)
    {
        var oldZoom = _zoomLevel;
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        if (Math.Abs(newZoom - oldZoom) < 0.001) return;

        var canvasX = (viewerPoint.X - MapTranslate.X) / oldZoom;
        var canvasY = (viewerPoint.Y - MapTranslate.Y) / oldZoom;

        MapTranslate.X = viewerPoint.X - canvasX * newZoom;
        MapTranslate.Y = viewerPoint.Y - canvasY * newZoom;

        SetZoom(newZoom);
    }

    private void SetZoom(double zoom)
    {
        _zoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);
        MapScale.ScaleX = _zoomLevel;
        MapScale.ScaleY = _zoomLevel;

        ZoomText.Text = $"{_zoomLevel * 100:F0}%";

        RedrawPolygon();
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
        _isDragging = true;
        _wasDragging = false;
        _dragStartPoint = e.GetPosition(MapViewerGrid);
        _dragStartTranslateX = MapTranslate.X;
        _dragStartTranslateY = MapTranslate.Y;
        MapViewerGrid.CaptureMouse();
    }

    private void MapViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var wasDragging = _wasDragging;
        _isDragging = false;
        _wasDragging = false;
        MapViewerGrid.ReleaseMouseCapture();

        // If it was a click (not a drag), add a point
        if (!wasDragging)
        {
            var pos = e.GetPosition(MapCanvas);
            AddPoint(pos.X, pos.Y);
        }
    }

    private void MapViewer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        RemoveLastPoint();
        e.Handled = true;
    }

    private void MapViewer_MouseMove(object sender, MouseEventArgs e)
    {
        // Update coordinate display
        if (_currentMapConfig != null)
        {
            var canvasPos = e.GetPosition(MapCanvas);
            var (gameX, gameZ) = _currentMapConfig.ScreenToGame(canvasPos.X, canvasPos.Y);
            GameCoordsText.Text = $"X: {gameX:F1}, Z: {gameZ:F1}";
            ScreenCoordsText.Text = $"X: {canvasPos.X:F0}, Y: {canvasPos.Y:F0}";
        }

        if (!_isDragging) return;

        var currentPt = e.GetPosition(MapViewerGrid);
        var deltaX = currentPt.X - _dragStartPoint.X;
        var deltaY = currentPt.Y - _dragStartPoint.Y;

        // Mark as dragging if moved more than 5 pixels
        if (Math.Abs(deltaX) > 5 || Math.Abs(deltaY) > 5)
        {
            _wasDragging = true;
        }

        if (_wasDragging)
        {
            MapTranslate.X = _dragStartTranslateX + deltaX;
            MapTranslate.Y = _dragStartTranslateY + deltaY;
            MapCanvas.Cursor = Cursors.ScrollAll;
        }
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
