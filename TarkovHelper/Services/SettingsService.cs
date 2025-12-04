using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TarkovHelper.Debug;

namespace TarkovHelper.Services;

/// <summary>
/// Application settings service for managing user preferences
/// </summary>
public class SettingsService
{
    private static SettingsService? _instance;
    public static SettingsService Instance => _instance ??= new SettingsService();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string ConfigDirectory => AppEnv.ConfigPath;

    private static string SettingsPath => Path.Combine(ConfigDirectory, "app_settings.json");

    private AppSettingsData _settings = new();
    private bool _settingsLoaded;
    private string? _detectionMethod;

    public event EventHandler<string?>? LogFolderChanged;
    public event EventHandler<int>? PlayerLevelChanged;
    public event EventHandler<double>? ScavRepChanged;
    public event EventHandler<double>? BaseFontSizeChanged;
    public event EventHandler<int>? DspDecodeCountChanged;

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
    /// Player level for quest filtering
    /// </summary>
    public int PlayerLevel
    {
        get
        {
            if (!_settingsLoaded)
            {
                LoadSettings();
            }
            return _settings.PlayerLevel ?? DefaultPlayerLevel;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinPlayerLevel, MaxPlayerLevel);
            if (_settings.PlayerLevel != clampedValue)
            {
                _settings.PlayerLevel = clampedValue;
                SaveSettings();
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
            if (!_settingsLoaded)
            {
                LoadSettings();
            }
            return _settings.ShowLevelLockedQuests ?? true;
        }
        set
        {
            _settings.ShowLevelLockedQuests = value;
            SaveSettings();
        }
    }

    /// <summary>
    /// Scav reputation for quest filtering (Fence karma)
    /// </summary>
    public double ScavRep
    {
        get
        {
            if (!_settingsLoaded)
            {
                LoadSettings();
            }
            return _settings.ScavRep ?? DefaultScavRep;
        }
        set
        {
            var clampedValue = Math.Round(Math.Clamp(value, MinScavRep, MaxScavRep), 1);
            if (Math.Abs((_settings.ScavRep ?? DefaultScavRep) - clampedValue) > 0.01)
            {
                _settings.ScavRep = clampedValue;
                SaveSettings();
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
            if (!_settingsLoaded)
            {
                LoadSettings();
            }

            // If user has set a path, use it
            if (!string.IsNullOrEmpty(_settings.LogFolderPath))
            {
                return _settings.LogFolderPath;
            }

            // Otherwise try auto-detection
            return AutoDetectLogFolder();
        }
        set
        {
            _settings.LogFolderPath = value;
            SaveSettings();
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
            if (!_settingsLoaded)
            {
                LoadSettings();
            }
            return _settings.HideWipeWarning ?? false;
        }
        set
        {
            _settings.HideWipeWarning = value;
            SaveSettings();
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
            if (!_settingsLoaded)
            {
                LoadSettings();
            }
            return _settings.SyncDaysRange ?? 0; // Default: all logs
        }
        set
        {
            var clampedValue = Math.Clamp(value, 0, 30);
            if (_settings.SyncDaysRange != clampedValue)
            {
                _settings.SyncDaysRange = clampedValue;
                SaveSettings();
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
            if (!_settingsLoaded)
            {
                LoadSettings();
            }
            return _settings.BaseFontSize ?? DefaultBaseFontSize;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinFontSize, MaxFontSize);
            if (Math.Abs((_settings.BaseFontSize ?? DefaultBaseFontSize) - clampedValue) > 0.01)
            {
                _settings.BaseFontSize = clampedValue;
                SaveSettings();
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
            if (!_settingsLoaded)
            {
                LoadSettings();
            }
            return _settings.DspDecodeCount ?? DefaultDspDecodeCount;
        }
        set
        {
            var clampedValue = Math.Clamp(value, MinDspDecodeCount, MaxDspDecodeCount);
            if (_settings.DspDecodeCount != clampedValue)
            {
                _settings.DspDecodeCount = clampedValue;
                SaveSettings();
                DspDecodeCountChanged?.Invoke(this, clampedValue);
            }
        }
    }

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

    /// <summary>
    /// Get logs folder path from game folder
    /// </summary>
    private string? GetLogsPathFromGameFolder(string gameFolder)
    {
        // Steam version: build/Logs
        var steamLogsPath = Path.Combine(gameFolder, "build", "Logs");
        if (Directory.Exists(steamLogsPath))
        {
            return steamLogsPath;
        }

        // BSG Launcher version: Logs
        var bsgLogsPath = Path.Combine(gameFolder, "Logs");
        if (Directory.Exists(bsgLogsPath))
        {
            return bsgLogsPath;
        }

        // Check if it's Steam version (has build folder)
        var buildFolder = Path.Combine(gameFolder, "build");
        if (Directory.Exists(buildFolder))
        {
            return steamLogsPath;
        }

        // Check if path contains Steam indicators
        if (gameFolder.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ||
            gameFolder.Contains("Steam", StringComparison.OrdinalIgnoreCase))
        {
            return steamLogsPath;
        }

        return bsgLogsPath;
    }

    /// <summary>
    /// Detect from BSG Launcher registry
    /// </summary>
    private string? TryDetectFromBsgLauncher()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov");
            var installPath = key?.GetValue("InstallLocation")?.ToString();
            if (!string.IsNullOrEmpty(installPath) && IsValidTarkovFolder(installPath))
            {
                return installPath;
            }

            using var userKey = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Battlestate Games\EscapeFromTarkov");
            var userPath = userKey?.GetValue("InstallLocation")?.ToString();
            if (!string.IsNullOrEmpty(userPath) && IsValidTarkovFolder(userPath))
            {
                return userPath;
            }
        }
        catch
        {
            // Registry access failed
        }

        return null;
    }

    /// <summary>
    /// Detect from Steam installation
    /// </summary>
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
                {
                    steamPath = defaultSteamPath;
                }
            }

            if (string.IsNullOrEmpty(steamPath))
            {
                return null;
            }

            steamPath = steamPath.Replace("/", "\\");

            var libraryFolders = GetSteamLibraryFolders(steamPath);
            string[] possibleFolderNames = ["Escape from Tarkov", "EscapeFromTarkov"];

            foreach (var libraryFolder in libraryFolders)
            {
                foreach (var folderName in possibleFolderNames)
                {
                    var tarkovPath = Path.Combine(libraryFolder, "steamapps", "common", folderName);
                    if (IsValidTarkovFolder(tarkovPath))
                    {
                        return tarkovPath;
                    }
                }
            }
        }
        catch
        {
            // Steam detection failed
        }

        return null;
    }

    /// <summary>
    /// Get all Steam library folders
    /// </summary>
    private List<string> GetSteamLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        try
        {
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath))
            {
                return folders;
            }

            var content = File.ReadAllText(vdfPath);
            var pathRegex = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            var matches = pathRegex.Matches(content);

            foreach (Match match in matches)
            {
                if (match.Groups.Count > 1)
                {
                    var path = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(path);
                    }
                }
            }
        }
        catch
        {
            // VDF parsing failed
        }

        return folders;
    }

    /// <summary>
    /// Try common default installation paths
    /// </summary>
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
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Check if a folder is a valid Tarkov installation
    /// </summary>
    public bool IsValidTarkovFolder(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

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

    /// <summary>
    /// Save settings to file
    /// </summary>
    public void SaveSettings()
    {
        try
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }

            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Save failed
        }
    }

    /// <summary>
    /// Load settings from file
    /// </summary>
    private void LoadSettings()
    {
        _settingsLoaded = true;

        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettingsData>(json, JsonOptions);
                if (settings != null)
                {
                    _settings = settings;
                }
            }
        }
        catch
        {
            // Use defaults on load failure
        }
    }

    /// <summary>
    /// Reset log folder setting (use auto-detection)
    /// </summary>
    public void ResetLogFolderPath()
    {
        _settings.LogFolderPath = null;
        SaveSettings();
        LogFolderChanged?.Invoke(this, AutoDetectLogFolder());
    }

    private class AppSettingsData
    {
        public string? LogFolderPath { get; set; }
        public int? PlayerLevel { get; set; }
        public double? ScavRep { get; set; }
        public bool? ShowLevelLockedQuests { get; set; }
        public bool? HideWipeWarning { get; set; }
        public int? SyncDaysRange { get; set; }
        public double? BaseFontSize { get; set; }
        public int? DspDecodeCount { get; set; }
    }
}
