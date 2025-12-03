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

    #region In-Progress Quest Input

    public string InProgressQuestInputButton => CurrentLanguage switch
    {
        AppLanguage.KO => "진행중 퀘스트 입력",
        AppLanguage.JA => "進行中クエスト入力",
        _ => "Enter In-Progress Quests"
    };

    public string InProgressQuestInputTitle => CurrentLanguage switch
    {
        AppLanguage.KO => "진행중 퀘스트 입력",
        AppLanguage.JA => "進行中クエスト入力",
        _ => "Enter In-Progress Quests"
    };

    public string QuestSelection => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 선택",
        AppLanguage.JA => "クエスト選択",
        _ => "Quest Selection"
    };

    public string SearchQuestsPlaceholder => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 검색...",
        AppLanguage.JA => "クエスト検索...",
        _ => "Search quests..."
    };

    public string TraderFilter => CurrentLanguage switch
    {
        AppLanguage.KO => "트레이더:",
        AppLanguage.JA => "トレーダー:",
        _ => "Trader:"
    };

    public string AllTraders => CurrentLanguage switch
    {
        AppLanguage.KO => "전체",
        AppLanguage.JA => "全て",
        _ => "All"
    };

    public string PrerequisitesPreview => CurrentLanguage switch
    {
        AppLanguage.KO => "선행 퀘스트 미리보기",
        AppLanguage.JA => "先行クエストプレビュー",
        _ => "Prerequisites Preview"
    };

    public string PrerequisitesDescription => CurrentLanguage switch
    {
        AppLanguage.KO => "체크된 퀘스트의 선행 퀘스트가 여기에 표시됩니다.\n적용 시 자동으로 완료 처리됩니다.",
        AppLanguage.JA => "選択されたクエストの先行クエストがここに表示されます。\n適用時に自動完了されます。",
        _ => "Prerequisites of selected quests will be shown here.\nThese will be auto-completed on apply."
    };

    public string SelectedQuestsCount => CurrentLanguage switch
    {
        AppLanguage.KO => "선택된 퀘스트: {0}개",
        AppLanguage.JA => "選択されたクエスト: {0}件",
        _ => "Selected quests: {0}"
    };

    public string PrerequisitesToComplete => CurrentLanguage switch
    {
        AppLanguage.KO => "자동 완료될 선행 퀘스트: {0}개",
        AppLanguage.JA => "自動完了される先行クエスト: {0}件",
        _ => "Prerequisites to complete: {0}"
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

    public string QuestDataNotLoaded => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 데이터가 로드되지 않았습니다. 먼저 데이터를 새로고침 해주세요.",
        AppLanguage.JA => "クエストデータがロードされていません。まずデータを更新してください。",
        _ => "Quest data is not loaded. Please refresh data first."
    };

    public string NoQuestsSelected => CurrentLanguage switch
    {
        AppLanguage.KO => "선택된 퀘스트가 없습니다.",
        AppLanguage.JA => "選択されたクエストがありません。",
        _ => "No quests selected."
    };

    public string QuestsAppliedSuccess => CurrentLanguage switch
    {
        AppLanguage.KO => "{0}개의 퀘스트가 Active로 설정되고, {1}개의 선행 퀘스트가 완료 처리되었습니다.",
        AppLanguage.JA => "{0}件のクエストがActiveに設定され、{1}件の先行クエストが完了処理されました。",
        _ => "{0} quest(s) set to Active, {1} prerequisite(s) marked as completed."
    };

    #endregion
}
