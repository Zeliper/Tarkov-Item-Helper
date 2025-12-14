using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;

namespace TarkovDBEditor.Views;

public partial class MapEditorWindow : Window
{
    // Zoom settings
    private double _zoomLevel = 1.0;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 12.0;  // 1200%
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

    // Floor management
    private string? _currentFloorId;
    private List<MapFloorConfig>? _sortedFloors;

    // Objective being edited
    private readonly QuestObjectiveItem? _objective;
    private readonly List<LocationPoint> _locationPoints = new();
    private readonly List<LocationPoint> _optionalPoints = new();
    private bool _isEditingOptionalPoints;

    // Marker editing mode (when no objective is provided)
    private bool _isMarkerMode;
    private MapMarkerType _currentMarkerType = MapMarkerType.PmcExtraction;
    private readonly List<MapMarker> _mapMarkers = new();

    // Marker selection and dragging
    private MapMarker? _selectedMarker;
    private bool _isDraggingMarker;
    private Point _markerDragStartScreen;
    private double _markerDragStartX;
    private double _markerDragStartZ;

    // Icon cache for marker images
    private static readonly Dictionary<MapMarkerType, BitmapImage?> _iconCache = new();

    // Screenshot watcher for player position
    private readonly ScreenshotWatcherService _watcherService = ScreenshotWatcherService.Instance;
    private readonly FloorLocationService _floorLocationService = FloorLocationService.Instance;
    private EftPosition? _lastPlayerPosition;

    // Result
    public bool WasSaved { get; private set; }

    public MapEditorWindow()
    {
        InitializeComponent();
        LoadMapConfigs();
        InitializeMarkerTypeSelector();
        Loaded += MapEditorWindow_Loaded;
        Closed += MapEditorWindow_Closed;
        PreviewKeyDown += MapEditorWindow_KeyDown;
    }

    public MapEditorWindow(QuestObjectiveItem objective) : this()
    {
        _objective = objective;
        _isMarkerMode = false;

        // Copy existing location points (preserve floor info)
        foreach (var point in objective.LocationPoints)
        {
            _locationPoints.Add(new LocationPoint(point.X, point.Y, point.Z, point.FloorId));
        }

        // Copy existing optional points (preserve floor info)
        foreach (var point in objective.OptionalPoints)
        {
            _optionalPoints.Add(new LocationPoint(point.X, point.Y, point.Z, point.FloorId));
        }
    }

    private void InitializeMarkerTypeSelector()
    {
        MarkerTypeSelector.Items.Clear();
        foreach (MapMarkerType markerType in Enum.GetValues(typeof(MapMarkerType)))
        {
            var (r, g, b) = MapMarker.GetMarkerColor(markerType);
            var item = new ComboBoxItem
            {
                Content = MapMarker.GetMarkerTypeName(markerType),
                Tag = markerType,
                Foreground = new SolidColorBrush(Color.FromRgb(r, g, b))
            };
            MarkerTypeSelector.Items.Add(item);
        }
        MarkerTypeSelector.SelectedIndex = 2; // Default to PMC Extraction
    }

    private void MapEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_objective != null)
        {
            // Objective editing mode
            _isMarkerMode = false;
            ObjectiveInfoPanel.Visibility = Visibility.Visible;
            MarkerEditorPanel.Visibility = Visibility.Collapsed;
            ClearPointsButton.Visibility = Visibility.Visible;
            ClearMarkersButton.Visibility = Visibility.Collapsed;
            ExportMarkersButton.Visibility = Visibility.Collapsed;

            ObjectiveDescriptionText.Text = _objective.Description.Length > 80
                ? _objective.Description.Substring(0, 80) + "..."
                : _objective.Description;

            // Find and select matching map (use EffectiveMapName which falls back to QuestLocation)
            var matchingMap = _mapConfigs?.FindByMapName(_objective.EffectiveMapName);
            if (matchingMap != null)
            {
                MapSelector.SelectedItem = matchingMap;
            }

            // Disable map selector in objective mode - coordinates are tied to this specific map
            MapSelector.IsEnabled = false;
        }
        else
        {
            // Marker editing mode
            _isMarkerMode = true;
            ObjectiveInfoPanel.Visibility = Visibility.Collapsed;
            MarkerEditorPanel.Visibility = Visibility.Visible;
            ClearPointsButton.Visibility = Visibility.Collapsed;
            ClearMarkersButton.Visibility = Visibility.Visible;
            ExportMarkersButton.Visibility = Visibility.Visible;
            SaveButton.Visibility = Visibility.Collapsed;
            TxtMarkerControls.Visibility = Visibility.Visible;
            TxtMarkerDelete.Visibility = Visibility.Visible;

            Title = "Map Marker Editor";
            LoadExistingMarkers();
        }

        if (MapSelector.SelectedItem == null && MapSelector.Items.Count > 0)
        {
            MapSelector.SelectedIndex = 0;
        }

        UpdatePointsDisplay();
        UpdateMarkerCount();

        // Subscribe to watcher events
        _watcherService.PositionDetected += OnPositionDetected;
        _watcherService.StateChanged += OnWatcherStateChanged;
        UpdateWatcherStatus();
    }

    private void MapEditorWindow_Closed(object? sender, EventArgs e)
    {
        // Unsubscribe from watcher events
        _watcherService.PositionDetected -= OnPositionDetected;
        _watcherService.StateChanged -= OnWatcherStateChanged;
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
            UpdateFloorSelector(config);
            LoadMap(config);
            UpdateMarkerCount();
            RedrawMarkers();
        }
    }

    #region Floor Management

    private void UpdateFloorSelector(MapConfig config)
    {
        FloorSelector.Items.Clear();
        _currentFloorId = null;
        _sortedFloors = null;

        var floors = config.Floors;
        if (floors == null || floors.Count == 0)
        {
            // Single floor map - hide floor selector
            TxtFloorLabel.Visibility = Visibility.Collapsed;
            FloorSelector.Visibility = Visibility.Collapsed;
            TxtFloorHotkeys.Visibility = Visibility.Collapsed;
            return;
        }

        // Multi-floor map - show floor selector
        TxtFloorLabel.Visibility = Visibility.Visible;
        FloorSelector.Visibility = Visibility.Visible;
        TxtFloorHotkeys.Visibility = Visibility.Visible;

        // Sort floors by order
        _sortedFloors = floors.OrderBy(f => f.Order).ToList();

        int defaultIndex = 0;
        for (int i = 0; i < _sortedFloors.Count; i++)
        {
            var floor = _sortedFloors[i];
            FloorSelector.Items.Add(new ComboBoxItem
            {
                Content = floor.DisplayName,
                Tag = floor.LayerId
            });

            if (floor.IsDefault)
            {
                defaultIndex = i;
            }
        }

        if (FloorSelector.Items.Count > 0)
        {
            FloorSelector.SelectedIndex = defaultIndex;
            _currentFloorId = _sortedFloors[defaultIndex].LayerId;
        }
    }

    private void FloorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FloorSelector.SelectedItem is ComboBoxItem floorItem && floorItem.Tag is string floorId)
        {
            if (_currentFloorId != floorId)
            {
                _currentFloorId = floorId;

                // Reload map with new floor (without resetting view)
                if (_currentMapConfig != null)
                {
                    LoadMap(_currentMapConfig, resetView: false);
                    RedrawMarkers();
                }
            }
        }
    }

    private async void MapEditorWindow_KeyDown(object sender, KeyEventArgs e)
    {
        // Delete selected marker (but not when typing in TextBox)
        if (e.Key == Key.Delete && _selectedMarker != null && _isMarkerMode)
        {
            // Don't handle if TextBox has focus
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
                return;

            var markerToDelete = _selectedMarker;
            _mapMarkers.Remove(markerToDelete);
            _selectedMarker = null;

            // Delete from DB
            try
            {
                await _markerService.DeleteMarkerAsync(markerToDelete.Id);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error deleting marker: {ex.Message}";
            }

            UpdateMarkerCount();
            RedrawMarkers();
            StatusText.Text = "Marker deleted";
            e.Handled = true;
            return;
        }

        // Handle Numpad 0-5 for floor selection
        if (_sortedFloors == null || _sortedFloors.Count == 0)
            return;

        int floorIndex = -1;

        switch (e.Key)
        {
            case Key.NumPad0:
                floorIndex = 0;
                break;
            case Key.NumPad1:
                floorIndex = 1;
                break;
            case Key.NumPad2:
                floorIndex = 2;
                break;
            case Key.NumPad3:
                floorIndex = 3;
                break;
            case Key.NumPad4:
                floorIndex = 4;
                break;
            case Key.NumPad5:
                floorIndex = 5;
                break;
        }

        if (floorIndex >= 0 && floorIndex < _sortedFloors.Count)
        {
            FloorSelector.SelectedIndex = floorIndex;
            e.Handled = true;
        }
    }

    #endregion

    private void LoadMap(MapConfig config, bool resetView = true)
    {
        try
        {
            var svgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Maps", config.SvgFileName);

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

                // Set default floor (main) as dimmed background when viewing other floors
                var defaultFloor = config.Floors.FirstOrDefault(f => f.IsDefault);
                var currentFloor = config.Floors.FirstOrDefault(f =>
                    string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));

                if (defaultFloor != null && !string.Equals(_currentFloorId, defaultFloor.LayerId, StringComparison.OrdinalIgnoreCase))
                {
                    backgroundFloorId = defaultFloor.LayerId;

                    // Lower opacity for basement floors
                    if (currentFloor != null && currentFloor.Order < 0)
                    {
                        backgroundOpacity = 0.15;
                    }
                }
            }

            // Load SVG with floor filtering - only main floor shown as dimmed background
            if (visibleFloors != null)
            {
                var preprocessor = new Services.SvgStylePreprocessor();
                var processedSvg = preprocessor.ProcessSvgFile(svgPath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

                // Save to temp file and load
                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"map_editor_{Guid.NewGuid()}.svg");
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

            // Reset view only on initial load
            if (resetView)
            {
                SetZoom(1.0);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CenterMapInView();
                    RedrawPoints();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                RedrawPoints();
            }

            var floorInfo = !string.IsNullOrEmpty(_currentFloorId) ? $" [{_currentFloorId}]" : "";
            StatusText.Text = $"Loaded: {config.DisplayName}{floorInfo}";
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

        // Add point with current floor info to the appropriate list
        var newPoint = new LocationPoint(gameX, 0, gameZ, _currentFloorId);
        if (_isEditingOptionalPoints)
        {
            _optionalPoints.Add(newPoint);
        }
        else
        {
            _locationPoints.Add(newPoint);
        }

        UpdatePointsDisplay();
        RedrawPoints();

        var floorInfo = _currentFloorId != null ? $" (Floor: {_currentFloorId})" : "";
        var pointType = _isEditingOptionalPoints ? "OR location" : "point";
        StatusText.Text = $"Added {pointType}: ({gameX:F1}, {gameZ:F1}){floorInfo}";
    }

    private void RemoveLastPoint()
    {
        if (_isEditingOptionalPoints)
        {
            if (_optionalPoints.Count > 0)
            {
                _optionalPoints.RemoveAt(_optionalPoints.Count - 1);
                UpdatePointsDisplay();
                RedrawPoints();
                StatusText.Text = "Removed last OR location";
            }
        }
        else
        {
            if (_locationPoints.Count > 0)
            {
                _locationPoints.RemoveAt(_locationPoints.Count - 1);
                UpdatePointsDisplay();
                RedrawPoints();
                StatusText.Text = "Removed last point";
            }
        }
    }

    private void UpdatePointsDisplay()
    {
        PointsCountText.Text = _locationPoints.Count.ToString();
        OptionalPointsCountText.Text = $" / {_optionalPoints.Count} OR";
    }

    private void RedrawPoints()
    {
        PointsCanvas.Children.Clear();

        if (_currentMapConfig == null) return;
        if (_locationPoints.Count == 0 && _optionalPoints.Count == 0) return;

        System.Diagnostics.Debug.WriteLine($"[MapEditor.RedrawPoints] CurrentMap: Key={_currentMapConfig.Key}, Transform={string.Join(",", _currentMapConfig.CalibratedTransform ?? Array.Empty<double>())}");
        foreach (var pt in _locationPoints)
        {
            var (sx, sy) = _currentMapConfig.GameToScreen(pt.X, pt.Z);
            System.Diagnostics.Debug.WriteLine($"  Point: Game({pt.X:F2}, {pt.Z:F2}) -> Screen({sx:F2}, {sy:F2}), Floor={pt.FloorId}");
        }

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        // Helper to determine opacity based on floor
        double GetOpacity(LocationPoint point)
        {
            if (!hasFloors || _currentFloorId == null) return 1.0;
            if (point.FloorId == null) return 1.0; // Legacy points without floor info
            return string.Equals(point.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
        }

        // Get points for the current floor (for polygon/line drawing)
        var currentFloorPoints = _locationPoints
            .Where(p => !hasFloors || _currentFloorId == null ||
                       p.FloorId == null ||
                       string.Equals(p.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Get points on other floors (for faded polygon drawing)
        var otherFloorPoints = hasFloors && _currentFloorId != null
            ? _locationPoints
                .Where(p => p.FloorId != null &&
                           !string.Equals(p.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : new List<LocationPoint>();

        // Draw faded polygon for other floors if 3+ points exist there
        if (otherFloorPoints.Count >= 3)
        {
            var fadedPolygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(25, 112, 168, 0)),  // Very faded fill
                Stroke = new SolidColorBrush(Color.FromArgb(80, 112, 168, 0)),  // Faded stroke
                StrokeThickness = 2 * inverseScale,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };

            foreach (var point in otherFloorPoints)
            {
                var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
                fadedPolygon.Points.Add(new Point(sx, sy));
            }

            PointsCanvas.Children.Add(fadedPolygon);

            // Add floor label at centroid of other floor polygon
            var centroidX = otherFloorPoints.Average(p => p.X);
            var centroidZ = otherFloorPoints.Average(p => p.Z);
            var (labelX, labelY) = _currentMapConfig.GameToScreen(centroidX, centroidZ);

            // Get the floor name from the first point (they should all be on the same floor for this case)
            var otherFloorId = otherFloorPoints.First().FloorId;
            var floorDisplayName = _sortedFloors?
                .FirstOrDefault(f => string.Equals(f.LayerId, otherFloorId, StringComparison.OrdinalIgnoreCase))
                ?.DisplayName ?? otherFloorId;

            var floorLabel = new TextBlock
            {
                Text = $"[{floorDisplayName}]",
                Foreground = new SolidColorBrush(Color.FromArgb(150, 154, 136, 102)),
                FontSize = 36 * inverseScale,
                FontWeight = FontWeights.SemiBold,
                FontStyle = FontStyles.Italic
            };

            floorLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(floorLabel, labelX - floorLabel.DesiredSize.Width / 2);
            Canvas.SetTop(floorLabel, labelY - floorLabel.DesiredSize.Height / 2);
            PointsCanvas.Children.Add(floorLabel);
        }
        // Draw faded line for other floors if 2 points
        else if (otherFloorPoints.Count == 2)
        {
            var (sx1, sy1) = _currentMapConfig.GameToScreen(otherFloorPoints[0].X, otherFloorPoints[0].Z);
            var (sx2, sy2) = _currentMapConfig.GameToScreen(otherFloorPoints[1].X, otherFloorPoints[1].Z);

            var fadedLine = new Line
            {
                X1 = sx1, Y1 = sy1,
                X2 = sx2, Y2 = sy2,
                Stroke = new SolidColorBrush(Color.FromArgb(80, 112, 168, 0)),
                StrokeThickness = 2 * inverseScale,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };

            PointsCanvas.Children.Add(fadedLine);
        }

        // Draw polygon if 3+ points on current floor
        if (currentFloorPoints.Count >= 3)
        {
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(80, 112, 168, 0)),
                Stroke = new SolidColorBrush(Color.FromRgb(112, 168, 0)),
                StrokeThickness = 2 * inverseScale
            };

            foreach (var point in currentFloorPoints)
            {
                var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
                polygon.Points.Add(new Point(sx, sy));
            }

            PointsCanvas.Children.Add(polygon);
        }
        // Draw line if 2 points on current floor
        else if (currentFloorPoints.Count == 2)
        {
            var (sx1, sy1) = _currentMapConfig.GameToScreen(currentFloorPoints[0].X, currentFloorPoints[0].Z);
            var (sx2, sy2) = _currentMapConfig.GameToScreen(currentFloorPoints[1].X, currentFloorPoints[1].Z);

            var line = new Line
            {
                X1 = sx1, Y1 = sy1,
                X2 = sx2, Y2 = sy2,
                Stroke = new SolidColorBrush(Color.FromRgb(112, 168, 0)),
                StrokeThickness = 2 * inverseScale
            };

            PointsCanvas.Children.Add(line);
        }

        // Draw point markers (all points, with different opacity)
        // For 3+ points (polygon), don't show individual markers - only show the polygon area
        var showMarkers = _locationPoints.Count < 3;
        var markerSize = 48 * inverseScale;  // 4x larger (was 12)
        var index = 1;
        foreach (var point in _locationPoints)
        {
            var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
            var opacity = GetOpacity(point);
            var isCurrentFloor = opacity > 0.5;

            if (showMarkers)
            {
                // Circle marker (only for 1-2 points)
                var ellipse = new Ellipse
                {
                    Width = markerSize,
                    Height = markerSize,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 112, 168, 0)),
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    StrokeThickness = 4 * inverseScale  // 4x larger (was 1)
                };

                Canvas.SetLeft(ellipse, sx - markerSize / 2);
                Canvas.SetTop(ellipse, sy - markerSize / 2);
                PointsCanvas.Children.Add(ellipse);

                // Number label
                var label = new TextBlock
                {
                    Text = index.ToString(),
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    FontSize = 40 * inverseScale,  // 4x larger (was 10)
                    FontWeight = FontWeights.Bold
                };

                Canvas.SetLeft(label, sx + markerSize / 2 + 8 * inverseScale);
                Canvas.SetTop(label, sy - markerSize / 2);
                PointsCanvas.Children.Add(label);

                // Floor indicator (if different floor and floors exist)
                if (hasFloors && point.FloorId != null && !isCurrentFloor)
                {
                    // Get DisplayName from floor config instead of using LayerId
                    var floorDisplayName = _sortedFloors?
                        .FirstOrDefault(f => string.Equals(f.LayerId, point.FloorId, StringComparison.OrdinalIgnoreCase))
                        ?.DisplayName ?? point.FloorId;

                    var floorLabel = new TextBlock
                    {
                        Text = $"[{floorDisplayName}]",
                        Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), 154, 136, 102)),
                        FontSize = 32 * inverseScale,  // 4x larger (was 8)
                        FontStyle = FontStyles.Italic
                    };

                    Canvas.SetLeft(floorLabel, sx + markerSize / 2 + 8 * inverseScale);
                    Canvas.SetTop(floorLabel, sy + markerSize / 2);
                    PointsCanvas.Children.Add(floorLabel);
                }
            }

            index++;
        }

        // Draw Optional Points (OR locations) - always as individual markers with different color
        if (_optionalPoints.Count > 0)
        {
            var optMarkerSize = 48 * inverseScale;
            var optIndex = 1;
            foreach (var point in _optionalPoints)
            {
                var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);

                // Determine opacity based on floor
                double optOpacity = 1.0;
                if (hasFloors && _currentFloorId != null && point.FloorId != null)
                {
                    optOpacity = string.Equals(point.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
                }
                var isOptCurrentFloor = optOpacity > 0.5;

                // Orange circle for OR locations
                var optEllipse = new Ellipse
                {
                    Width = optMarkerSize,
                    Height = optMarkerSize,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(optOpacity * 255), 255, 87, 34)), // #FF5722
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(optOpacity * 255), 255, 255, 255)),
                    StrokeThickness = 4 * inverseScale
                };

                Canvas.SetLeft(optEllipse, sx - optMarkerSize / 2);
                Canvas.SetTop(optEllipse, sy - optMarkerSize / 2);
                PointsCanvas.Children.Add(optEllipse);

                // "OR" prefix label
                var orLabel = new TextBlock
                {
                    Text = $"OR{optIndex}",
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(optOpacity * 255), 255, 255, 255)),
                    FontSize = 36 * inverseScale,
                    FontWeight = FontWeights.Bold
                };

                Canvas.SetLeft(orLabel, sx + optMarkerSize / 2 + 8 * inverseScale);
                Canvas.SetTop(orLabel, sy - optMarkerSize / 2);
                PointsCanvas.Children.Add(orLabel);

                // Floor indicator (if different floor)
                if (hasFloors && point.FloorId != null && !isOptCurrentFloor)
                {
                    var floorDisplayName = _sortedFloors?
                        .FirstOrDefault(f => string.Equals(f.LayerId, point.FloorId, StringComparison.OrdinalIgnoreCase))
                        ?.DisplayName ?? point.FloorId;

                    var floorLabel = new TextBlock
                    {
                        Text = $"[{floorDisplayName}]",
                        Foreground = new SolidColorBrush(Color.FromArgb((byte)(optOpacity * 200), 154, 136, 102)),
                        FontSize = 28 * inverseScale,
                        FontStyle = FontStyles.Italic
                    };

                    Canvas.SetLeft(floorLabel, sx + optMarkerSize / 2 + 8 * inverseScale);
                    Canvas.SetTop(floorLabel, sy + optMarkerSize / 2);
                    PointsCanvas.Children.Add(floorLabel);
                }

                optIndex++;
            }
        }
    }

    private void PointTypeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string mode) return;

        _isEditingOptionalPoints = mode == "Optional";

        // Update button visuals
        if (_isEditingOptionalPoints)
        {
            BtnLocationMode.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            BtnLocationMode.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            BtnOptionalMode.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x22)); // #FF5722
            BtnOptionalMode.Foreground = Brushes.White;
            StatusText.Text = "Mode: OR Locations (click to add alternative locations)";
        }
        else
        {
            BtnLocationMode.Background = new SolidColorBrush(Color.FromRgb(0x70, 0xA8, 0x00)); // #70A800
            BtnLocationMode.Foreground = Brushes.White;
            BtnOptionalMode.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
            BtnOptionalMode.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            StatusText.Text = "Mode: Area (click to define area points)";
        }
    }

    private void ClearPoints_Click(object sender, RoutedEventArgs e)
    {
        if (_isEditingOptionalPoints)
        {
            if (_optionalPoints.Count > 0)
            {
                var result = MessageBox.Show("Clear all OR locations?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _optionalPoints.Clear();
                    UpdatePointsDisplay();
                    RedrawPoints();
                    StatusText.Text = "All OR locations cleared";
                }
            }
        }
        else
        {
            if (_locationPoints.Count > 0)
            {
                var result = MessageBox.Show("Clear all area points?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _locationPoints.Clear();
                    UpdatePointsDisplay();
                    RedrawPoints();
                    StatusText.Text = "All area points cleared";
                }
            }
        }
    }

    private void SaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[SaveAndClose] _locationPoints: {_locationPoints.Count}, _optionalPoints: {_optionalPoints.Count}");

        if (_objective != null)
        {
            // Update objective's location points (including FloorId)
            _objective.LocationPoints.Clear();
            foreach (var point in _locationPoints)
            {
                _objective.LocationPoints.Add(new LocationPoint(point.X, point.Y, point.Z, point.FloorId));
            }

            // Update objective's optional points (including FloorId)
            _objective.OptionalPoints.Clear();
            foreach (var point in _optionalPoints)
            {
                _objective.OptionalPoints.Add(new LocationPoint(point.X, point.Y, point.Z, point.FloorId));
            }

            System.Diagnostics.Debug.WriteLine($"[SaveAndClose] After save - LocationPoints: {_objective.LocationPoints.Count}, OptionalPoints: {_objective.OptionalPoints.Count}");
            System.Diagnostics.Debug.WriteLine($"[SaveAndClose] LocationPointsJson: {_objective.LocationPointsJson}");
            System.Diagnostics.Debug.WriteLine($"[SaveAndClose] OptionalPointsJson: {_objective.OptionalPointsJson}");
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
        RedrawMarkers();
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
        var isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        // Ctrl+Click for marker selection/dragging
        if (isCtrlPressed && _isMarkerMode && _currentMapConfig != null)
        {
            var canvasPos = e.GetPosition(MapCanvas);
            var clickedMarker = FindMarkerAtPosition(canvasPos.X, canvasPos.Y);

            if (clickedMarker != null)
            {
                // Select and start dragging
                _selectedMarker = clickedMarker;
                _isDraggingMarker = true;
                _markerDragStartScreen = e.GetPosition(MapViewerGrid);
                _markerDragStartX = clickedMarker.X;
                _markerDragStartZ = clickedMarker.Z;
                RedrawMarkers();
                StatusText.Text = $"Selected: {clickedMarker.Name}";
                MapViewerGrid.CaptureMouse();
                e.Handled = true;
                return;
            }
            else
            {
                // Click on empty space - deselect
                if (_selectedMarker != null)
                {
                    _selectedMarker = null;
                    RedrawMarkers();
                    StatusText.Text = "Selection cleared";
                }
            }
        }

        _isDragging = true;
        _wasDragging = false;
        _dragStartPoint = e.GetPosition(MapViewerGrid);
        _dragStartTranslateX = MapTranslate.X;
        _dragStartTranslateY = MapTranslate.Y;
        MapViewerGrid.CaptureMouse();
    }

    private async void MapViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Handle marker drag end
        if (_isDraggingMarker)
        {
            _isDraggingMarker = false;
            MapViewerGrid.ReleaseMouseCapture();
            if (_selectedMarker != null)
            {
                // Save updated position to DB
                try
                {
                    await _markerService.SaveMarkerAsync(_selectedMarker);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Error saving marker position: {ex.Message}";
                }
                StatusText.Text = $"Moved: {_selectedMarker.Name} to ({_selectedMarker.X:F1}, {_selectedMarker.Z:F1})";
            }
            return;
        }

        var wasDragging = _wasDragging;
        _isDragging = false;
        _wasDragging = false;
        MapViewerGrid.ReleaseMouseCapture();

        // If Ctrl is pressed, don't add marker (selection mode)
        var isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
        if (isCtrlPressed) return;

        // If it was a click (not a drag), add a point or marker
        if (!wasDragging)
        {
            var pos = e.GetPosition(MapCanvas);
            if (_isMarkerMode)
            {
                AddMarker(pos.X, pos.Y);
            }
            else
            {
                AddPoint(pos.X, pos.Y);
            }
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

        // Handle marker dragging
        if (_isDraggingMarker && _selectedMarker != null && _currentMapConfig != null)
        {
            var currentPoint = e.GetPosition(MapViewerGrid);
            var deltaScreenX = (currentPoint.X - _markerDragStartScreen.X) / _zoomLevel;
            var deltaScreenY = (currentPoint.Y - _markerDragStartScreen.Y) / _zoomLevel;

            // Convert screen delta to game coordinate delta
            var (startGameX, startGameZ) = _currentMapConfig.ScreenToGame(0, 0);
            var (endGameX, endGameZ) = _currentMapConfig.ScreenToGame(deltaScreenX, deltaScreenY);
            var deltaGameX = endGameX - startGameX;
            var deltaGameZ = endGameZ - startGameZ;

            _selectedMarker.X = _markerDragStartX + deltaGameX;
            _selectedMarker.Z = _markerDragStartZ + deltaGameZ;
            RedrawMarkers();
            return;
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

    #region Map Marker Management

    private readonly MapMarkerService _markerService = MapMarkerService.Instance;

    private async void LoadExistingMarkers()
    {
        _mapMarkers.Clear();

        try
        {
            var markers = await _markerService.LoadAllMarkersAsync();
            foreach (var marker in markers)
            {
                _mapMarkers.Add(marker);
            }
            StatusText.Text = $"Loaded {_mapMarkers.Count} markers from DB";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading markers: {ex.Message}";
        }
    }

    private void MarkerTypeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MarkerTypeSelector.SelectedItem is ComboBoxItem item && item.Tag is MapMarkerType markerType)
        {
            _currentMarkerType = markerType;
        }
    }

    private async void AddMarker(double screenX, double screenY)
    {
        if (_currentMapConfig == null) return;

        var name = MarkerNameInput.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a marker name.", "Name Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            MarkerNameInput.Focus();
            return;
        }

        // Convert screen coordinates to game coordinates
        var (gameX, gameZ) = _currentMapConfig.ScreenToGame(screenX, screenY);

        var marker = new MapMarker
        {
            Name = name,
            MarkerType = _currentMarkerType,
            MapKey = _currentMapConfig.Key,
            X = gameX,
            Y = 0,
            Z = gameZ,
            FloorId = _currentFloorId
        };

        _mapMarkers.Add(marker);

        // Save to DB immediately
        try
        {
            await _markerService.SaveMarkerAsync(marker);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error saving marker: {ex.Message}";
        }

        UpdateMarkerCount();
        RedrawMarkers();

        // Clear name input for next marker
        MarkerNameInput.Clear();

        var floorInfo = _currentFloorId != null ? $" [{_currentFloorId}]" : "";
        StatusText.Text = $"Added {MapMarker.GetMarkerTypeName(_currentMarkerType)}: {name}{floorInfo}";
    }

    private void UpdateMarkerCount()
    {
        if (_currentMapConfig == null)
        {
            MarkerCountText.Text = _mapMarkers.Count.ToString();
        }
        else
        {
            var mapMarkers = _mapMarkers.Count(m => m.MapKey == _currentMapConfig.Key);
            MarkerCountText.Text = $"{mapMarkers} / {_mapMarkers.Count}";
        }
    }

    private MapMarker? FindMarkerAtPosition(double screenX, double screenY)
    {
        if (_currentMapConfig == null) return null;

        var hitRadius = 30.0; // Screen pixels for hit detection

        foreach (var marker in _mapMarkers.Where(m => m.MapKey == _currentMapConfig.Key))
        {
            var (markerScreenX, markerScreenY) = _currentMapConfig.GameToScreen(marker.X, marker.Z);
            var distance = Math.Sqrt(Math.Pow(screenX - markerScreenX, 2) + Math.Pow(screenY - markerScreenY, 2));

            if (distance <= hitRadius)
            {
                return marker;
            }
        }

        return null;
    }

    private BitmapImage? GetMarkerIcon(MapMarkerType markerType)
    {
        if (_iconCache.TryGetValue(markerType, out var cachedIcon))
        {
            return cachedIcon;
        }

        try
        {
            var iconFileName = MapMarker.GetIconFileName(markerType);
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons", iconFileName);

            if (File.Exists(iconPath))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(iconPath, UriKind.Absolute);
                bitmap.DecodePixelWidth = 64; // Limit size for performance
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
        MarkersCanvas.Children.Clear();

        if (_currentMapConfig == null) return;

        // In objective mode, still show markers but smaller and more transparent (reference only)
        var isReferenceMode = !_isMarkerMode;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        // Filter markers for current map
        var markersForMap = _mapMarkers.Where(m => m.MapKey == _currentMapConfig.Key).ToList();

        // In objective mode, need to load markers if not already loaded
        if (isReferenceMode && markersForMap.Count == 0)
        {
            LoadMarkersForReferenceMode();
            markersForMap = _mapMarkers.Where(m => m.MapKey == _currentMapConfig.Key).ToList();
        }

        foreach (var marker in markersForMap)
        {
            var (sx, sy) = _currentMapConfig.GameToScreen(marker.X, marker.Z);
            var isSelected = marker == _selectedMarker;

            // Determine opacity based on floor
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && marker.FloorId != null)
            {
                opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            // In reference mode: smaller and more transparent (10% opacity)
            if (isReferenceMode)
            {
                opacity *= 0.10;
            }

            var (r, g, b) = MapMarker.GetMarkerColor(marker.MarkerType);
            var markerColor = Color.FromArgb((byte)(opacity * 255), r, g, b);

            // Create marker visual - smaller in reference mode (50% size)
            var markerSize = (isReferenceMode ? 24 : 48) * inverseScale;
            var iconImage = GetMarkerIcon(marker.MarkerType);

            // Draw selection highlight (not in reference mode)
            if (isSelected && !isReferenceMode)
            {
                var selectionSize = markerSize + 16 * inverseScale;
                var selectionRing = new Ellipse
                {
                    Width = selectionSize,
                    Height = selectionSize,
                    Fill = Brushes.Transparent,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 215, 0)), // Gold
                    StrokeThickness = 4 * inverseScale,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };

                Canvas.SetLeft(selectionRing, sx - selectionSize / 2);
                Canvas.SetTop(selectionRing, sy - selectionSize / 2);
                MarkersCanvas.Children.Add(selectionRing);
            }

            if (iconImage != null)
            {
                // Use actual icon image
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
                // Fallback to colored circle with letter
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

                // Icon text fallback
                var iconText = marker.MarkerType switch
                {
                    MapMarkerType.PmcSpawn => "P",
                    MapMarkerType.ScavSpawn => "S",
                    MapMarkerType.PmcExtraction => "E",
                    MapMarkerType.ScavExtraction => "E",
                    MapMarkerType.SharedExtraction => "E",
                    MapMarkerType.Transit => "T",
                    MapMarkerType.BossSpawn => "B",
                    MapMarkerType.RaiderSpawn => "R",
                    MapMarkerType.Lever => "L",
                    MapMarkerType.Keys => "K",
                    _ => "?"
                };

                var icon = new TextBlock
                {
                    Text = iconText,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    FontSize = (isReferenceMode ? 12 : 24) * inverseScale,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };

                icon.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(icon, sx - icon.DesiredSize.Width / 2);
                Canvas.SetTop(icon, sy - icon.DesiredSize.Height / 2);
                MarkersCanvas.Children.Add(icon);
            }

            // Skip labels in reference mode (keep it minimal)
            if (isReferenceMode) continue;

            // Name label
            var nameLabel = new TextBlock
            {
                Text = marker.Name,
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

    private void LoadMarkersForReferenceMode()
    {
        // Synchronously load markers for reference display in objective mode
        try
        {
            var markers = _markerService.LoadAllMarkersAsync().GetAwaiter().GetResult();
            _mapMarkers.Clear();
            foreach (var marker in markers)
            {
                _mapMarkers.Add(marker);
            }
        }
        catch
        {
            // Ignore errors in reference mode
        }
    }

    private async void ClearMarkers_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMapConfig == null) return;

        var mapMarkers = _mapMarkers.Where(m => m.MapKey == _currentMapConfig.Key).ToList();
        if (mapMarkers.Count > 0)
        {
            var result = MessageBox.Show(
                $"Clear all {mapMarkers.Count} markers for {_currentMapConfig.DisplayName}?",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Delete from DB
                try
                {
                    await _markerService.DeleteMarkersByMapAsync(_currentMapConfig.Key);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Error clearing markers: {ex.Message}";
                    return;
                }

                foreach (var marker in mapMarkers)
                {
                    _mapMarkers.Remove(marker);
                }
                UpdateMarkerCount();
                RedrawMarkers();
                StatusText.Text = $"Cleared markers for {_currentMapConfig.DisplayName}";
            }
        }
    }

    private void ExportMarkers_Click(object sender, RoutedEventArgs e)
    {
        // Export to JSON file (for backup/sharing)
        try
        {
            var exportPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Resources", "Data", "map_markers_export.json");

            var directory = System.IO.Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_mapMarkers, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            File.WriteAllText(exportPath, json);
            StatusText.Text = $"Exported {_mapMarkers.Count} markers to {exportPath}";
            MessageBox.Show($"Successfully exported {_mapMarkers.Count} markers to JSON.\n(Markers are already saved in DB)", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export markers: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Player Position Display

    private void OnPositionDetected(object? sender, PositionDetectedEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
            _lastPlayerPosition = e.Position;
            UpdatePlayerPositionText(e.Position);
            DrawPlayerMarker(e.Position);

            // Auto-detect floor if configured
            if (_currentMapConfig != null)
            {
                var detectedFloor = await _floorLocationService.DetectFloorAsync(
                    _currentMapConfig.Key, e.Position.X, e.Position.Y, e.Position.Z);

                if (!string.IsNullOrEmpty(detectedFloor) && detectedFloor != _currentFloorId)
                {
                    // Auto-switch floor
                    var floorItem = FloorSelector.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(item => item.Tag as string == detectedFloor);

                    if (floorItem != null)
                    {
                        FloorSelector.SelectedItem = floorItem;
                    }
                }
            }
        });
    }

    private void OnWatcherStateChanged(object? sender, WatcherStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateWatcherStatus();
        });
    }

    private void UpdateWatcherStatus()
    {
        if (_watcherService.IsWatching)
        {
            WatcherIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x70, 0xA8, 0x00));
            WatcherToggleButton.Content = "Stop";
            WatcherToggleButton.Background = new SolidColorBrush(Color.FromRgb(0xD4, 0x1C, 0x00));
        }
        else
        {
            WatcherIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            WatcherToggleButton.Content = "Start";
            WatcherToggleButton.Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44));
        }

        var position = _watcherService.CurrentPosition;
        if (position != null)
        {
            UpdatePlayerPositionText(position);
            DrawPlayerMarker(position);
        }
        else
        {
            PlayerPositionText.Text = "Player: --";
        }
    }

    private void UpdatePlayerPositionText(EftPosition position)
    {
        var angleStr = position.Angle.HasValue ? $" {position.Angle:F0}°" : "";
        PlayerPositionText.Text = $"Player: X:{position.X:F0}, Y:{position.Y:F1}, Z:{position.Z:F0}{angleStr}";
    }

    private async void WatcherToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_watcherService.IsWatching)
        {
            _watcherService.StopWatching();
        }
        else
        {
            // Try to get saved path from settings
            var settingsService = AppSettingsService.Instance;
            var savedPath = await settingsService.GetAsync(AppSettingsService.ScreenshotWatcherPath, "");

            if (string.IsNullOrEmpty(savedPath) || !Directory.Exists(savedPath))
            {
                // Try auto-detect
                savedPath = _watcherService.DetectDefaultScreenshotFolder();
            }

            if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
            {
                _watcherService.StartWatching(savedPath);
            }
            else
            {
                MessageBox.Show(
                    "Screenshot folder not configured.\n\nPlease configure it via:\nDebug > Screenshot Watcher Settings...",
                    "Watcher Not Configured",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        UpdateWatcherStatus();
    }

    private void DrawPlayerMarker(EftPosition position)
    {
        if (_currentMapConfig == null) return;

        PlayerCanvas.Children.Clear();

        // Convert game coords to screen coords (using player-specific transform if available)
        var (screenX, screenY) = _currentMapConfig.GameToScreenForPlayer(position.X, position.Z);

        // Debug output
        var usedTransform = _currentMapConfig.PlayerMarkerTransform ?? _currentMapConfig.CalibratedTransform;
        System.Diagnostics.Debug.WriteLine($"[DrawPlayerMarker] Map: {_currentMapConfig.Key}");
        System.Diagnostics.Debug.WriteLine($"  Game: X={position.X:F2}, Y={position.Y:F2}, Z={position.Z:F2}");
        System.Diagnostics.Debug.WriteLine($"  Screen: X={screenX:F2}, Y={screenY:F2}");
        System.Diagnostics.Debug.WriteLine($"  MapSize: {_currentMapConfig.ImageWidth}x{_currentMapConfig.ImageHeight}");
        System.Diagnostics.Debug.WriteLine($"  PlayerMarkerTransform: [{string.Join(", ", usedTransform ?? Array.Empty<double>())}]");

        // Scale for current zoom level
        double inverseScale = 1.0 / _zoomLevel;
        double markerSize = 16 * inverseScale;

        // Draw player circle (cyan)
        var playerCircle = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
            Stroke = Brushes.White,
            StrokeThickness = 2 * inverseScale
        };

        Canvas.SetLeft(playerCircle, screenX - markerSize / 2);
        Canvas.SetTop(playerCircle, screenY - markerSize / 2);
        PlayerCanvas.Children.Add(playerCircle);

        // Draw direction arrow if angle is available
        if (position.Angle.HasValue)
        {
            double arrowLength = 24 * inverseScale;
            double angleRad = (position.Angle.Value - 90) * Math.PI / 180.0; // -90 to point up at 0°

            double endX = screenX + Math.Cos(angleRad) * arrowLength;
            double endY = screenY + Math.Sin(angleRad) * arrowLength;

            // Arrow line
            var arrowLine = new Line
            {
                X1 = screenX,
                Y1 = screenY,
                X2 = endX,
                Y2 = endY,
                Stroke = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
                StrokeThickness = 3 * inverseScale,
                StrokeEndLineCap = PenLineCap.Triangle
            };
            PlayerCanvas.Children.Add(arrowLine);

            // Arrow head
            double headSize = 8 * inverseScale;
            double headAngle1 = angleRad + 2.5; // ~143 degrees
            double headAngle2 = angleRad - 2.5;

            var arrowHead = new Polygon
            {
                Points = new PointCollection
                {
                    new Point(endX, endY),
                    new Point(endX - Math.Cos(headAngle1) * headSize, endY - Math.Sin(headAngle1) * headSize),
                    new Point(endX - Math.Cos(headAngle2) * headSize, endY - Math.Sin(headAngle2) * headSize)
                },
                Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4))
            };
            PlayerCanvas.Children.Add(arrowHead);
        }

        // Draw "P" label
        var label = new TextBlock
        {
            Text = "P",
            Foreground = Brushes.White,
            FontSize = 10 * inverseScale,
            FontWeight = FontWeights.Bold
        };

        Canvas.SetLeft(label, screenX - 4 * inverseScale);
        Canvas.SetTop(label, screenY - 5 * inverseScale);
        PlayerCanvas.Children.Add(label);
    }

    #endregion
}
