using System.Text.Json;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services.Settings;

/// <summary>
/// Map-related settings service.
/// Manages all map display, marker, and tracking settings.
/// </summary>
public class MapSettings
{
    private static readonly ILogger _log = Log.For<MapSettings>();
    private static MapSettings? _instance;
    public static MapSettings Instance => _instance ??= new MapSettings();

    private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;
    private bool _settingsLoaded;

    #region Setting Keys

    private const string KeyMapDrawerOpen = "map.drawerOpen";
    private const string KeyMapDrawerWidth = "map.drawerWidth";
    private const string KeyMapShowExtracts = "map.showExtracts";
    private const string KeyMapShowPmcExtracts = "map.showPmcExtracts";
    private const string KeyMapShowScavExtracts = "map.showScavExtracts";
    private const string KeyMapShowTransits = "map.showTransits";
    private const string KeyMapShowQuests = "map.showQuests";
    private const string KeyMapIncompleteOnly = "map.incompleteOnly";
    private const string KeyMapCurrentMapOnly = "map.currentMapOnly";
    private const string KeyMapSortOption = "map.sortOption";
    private const string KeyMapHiddenQuests = "map.hiddenQuests";
    private const string KeyMapCollapsedQuests = "map.collapsedQuests";
    private const string KeyMapLastSelectedMap = "map.lastSelectedMap";
    private const string KeyMapMarkerScale = "map.markerScale";
    private const string KeyMapShowTrail = "map.showTrail";
    private const string KeyMapShowMinimap = "map.showMinimap";
    private const string KeyMapMinimapSize = "map.minimapSize";
    private const string KeyMapMarkerOpacity = "map.markerOpacity";
    private const string KeyMapAutoHideCompleted = "map.autoHideCompleted";
    private const string KeyMapFadeCompleted = "map.fadeCompleted";
    private const string KeyMapShowLabels = "map.showLabels";
    private const string KeyMapLabelScale = "map.labelScale";
    private const string KeyMapQuestStatusColors = "map.questStatusColors";
    private const string KeyMapHideCompletedQuests = "map.hideCompletedQuests";
    private const string KeyMapShowActiveOnly = "map.showActiveOnly";
    private const string KeyMapHideCompletedObjectives = "map.hideCompletedObjectives";
    private const string KeyMapQuestMarkerStyle = "map.questMarkerStyle";
    private const string KeyMapShowKappaHighlight = "map.showKappaHighlight";
    private const string KeyMapTraderFilter = "map.traderFilter";
    private const string KeyMapTrailColor = "map.trailColor";
    private const string KeyMapTrailThickness = "map.trailThickness";
    private const string KeyMapAutoStartTracking = "map.autoStartTracking";
    private const string KeyMapClusteringEnabled = "map.clusteringEnabled";
    private const string KeyMapClusterZoomThreshold = "map.clusterZoomThreshold";
    private const string KeyMapClusterTextOnly = "map.clusterTextOnly";
    private const string KeyMapAutoFloorEnabled = "map.autoFloorEnabled";
    private const string KeyMapShowBosses = "map.showBosses";
    private const string KeyMapShowSpawns = "map.showSpawns";
    private const string KeyMapShowLevers = "map.showLevers";
    private const string KeyMapShowKeys = "map.showKeys";
    private const string KeyMapLeftPanelExpanded = "map.leftPanelExpanded";
    private const string KeyExpanderLayersExpanded = "map.expanderLayers";
    private const string KeyExpanderFloorExpanded = "map.expanderFloor";
    private const string KeyExpanderMapInfoExpanded = "map.expanderMapInfo";
    private const string KeyQuestPanelVisible = "map.questPanelVisible";
    private const string KeyMapScreenshotPath = "map.screenshotPath";
    private const string KeyMapQuestMarkerSize = "map.questMarkerSize";
    private const string KeyMapPlayerMarkerSize = "map.playerMarkerSize";
    private const string KeyMapExtractNameSize = "map.extractNameSize";
    private const string KeyMapQuestNameSize = "map.questNameSize";
    private const string KeyMapLastZoomLevel = "map.lastZoomLevel";
    private const string KeyMapLastTranslateX = "map.lastTranslateX";
    private const string KeyMapLastTranslateY = "map.lastTranslateY";

    #endregion

    #region Constants

    public const double MinMarkerScale = 0.5;
    public const double MaxMarkerScale = 2.0;
    public const double DefaultMarkerScale = 1.0;
    public const double DefaultDrawerWidth = 320;

    #endregion

    #region Cached Values

    private bool? _drawerOpen;
    private double? _drawerWidth;
    private bool? _showExtracts;
    private bool? _showPmcExtracts;
    private bool? _showScavExtracts;
    private bool? _showTransits;
    private bool? _showQuests;
    private bool? _incompleteOnly;
    private bool? _currentMapOnly;
    private string? _sortOption;
    private HashSet<string>? _hiddenQuests;
    private HashSet<string>? _collapsedQuests;
    private string? _lastSelectedMap;
    private double? _markerScale;
    private bool? _showTrail;
    private bool? _showMinimap;
    private string? _minimapSize;
    private double? _markerOpacity;
    private bool? _autoHideCompleted;
    private bool? _fadeCompleted;
    private bool? _showLabels;
    private double? _labelScale;
    private bool? _questStatusColors;
    private bool? _hideCompletedQuests;
    private bool? _showActiveOnly;
    private bool? _hideCompletedObjectives;
    private int? _questMarkerStyle;
    private bool? _showKappaHighlight;
    private string? _traderFilter;
    private string? _trailColor;
    private double? _trailThickness;
    private bool? _autoStartTracking;
    private bool? _clusteringEnabled;
    private double? _clusterZoomThreshold;
    private bool? _autoFloorEnabled;
    private bool? _showBosses;
    private bool? _showSpawns;
    private bool? _showLevers;
    private bool? _showKeys;
    private bool? _leftPanelExpanded;
    private bool? _expanderLayersExpanded;
    private bool? _expanderFloorExpanded;
    private bool? _expanderMapInfoExpanded;
    private bool? _questPanelVisible;
    private string? _screenshotPath;
    private int? _questMarkerSize;
    private int? _playerMarkerSize;
    private double? _extractNameSize;
    private double? _questNameSize;
    private double? _lastZoomLevel;
    private double? _lastTranslateX;
    private double? _lastTranslateY;

    #endregion

    private MapSettings()
    {
        LoadSettings();
    }

    #region Properties - Drawer

    public bool DrawerOpen
    {
        get
        {
            EnsureLoaded();
            return _drawerOpen ?? true;
        }
        set
        {
            if (_drawerOpen != value)
            {
                _drawerOpen = value;
                SaveSetting(KeyMapDrawerOpen, value.ToString());
            }
        }
    }

    public double DrawerWidth
    {
        get
        {
            EnsureLoaded();
            return _drawerWidth ?? DefaultDrawerWidth;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 250, 500);
            if (Math.Abs((_drawerWidth ?? DefaultDrawerWidth) - clampedValue) > 1)
            {
                _drawerWidth = clampedValue;
                SaveSetting(KeyMapDrawerWidth, clampedValue.ToString());
            }
        }
    }

    #endregion

    #region Properties - Extracts

    public bool ShowExtracts
    {
        get
        {
            EnsureLoaded();
            return _showExtracts ?? true;
        }
        set
        {
            if (_showExtracts != value)
            {
                _showExtracts = value;
                SaveSetting(KeyMapShowExtracts, value.ToString());
            }
        }
    }

    public bool ShowPmcExtracts
    {
        get
        {
            EnsureLoaded();
            return _showPmcExtracts ?? true;
        }
        set
        {
            if (_showPmcExtracts != value)
            {
                _showPmcExtracts = value;
                SaveSetting(KeyMapShowPmcExtracts, value.ToString());
            }
        }
    }

    public bool ShowScavExtracts
    {
        get
        {
            EnsureLoaded();
            return _showScavExtracts ?? true;
        }
        set
        {
            if (_showScavExtracts != value)
            {
                _showScavExtracts = value;
                SaveSetting(KeyMapShowScavExtracts, value.ToString());
            }
        }
    }

    public bool ShowTransits
    {
        get
        {
            EnsureLoaded();
            return _showTransits ?? true;
        }
        set
        {
            if (_showTransits != value)
            {
                _showTransits = value;
                SaveSetting(KeyMapShowTransits, value.ToString());
            }
        }
    }

    #endregion

    #region Properties - Quests

    public bool ShowQuests
    {
        get
        {
            EnsureLoaded();
            return _showQuests ?? true;
        }
        set
        {
            if (_showQuests != value)
            {
                _showQuests = value;
                SaveSetting(KeyMapShowQuests, value.ToString());
            }
        }
    }

    public bool IncompleteOnly
    {
        get
        {
            EnsureLoaded();
            return _incompleteOnly ?? false;
        }
        set
        {
            if (_incompleteOnly != value)
            {
                _incompleteOnly = value;
                SaveSetting(KeyMapIncompleteOnly, value.ToString());
            }
        }
    }

    public bool CurrentMapOnly
    {
        get
        {
            EnsureLoaded();
            return _currentMapOnly ?? true;
        }
        set
        {
            if (_currentMapOnly != value)
            {
                _currentMapOnly = value;
                SaveSetting(KeyMapCurrentMapOnly, value.ToString());
            }
        }
    }

    public string SortOption
    {
        get
        {
            EnsureLoaded();
            return _sortOption ?? "name";
        }
        set
        {
            if (_sortOption != value)
            {
                _sortOption = value;
                SaveSetting(KeyMapSortOption, value ?? "name");
            }
        }
    }

    public HashSet<string> HiddenQuests
    {
        get
        {
            EnsureLoaded();
            return _hiddenQuests ?? new HashSet<string>();
        }
        set
        {
            _hiddenQuests = value;
            var json = JsonSerializer.Serialize(value?.ToArray() ?? Array.Empty<string>());
            SaveSetting(KeyMapHiddenQuests, json);
        }
    }

    public HashSet<string> CollapsedQuests
    {
        get
        {
            EnsureLoaded();
            return _collapsedQuests ?? new HashSet<string>();
        }
        set
        {
            _collapsedQuests = value;
            var json = JsonSerializer.Serialize(value?.ToArray() ?? Array.Empty<string>());
            SaveSetting(KeyMapCollapsedQuests, json);
        }
    }

    public string? LastSelectedMap
    {
        get
        {
            EnsureLoaded();
            return _lastSelectedMap;
        }
        set
        {
            if (_lastSelectedMap != value)
            {
                _lastSelectedMap = value;
                SaveSetting(KeyMapLastSelectedMap, value ?? "");
            }
        }
    }

    public bool QuestStatusColors
    {
        get
        {
            EnsureLoaded();
            return _questStatusColors ?? true;
        }
        set
        {
            if (_questStatusColors != value)
            {
                _questStatusColors = value;
                SaveSetting(KeyMapQuestStatusColors, value.ToString());
            }
        }
    }

    public bool HideCompletedQuests
    {
        get
        {
            EnsureLoaded();
            return _hideCompletedQuests ?? false;
        }
        set
        {
            if (_hideCompletedQuests != value)
            {
                _hideCompletedQuests = value;
                SaveSetting(KeyMapHideCompletedQuests, value.ToString());
            }
        }
    }

    public bool ShowActiveOnly
    {
        get
        {
            EnsureLoaded();
            return _showActiveOnly ?? true;
        }
        set
        {
            if (_showActiveOnly != value)
            {
                _showActiveOnly = value;
                SaveSetting(KeyMapShowActiveOnly, value.ToString());
            }
        }
    }

    public bool HideCompletedObjectives
    {
        get
        {
            EnsureLoaded();
            return _hideCompletedObjectives ?? true;
        }
        set
        {
            if (_hideCompletedObjectives != value)
            {
                _hideCompletedObjectives = value;
                SaveSetting(KeyMapHideCompletedObjectives, value.ToString());
            }
        }
    }

    public int QuestMarkerStyle
    {
        get
        {
            EnsureLoaded();
            return _questMarkerStyle ?? 2;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0, 3);
            if (_questMarkerStyle != clampedValue)
            {
                _questMarkerStyle = clampedValue;
                SaveSetting(KeyMapQuestMarkerStyle, clampedValue.ToString());
            }
        }
    }

    public bool ShowKappaHighlight
    {
        get
        {
            EnsureLoaded();
            return _showKappaHighlight ?? true;
        }
        set
        {
            if (_showKappaHighlight != value)
            {
                _showKappaHighlight = value;
                SaveSetting(KeyMapShowKappaHighlight, value.ToString());
            }
        }
    }

    public string TraderFilter
    {
        get
        {
            EnsureLoaded();
            return _traderFilter ?? "";
        }
        set
        {
            if (_traderFilter != value)
            {
                _traderFilter = value;
                SaveSetting(KeyMapTraderFilter, value ?? "");
            }
        }
    }

    #endregion

    #region Properties - Markers

    public double MarkerScale
    {
        get
        {
            EnsureLoaded();
            return _markerScale ?? DefaultMarkerScale;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinMarkerScale, MaxMarkerScale);
            if (Math.Abs((_markerScale ?? DefaultMarkerScale) - clampedValue) > 0.01)
            {
                _markerScale = clampedValue;
                SaveSetting(KeyMapMarkerScale, clampedValue.ToString());
            }
        }
    }

    public double MarkerOpacity
    {
        get
        {
            EnsureLoaded();
            return _markerOpacity ?? 100;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0, 100);
            if (Math.Abs((_markerOpacity ?? 100) - clampedValue) > 0.5)
            {
                _markerOpacity = clampedValue;
                SaveSetting(KeyMapMarkerOpacity, clampedValue.ToString());
            }
        }
    }

    public bool AutoHideCompleted
    {
        get
        {
            EnsureLoaded();
            return _autoHideCompleted ?? false;
        }
        set
        {
            if (_autoHideCompleted != value)
            {
                _autoHideCompleted = value;
                SaveSetting(KeyMapAutoHideCompleted, value.ToString());
            }
        }
    }

    public bool FadeCompleted
    {
        get
        {
            EnsureLoaded();
            return _fadeCompleted ?? true;
        }
        set
        {
            if (_fadeCompleted != value)
            {
                _fadeCompleted = value;
                SaveSetting(KeyMapFadeCompleted, value.ToString());
            }
        }
    }

    public bool ShowLabels
    {
        get
        {
            EnsureLoaded();
            return _showLabels ?? false;
        }
        set
        {
            if (_showLabels != value)
            {
                _showLabels = value;
                SaveSetting(KeyMapShowLabels, value.ToString());
            }
        }
    }

    public double LabelScale
    {
        get
        {
            EnsureLoaded();
            return _labelScale ?? 1.0;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0.5, 1.5);
            if (Math.Abs((_labelScale ?? 1.0) - clampedValue) > 0.01)
            {
                _labelScale = clampedValue;
                SaveSetting(KeyMapLabelScale, clampedValue.ToString());
            }
        }
    }

    public int QuestMarkerSize
    {
        get
        {
            EnsureLoaded();
            return _questMarkerSize ?? 18;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 12, 32);
            if (_questMarkerSize != clampedValue)
            {
                _questMarkerSize = clampedValue;
                SaveSetting(KeyMapQuestMarkerSize, clampedValue.ToString());
            }
        }
    }

    public int PlayerMarkerSize
    {
        get
        {
            EnsureLoaded();
            return _playerMarkerSize ?? 18;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 12, 32);
            if (_playerMarkerSize != clampedValue)
            {
                _playerMarkerSize = clampedValue;
                SaveSetting(KeyMapPlayerMarkerSize, clampedValue.ToString());
            }
        }
    }

    public double ExtractNameSize
    {
        get
        {
            EnsureLoaded();
            return _extractNameSize ?? 16.0;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 10.0, 32.0);
            if (Math.Abs((_extractNameSize ?? 16.0) - clampedValue) > 0.1)
            {
                _extractNameSize = clampedValue;
                SaveSetting(KeyMapExtractNameSize, clampedValue.ToString());
            }
        }
    }

    public double QuestNameSize
    {
        get
        {
            EnsureLoaded();
            return _questNameSize ?? 20.0;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 12.0, 32.0);
            if (Math.Abs((_questNameSize ?? 20.0) - clampedValue) > 0.1)
            {
                _questNameSize = clampedValue;
                SaveSetting(KeyMapQuestNameSize, clampedValue.ToString());
            }
        }
    }

    #endregion

    #region Properties - Trail

    public bool ShowTrail
    {
        get
        {
            EnsureLoaded();
            return _showTrail ?? true;
        }
        set
        {
            if (_showTrail != value)
            {
                _showTrail = value;
                SaveSetting(KeyMapShowTrail, value.ToString());
            }
        }
    }

    public string TrailColor
    {
        get
        {
            EnsureLoaded();
            return _trailColor ?? "#00FF00";
        }
        set
        {
            if (_trailColor != value)
            {
                _trailColor = value;
                SaveSetting(KeyMapTrailColor, value ?? "#00FF00");
            }
        }
    }

    public double TrailThickness
    {
        get
        {
            EnsureLoaded();
            return _trailThickness ?? 2.0;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 1, 5);
            if (Math.Abs((_trailThickness ?? 2.0) - clampedValue) > 0.1)
            {
                _trailThickness = clampedValue;
                SaveSetting(KeyMapTrailThickness, clampedValue.ToString());
            }
        }
    }

    #endregion

    #region Properties - Minimap

    public bool ShowMinimap
    {
        get
        {
            EnsureLoaded();
            return _showMinimap ?? true;
        }
        set
        {
            if (_showMinimap != value)
            {
                _showMinimap = value;
                SaveSetting(KeyMapShowMinimap, value.ToString());
            }
        }
    }

    public string MinimapSize
    {
        get
        {
            EnsureLoaded();
            return _minimapSize ?? "M";
        }
        set
        {
            if (_minimapSize != value)
            {
                _minimapSize = value;
                SaveSetting(KeyMapMinimapSize, value ?? "M");
            }
        }
    }

    #endregion

    #region Properties - Clustering

    public bool ClusteringEnabled
    {
        get
        {
            EnsureLoaded();
            return _clusteringEnabled ?? true;
        }
        set
        {
            if (_clusteringEnabled != value)
            {
                _clusteringEnabled = value;
                SaveSetting(KeyMapClusteringEnabled, value.ToString());
            }
        }
    }

    public double ClusterZoomThreshold
    {
        get
        {
            EnsureLoaded();
            return _clusterZoomThreshold ?? 50;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0, 100);
            if (Math.Abs((_clusterZoomThreshold ?? 50) - clampedValue) > 0.5)
            {
                _clusterZoomThreshold = clampedValue;
                SaveSetting(KeyMapClusterZoomThreshold, clampedValue.ToString());
            }
        }
    }

    #endregion

    #region Properties - Layers

    public bool AutoFloorEnabled
    {
        get
        {
            EnsureLoaded();
            return _autoFloorEnabled ?? true;
        }
        set
        {
            if (_autoFloorEnabled != value)
            {
                _autoFloorEnabled = value;
                SaveSetting(KeyMapAutoFloorEnabled, value.ToString());
            }
        }
    }

    public bool ShowBosses
    {
        get
        {
            EnsureLoaded();
            return _showBosses ?? true;
        }
        set
        {
            if (_showBosses != value)
            {
                _showBosses = value;
                SaveSetting(KeyMapShowBosses, value.ToString());
            }
        }
    }

    public bool ShowSpawns
    {
        get
        {
            EnsureLoaded();
            return _showSpawns ?? true;
        }
        set
        {
            if (_showSpawns != value)
            {
                _showSpawns = value;
                SaveSetting(KeyMapShowSpawns, value.ToString());
            }
        }
    }

    public bool ShowLevers
    {
        get
        {
            EnsureLoaded();
            return _showLevers ?? true;
        }
        set
        {
            if (_showLevers != value)
            {
                _showLevers = value;
                SaveSetting(KeyMapShowLevers, value.ToString());
            }
        }
    }

    public bool ShowKeys
    {
        get
        {
            EnsureLoaded();
            return _showKeys ?? true;
        }
        set
        {
            if (_showKeys != value)
            {
                _showKeys = value;
                SaveSetting(KeyMapShowKeys, value.ToString());
            }
        }
    }

    #endregion

    #region Properties - Panels

    public bool LeftPanelExpanded
    {
        get
        {
            EnsureLoaded();
            return _leftPanelExpanded ?? false;
        }
        set
        {
            if (_leftPanelExpanded != value)
            {
                _leftPanelExpanded = value;
                SaveSetting(KeyMapLeftPanelExpanded, value.ToString());
            }
        }
    }

    public bool ExpanderLayersExpanded
    {
        get
        {
            EnsureLoaded();
            return _expanderLayersExpanded ?? false;
        }
        set
        {
            if (_expanderLayersExpanded != value)
            {
                _expanderLayersExpanded = value;
                SaveSetting(KeyExpanderLayersExpanded, value.ToString());
            }
        }
    }

    public bool ExpanderFloorExpanded
    {
        get
        {
            EnsureLoaded();
            return _expanderFloorExpanded ?? false;
        }
        set
        {
            if (_expanderFloorExpanded != value)
            {
                _expanderFloorExpanded = value;
                SaveSetting(KeyExpanderFloorExpanded, value.ToString());
            }
        }
    }

    public bool ExpanderMapInfoExpanded
    {
        get
        {
            EnsureLoaded();
            return _expanderMapInfoExpanded ?? false;
        }
        set
        {
            if (_expanderMapInfoExpanded != value)
            {
                _expanderMapInfoExpanded = value;
                SaveSetting(KeyExpanderMapInfoExpanded, value.ToString());
            }
        }
    }

    public bool QuestPanelVisible
    {
        get
        {
            EnsureLoaded();
            return _questPanelVisible ?? true;
        }
        set
        {
            if (_questPanelVisible != value)
            {
                _questPanelVisible = value;
                SaveSetting(KeyQuestPanelVisible, value.ToString());
            }
        }
    }

    #endregion

    #region Properties - Tracking

    public bool AutoStartTracking
    {
        get
        {
            EnsureLoaded();
            return _autoStartTracking ?? false;
        }
        set
        {
            if (_autoStartTracking != value)
            {
                _autoStartTracking = value;
                SaveSetting(KeyMapAutoStartTracking, value.ToString());
            }
        }
    }

    public string? ScreenshotPath
    {
        get
        {
            EnsureLoaded();
            return string.IsNullOrEmpty(_screenshotPath) ? null : _screenshotPath;
        }
        set
        {
            if (_screenshotPath != value)
            {
                _screenshotPath = value;
                SaveSetting(KeyMapScreenshotPath, value ?? "");
            }
        }
    }

    #endregion

    #region Properties - View State

    public double LastZoomLevel
    {
        get
        {
            EnsureLoaded();
            return _lastZoomLevel ?? 1.0;
        }
        set
        {
            if (Math.Abs((_lastZoomLevel ?? 1.0) - value) > 0.01)
            {
                _lastZoomLevel = value;
                SaveSetting(KeyMapLastZoomLevel, value.ToString());
            }
        }
    }

    public double LastTranslateX
    {
        get
        {
            EnsureLoaded();
            return _lastTranslateX ?? 0;
        }
        set
        {
            if (Math.Abs((_lastTranslateX ?? 0) - value) > 1)
            {
                _lastTranslateX = value;
                SaveSetting(KeyMapLastTranslateX, value.ToString());
            }
        }
    }

    public double LastTranslateY
    {
        get
        {
            EnsureLoaded();
            return _lastTranslateY ?? 0;
        }
        set
        {
            if (Math.Abs((_lastTranslateY ?? 0) - value) > 1)
            {
                _lastTranslateY = value;
                SaveSetting(KeyMapLastTranslateY, value.ToString());
            }
        }
    }

    #endregion

    #region Helper Methods

    public void AddHiddenQuest(string questId)
    {
        var hidden = HiddenQuests;
        if (hidden.Add(questId))
        {
            HiddenQuests = hidden;
        }
    }

    public void RemoveHiddenQuest(string questId)
    {
        var hidden = HiddenQuests;
        if (hidden.Remove(questId))
        {
            HiddenQuests = hidden;
        }
    }

    public void ClearHiddenQuests()
    {
        HiddenQuests = new HashSet<string>();
    }

    public void ToggleQuestCollapsed(string questId)
    {
        var collapsed = CollapsedQuests;
        if (collapsed.Contains(questId))
            collapsed.Remove(questId);
        else
            collapsed.Add(questId);
        CollapsedQuests = collapsed;
    }

    #endregion

    #region Private Methods

    private void EnsureLoaded()
    {
        if (!_settingsLoaded) LoadSettings();
    }

    private void SaveSetting(string key, string value)
    {
        try
        {
            _userDataDb.SetSetting(key, value);
        }
        catch (Exception ex)
        {
            _log.Error($"Save failed: {ex.Message}");
        }
    }

    private void LoadSettings()
    {
        _settingsLoaded = true;

        try
        {
            // Drawer
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapDrawerOpen), out var drawerOpen))
                _drawerOpen = drawerOpen;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapDrawerWidth), out var drawerWidth))
                _drawerWidth = drawerWidth;

            // Extracts
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowExtracts), out var showExtracts))
                _showExtracts = showExtracts;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowPmcExtracts), out var showPmcExtracts))
                _showPmcExtracts = showPmcExtracts;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowScavExtracts), out var showScavExtracts))
                _showScavExtracts = showScavExtracts;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowTransits), out var showTransits))
                _showTransits = showTransits;

            // Quests
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowQuests), out var showQuests))
                _showQuests = showQuests;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapIncompleteOnly), out var incompleteOnly))
                _incompleteOnly = incompleteOnly;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapCurrentMapOnly), out var currentMapOnly))
                _currentMapOnly = currentMapOnly;

            _sortOption = _userDataDb.GetSetting(KeyMapSortOption);
            if (string.IsNullOrEmpty(_sortOption)) _sortOption = "name";

            // Hidden quests (JSON array)
            var hiddenJson = _userDataDb.GetSetting(KeyMapHiddenQuests);
            if (!string.IsNullOrEmpty(hiddenJson))
            {
                try
                {
                    var hiddenArray = JsonSerializer.Deserialize<string[]>(hiddenJson);
                    _hiddenQuests = hiddenArray != null ? new HashSet<string>(hiddenArray) : new HashSet<string>();
                }
                catch
                {
                    _hiddenQuests = new HashSet<string>();
                }
            }

            // Collapsed quests (JSON array)
            var collapsedJson = _userDataDb.GetSetting(KeyMapCollapsedQuests);
            if (!string.IsNullOrEmpty(collapsedJson))
            {
                try
                {
                    var collapsedArray = JsonSerializer.Deserialize<string[]>(collapsedJson);
                    _collapsedQuests = collapsedArray != null ? new HashSet<string>(collapsedArray) : new HashSet<string>();
                }
                catch
                {
                    _collapsedQuests = new HashSet<string>();
                }
            }

            _lastSelectedMap = _userDataDb.GetSetting(KeyMapLastSelectedMap);
            if (string.IsNullOrEmpty(_lastSelectedMap)) _lastSelectedMap = null;

            // Markers
            if (double.TryParse(_userDataDb.GetSetting(KeyMapMarkerScale), out var markerScale))
                _markerScale = markerScale;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapMarkerOpacity), out var markerOpacity))
                _markerOpacity = markerOpacity;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapAutoHideCompleted), out var autoHideCompleted))
                _autoHideCompleted = autoHideCompleted;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapFadeCompleted), out var fadeCompleted))
                _fadeCompleted = fadeCompleted;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowLabels), out var showLabels))
                _showLabels = showLabels;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapLabelScale), out var labelScale))
                _labelScale = labelScale;

            if (int.TryParse(_userDataDb.GetSetting(KeyMapQuestMarkerSize), out var questMarkerSize))
                _questMarkerSize = questMarkerSize;

            if (int.TryParse(_userDataDb.GetSetting(KeyMapPlayerMarkerSize), out var playerMarkerSize))
                _playerMarkerSize = playerMarkerSize;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapExtractNameSize), out var extractNameSize))
                _extractNameSize = extractNameSize;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapQuestNameSize), out var questNameSize))
                _questNameSize = questNameSize;

            // Quest display
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapQuestStatusColors), out var questStatusColors))
                _questStatusColors = questStatusColors;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapHideCompletedQuests), out var hideCompletedQuests))
                _hideCompletedQuests = hideCompletedQuests;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowActiveOnly), out var showActiveOnly))
                _showActiveOnly = showActiveOnly;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapHideCompletedObjectives), out var hideCompletedObjectives))
                _hideCompletedObjectives = hideCompletedObjectives;

            if (int.TryParse(_userDataDb.GetSetting(KeyMapQuestMarkerStyle), out var questMarkerStyle))
                _questMarkerStyle = questMarkerStyle;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowKappaHighlight), out var showKappaHighlight))
                _showKappaHighlight = showKappaHighlight;

            _traderFilter = _userDataDb.GetSetting(KeyMapTraderFilter);
            if (string.IsNullOrEmpty(_traderFilter)) _traderFilter = null;

            // Trail
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowTrail), out var showTrail))
                _showTrail = showTrail;

            _trailColor = _userDataDb.GetSetting(KeyMapTrailColor);
            if (string.IsNullOrEmpty(_trailColor)) _trailColor = "#00FF00";

            if (double.TryParse(_userDataDb.GetSetting(KeyMapTrailThickness), out var trailThickness))
                _trailThickness = trailThickness;

            // Minimap
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowMinimap), out var showMinimap))
                _showMinimap = showMinimap;

            _minimapSize = _userDataDb.GetSetting(KeyMapMinimapSize);
            if (string.IsNullOrEmpty(_minimapSize)) _minimapSize = "M";

            // Clustering
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapClusteringEnabled), out var clusteringEnabled))
                _clusteringEnabled = clusteringEnabled;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapClusterZoomThreshold), out var clusterZoomThreshold))
                _clusterZoomThreshold = clusterZoomThreshold;

            // Layers
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapAutoFloorEnabled), out var autoFloorEnabled))
                _autoFloorEnabled = autoFloorEnabled;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowBosses), out var showBosses))
                _showBosses = showBosses;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowSpawns), out var showSpawns))
                _showSpawns = showSpawns;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowLevers), out var showLevers))
                _showLevers = showLevers;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowKeys), out var showKeys))
                _showKeys = showKeys;

            // Panels
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapLeftPanelExpanded), out var leftPanelExpanded))
                _leftPanelExpanded = leftPanelExpanded;

            if (bool.TryParse(_userDataDb.GetSetting(KeyExpanderLayersExpanded), out var expanderLayersExpanded))
                _expanderLayersExpanded = expanderLayersExpanded;

            if (bool.TryParse(_userDataDb.GetSetting(KeyExpanderFloorExpanded), out var expanderFloorExpanded))
                _expanderFloorExpanded = expanderFloorExpanded;

            if (bool.TryParse(_userDataDb.GetSetting(KeyExpanderMapInfoExpanded), out var expanderMapInfoExpanded))
                _expanderMapInfoExpanded = expanderMapInfoExpanded;

            if (bool.TryParse(_userDataDb.GetSetting(KeyQuestPanelVisible), out var questPanelVisible))
                _questPanelVisible = questPanelVisible;

            // Tracking
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapAutoStartTracking), out var autoStartTracking))
                _autoStartTracking = autoStartTracking;

            _screenshotPath = _userDataDb.GetSetting(KeyMapScreenshotPath);
            if (string.IsNullOrEmpty(_screenshotPath)) _screenshotPath = null;

            // View state
            if (double.TryParse(_userDataDb.GetSetting(KeyMapLastZoomLevel), out var lastZoomLevel))
                _lastZoomLevel = lastZoomLevel;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapLastTranslateX), out var lastTranslateX))
                _lastTranslateX = lastTranslateX;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapLastTranslateY), out var lastTranslateY))
                _lastTranslateY = lastTranslateY;
        }
        catch (Exception ex)
        {
            _log.Error($"Load failed: {ex.Message}");
        }
    }

    #endregion
}
