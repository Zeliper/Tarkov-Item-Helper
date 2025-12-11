using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovDBEditor.Models;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace TarkovDBEditor.Views;

public partial class MapEditorWindow : Window
{
    // Zoom settings
    private double _zoomLevel = 1.0;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;
    private static readonly double[] ZoomPresets = { 0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 5.0 };

    // Drag state
    private bool _isDragging;
    private Point _dragStartPoint;
    private double _dragStartTranslateX;
    private double _dragStartTranslateY;
    private bool _wasDragging;

    // Map configuration
    private MapConfigList? _mapConfigs;
    private MapConfig? _currentMapConfig;

    // Objective being edited
    private readonly QuestObjectiveItem? _objective;
    private readonly List<LocationPoint> _locationPoints = new();

    // Result
    public bool WasSaved { get; private set; }

    public MapEditorWindow()
    {
        InitializeComponent();
        LoadMapConfigs();
        Loaded += MapEditorWindow_Loaded;
    }

    public MapEditorWindow(QuestObjectiveItem objective) : this()
    {
        _objective = objective;

        // Copy existing points
        foreach (var point in objective.LocationPoints)
        {
            _locationPoints.Add(new LocationPoint(point.X, point.Y, point.Z));
        }
    }

    private void MapEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_objective != null)
        {
            ObjectiveDescriptionText.Text = _objective.Description.Length > 80
                ? _objective.Description.Substring(0, 80) + "..."
                : _objective.Description;

            // Find and select matching map
            var matchingMap = _mapConfigs?.FindByMapName(_objective.MapName);
            if (matchingMap != null)
            {
                MapSelector.SelectedItem = matchingMap;
            }
            else if (MapSelector.Items.Count > 0)
            {
                MapSelector.SelectedIndex = 0;
            }
        }
        else if (MapSelector.Items.Count > 0)
        {
            MapSelector.SelectedIndex = 0;
        }

        UpdatePointsDisplay();
    }

    private void LoadMapConfigs()
    {
        try
        {
            var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Data", "map_configs.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                _mapConfigs = JsonSerializer.Deserialize<MapConfigList>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (_mapConfigs != null)
                {
                    foreach (var map in _mapConfigs.Maps)
                    {
                        MapSelector.Items.Add(map);
                    }
                }
            }
            else
            {
                StatusText.Text = "Warning: map_configs.json not found";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading map configs: {ex.Message}";
        }
    }

    private void MapSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapSelector.SelectedItem is MapConfig config)
        {
            _currentMapConfig = config;
            LoadMap(config);
        }
    }

    private void LoadMap(MapConfig config)
    {
        try
        {
            var svgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Maps", config.SvgFileName);

            if (!File.Exists(svgPath))
            {
                MessageBox.Show($"Map file not found: {svgPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MapSvg.Source = new Uri(svgPath, UriKind.Absolute);
            MapSvg.Width = config.ImageWidth;
            MapSvg.Height = config.ImageHeight;

            MapCanvas.Width = config.ImageWidth;
            MapCanvas.Height = config.ImageHeight;
            Canvas.SetLeft(MapSvg, 0);
            Canvas.SetTop(MapSvg, 0);

            // Reset view
            SetZoom(1.0);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CenterMapInView();
                RedrawPoints();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            StatusText.Text = $"Loaded: {config.DisplayName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load map: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #region Points Management

    private void AddPoint(double screenX, double screenY)
    {
        if (_currentMapConfig == null) return;

        // Convert screen coordinates to game coordinates
        var (gameX, gameZ) = _currentMapConfig.ScreenToGame(screenX, screenY);

        _locationPoints.Add(new LocationPoint(gameX, 0, gameZ)); // Y is height, set to 0 for now
        UpdatePointsDisplay();
        RedrawPoints();

        StatusText.Text = $"Added point: ({gameX:F1}, {gameZ:F1})";
    }

    private void RemoveLastPoint()
    {
        if (_locationPoints.Count > 0)
        {
            _locationPoints.RemoveAt(_locationPoints.Count - 1);
            UpdatePointsDisplay();
            RedrawPoints();
            StatusText.Text = "Removed last point";
        }
    }

    private void UpdatePointsDisplay()
    {
        PointsCountText.Text = _locationPoints.Count.ToString();
    }

    private void RedrawPoints()
    {
        PointsCanvas.Children.Clear();

        if (_currentMapConfig == null || _locationPoints.Count == 0) return;

        var inverseScale = 1.0 / _zoomLevel;

        // Draw polygon if 3+ points
        if (_locationPoints.Count >= 3)
        {
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(80, 112, 168, 0)),
                Stroke = new SolidColorBrush(Color.FromRgb(112, 168, 0)),
                StrokeThickness = 2 * inverseScale
            };

            foreach (var point in _locationPoints)
            {
                var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
                polygon.Points.Add(new Point(sx, sy));
            }

            PointsCanvas.Children.Add(polygon);
        }
        // Draw line if 2 points
        else if (_locationPoints.Count == 2)
        {
            var (sx1, sy1) = _currentMapConfig.GameToScreen(_locationPoints[0].X, _locationPoints[0].Z);
            var (sx2, sy2) = _currentMapConfig.GameToScreen(_locationPoints[1].X, _locationPoints[1].Z);

            var line = new Line
            {
                X1 = sx1, Y1 = sy1,
                X2 = sx2, Y2 = sy2,
                Stroke = new SolidColorBrush(Color.FromRgb(112, 168, 0)),
                StrokeThickness = 2 * inverseScale
            };

            PointsCanvas.Children.Add(line);
        }

        // Draw point markers
        var markerSize = 12 * inverseScale;
        var index = 1;
        foreach (var point in _locationPoints)
        {
            var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);

            // Circle marker
            var ellipse = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Fill = new SolidColorBrush(Color.FromRgb(112, 168, 0)),
                Stroke = Brushes.White,
                StrokeThickness = 1 * inverseScale
            };

            Canvas.SetLeft(ellipse, sx - markerSize / 2);
            Canvas.SetTop(ellipse, sy - markerSize / 2);
            PointsCanvas.Children.Add(ellipse);

            // Number label
            var label = new TextBlock
            {
                Text = index.ToString(),
                Foreground = Brushes.White,
                FontSize = 10 * inverseScale,
                FontWeight = FontWeights.Bold
            };

            Canvas.SetLeft(label, sx + markerSize / 2 + 2 * inverseScale);
            Canvas.SetTop(label, sy - markerSize / 2);
            PointsCanvas.Children.Add(label);

            index++;
        }
    }

    private void ClearPoints_Click(object sender, RoutedEventArgs e)
    {
        if (_locationPoints.Count > 0)
        {
            var result = MessageBox.Show("Clear all points?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _locationPoints.Clear();
                UpdatePointsDisplay();
                RedrawPoints();
                StatusText.Text = "All points cleared";
            }
        }
    }

    private void SaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        if (_objective != null)
        {
            // Update objective's location points
            _objective.LocationPoints.Clear();
            foreach (var point in _locationPoints)
            {
                _objective.LocationPoints.Add(new LocationPoint(point.X, point.Y, point.Z));
            }
        }

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

        var zoomPercent = $"{_zoomLevel * 100:F0}%";
        ZoomText.Text = zoomPercent;

        RedrawPoints();
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

        var currentPoint = e.GetPosition(MapViewerGrid);
        var deltaX = currentPoint.X - _dragStartPoint.X;
        var deltaY = currentPoint.Y - _dragStartPoint.Y;

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
