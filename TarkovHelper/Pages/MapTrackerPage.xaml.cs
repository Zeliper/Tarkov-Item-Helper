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
/// Display model for quest objectives in the floating quest panel
/// </summary>
internal sealed class QuestObjectiveDisplay
{
    public string QuestName { get; set; } = "";
    public string ObjectiveDescription { get; set; } = "";
    public double X { get; set; }
    public double Z { get; set; }
    public string? FloorId { get; set; }
    public QuestObjective? SourceObjective { get; set; }
}

/// <summary>
/// Hit region for quest objective tooltip detection
/// </summary>
internal sealed class QuestHitRegion
{
    public QuestObjective Objective { get; init; } = null!;
    public List<QuestObjective>? ClusterObjectives { get; init; }  // Non-null for clustered objectives
    public double ScreenX { get; init; }
    public double ScreenY { get; init; }
    public double Radius { get; init; }
    public string LocalizedQuestName { get; set; } = "";
    public int Priority { get; set; }  // Higher = more important

    public bool IsCluster => ClusterObjectives != null && ClusterObjectives.Count > 1;

    public bool Contains(double x, double y)
    {
        var dx = x - ScreenX;
        var dy = y - ScreenY;
        return dx * dx + dy * dy <= Radius * Radius;
    }
}

/// <summary>
/// Cluster of quest objectives at similar locations
/// </summary>
internal sealed class QuestObjectiveCluster
{
    public List<(QuestObjective Objective, double ScreenX, double ScreenY, int Priority, string LocalizedName, QuestStatus Status, bool IsKappa)> Items { get; } = new();
    public double CenterScreenX { get; private set; }
    public double CenterScreenY { get; private set; }

    public void AddItem(QuestObjective objective, double sx, double sy, int priority, string localizedName, QuestStatus status, bool isKappa)
    {
        Items.Add((objective, sx, sy, priority, localizedName, status, isKappa));
        // Recalculate center
        CenterScreenX = Items.Average(i => i.ScreenX);
        CenterScreenY = Items.Average(i => i.ScreenY);
    }

    /// <summary>
    /// Gets the highest priority item in the cluster
    /// </summary>
    public (QuestObjective Objective, double ScreenX, double ScreenY, int Priority, string LocalizedName, QuestStatus Status, bool IsKappa) GetPrimaryItem()
    {
        return Items.OrderByDescending(i => i.Priority).First();
    }

    /// <summary>
    /// Check if any item in the cluster is Kappa-required
    /// </summary>
    public bool HasKappaItem() => Items.Any(i => i.IsKappa);
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

    // Quest objective hit regions for tooltip
    private readonly List<QuestHitRegion> _questHitRegions = new();
    private QuestHitRegion? _hoveredQuestRegion;
    private const double QuestClusterDistance = 40.0;  // Screen pixels to cluster quests

    // Settings panel state
    private bool _settingsPanelOpen = false;

    // Quest panel state
    private bool _questPanelVisible = true;

    // Layer menu popup state
    private bool _layerMenuOpen = false;

    // Initialization flag to prevent events during XAML loading
    private bool _isInitialized = false;

    // Display settings
    private double _markerScale = 1.0;
    private double _labelScale = 1.0;  // Label size scale (0.5 to 1.5)
    private double _labelShowZoomThreshold = 0.5;
    private bool _showMarkerLabels = true;

    // Quest display settings
    private bool _questStatusColorsEnabled = true;  // Color quests by status
    private bool _hideCompletedQuests = false;      // Hide done quests
    private bool _showActiveQuestsOnly = false;     // Show only active quests
    private bool _showKappaHighlight = true;        // Highlight Kappa-required quests
    private string _traderFilter = "";              // Trader filter (empty = all)

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

        // Mark initialization complete - events will now save settings
        _isInitialized = true;

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
        _labelScale = _settings.MapLabelScale;
        _showMarkerLabels = _settings.MapShowLabels;
        _clusteringEnabled = _settings.MapClusteringEnabled;
        _clusterZoomThreshold = _settings.MapClusterZoomThreshold / 100.0;  // Convert from 0-100 to 0-1
        _autoFloorEnabled = _settings.MapAutoFloorEnabled;

        // Quest display settings
        _questStatusColorsEnabled = _settings.MapQuestStatusColors;
        _hideCompletedQuests = _settings.MapHideCompletedQuests;
        _showActiveQuestsOnly = _settings.MapShowActiveOnly;
        _showKappaHighlight = _settings.MapShowKappaHighlight;
        _traderFilter = _settings.MapTraderFilter;

        // Layer visibility - apply to chips
        ChipBoss.IsChecked = _settings.MapShowBosses;
        ChipExtract.IsChecked = _settings.MapShowExtracts;
        ChipTransit.IsChecked = _settings.MapShowTransits;
        ChipSpawn.IsChecked = _settings.MapShowSpawns;
        ChipLever.IsChecked = _settings.MapShowLevers;
        ChipKeys.IsChecked = _settings.MapShowKeys;
        ChipQuest.IsChecked = _settings.MapShowQuests;

        // Quest panel state
        _questPanelVisible = _settings.QuestPanelVisible;
        UpdateQuestPanelState();

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
        _currentFloorId = null;
        _sortedFloors = null;

        var floors = config.Floors;
        if (floors == null || floors.Count == 0)
        {
            FloorSelector.Visibility = Visibility.Collapsed;
            return;
        }

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
                    RedrawMarkers();
                    RedrawObjectives();
                }
            }
        }
    }

    private void ChkAutoFloor_CheckedChanged(object sender, RoutedEventArgs e)
    {
        _autoFloorEnabled = ChkAutoFloor.IsChecked == true;
        if (_isInitialized) _settings.MapAutoFloorEnabled = _autoFloorEnabled;  // Save to settings
        // Floor will be auto-detected on next position update
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ignore if focus is on a text input
        if (e.OriginalSource is System.Windows.Controls.TextBox)
            return;

        switch (e.Key)
        {
            // Quest panel toggle (Q)
            case Key.Q:
                ToggleQuestPanel();
                e.Handled = true;
                return;

            // Layer menu toggle (L)
            case Key.L:
                ToggleLayerMenu();
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
                    CopySelectedMarkerCoords();
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
                if (ShortcutsPopup.Visibility == Visibility.Visible)
                {
                    ShortcutsPopup.Visibility = Visibility.Collapsed;
                }
                else if (_settingsPanelOpen)
                {
                    CloseSettingsPanel();
                }
                else if (_layerMenuOpen)
                {
                    CloseLayerMenu();
                }
                else if (!string.IsNullOrEmpty(TxtSearch.Text))
                {
                    TxtSearch.Text = "";
                }
                else if (_selectedMarker != null)
                {
                    _selectedMarker = null;
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
        SliderLabelSize.Value = _labelScale * 100;
        TxtLabelSize.Text = $"{_labelScale * 100:F0}%";
        ChkShowLabels.IsChecked = _showMarkerLabels;
        ChkEnableClustering.IsChecked = _clusteringEnabled;
        SliderClusterZoom.Value = _clusterZoomThreshold * 100;
        ChkAutoFloor.IsChecked = _autoFloorEnabled;

        // Sync Quest display settings
        ChkQuestStatusColors.IsChecked = _questStatusColorsEnabled;
        ChkHideCompletedQuests.IsChecked = _hideCompletedQuests;
        ChkShowActiveOnly.IsChecked = _showActiveQuestsOnly;
        ChkShowKappaHighlight.IsChecked = _showKappaHighlight;

        // Sync trader filter combobox
        SelectTraderFilterItem(_traderFilter);
    }

    /// <summary>
    /// Select the trader filter combobox item by tag value
    /// </summary>
    private void SelectTraderFilterItem(string traderTag)
    {
        foreach (ComboBoxItem item in CmbTraderFilter.Items)
        {
            if ((item.Tag as string ?? "") == traderTag)
            {
                CmbTraderFilter.SelectedItem = item;
                return;
            }
        }
        // Default to first item (All Traders)
        CmbTraderFilter.SelectedIndex = 0;
    }

    private void SliderMarkerSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMarkerSize == null) return;
        _markerScale = SliderMarkerSize.Value / 100.0;
        TxtMarkerSize.Text = $"{SliderMarkerSize.Value:F0}%";
        if (_isInitialized) _settings.MapMarkerScale = _markerScale;  // Save to settings
        RedrawMarkers();
    }

    private void SliderLabelSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtLabelSize == null) return;
        _labelScale = SliderLabelSize.Value / 100.0;
        TxtLabelSize.Text = $"{SliderLabelSize.Value:F0}%";
        if (_isInitialized) _settings.MapLabelScale = _labelScale;  // Save to settings
        RedrawMarkers();
        RedrawObjectives();
    }

    private void ChkShowLabels_Changed(object sender, RoutedEventArgs e)
    {
        _showMarkerLabels = ChkShowLabels.IsChecked == true;
        if (_isInitialized) _settings.MapShowLabels = _showMarkerLabels;  // Save to settings
        RedrawMarkers();
        RedrawObjectives();
    }

    private void ChkQuestStatusColors_Changed(object sender, RoutedEventArgs e)
    {
        _questStatusColorsEnabled = ChkQuestStatusColors.IsChecked == true;
        if (_isInitialized) _settings.MapQuestStatusColors = _questStatusColorsEnabled;
        RedrawObjectives();
    }

    private void ChkHideCompletedQuests_Changed(object sender, RoutedEventArgs e)
    {
        _hideCompletedQuests = ChkHideCompletedQuests.IsChecked == true;
        if (_isInitialized) _settings.MapHideCompletedQuests = _hideCompletedQuests;
        RedrawObjectives();
        UpdateCounts();
    }

    private void ChkShowActiveOnly_Changed(object sender, RoutedEventArgs e)
    {
        _showActiveQuestsOnly = ChkShowActiveOnly.IsChecked == true;
        if (_isInitialized) _settings.MapShowActiveOnly = _showActiveQuestsOnly;
        RedrawObjectives();
        UpdateCounts();
    }

    private void ChkShowKappaHighlight_Changed(object sender, RoutedEventArgs e)
    {
        _showKappaHighlight = ChkShowKappaHighlight.IsChecked == true;
        if (_isInitialized) _settings.MapShowKappaHighlight = _showKappaHighlight;
        RedrawObjectives();
    }

    private void CmbTraderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTraderFilter.SelectedItem is ComboBoxItem selectedItem)
        {
            _traderFilter = selectedItem.Tag as string ?? "";
            if (_isInitialized) _settings.MapTraderFilter = _traderFilter;
            RedrawObjectives();
            UpdateCounts();
        }
    }

    private void ChkEnableClustering_Changed(object sender, RoutedEventArgs e)
    {
        _clusteringEnabled = ChkEnableClustering.IsChecked == true;
        if (_isInitialized) _settings.MapClusteringEnabled = _clusteringEnabled;  // Save to settings
        RedrawMarkers();
    }

    private void SliderClusterZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtClusterZoom == null) return;
        _clusterZoomThreshold = SliderClusterZoom.Value / 100.0;
        TxtClusterZoom.Text = $"{SliderClusterZoom.Value:F0}%";
        if (_isInitialized) _settings.MapClusterZoomThreshold = SliderClusterZoom.Value;  // Save to settings (0-100)
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

    #region Quest Panel

    private void ToggleQuestPanel()
    {
        _questPanelVisible = !_questPanelVisible;
        UpdateQuestPanelState();
        if (_isInitialized) _settings.QuestPanelVisible = _questPanelVisible;
    }

    private void UpdateQuestPanelState()
    {
        QuestPanel.Visibility = _questPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        TxtQuestPanelToggle.Text = _questPanelVisible ? "◀" : "▶";
    }

    private void BtnToggleQuestPanel_Click(object sender, RoutedEventArgs e)
    {
        ToggleQuestPanel();
    }

    private void BtnCollapseQuests_Click(object sender, RoutedEventArgs e)
    {
        // Toggle between expanded and collapsed quest panel content
        bool isExpanded = QuestFilters.Visibility == Visibility.Visible;
        QuestFilters.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        QuestListScroll.Visibility = isExpanded ? Visibility.Collapsed : Visibility.Visible;
        TxtQuestCollapseIcon.Text = isExpanded ? "▶" : "▼";
    }

    private void QuestFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        RefreshQuestList();
    }

    private void QuestItem_Click(object sender, MouseButtonEventArgs e)
    {
        // Focus on quest marker on map when clicked
        if (sender is FrameworkElement element && element.Tag is QuestObjectiveDisplay quest)
        {
            FocusOnQuestObjective(quest);
        }
    }

    private void BtnFocusQuest_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is QuestObjectiveDisplay quest)
        {
            FocusOnQuestObjective(quest);
        }
    }

    private void FocusOnQuestObjective(QuestObjectiveDisplay quest)
    {
        // Pan to quest objective location
        if (_currentMapConfig != null && (quest.X != 0 || quest.Z != 0))
        {
            var (sx, sy) = _currentMapConfig.GameToScreen(quest.X, quest.Z);
            var viewCenter = new Point(MapViewerGrid.ActualWidth / 2, MapViewerGrid.ActualHeight / 2);
            MapTranslate.X = viewCenter.X - sx * _zoomLevel;
            MapTranslate.Y = viewCenter.Y - sy * _zoomLevel;
        }
    }

    private void RefreshQuestList()
    {
        // This will be implemented to filter and display quest objectives
        // For now, keep the list as is
    }

    #endregion

    #region Layer Menu

    private void ToggleLayerMenu()
    {
        _layerMenuOpen = !_layerMenuOpen;
        LayerMenuPopup.Visibility = _layerMenuOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseLayerMenu()
    {
        _layerMenuOpen = false;
        LayerMenuPopup.Visibility = Visibility.Collapsed;
    }

    private void BtnMenu_Click(object sender, RoutedEventArgs e)
    {
        ToggleLayerMenu();
    }

    private void BtnToggleMinimap_Click(object sender, RoutedEventArgs e)
    {
        MinimapPanel.Visibility = MinimapPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    #endregion

    private MapMarker? _selectedMarker;

    private void CopySelectedMarkerCoords()
    {
        if (_selectedMarker != null)
        {
            Clipboard.SetText($"{_selectedMarker.X:F1}, {_selectedMarker.Z:F1}");
        }
    }

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

    private static SolidColorBrush GetMarkerBrush(MarkerType type)
    {
        var (r, g, b) = MapMarker.GetMarkerColor(type);
        return new SolidColorBrush(Color.FromRgb(r, g, b));
    }

    // Removed: Context panel methods - UI redesigned to use floating panels
    private void ShowContextSearch() { /* Context panel removed */ }
    private void ShowContextDefault() { /* Context panel removed */ }
    private void ShowContextSelected(MapMarker marker) { _selectedMarker = marker; }

    private void BtnBackToDefault_Click(object sender, RoutedEventArgs e)
    {
        _selectedMarker = null;
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

        // Search results are handled by tooltip hover - no dedicated list panel
        // Pan to first result if found
        if (_searchResults.Count > 0 && _currentMapConfig != null)
        {
            var first = _searchResults[0];
            var (sx, sy) = _currentMapConfig.GameToScreen(first.Marker.X, first.Marker.Z);
            MapTranslate.X = MapViewerGrid.ActualWidth / 2 - sx * _zoomLevel;
            MapTranslate.Y = MapViewerGrid.ActualHeight / 2 - sy * _zoomLevel;
        }
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

            // Map title shown in ComboBox dropdown
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
        // Guard against XAML initialization
        if (!_isInitialized) return;

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
        if (!_isInitialized) return;

        _settings.MapShowBosses = ChipBoss.IsChecked == true;
        _settings.MapShowExtracts = ChipExtract.IsChecked == true;
        _settings.MapShowTransits = ChipTransit.IsChecked == true;
        _settings.MapShowSpawns = ChipSpawn.IsChecked == true;
        _settings.MapShowLevers = ChipLever.IsChecked == true;
        _settings.MapShowKeys = ChipKeys.IsChecked == true;
        _settings.MapShowQuests = ChipQuest.IsChecked == true;
    }

    // Removed: SyncLayerCheckboxes() and SyncLayerChips() - old context panel checkboxes removed
    // Layer toggle chips in LayerMenuPopup are the only layer controls now

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
        // Guard against XAML initialization - UI elements may not exist yet
        if (!_isInitialized) return;

        if (_currentMapConfig == null)
        {
            TotalMarkerCountText.Text = "/0";
            TxtQuestCount.Text = "(0)";
            MarkerCountText.Text = "0";
            return;
        }

        var markers = MapMarkerDbService.Instance.GetMarkersForMap(_currentMapConfig.Key);
        var totalCount = markers.Count;
        var visibleCount = markers.Count(m => ShouldShowMarkerType(m.Type));

        // Update status bar
        MarkerCountText.Text = visibleCount.ToString();
        TotalMarkerCountText.Text = $"/{totalCount}";

        var objectives = QuestObjectiveDbService.Instance.GetObjectivesForMap(_currentMapConfig.Key, _currentMapConfig);
        TxtQuestCount.Text = $"({objectives.Count})";
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
        if (MarkersCanvas == null) return;

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
        _questHitRegions.Clear();

        if (_currentMapConfig == null) return;
        if (!ShouldShowQuestObjectives()) return;

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var objectives = QuestObjectiveDbService.Instance.GetObjectivesForMap(_currentMapConfig.Key, _currentMapConfig);

        // Collect single-point objectives for clustering
        var singlePointObjectives = new List<(QuestObjective Obj, double SX, double SY, int Priority, string Name, double Opacity, QuestStatus Status, bool IsKappa)>();

        int count = 0;
        foreach (var objective in objectives)
        {
            // Get quest status for filtering and coloring
            var questStatus = GetQuestStatusForObjective(objective);

            // Apply filtering based on quest status
            if (_hideCompletedQuests && questStatus == QuestStatus.Done)
                continue;

            if (_showActiveQuestsOnly && questStatus != QuestStatus.Active)
                continue;

            // Apply trader filter
            if (!string.IsNullOrEmpty(_traderFilter) && !MatchesTraderFilter(objective))
                continue;

            count++;

            var questName = GetLocalizedQuestName(objective);
            var priority = CalculateQuestPriority(objective);
            var isKappa = IsKappaRequired(objective);

            // Draw LocationPoints (polygon, line, or single point)
            if (objective.HasCoordinates)
            {
                var points = objective.LocationPoints;
                double opacity = GetObjectiveOpacity(points[0].FloorId, hasFloors);

                if (points.Count == 1)
                {
                    // Collect single points for smart label clustering
                    var (sx, sy) = _currentMapConfig!.GameToScreen(points[0].X, points[0].Z);
                    singlePointObjectives.Add((objective, sx, sy, priority, questName, opacity, questStatus, isKappa));
                }
                else if (points.Count == 2)
                {
                    // Two points - draw dashed line (no clustering)
                    var objectiveColor = GetQuestStatusColor(questStatus, opacity);
                    bool showLabels = _showMarkerLabels && _zoomLevel >= _labelShowZoomThreshold;
                    DrawObjectiveLine(points[0], points[1], inverseScale, objectiveColor, questName, showLabels);
                }
                else
                {
                    // 3+ points - draw polygon (no clustering)
                    var objectiveColor = GetQuestStatusColor(questStatus, opacity);
                    bool showLabels = _showMarkerLabels && _zoomLevel >= _labelShowZoomThreshold;
                    DrawObjectivePolygon(points, inverseScale, objectiveColor, questName, showLabels);
                }
            }

            // Draw OptionalPoints (OR locations)
            if (objective.HasOptionalPoints)
            {
                DrawObjectiveOptionalPoints(objective, inverseScale, hasFloors, questStatus);
            }
        }

        // Apply smart label clustering for single-point objectives
        DrawClusteredObjectives(singlePointObjectives, inverseScale);

        TxtQuestCount.Text = count.ToString();
    }

    /// <summary>
    /// Get quest status for an objective
    /// </summary>
    private QuestStatus GetQuestStatusForObjective(QuestObjective objective)
    {
        var quest = QuestDbService.Instance.GetQuestById(objective.QuestId);
        if (quest == null) return QuestStatus.Locked;

        return QuestProgressService.Instance.GetStatus(quest);
    }

    /// <summary>
    /// Check if objective's quest matches the trader filter
    /// </summary>
    private bool MatchesTraderFilter(QuestObjective objective)
    {
        if (string.IsNullOrEmpty(_traderFilter)) return true;

        var quest = QuestDbService.Instance.GetQuestById(objective.QuestId);
        if (quest == null) return false;

        return string.Equals(quest.Trader, _traderFilter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Check if a quest objective is for a Kappa-required quest
    /// </summary>
    private bool IsKappaRequired(QuestObjective objective)
    {
        var quest = QuestDbService.Instance.GetQuestById(objective.QuestId);
        return quest?.ReqKappa ?? false;
    }

    /// <summary>
    /// Get color based on quest status
    /// Active = Green, Locked = Amber, Done = Grey
    /// </summary>
    private Color GetQuestStatusColor(QuestStatus status, double opacity)
    {
        if (!_questStatusColorsEnabled)
        {
            // Default amber color when status colors disabled
            return Color.FromArgb((byte)(opacity * 255), 0xF5, 0xA6, 0x23);
        }

        return status switch
        {
            QuestStatus.Active => Color.FromArgb((byte)(opacity * 255), 0x4C, 0xAF, 0x50),  // Green
            QuestStatus.Done => Color.FromArgb((byte)(opacity * 255), 0x9E, 0x9E, 0x9E),    // Grey
            _ => Color.FromArgb((byte)(opacity * 255), 0xFF, 0xA0, 0x00)                     // Amber (Locked)
        };
    }

    /// <summary>
    /// Get opacity based on floor matching
    /// </summary>
    private double GetObjectiveOpacity(string? pointFloor, bool hasFloors)
    {
        if (hasFloors && _currentFloorId != null && pointFloor != null)
        {
            if (!string.Equals(pointFloor, _currentFloorId, StringComparison.OrdinalIgnoreCase))
            {
                return 0.3;
            }
        }
        return 1.0;
    }

    /// <summary>
    /// Calculate priority for a quest objective (higher = more important)
    /// Priority: Active quests > Kappa required > Lower level requirement
    /// </summary>
    private int CalculateQuestPriority(QuestObjective objective)
    {
        int priority = 100;  // Base priority

        // Check quest properties from QuestDbService
        var quest = QuestDbService.Instance.GetQuestById(objective.QuestId);
        if (quest != null)
        {
            // Check if quest is active (highest priority)
            var questProgress = QuestProgressService.Instance;
            var status = questProgress.GetStatus(quest);
            if (status == QuestStatus.Active)
            {
                priority += 1000;  // Active quests get huge bonus
            }

            // Kappa required quests get bonus
            if (quest.ReqKappa)
            {
                priority += 200;
            }

            // Lower level requirements get higher priority (inverted)
            if (quest.RequiredLevel.HasValue)
            {
                priority += Math.Max(0, 100 - quest.RequiredLevel.Value);
            }
        }

        return priority;
    }

    /// <summary>
    /// Draw clustered quest objectives with smart label system
    /// </summary>
    private void DrawClusteredObjectives(List<(QuestObjective Obj, double SX, double SY, int Priority, string Name, double Opacity, QuestStatus Status, bool IsKappa)> objectives, double inverseScale)
    {
        if (objectives.Count == 0) return;

        // Build clusters based on screen distance
        var clusters = new List<QuestObjectiveCluster>();
        var clusterDistance = QuestClusterDistance * inverseScale;  // Scale with zoom

        foreach (var obj in objectives)
        {
            // Find existing cluster within distance
            QuestObjectiveCluster? targetCluster = null;
            foreach (var cluster in clusters)
            {
                var dx = obj.SX - cluster.CenterScreenX;
                var dy = obj.SY - cluster.CenterScreenY;
                if (Math.Sqrt(dx * dx + dy * dy) < clusterDistance)
                {
                    targetCluster = cluster;
                    break;
                }
            }

            if (targetCluster != null)
            {
                targetCluster.AddItem(obj.Obj, obj.SX, obj.SY, obj.Priority, obj.Name, obj.Status, obj.IsKappa);
            }
            else
            {
                var newCluster = new QuestObjectiveCluster();
                newCluster.AddItem(obj.Obj, obj.SX, obj.SY, obj.Priority, obj.Name, obj.Status, obj.IsKappa);
                clusters.Add(newCluster);
            }
        }

        // Draw each cluster
        bool showLabels = _showMarkerLabels && _zoomLevel >= _labelShowZoomThreshold;
        var markerSize = 24 * inverseScale;

        foreach (var cluster in clusters)
        {
            var primary = cluster.GetPrimaryItem();
            var sx = cluster.CenterScreenX;
            var sy = cluster.CenterScreenY;

            // Get opacity and status from first item (approximate)
            var firstItem = objectives.First(o => o.Obj == primary.Objective);
            var opacity = firstItem.Opacity;
            var objectiveColor = GetQuestStatusColor(primary.Status, opacity);

            // Draw circular marker
            var circle = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 220), objectiveColor.R, objectiveColor.G, objectiveColor.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 2 * inverseScale
            };
            Canvas.SetLeft(circle, sx - markerSize / 2);
            Canvas.SetTop(circle, sy - markerSize / 2);
            ObjectivesCanvas.Children.Add(circle);

            // Draw Kappa star badge if enabled and cluster has Kappa quest
            if (_showKappaHighlight && cluster.HasKappaItem())
            {
                var starSize = 12 * inverseScale;
                var star = new TextBlock
                {
                    Text = "⭐",
                    FontSize = starSize,
                    Foreground = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0xFF, 0xD7, 0x00)),  // Gold
                };
                Canvas.SetLeft(star, sx - markerSize / 2 - starSize / 2);
                Canvas.SetTop(star, sy - markerSize / 2 - starSize / 2);
                ObjectivesCanvas.Children.Add(star);
            }

            // If cluster has multiple items, show count badge
            if (cluster.Items.Count > 1)
            {
                var badgeSize = 14 * inverseScale;
                var badge = new Border
                {
                    Width = badgeSize,
                    Height = badgeSize,
                    CornerRadius = new CornerRadius(badgeSize / 2),
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x70, 0x43)),  // Cluster color
                    Child = new TextBlock
                    {
                        Text = cluster.Items.Count.ToString(),
                        FontSize = 9 * inverseScale,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Canvas.SetLeft(badge, sx + markerSize / 2 - badgeSize / 2);
                Canvas.SetTop(badge, sy - markerSize / 2 - badgeSize / 2);
                ObjectivesCanvas.Children.Add(badge);
            }

            // Only show label for the highest priority quest in cluster
            if (showLabels)
            {
                var fontSize = Math.Max(10, 24 * inverseScale * _labelScale);
                DrawQuestNameLabel(sx + markerSize / 2 + 8 * inverseScale, sy - fontSize / 2, primary.LocalizedName, objectiveColor, fontSize, opacity);
            }

            // Register hit region for tooltip
            _questHitRegions.Add(new QuestHitRegion
            {
                Objective = primary.Objective,
                ClusterObjectives = cluster.Items.Count > 1 ? cluster.Items.Select(i => i.Objective).ToList() : null,
                ScreenX = sx,
                ScreenY = sy,
                Radius = markerSize / 2 + 5,
                LocalizedQuestName = primary.LocalizedName,
                Priority = primary.Priority
            });
        }
    }

    // Note: DrawObjectiveLocationPoints and DrawObjectivePointMarker removed -
    // replaced by DrawClusteredObjectives with smart label system

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

    private void DrawObjectiveOptionalPoints(QuestObjective objective, double inverseScale, bool hasFloors, QuestStatus questStatus)
    {
        // Get base color from quest status (lighter version for optional points)
        var baseColor = GetQuestStatusColor(questStatus, 1.0);
        // Make it lighter/more pastel for optional points
        var optionalColor = Color.FromRgb(
            (byte)Math.Min(255, baseColor.R + 50),
            (byte)Math.Min(255, baseColor.G + 50),
            (byte)Math.Min(255, baseColor.B + 50));

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
            var markerSize = 20 * inverseScale;  // Smaller than required objectives

            // Clean circular marker with white border (no "OR#" label)
            var circle = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 200), optionalColor.R, optionalColor.G, optionalColor.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0xFF, 0xFF, 0xFF)),  // White border
                StrokeThickness = 2 * inverseScale
            };
            Canvas.SetLeft(circle, sx - markerSize / 2);
            Canvas.SetTop(circle, sy - markerSize / 2);
            ObjectivesCanvas.Children.Add(circle);

            // Quest name label (only for first optional point)
            if (showLabels && i == 0 && !string.IsNullOrEmpty(questName))
            {
                var fontSize = Math.Max(10, 24 * inverseScale * _labelScale);
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

                    // Marker selection handled via ShowContextSelected
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
        // Hit test markers for tooltip (only when not dragging)
        if (_currentMapConfig != null && !_isDragging)
        {
            var canvasPos = e.GetPosition(MapCanvas);
            UpdateMarkerTooltip(canvasPos, e.GetPosition(MapViewerGrid));
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

        // Find quest objective under cursor
        QuestHitRegion? foundQuestRegion = null;
        if (foundRegion == null)  // Only check quests if no marker found
        {
            foreach (var region in _questHitRegions)
            {
                if (region.Contains(canvasPos.X, canvasPos.Y))
                {
                    foundQuestRegion = region;
                    break;
                }
            }
        }

        if (foundRegion != null)
        {
            // Hide quest tooltip if showing
            _hoveredQuestRegion = null;

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
        else if (foundQuestRegion != null)
        {
            // Hide marker tooltip if showing
            _hoveredMarker = null;
            _currentTooltipHitRegion = null;

            if (_hoveredQuestRegion != foundQuestRegion)
            {
                _hoveredQuestRegion = foundQuestRegion;
                ShowQuestTooltip(foundQuestRegion, viewerPos);
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
            if (_hoveredMarker != null || _hoveredQuestRegion != null)
            {
                _hoveredMarker = null;
                _hoveredQuestRegion = null;
                _currentTooltipHitRegion = null;
                MarkerTooltip.Visibility = Visibility.Collapsed;
            }
            MapCanvas.Cursor = Cursors.Arrow;
        }
    }

    /// <summary>
    /// Show tooltip for quest objective cluster
    /// </summary>
    private void ShowQuestTooltip(QuestHitRegion hitRegion, Point viewerPos)
    {
        var isCluster = hitRegion.IsCluster;

        // Set tooltip icon (quest marker icon)
        TooltipIcon.Text = "●";
        TooltipIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));  // Quest amber color

        if (isCluster && hitRegion.ClusterObjectives != null)
        {
            TooltipTitle.Text = $"{hitRegion.ClusterObjectives.Count} Quests";
            TooltipType.Text = "Quest Cluster";

            // Show cluster info with quest names
            TooltipClusterInfo.Visibility = Visibility.Visible;
            TooltipClusterCount.Text = $"{hitRegion.ClusterObjectives.Count} quests at this location";

            // Populate cluster list with quest names
            var questNames = hitRegion.ClusterObjectives
                .Select(o => GetLocalizedQuestName(o))
                .Take(6)
                .ToList();
            TooltipClusterList.ItemsSource = questNames;

            TooltipClusterTypes.Text = "";  // No type summary for quests

            // Hide single-marker details
            TooltipDetails.Visibility = Visibility.Collapsed;

            // Use primary objective coordinates
            var primaryObj = hitRegion.Objective;
            if (primaryObj.HasCoordinates)
            {
                var pt = primaryObj.LocationPoints[0];
                TooltipCoords.Text = $"X: {pt.X:F1}, Z: {pt.Z:F1}";
            }
        }
        else
        {
            TooltipTitle.Text = hitRegion.LocalizedQuestName;
            TooltipType.Text = "Quest Objective";

            // Hide cluster info
            TooltipClusterInfo.Visibility = Visibility.Collapsed;
            TooltipClusterList.ItemsSource = null;

            // Show quest details
            var obj = hitRegion.Objective;
            if (!string.IsNullOrEmpty(obj.Description))
            {
                TooltipDetails.Text = obj.Description;
                TooltipDetails.Visibility = Visibility.Visible;
            }
            else
            {
                TooltipDetails.Visibility = Visibility.Collapsed;
            }

            // Set coordinates
            if (obj.HasCoordinates)
            {
                var pt = obj.LocationPoints[0];
                TooltipCoords.Text = $"X: {pt.X:F1}, Z: {pt.Z:F1}";
            }
        }

        // Set border color to quest amber
        MarkerTooltip.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));

        // Position and show tooltip
        PositionTooltip(viewerPos);
        MarkerTooltip.Visibility = Visibility.Visible;
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
        TooltipIcon.Text = GetMarkerIconText(displayType);
        TooltipIcon.Foreground = GetMarkerBrush(displayType);

        // Set tooltip title
        if (isCluster)
        {
            TooltipTitle.Text = $"{hitRegion.ClusterMarkers!.Count} Markers";
            TooltipType.Text = "Cluster";

            // Show cluster info
            TooltipClusterInfo.Visibility = Visibility.Visible;
            TooltipClusterCount.Text = $"{hitRegion.ClusterMarkers.Count} markers in this area";

            // Populate cluster list with marker names (max 6 items)
            var markerNames = hitRegion.ClusterMarkers
                .OrderByDescending(m => GetMarkerTypePriority(m.Type))
                .Take(6)
                .Select(m => GetLocalizedMarkerName(m))
                .ToList();
            TooltipClusterList.ItemsSource = markerNames;

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
