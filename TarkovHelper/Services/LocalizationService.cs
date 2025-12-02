using System.ComponentModel;
using System.IO;
using System.Text.Json;

namespace TarkovHelper.Services;

/// <summary>
/// Supported languages
/// </summary>
public enum AppLanguage
{
    EN,
    KO,
    JA
}

/// <summary>
/// Centralized localization service for managing UI language
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static string DataDirectory => Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Data"
    );

    private static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    private AppLanguage _currentLanguage = AppLanguage.EN;

    public LocalizationService()
    {
        LoadSettings();
    }

    public AppLanguage CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnPropertyChanged(nameof(CurrentLanguage));
                LanguageChanged?.Invoke(this, value);
                SaveSettings();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<AppLanguage>? LanguageChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #region Settings Persistence

    private void SaveSettings()
    {
        try
        {
            if (!Directory.Exists(DataDirectory))
            {
                Directory.CreateDirectory(DataDirectory);
            }

            var settings = new AppSettings { Language = _currentLanguage.ToString() };
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore save failures
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null && Enum.TryParse<AppLanguage>(settings.Language, out var lang))
                {
                    _currentLanguage = lang;
                }
            }
        }
        catch
        {
            // Use default (EN) on load failure
            _currentLanguage = AppLanguage.EN;
        }
    }

    private class AppSettings
    {
        public string Language { get; set; } = "EN";
    }

    #endregion

    #region UI Strings

    public string Welcome => CurrentLanguage switch
    {
        AppLanguage.KO => "Tarkov Helper에 오신 것을 환영합니다",
        AppLanguage.JA => "Tarkov Helperへようこそ",
        _ => "Welcome to Tarkov Helper"
    };

    #endregion
}
