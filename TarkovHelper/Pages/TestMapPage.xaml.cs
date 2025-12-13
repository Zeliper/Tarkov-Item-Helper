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
/// Test Map Page - DB Editor 방식으로 마커 표시
/// </summary>
public partial class TestMapPage : UserControl
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
    private Ellipse? _playerMarker;
    private readonly List<Ellipse> _trailMarkers = new();
    private bool _showPlayerMarker = true;
    private bool _showTrail = true;

    public TestMapPage()
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

        Loaded += TestMapPage_Loaded;
        Unloaded += TestMapPage_Unloaded;
    }

    private async void TestMapPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Load map configs and markers from DB
        _dbService.LoadMapConfigs();
        await _dbService.LoadMarkersAsync();

        // Load quest objectives data
        await LoadQuestDataAsync();

        LoadMapSelector();

        if (MapSelector.Items.Count > 0)
        {
            MapSelector.SelectedIndex = 0;
        }
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
            UpdateFloorSelector(config);
            LoadMap(config);
            UpdateCounts();
            RedrawAll();
            UpdateStatusBarMapInfo();

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
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                RedrawAll();
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

            var markerSize = 40 * inverseScale;
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

    #region Quest Data

    /// <summary>
    /// 퀘스트 목표 데이터 로드 (DB에서)
    /// </summary>
    private async Task LoadQuestDataAsync()
    {
        try
        {
            StatusText.Text = "Loading quest objectives from DB...";

            await _dbService.LoadQuestObjectivesAsync();

            StatusText.Text = $"Loaded {_dbService.TotalObjectiveCount} quest objectives from DB";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Quest data load failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[TestMapPage] Quest data load error: {ex}");
        }
    }

    /// <summary>
    /// 현재 맵의 퀘스트 목표 업데이트
    /// </summary>
    private void UpdateCurrentMapQuestObjectives()
    {
        _currentMapQuestObjectives.Clear();

        if (_currentMapConfig == null || !_dbService.ObjectivesLoaded) return;

        // 맵 키로 필터링 (DbMapConfig.Key 사용)
        var mapKey = _currentMapConfig.Key;

        // DB에서 퀘스트 목표 가져와서 변환
        var dbObjectives = _dbService.GetObjectivesForMap(mapKey);
        _currentMapQuestObjectives = dbObjectives.Select(ConvertToTaskObjective).ToList();

        System.Diagnostics.Debug.WriteLine($"[TestMapPage] Map '{mapKey}': {_currentMapQuestObjectives.Count} quest objectives from DB");
    }

    /// <summary>
    /// DbQuestObjective를 TaskObjectiveWithLocation으로 변환
    /// </summary>
    private TaskObjectiveWithLocation ConvertToTaskObjective(DbQuestObjective dbObj)
    {
        var result = new TaskObjectiveWithLocation
        {
            ObjectiveId = dbObj.Id,
            Description = dbObj.Description,
            Type = "visit", // DB에서는 타입 정보가 없으므로 기본값
            TaskNormalizedName = dbObj.QuestId,
            TaskName = dbObj.QuestName ?? dbObj.QuestId,
            TaskNameKo = dbObj.QuestNameKo,
            Locations = new List<QuestObjectiveLocation>()
        };

        // LocationPoints를 QuestObjectiveLocation으로 변환
        // DB 좌표: X=수평X, Y=높이, Z=수평깊이
        foreach (var pt in dbObj.LocationPoints)
        {
            result.Locations.Add(new QuestObjectiveLocation
            {
                Id = $"{dbObj.Id}_{pt.X}_{pt.Z}",
                MapName = dbObj.EffectiveMapName ?? "",
                X = pt.X,
                Y = pt.Y,  // 높이
                Z = pt.Z   // 수평 깊이 (GameToScreen의 두 번째 파라미터)
            });
        }

        // OptionalPoints도 Locations에 추가 (별도 표시가 필요하면 나중에 분리)
        foreach (var pt in dbObj.OptionalPoints)
        {
            result.Locations.Add(new QuestObjectiveLocation
            {
                Id = $"{dbObj.Id}_opt_{pt.X}_{pt.Z}",
                MapName = dbObj.EffectiveMapName ?? "",
                X = pt.X,
                Y = pt.Y,
                Z = pt.Z
            });
        }

        return result;
    }

    /// <summary>
    /// 퀘스트 마커 다시 그리기
    /// </summary>
    private void RefreshQuestMarkers()
    {
        if (QuestMarkersCanvas == null) return;
        QuestMarkersCanvas.Children.Clear();
        _markersByObjectiveId.Clear(); // 마커 매핑 초기화

        if (!_showQuestMarkers || _currentMapConfig == null) return;

        UpdateCurrentMapQuestObjectives();

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var visibleCount = 0;
        foreach (var objective in _currentMapQuestObjectives)
        {
            // 숨긴 퀘스트 필터링
            if (_hiddenQuestIds.Contains(objective.TaskNormalizedName))
                continue;

            // 현재 맵의 위치만 필터링
            var locationsForCurrentMap = objective.Locations
                .Where(loc => IsLocationOnCurrentMap(loc))
                .ToList();

            if (locationsForCurrentMap.Count == 0) continue;

            // 완료 여부 확인 (목표별)
            var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);
            objective.IsCompleted = isCompleted;

            // 목표 타입별 색상 (완료된 경우 흐리게)
            var objectiveColor = GetQuestTypeColor(objective.Type);
            var opacity = isCompleted ? 0.4 : 1.0;

            // Multi-point 렌더링 (TarkovDBEditor 방식)
            RenderQuestObjectiveArea(objective, locationsForCurrentMap, objectiveColor, inverseScale, hasFloors, opacity);
            visibleCount++;
        }

        // 카운트 업데이트 (표시 중인 퀘스트만)
        QuestMarkerCountText.Text = visibleCount.ToString();
    }

    /// <summary>
    /// 위치가 현재 맵에 있는지 확인
    /// </summary>
    private bool IsLocationOnCurrentMap(QuestObjectiveLocation location)
    {
        if (_currentMapConfig == null) return false;

        var mapKey = _currentMapConfig.Key.ToLowerInvariant();
        var locationMapName = location.MapNormalizedName?.ToLowerInvariant() ?? "";
        var locationMapNameAlt = location.MapName?.ToLowerInvariant() ?? "";

        return locationMapName == mapKey || locationMapNameAlt == mapKey;
    }

    /// <summary>
    /// 퀘스트 목표 영역 렌더링 (Multi-point 지원)
    /// </summary>
    private void RenderQuestObjectiveArea(
        TaskObjectiveWithLocation objective,
        List<QuestObjectiveLocation> locations,
        Color objectiveColor,
        double inverseScale,
        bool hasFloors,
        double opacity = 1.0)
    {
        // API에서는 층 정보를 제공하지 않으므로 모든 포인트를 사용
        var points = locations;

        // 마커 리스트 초기화
        if (!_markersByObjectiveId.ContainsKey(objective.ObjectiveId))
            _markersByObjectiveId[objective.ObjectiveId] = new List<FrameworkElement>();

        // 1. 3개 이상: Polygon (채워진 영역)
        if (points.Count >= 3)
        {
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb((byte)(60 * opacity), objectiveColor.R, objectiveColor.G, objectiveColor.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), objectiveColor.R, objectiveColor.G, objectiveColor.B)),
                StrokeThickness = 2 * inverseScale,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Tag = objective,
                Cursor = Cursors.Hand,
                ToolTip = CreateEnhancedTooltip(objective),
                ContextMenu = CreateMarkerContextMenu(objective)
            };
            polygon.MouseLeftButtonDown += QuestMarker_Click;
            polygon.MouseEnter += QuestMarker_MouseEnter;
            polygon.MouseLeave += QuestMarker_MouseLeave;
            polygon.MouseRightButtonDown += QuestMarker_RightClick;

            foreach (var point in points)
            {
                var screenCoords = _currentMapConfig!.GameToScreen(point.X, point.Z ?? 0);
                if (screenCoords == null) continue;
                polygon.Points.Add(new Point(screenCoords.Value.screenX, screenCoords.Value.screenY));
            }

            if (polygon.Points.Count >= 3)
            {
                QuestMarkersCanvas.Children.Add(polygon);
                _markersByObjectiveId[objective.ObjectiveId].Add(polygon);

                // Centroid에 라벨 추가
                AddAreaLabel(objective, points, objectiveColor, inverseScale, opacity);

                // 완료된 경우 체크마크 오버레이 추가
                if (objective.IsCompleted)
                {
                    var centroid = GetCentroid(points);
                    if (centroid != null)
                        AddCompletionCheckmark(centroid.Value.screenX, centroid.Value.screenY, inverseScale);
                }
            }
        }
        // 2. 2개: Line
        else if (points.Count == 2)
        {
            var p1 = _currentMapConfig!.GameToScreen(points[0].X, points[0].Z ?? 0);
            var p2 = _currentMapConfig.GameToScreen(points[1].X, points[1].Z ?? 0);

            if (p1 != null && p2 != null)
            {
                var line = new Line
                {
                    X1 = p1.Value.screenX, Y1 = p1.Value.screenY,
                    X2 = p2.Value.screenX, Y2 = p2.Value.screenY,
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), objectiveColor.R, objectiveColor.G, objectiveColor.B)),
                    StrokeThickness = 3 * inverseScale,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Tag = objective,
                    Cursor = Cursors.Hand,
                    ToolTip = CreateEnhancedTooltip(objective),
                    ContextMenu = CreateMarkerContextMenu(objective)
                };
                line.MouseLeftButtonDown += QuestMarker_Click;
                line.MouseEnter += QuestMarker_MouseEnter;
                line.MouseLeave += QuestMarker_MouseLeave;
                line.MouseRightButtonDown += QuestMarker_RightClick;

                QuestMarkersCanvas.Children.Add(line);
                _markersByObjectiveId[objective.ObjectiveId].Add(line);

                // 중간점에 라벨 추가
                var midX = (p1.Value.screenX + p2.Value.screenX) / 2;
                var midY = (p1.Value.screenY + p2.Value.screenY) / 2;
                AddQuestLabel(objective, midX, midY, objectiveColor, inverseScale, opacity);

                // 완료된 경우 체크마크 오버레이 추가
                if (objective.IsCompleted)
                {
                    AddCompletionCheckmark(midX, midY, inverseScale);
                }
            }
        }
        // 3. 1개: Diamond Marker
        else if (points.Count == 1)
        {
            var screenCoords = _currentMapConfig!.GameToScreen(points[0].X, points[0].Z ?? 0);
            if (screenCoords != null)
            {
                var marker = CreateDiamondMarker(screenCoords.Value.screenX, screenCoords.Value.screenY, objectiveColor, inverseScale, opacity, objective);
                QuestMarkersCanvas.Children.Add(marker);
                AddQuestLabel(objective, screenCoords.Value.screenX, screenCoords.Value.screenY, objectiveColor, inverseScale, opacity);

                // 완료된 경우 체크마크 오버레이 추가
                if (objective.IsCompleted)
                {
                    AddCompletionCheckmark(screenCoords.Value.screenX, screenCoords.Value.screenY, inverseScale);
                }
            }
        }
    }

    /// <summary>
    /// 포인트 목록의 중심 좌표 계산
    /// </summary>
    private (double screenX, double screenY)? GetCentroid(List<QuestObjectiveLocation> points)
    {
        if (points.Count == 0 || _currentMapConfig == null) return null;

        var avgX = points.Average(p => p.X);
        var avgZ = points.Average(p => p.Z ?? 0);

        return _currentMapConfig.GameToScreen(avgX, avgZ);
    }

    /// <summary>
    /// 완료 체크마크 오버레이 추가 - 앱 테마 적용
    /// </summary>
    private void AddCompletionCheckmark(double screenX, double screenY, double inverseScale)
    {
        var size = 20 * inverseScale;

        // 체크마크 배경 원
        var background = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(ThemeSuccessColor),
            Stroke = new SolidColorBrush(ThemeBackgroundDark),
            StrokeThickness = 1.5 * inverseScale
        };

        // 드롭 섀도우
        background.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 4 * inverseScale,
            ShadowDepth = 1 * inverseScale,
            Opacity = 0.4
        };

        Canvas.SetLeft(background, screenX - size / 2);
        Canvas.SetTop(background, screenY - size / 2 - 18 * inverseScale); // 마커 위에 표시
        QuestMarkersCanvas.Children.Add(background);

        // 체크마크 텍스트
        var checkmark = new TextBlock
        {
            Text = "✓",
            Foreground = new SolidColorBrush(ThemeTextPrimary),
            FontSize = 12 * inverseScale,
            FontWeight = FontWeights.Bold
        };
        checkmark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(checkmark, screenX - checkmark.DesiredSize.Width / 2);
        Canvas.SetTop(checkmark, screenY - checkmark.DesiredSize.Height / 2 - 18 * inverseScale);
        QuestMarkersCanvas.Children.Add(checkmark);
    }

    /// <summary>
    /// 마름모 마커 생성 (단일 포인트용) - 개선된 스타일
    /// </summary>
    private Canvas CreateDiamondMarker(double screenX, double screenY, Color color, double inverseScale, double opacity, TaskObjectiveWithLocation? objective = null)
    {
        var size = 18 * inverseScale;
        var canvas = new Canvas { Width = 0, Height = 0 };

        // 글로우 효과 (배경 마름모)
        var glow = new Polygon
        {
            Points = new PointCollection
            {
                new Point(0, -size - 4 * inverseScale),
                new Point(size + 4 * inverseScale, 0),
                new Point(0, size + 4 * inverseScale),
                new Point(-size - 4 * inverseScale, 0)
            },
            Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 80), color.R, color.G, color.B)),
            Stroke = Brushes.Transparent
        };
        canvas.Children.Add(glow);

        // 메인 마름모
        var diamond = new Polygon
        {
            Points = new PointCollection
            {
                new Point(0, -size),
                new Point(size, 0),
                new Point(0, size),
                new Point(-size, 0)
            },
            Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)),
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2.5 * inverseScale
        };

        // 드롭 섀도우 효과
        diamond.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 6 * inverseScale,
            ShadowDepth = 2 * inverseScale,
            Opacity = 0.6
        };

        canvas.Children.Add(diamond);
        Canvas.SetLeft(canvas, screenX);
        Canvas.SetTop(canvas, screenY);
        canvas.Opacity = opacity;

        // 상호작용 추가
        if (objective != null)
        {
            canvas.Tag = objective;
            canvas.Cursor = Cursors.Hand;
            canvas.ToolTip = CreateEnhancedTooltip(objective);
            canvas.ContextMenu = CreateMarkerContextMenu(objective);
            canvas.MouseLeftButtonDown += QuestMarker_Click;
            canvas.MouseEnter += QuestMarker_MouseEnter;
            canvas.MouseLeave += QuestMarker_MouseLeave;
            canvas.MouseRightButtonDown += QuestMarker_RightClick;

            // 마커 매핑에 추가
            if (!_markersByObjectiveId.ContainsKey(objective.ObjectiveId))
                _markersByObjectiveId[objective.ObjectiveId] = new List<FrameworkElement>();
            _markersByObjectiveId[objective.ObjectiveId].Add(canvas);
        }

        return canvas;
    }

    /// <summary>
    /// 영역 라벨 추가 (Centroid 위치)
    /// </summary>
    private void AddAreaLabel(TaskObjectiveWithLocation objective, List<QuestObjectiveLocation> points, Color color, double inverseScale, double opacity = 1.0)
    {
        // Centroid 계산 (tarkov.dev API: X=horizontal X, Z=horizontal depth)
        var avgX = points.Average(p => p.X);
        var avgZ = points.Average(p => p.Z ?? 0);

        var centroid = _currentMapConfig!.GameToScreen(avgX, avgZ);
        if (centroid == null) return;

        AddQuestLabel(objective, centroid.Value.screenX, centroid.Value.screenY, color, inverseScale, opacity);
    }

    /// <summary>
    /// 퀘스트 라벨 추가 - 개선된 스타일 (배경 + 그림자)
    /// </summary>
    private void AddQuestLabel(TaskObjectiveWithLocation objective, double screenX, double screenY, Color color, double inverseScale, double opacity)
    {
        var displayName = !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;

        // 완료 표시
        var statusIcon = objective.IsCompleted ? "✓ " : "";

        // 라벨 컨테이너 (배경 + 텍스트)
        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 35)),
            BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)),
            BorderThickness = new Thickness(2 * inverseScale),
            CornerRadius = new CornerRadius(4 * inverseScale),
            Padding = new Thickness(8 * inverseScale, 4 * inverseScale, 8 * inverseScale, 4 * inverseScale),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 8 * inverseScale,
                ShadowDepth = 2 * inverseScale,
                Opacity = 0.7
            }
        };

        var textPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // 완료 체크마크
        if (objective.IsCompleted)
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = "✓ ",
                Foreground = new SolidColorBrush(Colors.LimeGreen),
                FontSize = 13 * inverseScale,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // 퀘스트 이름
        textPanel.Children.Add(new TextBlock
        {
            Text = displayName,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 13 * inverseScale,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        container.Child = textPanel;

        // 위치 설정 (마커 오른쪽에 배치)
        Canvas.SetLeft(container, screenX + 24 * inverseScale);
        Canvas.SetTop(container, screenY - 14 * inverseScale);
        container.Opacity = opacity;

        QuestMarkersCanvas.Children.Add(container);
    }

    /// <summary>
    /// 퀘스트 목표 타입별 색상
    /// </summary>
    private static Color GetQuestTypeColor(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "visit" => Color.FromRgb(33, 150, 243),       // 파랑 #2196F3
            "mark" => Color.FromRgb(76, 175, 80),         // 초록 #4CAF50
            "plantitem" => Color.FromRgb(255, 152, 0),    // 주황 #FF9800
            "extract" => Color.FromRgb(244, 67, 54),      // 빨강 #F44336
            "finditem" or "findquestitem" or "giveitem" => Color.FromRgb(255, 235, 59), // 노랑 #FFEB3B
            "kill" or "shoot" => Color.FromRgb(156, 39, 176), // 보라 #9C27B0
            _ => Color.FromRgb(255, 193, 7)               // 기본: 금색 #FFC107
        };
    }

    /// <summary>
    /// 퀘스트 마커 툴팁 생성
    /// </summary>
    private object CreateQuestTooltip(TaskObjectiveWithLocation objective)
    {
        var questName = !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;

        var description = !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;

        var typeDisplay = GetQuestTypeDisplayName(objective.Type);
        var statusText = objective.IsCompleted ? " ✓ 완료" : "";

        var panel = new StackPanel { MaxWidth = 300 };

        // 퀘스트 이름
        panel.Children.Add(new TextBlock
        {
            Text = questName + statusText,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(GetQuestTypeColor(objective.Type)),
            TextWrapping = TextWrapping.Wrap
        });

        // 목표 타입
        panel.Children.Add(new TextBlock
        {
            Text = $"[{typeDisplay}]",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 4)
        });

        // 목표 설명
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White
        });

        return panel;
    }

    // 앱 테마 색상 (App.xaml과 동일)
    private static readonly Color ThemeBackgroundDark = Color.FromRgb(0x1a, 0x1a, 0x1a);
    private static readonly Color ThemeBackgroundMedium = Color.FromRgb(0x25, 0x25, 0x25);
    private static readonly Color ThemeBackgroundLight = Color.FromRgb(0x2d, 0x2d, 0x2d);
    private static readonly Color ThemeBorderColor = Color.FromRgb(0x3d, 0x3d, 0x3d);
    private static readonly Color ThemeTextPrimary = Color.FromRgb(0xe0, 0xe0, 0xe0);
    private static readonly Color ThemeTextSecondary = Color.FromRgb(0x9e, 0x9e, 0x9e);
    private static readonly Color ThemeAccentColor = Color.FromRgb(0xc5, 0xa8, 0x4a);
    private static readonly Color ThemeSuccessColor = Color.FromRgb(0x4c, 0xaf, 0x50);

    /// <summary>
    /// 개선된 퀘스트 마커 툴팁 생성 (진행률, 좌표, 위치 수 포함) - 앱 테마 적용
    /// </summary>
    private object CreateEnhancedTooltip(TaskObjectiveWithLocation objective)
    {
        var questName = !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;

        var description = !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;

        var typeDisplay = GetQuestTypeDisplayName(objective.Type);
        var typeColor = GetQuestTypeColor(objective.Type);
        var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);

        var border = new Border
        {
            Background = new SolidColorBrush(ThemeBackgroundMedium),
            BorderBrush = new SolidColorBrush(ThemeBorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            MaxWidth = 300
        };

        var panel = new StackPanel();

        // 헤더 (퀘스트 이름 + 상태 아이콘)
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        if (isCompleted)
        {
            headerPanel.Children.Add(new TextBlock
            {
                Text = "✓ ",
                Foreground = new SolidColorBrush(ThemeSuccessColor),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        headerPanel.Children.Add(new TextBlock
        {
            Text = questName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeTextPrimary),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(headerPanel);

        // 목표 타입 뱃지
        var typeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, typeColor.R, typeColor.G, typeColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, typeColor.R, typeColor.G, typeColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(0, 6, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        typeBadge.Child = new TextBlock
        {
            Text = typeDisplay,
            Foreground = new SolidColorBrush(typeColor),
            FontSize = 11
        };
        panel.Children.Add(typeBadge);

        // 목표 설명
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeTextPrimary),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });

        // 위치 수 정보
        var locationCount = objective.Locations.Count;
        if (locationCount > 1)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"📍 {locationCount}개 위치",
                Foreground = new SolidColorBrush(ThemeTextSecondary),
                FontSize = 11
            });
        }

        // 힌트
        var hintText = new TextBlock
        {
            Text = "클릭: 이동 | 우클릭: 메뉴",
            Foreground = new SolidColorBrush(ThemeTextSecondary),
            FontSize = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };
        panel.Children.Add(hintText);

        border.Child = panel;
        return border;
    }

    /// <summary>
    /// 마커 우클릭 컨텍스트 메뉴 생성 - 앱 테마 자동 적용 (App.xaml MenuItem 스타일)
    /// </summary>
    private ContextMenu CreateMarkerContextMenu(TaskObjectiveWithLocation objective)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(ThemeBackgroundMedium),
            BorderBrush = new SolidColorBrush(ThemeBorderColor),
            BorderThickness = new Thickness(1)
        };
        var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);

        // 완료/미완료 토글
        var completeMenuItem = new MenuItem
        {
            Header = isCompleted ? "미완료로 표시" : "완료로 표시",
            Tag = objective
        };
        completeMenuItem.Click += (s, e) =>
        {
            var obj = (s as MenuItem)?.Tag as TaskObjectiveWithLocation;
            if (obj != null)
            {
                var currentState = _progressService.IsObjectiveCompletedById(obj.ObjectiveId);
                _progressService.SetObjectiveCompletedById(obj.ObjectiveId, !currentState, obj.TaskNormalizedName);
                RefreshQuestMarkers();
                RefreshQuestDrawer();
            }
        };
        menu.Items.Add(completeMenuItem);

        menu.Items.Add(new Separator());

        // Drawer에서 보기
        var viewInDrawerMenuItem = new MenuItem
        {
            Header = "Drawer에서 보기",
            Tag = objective
        };
        viewInDrawerMenuItem.Click += (s, e) =>
        {
            var obj = (s as MenuItem)?.Tag as TaskObjectiveWithLocation;
            if (obj != null)
            {
                if (!_isDrawerOpen) OpenDrawer();
                ScrollToQuestInDrawer(obj.TaskNormalizedName);
            }
        };
        menu.Items.Add(viewInDrawerMenuItem);

        // 이 퀘스트 숨기기
        var hideQuestMenuItem = new MenuItem
        {
            Header = "이 퀘스트 숨기기",
            Tag = objective
        };
        hideQuestMenuItem.Click += (s, e) =>
        {
            var obj = (s as MenuItem)?.Tag as TaskObjectiveWithLocation;
            if (obj != null)
            {
                _hiddenQuestIds.Add(obj.TaskNormalizedName);
                RefreshQuestMarkers();
                RefreshQuestDrawer();
            }
        };
        menu.Items.Add(hideQuestMenuItem);

        menu.Items.Add(new Separator());

        // 좌표 복사
        var copyCoordMenuItem = new MenuItem
        {
            Header = "좌표 복사",
            Tag = objective
        };
        copyCoordMenuItem.Click += (s, e) =>
        {
            var obj = (s as MenuItem)?.Tag as TaskObjectiveWithLocation;
            if (obj != null && obj.Locations.Count > 0)
            {
                var loc = obj.Locations[0];
                var coordText = $"X: {loc.X:F1}, Z: {loc.Z:F1}";
                System.Windows.Clipboard.SetText(coordText);
                StatusText.Text = $"좌표 복사됨: {coordText}";
            }
        };
        menu.Items.Add(copyCoordMenuItem);

        return menu;
    }

    /// <summary>
    /// 마커 마우스 진입 - Drawer 항목 강조
    /// </summary>
    private void QuestMarker_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            _hoveredObjectiveId = objective.ObjectiveId;

            // 마커 강조 효과
            HighlightMarker(element, true);

            // Drawer가 열려있으면 해당 퀘스트 강조
            if (_isDrawerOpen)
            {
                _highlightedQuestId = objective.TaskNormalizedName;
                RefreshQuestDrawer();
            }
        }
    }

    /// <summary>
    /// 마커 마우스 이탈 - Drawer 강조 해제
    /// </summary>
    private void QuestMarker_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            _hoveredObjectiveId = null;

            // 마커 강조 해제
            HighlightMarker(element, false);

            // Drawer 강조 해제
            if (_isDrawerOpen && _highlightedQuestId == objective.TaskNormalizedName)
            {
                _highlightedQuestId = null;
                RefreshQuestDrawer();
            }
        }
    }

    /// <summary>
    /// 마커 우클릭 핸들러
    /// </summary>
    private void QuestMarker_RightClick(object sender, MouseButtonEventArgs e)
    {
        // ContextMenu가 자동으로 표시됨
        e.Handled = true;
    }

    /// <summary>
    /// 마커 강조 효과 적용/해제 - 앱 테마 Accent 색상 사용
    /// </summary>
    private void HighlightMarker(FrameworkElement element, bool highlight)
    {
        if (element is Polygon polygon)
        {
            if (highlight)
            {
                polygon.StrokeThickness *= 1.5;
                polygon.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ThemeAccentColor,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            else
            {
                polygon.StrokeThickness /= 1.5;
                polygon.Effect = null;
            }
        }
        else if (element is Line line)
        {
            if (highlight)
            {
                line.StrokeThickness *= 1.5;
                line.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ThemeAccentColor,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            else
            {
                line.StrokeThickness /= 1.5;
                line.Effect = null;
            }
        }
        else if (element is Canvas canvas)
        {
            if (highlight)
            {
                canvas.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ThemeAccentColor,
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            else
            {
                canvas.Effect = null;
            }
        }
    }

    /// <summary>
    /// Drawer 아이템 호버 시작 - 해당 마커 펄스 애니메이션
    /// </summary>
    private void QuestDrawerItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerItem item)
        {
            var objectiveId = item.Objective.ObjectiveId;

            // 해당 목표의 마커들 찾아서 펄스 효과 시작
            if (_markersByObjectiveId.TryGetValue(objectiveId, out var markers))
            {
                foreach (var marker in markers)
                {
                    StartPulseAnimation(marker);
                }
            }
        }
    }

    /// <summary>
    /// Drawer 아이템 호버 종료 - 펄스 애니메이션 중지
    /// </summary>
    private void QuestDrawerItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerItem item)
        {
            var objectiveId = item.Objective.ObjectiveId;

            // 해당 목표의 마커들 펄스 효과 중지
            if (_markersByObjectiveId.TryGetValue(objectiveId, out var markers))
            {
                foreach (var marker in markers)
                {
                    StopPulseAnimation(marker);
                }
            }
        }
    }

    /// <summary>
    /// 마커 펄스 애니메이션 시작
    /// </summary>
    private void StartPulseAnimation(FrameworkElement element)
    {
        // 기존 애니메이션 중지
        element.BeginAnimation(UIElement.OpacityProperty, null);

        // 펄스 애니메이션 생성
        var pulseAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1.0,
            To = 0.4,
            Duration = TimeSpan.FromMilliseconds(400),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.SineEase()
        };

        // 마커 강조 효과 추가
        HighlightMarker(element, true);

        // 애니메이션 시작
        element.BeginAnimation(UIElement.OpacityProperty, pulseAnimation);
    }

    /// <summary>
    /// 마커 펄스 애니메이션 중지
    /// </summary>
    private void StopPulseAnimation(FrameworkElement element)
    {
        // 애니메이션 중지
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1.0;

        // 마커 강조 효과 해제
        HighlightMarker(element, false);
    }

    /// <summary>
    /// 퀘스트 목표 타입 표시 이름
    /// </summary>
    private static string GetQuestTypeDisplayName(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "visit" => "방문",
            "mark" => "마킹",
            "plantitem" => "아이템 설치",
            "extract" => "탈출",
            "finditem" => "아이템 찾기",
            "findquestitem" => "퀘스트 아이템 찾기",
            "giveitem" => "아이템 전달",
            "kill" or "shoot" => "처치",
            _ => type ?? "기타"
        };
    }

    /// <summary>
    /// 퀘스트 마커 클릭 이벤트 - Drawer 열고 해당 퀘스트로 스크롤
    /// </summary>
    private void QuestMarker_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            // Drawer 열기
            if (!_isDrawerOpen)
            {
                OpenDrawer();
            }

            // 해당 퀘스트 하이라이트 및 스크롤
            ScrollToQuestInDrawer(objective.TaskNormalizedName);

            e.Handled = true;
        }
    }

    /// <summary>
    /// Drawer에서 특정 퀘스트로 스크롤
    /// </summary>
    private void ScrollToQuestInDrawer(string questId)
    {
        _highlightedQuestId = questId;

        // ItemsSource에서 해당 그룹 찾기
        if (QuestObjectivesList.ItemsSource is List<QuestDrawerGroup> groups)
        {
            var targetGroup = groups.FirstOrDefault(g => g.QuestId == questId);
            if (targetGroup != null)
            {
                // 해당 아이템으로 스크롤
                var index = groups.IndexOf(targetGroup);
                if (index >= 0)
                {
                    // ItemsControl의 컨테이너 가져오기
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var container = QuestObjectivesList.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
                        container?.BringIntoView();

                        // 하이라이트 효과 (2초 후 해제)
                        RefreshQuestDrawer();
                        var timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(2)
                        };
                        timer.Tick += (s, e) =>
                        {
                            _highlightedQuestId = null;
                            RefreshQuestDrawer();
                            timer.Stop();
                        };
                        timer.Start();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
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

    #region Quest Drawer

    /// <summary>
    /// Drawer 토글 버튼 클릭
    /// </summary>
    private void BtnToggleDrawer_Click(object sender, RoutedEventArgs e)
    {
        if (_isDrawerOpen)
        {
            CloseDrawer();
        }
        else
        {
            OpenDrawer();
        }
    }

    /// <summary>
    /// Drawer 닫기 버튼 클릭
    /// </summary>
    private void BtnCloseDrawer_Click(object sender, RoutedEventArgs e)
    {
        CloseDrawer();
    }

    /// <summary>
    /// Drawer 열기
    /// </summary>
    private void OpenDrawer()
    {
        _isDrawerOpen = true;
        QuestDrawerColumn.Width = new GridLength(_drawerWidth);
        QuestDrawerPanel.Visibility = Visibility.Visible;
        BtnToggleDrawer.Content = "▶";
        RefreshQuestDrawer();
    }

    /// <summary>
    /// Drawer 닫기
    /// </summary>
    private void CloseDrawer()
    {
        _isDrawerOpen = false;
        QuestDrawerColumn.Width = new GridLength(0);
        QuestDrawerPanel.Visibility = Visibility.Collapsed;
        BtnToggleDrawer.Content = "◀";
    }

    /// <summary>
    /// Drawer 필터 변경
    /// </summary>
    private void DrawerFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_isDrawerOpen)
        {
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// Drawer 퀘스트 목록 새로고침
    /// </summary>
    private void RefreshQuestDrawer()
    {
        // 초기화 전 또는 서비스가 준비되지 않은 경우 무시
        if (_dbService == null || !_dbService.ObjectivesLoaded) return;

        var incompleteOnly = ChkIncompleteOnly?.IsChecked == true;
        var currentMapOnly = ChkCurrentMapOnly?.IsChecked == true;

        // DB에서 모든 퀘스트 목표 가져와서 변환
        var allDbObjectives = _dbService.GetAllObjectives();
        var allObjectives = allDbObjectives.Select(ConvertToTaskObjective).ToList();

        // 필터 적용
        var filteredObjectives = allObjectives
            .Where(obj =>
            {
                // 미완료 필터
                if (incompleteOnly)
                {
                    var isCompleted = _progressService.IsQuestCompleted(obj.TaskNormalizedName);
                    if (isCompleted) return false;
                }

                // 현재 맵 필터
                if (currentMapOnly && _currentMapConfig != null)
                {
                    var mapKey = _currentMapConfig.Key;
                    var normalizedMapKey = MapMarkerDbService.NormalizeMapKey(mapKey);
                    var hasLocationOnMap = obj.Locations.Any(loc =>
                    {
                        var locationMapNormalized = MapMarkerDbService.NormalizeMapKey(loc.MapName ?? "");
                        return locationMapNormalized.Equals(normalizedMapKey, StringComparison.OrdinalIgnoreCase);
                    });
                    if (!hasLocationOnMap) return false;
                }

                // 위치 정보가 있는 목표만
                return obj.Locations.Count > 0;
            })
            .ToList();

        // 퀘스트별로 그룹화
        var groups = filteredObjectives
            .GroupBy(obj => obj.TaskNormalizedName)
            .Select(g =>
            {
                var firstObj = g.First();
                var questName = !string.IsNullOrEmpty(firstObj.TaskNameKo)
                    ? firstObj.TaskNameKo
                    : firstObj.TaskName;

                // 각 목표별로 완료 상태 확인
                var objectives = g.Select(obj =>
                {
                    var objCompleted = _progressService.IsObjectiveCompletedById(obj.ObjectiveId);
                    return new QuestDrawerItem(obj, objCompleted);
                }).ToList();

                // 퀘스트 완료 = 모든 목표 완료
                var isQuestCompleted = objectives.All(o => o.IsCompleted);

                var group = new QuestDrawerGroup(g.Key, questName, isQuestCompleted, objectives)
                {
                    IsVisible = !_hiddenQuestIds.Contains(g.Key),
                    IsHighlighted = g.Key == _highlightedQuestId,
                    IsExpanded = !_collapsedQuestIds.Contains(g.Key)
                };
                return group;
            })
            .ToList();

        // 검색 필터 적용
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var search = _searchText.ToLowerInvariant();
            groups = groups.Where(g =>
                g.QuestName.ToLowerInvariant().Contains(search) ||
                g.Objectives.Any(o => o.DescriptionDisplay.ToLowerInvariant().Contains(search))
            ).ToList();
        }

        // 정렬 적용
        groups = _sortOption switch
        {
            "progress" => groups.OrderByDescending(g => (double)g.CompletedCount / g.ObjectiveCount)
                                .ThenBy(g => g.QuestName).ToList(),
            "count" => groups.OrderByDescending(g => g.ObjectiveCount)
                             .ThenBy(g => g.QuestName).ToList(),
            _ => groups.OrderBy(g => g.QuestName).ToList() // name (default)
        };

        QuestObjectivesList.ItemsSource = groups;

        // 빈 상태 패널 표시/숨김
        if (EmptyStatePanel != null)
        {
            EmptyStatePanel.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 퀘스트 카운트 업데이트
        var totalObjectives = groups.Sum(g => g.ObjectiveCount);
        var completedObjectives = groups.Sum(g => g.CompletedCount);
        if (QuestCountText != null)
        {
            QuestCountText.Text = $"({groups.Count}퀘스트, {completedObjectives}/{totalObjectives})";
        }
    }

    /// <summary>
    /// Drawer 퀘스트 아이템 클릭 - 맵에서 해당 위치로 포커스
    /// </summary>
    private void QuestDrawerItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerItem item)
        {
            FocusOnQuestObjective(item.Objective);
        }
    }

    /// <summary>
    /// 퀘스트 표시/숨김 토글
    /// </summary>
    private void QuestVisibilityToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is QuestDrawerGroup group)
        {
            if (checkBox.IsChecked == true)
            {
                _hiddenQuestIds.Remove(group.QuestId);
            }
            else
            {
                _hiddenQuestIds.Add(group.QuestId);
            }

            // 맵 마커 새로고침
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// 퀘스트 목표 우클릭 - 완료 토글
    /// </summary>
    private void QuestDrawerItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerItem item)
        {
            var objectiveId = item.Objective.ObjectiveId;
            var questId = item.Objective.TaskNormalizedName;
            var isCurrentlyCompleted = _progressService.IsObjectiveCompletedById(objectiveId);

            // 완료 상태 토글
            _progressService.SetObjectiveCompletedById(objectiveId, !isCurrentlyCompleted, questId);

            // UI 새로고침
            RefreshQuestDrawer();
            RefreshQuestMarkers();

            e.Handled = true;
        }
    }

    /// <summary>
    /// 퀘스트 헤더 클릭 - 접기/펼치기
    /// </summary>
    private void QuestHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerGroup group)
        {
            group.IsExpanded = !group.IsExpanded;
            if (group.IsExpanded)
                _collapsedQuestIds.Remove(group.QuestId);
            else
                _collapsedQuestIds.Add(group.QuestId);
        }
    }

    /// <summary>
    /// 검색어 변경
    /// </summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            _searchText = textBox.Text;
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 정렬 옵션 변경
    /// </summary>
    private void SortOption_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
        {
            _sortOption = item.Tag?.ToString() ?? "name";
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 전체 표시 버튼
    /// </summary>
    private void BtnShowAll_Click(object sender, RoutedEventArgs e)
    {
        _hiddenQuestIds.Clear();
        RefreshQuestDrawer();
        RefreshQuestMarkers();
    }

    /// <summary>
    /// 전체 숨김 버튼
    /// </summary>
    private void BtnHideAll_Click(object sender, RoutedEventArgs e)
    {
        if (QuestObjectivesList.ItemsSource is List<QuestDrawerGroup> groups)
        {
            foreach (var g in groups)
                _hiddenQuestIds.Add(g.QuestId);
            RefreshQuestDrawer();
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// 전체 펼치기
    /// </summary>
    private void BtnExpandAll_Click(object sender, RoutedEventArgs e)
    {
        _collapsedQuestIds.Clear();
        RefreshQuestDrawer();
    }

    /// <summary>
    /// 전체 접기
    /// </summary>
    private void BtnCollapseAll_Click(object sender, RoutedEventArgs e)
    {
        if (QuestObjectivesList.ItemsSource is List<QuestDrawerGroup> groups)
        {
            foreach (var g in groups)
                _collapsedQuestIds.Add(g.QuestId);
            RefreshQuestDrawer();
        }
    }

    /// <summary>
    /// 퀘스트 컨텍스트 메뉴 - 모두 완료
    /// </summary>
    private void QuestContextMenu_MarkAllComplete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is QuestDrawerGroup group)
        {
            foreach (var obj in group.Objectives)
            {
                _progressService.SetObjectiveCompletedById(obj.Objective.ObjectiveId, true, group.QuestId);
            }
            RefreshQuestDrawer();
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// 퀘스트 컨텍스트 메뉴 - 모두 미완료
    /// </summary>
    private void QuestContextMenu_MarkAllIncomplete(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is QuestDrawerGroup group)
        {
            foreach (var obj in group.Objectives)
            {
                _progressService.SetObjectiveCompletedById(obj.Objective.ObjectiveId, false, group.QuestId);
            }
            RefreshQuestDrawer();
            RefreshQuestMarkers();
        }
    }

    /// <summary>
    /// Drawer 리사이즈 시작
    /// </summary>
    private void DrawerResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement grip)
        {
            grip.CaptureMouse();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Drawer 리사이즈 진행
    /// </summary>
    private void DrawerResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement grip && grip.IsMouseCaptured)
        {
            var pos = e.GetPosition(this);
            var newWidth = pos.X;
            if (newWidth >= DrawerMinWidth && newWidth <= DrawerMaxWidth)
            {
                _drawerWidth = newWidth;
                QuestDrawerColumn.Width = new GridLength(_drawerWidth);
            }
        }
    }

    /// <summary>
    /// Drawer 리사이즈 종료
    /// </summary>
    private void DrawerResizeGrip_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement grip)
        {
            grip.ReleaseMouseCapture();
        }
    }

    /// <summary>
    /// Drawer 키보드 네비게이션
    /// </summary>
    private void QuestDrawer_KeyDown(object sender, KeyEventArgs e)
    {
        if (QuestObjectivesList.ItemsSource is not List<QuestDrawerGroup> groups || groups.Count == 0)
            return;

        var currentIndex = -1;
        for (int i = 0; i < groups.Count; i++)
        {
            if (groups[i].IsHighlighted)
            {
                currentIndex = i;
                break;
            }
        }

        switch (e.Key)
        {
            case Key.Down:
                currentIndex = Math.Min(currentIndex + 1, groups.Count - 1);
                break;
            case Key.Up:
                currentIndex = Math.Max(currentIndex - 1, 0);
                break;
            case Key.Enter when currentIndex >= 0:
                // 첫 번째 목표로 포커스
                var firstObj = groups[currentIndex].Objectives.FirstOrDefault();
                if (firstObj != null)
                    FocusOnQuestObjective(firstObj.Objective);
                return;
            case Key.Space when currentIndex >= 0:
                // 접기/펼치기 토글
                groups[currentIndex].IsExpanded = !groups[currentIndex].IsExpanded;
                if (groups[currentIndex].IsExpanded)
                    _collapsedQuestIds.Remove(groups[currentIndex].QuestId);
                else
                    _collapsedQuestIds.Add(groups[currentIndex].QuestId);
                return;
            default:
                return;
        }

        // 하이라이트 업데이트
        _highlightedQuestId = currentIndex >= 0 ? groups[currentIndex].QuestId : null;
        RefreshQuestDrawer();
        e.Handled = true;
    }

    /// <summary>
    /// 퀘스트 목표 위치로 맵 포커스 이동 (부드러운 애니메이션)
    /// </summary>
    private void FocusOnQuestObjective(TaskObjectiveWithLocation objective)
    {
        if (_currentMapConfig == null) return;

        // 현재 맵의 위치 찾기
        var mapKey = _currentMapConfig.Key.ToLowerInvariant();
        var locationsOnMap = objective.Locations
            .Where(loc =>
            {
                var locationMapName = loc.MapNormalizedName?.ToLowerInvariant() ?? "";
                var locationMapNameAlt = loc.MapName?.ToLowerInvariant() ?? "";
                return locationMapName == mapKey || locationMapNameAlt == mapKey;
            })
            .ToList();

        if (locationsOnMap.Count == 0)
        {
            // 현재 맵에 위치가 없으면 해당 맵으로 전환
            var firstLocation = objective.Locations.FirstOrDefault();
            if (firstLocation != null)
            {
                var targetMapKey = firstLocation.MapNormalizedName ?? firstLocation.MapName;
                if (!string.IsNullOrEmpty(targetMapKey))
                {
                    // 맵 선택기에서 해당 맵 찾기
                    for (int i = 0; i < MapSelector.Items.Count; i++)
                    {
                        if (MapSelector.Items[i] is DbMapConfig config &&
                            config.Key.Equals(targetMapKey, StringComparison.OrdinalIgnoreCase))
                        {
                            MapSelector.SelectedIndex = i;
                            // 맵 변경 후 다시 포커스 시도
                            Dispatcher.BeginInvoke(new Action(() => FocusOnQuestObjective(objective)),
                                System.Windows.Threading.DispatcherPriority.Loaded);
                            return;
                        }
                    }
                }
            }
            return;
        }

        // Centroid 계산 (tarkov.dev API: X=horizontal X, Z=horizontal depth)
        var avgX = locationsOnMap.Average(loc => loc.X);
        var avgZ = locationsOnMap.Average(loc => loc.Z ?? 0);

        var screenCoords = _currentMapConfig.GameToScreen(avgX, avgZ);
        if (screenCoords == null) return;

        // 목표 줌 레벨
        var targetZoom = Math.Max(2.0, _zoomLevel);

        // 목표 위치 계산
        var viewerCenterX = MapViewerGrid.ActualWidth / 2;
        var viewerCenterY = MapViewerGrid.ActualHeight / 2;
        var targetTranslateX = viewerCenterX - screenCoords.Value.screenX * targetZoom;
        var targetTranslateY = viewerCenterY - screenCoords.Value.screenY * targetZoom;

        // 부드러운 애니메이션 적용
        AnimateMapTo(targetZoom, targetTranslateX, targetTranslateY);

        // 상태 텍스트 업데이트
        var questName = !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;
        StatusText.Text = $"포커스: {questName}";

        // 해당 마커 일시 강조
        var objectiveId = objective.ObjectiveId;
        if (_markersByObjectiveId.TryGetValue(objectiveId, out var markers))
        {
            foreach (var marker in markers)
            {
                StartPulseAnimation(marker);

                // 2초 후 펄스 중지
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                var capturedMarker = marker;
                timer.Tick += (s, e) =>
                {
                    StopPulseAnimation(capturedMarker);
                    timer.Stop();
                };
                timer.Start();
            }
        }
    }

    /// <summary>
    /// 맵을 목표 줌/위치로 부드럽게 애니메이션
    /// </summary>
    private void AnimateMapTo(double targetZoom, double targetTranslateX, double targetTranslateY)
    {
        var duration = TimeSpan.FromMilliseconds(350);
        var easing = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

        // 줌 애니메이션
        var zoomAnimationX = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetZoom,
            Duration = duration,
            EasingFunction = easing
        };
        var zoomAnimationY = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetZoom,
            Duration = duration,
            EasingFunction = easing
        };

        // 이동 애니메이션
        var translateAnimationX = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetTranslateX,
            Duration = duration,
            EasingFunction = easing
        };
        var translateAnimationY = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetTranslateY,
            Duration = duration,
            EasingFunction = easing
        };

        // 줌 텍스트 업데이트를 위한 이벤트
        zoomAnimationX.Completed += (s, e) =>
        {
            _zoomLevel = targetZoom;
            ZoomText.Text = $"{_zoomLevel * 100:F0}%";
            RedrawAll(); // 마커 스케일 업데이트
        };

        // 애니메이션 시작
        MapScale.BeginAnimation(ScaleTransform.ScaleXProperty, zoomAnimationX);
        MapScale.BeginAnimation(ScaleTransform.ScaleYProperty, zoomAnimationY);
        MapTranslate.BeginAnimation(TranslateTransform.XProperty, translateAnimationX);
        MapTranslate.BeginAnimation(TranslateTransform.YProperty, translateAnimationY);
    }

    #endregion

    #region UI Controls (Legend, Copy, Keyboard Shortcuts)

    /// <summary>
    /// 페이지 전역 키보드 단축키 처리
    /// </summary>
    private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 텍스트 입력 중이면 단축키 무시
        if (e.OriginalSource is TextBox) return;

        switch (e.Key)
        {
            case Key.D1: // 1 - 탈출구 토글
            case Key.E:
                ChkShowMarkers.IsChecked = !ChkShowMarkers.IsChecked;
                e.Handled = true;
                break;

            case Key.D2: // 2 - 환승 토글
            case Key.T:
                ChkShowTransit.IsChecked = !ChkShowTransit.IsChecked;
                e.Handled = true;
                break;

            case Key.D3: // 3 - 퀘스트 토글
                ChkShowQuests.IsChecked = !ChkShowQuests.IsChecked;
                e.Handled = true;
                break;

            case Key.R: // R - 뷰 초기화
                BtnResetView_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.Q: // Q - Drawer 토글
                BtnToggleDrawer_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.L: // L - 범례 토글
                BtnLegend_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.Escape: // ESC - 팝업 닫기
                if (LegendPopup.Visibility == Visibility.Visible)
                {
                    LegendPopup.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
                else if (_isDrawerOpen)
                {
                    CloseDrawer();
                    e.Handled = true;
                }
                break;

            case Key.OemPlus: // + - 줌 인
            case Key.Add:
                BtnZoomIn_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Key.OemMinus: // - - 줌 아웃
            case Key.Subtract:
                BtnZoomOut_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            // 넘패드 0~4: 층 변경
            case Key.NumPad0:
                SelectFloorByIndex(0);
                e.Handled = true;
                break;
            case Key.NumPad1:
                SelectFloorByIndex(1);
                e.Handled = true;
                break;
            case Key.NumPad2:
                SelectFloorByIndex(2);
                e.Handled = true;
                break;
            case Key.NumPad3:
                SelectFloorByIndex(3);
                e.Handled = true;
                break;
            case Key.NumPad4:
                SelectFloorByIndex(4);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 인덱스로 층 선택 (0부터 시작)
    /// </summary>
    private void SelectFloorByIndex(int index)
    {
        if (_sortedFloors == null || _sortedFloors.Count == 0)
        {
            StatusText.Text = "이 맵에는 층이 없습니다";
            return;
        }

        if (index >= 0 && index < FloorSelector.Items.Count)
        {
            FloorSelector.SelectedIndex = index;
            var floorName = _sortedFloors[index].DisplayName;
            StatusText.Text = $"층 변경: {floorName}";
        }
        else
        {
            StatusText.Text = $"층 {index}이(가) 없습니다 (0~{FloorSelector.Items.Count - 1} 사용 가능)";
        }
    }

    /// <summary>
    /// 범례 버튼 클릭 - 범례 표시/숨김 토글
    /// </summary>
    private void BtnLegend_Click(object sender, RoutedEventArgs e)
    {
        if (LegendPopup.Visibility == Visibility.Visible)
        {
            LegendPopup.Visibility = Visibility.Collapsed;
        }
        else
        {
            LegendPopup.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 범례 닫기 버튼 클릭
    /// </summary>
    private void BtnCloseLegend_Click(object sender, RoutedEventArgs e)
    {
        LegendPopup.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// 좌표 복사 버튼 클릭 - 현재 마우스 위치 게임 좌표를 클립보드에 복사
    /// </summary>
    private void BtnCopyCoords_Click(object sender, RoutedEventArgs e)
    {
        if (_hasValidCoordinates)
        {
            var coordsText = $"X: {_currentGameX:F1}, Z: {_currentGameZ:F1}";
            try
            {
                Clipboard.SetText(coordsText);
                StatusText.Text = $"좌표 복사됨: {coordsText}";
            }
            catch
            {
                StatusText.Text = "좌표 복사 실패";
            }
        }
        else
        {
            StatusText.Text = "복사할 좌표 없음 - 맵 위에 마우스를 올려주세요";
        }
    }

    /// <summary>
    /// 상태바 맵 정보 업데이트
    /// </summary>
    private void UpdateStatusBarMapInfo()
    {
        if (_currentMapConfig != null)
        {
            CurrentMapName.Text = _currentMapConfig.DisplayName;

            if (_sortedFloors != null && _sortedFloors.Count > 0 && !string.IsNullOrEmpty(_currentFloorId))
            {
                var floor = _sortedFloors.FirstOrDefault(f => f.LayerId == _currentFloorId);
                CurrentFloorName.Text = floor != null ? $"[{floor.DisplayName}]" : "";
            }
            else
            {
                CurrentFloorName.Text = "";
            }
        }
        else
        {
            CurrentMapName.Text = "맵 선택";
            CurrentFloorName.Text = "";
        }
    }

    #endregion

    #region Map Tracker

    private void TestMapPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Disconnect events to prevent memory leaks
        _trackerService.PositionUpdated -= OnPositionUpdated;
        _trackerService.WatchingStateChanged -= OnWatchingStateChanged;
        _trackerService.StatusMessage -= OnTrackerStatusMessage;
        _logMapWatcher.MapChanged -= OnLogMapChanged;
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
        if (_trackerService.IsWatching)
        {
            _trackerService.StopTracking();
        }
        else
        {
            if (string.IsNullOrEmpty(_trackerService.Settings.ScreenshotFolderPath))
            {
                StatusText.Text = "먼저 스크린샷 폴더를 선택하세요";
                return;
            }

            // Set current map for coordinate transformation
            if (_currentMapConfig != null)
            {
                _trackerService.SetCurrentMap(_currentMapConfig.Key);
            }

            _trackerService.StartTracking();

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
    /// </summary>
    private void OnPositionUpdated(object? sender, ScreenPosition position)
    {
        Dispatcher.Invoke(() =>
        {
            if (_currentMapConfig == null) return;

            // ScreenPosition.X and Y are already image pixel coordinates
            var svgX = position.X;
            var svgY = position.Y;

            // Add trail marker for previous position
            if (_playerMarker != null && _showTrail)
            {
                AddTrailMarker(Canvas.GetLeft(_playerMarker), Canvas.GetTop(_playerMarker));
            }

            // Update player marker
            UpdatePlayerMarker(svgX, svgY);

            // Show original game coordinates in status
            if (position.OriginalPosition != null)
            {
                StatusText.Text = $"위치: X={position.OriginalPosition.X:F0}, Z={position.OriginalPosition.Y:F0}";
            }
            else
            {
                StatusText.Text = $"위치: Screen X={svgX:F0}, Y={svgY:F0}";
            }
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
            TrackingStatusBorder.Background = isWatching
                ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // Green
                : new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
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
    /// 플레이어 마커 업데이트
    /// </summary>
    private void UpdatePlayerMarker(double svgX, double svgY)
    {
        if (!_showPlayerMarker) return;

        if (_playerMarker == null)
        {
            _playerMarker = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = new SolidColorBrush(Color.FromRgb(33, 150, 243)), // Blue
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            _playerMarker.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 4,
                ShadowDepth = 2,
                Opacity = 0.5
            };
            PlayerMarkerCanvas.Children.Add(_playerMarker);
        }

        Canvas.SetLeft(_playerMarker, svgX - 8);
        Canvas.SetTop(_playerMarker, svgY - 8);
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

        Canvas.SetLeft(trailDot, svgX - 3 + 8); // Center on player marker position
        Canvas.SetTop(trailDot, svgY - 3 + 8);
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
        if (_playerMarker != null)
        {
            PlayerMarkerCanvas.Children.Remove(_playerMarker);
            _playerMarker = null;
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

/// <summary>
/// Quest Drawer 그룹 ViewModel (퀘스트별 그룹)
/// </summary>
public class QuestDrawerGroup : System.ComponentModel.INotifyPropertyChanged
{
    public string QuestId { get; }
    public string QuestName { get; }
    public bool IsCompleted { get; }
    public bool IsVisible { get; set; } = true; // 맵에 표시 여부
    public bool IsHighlighted { get; set; } // 하이라이트 여부
    public List<QuestDrawerItem> Objectives { get; }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public QuestDrawerGroup(string questId, string questName, bool isCompleted, List<QuestDrawerItem> objectives)
    {
        QuestId = questId;
        QuestName = questName;
        IsCompleted = isCompleted;
        Objectives = objectives;
    }

    public int ObjectiveCount => Objectives.Count;
    public int CompletedCount => Objectives.Count(o => o.IsCompleted);
    public string ProgressText => $"{CompletedCount}/{ObjectiveCount}";

    /// <summary>
    /// 진행률 (0.0 ~ 1.0)
    /// </summary>
    public double ProgressPercent => ObjectiveCount > 0 ? (double)CompletedCount / ObjectiveCount : 0;

    /// <summary>
    /// 선택된 항목 여부
    /// </summary>
    public bool IsSelected { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Quest Drawer 아이템 ViewModel
/// </summary>
public class QuestDrawerItem
{
    public TaskObjectiveWithLocation Objective { get; }
    public bool IsCompleted { get; }

    public QuestDrawerItem(TaskObjectiveWithLocation objective, bool isCompleted)
    {
        Objective = objective;
        IsCompleted = isCompleted;
    }

    /// <summary>
    /// 표시용 퀘스트 이름
    /// </summary>
    public string TaskDisplayName =>
        !string.IsNullOrEmpty(Objective.TaskNameKo) ? Objective.TaskNameKo : Objective.TaskName;

    /// <summary>
    /// 표시용 목표 설명 (짧게)
    /// </summary>
    public string DescriptionDisplay
    {
        get
        {
            var desc = !string.IsNullOrEmpty(Objective.DescriptionKo)
                ? Objective.DescriptionKo
                : Objective.Description;

            // 최대 60자로 제한
            if (desc.Length > 60)
                desc = desc.Substring(0, 57) + "...";

            return desc;
        }
    }

    /// <summary>
    /// 목표 타입 아이콘 (이모지)
    /// </summary>
    public string TypeIcon => Objective.Type switch
    {
        "visit" => "📍",      // 방문
        "mark" => "🎯",       // 마킹
        "plantItem" => "📦",  // 아이템 설치
        "extract" => "🚪",    // 탈출
        "findItem" => "🔍",   // 아이템 찾기
        "giveItem" => "🎁",   // 아이템 전달
        "shoot" => "💀",      // 처치
        "skill" => "📈",      // 스킬
        "buildWeapon" => "🔧", // 무기 조립
        "traderLevel" => "💼", // 트레이더 레벨
        _ => "📋"             // 기타
    };

    /// <summary>
    /// 첫 번째 위치의 맵 이름
    /// </summary>
    public string MapName => Objective.Locations.FirstOrDefault()?.MapName ?? "";

    /// <summary>
    /// 맵 이름 짧은 태그
    /// </summary>
    public string MapTag
    {
        get
        {
            var map = MapName.ToLowerInvariant();
            return map switch
            {
                "customs" => "CUS",
                "factory" => "FAC",
                "interchange" => "INT",
                "woods" => "WOD",
                "shoreline" => "SHR",
                "reserve" => "RSV",
                "lighthouse" => "LHT",
                "streets of tarkov" => "STR",
                "ground zero" => "GZ",
                "labs" => "LAB",
                _ => map.Length > 3 ? map.Substring(0, 3).ToUpperInvariant() : map.ToUpperInvariant()
            };
        }
    }

    /// <summary>
    /// 맵 태그 표시 여부
    /// </summary>
    public bool ShowMapTag => !string.IsNullOrEmpty(MapName);
}

/// <summary>
/// 문자열이 비어있으면 Visible, 있으면 Collapsed (Watermark용)
/// </summary>
public class StringToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// bool을 Visibility로 변환
/// </summary>
public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 진행률(0.0~1.0)을 프로그레스 바 너비로 변환 (최대 120px)
/// </summary>
public class ProgressWidthConverter : System.Windows.Data.IValueConverter
{
    private const double MaxWidth = 120.0;

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is double percent)
        {
            return Math.Max(0, Math.Min(MaxWidth, percent * MaxWidth));
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
