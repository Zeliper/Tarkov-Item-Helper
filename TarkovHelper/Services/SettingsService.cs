using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TarkovHelper.Debug;

namespace TarkovHelper.Services;

/// <summary>
/// Application settings service for managing user preferences
/// Settings are stored in user_data.db (UserSettings table)
/// </summary>
public class SettingsService
{
    private static SettingsService? _instance;
    public static SettingsService Instance => _instance ??= new SettingsService();

    private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

    // Setting keys
    private const string KeyLogFolderPath = "app.logFolderPath";
    private const string KeyPlayerLevel = "app.playerLevel";
    private const string KeyScavRep = "app.scavRep";
    private const string KeyShowLevelLockedQuests = "app.showLevelLockedQuests";
    private const string KeyHideWipeWarning = "app.hideWipeWarning";
    private const string KeySyncDaysRange = "app.syncDaysRange";
    private const string KeyBaseFontSize = "app.baseFontSize";
    private const string KeyDspDecodeCount = "app.dspDecodeCount";
    private const string KeyPlayerFaction = "app.playerFaction";
    private const string KeyHasEodEdition = "app.hasEodEdition";
    private const string KeyHasUnheardEdition = "app.hasUnheardEdition";
    private const string KeyPrestigeLevel = "app.prestigeLevel";

    // Map settings keys
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
    private const string KeyMapShowKappaHighlight = "map.showKappaHighlight";
    private const string KeyMapTraderFilter = "map.traderFilter";
    private const string KeyMapTrailColor = "map.trailColor";
    private const string KeyMapTrailThickness = "map.trailThickness";
    private const string KeyMapAutoStartTracking = "map.autoStartTracking";
    private const string KeyMapClusteringEnabled = "map.clusteringEnabled";
    private const string KeyMapClusterZoomThreshold = "map.clusterZoomThreshold";
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

    private bool _settingsLoaded;
    private string? _detectionMethod;

    // Cached values
    private string? _logFolderPath;
    private int? _playerLevel;
    private double? _scavRep;
    private bool? _showLevelLockedQuests;
    private bool? _hideWipeWarning;
    private int? _syncDaysRange;
    private double? _baseFontSize;
    private int? _dspDecodeCount;
    private string? _playerFaction;
    private bool? _hasEodEdition;
    private bool? _hasUnheardEdition;
    private int? _prestigeLevel;

    // Map cached values
    private bool? _mapDrawerOpen;
    private double? _mapDrawerWidth;
    private bool? _mapShowExtracts;
    private bool? _mapShowPmcExtracts;
    private bool? _mapShowScavExtracts;
    private bool? _mapShowTransits;
    private bool? _mapShowQuests;
    private bool? _mapIncompleteOnly;
    private bool? _mapCurrentMapOnly;
    private string? _mapSortOption;
    private HashSet<string>? _mapHiddenQuests;
    private HashSet<string>? _mapCollapsedQuests;
    private string? _mapLastSelectedMap;
    private double? _mapMarkerScale;
    private bool? _mapShowTrail;
    private bool? _mapShowMinimap;
    private string? _mapMinimapSize;
    private double? _mapMarkerOpacity;
    private bool? _mapAutoHideCompleted;
    private bool? _mapFadeCompleted;
    private bool? _mapShowLabels;
    private double? _mapLabelScale;
    private bool? _mapQuestStatusColors;
    private bool? _mapHideCompletedQuests;
    private bool? _mapShowActiveOnly;
    private bool? _mapHideCompletedObjectives;
    private bool? _mapShowKappaHighlight;
    private string? _mapTraderFilter;  // Empty = all, or trader name
    private string? _mapTrailColor;
    private double? _mapTrailThickness;
    private bool? _mapAutoStartTracking;
    private bool? _mapClusteringEnabled;
    private double? _mapClusterZoomThreshold;
    private bool? _mapAutoFloorEnabled;
    private bool? _mapShowBosses;
    private bool? _mapShowSpawns;
    private bool? _mapShowLevers;
    private bool? _mapShowKeys;
    private string? _mapScreenshotPath;

    public event EventHandler<string?>? LogFolderChanged;
    public event EventHandler<int>? PlayerLevelChanged;
    public event EventHandler<double>? ScavRepChanged;
    public event EventHandler<double>? BaseFontSizeChanged;
    public event EventHandler<int>? DspDecodeCountChanged;
    public event EventHandler<string?>? PlayerFactionChanged;
    public event EventHandler<bool>? HasEodEditionChanged;
    public event EventHandler<bool>? HasUnheardEditionChanged;
    public event EventHandler<int>? PrestigeLevelChanged;

    private SettingsService()
    {
        LoadSettings();
    }

    /// <summary>
    /// Player level constants
    /// </summary>
    public const int MinPlayerLevel = 1;
    public const int MaxPlayerLevel = 79;
    public const int DefaultPlayerLevel = 15;

    /// <summary>
    /// Scav Rep constants
    /// </summary>
    public const double MinScavRep = -6.0;
    public const double MaxScavRep = 6.0;
    public const double DefaultScavRep = 1.0;
    public const double ScavRepStep = 0.1;

    /// <summary>
    /// Font size constants
    /// </summary>
    public const double MinFontSize = 10;
    public const double MaxFontSize = 28;
    public const double DefaultBaseFontSize = 18;

    /// <summary>
    /// DSP Decode count constants (for Make Amends quest branches)
    /// </summary>
    public const int MinDspDecodeCount = 0;
    public const int MaxDspDecodeCount = 3;
    public const int DefaultDspDecodeCount = 0;

    /// <summary>
    /// Prestige level constants
    /// </summary>
    public const int MinPrestigeLevel = 0;
    public const int MaxPrestigeLevel = 5;
    public const int DefaultPrestigeLevel = 0;

    /// <summary>
    /// Player level for quest filtering
    /// </summary>
    public int PlayerLevel
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _playerLevel ?? DefaultPlayerLevel;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinPlayerLevel, MaxPlayerLevel);
            if (_playerLevel != clampedValue)
            {
                _playerLevel = clampedValue;
                SaveSetting(KeyPlayerLevel, clampedValue.ToString());
                PlayerLevelChanged?.Invoke(this, clampedValue);
            }
        }
    }

    /// <summary>
    /// Whether to show level-locked quests in the quest list
    /// </summary>
    public bool ShowLevelLockedQuests
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _showLevelLockedQuests ?? true;
        }
        set
        {
            _showLevelLockedQuests = value;
            SaveSetting(KeyShowLevelLockedQuests, value.ToString());
        }
    }

    /// <summary>
    /// Scav reputation for quest filtering (Fence karma)
    /// </summary>
    public double ScavRep
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _scavRep ?? DefaultScavRep;
        }
        set
        {
            var clampedValue = Math.Round(Math.Clamp(value, MinScavRep, MaxScavRep), 1);
            if (Math.Abs((_scavRep ?? DefaultScavRep) - clampedValue) > 0.01)
            {
                _scavRep = clampedValue;
                SaveSetting(KeyScavRep, clampedValue.ToString());
                ScavRepChanged?.Invoke(this, clampedValue);
            }
        }
    }

    /// <summary>
    /// Log folder path (user-set or auto-detected)
    /// </summary>
    public string? LogFolderPath
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();

            // If user has set a path, use it
            if (!string.IsNullOrEmpty(_logFolderPath))
            {
                return _logFolderPath;
            }

            // Otherwise try auto-detection
            return AutoDetectLogFolder();
        }
        set
        {
            _logFolderPath = value;
            SaveSetting(KeyLogFolderPath, value ?? "");
            LogFolderChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// How the log folder was detected
    /// </summary>
    public string? DetectionMethod => _detectionMethod;

    /// <summary>
    /// Check if log folder is valid
    /// </summary>
    public bool IsLogFolderValid
    {
        get
        {
            var folder = LogFolderPath;
            return !string.IsNullOrEmpty(folder) && Directory.Exists(folder);
        }
    }

    /// <summary>
    /// Whether to hide the wipe warning dialog before quest sync
    /// </summary>
    public bool HideWipeWarning
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _hideWipeWarning ?? false;
        }
        set
        {
            _hideWipeWarning = value;
            SaveSetting(KeyHideWipeWarning, value.ToString());
        }
    }

    /// <summary>
    /// Number of days to look back when syncing quest progress from logs
    /// 0 = All logs, 1-30 = specific range
    /// </summary>
    public int SyncDaysRange
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _syncDaysRange ?? 0;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0, 30);
            if (_syncDaysRange != clampedValue)
            {
                _syncDaysRange = clampedValue;
                SaveSetting(KeySyncDaysRange, clampedValue.ToString());
            }
        }
    }

    /// <summary>
    /// Base font size for the application
    /// </summary>
    public double BaseFontSize
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _baseFontSize ?? DefaultBaseFontSize;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinFontSize, MaxFontSize);
            if (Math.Abs((_baseFontSize ?? DefaultBaseFontSize) - clampedValue) > 0.01)
            {
                _baseFontSize = clampedValue;
                SaveSetting(KeyBaseFontSize, clampedValue.ToString());
                BaseFontSizeChanged?.Invoke(this, clampedValue);
            }
        }
    }

    /// <summary>
    /// DSP Radio Transmitter decode count for Make Amends quest branches
    /// 0 = Buyout, 1 = Security, 2 or 3 = Software
    /// </summary>
    public int DspDecodeCount
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _dspDecodeCount ?? DefaultDspDecodeCount;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinDspDecodeCount, MaxDspDecodeCount);
            if (_dspDecodeCount != clampedValue)
            {
                _dspDecodeCount = clampedValue;
                SaveSetting(KeyDspDecodeCount, clampedValue.ToString());
                DspDecodeCountChanged?.Invoke(this, clampedValue);
            }
        }
    }

    /// <summary>
    /// Player faction (bear, usec, or null for any/both)
    /// </summary>
    public string? PlayerFaction
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _playerFaction;
        }
        set
        {
            var normalizedValue = string.IsNullOrEmpty(value) ? null : value.ToLowerInvariant();
            if (_playerFaction != normalizedValue)
            {
                _playerFaction = normalizedValue;
                SaveSetting(KeyPlayerFaction, normalizedValue ?? "");
                PlayerFactionChanged?.Invoke(this, normalizedValue);
            }
        }
    }

    /// <summary>
    /// Check if a task should be included based on player's selected faction
    /// </summary>
    public bool ShouldIncludeTask(string? taskFaction)
    {
        if (string.IsNullOrEmpty(taskFaction))
            return true;

        var playerFaction = PlayerFaction;
        if (string.IsNullOrEmpty(playerFaction))
            return true;

        return string.Equals(taskFaction, playerFaction, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether player has Edge of Darkness edition
    /// </summary>
    public bool HasEodEdition
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _hasEodEdition ?? false;
        }
        set
        {
            if (_hasEodEdition != value)
            {
                _hasEodEdition = value;
                SaveSetting(KeyHasEodEdition, value.ToString());
                HasEodEditionChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Whether player has The Unheard edition
    /// </summary>
    public bool HasUnheardEdition
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _hasUnheardEdition ?? false;
        }
        set
        {
            if (_hasUnheardEdition != value)
            {
                _hasUnheardEdition = value;
                SaveSetting(KeyHasUnheardEdition, value.ToString());
                HasUnheardEditionChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Player's prestige level (0-5)
    /// </summary>
    public int PrestigeLevel
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _prestigeLevel ?? DefaultPrestigeLevel;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinPrestigeLevel, MaxPrestigeLevel);
            if (_prestigeLevel != clampedValue)
            {
                _prestigeLevel = clampedValue;
                SaveSetting(KeyPrestigeLevel, clampedValue.ToString());
                PrestigeLevelChanged?.Invoke(this, clampedValue);
            }
        }
    }

    #region Map Settings

    /// <summary>
    /// Map marker scale constants
    /// </summary>
    public const double MinMarkerScale = 0.5;
    public const double MaxMarkerScale = 2.0;
    public const double DefaultMarkerScale = 1.0;
    public const double DefaultDrawerWidth = 320;

    /// <summary>
    /// Whether the Quest Drawer is open (defaults to true for sidebar always visible)
    /// </summary>
    public bool MapDrawerOpen
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapDrawerOpen ?? true;  // Default to open
        }
        set
        {
            if (_mapDrawerOpen != value)
            {
                _mapDrawerOpen = value;
                SaveSetting(KeyMapDrawerOpen, value.ToString());
            }
        }
    }

    /// <summary>
    /// Quest Drawer width in pixels
    /// </summary>
    public double MapDrawerWidth
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapDrawerWidth ?? DefaultDrawerWidth;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 250, 500);
            if (Math.Abs((_mapDrawerWidth ?? DefaultDrawerWidth) - clampedValue) > 1)
            {
                _mapDrawerWidth = clampedValue;
                SaveSetting(KeyMapDrawerWidth, clampedValue.ToString());
            }
        }
    }

    /// <summary>
    /// Show extract markers on map
    /// </summary>
    public bool MapShowExtracts
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowExtracts ?? true;
        }
        set
        {
            if (_mapShowExtracts != value)
            {
                _mapShowExtracts = value;
                SaveSetting(KeyMapShowExtracts, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show PMC extraction markers on map
    /// </summary>
    public bool MapShowPmcExtracts
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowPmcExtracts ?? true;
        }
        set
        {
            if (_mapShowPmcExtracts != value)
            {
                _mapShowPmcExtracts = value;
                SaveSetting(KeyMapShowPmcExtracts, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show Scav extraction markers on map
    /// </summary>
    public bool MapShowScavExtracts
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowScavExtracts ?? true;
        }
        set
        {
            if (_mapShowScavExtracts != value)
            {
                _mapShowScavExtracts = value;
                SaveSetting(KeyMapShowScavExtracts, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show transit markers on map
    /// </summary>
    public bool MapShowTransits
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowTransits ?? true;
        }
        set
        {
            if (_mapShowTransits != value)
            {
                _mapShowTransits = value;
                SaveSetting(KeyMapShowTransits, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show quest markers on map
    /// </summary>
    public bool MapShowQuests
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowQuests ?? true;
        }
        set
        {
            if (_mapShowQuests != value)
            {
                _mapShowQuests = value;
                SaveSetting(KeyMapShowQuests, value.ToString());
            }
        }
    }

    /// <summary>
    /// Filter to show only incomplete quests in drawer
    /// </summary>
    public bool MapIncompleteOnly
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapIncompleteOnly ?? false;
        }
        set
        {
            if (_mapIncompleteOnly != value)
            {
                _mapIncompleteOnly = value;
                SaveSetting(KeyMapIncompleteOnly, value.ToString());
            }
        }
    }

    /// <summary>
    /// Filter to show only current map quests in drawer
    /// </summary>
    public bool MapCurrentMapOnly
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapCurrentMapOnly ?? true;
        }
        set
        {
            if (_mapCurrentMapOnly != value)
            {
                _mapCurrentMapOnly = value;
                SaveSetting(KeyMapCurrentMapOnly, value.ToString());
            }
        }
    }

    /// <summary>
    /// Sort option for quest drawer (name, progress, count)
    /// </summary>
    public string MapSortOption
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapSortOption ?? "name";
        }
        set
        {
            if (_mapSortOption != value)
            {
                _mapSortOption = value;
                SaveSetting(KeyMapSortOption, value ?? "name");
            }
        }
    }

    /// <summary>
    /// Set of hidden quest IDs
    /// </summary>
    public HashSet<string> MapHiddenQuests
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapHiddenQuests ?? new HashSet<string>();
        }
        set
        {
            _mapHiddenQuests = value;
            var json = JsonSerializer.Serialize(value?.ToArray() ?? Array.Empty<string>());
            SaveSetting(KeyMapHiddenQuests, json);
        }
    }

    /// <summary>
    /// Set of collapsed quest IDs in drawer
    /// </summary>
    public HashSet<string> MapCollapsedQuests
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapCollapsedQuests ?? new HashSet<string>();
        }
        set
        {
            _mapCollapsedQuests = value;
            var json = JsonSerializer.Serialize(value?.ToArray() ?? Array.Empty<string>());
            SaveSetting(KeyMapCollapsedQuests, json);
        }
    }

    /// <summary>
    /// Last selected map key
    /// </summary>
    public string? MapLastSelectedMap
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapLastSelectedMap;
        }
        set
        {
            if (_mapLastSelectedMap != value)
            {
                _mapLastSelectedMap = value;
                SaveSetting(KeyMapLastSelectedMap, value ?? "");
            }
        }
    }

    /// <summary>
    /// Marker scale multiplier (0.5 - 2.0)
    /// </summary>
    public double MapMarkerScale
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapMarkerScale ?? DefaultMarkerScale;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinMarkerScale, MaxMarkerScale);
            if (Math.Abs((_mapMarkerScale ?? DefaultMarkerScale) - clampedValue) > 0.01)
            {
                _mapMarkerScale = clampedValue;
                SaveSetting(KeyMapMarkerScale, clampedValue.ToString());
            }
        }
    }

    /// <summary>
    /// Show trail/path on map
    /// </summary>
    public bool MapShowTrail
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowTrail ?? true;
        }
        set
        {
            if (_mapShowTrail != value)
            {
                _mapShowTrail = value;
                SaveSetting(KeyMapShowTrail, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show minimap overlay
    /// </summary>
    public bool MapShowMinimap
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowMinimap ?? true;
        }
        set
        {
            if (_mapShowMinimap != value)
            {
                _mapShowMinimap = value;
                SaveSetting(KeyMapShowMinimap, value.ToString());
            }
        }
    }

    /// <summary>
    /// Minimap size (S, M, L)
    /// </summary>
    public string MapMinimapSize
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapMinimapSize ?? "M";
        }
        set
        {
            if (_mapMinimapSize != value)
            {
                _mapMinimapSize = value;
                SaveSetting(KeyMapMinimapSize, value ?? "M");
            }
        }
    }

    /// <summary>
    /// Marker opacity (0-100)
    /// </summary>
    public double MapMarkerOpacity
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapMarkerOpacity ?? 100;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0, 100);
            if (Math.Abs((_mapMarkerOpacity ?? 100) - clampedValue) > 0.5)
            {
                _mapMarkerOpacity = clampedValue;
                SaveSetting(KeyMapMarkerOpacity, clampedValue.ToString());
            }
        }
    }

    /// <summary>
    /// Auto-hide completed quest markers
    /// </summary>
    public bool MapAutoHideCompleted
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapAutoHideCompleted ?? false;
        }
        set
        {
            if (_mapAutoHideCompleted != value)
            {
                _mapAutoHideCompleted = value;
                SaveSetting(KeyMapAutoHideCompleted, value.ToString());
            }
        }
    }

    /// <summary>
    /// Fade completed quest markers instead of hiding
    /// </summary>
    public bool MapFadeCompleted
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapFadeCompleted ?? true;
        }
        set
        {
            if (_mapFadeCompleted != value)
            {
                _mapFadeCompleted = value;
                SaveSetting(KeyMapFadeCompleted, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show marker labels
    /// </summary>
    public bool MapShowLabels
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowLabels ?? false;
        }
        set
        {
            if (_mapShowLabels != value)
            {
                _mapShowLabels = value;
                SaveSetting(KeyMapShowLabels, value.ToString());
            }
        }
    }

    /// <summary>
    /// Label scale (0.5 - 1.5)
    /// </summary>
    public double MapLabelScale
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapLabelScale ?? 1.0;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0.5, 1.5);
            if (Math.Abs((_mapLabelScale ?? 1.0) - clampedValue) > 0.01)
            {
                _mapLabelScale = clampedValue;
                SaveSetting(KeyMapLabelScale, clampedValue.ToString());
            }
        }
    }

    /// <summary>
    /// Color quests by status (Active/Locked/Done)
    /// </summary>
    public bool MapQuestStatusColors
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapQuestStatusColors ?? true;
        }
        set
        {
            if (_mapQuestStatusColors != value)
            {
                _mapQuestStatusColors = value;
                SaveSetting(KeyMapQuestStatusColors, value.ToString());
            }
        }
    }

    /// <summary>
    /// Hide completed quests on map
    /// </summary>
    public bool MapHideCompletedQuests
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapHideCompletedQuests ?? false;
        }
        set
        {
            if (_mapHideCompletedQuests != value)
            {
                _mapHideCompletedQuests = value;
                SaveSetting(KeyMapHideCompletedQuests, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show only active quests on map
    /// </summary>
    public bool MapShowActiveOnly
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowActiveOnly ?? false;
        }
        set
        {
            if (_mapShowActiveOnly != value)
            {
                _mapShowActiveOnly = value;
                SaveSetting(KeyMapShowActiveOnly, value.ToString());
            }
        }
    }

    /// <summary>
    /// Hide individually completed objectives on map
    /// </summary>
    public bool MapHideCompletedObjectives
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapHideCompletedObjectives ?? false;
        }
        set
        {
            if (_mapHideCompletedObjectives != value)
            {
                _mapHideCompletedObjectives = value;
                SaveSetting(KeyMapHideCompletedObjectives, value.ToString());
            }
        }
    }

    /// <summary>
    /// Highlight Kappa-required quests on map
    /// </summary>
    public bool MapShowKappaHighlight
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowKappaHighlight ?? true;  // Default: show kappa highlight
        }
        set
        {
            if (_mapShowKappaHighlight != value)
            {
                _mapShowKappaHighlight = value;
                SaveSetting(KeyMapShowKappaHighlight, value.ToString());
            }
        }
    }

    /// <summary>
    /// Trader filter for quests on map (empty = all traders)
    /// </summary>
    public string MapTraderFilter
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapTraderFilter ?? "";
        }
        set
        {
            if (_mapTraderFilter != value)
            {
                _mapTraderFilter = value;
                SaveSetting(KeyMapTraderFilter, value ?? "");
            }
        }
    }

    /// <summary>
    /// Trail color (hex string)
    /// </summary>
    public string MapTrailColor
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapTrailColor ?? "#00FF00";
        }
        set
        {
            if (_mapTrailColor != value)
            {
                _mapTrailColor = value;
                SaveSetting(KeyMapTrailColor, value ?? "#00FF00");
            }
        }
    }

    /// <summary>
    /// Trail thickness (1-5)
    /// </summary>
    public double MapTrailThickness
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapTrailThickness ?? 2.0;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 1, 5);
            if (Math.Abs((_mapTrailThickness ?? 2.0) - clampedValue) > 0.1)
            {
                _mapTrailThickness = clampedValue;
                SaveSetting(KeyMapTrailThickness, clampedValue.ToString());
            }
        }
    }

    /// <summary>
    /// Auto-start tracking when map opens
    /// </summary>
    public bool MapAutoStartTracking
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapAutoStartTracking ?? false;
        }
        set
        {
            if (_mapAutoStartTracking != value)
            {
                _mapAutoStartTracking = value;
                SaveSetting(KeyMapAutoStartTracking, value.ToString());
            }
        }
    }

    /// <summary>
    /// Enable marker clustering
    /// </summary>
    public bool MapClusteringEnabled
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapClusteringEnabled ?? true;
        }
        set
        {
            if (_mapClusteringEnabled != value)
            {
                _mapClusteringEnabled = value;
                SaveSetting(KeyMapClusteringEnabled, value.ToString());
            }
        }
    }

    /// <summary>
    /// Zoom threshold for clustering (0-100)
    /// </summary>
    public double MapClusterZoomThreshold
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapClusterZoomThreshold ?? 50;
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0, 100);
            if (Math.Abs((_mapClusterZoomThreshold ?? 50) - clampedValue) > 0.5)
            {
                _mapClusterZoomThreshold = clampedValue;
                SaveSetting(KeyMapClusterZoomThreshold, clampedValue.ToString());
            }
        }
    }

    /// <summary>
    /// Enable auto floor detection
    /// </summary>
    public bool MapAutoFloorEnabled
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapAutoFloorEnabled ?? true;
        }
        set
        {
            if (_mapAutoFloorEnabled != value)
            {
                _mapAutoFloorEnabled = value;
                SaveSetting(KeyMapAutoFloorEnabled, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show boss markers on map
    /// </summary>
    public bool MapShowBosses
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowBosses ?? true;
        }
        set
        {
            if (_mapShowBosses != value)
            {
                _mapShowBosses = value;
                SaveSetting(KeyMapShowBosses, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show spawn markers on map
    /// </summary>
    public bool MapShowSpawns
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowSpawns ?? true;
        }
        set
        {
            if (_mapShowSpawns != value)
            {
                _mapShowSpawns = value;
                SaveSetting(KeyMapShowSpawns, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show lever markers on map
    /// </summary>
    public bool MapShowLevers
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowLevers ?? true;
        }
        set
        {
            if (_mapShowLevers != value)
            {
                _mapShowLevers = value;
                SaveSetting(KeyMapShowLevers, value.ToString());
            }
        }
    }

    /// <summary>
    /// Show key markers on map
    /// </summary>
    public bool MapShowKeys
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapShowKeys ?? true;
        }
        set
        {
            if (_mapShowKeys != value)
            {
                _mapShowKeys = value;
                SaveSetting(KeyMapShowKeys, value.ToString());
            }
        }
    }

    private bool? _mapLeftPanelExpanded;
    /// <summary>
    /// Left panel expanded state (true=80px with labels, false=48px icon only)
    /// </summary>
    public bool LeftPanelExpanded
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _mapLeftPanelExpanded ?? false;  // Default: collapsed
        }
        set
        {
            if (_mapLeftPanelExpanded != value)
            {
                _mapLeftPanelExpanded = value;
                SaveSetting(KeyMapLeftPanelExpanded, value.ToString());
            }
        }
    }

    private bool? _expanderLayersExpanded;
    public bool ExpanderLayersExpanded
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _expanderLayersExpanded ?? false;  // Default: collapsed
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

    private bool? _expanderFloorExpanded;
    public bool ExpanderFloorExpanded
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _expanderFloorExpanded ?? false;  // Default: collapsed
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

    private bool? _expanderMapInfoExpanded;
    public bool ExpanderMapInfoExpanded
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _expanderMapInfoExpanded ?? false;  // Default: collapsed
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

    private bool? _questPanelVisible;
    public bool QuestPanelVisible
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _questPanelVisible ?? true;  // Default: visible
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

    /// <summary>
    /// Custom screenshot folder path for map tracking (user-set, empty = auto-detect)
    /// </summary>
    public string? MapScreenshotPath
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return string.IsNullOrEmpty(_mapScreenshotPath) ? null : _mapScreenshotPath;
        }
        set
        {
            if (_mapScreenshotPath != value)
            {
                _mapScreenshotPath = value;
                SaveSetting(KeyMapScreenshotPath, value ?? "");
            }
        }
    }

    /// <summary>
    /// Add a quest to hidden list
    /// </summary>
    public void AddHiddenQuest(string questId)
    {
        var hidden = MapHiddenQuests;
        if (hidden.Add(questId))
        {
            MapHiddenQuests = hidden;
        }
    }

    /// <summary>
    /// Remove a quest from hidden list
    /// </summary>
    public void RemoveHiddenQuest(string questId)
    {
        var hidden = MapHiddenQuests;
        if (hidden.Remove(questId))
        {
            MapHiddenQuests = hidden;
        }
    }

    /// <summary>
    /// Clear all hidden quests
    /// </summary>
    public void ClearHiddenQuests()
    {
        MapHiddenQuests = new HashSet<string>();
    }

    /// <summary>
    /// Toggle quest collapsed state
    /// </summary>
    public void ToggleQuestCollapsed(string questId)
    {
        var collapsed = MapCollapsedQuests;
        if (collapsed.Contains(questId))
            collapsed.Remove(questId);
        else
            collapsed.Add(questId);
        MapCollapsedQuests = collapsed;
    }

    #endregion

    /// <summary>
    /// Auto-detect Tarkov log folder from game installation
    /// </summary>
    public string? AutoDetectLogFolder()
    {
        string? gameFolder;

        // 1. Try BSG Launcher registry
        gameFolder = TryDetectFromBsgLauncher();
        if (gameFolder != null)
        {
            var logsPath = GetLogsPathFromGameFolder(gameFolder);
            if (logsPath != null)
            {
                _detectionMethod = "BSG Launcher";
                return logsPath;
            }
        }

        // 2. Try Steam installation
        gameFolder = TryDetectFromSteam();
        if (gameFolder != null)
        {
            var logsPath = GetLogsPathFromGameFolder(gameFolder);
            if (logsPath != null)
            {
                _detectionMethod = "Steam";
                return logsPath;
            }
        }

        // 3. Try default installation paths
        gameFolder = TryDetectFromDefaultPaths();
        if (gameFolder != null)
        {
            var logsPath = GetLogsPathFromGameFolder(gameFolder);
            if (logsPath != null)
            {
                _detectionMethod = "Default Path";
                return logsPath;
            }
        }

        _detectionMethod = null;
        return null;
    }

    private string? GetLogsPathFromGameFolder(string gameFolder)
    {
        var steamLogsPath = Path.Combine(gameFolder, "build", "Logs");
        if (Directory.Exists(steamLogsPath))
            return steamLogsPath;

        var bsgLogsPath = Path.Combine(gameFolder, "Logs");
        if (Directory.Exists(bsgLogsPath))
            return bsgLogsPath;

        var buildFolder = Path.Combine(gameFolder, "build");
        if (Directory.Exists(buildFolder))
            return steamLogsPath;

        if (gameFolder.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ||
            gameFolder.Contains("Steam", StringComparison.OrdinalIgnoreCase))
            return steamLogsPath;

        return bsgLogsPath;
    }

    private string? TryDetectFromBsgLauncher()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov");
            var installPath = key?.GetValue("InstallLocation")?.ToString();
            if (!string.IsNullOrEmpty(installPath) && IsValidTarkovFolder(installPath))
                return installPath;

            using var userKey = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Battlestate Games\EscapeFromTarkov");
            var userPath = userKey?.GetValue("InstallLocation")?.ToString();
            if (!string.IsNullOrEmpty(userPath) && IsValidTarkovFolder(userPath))
                return userPath;
        }
        catch
        {
            // Registry access failed
        }

        return null;
    }

    private string? TryDetectFromSteam()
    {
        try
        {
            string? steamPath = null;

            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam"))
            {
                steamPath = key?.GetValue("SteamPath")?.ToString();
            }

            if (string.IsNullOrEmpty(steamPath))
            {
                var defaultSteamPath = @"C:\Program Files (x86)\Steam";
                if (Directory.Exists(defaultSteamPath))
                    steamPath = defaultSteamPath;
            }

            if (string.IsNullOrEmpty(steamPath))
                return null;

            steamPath = steamPath.Replace("/", "\\");

            var libraryFolders = GetSteamLibraryFolders(steamPath);
            string[] possibleFolderNames = ["Escape from Tarkov", "EscapeFromTarkov"];

            foreach (var libraryFolder in libraryFolders)
            {
                foreach (var folderName in possibleFolderNames)
                {
                    var tarkovPath = Path.Combine(libraryFolder, "steamapps", "common", folderName);
                    if (IsValidTarkovFolder(tarkovPath))
                        return tarkovPath;
                }
            }
        }
        catch
        {
            // Steam detection failed
        }

        return null;
    }

    private List<string> GetSteamLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        try
        {
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
                return folders;

            var content = File.ReadAllText(vdfPath);
            var pathRegex = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            var matches = pathRegex.Matches(content);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                        folders.Add(path);
                }
            }
        }
        catch
        {
            // VDF parsing failed
        }

        return folders;
    }

    private string? TryDetectFromDefaultPaths()
    {
        string[] defaultPaths =
        [
            @"C:\Battlestate Games\EFT",
            @"C:\Battlestate Games\Escape from Tarkov",
            @"D:\Battlestate Games\EFT",
            @"D:\Battlestate Games\Escape from Tarkov",
            @"E:\Battlestate Games\EFT",
            @"E:\Battlestate Games\Escape from Tarkov",
            @"C:\Games\EFT",
            @"D:\Games\EFT",
            @"C:\Program Files\Battlestate Games\EFT",
            @"C:\Program Files (x86)\Battlestate Games\EFT"
        ];

        foreach (var path in defaultPaths)
        {
            if (IsValidTarkovFolder(path))
                return path;
        }

        return null;
    }

    public bool IsValidTarkovFolder(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return false;

        var exePath = Path.Combine(folderPath, "EscapeFromTarkov.exe");
        var bsgLogsPath = Path.Combine(folderPath, "Logs");
        var steamBuildPath = Path.Combine(folderPath, "build");
        var steamLogsPath = Path.Combine(folderPath, "build", "Logs");
        var steamExePath = Path.Combine(folderPath, "build", "EscapeFromTarkov.exe");

        return File.Exists(exePath) ||
               File.Exists(steamExePath) ||
               Directory.Exists(bsgLogsPath) ||
               Directory.Exists(steamLogsPath) ||
               Directory.Exists(steamBuildPath);
    }

    private void SaveSetting(string key, string value)
    {
        try
        {
            _userDataDb.SetSetting(key, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Save failed: {ex.Message}");
        }
    }

    private void LoadSettings()
    {
        _settingsLoaded = true;

        try
        {
            // First check if JSON migration is needed
            MigrateFromJsonIfNeeded();

            // Load from DB
            _logFolderPath = _userDataDb.GetSetting(KeyLogFolderPath);
            if (string.IsNullOrEmpty(_logFolderPath)) _logFolderPath = null;

            if (int.TryParse(_userDataDb.GetSetting(KeyPlayerLevel), out var level))
                _playerLevel = level;

            if (double.TryParse(_userDataDb.GetSetting(KeyScavRep), out var scavRep))
                _scavRep = scavRep;

            if (bool.TryParse(_userDataDb.GetSetting(KeyShowLevelLockedQuests), out var showLocked))
                _showLevelLockedQuests = showLocked;

            if (bool.TryParse(_userDataDb.GetSetting(KeyHideWipeWarning), out var hideWarning))
                _hideWipeWarning = hideWarning;

            if (int.TryParse(_userDataDb.GetSetting(KeySyncDaysRange), out var syncDays))
                _syncDaysRange = syncDays;

            if (double.TryParse(_userDataDb.GetSetting(KeyBaseFontSize), out var fontSize))
                _baseFontSize = fontSize;

            if (int.TryParse(_userDataDb.GetSetting(KeyDspDecodeCount), out var dspCount))
                _dspDecodeCount = dspCount;

            _playerFaction = _userDataDb.GetSetting(KeyPlayerFaction);
            if (string.IsNullOrEmpty(_playerFaction)) _playerFaction = null;

            if (bool.TryParse(_userDataDb.GetSetting(KeyHasEodEdition), out var hasEod))
                _hasEodEdition = hasEod;

            if (bool.TryParse(_userDataDb.GetSetting(KeyHasUnheardEdition), out var hasUnheard))
                _hasUnheardEdition = hasUnheard;

            if (int.TryParse(_userDataDb.GetSetting(KeyPrestigeLevel), out var prestige))
                _prestigeLevel = prestige;

            // Load Map settings
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapDrawerOpen), out var drawerOpen))
                _mapDrawerOpen = drawerOpen;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapDrawerWidth), out var drawerWidth))
                _mapDrawerWidth = drawerWidth;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowExtracts), out var showExtracts))
                _mapShowExtracts = showExtracts;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowPmcExtracts), out var showPmcExtracts))
                _mapShowPmcExtracts = showPmcExtracts;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowScavExtracts), out var showScavExtracts))
                _mapShowScavExtracts = showScavExtracts;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowTransits), out var showTransits))
                _mapShowTransits = showTransits;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowQuests), out var showQuests))
                _mapShowQuests = showQuests;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapIncompleteOnly), out var incompleteOnly))
                _mapIncompleteOnly = incompleteOnly;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapCurrentMapOnly), out var currentMapOnly))
                _mapCurrentMapOnly = currentMapOnly;

            _mapSortOption = _userDataDb.GetSetting(KeyMapSortOption);
            if (string.IsNullOrEmpty(_mapSortOption)) _mapSortOption = "name";

            // Load hidden quests (JSON array)
            var hiddenJson = _userDataDb.GetSetting(KeyMapHiddenQuests);
            if (!string.IsNullOrEmpty(hiddenJson))
            {
                try
                {
                    var hiddenArray = JsonSerializer.Deserialize<string[]>(hiddenJson);
                    _mapHiddenQuests = hiddenArray != null ? new HashSet<string>(hiddenArray) : new HashSet<string>();
                }
                catch
                {
                    _mapHiddenQuests = new HashSet<string>();
                }
            }

            // Load collapsed quests (JSON array)
            var collapsedJson = _userDataDb.GetSetting(KeyMapCollapsedQuests);
            if (!string.IsNullOrEmpty(collapsedJson))
            {
                try
                {
                    var collapsedArray = JsonSerializer.Deserialize<string[]>(collapsedJson);
                    _mapCollapsedQuests = collapsedArray != null ? new HashSet<string>(collapsedArray) : new HashSet<string>();
                }
                catch
                {
                    _mapCollapsedQuests = new HashSet<string>();
                }
            }

            _mapLastSelectedMap = _userDataDb.GetSetting(KeyMapLastSelectedMap);
            if (string.IsNullOrEmpty(_mapLastSelectedMap)) _mapLastSelectedMap = null;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapMarkerScale), out var markerScale))
                _mapMarkerScale = markerScale;

            // Load new map settings
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowTrail), out var showTrail))
                _mapShowTrail = showTrail;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowMinimap), out var showMinimap))
                _mapShowMinimap = showMinimap;

            _mapMinimapSize = _userDataDb.GetSetting(KeyMapMinimapSize);
            if (string.IsNullOrEmpty(_mapMinimapSize)) _mapMinimapSize = "M";

            if (double.TryParse(_userDataDb.GetSetting(KeyMapMarkerOpacity), out var markerOpacity))
                _mapMarkerOpacity = markerOpacity;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapAutoHideCompleted), out var autoHideCompleted))
                _mapAutoHideCompleted = autoHideCompleted;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapFadeCompleted), out var fadeCompleted))
                _mapFadeCompleted = fadeCompleted;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowLabels), out var showLabels))
                _mapShowLabels = showLabels;

            _mapTrailColor = _userDataDb.GetSetting(KeyMapTrailColor);
            if (string.IsNullOrEmpty(_mapTrailColor)) _mapTrailColor = "#00FF00";

            if (double.TryParse(_userDataDb.GetSetting(KeyMapTrailThickness), out var trailThickness))
                _mapTrailThickness = trailThickness;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapAutoStartTracking), out var autoStartTracking))
                _mapAutoStartTracking = autoStartTracking;

            // Load clustering and layer settings
            if (bool.TryParse(_userDataDb.GetSetting(KeyMapClusteringEnabled), out var clusteringEnabled))
                _mapClusteringEnabled = clusteringEnabled;

            if (double.TryParse(_userDataDb.GetSetting(KeyMapClusterZoomThreshold), out var clusterZoomThreshold))
                _mapClusterZoomThreshold = clusterZoomThreshold;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapAutoFloorEnabled), out var autoFloorEnabled))
                _mapAutoFloorEnabled = autoFloorEnabled;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowBosses), out var showBosses))
                _mapShowBosses = showBosses;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowSpawns), out var showSpawns))
                _mapShowSpawns = showSpawns;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowLevers), out var showLevers))
                _mapShowLevers = showLevers;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowKeys), out var showKeys))
                _mapShowKeys = showKeys;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapLeftPanelExpanded), out var leftPanelExpanded))
                _mapLeftPanelExpanded = leftPanelExpanded;

            // Load Expander states
            if (bool.TryParse(_userDataDb.GetSetting(KeyExpanderLayersExpanded), out var expanderLayersExpanded))
                _expanderLayersExpanded = expanderLayersExpanded;

            if (bool.TryParse(_userDataDb.GetSetting(KeyExpanderFloorExpanded), out var expanderFloorExpanded))
                _expanderFloorExpanded = expanderFloorExpanded;

            if (bool.TryParse(_userDataDb.GetSetting(KeyExpanderMapInfoExpanded), out var expanderMapInfoExpanded))
                _expanderMapInfoExpanded = expanderMapInfoExpanded;

            if (bool.TryParse(_userDataDb.GetSetting(KeyQuestPanelVisible), out var questPanelVisible))
                _questPanelVisible = questPanelVisible;

            _mapScreenshotPath = _userDataDb.GetSetting(KeyMapScreenshotPath);
            if (string.IsNullOrEmpty(_mapScreenshotPath)) _mapScreenshotPath = null;

            // Load quest display settings
            if (double.TryParse(_userDataDb.GetSetting(KeyMapLabelScale), out var labelScale))
                _mapLabelScale = labelScale;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapQuestStatusColors), out var questStatusColors))
                _mapQuestStatusColors = questStatusColors;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapHideCompletedQuests), out var hideCompletedQuests))
                _mapHideCompletedQuests = hideCompletedQuests;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowActiveOnly), out var showActiveOnly))
                _mapShowActiveOnly = showActiveOnly;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapHideCompletedObjectives), out var hideCompletedObjectives))
                _mapHideCompletedObjectives = hideCompletedObjectives;

            if (bool.TryParse(_userDataDb.GetSetting(KeyMapShowKappaHighlight), out var showKappaHighlight))
                _mapShowKappaHighlight = showKappaHighlight;

            _mapTraderFilter = _userDataDb.GetSetting(KeyMapTraderFilter);
            if (string.IsNullOrEmpty(_mapTraderFilter)) _mapTraderFilter = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Migrate from legacy app_settings.json if it exists
    /// </summary>
    private void MigrateFromJsonIfNeeded()
    {
        var jsonPath = Path.Combine(AppEnv.ConfigPath, "app_settings.json");
        if (!File.Exists(jsonPath)) return;

        try
        {
            var json = File.ReadAllText(jsonPath);
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var settings = JsonSerializer.Deserialize<LegacyAppSettings>(json, options);

            if (settings != null)
            {
                if (!string.IsNullOrEmpty(settings.LogFolderPath))
                    _userDataDb.SetSetting(KeyLogFolderPath, settings.LogFolderPath);

                if (settings.PlayerLevel.HasValue)
                    _userDataDb.SetSetting(KeyPlayerLevel, settings.PlayerLevel.Value.ToString());

                if (settings.ScavRep.HasValue)
                    _userDataDb.SetSetting(KeyScavRep, settings.ScavRep.Value.ToString());

                if (settings.ShowLevelLockedQuests.HasValue)
                    _userDataDb.SetSetting(KeyShowLevelLockedQuests, settings.ShowLevelLockedQuests.Value.ToString());

                if (settings.HideWipeWarning.HasValue)
                    _userDataDb.SetSetting(KeyHideWipeWarning, settings.HideWipeWarning.Value.ToString());

                if (settings.SyncDaysRange.HasValue)
                    _userDataDb.SetSetting(KeySyncDaysRange, settings.SyncDaysRange.Value.ToString());

                if (settings.BaseFontSize.HasValue)
                    _userDataDb.SetSetting(KeyBaseFontSize, settings.BaseFontSize.Value.ToString());

                if (settings.DspDecodeCount.HasValue)
                    _userDataDb.SetSetting(KeyDspDecodeCount, settings.DspDecodeCount.Value.ToString());

                if (!string.IsNullOrEmpty(settings.PlayerFaction))
                    _userDataDb.SetSetting(KeyPlayerFaction, settings.PlayerFaction);
            }

            // Delete the JSON file after migration
            File.Delete(jsonPath);
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Migrated and deleted: {jsonPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SettingsService] Migration failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Reset log folder setting (use auto-detection)
    /// </summary>
    public void ResetLogFolderPath()
    {
        _logFolderPath = null;
        SaveSetting(KeyLogFolderPath, "");
        LogFolderChanged?.Invoke(this, AutoDetectLogFolder());
    }

    private class LegacyAppSettings
    {
        public string? LogFolderPath { get; set; }
        public int? PlayerLevel { get; set; }
        public double? ScavRep { get; set; }
        public bool? ShowLevelLockedQuests { get; set; }
        public bool? HideWipeWarning { get; set; }
        public int? SyncDaysRange { get; set; }
        public double? BaseFontSize { get; set; }
        public int? DspDecodeCount { get; set; }
        public string? PlayerFaction { get; set; }
    }
}
