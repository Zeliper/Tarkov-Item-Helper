using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Data.Sqlite;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;

namespace TarkovDBEditor.Views;

/// <summary>
/// Map Preview Window - 읽기 전용으로 Marker와 Quest Objectives 표시
/// </summary>
public partial class MapPreviewWindow : Window
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

    // Map configuration
    private MapConfigList? _mapConfigs;
    private MapConfig? _currentMapConfig;

    // Floor management
    private string? _currentFloorId;
    private List<MapFloorConfig>? _sortedFloors;

    // Data
    private readonly List<MapMarker> _mapMarkers = new();
    private readonly List<QuestObjectiveItem> _questObjectives = new();
    private readonly List<ApiMarker> _apiMarkers = new();

    // Icon cache
    private static readonly Dictionary<MapMarkerType, BitmapImage?> _iconCache = new();

    // Screenshot watcher for player position
    private readonly ScreenshotWatcherService _watcherService = ScreenshotWatcherService.Instance;
    private readonly FloorLocationService _floorLocationService = FloorLocationService.Instance;

    // Global hotkey service
    private readonly GlobalHotkeyService _hotkeyService = GlobalHotkeyService.Instance;

    public MapPreviewWindow()
    {
        InitializeComponent();
        LoadMapConfigs();
        Loaded += MapPreviewWindow_Loaded;
        Closed += MapPreviewWindow_Closed;
        PreviewKeyDown += MapPreviewWindow_KeyDown;

        // Connect global hotkey service for floor switching when EFT is foreground
        _hotkeyService.FloorHotkeyPressed += OnFloorHotkeyPressed;
    }

    private async void MapPreviewWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();

        if (MapSelector.Items.Count > 0)
        {
            MapSelector.SelectedIndex = 0;
        }

        // Subscribe to watcher events
        _watcherService.PositionDetected += OnPositionDetected;
        _watcherService.StateChanged += OnWatcherStateChanged;
        UpdateWatcherStatus();

        // Start global hotkey hook for floor switching when EFT is foreground
        _hotkeyService.StartHook();
    }

    private void MapPreviewWindow_Closed(object? sender, EventArgs e)
    {
        // Unsubscribe from watcher events
        _watcherService.PositionDetected -= OnPositionDetected;
        _watcherService.StateChanged -= OnWatcherStateChanged;

        // Stop global hotkey hook
        _hotkeyService.FloorHotkeyPressed -= OnFloorHotkeyPressed;
        _hotkeyService.StopHook();
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

    private async Task LoadDataAsync()
    {
        // Load markers
        await LoadMarkersAsync();

        // Load quest objectives
        await LoadQuestObjectivesAsync();

        // Load API reference markers
        await LoadApiMarkersAsync();
    }

    private async Task LoadMarkersAsync()
    {
        _mapMarkers.Clear();

        try
        {
            var markers = await MapMarkerService.Instance.LoadAllMarkersAsync();
            foreach (var marker in markers)
            {
                _mapMarkers.Add(marker);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading markers: {ex.Message}";
        }
    }

    private async Task LoadQuestObjectivesAsync()
    {
        _questObjectives.Clear();

        if (!DatabaseService.Instance.IsConnected) return;

        try
        {
            var connectionString = $"Data Source={DatabaseService.Instance.DatabasePath}";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // Check if QuestObjectives table exists
            var checkSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='QuestObjectives'";
            await using var checkCmd = new SqliteCommand(checkSql, connection);
            var result = await checkCmd.ExecuteScalarAsync();
            if (result == null) return;

            // Migrate OptionalPoints column if not exists
            try
            {
                using var alterCmd = new SqliteCommand(
                    "ALTER TABLE QuestObjectives ADD COLUMN OptionalPoints TEXT",
                    connection);
                await alterCmd.ExecuteNonQueryAsync();
            }
            catch { /* Column already exists - ignore */ }

            // Load objectives with location points or optional points
            var sql = @"
                SELECT o.Id, o.QuestId, o.Description, o.MapName, o.LocationPoints, q.Location as QuestLocation, o.OptionalPoints
                FROM QuestObjectives o
                LEFT JOIN Quests q ON o.QuestId = q.Id
                WHERE (o.LocationPoints IS NOT NULL AND o.LocationPoints != '')
                   OR (o.OptionalPoints IS NOT NULL AND o.OptionalPoints != '')";

            await using var cmd = new SqliteCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var objective = new QuestObjectiveItem
                {
                    Id = reader.GetString(0),
                    QuestId = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    MapName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    QuestLocation = reader.IsDBNull(5) ? null : reader.GetString(5)
                };

                var locationJson = reader.IsDBNull(4) ? null : reader.GetString(4);
                objective.LocationPointsJson = locationJson;

                var optionalJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                System.Diagnostics.Debug.WriteLine($"[LoadQuestObjectivesAsync] Id={objective.Id}, optionalJson={optionalJson}");
                objective.OptionalPointsJson = optionalJson;
                System.Diagnostics.Debug.WriteLine($"[LoadQuestObjectivesAsync] After parse - OptionalPoints.Count={objective.OptionalPoints.Count}");

                if (objective.HasCoordinates || objective.HasOptionalPoints)
                {
                    _questObjectives.Add(objective);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading objectives: {ex.Message}";
        }
    }

    private async Task LoadApiMarkersAsync()
    {
        _apiMarkers.Clear();

        if (!DatabaseService.Instance.IsConnected) return;

        try
        {
            // Ensure the ApiMarkers table exists
            await ApiMarkerService.Instance.EnsureTableExistsAsync();

            // Get all map keys from configs
            if (_mapConfigs != null)
            {
                foreach (var map in _mapConfigs.Maps)
                {
                    var markers = await ApiMarkerService.Instance.GetByMapKeyAsync(map.Key);
                    foreach (var marker in markers)
                    {
                        _apiMarkers.Add(marker);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadApiMarkersAsync] Error: {ex.Message}");
        }
    }

    private void MapSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapSelector.SelectedItem is MapConfig config)
        {
            _currentMapConfig = config;
            UpdateFloorSelector(config);
            LoadMap(config);
            UpdateCounts();
            RedrawAll();
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
            TxtFloorLabel.Visibility = Visibility.Collapsed;
            FloorSelector.Visibility = Visibility.Collapsed;
            TxtFloorHotkeys.Visibility = Visibility.Collapsed;
            return;
        }

        TxtFloorLabel.Visibility = Visibility.Visible;
        FloorSelector.Visibility = Visibility.Visible;
        TxtFloorHotkeys.Visibility = Visibility.Visible;

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
                    RedrawAll();
                }
            }
        }
    }

    private void MapPreviewWindow_KeyDown(object sender, KeyEventArgs e)
    {
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

    /// <summary>
    /// Global hotkey handler for floor switching when EFT is in foreground
    /// </summary>
    private void OnFloorHotkeyPressed(object? sender, FloorHotkeyEventArgs e)
    {
        // Dispatch to UI thread
        Dispatcher.BeginInvoke(() =>
        {
            if (_sortedFloors == null || _sortedFloors.Count == 0)
                return;

            if (e.FloorIndex >= 0 && e.FloorIndex < _sortedFloors.Count)
            {
                FloorSelector.SelectedIndex = e.FloorIndex;
            }
        });
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

            // Floor filtering
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
                var preprocessor = new SvgStylePreprocessor();
                var processedSvg = preprocessor.ProcessSvgFile(svgPath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"map_preview_{Guid.NewGuid()}.svg");
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
                    RedrawAll();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                RedrawAll();
            }

            var floorInfo = !string.IsNullOrEmpty(_currentFloorId) ? $" [{_currentFloorId}]" : "";
            StatusText.Text = $"Loaded: {config.DisplayName}{floorInfo}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load map: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateCounts()
    {
        if (_currentMapConfig == null)
        {
            MarkerCountText.Text = "0";
            ObjectiveCountText.Text = "0";
            ApiMarkerCountText.Text = "0";
            return;
        }

        var markerCount = _mapMarkers.Count(m => m.MapKey == _currentMapConfig.Key);
        var objectiveCount = _questObjectives.Count(o => _currentMapConfig.MatchesMapName(o.EffectiveMapName));
        var apiMarkerCount = _apiMarkers.Count(m => string.Equals(m.MapKey, _currentMapConfig.Key, StringComparison.OrdinalIgnoreCase));

        MarkerCountText.Text = markerCount.ToString();
        ObjectiveCountText.Text = objectiveCount.ToString();
        ApiMarkerCountText.Text = apiMarkerCount.ToString();
    }

    private void RedrawAll()
    {
        // Skip if controls are not yet initialized
        if (!IsLoaded) return;

        if (ChkShowMarkers?.IsChecked == true)
        {
            RedrawMarkers();
        }
        else
        {
            MarkersCanvas?.Children.Clear();
        }

        if (ChkShowObjectives?.IsChecked == true)
        {
            RedrawObjectives();
        }
        else
        {
            ObjectivesCanvas?.Children.Clear();
        }

        if (ChkShowApiMarkers?.IsChecked == true)
        {
            RedrawApiMarkers();
        }
        else
        {
            ApiMarkersCanvas?.Children.Clear();
        }
    }

    private void LayerVisibility_Changed(object sender, RoutedEventArgs e)
    {
        RedrawAll();
    }

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
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Icons", iconFileName);

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
        if(MarkersCanvas == null) return;
        MarkersCanvas.Children.Clear();

        if (_currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var markersForMap = _mapMarkers.Where(m => m.MapKey == _currentMapConfig.Key).ToList();

        foreach (var marker in markersForMap)
        {
            var (sx, sy) = _currentMapConfig.GameToScreen(marker.X, marker.Z);

            // Determine opacity based on floor
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && marker.FloorId != null)
            {
                opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var (r, g, b) = MapMarker.GetMarkerColor(marker.MarkerType);
            var markerColor = Color.FromArgb((byte)(opacity * 255), r, g, b);

            var markerSize = 48 * inverseScale;
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

    #endregion

    #region Draw Objectives

    private void RedrawObjectives()
    {
        if (ObjectivesCanvas == null) return;
        ObjectivesCanvas.Children.Clear();

        if (_currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var objectivesForMap = _questObjectives
            .Where(o => _currentMapConfig.MatchesMapName(o.EffectiveMapName))
            .ToList();

        System.Diagnostics.Debug.WriteLine($"[RedrawObjectives] CurrentMap: Key={_currentMapConfig.Key}, Transform={string.Join(",", _currentMapConfig.CalibratedTransform ?? Array.Empty<double>())}");
        System.Diagnostics.Debug.WriteLine($"[RedrawObjectives] Found {objectivesForMap.Count} objectives for this map");

        foreach (var objective in objectivesForMap)
        {
            // Skip only if BOTH LocationPoints AND OptionalPoints are empty
            if (objective.LocationPoints.Count == 0 && objective.OptionalPoints.Count == 0) continue;

            System.Diagnostics.Debug.WriteLine($"[RedrawObjectives] Objective: {objective.Id}, EffectiveMapName={objective.EffectiveMapName}, LocationPoints={objective.LocationPoints.Count}, OptionalPoints={objective.OptionalPoints.Count}");
            foreach (var pt in objective.LocationPoints)
            {
                var (sx, sy) = _currentMapConfig.GameToScreen(pt.X, pt.Z);
                System.Diagnostics.Debug.WriteLine($"  Point: Game({pt.X:F2}, {pt.Z:F2}) -> Screen({sx:F2}, {sy:F2}), Floor={pt.FloorId}");
            }

            // Get points for the current floor
            var currentFloorPoints = objective.LocationPoints
                .Where(p => !hasFloors || _currentFloorId == null ||
                           p.FloorId == null ||
                           string.Equals(p.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Get points on other floors (for faded polygon drawing)
            var otherFloorPoints = hasFloors && _currentFloorId != null
                ? objective.LocationPoints
                    .Where(p => p.FloorId != null &&
                               !string.Equals(p.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<LocationPoint>();

            // Objective color (amber/yellow)
            var objectiveColor = Color.FromRgb(255, 193, 7); // FFC107
            var fadedObjectiveColor = Color.FromArgb(80, 255, 193, 7);

            // Draw faded polygon for other floors if 3+ points exist there
            if (otherFloorPoints.Count >= 3)
            {
                var fadedPolygon = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(20, 255, 193, 7)),
                    Stroke = new SolidColorBrush(fadedObjectiveColor),
                    StrokeThickness = 2 * inverseScale,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };

                foreach (var point in otherFloorPoints)
                {
                    var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
                    fadedPolygon.Points.Add(new Point(sx, sy));
                }

                ObjectivesCanvas.Children.Add(fadedPolygon);

                // Add floor label at centroid
                var centroidX = otherFloorPoints.Average(p => p.X);
                var centroidZ = otherFloorPoints.Average(p => p.Z);
                var (labelX, labelY) = _currentMapConfig.GameToScreen(centroidX, centroidZ);

                var otherFloorId = otherFloorPoints.First().FloorId;
                var floorDisplayName = _sortedFloors?
                    .FirstOrDefault(f => string.Equals(f.LayerId, otherFloorId, StringComparison.OrdinalIgnoreCase))
                    ?.DisplayName ?? otherFloorId;

                var floorLabel = new TextBlock
                {
                    Text = $"[{floorDisplayName}]",
                    Foreground = new SolidColorBrush(Color.FromArgb(120, 255, 193, 7)),
                    FontSize = 20 * inverseScale,
                    FontWeight = FontWeights.SemiBold,
                    FontStyle = FontStyles.Italic
                };

                floorLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(floorLabel, labelX - floorLabel.DesiredSize.Width / 2);
                Canvas.SetTop(floorLabel, labelY - floorLabel.DesiredSize.Height / 2);
                ObjectivesCanvas.Children.Add(floorLabel);
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
                    Stroke = new SolidColorBrush(fadedObjectiveColor),
                    StrokeThickness = 2 * inverseScale,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };

                ObjectivesCanvas.Children.Add(fadedLine);
            }

            // Draw polygon if 3+ points on current floor
            if (currentFloorPoints.Count >= 3)
            {
                var polygon = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(60, 255, 193, 7)),
                    Stroke = new SolidColorBrush(objectiveColor),
                    StrokeThickness = 2 * inverseScale,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };

                foreach (var point in currentFloorPoints)
                {
                    var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
                    polygon.Points.Add(new Point(sx, sy));
                }

                ObjectivesCanvas.Children.Add(polygon);

                // Add label at centroid
                var centroidX = currentFloorPoints.Average(p => p.X);
                var centroidZ = currentFloorPoints.Average(p => p.Z);
                var (labelX, labelY) = _currentMapConfig.GameToScreen(centroidX, centroidZ);

                AddObjectiveLabel(objective, labelX, labelY, inverseScale, objectiveColor);
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
                    Stroke = new SolidColorBrush(objectiveColor),
                    StrokeThickness = 3 * inverseScale,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };

                ObjectivesCanvas.Children.Add(line);

                // Add label at midpoint
                var midX = (sx1 + sx2) / 2;
                var midY = (sy1 + sy2) / 2;
                AddObjectiveLabel(objective, midX, midY, inverseScale, objectiveColor);
            }
            // Draw single point on current floor
            else if (currentFloorPoints.Count == 1)
            {
                var point = currentFloorPoints[0];
                var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
                var markerSize = 40 * inverseScale;

                // Diamond shape for objectives
                var diamond = new Polygon
                {
                    Fill = new SolidColorBrush(Color.FromArgb(200, 255, 193, 7)),
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                    StrokeThickness = 2 * inverseScale,
                    Points = new PointCollection
                    {
                        new Point(sx, sy - markerSize / 2),          // Top
                        new Point(sx + markerSize / 2, sy),          // Right
                        new Point(sx, sy + markerSize / 2),          // Bottom
                        new Point(sx - markerSize / 2, sy)           // Left
                    }
                };

                ObjectivesCanvas.Children.Add(diamond);

                // Exclamation mark
                var exclaim = new TextBlock
                {
                    Text = "!",
                    Foreground = Brushes.Black,
                    FontSize = 24 * inverseScale,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };

                exclaim.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(exclaim, sx - exclaim.DesiredSize.Width / 2);
                Canvas.SetTop(exclaim, sy - exclaim.DesiredSize.Height / 2);
                ObjectivesCanvas.Children.Add(exclaim);

                AddObjectiveLabel(objective, sx + markerSize / 2 + 8 * inverseScale, sy, inverseScale, objectiveColor);
            }

            // Draw faded individual points on other floors (only if less than 3 points on that floor)
            // This shows single points or endpoints of lines on other floors
            if (otherFloorPoints.Count > 0 && otherFloorPoints.Count < 3)
            {
                foreach (var point in otherFloorPoints)
                {
                    var (sx, sy) = _currentMapConfig.GameToScreen(point.X, point.Z);
                    var fadedSize = 24 * inverseScale;

                    var fadedMarker = new Ellipse
                    {
                        Width = fadedSize,
                        Height = fadedSize,
                        Fill = new SolidColorBrush(Color.FromArgb(80, 255, 193, 7)),
                        Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 193, 7)),
                        StrokeThickness = 1 * inverseScale
                    };

                    Canvas.SetLeft(fadedMarker, sx - fadedSize / 2);
                    Canvas.SetTop(fadedMarker, sy - fadedSize / 2);
                    ObjectivesCanvas.Children.Add(fadedMarker);

                    // Floor label
                    var floorDisplayName = _sortedFloors?
                        .FirstOrDefault(f => string.Equals(f.LayerId, point.FloorId, StringComparison.OrdinalIgnoreCase))
                        ?.DisplayName ?? point.FloorId;

                    var floorLabel = new TextBlock
                    {
                        Text = $"[{floorDisplayName}]",
                        Foreground = new SolidColorBrush(Color.FromArgb(100, 255, 193, 7)),
                        FontSize = 16 * inverseScale,
                        FontStyle = FontStyles.Italic
                    };

                    Canvas.SetLeft(floorLabel, sx + fadedSize / 2 + 4 * inverseScale);
                    Canvas.SetTop(floorLabel, sy - 8 * inverseScale);
                    ObjectivesCanvas.Children.Add(floorLabel);
                }
            }

            // Draw Optional Points (OR locations) - orange markers
            System.Diagnostics.Debug.WriteLine($"[RedrawObjectives] Objective {objective.Id}: OptionalPoints.Count = {objective.OptionalPoints.Count}");
            if (objective.OptionalPoints.Count > 0)
            {
                var optMarkerSize = 40 * inverseScale;
                var optIndex = 1;
                foreach (var point in objective.OptionalPoints)
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
                        StrokeThickness = 3 * inverseScale
                    };

                    Canvas.SetLeft(optEllipse, sx - optMarkerSize / 2);
                    Canvas.SetTop(optEllipse, sy - optMarkerSize / 2);
                    ObjectivesCanvas.Children.Add(optEllipse);

                    // "OR" prefix label
                    var orLabel = new TextBlock
                    {
                        Text = $"OR{optIndex}",
                        Foreground = new SolidColorBrush(Color.FromArgb((byte)(optOpacity * 255), 255, 255, 255)),
                        FontSize = 28 * inverseScale,
                        FontWeight = FontWeights.Bold
                    };

                    Canvas.SetLeft(orLabel, sx + optMarkerSize / 2 + 8 * inverseScale);
                    Canvas.SetTop(orLabel, sy - optMarkerSize / 2);
                    ObjectivesCanvas.Children.Add(orLabel);

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
                            FontSize = 20 * inverseScale,
                            FontStyle = FontStyles.Italic
                        };

                        Canvas.SetLeft(floorLabel, sx + optMarkerSize / 2 + 8 * inverseScale);
                        Canvas.SetTop(floorLabel, sy + optMarkerSize / 2);
                        ObjectivesCanvas.Children.Add(floorLabel);
                    }

                    optIndex++;
                }
            }
        }
    }

    private void AddObjectiveLabel(QuestObjectiveItem objective, double x, double y, double inverseScale, Color color)
    {
        var description = objective.Description.Length > 50
            ? objective.Description.Substring(0, 47) + "..."
            : objective.Description;

        var label = new TextBlock
        {
            Text = description,
            Foreground = new SolidColorBrush(color),
            FontSize = 22 * inverseScale,
            FontWeight = FontWeights.Medium,
            MaxWidth = 300 * inverseScale,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        // Add background for readability
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 30, 30, 30)),
            CornerRadius = new CornerRadius(3 * inverseScale),
            Padding = new Thickness(4 * inverseScale, 2 * inverseScale, 4 * inverseScale, 2 * inverseScale),
            Child = label
        };

        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y - 12 * inverseScale);
        ObjectivesCanvas.Children.Add(border);
    }

    #endregion

    #region Draw API Reference Markers

    private void RedrawApiMarkers()
    {
        if (ApiMarkersCanvas == null) return;
        ApiMarkersCanvas.Children.Clear();

        if (_currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        // API 마커 색상 (주황색)
        var apiMarkerColor = Color.FromRgb(230, 81, 0); // #E65100

        var apiMarkersForMap = _apiMarkers
            .Where(m => string.Equals(m.MapKey, _currentMapConfig.Key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var marker in apiMarkersForMap)
        {
            // API 마커의 geometry.y는 gameX, geometry.x는 gameZ에 대응
            // 저장 시: marker.X = geometry.x (gameZ), marker.Z = geometry.y (gameX)
            // 따라서 GameToScreenForPlayer(gameX, gameZ) = GameToScreenForPlayer(marker.Z, marker.X)
            var (sx, sy) = _currentMapConfig.GameToScreenForPlayer(marker.Z, marker.X);

            // Determine opacity based on floor
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && marker.FloorId != null)
            {
                opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
            }

            var markerSize = 36 * inverseScale;

            // Pentagon shape for API markers (to distinguish from other markers)
            var pentagon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), apiMarkerColor.R, apiMarkerColor.G, apiMarkerColor.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                StrokeThickness = 2 * inverseScale
            };

            // Create pentagon points
            var radius = markerSize / 2;
            for (int i = 0; i < 5; i++)
            {
                var angle = Math.PI / 2 + (2 * Math.PI * i / 5); // Start from top
                var px = sx + radius * Math.Cos(angle);
                var py = sy - radius * Math.Sin(angle);
                pentagon.Points.Add(new Point(px, py));
            }

            ApiMarkersCanvas.Children.Add(pentagon);

            // "API" text inside marker
            var apiLabel = new TextBlock
            {
                Text = "A",
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                FontSize = 16 * inverseScale,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center
            };

            apiLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(apiLabel, sx - apiLabel.DesiredSize.Width / 2);
            Canvas.SetTop(apiLabel, sy - apiLabel.DesiredSize.Height / 2);
            ApiMarkersCanvas.Children.Add(apiLabel);

            // Name label
            var displayName = !string.IsNullOrEmpty(marker.NameKo) ? marker.NameKo : marker.Name;
            if (displayName.Length > 30)
                displayName = displayName.Substring(0, 27) + "...";

            var nameLabel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 180), 30, 30, 30)),
                CornerRadius = new CornerRadius(3 * inverseScale),
                Padding = new Thickness(4 * inverseScale, 2 * inverseScale, 4 * inverseScale, 2 * inverseScale),
                Child = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Children =
                    {
                        new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), apiMarkerColor.R, apiMarkerColor.G, apiMarkerColor.B)),
                            CornerRadius = new CornerRadius(2 * inverseScale),
                            Padding = new Thickness(3 * inverseScale, 1 * inverseScale, 3 * inverseScale, 1 * inverseScale),
                            Margin = new Thickness(0, 0, 4 * inverseScale, 0),
                            Child = new TextBlock
                            {
                                Text = "API",
                                Foreground = Brushes.White,
                                FontSize = 10 * inverseScale,
                                FontWeight = FontWeights.Bold
                            }
                        },
                        new TextBlock
                        {
                            Text = displayName,
                            Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), apiMarkerColor.R, apiMarkerColor.G, apiMarkerColor.B)),
                            FontSize = 20 * inverseScale,
                            FontWeight = FontWeights.Medium
                        }
                    }
                }
            };

            Canvas.SetLeft(nameLabel, sx + markerSize / 2 + 8 * inverseScale);
            Canvas.SetTop(nameLabel, sy - 12 * inverseScale);
            ApiMarkersCanvas.Children.Add(nameLabel);

            // Category label (below name)
            var categoryDisplay = !string.IsNullOrEmpty(marker.SubCategory)
                ? $"{marker.Category} > {marker.SubCategory}"
                : marker.Category;

            if (categoryDisplay.Length > 40)
                categoryDisplay = categoryDisplay.Substring(0, 37) + "...";

            var categoryLabel = new TextBlock
            {
                Text = categoryDisplay,
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 180), 200, 200, 200)),
                FontSize = 16 * inverseScale,
                FontStyle = FontStyles.Italic
            };

            Canvas.SetLeft(categoryLabel, sx + markerSize / 2 + 8 * inverseScale);
            Canvas.SetTop(categoryLabel, sy + 14 * inverseScale);
            ApiMarkersCanvas.Children.Add(categoryLabel);

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
                    FontSize = 18 * inverseScale,
                    FontStyle = FontStyles.Italic
                };

                Canvas.SetLeft(floorLabel, sx + markerSize / 2 + 8 * inverseScale);
                Canvas.SetTop(floorLabel, sy + 32 * inverseScale);
                ApiMarkersCanvas.Children.Add(floorLabel);
            }
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

        var zoomPercent = $"{_zoomLevel * 100:F0}%";
        ZoomText.Text = zoomPercent;

        RedrawAll();
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
        _dragStartPoint = e.GetPosition(MapViewerGrid);
        _dragStartTranslateX = MapTranslate.X;
        _dragStartTranslateY = MapTranslate.Y;
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
            var (gameX, gameZ) = _currentMapConfig.ScreenToGame(canvasPos.X, canvasPos.Y);
            GameCoordsText.Text = $"X: {gameX:F1}, Z: {gameZ:F1}";
        }

        if (!_isDragging) return;

        var currentPt = e.GetPosition(MapViewerGrid);
        var deltaX = currentPt.X - _dragStartPoint.X;
        var deltaY = currentPt.Y - _dragStartPoint.Y;

        MapTranslate.X = _dragStartTranslateX + deltaX;
        MapTranslate.Y = _dragStartTranslateY + deltaY;
        MapCanvas.Cursor = Cursors.ScrollAll;
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

    #region Player Position Display

    private void OnPositionDetected(object? sender, PositionDetectedEventArgs e)
    {
        Dispatcher.Invoke(async () =>
        {
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

        // Convert game coords to screen coords
        var (screenX, screenY) = _currentMapConfig.GameToScreen(position.X, position.Z);

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
            double headAngle1 = angleRad + 2.5;
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
