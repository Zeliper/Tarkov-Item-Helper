using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;
using TarkovDBEditor.ViewModels;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Cursors = System.Windows.Input.Cursors;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace TarkovDBEditor.Views;

public partial class QuestObjectiveEditorWindow : Window
{
    private readonly QuestObjectiveEditorViewModel _viewModel;
    private readonly ScreenshotWatcherService _watcherService = ScreenshotWatcherService.Instance;
    private readonly FloorLocationService _floorLocationService = FloorLocationService.Instance;
    private EftPosition? _lastPlayerPosition;

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

    // Floor management
    private string? _currentFloorId;
    private List<MapFloorConfig>? _sortedFloors;

    public QuestObjectiveEditorWindow()
    {
        InitializeComponent();

        _viewModel = new QuestObjectiveEditorViewModel();
        DataContext = _viewModel;

        LoadMapConfigs();
        Loaded += Window_Loaded;
        Closed += Window_Closed;
        PreviewKeyDown += Window_KeyDown;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _watcherService.PositionDetected -= OnPositionDetected;
        _watcherService.StateChanged -= OnWatcherStateChanged;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Don't handle if TextBox has focus
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            return;

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

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAvailableMapsAsync();

        // Set ItemsSource for objectives list
        ObjectivesList.ItemsSource = _viewModel.FilteredObjectives;

        // Register watcher event handlers
        _watcherService.PositionDetected += OnPositionDetected;
        _watcherService.StateChanged += OnWatcherStateChanged;

        // Auto-start Player Tracking
        await AutoStartPlayerTracking();

        // Populate map selector
        MapSelector.Items.Clear();
        if (_mapConfigs != null)
        {
            foreach (var map in _mapConfigs.Maps)
            {
                MapSelector.Items.Add(map.DisplayName);
            }
        }

        if (MapSelector.Items.Count > 0)
        {
            MapSelector.SelectedIndex = 0;
        }
    }

    private async Task AutoStartPlayerTracking()
    {
        if (_watcherService.IsWatching)
        {
            UpdateWatcherStatus();
            return;
        }

        var settingsService = AppSettingsService.Instance;
        var savedPath = await settingsService.GetAsync(AppSettingsService.ScreenshotWatcherPath, "");

        if (string.IsNullOrEmpty(savedPath) || !Directory.Exists(savedPath))
        {
            savedPath = _watcherService.DetectDefaultScreenshotFolder();
        }

        if (!string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath))
        {
            _watcherService.StartWatching(savedPath);
        }

        UpdateWatcherStatus();
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

    #region Map Selection

    private void MapSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapSelector.SelectedItem is string displayName && _mapConfigs != null)
        {
            var config = _mapConfigs.Maps.FirstOrDefault(m => m.DisplayName == displayName);
            if (config != null)
            {
                _currentMapConfig = config;
                _viewModel.SelectedMapKey = config.Key;
                _viewModel.CurrentMapConfig = config;
                UpdateFloorSelector(config);
                LoadMap(config);
                UpdateProgressText();
            }
        }
    }

    private void UpdateFloorSelector(MapConfig config)
    {
        FloorSelector.Items.Clear();
        _currentFloorId = null;
        _sortedFloors = null;

        var floors = config.Floors;
        if (floors == null || floors.Count == 0)
        {
            TxtFloorLabel.Visibility = Visibility.Collapsed;
            FloorSelector.Visibility = Visibility.Collapsed;
            return;
        }

        TxtFloorLabel.Visibility = Visibility.Visible;
        FloorSelector.Visibility = Visibility.Visible;

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

                if (_currentMapConfig != null)
                {
                    LoadMap(_currentMapConfig, resetView: false);
                }
            }
        }
    }

    #endregion

    #region Map Loading

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

            IEnumerable<string>? visibleFloors = null;
            IEnumerable<string>? allFloors = null;
            string? backgroundFloorId = null;
            double backgroundOpacity = 0.3;

            if (config.Floors != null && config.Floors.Count > 0 && !string.IsNullOrEmpty(_currentFloorId))
            {
                allFloors = config.Floors.Select(f => f.LayerId);
                visibleFloors = new[] { _currentFloorId };

                var defaultFloor = config.Floors.FirstOrDefault(f => f.IsDefault);
                var currentFloor = config.Floors.FirstOrDefault(f =>
                    string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));

                if (defaultFloor != null && !string.Equals(_currentFloorId, defaultFloor.LayerId, StringComparison.OrdinalIgnoreCase))
                {
                    backgroundFloorId = defaultFloor.LayerId;

                    if (currentFloor != null && currentFloor.Order < 0)
                    {
                        backgroundOpacity = 0.15;
                    }
                }
            }

            if (visibleFloors != null)
            {
                var preprocessor = new Services.SvgStylePreprocessor();
                var processedSvg = preprocessor.ProcessSvgFile(svgPath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"quest_obj_editor_{Guid.NewGuid()}.svg");
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

            if (resetView)
            {
                SetZoom(1.0);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CenterMapInView();
                    RedrawAllLayers();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                RedrawAllLayers();
            }

            var floorInfo = !string.IsNullOrEmpty(_currentFloorId) ? $" [{_currentFloorId}]" : "";
            StatusText.Text = $"Loaded: {config.DisplayName}{floorInfo}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load map: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Drawing

    private void RedrawAllLayers()
    {
        if (ApiMarkersCanvas == null || ObjectivesCanvas == null || EditingCanvas == null) return;

        RedrawApiMarkers();
        RedrawAllObjectives();
        RedrawEditingPoints();
    }

    private void RedrawApiMarkers()
    {
        if (ApiMarkersCanvas == null) return;

        ApiMarkersCanvas.Children.Clear();

        if (_currentMapConfig == null || ChkShowApiMarkers == null || !(ChkShowApiMarkers.IsChecked ?? false)) return;

        var inverseScale = 1.0 / _zoomLevel;
        var markerSize = 20 * inverseScale;
        var highlightedMarkerSize = 28 * inverseScale;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        // Get set of quest names where all objectives on this map are approved
        var fullyApprovedQuests = _viewModel.GetFullyApprovedQuestNamesForCurrentMap();

        // Get selected objective's quest name for highlighting
        var selectedQuestName = _viewModel.SelectedObjective?.QuestNameEN?.ToLowerInvariant();

        foreach (var marker in _viewModel.ApiMarkersForMap)
        {
            // Skip markers for quests that are fully approved
            if (!string.IsNullOrEmpty(marker.QuestNameEn) &&
                fullyApprovedQuests.Contains(marker.QuestNameEn.ToLowerInvariant()))
            {
                continue;
            }

            // API 마커는 PlayerMarkerTransform 사용 (geometry.y = gameX, geometry.x = gameZ)
            // 저장 시: marker.X = geometry.x, marker.Z = geometry.y
            var (sx, sy) = _currentMapConfig.GameToScreenForPlayer(marker.Z, marker.X);

            // Floor-based opacity
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && marker.FloorId != null)
            {
                opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            // Check if this marker belongs to the selected objective's quest
            var isHighlighted = !string.IsNullOrEmpty(selectedQuestName) &&
                               !string.IsNullOrEmpty(marker.QuestNameEn) &&
                               string.Equals(marker.QuestNameEn.ToLowerInvariant(), selectedQuestName, StringComparison.OrdinalIgnoreCase);

            if (isHighlighted)
            {
                // Draw highlight ring (cyan glow effect)
                var glowRing = new Ellipse
                {
                    Width = (highlightedMarkerSize + 12 * inverseScale) * 2,
                    Height = (highlightedMarkerSize + 12 * inverseScale) * 2,
                    Fill = new SolidColorBrush(Color.FromArgb(60, 0, 188, 212)),
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), 0, 188, 212)),
                    StrokeThickness = 3 * inverseScale
                };
                Canvas.SetLeft(glowRing, sx - highlightedMarkerSize - 12 * inverseScale);
                Canvas.SetTop(glowRing, sy - highlightedMarkerSize - 12 * inverseScale);
                ApiMarkersCanvas.Children.Add(glowRing);

                // Pentagon shape (cyan for highlighted)
                var pentagon = CreatePentagon(sx, sy, highlightedMarkerSize, Color.FromArgb((byte)(opacity * 255), 0, 188, 212));
                ApiMarkersCanvas.Children.Add(pentagon);

                // Label (cyan, bold)
                var label = new TextBlock
                {
                    Text = marker.Name,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0, 188, 212)),
                    FontSize = 12 * inverseScale,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(label, sx + highlightedMarkerSize + 4 * inverseScale);
                Canvas.SetTop(label, sy - 6 * inverseScale);
                ApiMarkersCanvas.Children.Add(label);
            }
            else
            {
                // Non-highlighted: reduce opacity if there's a selection
                var dimmedOpacity = !string.IsNullOrEmpty(selectedQuestName) ? opacity * 0.4 : opacity;

                // Pentagon shape for API markers (orange)
                var pentagon = CreatePentagon(sx, sy, markerSize, Color.FromArgb((byte)(dimmedOpacity * 255), 230, 81, 0));
                ApiMarkersCanvas.Children.Add(pentagon);

                // Label
                var label = new TextBlock
                {
                    Text = marker.Name,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(dimmedOpacity * 200), 230, 81, 0)),
                    FontSize = 10 * inverseScale,
                    FontWeight = FontWeights.SemiBold
                };
                Canvas.SetLeft(label, sx + markerSize);
                Canvas.SetTop(label, sy - 5 * inverseScale);
                ApiMarkersCanvas.Children.Add(label);
            }
        }
    }

    private Polygon CreatePentagon(double cx, double cy, double size, Color color)
    {
        var polygon = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 1.5 / _zoomLevel
        };

        for (int i = 0; i < 5; i++)
        {
            double angle = Math.PI * 2 * i / 5 - Math.PI / 2;
            double px = cx + size * Math.Cos(angle);
            double py = cy + size * Math.Sin(angle);
            polygon.Points.Add(new Point(px, py));
        }

        return polygon;
    }

    private void RedrawAllObjectives()
    {
        if (ObjectivesCanvas == null) return;

        ObjectivesCanvas.Children.Clear();

        if (_currentMapConfig == null || ChkShowAllObjectives == null || !(ChkShowAllObjectives.IsChecked ?? false)) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        foreach (var obj in _viewModel.FilteredObjectives)
        {
            if (obj == _viewModel.SelectedObjective) continue; // Skip selected, drawn in editing layer

            // Draw LocationPoints
            if (obj.LocationPoints.Count > 0)
            {
                DrawObjectivePoints(obj.LocationPoints, Color.FromRgb(255, 193, 7), inverseScale, hasFloors, obj.Description);
            }

            // Draw OptionalPoints
            if (obj.OptionalPoints.Count > 0)
            {
                DrawOptionalPoints(obj.OptionalPoints, inverseScale, hasFloors);
            }
        }
    }

    private void DrawObjectivePoints(IReadOnlyList<LocationPoint> points, Color color, double inverseScale, bool hasFloors, string? label = null)
    {
        if (_currentMapConfig == null || points.Count == 0) return;

        var currentFloorPoints = points
            .Where(p => !hasFloors || _currentFloorId == null || p.FloorId == null ||
                       string.Equals(p.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (currentFloorPoints.Count >= 3)
        {
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2 * inverseScale,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };

            foreach (var pt in currentFloorPoints)
            {
                var (sx, sy) = _currentMapConfig.GameToScreenForPlayer(pt.X, pt.Z);
                polygon.Points.Add(new Point(sx, sy));
            }

            ObjectivesCanvas.Children.Add(polygon);
        }
        else if (currentFloorPoints.Count == 2)
        {
            var (sx1, sy1) = _currentMapConfig.GameToScreenForPlayer(currentFloorPoints[0].X, currentFloorPoints[0].Z);
            var (sx2, sy2) = _currentMapConfig.GameToScreenForPlayer(currentFloorPoints[1].X, currentFloorPoints[1].Z);

            var line = new Line
            {
                X1 = sx1, Y1 = sy1,
                X2 = sx2, Y2 = sy2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2 * inverseScale,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };

            ObjectivesCanvas.Children.Add(line);
        }
        else if (currentFloorPoints.Count == 1)
        {
            var pt = currentFloorPoints[0];
            var (sx, sy) = _currentMapConfig.GameToScreenForPlayer(pt.X, pt.Z);
            var size = 16 * inverseScale;

            var diamond = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
                Points = new PointCollection
                {
                    new Point(sx, sy - size),
                    new Point(sx + size, sy),
                    new Point(sx, sy + size),
                    new Point(sx - size, sy)
                }
            };

            ObjectivesCanvas.Children.Add(diamond);
        }
    }

    private void DrawOptionalPoints(IReadOnlyList<LocationPoint> points, double inverseScale, bool hasFloors)
    {
        if (_currentMapConfig == null) return;

        var markerSize = 14 * inverseScale;
        int index = 1;

        foreach (var pt in points)
        {
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && pt.FloorId != null)
            {
                opacity = string.Equals(pt.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var (sx, sy) = _currentMapConfig.GameToScreenForPlayer(pt.X, pt.Z);

            var ellipse = new Ellipse
            {
                Width = markerSize * 2,
                Height = markerSize * 2,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), 255, 87, 34)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                StrokeThickness = 2 * inverseScale
            };

            Canvas.SetLeft(ellipse, sx - markerSize);
            Canvas.SetTop(ellipse, sy - markerSize);
            ObjectivesCanvas.Children.Add(ellipse);

            var label = new TextBlock
            {
                Text = $"OR{index}",
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                FontSize = 10 * inverseScale,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(label, sx + markerSize + 2);
            Canvas.SetTop(label, sy - 6 * inverseScale);
            ObjectivesCanvas.Children.Add(label);

            index++;
        }
    }

    private void RedrawEditingPoints()
    {
        if (EditingCanvas == null) return;

        EditingCanvas.Children.Clear();

        if (_currentMapConfig == null || _viewModel?.SelectedObjective == null) return;

        var obj = _viewModel.SelectedObjective;
        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        // Draw LocationPoints (green when editing area, amber otherwise)
        var areaColor = _viewModel.IsEditingLocationPoints ? Color.FromRgb(112, 168, 0) : Color.FromRgb(255, 193, 7);
        DrawEditingLocationPoints(obj.LocationPoints.ToList(), areaColor, inverseScale, hasFloors);

        // Draw OptionalPoints (orange when editing OR, dimmed otherwise)
        var orColor = _viewModel.IsEditingOptionalPoints ? Color.FromRgb(255, 87, 34) : Color.FromRgb(200, 100, 50);
        DrawEditingOptionalPoints(obj.OptionalPoints.ToList(), orColor, inverseScale, hasFloors);

        // Update point counts
        AreaPointsCount.Text = $"{obj.LocationPoints.Count} points";
        OrPointsCount.Text = $"{obj.OptionalPoints.Count} points";
    }

    private void DrawEditingLocationPoints(List<LocationPoint> points, Color color, double inverseScale, bool hasFloors)
    {
        if (_currentMapConfig == null || points.Count == 0) return;

        var currentFloorPoints = points
            .Where(p => !hasFloors || _currentFloorId == null || p.FloorId == null ||
                       string.Equals(p.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Draw polygon/line
        if (currentFloorPoints.Count >= 3)
        {
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 3 * inverseScale
            };

            foreach (var pt in currentFloorPoints)
            {
                var (sx, sy) = _currentMapConfig.GameToScreenForPlayer(pt.X, pt.Z);
                polygon.Points.Add(new Point(sx, sy));
            }

            EditingCanvas.Children.Add(polygon);
        }
        else if (currentFloorPoints.Count == 2)
        {
            var (sx1, sy1) = _currentMapConfig.GameToScreenForPlayer(currentFloorPoints[0].X, currentFloorPoints[0].Z);
            var (sx2, sy2) = _currentMapConfig.GameToScreenForPlayer(currentFloorPoints[1].X, currentFloorPoints[1].Z);

            var line = new Line
            {
                X1 = sx1, Y1 = sy1, X2 = sx2, Y2 = sy2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 3 * inverseScale
            };

            EditingCanvas.Children.Add(line);
        }

        // Draw markers for each point
        var markerSize = 24 * inverseScale;
        int index = 1;
        foreach (var pt in points)
        {
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && pt.FloorId != null)
            {
                opacity = string.Equals(pt.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var (sx, sy) = _currentMapConfig.GameToScreenForPlayer(pt.X, pt.Z);

            var ellipse = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                StrokeThickness = 2 * inverseScale
            };

            Canvas.SetLeft(ellipse, sx - markerSize / 2);
            Canvas.SetTop(ellipse, sy - markerSize / 2);
            EditingCanvas.Children.Add(ellipse);

            var label = new TextBlock
            {
                Text = index.ToString(),
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12 * inverseScale,
                FontWeight = FontWeights.Bold
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, sx - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, sy - label.DesiredSize.Height / 2);
            EditingCanvas.Children.Add(label);

            index++;
        }
    }

    private void DrawEditingOptionalPoints(List<LocationPoint> points, Color color, double inverseScale, bool hasFloors)
    {
        if (_currentMapConfig == null) return;

        var markerSize = 20 * inverseScale;
        int index = 1;

        foreach (var pt in points)
        {
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && pt.FloorId != null)
            {
                opacity = string.Equals(pt.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var (sx, sy) = _currentMapConfig.GameToScreenForPlayer(pt.X, pt.Z);

            var ellipse = new Ellipse
            {
                Width = markerSize * 2,
                Height = markerSize * 2,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                StrokeThickness = 3 * inverseScale
            };

            Canvas.SetLeft(ellipse, sx - markerSize);
            Canvas.SetTop(ellipse, sy - markerSize);
            EditingCanvas.Children.Add(ellipse);

            var label = new TextBlock
            {
                Text = $"OR{index}",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12 * inverseScale,
                FontWeight = FontWeights.Bold
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, sx - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, sy - label.DesiredSize.Height / 2);
            EditingCanvas.Children.Add(label);

            index++;
        }
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

        RedrawAllLayers();
        RedrawPlayerMarker();
    }

    private void RedrawPlayerMarker()
    {
        if (_lastPlayerPosition != null)
        {
            DrawPlayerMarker(_lastPlayerPosition);
        }
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

        RedrawPlayerMarker();
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
        if (!wasDragging && _viewModel.SelectedObjective != null)
        {
            var pos = e.GetPosition(MapCanvas);
            AddPoint(pos.X, pos.Y);
        }
    }

    private void MapViewer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedObjective != null)
        {
            RemoveLastPoint();
        }
        e.Handled = true;
    }

    private void MapViewer_MouseMove(object sender, MouseEventArgs e)
    {
        // Update coordinate display
        if (_currentMapConfig != null)
        {
            var canvasPos = e.GetPosition(MapCanvas);
            var (gameX, gameZ) = _currentMapConfig.ScreenToGameForPlayer(canvasPos.X, canvasPos.Y);
            GameCoordsText.Text = $"X: {gameX:F1}, Z: {gameZ:F1}";
            ScreenCoordsText.Text = $"X: {canvasPos.X:F0}, Y: {canvasPos.Y:F0}";
        }

        if (!_isDragging) return;

        var currentPt = e.GetPosition(MapViewerGrid);
        var deltaX = currentPt.X - _dragStartPoint.X;
        var deltaY = currentPt.Y - _dragStartPoint.Y;

        if (Math.Abs(deltaX) > 5 || Math.Abs(deltaY) > 5)
        {
            _wasDragging = true;
        }

        if (_wasDragging)
        {
            MapTranslate.X = _dragStartTranslateX + deltaX;
            MapTranslate.Y = _dragStartTranslateY + deltaY;
            MapCanvas.Cursor = Cursors.ScrollAll;
            RedrawPlayerMarker();
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

    #region Point Editing

    private void AddPoint(double screenX, double screenY)
    {
        if (_currentMapConfig == null || _viewModel.SelectedObjective == null) return;

        var (gameX, gameZ) = _currentMapConfig.ScreenToGameForPlayer(screenX, screenY);

        if (_viewModel.IsEditingLocationPoints)
        {
            _viewModel.AddLocationPoint(gameX, gameZ, _currentFloorId);
            StatusText.Text = $"Added area point: ({gameX:F1}, {gameZ:F1})";
        }
        else
        {
            _viewModel.AddOptionalPoint(gameX, gameZ, _currentFloorId);
            StatusText.Text = $"Added OR location: ({gameX:F1}, {gameZ:F1})";
        }

        RedrawEditingPoints();
    }

    private void RemoveLastPoint()
    {
        if (_viewModel.SelectedObjective == null) return;

        if (_viewModel.IsEditingLocationPoints)
        {
            _viewModel.RemoveLastLocationPoint();
            StatusText.Text = "Removed last area point";
        }
        else
        {
            _viewModel.RemoveLastOptionalPoint();
            StatusText.Text = "Removed last OR location";
        }

        RedrawEditingPoints();
    }

    #endregion

    #region UI Events

    private void ObjectivesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ObjectivesList.SelectedItem is QuestObjectiveItem obj)
        {
            _viewModel.SelectedObjective = obj;
            RedrawAllLayers();
            UpdateApproveButton();

            // Update selected info panel
            SelectedInfoPanel.Visibility = Visibility.Visible;
            NoSelectionText.Visibility = Visibility.Collapsed;
            SelectedQuestName.Text = obj.QuestName ?? obj.QuestNameEN ?? "(Unknown Quest)";
        }
        else
        {
            _viewModel.SelectedObjective = null;
            SelectedInfoPanel.Visibility = Visibility.Collapsed;
            NoSelectionText.Visibility = Visibility.Visible;
        }
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        if (FilterCombo.SelectedItem is ComboBoxItem item)
        {
            var content = item.Content?.ToString() ?? "";
            _viewModel.FilterMode = content switch
            {
                "Pending Approval" => ObjectiveFilterMode.PendingApproval,
                "Approved" => ObjectiveFilterMode.Approved,
                "Has Coordinates" => ObjectiveFilterMode.HasCoordinates,
                "No Coordinates" => ObjectiveFilterMode.NoCoordinates,
                _ => ObjectiveFilterMode.All
            };
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_viewModel == null) return;
        _viewModel.SearchText = SearchBox.Text;
    }

    private void ChkHideApproved_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null || ChkHideApproved == null) return;
        _viewModel.HideApproved = ChkHideApproved.IsChecked ?? false;
        UpdateProgressText();
    }

    private void ChkNearPlayer_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null || ChkNearPlayer == null) return;
        _viewModel.NearPlayerOnly = ChkNearPlayer.IsChecked ?? false;

        // Update player position in ViewModel
        if (_viewModel.NearPlayerOnly && _lastPlayerPosition != null)
        {
            _viewModel.PlayerPosition = _lastPlayerPosition;
        }
    }

    private void RadiusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null || RadiusCombo == null) return;
        if (RadiusCombo.SelectedItem is ComboBoxItem item)
        {
            var content = item.Content?.ToString() ?? "50m";
            var radiusStr = content.Replace("m", "");
            if (double.TryParse(radiusStr, out var radius))
            {
                _viewModel.NearPlayerRadius = radius;
            }
        }
    }

    private void LayerToggle_Changed(object sender, RoutedEventArgs e)
    {
        RedrawAllLayers();
    }

    private void EditModeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string mode)
        {
            _viewModel.IsEditingLocationPoints = mode == "Area";

            // Update button styles
            BtnAreaMode.Background = _viewModel.IsEditingLocationPoints
                ? new SolidColorBrush(Color.FromRgb(112, 168, 0))
                : new SolidColorBrush(Color.FromRgb(68, 68, 68));
            BtnAreaMode.Foreground = _viewModel.IsEditingLocationPoints
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(136, 136, 136));

            BtnOrMode.Background = _viewModel.IsEditingOptionalPoints
                ? new SolidColorBrush(Color.FromRgb(255, 87, 34))
                : new SolidColorBrush(Color.FromRgb(68, 68, 68));
            BtnOrMode.Foreground = _viewModel.IsEditingOptionalPoints
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(136, 136, 136));

            RedrawEditingPoints();

            var modeText = _viewModel.IsEditingLocationPoints ? "Area Points" : "OR Locations";
            StatusText.Text = $"Edit mode: {modeText}";
        }
    }

    private async void ApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is QuestObjectiveItem obj)
        {
            var isApproved = cb.IsChecked ?? false;
            obj.IsApproved = isApproved;
            await _viewModel.UpdateObjectiveApprovalAsync(obj.Id, isApproved);
            UpdateProgressText();

            // Reapply filter to update list
            _viewModel.ApplyFilter();

            // Redraw API markers (hide markers for fully approved quests)
            RedrawApiMarkers();
        }
    }

    private void ClearPoints_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedObjective == null) return;

        if (_viewModel.IsEditingLocationPoints)
        {
            _viewModel.ClearLocationPoints();
            StatusText.Text = "Cleared area points";
        }
        else
        {
            _viewModel.ClearOptionalPoints();
            StatusText.Text = "Cleared OR locations";
        }

        RedrawEditingPoints();
    }

    private async void SavePoints_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedObjective == null) return;

        await _viewModel.SaveLocationPointsAsync(_viewModel.SelectedObjective);
        await _viewModel.SaveOptionalPointsAsync(_viewModel.SelectedObjective);

        StatusText.Text = "Points saved successfully";
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedObjective == null) return;

        var objective = _viewModel.SelectedObjective;
        var newApproval = !objective.IsApproved;
        objective.IsApproved = newApproval;
        await _viewModel.UpdateObjectiveApprovalAsync(objective.Id, newApproval);

        UpdateProgressText();
        StatusText.Text = newApproval ? "Objective approved" : "Approval removed";

        // Reapply filter to update list (e.g., hide approved items if filter is active)
        // This may change SelectedObjective to null, so do it last
        _viewModel.ApplyFilter();

        // Update button after filter (SelectedObjective may have changed)
        UpdateApproveButton();

        // Redraw API markers (hide markers for fully approved quests)
        RedrawApiMarkers();
    }

    private void UpdateProgressText()
    {
        if (_viewModel == null || ProgressText == null) return;

        var approved = _viewModel.ApprovedCount;
        var total = _viewModel.TotalCount;
        var percent = total > 0 ? (double)approved / total * 100 : 0;
        ProgressText.Text = $"{approved} / {total} ({percent:F0}%)";
    }

    private void UpdateApproveButton()
    {
        if (_viewModel == null || BtnApprove == null) return;

        if (_viewModel.SelectedObjective == null)
        {
            BtnApprove.Content = "Approve";
            BtnApprove.Background = new SolidColorBrush(Color.FromRgb(112, 168, 0));
            return;
        }

        if (_viewModel.SelectedObjective.IsApproved)
        {
            BtnApprove.Content = "Unapprove";
            BtnApprove.Background = new SolidColorBrush(Color.FromRgb(198, 40, 40));
        }
        else
        {
            BtnApprove.Content = "Approve";
            BtnApprove.Background = new SolidColorBrush(Color.FromRgb(112, 168, 0));
        }
    }

    private void OpenWiki_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedObjective == null) return;

        var wikiUrl = _viewModel.SelectedObjective.WikiPageLink;
        if (string.IsNullOrEmpty(wikiUrl))
        {
            MessageBox.Show("No Wiki link available for this quest.", "Wiki Link", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(wikiUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open Wiki: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

            // Update ViewModel for Near Player filter
            if (_viewModel.NearPlayerOnly)
            {
                _viewModel.PlayerPosition = e.Position;
            }

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
        if (WatcherIndicator == null || WatcherToggleButton == null || PlayerPositionText == null) return;

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
        if (PlayerPositionText == null) return;

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
        if (_currentMapConfig == null || PlayerCanvas == null) return;

        PlayerCanvas.Children.Clear();

        // Convert game coords to map canvas coords
        var (mapX, mapY) = _currentMapConfig.GameToScreenForPlayer(position.X, position.Z);

        // Apply MapCanvas transform to get viewer coords
        double viewerX = mapX * _zoomLevel + MapTranslate.X;
        double viewerY = mapY * _zoomLevel + MapTranslate.Y;

        // Fixed size (independent of zoom level)
        double markerSize = 20;

        // Draw player circle (cyan)
        var playerCircle = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
            Stroke = Brushes.White,
            StrokeThickness = 2
        };

        Canvas.SetLeft(playerCircle, viewerX - markerSize / 2);
        Canvas.SetTop(playerCircle, viewerY - markerSize / 2);
        PlayerCanvas.Children.Add(playerCircle);

        // Draw direction arrow if angle is available
        if (position.Angle.HasValue)
        {
            double arrowLength = 30;
            double angleRad = (position.Angle.Value - 90) * Math.PI / 180.0; // -90 to point up at 0°

            var arrowLine = new Line
            {
                X1 = viewerX,
                Y1 = viewerY,
                X2 = viewerX + arrowLength * Math.Cos(angleRad),
                Y2 = viewerY + arrowLength * Math.Sin(angleRad),
                Stroke = Brushes.White,
                StrokeThickness = 3,
                StrokeEndLineCap = PenLineCap.Triangle
            };
            PlayerCanvas.Children.Add(arrowLine);
        }

        // Add player label
        var label = new TextBlock
        {
            Text = "YOU",
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),
            FontSize = 12,
            FontWeight = FontWeights.Bold
        };
        Canvas.SetLeft(label, viewerX + markerSize / 2 + 4);
        Canvas.SetTop(label, viewerY - 8);
        PlayerCanvas.Children.Add(label);
    }

    #endregion
}
