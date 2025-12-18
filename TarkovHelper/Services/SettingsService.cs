using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TarkovHelper.Debug;
using TarkovHelper.Services.Logging;
using TarkovHelper.Services.Settings;

namespace TarkovHelper.Services;

/// <summary>
/// Application settings service for managing user preferences
/// Settings are stored in user_data.db (UserSettings table)
/// </summary>
public class SettingsService
{
    private static readonly ILogger _log = Log.For<SettingsService>();
    private static SettingsService? _instance;
    public static SettingsService Instance => _instance ??= new SettingsService();

    private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;

    // Setting keys
    private const string KeyLogFolderPath = "app.logFolderPath";
    private const string KeyLogMonitoringEnabled = "app.logMonitoringEnabled";

    // Logging settings keys
    private const string KeyLoggingLevel = "logging.level";
    private const string KeyLoggingMaxDays = "logging.maxDays";
    private const string KeyLoggingMaxSizeMB = "logging.maxSizeMB";
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

    // Map settings keys moved to MapSettings service

    private bool _settingsLoaded;
    private string? _detectionMethod;

    // Cached values
    private string? _logFolderPath;
    private bool? _logMonitoringEnabled;
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

    // Map cached values moved to MapSettings service

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
    /// Whether log monitoring is enabled (auto-start on app launch)
    /// </summary>
    public bool LogMonitoringEnabled
    {
        get
        {
            if (!_settingsLoaded) LoadSettings();
            return _logMonitoringEnabled ?? true;  // Default: enabled
        }
        set
        {
            if (_logMonitoringEnabled != value)
            {
                _logMonitoringEnabled = value;
                SaveSetting(KeyLogMonitoringEnabled, value.ToString());
            }
        }
    }

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

    #region Map Settings (Facade - delegates to MapSettings)

    // Map settings are now managed by MapSettings service.
    // These properties delegate to MapSettings.Instance for backward compatibility.

    private MapSettings Map => MapSettings.Instance;

    public const double MinMarkerScale = MapSettings.MinMarkerScale;
    public const double MaxMarkerScale = MapSettings.MaxMarkerScale;
    public const double DefaultMarkerScale = MapSettings.DefaultMarkerScale;
    public const double DefaultDrawerWidth = MapSettings.DefaultDrawerWidth;

    public bool MapDrawerOpen { get => Map.DrawerOpen; set => Map.DrawerOpen = value; }
    public double MapDrawerWidth { get => Map.DrawerWidth; set => Map.DrawerWidth = value; }
    public bool MapShowExtracts { get => Map.ShowExtracts; set => Map.ShowExtracts = value; }
    public bool MapShowPmcExtracts { get => Map.ShowPmcExtracts; set => Map.ShowPmcExtracts = value; }
    public bool MapShowScavExtracts { get => Map.ShowScavExtracts; set => Map.ShowScavExtracts = value; }
    public bool MapShowTransits { get => Map.ShowTransits; set => Map.ShowTransits = value; }
    public bool MapShowQuests { get => Map.ShowQuests; set => Map.ShowQuests = value; }
    public bool MapIncompleteOnly { get => Map.IncompleteOnly; set => Map.IncompleteOnly = value; }
    public bool MapCurrentMapOnly { get => Map.CurrentMapOnly; set => Map.CurrentMapOnly = value; }
    public string MapSortOption { get => Map.SortOption; set => Map.SortOption = value; }
    public HashSet<string> MapHiddenQuests { get => Map.HiddenQuests; set => Map.HiddenQuests = value; }
    public HashSet<string> MapCollapsedQuests { get => Map.CollapsedQuests; set => Map.CollapsedQuests = value; }
    public string? MapLastSelectedMap { get => Map.LastSelectedMap; set => Map.LastSelectedMap = value; }
    public double MapMarkerScale { get => Map.MarkerScale; set => Map.MarkerScale = value; }
    public bool MapShowTrail { get => Map.ShowTrail; set => Map.ShowTrail = value; }
    public bool MapShowMinimap { get => Map.ShowMinimap; set => Map.ShowMinimap = value; }
    public string MapMinimapSize { get => Map.MinimapSize; set => Map.MinimapSize = value; }
    public double MapMarkerOpacity { get => Map.MarkerOpacity; set => Map.MarkerOpacity = value; }
    public bool MapAutoHideCompleted { get => Map.AutoHideCompleted; set => Map.AutoHideCompleted = value; }
    public bool MapFadeCompleted { get => Map.FadeCompleted; set => Map.FadeCompleted = value; }
    public bool MapShowLabels { get => Map.ShowLabels; set => Map.ShowLabels = value; }
    public double MapLabelScale { get => Map.LabelScale; set => Map.LabelScale = value; }
    public bool MapQuestStatusColors { get => Map.QuestStatusColors; set => Map.QuestStatusColors = value; }
    public bool MapHideCompletedQuests { get => Map.HideCompletedQuests; set => Map.HideCompletedQuests = value; }
    public bool MapShowActiveOnly { get => Map.ShowActiveOnly; set => Map.ShowActiveOnly = value; }
    public bool MapHideCompletedObjectives { get => Map.HideCompletedObjectives; set => Map.HideCompletedObjectives = value; }
    public int MapQuestMarkerStyle { get => Map.QuestMarkerStyle; set => Map.QuestMarkerStyle = value; }
    public bool MapShowKappaHighlight { get => Map.ShowKappaHighlight; set => Map.ShowKappaHighlight = value; }
    public string MapTraderFilter { get => Map.TraderFilter; set => Map.TraderFilter = value; }
    public string MapTrailColor { get => Map.TrailColor; set => Map.TrailColor = value; }
    public double MapTrailThickness { get => Map.TrailThickness; set => Map.TrailThickness = value; }
    public bool MapAutoStartTracking { get => Map.AutoStartTracking; set => Map.AutoStartTracking = value; }
    public bool MapClusteringEnabled { get => Map.ClusteringEnabled; set => Map.ClusteringEnabled = value; }
    public double MapClusterZoomThreshold { get => Map.ClusterZoomThreshold; set => Map.ClusterZoomThreshold = value; }
    public bool MapAutoFloorEnabled { get => Map.AutoFloorEnabled; set => Map.AutoFloorEnabled = value; }
    public bool MapShowBosses { get => Map.ShowBosses; set => Map.ShowBosses = value; }
    public bool MapShowSpawns { get => Map.ShowSpawns; set => Map.ShowSpawns = value; }
    public bool MapShowLevers { get => Map.ShowLevers; set => Map.ShowLevers = value; }
    public bool MapShowKeys { get => Map.ShowKeys; set => Map.ShowKeys = value; }
    public bool LeftPanelExpanded { get => Map.LeftPanelExpanded; set => Map.LeftPanelExpanded = value; }
    public bool ExpanderLayersExpanded { get => Map.ExpanderLayersExpanded; set => Map.ExpanderLayersExpanded = value; }
    public bool ExpanderFloorExpanded { get => Map.ExpanderFloorExpanded; set => Map.ExpanderFloorExpanded = value; }
    public bool ExpanderMapInfoExpanded { get => Map.ExpanderMapInfoExpanded; set => Map.ExpanderMapInfoExpanded = value; }
    public bool QuestPanelVisible { get => Map.QuestPanelVisible; set => Map.QuestPanelVisible = value; }
    public string? MapScreenshotPath { get => Map.ScreenshotPath; set => Map.ScreenshotPath = value; }
    public int MapQuestMarkerSize { get => Map.QuestMarkerSize; set => Map.QuestMarkerSize = value; }
    public int MapPlayerMarkerSize { get => Map.PlayerMarkerSize; set => Map.PlayerMarkerSize = value; }
    public double MapExtractNameSize { get => Map.ExtractNameSize; set => Map.ExtractNameSize = value; }
    public double MapQuestNameSize { get => Map.QuestNameSize; set => Map.QuestNameSize = value; }
    public double MapLastZoomLevel { get => Map.LastZoomLevel; set => Map.LastZoomLevel = value; }
    public double MapLastTranslateX { get => Map.LastTranslateX; set => Map.LastTranslateX = value; }
    public double MapLastTranslateY { get => Map.LastTranslateY; set => Map.LastTranslateY = value; }

    public void AddHiddenQuest(string questId) => Map.AddHiddenQuest(questId);
    public void RemoveHiddenQuest(string questId) => Map.RemoveHiddenQuest(questId);
    public void ClearHiddenQuests() => Map.ClearHiddenQuests();
    public void ToggleQuestCollapsed(string questId) => Map.ToggleQuestCollapsed(questId);

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
            _log.Error($"Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Generic getter for any setting key
    /// </summary>
    public string GetValue(string key, string defaultValue = "")
    {
        try
        {
            var value = _userDataDb.GetSetting(key);
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Generic setter for any setting key
    /// </summary>
    public void SetValue(string key, string value)
    {
        SaveSetting(key, value);
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

            if (bool.TryParse(_userDataDb.GetSetting(KeyLogMonitoringEnabled), out var logMonitoring))
                _logMonitoringEnabled = logMonitoring;

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

            // Map settings are now loaded by MapSettings service
        }
        catch (Exception ex)
        {
            _log.Error($"Load failed: {ex.Message}");
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
            _log.Info($"Migrated and deleted: {jsonPath}");
        }
        catch (Exception ex)
        {
            _log.Error($"Migration failed: {ex.Message}");
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
