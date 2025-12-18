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
/// Core functionality for LocalizationService - settings persistence and common UI strings.
/// This is a partial class; other parts contain domain-specific strings.
/// </summary>
public partial class LocalizationService : INotifyPropertyChanged
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private readonly UserDataDbService _userDataDb = UserDataDbService.Instance;
    private const string KeyLanguage = "app.language";

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
            _userDataDb.SetSetting(KeyLanguage, _currentLanguage.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Save failed: {ex.Message}");
        }
    }

    private void LoadSettings()
    {
        try
        {
            // First check if JSON migration is needed
            MigrateFromJsonIfNeeded();

            // Load from DB
            var langStr = _userDataDb.GetSetting(KeyLanguage);
            if (!string.IsNullOrEmpty(langStr) && Enum.TryParse<AppLanguage>(langStr, out var lang))
            {
                _currentLanguage = lang;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Load failed: {ex.Message}");
            _currentLanguage = AppLanguage.EN;
        }
    }

    /// <summary>
    /// Migrate from legacy settings.json if it exists
    /// </summary>
    private void MigrateFromJsonIfNeeded()
    {
        // Check old Data/settings.json path
        var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        var jsonPath = Path.Combine(dataDir, "settings.json");

        if (!File.Exists(jsonPath)) return;

        try
        {
            var json = File.ReadAllText(jsonPath);
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var settings = JsonSerializer.Deserialize<LegacySettings>(json, options);

            if (settings != null && Enum.TryParse<AppLanguage>(settings.Language, out var lang))
            {
                _userDataDb.SetSetting(KeyLanguage, lang.ToString());
            }

            // Delete the JSON file after migration
            File.Delete(jsonPath);
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Migrated and deleted: {jsonPath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LocalizationService] Migration failed: {ex.Message}");
        }
    }

    private class LegacySettings
    {
        public string Language { get; set; } = "EN";
    }

    #endregion

    #region Common UI Strings

    public string Welcome => CurrentLanguage switch
    {
        AppLanguage.KO => "Tarkov Helper에 오신 것을 환영합니다",
        AppLanguage.JA => "Tarkov Helperへようこそ",
        _ => "Welcome to Tarkov Helper"
    };

    public string Cancel => CurrentLanguage switch
    {
        AppLanguage.KO => "취소",
        AppLanguage.JA => "キャンセル",
        _ => "Cancel"
    };

    public string Apply => CurrentLanguage switch
    {
        AppLanguage.KO => "적용",
        AppLanguage.JA => "適用",
        _ => "Apply"
    };

    public string Close => CurrentLanguage switch
    {
        AppLanguage.KO => "닫기",
        AppLanguage.JA => "閉じる",
        _ => "Close"
    };

    public string Settings => CurrentLanguage switch
    {
        AppLanguage.KO => "설정",
        AppLanguage.JA => "設定",
        _ => "Settings"
    };

    public string Browse => CurrentLanguage switch
    {
        AppLanguage.KO => "찾아보기",
        AppLanguage.JA => "参照",
        _ => "Browse"
    };

    public string Open => CurrentLanguage switch
    {
        AppLanguage.KO => "열기",
        AppLanguage.JA => "開く",
        _ => "Open"
    };

    public string Start => CurrentLanguage switch
    {
        AppLanguage.KO => "시작",
        AppLanguage.JA => "開始",
        _ => "Start"
    };

    public string Stop => CurrentLanguage switch
    {
        AppLanguage.KO => "중지",
        AppLanguage.JA => "停止",
        _ => "Stop"
    };

    public string Reset => CurrentLanguage switch
    {
        AppLanguage.KO => "리셋",
        AppLanguage.JA => "リセット",
        _ => "Reset"
    };

    public string ResetAll => CurrentLanguage switch
    {
        AppLanguage.KO => "초기화",
        AppLanguage.JA => "リセット",
        _ => "Reset"
    };

    public string SelectAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 선택",
        AppLanguage.JA => "すべて選択",
        _ => "Select All"
    };

    public string DeselectAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 해제",
        AppLanguage.JA => "すべて解除",
        _ => "Deselect All"
    };

    public string ShowAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 표시",
        AppLanguage.JA => "すべて表示",
        _ => "Show All"
    };

    public string HideAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 숨기기",
        AppLanguage.JA => "すべて非表示",
        _ => "Hide All"
    };

    public string ExpandAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 펼치기",
        AppLanguage.JA => "すべて展開",
        _ => "Expand All"
    };

    public string CollapseAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 접기",
        AppLanguage.JA => "すべて折りたたむ",
        _ => "Collapse All"
    };

    public string ShowMore => CurrentLanguage switch
    {
        AppLanguage.KO => "더 보기",
        AppLanguage.JA => "もっと見る",
        _ => "Show More"
    };

    public string ShowLess => CurrentLanguage switch
    {
        AppLanguage.KO => "접기",
        AppLanguage.JA => "閉じる",
        _ => "Show Less"
    };

    public string FilterAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체",
        AppLanguage.JA => "全て",
        _ => "All"
    };

    public string Folder => CurrentLanguage switch
    {
        AppLanguage.KO => "폴더",
        AppLanguage.JA => "フォルダ",
        _ => "Folder"
    };

    public string Waiting => CurrentLanguage switch
    {
        AppLanguage.KO => "대기 중",
        AppLanguage.JA => "待機中",
        _ => "Waiting"
    };

    public string Tracking => CurrentLanguage switch
    {
        AppLanguage.KO => "추적 중",
        AppLanguage.JA => "追跡中",
        _ => "Tracking"
    };

    public string AutoDetect => CurrentLanguage switch
    {
        AppLanguage.KO => "자동 감지",
        AppLanguage.JA => "自動検出",
        _ => "Auto Detect"
    };

    public string Automation => CurrentLanguage switch
    {
        AppLanguage.KO => "자동화",
        AppLanguage.JA => "自動化",
        _ => "Automation"
    };

    public string Layers => CurrentLanguage switch
    {
        AppLanguage.KO => "레이어",
        AppLanguage.JA => "レイヤー",
        _ => "Layers"
    };

    public string Legend => CurrentLanguage switch
    {
        AppLanguage.KO => "범례",
        AppLanguage.JA => "凡例",
        _ => "Legend"
    };

    #endregion
}
