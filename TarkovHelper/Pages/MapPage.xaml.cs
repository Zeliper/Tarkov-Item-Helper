using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TarkovHelper.Models.MapTracker;
using TarkovHelper.Services;
using TarkovHelper.Services.MapTracker;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - 맵 뷰어 및 마커 표시
/// </summary>
public partial class MapPage : UserControl
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
    private DbMapConfig? _currentMapConfig;

    // Floor management
    private string? _currentFloorId;
    private List<DbMapFloorConfig>? _sortedFloors;

    // Data services
    private readonly MapMarkerDbService _dbService;
    private readonly QuestProgressService _progressService = QuestProgressService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    // Quest marker state
    private List<TaskObjectiveWithLocation> _currentMapQuestObjectives = new();
    private bool _showQuestMarkers = true;
    private HashSet<string> _hiddenQuestIds = new(); // 숨긴 퀘스트 ID

    // Quest Drawer state
    private bool _isDrawerOpen;
    private double _drawerWidth = 320;
    private const double DrawerMinWidth = 250;
    private const double DrawerMaxWidth = 500;
    private string? _highlightedQuestId; // 하이라이트된 퀘스트
    private HashSet<string> _collapsedQuestIds = new(); // 접힌 퀘스트 ID
    private string _searchText = ""; // 검색어
    private string _sortOption = "name"; // 정렬 옵션: name, progress, count

    // Marker scale
    private double _markerScale = 1.0;

    // Quest type filters
    private HashSet<string> _enabledQuestTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "visit", "mark", "plantitem", "extract",
        "finditem", "findquestitem", "giveitem",
        "kill", "shoot", "other"
    };

    // 마커-Drawer 연계
    private Dictionary<string, List<FrameworkElement>> _markersByObjectiveId = new(); // 목표ID → 마커 UI 요소
    private string? _hoveredObjectiveId; // 호버 중인 목표 ID

    // Icon cache
    private static readonly Dictionary<MapMarkerType, BitmapImage?> _iconCache = new();

    // 현재 마우스 위치 (게임 좌표)
    private double _currentGameX;
    private double _currentGameZ;
    private bool _hasValidCoordinates;

    // Map Tracker related
    private readonly MapTrackerService _trackerService;
    private readonly LogMapWatcherService _logMapWatcher = LogMapWatcherService.Instance;
    private Ellipse? _playerMarkerCircle;
    private Polygon? _playerMarkerArrow;
    private readonly List<Ellipse> _trailMarkers = new();
    private bool _showPlayerMarker = true;
    private bool _showTrail = true;

    // Minimap state
    private bool _isMinimapExpanded = true;
    private RenderTargetBitmap? _minimapBitmap;
    private double _minimapScale = 1.0;
    private Size _mapSize = Size.Empty;
    private bool _isMinimapDragging = false;

    public MapPage()
    {
        InitializeComponent();
        _dbService = MapMarkerDbService.Instance;
        _trackerService = MapTrackerService.Instance;

        // Connect tracker events
        _trackerService.PositionUpdated += OnPositionUpdated;
        _trackerService.WatchingStateChanged += OnWatchingStateChanged;
        _trackerService.StatusMessage += OnTrackerStatusMessage;

        // Connect log map watcher for auto map switch
        _logMapWatcher.MapChanged += OnLogMapChanged;

        // Subscribe to language changes
        _loc.LanguageChanged += OnLanguageChanged;

        Loaded += MapPage_Loaded;
        Unloaded += MapPage_Unloaded;
    }

    private async void MapPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Load map configs and markers from DB
        _dbService.LoadMapConfigs();
        await _dbService.LoadMarkersAsync();

        // Load quest objectives data
        await LoadQuestDataAsync();

        // Load settings from DB
        LoadSettingsFromDb();

        LoadMapSelector();

        // Try to restore last selected map, or use first map
        var lastMap = _settings.MapLastSelectedMap;
        var selectedIndex = 0;
        if (!string.IsNullOrEmpty(lastMap))
        {
            for (int i = 0; i < MapSelector.Items.Count; i++)
            {
                if (MapSelector.Items[i] is DbMapConfig config && config.Key == lastMap)
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        if (MapSelector.Items.Count > 0)
        {
            MapSelector.SelectedIndex = selectedIndex;
        }

        // Apply drawer state after map is loaded
        if (_settings.MapDrawerOpen)
        {
            OpenDrawer();
        }

        // Apply localization
        UpdateLocalization();
    }

    /// <summary>
    /// Load settings from SettingsService and apply to UI
    /// </summary>
    private void LoadSettingsFromDb()
    {
        // Load cached settings
        _hiddenQuestIds = _settings.MapHiddenQuests;
        _collapsedQuestIds = _settings.MapCollapsedQuests;
        _drawerWidth = _settings.MapDrawerWidth;
        _sortOption = _settings.MapSortOption;

        // Apply checkbox states
        if (ChkShowMarkers != null)
            ChkShowMarkers.IsChecked = _settings.MapShowExtracts;
        if (ChkShowTransit != null)
            ChkShowTransit.IsChecked = _settings.MapShowTransits;
        if (ChkShowQuests != null)
            ChkShowQuests.IsChecked = _settings.MapShowQuests;
        if (ChkIncompleteOnly != null)
            ChkIncompleteOnly.IsChecked = _settings.MapIncompleteOnly;
        if (ChkCurrentMapOnly != null)
            ChkCurrentMapOnly.IsChecked = _settings.MapCurrentMapOnly;

        // Apply marker scale
        _markerScale = _settings.MapMarkerScale;
        if (MarkerScaleSlider != null)
        {
            MarkerScaleSlider.Value = _markerScale;
            if (MarkerScaleText != null)
                MarkerScaleText.Text = $"{_markerScale:F1}x";
        }

        System.Diagnostics.Debug.WriteLine($"[MapPage] Settings loaded - DrawerOpen: {_settings.MapDrawerOpen}, HiddenQuests: {_hiddenQuestIds.Count}, MarkerScale: {_markerScale}");
    }

    private void LoadMapSelector()
    {
        MapSelector.Items.Clear();

        // Get all available maps from map_configs.json via DbMapConfig
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var configPath = System.IO.Path.Combine(appDir, "Assets", "DB", "Data", "map_configs.json");

        if (!File.Exists(configPath))
        {
            StatusText.Text = "Warning: map_configs.json not found";
            return;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var configList = System.Text.Json.JsonSerializer.Deserialize<DbMapConfigList>(json, options);

            if (configList?.Maps != null)
            {
                foreach (var map in configList.Maps)
                {
                    MapSelector.Items.Add(map);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error loading map configs: {ex.Message}";
        }
    }

    private void MapSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MapSelector.SelectedItem is DbMapConfig config)
        {
            _currentMapConfig = config;

            // Debug: Show loaded transform values
            System.Diagnostics.Debug.WriteLine($"[MapPage] Map selected: {config.Key}");
            System.Diagnostics.Debug.WriteLine($"[MapPage] Map size: {config.ImageWidth}x{config.ImageHeight}");
            if (config.PlayerMarkerTransform != null)
            {
                System.Diagnostics.Debug.WriteLine($"[MapPage] PlayerMarkerTransform loaded: [{string.Join(", ", config.PlayerMarkerTransform)}]");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MapPage] PlayerMarkerTransform is NULL!");
            }
            if (config.CalibratedTransform != null)
            {
                System.Diagnostics.Debug.WriteLine($"[MapPage] CalibratedTransform loaded: [{string.Join(", ", config.CalibratedTransform)}]");
            }

            UpdateFloorSelector(config);
            LoadMap(config);
            UpdateCounts();
            RedrawAll();
            UpdateStatusBarMapInfo();

            // Save last selected map
            _settings.MapLastSelectedMap = config.Key;

            // Drawer가 열려있으면 새로고침
            if (_isDrawerOpen)
            {
                RefreshQuestDrawer();
            }
        }
    }

    #region Floor Management

    private void UpdateFloorSelector(DbMapConfig config)
    {
        FloorSelector.Items.Clear();
        _currentFloorId = null;
        _sortedFloors = null;

        var floors = config.Floors;
        if (floors == null || floors.Count == 0)
        {
            FloorSelectorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        FloorSelectorPanel.Visibility = Visibility.Visible;

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

                UpdateStatusBarMapInfo();
            }
        }
    }

    #endregion

    private void LoadMap(DbMapConfig config, bool resetView = true)
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var svgPath = System.IO.Path.Combine(appDir, "Assets", "DB", "Maps", config.SvgFileName);

            // Fallback to Assets/Maps if not found in DB/Maps
            if (!File.Exists(svgPath))
            {
                svgPath = System.IO.Path.Combine(appDir, "Assets", "Maps", config.SvgFileName);
            }

            if (!File.Exists(svgPath))
            {
                StatusText.Text = $"Map file not found: {config.SvgFileName}";
                return;
            }

            // Floor filtering using SvgStylePreprocessor
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

            // Load SVG with floor filtering
            if (visibleFloors != null)
            {
                var preprocessor = new SvgStylePreprocessor();
                var processedSvg = preprocessor.ProcessSvgFile(svgPath, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"testmap_preview_{Guid.NewGuid()}.svg");
                File.WriteAllText(tempPath, processedSvg);
                MapSvg.Source = new Uri(tempPath, UriKind.Absolute);
            }
            else
            {
                MapSvg.Source = new Uri(svgPath, UriKind.Absolute);
            }

            // CRITICAL: Set explicit dimensions from config
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
                    UpdateMinimap();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                RedrawAll();
                UpdateMinimap();
            }

            var floorInfo = !string.IsNullOrEmpty(_currentFloorId) ? $" [{_currentFloorId}]" : "";
            StatusText.Text = $"Loaded: {config.DisplayName}{floorInfo} ({config.ImageWidth}x{config.ImageHeight})";
            UpdateStatusBarMapInfo();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load map: {ex.Message}";
        }
    }

    private void UpdateCounts()
    {
        if (_currentMapConfig == null)
        {
            MarkerCountText.Text = "0";
            TransitCountText.Text = "0";
            return;
        }

        var markers = _dbService.GetMarkersForMap(_currentMapConfig.Key);
        var extractCount = markers.Count(m => m.IsExtraction);
        var transitCount = markers.Count(m => m.IsTransit);

        MarkerCountText.Text = extractCount.ToString();
        TransitCountText.Text = transitCount.ToString();
    }

    private void RedrawAll()
    {
        if (!IsLoaded) return;

        if (ChkShowMarkers?.IsChecked == true)
        {
            RedrawMarkers();
        }
        else
        {
            MarkersCanvas?.Children.Clear();
        }

        if (ChkShowTransit?.IsChecked == true)
        {
            RedrawTransit();
        }
        else
        {
            TransitCanvas?.Children.Clear();
        }

        // Quest markers
        _showQuestMarkers = ChkShowQuests?.IsChecked == true;
        if (_showQuestMarkers)
        {
            RefreshQuestMarkers();
        }
        else
        {
            QuestMarkersCanvas?.Children.Clear();
            QuestMarkerCountText.Text = "0";
        }
    }

    private void LayerVisibility_Changed(object sender, RoutedEventArgs e)
    {
        // Save checkbox states to settings
        if (sender is CheckBox checkBox)
        {
            var isChecked = checkBox.IsChecked == true;
            switch (checkBox.Name)
            {
                case "ChkShowMarkers":
                    _settings.MapShowExtracts = isChecked;
                    break;
                case "ChkShowTransit":
                    _settings.MapShowTransits = isChecked;
                    break;
                case "ChkShowQuests":
                    _settings.MapShowQuests = isChecked;
                    break;
            }
        }

        RedrawAll();
    }

    private void MarkerScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _markerScale = e.NewValue;

        // Update display text
        if (MarkerScaleText != null)
            MarkerScaleText.Text = $"{_markerScale:F1}x";

        // Save to settings
        _settings.MapMarkerScale = _markerScale;

        // Redraw all markers with new scale
        RedrawAll();
    }

}
