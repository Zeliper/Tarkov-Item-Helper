using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TarkovHelper.Models;
using TarkovHelper.Services;

// Type aliases for WPF disambiguation
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brushes = System.Windows.Media.Brushes;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;

namespace TarkovHelper.Pages;

/// <summary>
/// Hit region for marker tooltip detection
/// </summary>
internal sealed class MarkerHitRegion
{
    public MapMarker Marker { get; init; } = null!;
    public List<MapMarker>? ClusterMarkers { get; init; }  // Non-null for clustered markers
    public double ScreenX { get; init; }
    public double ScreenY { get; init; }
    public double Radius { get; init; }

    public bool IsCluster => ClusterMarkers != null && ClusterMarkers.Count > 1;

    public bool Contains(double x, double y)
    {
        var dx = x - ScreenX;
        var dy = y - ScreenY;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}

/// <summary>
/// Represents a cluster of markers
/// </summary>
internal sealed class MarkerCluster
{
    public List<MapMarker> Markers { get; } = new();
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public double ScreenX { get; private set; }
    public double ScreenY { get; private set; }

    /// <summary>
    /// Gets the primary marker type for the cluster (used for icon)
    /// Priority: Boss > Extraction > Transit > Quest > Others
    /// </summary>
    public MarkerType PrimaryType
    {
        get
        {
            if (Markers.Any(m => m.Type is MarkerType.BossSpawn or MarkerType.RaiderSpawn))
                return MarkerType.BossSpawn;
            if (Markers.Any(m => m.Type is MarkerType.PmcExtraction or MarkerType.ScavExtraction or MarkerType.SharedExtraction))
                return MarkerType.PmcExtraction;
            if (Markers.Any(m => m.Type == MarkerType.Transit))
                return MarkerType.Transit;
            return Markers.FirstOrDefault()?.Type ?? MarkerType.PmcSpawn;
        }
    }

    public void AddMarker(MapMarker marker, double sx, double sy)
    {
        Markers.Add(marker);
        // Recalculate center
        ScreenX = Markers.Count == 1 ? sx : (ScreenX * (Markers.Count - 1) + sx) / Markers.Count;
        ScreenY = Markers.Count == 1 ? sy : (ScreenY * (Markers.Count - 1) + sy) / Markers.Count;
    }
}

/// <summary>
/// Map Tracker Page - displays map with markers and real-time player position tracking
/// </summary>
public partial class MapTrackerPage : UserControl
{
    // Zoom settings
    private double _zoomLevel = 1.0;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 12.0;
    private static readonly double[] ZoomPresets = { 0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0, 12.0 };

    // Marker size constraints
    private const double MinMarkerSize = 16.0;  // Minimum marker size in pixels
    private const double MaxMarkerSize = 64.0;  // Maximum marker size in pixels
    private const double DefaultLabelShowZoomThreshold = 0.5;  // Default: show labels only when zoom >= this value

    // Overlapping marker offset
    private const double MarkerOverlapDistance = 30.0;  // Distance threshold to consider markers overlapping
    private const double MarkerOffsetRadius = 25.0;  // Offset radius for overlapping markers

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

    // Icon cache
    private static readonly Dictionary<MarkerType, BitmapImage?> _iconCache = new();

    // Screenshot watcher for player position
    private readonly MapTrackerService _watcherService = MapTrackerService.Instance;

    // Localization service
    private readonly LocalizationService _loc = LocalizationService.Instance;

    // Settings service
    private readonly SettingsService _settings = SettingsService.Instance;

    // Floor detection service
    private readonly FloorDetectionService _floorDetectionService = FloorDetectionService.Instance;
    private bool _autoFloorEnabled = true;

    // Marker hit regions for tooltip
    private readonly List<MarkerHitRegion> _markerHitRegions = new();
    private MapMarker? _hoveredMarker;
    private MarkerHitRegion? _currentTooltipHitRegion;  // For tooltip actions

    // Settings panel state
    private bool _settingsPanelOpen = false;

    // Display settings
    private double _markerScale = 1.0;
    private double _labelShowZoomThreshold = 0.5;
    private bool _showMarkerLabels = true;

    // LOD thresholds
    private const double LOD_CLUSTER_ONLY_THRESHOLD = 0.3;
    private const double LOD_SMALL_ICON_THRESHOLD = 0.6;
    private const double LOD_FULL_DETAIL_THRESHOLD = 1.0;

    /// <summary>
    /// Get LOD-based marker size multiplier
    /// </summary>
    private double GetLodSizeMultiplier()
    {
        return _zoomLevel switch
        {
            < LOD_CLUSTER_ONLY_THRESHOLD => 0.7,   // Very small at extreme zoom out
            < LOD_SMALL_ICON_THRESHOLD => 0.85,    // Small icons
            < LOD_FULL_DETAIL_THRESHOLD => 1.0,    // Normal size
            _ => 1.15                               // Larger when zoomed in
        };
    }

    /// <summary>
    /// Get dynamic cluster grid size based on zoom level
    /// </summary>
    private double GetDynamicClusterSize()
    {
        return _zoomLevel switch
        {
            < 0.3 => 100.0,  // Large clusters at extreme zoom out
            < 0.5 => 80.0,
            < 0.7 => 60.0,
            < 1.0 => 40.0,
            _ => 25.0        // Small clusters when zoomed in
        };
    }

    /// <summary>
    /// Check if labels should be shown based on LOD
    /// </summary>
    private bool ShouldShowLabelsLod()
    {
        return _showMarkerLabels && _zoomLevel >= LOD_CLUSTER_ONLY_THRESHOLD;
    }

    /// <summary>
    /// Check if clustering should be active based on zoom level
    /// </summary>
    private bool ShouldCluster()
    {
        return _clusteringEnabled && _zoomLevel < _clusterZoomThreshold;
    }

    // Clustering settings
    private bool _clusteringEnabled = true;
    private double _clusterZoomThreshold = 0.5;  // Cluster when zoom < this value
    private const double ClusterGridSize = 60.0;  // Grid size for clustering in pixels

    // Auto-follow state
    private bool _autoFollowEnabled;

    public MapTrackerPage()
    {
        InitializeComponent();
        LoadMapConfigs();
    }

    private async void MapTrackerPage_Loaded(object sender, RoutedEventArgs e)
    {
        // Load markers from database
        await MapMarkerDbService.Instance.LoadMarkersAsync();

        // Load quest objectives from database
        await QuestObjectiveDbService.Instance.LoadObjectivesAsync();

        // Load floor detection data
        await _floorDetectionService.LoadFloorRangesAsync();

        // Load settings from SettingsService
        LoadMapSettings();

        // Subscribe to language changes
        _loc.LanguageChanged += OnLanguageChanged;
        UpdateUIStrings();

        if (MapSelector.Items.Count > 0)
        {
            // Try to restore last selected map
            var lastMap = _settings.MapLastSelectedMap;
            if (!string.IsNullOrEmpty(lastMap))
            {
                var mapIndex = -1;
                for (int i = 0; i < MapSelector.Items.Count; i++)
                {
                    if (MapSelector.Items[i] is MapConfig config && config.Key == lastMap)
                    {
                        mapIndex = i;
                        break;
                    }
                }
                MapSelector.SelectedIndex = mapIndex >= 0 ? mapIndex : 0;
            }
            else
            {
                MapSelector.SelectedIndex = 0;
            }
        }

        // Subscribe to watcher events
        _watcherService.PositionDetected += OnPositionDetected;
        _watcherService.StateChanged += OnWatcherStateChanged;
        UpdateWatcherStatus();

        // Subscribe to keyboard events for floor hotkeys
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.PreviewKeyDown += Window_PreviewKeyDown;
        }
    }

    /// <summary>
    /// Load settings from SettingsService and apply to UI
    /// </summary>
    private void LoadMapSettings()
    {
        // Display settings
        _markerScale = _settings.MapMarkerScale;
        _showMarkerLabels = _settings.MapShowLabels;
        _clusteringEnabled = _settings.MapClusteringEnabled;
        _clusterZoomThreshold = _settings.MapClusterZoomThreshold / 100.0;  // Convert from 0-100 to 0-1
        _autoFloorEnabled = _settings.MapAutoFloorEnabled;

        // Layer visibility - apply to chips (checkboxes will sync via handler)
        ChipBoss.IsChecked = _settings.MapShowBosses;
        ChipExtract.IsChecked = _settings.MapShowExtracts;
        ChipTransit.IsChecked = _settings.MapShowTransits;
        ChipSpawn.IsChecked = _settings.MapShowSpawns;
        ChipLever.IsChecked = _settings.MapShowLevers;
        ChipKeys.IsChecked = _settings.MapShowKeys;
        ChipQuest.IsChecked = _settings.MapShowQuests;

        // Sync with checkboxes and update status text
        SyncLayerCheckboxes();
        UpdateChipStatusText();

        // Update settings UI
        SyncSettingsUI();
    }

    private void MapTrackerPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe from watcher events
        _watcherService.PositionDetected -= OnPositionDetected;
        _watcherService.StateChanged -= OnWatcherStateChanged;

        // Unsubscribe from language changes
        _loc.LanguageChanged -= OnLanguageChanged;

        // Unsubscribe from keyboard events
        var window = Window.GetWindow(this);
        if (window != null)
        {
            window.PreviewKeyDown -= Window_PreviewKeyDown;
        }
    }

    private void OnLanguageChanged(object? sender, AppLanguage e)
    {
        Dispatcher.Invoke(UpdateUIStrings);
    }

    /// <summary>
    /// Update all UI strings for current language
    /// </summary>
    private void UpdateUIStrings()
    {
        // Redraw markers to update localized names
        RedrawMarkers();
        RedrawObjectives();
    }

    /// <summary>
    /// Get localized marker name
    /// </summary>
    private string GetLocalizedMarkerName(MapMarker marker)
    {
        return _loc.CurrentLanguage switch
        {
            AppLanguage.KO => marker.NameKo ?? marker.Name,
            AppLanguage.JA => marker.NameJa ?? marker.Name,
            _ => marker.Name
        };
    }

    /// <summary>
    /// Get localized quest name
    /// </summary>
    private string GetLocalizedQuestName(QuestObjective objective)
    {
        return _loc.CurrentLanguage switch
        {
            AppLanguage.KO => objective.QuestNameKo ?? objective.QuestName,
            AppLanguage.JA => objective.QuestNameJa ?? objective.QuestName,
            _ => objective.QuestName
        };
    }

    private void LoadMapConfigs()
    {
        try
        {
            var configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Data", "map_configs.json");
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
                System.Diagnostics.Debug.WriteLine("Warning: map_configs.json not found");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading map configs: {ex.Message}");
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
            RedrawMarkers();
            RedrawObjectives();

            // Save last selected map
            _settings.MapLastSelectedMap = config.Key;
        }
    }

    #region Floor Management

    private void UpdateFloorSelector(MapConfig config)
    {
        FloorSelector.Items.Clear();
        StatusFloorSelector.Items.Clear();
        _currentFloorId = null;
        _sortedFloors = null;

        var floors = config.Floors;
        if (floors == null || floors.Count == 0)
        {
            FloorSelector.Visibility = Visibility.Collapsed;
            TxtFloorInfo.Visibility = Visibility.Collapsed;
            StatusFloorSelector.Visibility = Visibility.Collapsed;
            return;
        }

        FloorSelector.Visibility = Visibility.Visible;
        TxtFloorInfo.Visibility = Visibility.Visible;
        StatusFloorSelector.Visibility = Visibility.Visible;

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
            StatusFloorSelector.Items.Add(new ComboBoxItem
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
            StatusFloorSelector.SelectedIndex = defaultIndex;
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

                // Sync status bar floor selector
                if (StatusFloorSelector.SelectedIndex != FloorSelector.SelectedIndex)
                {
                    StatusFloorSelector.SelectedIndex = FloorSelector.SelectedIndex;
                }

                if (_currentMapConfig != null)
                {
                    LoadMap(_currentMapConfig, resetView: false);
                    RedrawMarkers();
                    RedrawObjectives();
                }
            }
        }
    }

    private void StatusFloorSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusFloorSelector.SelectedItem is ComboBoxItem floorItem && floorItem.Tag is string floorId)
        {
            if (_currentFloorId != floorId)
            {
                _currentFloorId = floorId;

                // Sync context panel floor selector
                if (FloorSelector.SelectedIndex != StatusFloorSelector.SelectedIndex)
                {
                    FloorSelector.SelectedIndex = StatusFloorSelector.SelectedIndex;
                }

                if (_currentMapConfig != null)
                {
                    LoadMap(_currentMapConfig, resetView: false);
                    RedrawMarkers();
                    RedrawObjectives();
                }
            }
        }
    }

    private void ChkAutoFloor_CheckedChanged(object sender, RoutedEventArgs e)
    {
        _autoFloorEnabled = ChkAutoFloor.IsChecked == true;
        _settings.MapAutoFloorEnabled = _autoFloorEnabled;  // Save to settings
        // Floor will be auto-detected on next position update
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ignore if focus is on a text input
        if (e.OriginalSource is System.Windows.Controls.TextBox)
            return;

        switch (e.Key)
        {
            // Context panel toggle (Tab)
            case Key.Tab:
                ToggleContextPanel();
                e.Handled = true;
                return;

            // Start/Stop tracking (Space)
            case Key.Space:
                WatcherToggleButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;

            // Center on player (P)
            case Key.P:
                CenterOnPlayer();
                e.Handled = true;
                return;

            // Fit to view (F)
            case Key.F:
                FitMapToView();
                e.Handled = true;
                return;

            // Reset view (R)
            case Key.R:
                SetZoom(1.0);
                CenterMapInView();
                e.Handled = true;
                return;

            // Toggle auto-follow (A)
            case Key.A:
                BtnAutoFollow.IsChecked = !BtnAutoFollow.IsChecked;
                e.Handled = true;
                return;

            // Focus search (/)
            case Key.OemQuestion:
                TxtSearch.Focus();
                e.Handled = true;
                return;

            // Zoom in (+/=)
            case Key.OemPlus:
            case Key.Add:
                SetZoom(_zoomLevel * 1.2);
                e.Handled = true;
                return;

            // Zoom out (-)
            case Key.OemMinus:
            case Key.Subtract:
                SetZoom(_zoomLevel / 1.2);
                e.Handled = true;
                return;

            // Copy coords (C) when marker selected
            case Key.C:
                if (_selectedMarker != null)
                {
                    BtnCopySelectedCoords_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
                return;

            // Go to floor (G) when marker selected
            case Key.G:
                if (_selectedMarker != null)
                {
                    BtnSelectedGoToFloor_Click(this, new RoutedEventArgs());
                }
                e.Handled = true;
                return;

            // Settings panel toggle (,)
            case Key.OemComma:
                ToggleSettingsPanel();
                e.Handled = true;
                return;

            // Help modal (F1)
            case Key.F1:
                ShortcutsPopup.Visibility = ShortcutsPopup.Visibility == Visibility.Visible
                    ? Visibility.Collapsed
                    : Visibility.Visible;
                e.Handled = true;
                return;

            // All layers ON (0)
            case Key.D0:
            case Key.NumPad0:
                BtnAllLayersOn_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;

            // All layers OFF (9)
            case Key.D9:
            case Key.NumPad9:
                BtnAllLayersOff_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;

            // Escape - clear selection, close popups
            case Key.Escape:
                if (_selectedMarker != null)
                {
                    ShowContextDefault();
                    _selectedMarker = null;
                }
                else if (ShortcutsPopup.Visibility == Visibility.Visible)
                {
                    ShortcutsPopup.Visibility = Visibility.Collapsed;
                }
                else if (_settingsPanelOpen)
                {
                    CloseSettingsPanel();
                }
                else if (!string.IsNullOrEmpty(TxtSearch.Text))
                {
                    TxtSearch.Text = "";
                }
                e.Handled = true;
                return;

            // Layer toggles (1-7)
            case Key.D1:
            case Key.NumPad1:
                ChipBoss.IsChecked = !ChipBoss.IsChecked;
                e.Handled = true;
                return;
            case Key.D2:
            case Key.NumPad2:
                ChipExtract.IsChecked = !ChipExtract.IsChecked;
                e.Handled = true;
                return;
            case Key.D3:
            case Key.NumPad3:
                ChipTransit.IsChecked = !ChipTransit.IsChecked;
                e.Handled = true;
                return;
            case Key.D4:
            case Key.NumPad4:
                ChipSpawn.IsChecked = !ChipSpawn.IsChecked;
                e.Handled = true;
                return;
            case Key.D5:
            case Key.NumPad5:
                ChipLever.IsChecked = !ChipLever.IsChecked;
                e.Handled = true;
                return;
            case Key.D6:
            case Key.NumPad6:
                ChipKeys.IsChecked = !ChipKeys.IsChecked;
                e.Handled = true;
                return;
            case Key.D7:
            case Key.NumPad7:
                ChipQuest.IsChecked = !ChipQuest.IsChecked;
                e.Handled = true;
                return;
        }
    }

    #endregion

    #region Settings Panel

    private void BtnToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        ToggleSettingsPanel();
    }

    private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
    {
        CloseSettingsPanel();
    }

    private void SettingsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CloseSettingsPanel();
    }

    private void ToggleSettingsPanel()
    {
        if (_settingsPanelOpen)
        {
            CloseSettingsPanel();
        }
        else
        {
            OpenSettingsPanel();
        }
    }

    private void OpenSettingsPanel()
    {
        _settingsPanelOpen = true;
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Visible;

        // Sync settings UI with current state
        SyncSettingsUI();
    }

    private void CloseSettingsPanel()
    {
        _settingsPanelOpen = false;
        SettingsOverlay.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void SyncSettingsUI()
    {
        // Sync Display settings
        SliderMarkerSize.Value = _markerScale * 100;
        ChkShowLabels.IsChecked = _showMarkerLabels;
        ChkEnableClustering.IsChecked = _clusteringEnabled;
        SliderClusterZoom.Value = _clusterZoomThreshold * 100;
        ChkAutoFloor.IsChecked = _autoFloorEnabled;
    }

    private void SliderMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMarkerSize == null) return;
        _markerScale = SliderMarkerSize.Value / 100.0;
        TxtMarkerSize.Text = $"{SliderMarkerSize.Value:F0}%";
        _settings.MapMarkerScale = _markerScale;  // Save to settings
        RedrawMarkers();
    }

    private void ChkShowLabels_Changed(object sender, RoutedEventArgs e)
    {
        _showMarkerLabels = ChkShowLabels.IsChecked == true;
        _settings.MapShowLabels = _showMarkerLabels;  // Save to settings
        RedrawMarkers();
        RedrawObjectives();
    }

    private void ChkEnableClustering_Changed(object sender, RoutedEventArgs e)
    {
        _clusteringEnabled = ChkEnableClustering.IsChecked == true;
        _settings.MapClusteringEnabled = _clusteringEnabled;  // Save to settings
        RedrawMarkers();
    }

    private void SliderClusterZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtClusterZoom == null) return;
        _clusterZoomThreshold = SliderClusterZoom.Value / 100.0;
        TxtClusterZoom.Text = $"{SliderClusterZoom.Value:F0}%";
        _settings.MapClusterZoomThreshold = SliderClusterZoom.Value;  // Save to settings (0-100)
        RedrawMarkers();
    }

    #endregion

    #region View Controls

    private void BtnFitView_Click(object sender, RoutedEventArgs e)
    {
        FitMapToView();
    }

    private void FitMapToView()
    {
        if (_currentMapConfig == null) return;

        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        if (viewerWidth <= 0 || viewerHeight <= 0) return;

        // Calculate zoom level to fit map in view with padding
        var padding = 40;
        var availableWidth = viewerWidth - padding * 2;
        var availableHeight = viewerHeight - padding * 2;

        var zoomX = availableWidth / _currentMapConfig.ImageWidth;
        var zoomY = availableHeight / _currentMapConfig.ImageHeight;
        var newZoom = Math.Min(zoomX, zoomY);

        // Clamp to valid range
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);

        SetZoom(newZoom);
        CenterMapInView();
    }

    private void BtnCenterPlayer_Click(object sender, RoutedEventArgs e)
    {
        CenterOnPlayer();
    }

    private void CenterOnPlayer()
    {
        if (_currentMapConfig == null) return;

        var position = _watcherService.CurrentPosition;
        if (position == null)
        {
            return;
        }

        // Convert player position to screen coordinates
        var (screenX, screenY) = _currentMapConfig.GameToScreenForPlayer(position.X, position.Z);

        // Center view on player position
        var viewerWidth = MapViewerGrid.ActualWidth;
        var viewerHeight = MapViewerGrid.ActualHeight;

        MapTranslate.X = viewerWidth / 2 - screenX * _zoomLevel;
        MapTranslate.Y = viewerHeight / 2 - screenY * _zoomLevel;
    }

    private void BtnAllLayersOn_Click(object sender, RoutedEventArgs e)
    {
        ChipBoss.IsChecked = true;
        ChipExtract.IsChecked = true;
        ChipTransit.IsChecked = true;
        ChipSpawn.IsChecked = true;
        ChipLever.IsChecked = true;
        ChipKeys.IsChecked = true;
        ChipQuest.IsChecked = true;
    }

    private void BtnAllLayersOff_Click(object sender, RoutedEventArgs e)
    {
        ChipBoss.IsChecked = false;
        ChipExtract.IsChecked = false;
        ChipTransit.IsChecked = false;
        ChipSpawn.IsChecked = false;
        ChipLever.IsChecked = false;
        ChipKeys.IsChecked = false;
        ChipQuest.IsChecked = false;
    }

    private void BtnShowShortcuts_Click(object sender, RoutedEventArgs e)
    {
        ShortcutsPopup.Visibility = ShortcutsPopup.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void BtnCloseShortcuts_Click(object sender, RoutedEventArgs e)
    {
        ShortcutsPopup.Visibility = Visibility.Collapsed;
    }

    private void BtnAutoFollow_Checked(object sender, RoutedEventArgs e)
    {
        _autoFollowEnabled = true;
        BtnAutoFollow.Background = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4));
        BtnAutoFollow.Foreground = Brushes.White;

        // Immediately center on player if position available
        var position = _watcherService.CurrentPosition;
        if (position != null)
        {
            CenterOnPlayer();
        }
    }

    private void BtnAutoFollow_Unchecked(object sender, RoutedEventArgs e)
    {
        _autoFollowEnabled = false;
        BtnAutoFollow.Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42));
        BtnAutoFollow.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
    }

    #endregion

    #region Context Panel

    private bool _contextPanelOpen = true;

    private void ToggleContextPanel()
    {
        _contextPanelOpen = !_contextPanelOpen;
        ContextPanelColumn.Width = _contextPanelOpen ? new GridLength(280) : new GridLength(0);
        ContextPanel.Visibility = _contextPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        TxtContextToggle.Text = _contextPanelOpen ? "▶" : "◀";
    }

    private void BtnToggleContextPanel_Click(object sender, RoutedEventArgs e)
    {
        ToggleContextPanel();
    }

    private void ShowContextDefault()
    {
        ContextDefault.Visibility = Visibility.Visible;
        ContextSelected.Visibility = Visibility.Collapsed;
        ContextSearch.Visibility = Visibility.Collapsed;
    }

    private void ShowContextSelected(MapMarker marker)
    {
        ContextDefault.Visibility = Visibility.Collapsed;
        ContextSelected.Visibility = Visibility.Visible;
        ContextSearch.Visibility = Visibility.Collapsed;

        // Update selected marker info
        SelectedMarkerName.Text = GetLocalizedMarkerName(marker);
        SelectedMarkerType.Text = _loc.GetMarkerTypeName(marker.Type);
        SelectedMarkerCoords.Text = $"X: {marker.X:F1}  Z: {marker.Z:F1}";

        // Set marker icon color
        var (r, g, b) = MapMarker.GetMarkerColor(marker.Type);
        SelectedMarkerIcon.Foreground = new SolidColorBrush(Color.FromRgb(r, g, b));
        SelectedMarkerIcon.Text = GetMarkerIconText(marker.Type);

        // Floor info
        if (!string.IsNullOrEmpty(marker.FloorId) && _sortedFloors != null)
        {
            var floor = _sortedFloors.FirstOrDefault(f =>
                string.Equals(f.LayerId, marker.FloorId, StringComparison.OrdinalIgnoreCase));
            SelectedMarkerFloor.Text = floor?.DisplayName ?? marker.FloorId;
            SelectedMarkerFloorLabel.Visibility = Visibility.Visible;
            SelectedMarkerFloor.Visibility = Visibility.Visible;
            BtnSelectedGoToFloor.Visibility =
                !string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            SelectedMarkerFloorLabel.Visibility = Visibility.Collapsed;
            SelectedMarkerFloor.Visibility = Visibility.Collapsed;
            BtnSelectedGoToFloor.Visibility = Visibility.Collapsed;
        }

        _selectedMarker = marker;
    }

    private MapMarker? _selectedMarker;

    private static string GetMarkerIconText(MarkerType type) => type switch
    {
        MarkerType.PmcExtraction => "▲",
        MarkerType.ScavExtraction => "▲",
        MarkerType.SharedExtraction => "▲",
        MarkerType.Transit => "■",
        MarkerType.PmcSpawn => "●",
        MarkerType.ScavSpawn => "●",
        MarkerType.BossSpawn => "☠",
        MarkerType.RaiderSpawn => "☠",
        MarkerType.Lever => "⚙",
        MarkerType.Keys => "🔑",
        _ => "●"
    };

    private void ShowContextSearch()
    {
        ContextDefault.Visibility = Visibility.Collapsed;
        ContextSelected.Visibility = Visibility.Collapsed;
        ContextSearch.Visibility = Visibility.Visible;
    }

    private void BtnBackToDefault_Click(object sender, RoutedEventArgs e)
    {
        ShowContextDefault();
        _selectedMarker = null;
    }

    private void BtnCopySelectedCoords_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMarker == null) return;

        var coordText = $"X: {_selectedMarker.X:F1}, Z: {_selectedMarker.Z:F1}";
        try
        {
            System.Windows.Clipboard.SetText(coordText);
            BtnCopySelectedCoords.Content = "Copied!";
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            timer.Tick += (_, _) =>
            {
                BtnCopySelectedCoords.Content = "Copy";
                timer.Stop();
            };
            timer.Start();
        }
        catch
        {
            // Clipboard may fail in some scenarios
        }
    }

    private void BtnSelectedGoToFloor_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMarker == null || string.IsNullOrEmpty(_selectedMarker.FloorId) || _sortedFloors == null)
            return;

        var floor = _sortedFloors.FirstOrDefault(f =>
            string.Equals(f.LayerId, _selectedMarker.FloorId, StringComparison.OrdinalIgnoreCase));

        if (floor != null)
        {
            var floorIndex = _sortedFloors.IndexOf(floor);
            if (floorIndex >= 0)
            {
                FloorSelector.SelectedIndex = floorIndex;
            }
        }
    }

    #endregion

    #region Search

    private List<SearchResult> _searchResults = new();

    private class SearchResult
    {
        public string Name { get; set; } = "";
        public string TypeAndFloor { get; set; } = "";
        public MapMarker Marker { get; set; } = null!;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = TxtSearch.Text.Trim();

        BtnClearSearch.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

        if (string.IsNullOrEmpty(query))
        {
            ShowContextDefault();
            return;
        }

        PerformSearch(query);
        ShowContextSearch();
    }

    private void PerformSearch(string query)
    {
        if (_currentMapConfig == null)
        {
            _searchResults.Clear();
            SearchResultsList.ItemsSource = null;
            TxtSearchResultCount.Text = "RESULTS (0)";
            return;
        }

        var markers = MapMarkerDbService.Instance.GetMarkersForMap(_currentMapConfig.Key);
        var lowerQuery = query.ToLowerInvariant();

        _searchResults = markers
            .Where(m => GetLocalizedMarkerName(m).ToLowerInvariant().Contains(lowerQuery) ||
                       _loc.GetMarkerTypeName(m.Type).ToLowerInvariant().Contains(lowerQuery))
            .Take(20)
            .Select(m =>
            {
                var floorInfo = "";
                if (!string.IsNullOrEmpty(m.FloorId) && _sortedFloors != null)
                {
                    var floor = _sortedFloors.FirstOrDefault(f =>
                        string.Equals(f.LayerId, m.FloorId, StringComparison.OrdinalIgnoreCase));
                    floorInfo = floor != null ? $" • {floor.DisplayName}" : $" • {m.FloorId}";
                }
                return new SearchResult
                {
                    Name = GetLocalizedMarkerName(m),
                    TypeAndFloor = $"{_loc.GetMarkerTypeName(m.Type)}{floorInfo}",
                    Marker = m
                };
            })
            .ToList();

        SearchResultsList.ItemsSource = _searchResults;
        TxtSearchResultCount.Text = $"RESULTS ({_searchResults.Count})";
    }

    private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
    {
        TxtSearch.Text = "";
        ShowContextDefault();
    }

    private void SearchResult_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is SearchResult result)
        {
            // Pan to marker
            if (_currentMapConfig != null)
            {
                var (sx, sy) = _currentMapConfig.GameToScreen(result.Marker.X, result.Marker.Z);

                var viewerWidth = MapViewerGrid.ActualWidth;
                var viewerHeight = MapViewerGrid.ActualHeight;

                MapTranslate.X = viewerWidth / 2 - sx * _zoomLevel;
                MapTranslate.Y = viewerHeight / 2 - sy * _zoomLevel;

                // Go to marker's floor if different
                if (!string.IsNullOrEmpty(result.Marker.FloorId) &&
                    !string.Equals(result.Marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) &&
                    _sortedFloors != null)
                {
                    var floorIndex = _sortedFloors.FindIndex(f =>
                        string.Equals(f.LayerId, result.Marker.FloorId, StringComparison.OrdinalIgnoreCase));
                    if (floorIndex >= 0)
                    {
                        FloorSelector.SelectedIndex = floorIndex;
                    }
                }
            }

            // Show marker details
            ShowContextSelected(result.Marker);
            TxtSearch.Text = "";
        }
    }

    #endregion

    #region Map Loading

    private void LoadMap(MapConfig config, bool resetView = true)
    {
        try
        {
            NoMapMessage.Visibility = Visibility.Collapsed;

            var svgPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Maps", config.SvgFileName);

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

                // Set default floor as dimmed background when viewing other floors
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

                var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"map_tracker_{Guid.NewGuid()}.svg");
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
                    RedrawMarkers();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                RedrawMarkers();
            }

            // Update map title in header
            TxtMapTitle.Text = config.DisplayName;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load map: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Layer Visibility

    private void LayerChip_Changed(object sender, RoutedEventArgs e)
    {
        // Sync layer chips with context panel checkboxes
        SyncLayerCheckboxes();

        // Update chip status text
        UpdateChipStatusText();

        // Save layer visibility to settings
        SaveLayerSettings();

        // Redraw
        RedrawMarkers();
        RedrawObjectives();
        UpdateCounts();
    }

    private void LayerVisibility_Changed(object sender, RoutedEventArgs e)
    {
        // Sync context panel checkboxes with layer chips
        SyncLayerChips();

        // Save layer visibility to settings
        SaveLayerSettings();

        // Redraw
        RedrawMarkers();
        RedrawObjectives();
        UpdateCounts();
    }

    /// <summary>
    /// Save layer visibility settings to SettingsService
    /// </summary>
    private void SaveLayerSettings()
    {
        // Guard against calls during XAML initialization
        if (ChipBoss == null || _settings == null) return;

        _settings.MapShowBosses = ChipBoss.IsChecked == true;
        _settings.MapShowExtracts = ChipExtract.IsChecked == true;
        _settings.MapShowTransits = ChipTransit.IsChecked == true;
        _settings.MapShowSpawns = ChipSpawn.IsChecked == true;
        _settings.MapShowLevers = ChipLever.IsChecked == true;
        _settings.MapShowKeys = ChipKeys.IsChecked == true;
        _settings.MapShowQuests = ChipQuest.IsChecked == true;
    }

    private void SyncLayerCheckboxes()
    {
        // Guard against calls during XAML initialization
        if (ChkShowExtractions == null) return;

        ChkShowBosses.IsChecked = ChipBoss.IsChecked;
        ChkShowExtractions.IsChecked = ChipExtract.IsChecked;
        ChkShowTransits.IsChecked = ChipTransit.IsChecked;
        ChkShowSpawns.IsChecked = ChipSpawn.IsChecked;
        ChkShowLevers.IsChecked = ChipLever.IsChecked;
        ChkShowKeys.IsChecked = ChipKeys.IsChecked;
        ChkShowObjectives.IsChecked = ChipQuest.IsChecked;
    }

    private void SyncLayerChips()
    {
        // Guard against calls during XAML initialization
        if (ChipBoss == null) return;

        ChipBoss.IsChecked = ChkShowBosses.IsChecked;
        ChipExtract.IsChecked = ChkShowExtractions.IsChecked;
        ChipTransit.IsChecked = ChkShowTransits.IsChecked;
        ChipSpawn.IsChecked = ChkShowSpawns.IsChecked;
        ChipLever.IsChecked = ChkShowLevers.IsChecked;
        ChipKeys.IsChecked = ChkShowKeys.IsChecked;
        ChipQuest.IsChecked = ChkShowObjectives.IsChecked;

        UpdateChipStatusText();
    }

    private void UpdateChipStatusText()
    {
        // Guard against calls during XAML initialization
        if (ChipBossStatus == null) return;

        ChipBossStatus.Text = ChipBoss.IsChecked == true ? "ON" : "OFF";
        ChipExtractStatus.Text = ChipExtract.IsChecked == true ? "ON" : "OFF";
        ChipTransitStatus.Text = ChipTransit.IsChecked == true ? "ON" : "OFF";
        ChipSpawnStatus.Text = ChipSpawn.IsChecked == true ? "ON" : "OFF";
        ChipLeverStatus.Text = ChipLever.IsChecked == true ? "ON" : "OFF";
        ChipKeysStatus.Text = ChipKeys.IsChecked == true ? "ON" : "OFF";
        ChipQuestStatus.Text = ChipQuest.IsChecked == true ? "ON" : "OFF";
    }

    private bool ShouldShowMarkerType(MarkerType type)
    {
        // Guard against calls during XAML initialization
        if (ChipBoss == null) return true;

        return type switch
        {
            MarkerType.PmcExtraction => ChipExtract.IsChecked == true,
            MarkerType.ScavExtraction => ChipExtract.IsChecked == true,
            MarkerType.SharedExtraction => ChipExtract.IsChecked == true,
            MarkerType.Transit => ChipTransit.IsChecked == true,
            MarkerType.PmcSpawn => ChipSpawn.IsChecked == true,
            MarkerType.ScavSpawn => ChipSpawn.IsChecked == true,
            MarkerType.BossSpawn => ChipBoss.IsChecked == true,
            MarkerType.RaiderSpawn => ChipBoss.IsChecked == true,
            MarkerType.Lever => ChipLever.IsChecked == true,
            MarkerType.Keys => ChipKeys.IsChecked == true,
            _ => true
        };
    }

    private bool ShouldShowQuestObjectives()
    {
        // Guard against calls during XAML initialization
        if (ChipQuest == null) return true;
        return ChipQuest.IsChecked == true;
    }

    #endregion

    #region Marker Drawing

    /// <summary>
    /// Calculate offset for overlapping markers using circular distribution
    /// </summary>
    private static (double offsetX, double offsetY) CalculateMarkerOffset(int index, int total, double radius)
    {
        if (total <= 1) return (0, 0);

        // Distribute markers in a circle
        double angle = 2 * Math.PI * index / total;
        double offsetX = Math.Cos(angle) * radius;
        double offsetY = Math.Sin(angle) * radius;
        return (offsetX, offsetY);
    }

    /// <summary>
    /// Group markers that are close together
    /// </summary>
    private Dictionary<(int, int), List<(MapMarker marker, double sx, double sy)>> GroupOverlappingMarkers(
        IEnumerable<(MapMarker marker, double sx, double sy)> markersWithCoords, double gridSize)
    {
        var groups = new Dictionary<(int, int), List<(MapMarker marker, double sx, double sy)>>();

        foreach (var item in markersWithCoords)
        {
            // Use grid-based grouping for efficiency
            var gridX = (int)(item.sx / gridSize);
            var gridY = (int)(item.sy / gridSize);
            var key = (gridX, gridY);

            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<(MapMarker, double, double)>();
                groups[key] = list;
            }
            list.Add(item);
        }

        return groups;
    }

    private void UpdateCounts()
    {
        if (_currentMapConfig == null)
        {
            TxtMarkerCount.Text = "0";
            TotalMarkerCountText.Text = "/0";
            TxtQuestCount.Text = "0";
            MarkerCountText.Text = "0";
            return;
        }

        var markers = MapMarkerDbService.Instance.GetMarkersForMap(_currentMapConfig.Key);
        var totalCount = markers.Count;
        var visibleCount = markers.Count(m => ShouldShowMarkerType(m.Type));

        // Update context panel
        TxtMarkerCount.Text = visibleCount.ToString();

        // Update status bar
        MarkerCountText.Text = visibleCount.ToString();
        TotalMarkerCountText.Text = $"/{totalCount}";

        var objectives = QuestObjectiveDbService.Instance.GetObjectivesForMap(_currentMapConfig.Key, _currentMapConfig);
        TxtQuestCount.Text = objectives.Count.ToString();
    }

    private BitmapImage? GetMarkerIcon(MarkerType markerType)
    {
        if (_iconCache.TryGetValue(markerType, out var cachedIcon))
        {
            return cachedIcon;
        }

        try
        {
            var iconFileName = GetIconFileName(markerType);
            var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "DB", "Icons", iconFileName);

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

    private static string GetIconFileName(MarkerType type)
    {
        return type switch
        {
            MarkerType.PmcSpawn => "PMC Spawn.webp",
            MarkerType.ScavSpawn => "SCAV Spawn.webp",
            MarkerType.PmcExtraction => "PMC Extraction.webp",
            MarkerType.ScavExtraction => "SCAV Extraction.webp",
            MarkerType.SharedExtraction => "PMC Extraction.webp",
            MarkerType.Transit => "Transit.webp",
            MarkerType.BossSpawn => "BOSS Spawn.webp",
            MarkerType.RaiderSpawn => "Raider Spawn.webp",
            MarkerType.Lever => "Lever.webp",
            MarkerType.Keys => "Keys.webp",
            _ => "PMC Spawn.webp"
        };
    }

    private void RedrawMarkers()
    {
        // Guard against calls during XAML initialization
        if (MarkersCanvas == null || TxtMarkerCount == null) return;

        MarkersCanvas.Children.Clear();
        _markerHitRegions.Clear();

        if (_currentMapConfig == null) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        // Determine if labels should be shown based on LOD
        bool showLabels = ShouldShowLabelsLod();

        // Check if clustering should be active
        bool shouldCluster = ShouldCluster();

        // Get dynamic cluster size based on zoom level
        double clusterGridSize = GetDynamicClusterSize() * inverseScale;

        var markers = MapMarkerDbService.Instance.GetMarkersForMap(_currentMapConfig.Key);

        // Pre-calculate screen coordinates for all visible markers
        var visibleMarkersWithCoords = new List<(MapMarker marker, double sx, double sy)>();
        foreach (var marker in markers)
        {
            if (!ShouldShowMarkerType(marker.Type))
                continue;

            var (sx, sy) = _currentMapConfig.GameToScreen(marker.X, marker.Z);
            visibleMarkersWithCoords.Add((marker, sx, sy));
        }

        int visibleCount = visibleMarkersWithCoords.Count;

        // Use clustering or individual markers based on zoom level
        if (shouldCluster && visibleMarkersWithCoords.Count > 1)
        {
            // Compute clusters with dynamic grid size
            var clusters = ComputeClusters(visibleMarkersWithCoords, clusterGridSize);

            foreach (var cluster in clusters)
            {
                if (cluster.Markers.Count == 1)
                {
                    // Single marker - draw normally
                    var marker = cluster.Markers[0];
                    DrawSingleMarker(marker, cluster.ScreenX, cluster.ScreenY, inverseScale, hasFloors, showLabels);
                }
                else
                {
                    // Multiple markers - draw cluster
                    DrawCluster(cluster, inverseScale, hasFloors);
                }
            }
        }
        else
        {
            // No clustering - draw markers individually with overlap handling
            // Group overlapping markers
            var overlapGroupSize = MarkerOverlapDistance * inverseScale;
            var markerGroups = GroupOverlappingMarkers(visibleMarkersWithCoords, overlapGroupSize);

            // Calculate offsets for overlapping markers
            var markerOffsets = new Dictionary<MapMarker, (double offsetX, double offsetY)>();
            foreach (var group in markerGroups.Values)
            {
                if (group.Count > 1)
                {
                    var offsetRadius = MarkerOffsetRadius * inverseScale;
                    for (int i = 0; i < group.Count; i++)
                    {
                        var (offsetX, offsetY) = CalculateMarkerOffset(i, group.Count, offsetRadius);
                        markerOffsets[group[i].marker] = (offsetX, offsetY);
                    }
                }
            }

            foreach (var (marker, baseSx, baseSy) in visibleMarkersWithCoords)
            {
                // Apply offset for overlapping markers
                var (offsetX, offsetY) = markerOffsets.TryGetValue(marker, out var offset) ? offset : (0.0, 0.0);
                var sx = baseSx + offsetX;
                var sy = baseSy + offsetY;

                DrawSingleMarker(marker, sx, sy, inverseScale, hasFloors, showLabels);
            }
        }

        MarkerCountText.Text = visibleCount.ToString();
    }

    /// <summary>
    /// Compute clusters using grid-based algorithm
    /// </summary>
    private List<MarkerCluster> ComputeClusters(List<(MapMarker marker, double sx, double sy)> markersWithCoords, double gridSize)
    {
        var grid = new Dictionary<(int, int), MarkerCluster>();

        foreach (var (marker, sx, sy) in markersWithCoords)
        {
            var gridX = (int)(sx / gridSize);
            var gridY = (int)(sy / gridSize);
            var key = (gridX, gridY);

            if (!grid.TryGetValue(key, out var cluster))
            {
                cluster = new MarkerCluster();
                grid[key] = cluster;
            }
            cluster.AddMarker(marker, sx, sy);
        }

        return grid.Values.ToList();
    }

    /// <summary>
    /// Draw a cluster with count badge
    /// </summary>
    private void DrawCluster(MarkerCluster cluster, double inverseScale, bool hasFloors)
    {
        var sx = cluster.ScreenX;
        var sy = cluster.ScreenY;

        // Use primary type for color and icon
        var primaryType = cluster.PrimaryType;
        var (r, g, b) = MapMarker.GetMarkerColor(primaryType);
        var markerColor = Color.FromRgb(r, g, b);

        // Cluster marker size (slightly larger than individual markers)
        var markerSize = Math.Clamp(56 * inverseScale * _markerScale, MinMarkerSize * inverseScale, MaxMarkerSize * inverseScale * _markerScale);

        var iconImage = GetMarkerIcon(primaryType);

        if (iconImage != null)
        {
            var image = new Image
            {
                Source = iconImage,
                Width = markerSize,
                Height = markerSize,
                Opacity = 0.9
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
                Stroke = Brushes.White,
                StrokeThickness = Math.Max(1, 3 * inverseScale)
            };

            Canvas.SetLeft(circle, sx - markerSize / 2);
            Canvas.SetTop(circle, sy - markerSize / 2);
            MarkersCanvas.Children.Add(circle);
        }

        // Draw count badge
        var badgeSize = Math.Max(16, 24 * inverseScale);
        var badgeX = sx + markerSize / 2 - badgeSize / 2;
        var badgeY = sy - markerSize / 2 - badgeSize / 4;

        var badge = new Ellipse
        {
            Width = badgeSize,
            Height = badgeSize,
            Fill = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            Stroke = new SolidColorBrush(markerColor),
            StrokeThickness = Math.Max(1, 2 * inverseScale)
        };
        Canvas.SetLeft(badge, badgeX);
        Canvas.SetTop(badge, badgeY);
        MarkersCanvas.Children.Add(badge);

        // Count text
        var countText = new TextBlock
        {
            Text = cluster.Markers.Count.ToString(),
            Foreground = Brushes.White,
            FontSize = Math.Max(9, 14 * inverseScale),
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        };
        countText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(countText, badgeX + badgeSize / 2 - countText.DesiredSize.Width / 2);
        Canvas.SetTop(countText, badgeY + badgeSize / 2 - countText.DesiredSize.Height / 2);
        MarkersCanvas.Children.Add(countText);

        // Register hit region for the entire cluster (includes all markers for tooltip)
        _markerHitRegions.Add(new MarkerHitRegion
        {
            Marker = cluster.Markers[0],
            ClusterMarkers = cluster.Markers,
            ScreenX = sx,
            ScreenY = sy,
            Radius = markerSize / 2 + 4 * inverseScale
        });
    }

    /// <summary>
    /// Draw a single marker
    /// </summary>
    private void DrawSingleMarker(MapMarker marker, double sx, double sy, double inverseScale, bool hasFloors, bool showLabels)
    {
        // Determine opacity based on floor
        double opacity = 1.0;
        if (hasFloors && _currentFloorId != null && marker.FloorId != null)
        {
            opacity = string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.3;
        }

        // Apply additional opacity reduction at extreme zoom out for LOD
        if (_zoomLevel < LOD_CLUSTER_ONLY_THRESHOLD)
        {
            opacity *= 0.8;  // Reduce opacity at extreme zoom out
        }

        var (r, g, b) = MapMarker.GetMarkerColor(marker.Type);
        var markerColor = Color.FromArgb((byte)(opacity * 255), r, g, b);

        // Calculate marker size with min/max constraints, user scale, and LOD multiplier
        var lodMultiplier = GetLodSizeMultiplier();
        var rawMarkerSize = 48 * inverseScale * _markerScale * lodMultiplier;
        var markerSize = Math.Clamp(rawMarkerSize, MinMarkerSize * inverseScale, MaxMarkerSize * inverseScale * _markerScale);

        var iconImage = GetMarkerIcon(marker.Type);

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
            var strokeThickness = Math.Max(1, 3 * inverseScale);
            var circle = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Fill = new SolidColorBrush(markerColor),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                StrokeThickness = strokeThickness
            };

            Canvas.SetLeft(circle, sx - markerSize / 2);
            Canvas.SetTop(circle, sy - markerSize / 2);
            MarkersCanvas.Children.Add(circle);

            // Icon text (only if marker is large enough)
            if (markerSize >= 20 * inverseScale)
            {
                var iconText = marker.Type switch
                {
                    MarkerType.PmcSpawn => "P",
                    MarkerType.ScavSpawn => "S",
                    MarkerType.PmcExtraction => "E",
                    MarkerType.ScavExtraction => "E",
                    MarkerType.SharedExtraction => "E",
                    MarkerType.Transit => "T",
                    MarkerType.BossSpawn => "B",
                    MarkerType.RaiderSpawn => "R",
                    MarkerType.Lever => "L",
                    MarkerType.Keys => "K",
                    _ => "?"
                };

                var fontSize = Math.Max(8, 24 * inverseScale);
                var icon = new TextBlock
                {
                    Text = iconText,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 255, 255, 255)),
                    FontSize = fontSize,
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Center
                };

                icon.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(icon, sx - icon.DesiredSize.Width / 2);
                Canvas.SetTop(icon, sy - icon.DesiredSize.Height / 2);
                MarkersCanvas.Children.Add(icon);
            }
        }

        // Register hit region for tooltip
        _markerHitRegions.Add(new MarkerHitRegion
        {
            Marker = marker,
            ScreenX = sx,
            ScreenY = sy,
            Radius = markerSize / 2 + 4 * inverseScale
        });

        // Name label (localized) - only show when zoomed in enough
        if (showLabels)
        {
            var fontSize = Math.Max(10, 28 * inverseScale);
            var nameLabel = new TextBlock
            {
                Text = GetLocalizedMarkerName(marker),
                Foreground = new SolidColorBrush(markerColor),
                FontSize = fontSize,
                FontWeight = FontWeights.SemiBold
            };

            Canvas.SetLeft(nameLabel, sx + markerSize / 2 + 8 * inverseScale);
            Canvas.SetTop(nameLabel, sy - fontSize / 2);
            MarkersCanvas.Children.Add(nameLabel);

            // Floor label (if different floor)
            if (hasFloors && marker.FloorId != null && opacity < 1.0)
            {
                var floorDisplayName = _sortedFloors?
                    .FirstOrDefault(f => string.Equals(f.LayerId, marker.FloorId, StringComparison.OrdinalIgnoreCase))
                    ?.DisplayName ?? marker.FloorId;

                var floorFontSize = Math.Max(8, 20 * inverseScale);
                var floorLabel = new TextBlock
                {
                    Text = $"[{floorDisplayName}]",
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), 154, 136, 102)),
                    FontSize = floorFontSize,
                    FontStyle = FontStyles.Italic
                };

                Canvas.SetLeft(floorLabel, sx + markerSize / 2 + 8 * inverseScale);
                Canvas.SetTop(floorLabel, sy + fontSize / 2 + 2 * inverseScale);
                MarkersCanvas.Children.Add(floorLabel);
            }
        }
    }

    #endregion

    #region Quest Objectives Drawing

    private void RedrawObjectives()
    {
        // Guard against calls during XAML initialization
        if (ObjectivesCanvas == null || TxtQuestCount == null) return;

        ObjectivesCanvas.Children.Clear();

        if (_currentMapConfig == null) return;
        if (!ShouldShowQuestObjectives()) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var objectives = QuestObjectiveDbService.Instance.GetObjectivesForMap(_currentMapConfig.Key, _currentMapConfig);

        int count = 0;
        foreach (var objective in objectives)
        {
            count++;

            // Draw LocationPoints (polygon, line, or single point)
            if (objective.HasCoordinates)
            {
                DrawObjectiveLocationPoints(objective, inverseScale, hasFloors);
            }

            // Draw OptionalPoints (OR locations)
            if (objective.HasOptionalPoints)
            {
                DrawObjectiveOptionalPoints(objective, inverseScale, hasFloors);
            }
        }

        TxtQuestCount.Text = count.ToString();
    }

    private void DrawObjectiveLocationPoints(QuestObjective objective, double inverseScale, bool hasFloors)
    {
        var points = objective.LocationPoints;
        if (points.Count == 0) return;

        // Determine opacity based on floor
        double opacity = 1.0;
        if (hasFloors && _currentFloorId != null)
        {
            var pointFloor = points[0].FloorId;
            if (pointFloor != null && !string.Equals(pointFloor, _currentFloorId, StringComparison.OrdinalIgnoreCase))
            {
                opacity = 0.3;
            }
        }

        // Objective color - amber/yellow (#FFC107)
        var objectiveColor = Color.FromArgb((byte)(opacity * 255), 0xFF, 0xC1, 0x07);

        // Get localized quest name
        var questName = GetLocalizedQuestName(objective);
        bool showLabels = _showMarkerLabels && _zoomLevel >= _labelShowZoomThreshold;

        if (points.Count == 1)
        {
            // Single point - draw diamond marker with "!"
            DrawObjectivePointMarker(points[0], inverseScale, objectiveColor, opacity, questName, showLabels);
        }
        else if (points.Count == 2)
        {
            // Two points - draw dashed line between them
            DrawObjectiveLine(points[0], points[1], inverseScale, objectiveColor, questName, showLabels);
        }
        else
        {
            // 3+ points - draw dashed polygon
            DrawObjectivePolygon(points, inverseScale, objectiveColor, questName, showLabels);
        }
    }

    private void DrawObjectivePointMarker(LocationPoint point, double inverseScale, Color color, double opacity, string questName, bool showLabel)
    {
        var (sx, sy) = _currentMapConfig!.GameToScreen(point.X, point.Z);
        var markerSize = 32 * inverseScale;

        // Diamond shape
        var diamond = new Polygon
        {
            Points = new PointCollection
            {
                new Point(sx, sy - markerSize / 2),
                new Point(sx + markerSize / 2, sy),
                new Point(sx, sy + markerSize / 2),
                new Point(sx - markerSize / 2, sy)
            },
            Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), color.R, color.G, color.B)),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2 * inverseScale
        };
        ObjectivesCanvas.Children.Add(diamond);

        // "!" exclamation mark
        var exclamation = new TextBlock
        {
            Text = "!",
            Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0, 0, 0)),
            FontSize = 20 * inverseScale,
            FontWeight = FontWeights.Bold
        };
        exclamation.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(exclamation, sx - exclamation.DesiredSize.Width / 2);
        Canvas.SetTop(exclamation, sy - exclamation.DesiredSize.Height / 2);
        ObjectivesCanvas.Children.Add(exclamation);

        // Quest name label (only show when zoomed in enough)
        if (showLabel && !string.IsNullOrEmpty(questName))
        {
            var fontSize = Math.Max(10, 24 * inverseScale);
            DrawQuestNameLabel(sx + markerSize / 2 + 8 * inverseScale, sy - fontSize / 2, questName, color, fontSize, opacity);
        }
    }

    private void DrawObjectiveLine(LocationPoint p1, LocationPoint p2, double inverseScale, Color color, string questName, bool showLabel)
    {
        var (sx1, sy1) = _currentMapConfig!.GameToScreen(p1.X, p1.Z);
        var (sx2, sy2) = _currentMapConfig!.GameToScreen(p2.X, p2.Z);

        var line = new Line
        {
            X1 = sx1,
            Y1 = sy1,
            X2 = sx2,
            Y2 = sy2,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 3 * inverseScale,
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        ObjectivesCanvas.Children.Add(line);

        // Draw endpoint markers
        DrawSmallPointMarker(sx1, sy1, inverseScale, color);
        DrawSmallPointMarker(sx2, sy2, inverseScale, color);

        // Quest name label at midpoint
        if (showLabel && !string.IsNullOrEmpty(questName))
        {
            var midX = (sx1 + sx2) / 2;
            var midY = (sy1 + sy2) / 2;
            var fontSize = Math.Max(10, 24 * inverseScale);
            DrawQuestNameLabel(midX, midY - fontSize - 4 * inverseScale, questName, color, fontSize, 1.0);
        }
    }

    private void DrawObjectivePolygon(List<LocationPoint> points, double inverseScale, Color color, string questName, bool showLabel)
    {
        var screenPoints = new PointCollection();
        foreach (var p in points)
        {
            var (sx, sy) = _currentMapConfig!.GameToScreen(p.X, p.Z);
            screenPoints.Add(new Point(sx, sy));
        }

        var polygon = new Polygon
        {
            Points = screenPoints,
            Fill = new SolidColorBrush(Color.FromArgb(50, color.R, color.G, color.B)),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = 2 * inverseScale,
            StrokeDashArray = new DoubleCollection { 4, 2 }
        };
        ObjectivesCanvas.Children.Add(polygon);

        // Draw vertex markers
        foreach (var sp in screenPoints)
        {
            DrawSmallPointMarker(sp.X, sp.Y, inverseScale, color);
        }

        // Quest name label at centroid
        if (showLabel && !string.IsNullOrEmpty(questName))
        {
            // Calculate centroid
            var centroidX = screenPoints.Average(p => p.X);
            var centroidY = screenPoints.Average(p => p.Y);
            var fontSize = Math.Max(10, 24 * inverseScale);
            DrawQuestNameLabel(centroidX, centroidY, questName, color, fontSize, 1.0, true);
        }
    }

    private void DrawSmallPointMarker(double sx, double sy, double inverseScale, Color color)
    {
        var size = 8 * inverseScale;
        var circle = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.White,
            StrokeThickness = 1 * inverseScale
        };
        Canvas.SetLeft(circle, sx - size / 2);
        Canvas.SetTop(circle, sy - size / 2);
        ObjectivesCanvas.Children.Add(circle);
    }

    /// <summary>
    /// Draw quest name label with semi-transparent background for readability
    /// </summary>
    private void DrawQuestNameLabel(double x, double y, string questName, Color color, double fontSize, double opacity, bool centerText = false)
    {
        // Create background border for readability
        var label = new TextBlock
        {
            Text = questName,
            Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold
        };
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Background for better readability
        var bgPadding = 4.0;
        var bgRect = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb((byte)(opacity * 180), 0x1A, 0x1A, 0x1A)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(bgPadding, 2, bgPadding, 2),
            Child = label
        };
        bgRect.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double finalX = centerText ? x - bgRect.DesiredSize.Width / 2 : x;
        double finalY = centerText ? y - bgRect.DesiredSize.Height / 2 : y;

        Canvas.SetLeft(bgRect, finalX);
        Canvas.SetTop(bgRect, finalY);
        ObjectivesCanvas.Children.Add(bgRect);
    }

    private void DrawObjectiveOptionalPoints(QuestObjective objective, double inverseScale, bool hasFloors)
    {
        // Optional points color - orange (#FF9800)
        var optionalColor = Color.FromRgb(0xFF, 0x98, 0x00);
        var questName = GetLocalizedQuestName(objective);
        bool showLabels = _showMarkerLabels && _zoomLevel >= _labelShowZoomThreshold;

        for (int i = 0; i < objective.OptionalPoints.Count; i++)
        {
            var point = objective.OptionalPoints[i];

            // Determine opacity based on floor
            double opacity = 1.0;
            if (hasFloors && _currentFloorId != null && point.FloorId != null)
            {
                if (!string.Equals(point.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase))
                {
                    opacity = 0.3;
                }
            }

            var color = Color.FromArgb((byte)(opacity * 255), optionalColor.R, optionalColor.G, optionalColor.B);
            var (sx, sy) = _currentMapConfig!.GameToScreen(point.X, point.Z);
            var markerSize = 28 * inverseScale;

            // Circle for optional point
            var circle = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 180), optionalColor.R, optionalColor.G, optionalColor.B)),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2 * inverseScale
            };
            Canvas.SetLeft(circle, sx - markerSize / 2);
            Canvas.SetTop(circle, sy - markerSize / 2);
            ObjectivesCanvas.Children.Add(circle);

            // "OR#" label
            var label = new TextBlock
            {
                Text = $"OR{i + 1}",
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0, 0, 0)),
                FontSize = 11 * inverseScale,
                FontWeight = FontWeights.Bold
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, sx - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, sy - label.DesiredSize.Height / 2);
            ObjectivesCanvas.Children.Add(label);

            // Quest name label (only for first optional point)
            if (showLabels && i == 0 && !string.IsNullOrEmpty(questName))
            {
                var fontSize = Math.Max(10, 24 * inverseScale);
                DrawQuestNameLabel(sx + markerSize / 2 + 8 * inverseScale, sy - fontSize / 2, questName, color, fontSize, opacity);
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

        RedrawMarkers();
        RedrawObjectives();
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
        var wasDragging = _isDragging;
        _isDragging = false;
        MapViewerGrid.ReleaseMouseCapture();
        MapCanvas.Cursor = Cursors.Arrow;

        // Only handle click selection if we weren't dragging
        if (!wasDragging || (_dragStartPoint != default &&
            Math.Abs(e.GetPosition(MapViewerGrid).X - _dragStartPoint.X) < 5 &&
            Math.Abs(e.GetPosition(MapViewerGrid).Y - _dragStartPoint.Y) < 5))
        {
            // Check if we clicked on a marker
            var canvasPos = e.GetPosition(MapCanvas);
            foreach (var region in _markerHitRegions)
            {
                if (region.Contains(canvasPos.X, canvasPos.Y))
                {
                    if (region.IsCluster && region.ClusterMarkers != null)
                    {
                        // For clusters, select the first marker for now
                        // TODO: Implement Spiderfy to expand clusters
                        ShowContextSelected(region.ClusterMarkers.First());
                    }
                    else
                    {
                        ShowContextSelected(region.Marker);
                    }

                    // Open context panel if closed
                    if (!_contextPanelOpen)
                    {
                        ToggleContextPanel();
                    }
                    return;
                }
            }

            // Clicked empty space - clear selection
            if (_selectedMarker != null)
            {
                ShowContextDefault();
                _selectedMarker = null;
            }
        }
    }

    private void MapViewer_MouseMove(object sender, MouseEventArgs e)
    {
        // Update coordinate display
        if (_currentMapConfig != null)
        {
            var canvasPos = e.GetPosition(MapCanvas);
            var (gameX, gameZ) = _currentMapConfig.ScreenToGame(canvasPos.X, canvasPos.Y);
            TxtCursorCoords.Text = $"X: {gameX:F1}, Z: {gameZ:F1}";

            // Hit test markers for tooltip (only when not dragging)
            if (!_isDragging)
            {
                UpdateMarkerTooltip(canvasPos, e.GetPosition(MapViewerGrid));
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

    private void UpdateMarkerTooltip(Point canvasPos, Point viewerPos)
    {
        // Find marker/cluster under cursor
        MarkerHitRegion? foundRegion = null;
        foreach (var region in _markerHitRegions)
        {
            if (region.Contains(canvasPos.X, canvasPos.Y))
            {
                foundRegion = region;
                break;
            }
        }

        if (foundRegion != null)
        {
            if (_hoveredMarker != foundRegion.Marker || _currentTooltipHitRegion != foundRegion)
            {
                _hoveredMarker = foundRegion.Marker;
                _currentTooltipHitRegion = foundRegion;
                ShowMarkerTooltip(foundRegion, viewerPos);
            }
            else
            {
                // Update tooltip position
                PositionTooltip(viewerPos);
            }
            MapCanvas.Cursor = Cursors.Hand;
        }
        else
        {
            if (_hoveredMarker != null)
            {
                _hoveredMarker = null;
                _currentTooltipHitRegion = null;
                MarkerTooltip.Visibility = Visibility.Collapsed;
            }
            MapCanvas.Cursor = Cursors.Arrow;
        }
    }

    private void ShowMarkerTooltip(MarkerHitRegion hitRegion, Point viewerPos)
    {
        var marker = hitRegion.Marker;
        var isCluster = hitRegion.IsCluster;

        // Set tooltip icon (use primary type for clusters)
        var displayType = isCluster ? hitRegion.ClusterMarkers!
            .GroupBy(m => m.Type)
            .OrderByDescending(g => GetMarkerTypePriority(g.Key))
            .First().Key : marker.Type;
        var icon = GetMarkerIcon(displayType);
        TooltipIcon.Source = icon;

        // Set tooltip title
        if (isCluster)
        {
            TooltipTitle.Text = $"{hitRegion.ClusterMarkers!.Count} Markers";
            TooltipType.Text = "Cluster";

            // Show cluster info
            TooltipClusterInfo.Visibility = Visibility.Visible;
            TooltipClusterCount.Text = $"{hitRegion.ClusterMarkers.Count} markers in this area";

            // Build type summary
            var typeCounts = hitRegion.ClusterMarkers
                .GroupBy(m => m.Type)
                .Select(g => $"{_loc.GetMarkerTypeName(g.Key)}: {g.Count()}")
                .Take(4);
            TooltipClusterTypes.Text = string.Join(", ", typeCounts);

            // Hide single-marker details
            TooltipDetails.Visibility = Visibility.Collapsed;

            // Calculate center coordinates for cluster
            var avgX = hitRegion.ClusterMarkers.Average(m => m.X);
            var avgZ = hitRegion.ClusterMarkers.Average(m => m.Z);
            TooltipCoords.Text = $"Center: X: {avgX:F1}, Z: {avgZ:F1}";
        }
        else
        {
            TooltipTitle.Text = GetLocalizedMarkerName(marker);
            TooltipType.Text = _loc.GetMarkerTypeName(marker.Type);

            // Hide cluster info
            TooltipClusterInfo.Visibility = Visibility.Collapsed;

            // Set floor info if available
            if (!string.IsNullOrEmpty(marker.FloorId) && _sortedFloors != null)
            {
                var floor = _sortedFloors.FirstOrDefault(f =>
                    string.Equals(f.LayerId, marker.FloorId, StringComparison.OrdinalIgnoreCase));
                TooltipDetails.Text = floor != null ? $"Floor: {floor.DisplayName}" : $"Floor: {marker.FloorId}";
                TooltipDetails.Visibility = Visibility.Visible;
            }
            else
            {
                TooltipDetails.Text = "";
                TooltipDetails.Visibility = Visibility.Collapsed;
            }

            // Set coordinates
            TooltipCoords.Text = $"X: {marker.X:F1}, Z: {marker.Z:F1}";
        }

        // Set border color based on marker type
        var (r, g, b) = MapMarker.GetMarkerColor(displayType);
        MarkerTooltip.BorderBrush = new SolidColorBrush(Color.FromRgb(r, g, b));

        // Show/hide "Go to Floor" button
        var hasFloorInfo = !string.IsNullOrEmpty(marker.FloorId) && _sortedFloors != null &&
                           !string.Equals(marker.FloorId, _currentFloorId, StringComparison.OrdinalIgnoreCase);
        BtnGoToFloor.Visibility = (!isCluster && hasFloorInfo) ? Visibility.Visible : Visibility.Collapsed;

        // Position and show tooltip
        PositionTooltip(viewerPos);
        MarkerTooltip.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Get marker type priority for cluster display (higher = more important)
    /// </summary>
    private static int GetMarkerTypePriority(MarkerType type) => type switch
    {
        MarkerType.BossSpawn => 100,
        MarkerType.RaiderSpawn => 90,
        MarkerType.PmcExtraction => 80,
        MarkerType.SharedExtraction => 75,
        MarkerType.ScavExtraction => 70,
        MarkerType.Transit => 60,
        MarkerType.Keys => 50,
        MarkerType.Lever => 40,
        MarkerType.PmcSpawn => 20,
        MarkerType.ScavSpawn => 10,
        _ => 0
    };

    private void PositionTooltip(Point viewerPos)
    {
        // Position tooltip near cursor but ensure it stays within bounds
        double tooltipX = viewerPos.X + 20;
        double tooltipY = viewerPos.Y + 20;

        // Get actual tooltip size (may not be measured yet)
        MarkerTooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var tooltipWidth = MarkerTooltip.DesiredSize.Width;
        var tooltipHeight = MarkerTooltip.DesiredSize.Height;

        // Ensure tooltip stays within viewer bounds
        if (tooltipX + tooltipWidth > MapViewerGrid.ActualWidth - 10)
        {
            tooltipX = viewerPos.X - tooltipWidth - 10;
        }
        if (tooltipY + tooltipHeight > MapViewerGrid.ActualHeight - 10)
        {
            tooltipY = viewerPos.Y - tooltipHeight - 10;
        }

        // Ensure not negative
        tooltipX = Math.Max(10, tooltipX);
        tooltipY = Math.Max(10, tooltipY);

        MarkerTooltip.Margin = new Thickness(tooltipX, tooltipY, 0, 0);
    }

    private void BtnCopyCoords_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTooltipHitRegion == null) return;

        string coordText;
        if (_currentTooltipHitRegion.IsCluster && _currentTooltipHitRegion.ClusterMarkers != null)
        {
            // Copy center coordinates for cluster
            var avgX = _currentTooltipHitRegion.ClusterMarkers.Average(m => m.X);
            var avgZ = _currentTooltipHitRegion.ClusterMarkers.Average(m => m.Z);
            coordText = $"X: {avgX:F1}, Z: {avgZ:F1}";
        }
        else
        {
            var marker = _currentTooltipHitRegion.Marker;
            coordText = $"X: {marker.X:F1}, Z: {marker.Z:F1}";
        }

        try
        {
            System.Windows.Clipboard.SetText(coordText);
            // Brief visual feedback - change button text temporarily
            BtnCopyCoords.Content = "Copied!";
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            timer.Tick += (_, _) =>
            {
                BtnCopyCoords.Content = "Copy";
                timer.Stop();
            };
            timer.Start();
        }
        catch
        {
            // Clipboard may fail in some scenarios
        }
    }

    private void BtnGoToFloor_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTooltipHitRegion == null || _currentTooltipHitRegion.IsCluster) return;

        var marker = _currentTooltipHitRegion.Marker;
        if (string.IsNullOrEmpty(marker.FloorId) || _sortedFloors == null) return;

        // Find the floor in the list
        var floor = _sortedFloors.FirstOrDefault(f =>
            string.Equals(f.LayerId, marker.FloorId, StringComparison.OrdinalIgnoreCase));

        if (floor != null)
        {
            // Select the floor in the floor selector (Settings Panel)
            var floorIndex = _sortedFloors.IndexOf(floor);
            if (floorIndex >= 0)
            {
                _currentFloorId = floor.LayerId;

                // Redraw to update floor visibility
                RedrawMarkers();
                RedrawObjectives();

                // Hide tooltip after action
                MarkerTooltip.Visibility = Visibility.Collapsed;
                _hoveredMarker = null;
                _currentTooltipHitRegion = null;
            }
        }
    }

    private void MapViewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var mousePos = e.GetPosition(MapViewerGrid);

        // Ctrl+scroll for 3x faster zoom
        var isCtrlHeld = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var baseZoomFactor = e.Delta > 0 ? 1.15 : 0.87;
        var zoomFactor = isCtrlHeld ? Math.Pow(baseZoomFactor, 3) : baseZoomFactor;

        var newZoom = Math.Clamp(_zoomLevel * zoomFactor, MinZoom, MaxZoom);

        ZoomToPoint(newZoom, mousePos);
        e.Handled = true;
    }

    #endregion

    #region Player Position Display

    private void OnPositionDetected(object? sender, PositionDetectedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdatePlayerPositionText(e.Position);
            DrawPlayerMarker(e.Position);

            // Auto-floor detection based on Y coordinate
            if (_autoFloorEnabled && _currentMapConfig != null)
            {
                AutoDetectAndSelectFloor(e.Position.Y);
            }

            // Auto-center on player if enabled
            if (_autoFollowEnabled)
            {
                CenterOnPlayer();
            }
        });
    }

    /// <summary>
    /// Auto-detect and select floor based on Y coordinate
    /// </summary>
    private void AutoDetectAndSelectFloor(double y)
    {
        if (_currentMapConfig == null || _sortedFloors == null || _sortedFloors.Count == 0)
            return;

        var detectedFloorId = _floorDetectionService.DetectFloor(_currentMapConfig.Key, y);
        if (detectedFloorId == null)
            return;

        // Only change floor if different from current
        if (string.Equals(_currentFloorId, detectedFloorId, StringComparison.OrdinalIgnoreCase))
            return;

        // Find floor index
        var floorIndex = _sortedFloors.FindIndex(f =>
            string.Equals(f.LayerId, detectedFloorId, StringComparison.OrdinalIgnoreCase));

        if (floorIndex >= 0 && floorIndex < FloorSelector.Items.Count)
        {
            FloorSelector.SelectedIndex = floorIndex;
            // Floor auto-detected
            System.Diagnostics.Debug.WriteLine($"Auto-detected floor: {_sortedFloors[floorIndex].DisplayName}");
        }
    }

    private void OnWatcherStateChanged(object? sender, WatcherStateChangedEventArgs e)
    {
        Dispatcher.Invoke(UpdateWatcherStatus);
    }

    private void UpdateWatcherStatus()
    {
        if (_watcherService.IsWatching)
        {
            WatcherIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x70, 0xA8, 0x00));
            TxtStartText.Text = "Stop";
            TxtStartIcon.Text = "■";
            BtnStartTracking.Background = new SolidColorBrush(Color.FromRgb(0xD4, 0x1C, 0x00));
        }
        else
        {
            WatcherIndicator.Fill = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            TxtStartText.Text = "Start";
            TxtStartIcon.Text = "▶";
            BtnStartTracking.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        }

        var position = _watcherService.CurrentPosition;
        if (position != null)
        {
            UpdatePlayerPositionText(position);
            DrawPlayerMarker(position);
        }
        else
        {
            PlayerPositionText.Text = "X: --  Z: --";
        }
    }

    private void UpdatePlayerPositionText(EftPosition position)
    {
        var angleStr = position.Angle.HasValue ? $" {position.Angle:F0}°" : "";
        PlayerPositionText.Text = $"X: {position.X:F0}  Z: {position.Z:F0}{angleStr}";
    }

    private void WatcherToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_watcherService.IsWatching)
        {
            _watcherService.StopWatching();
        }
        else
        {
            // Try auto-detect
            var path = _watcherService.DetectDefaultScreenshotFolder();

            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                _watcherService.StartWatching(path);
            }
            else
            {
                MessageBox.Show(
                    "Screenshot folder not found.\n\nPlease ensure EFT is installed and screenshots folder exists at:\n" +
                    "Documents\\Escape from Tarkov\\Screenshots",
                    "Folder Not Found",
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

        // Convert game coords to screen coords using player marker transform
        var (screenX, screenY) = _currentMapConfig.GameToScreenForPlayer(position.X, position.Z);

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
