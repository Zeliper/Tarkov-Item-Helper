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
        // 저장된 설정 로드
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
                OnPropertyChanged(nameof(IsEnglish));
                OnPropertyChanged(nameof(IsKorean));
                OnPropertyChanged(nameof(IsJapanese));
                LanguageChanged?.Invoke(this, value);
                SaveSettings(); // 언어 변경 시 저장
            }
        }
    }

    public bool IsEnglish => CurrentLanguage == AppLanguage.EN;
    public bool IsKorean => CurrentLanguage == AppLanguage.KO;
    public bool IsJapanese => CurrentLanguage == AppLanguage.JA;

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
            // 저장 실패 시 무시
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
            // 로드 실패 시 기본값 (EN) 사용
            _currentLanguage = AppLanguage.EN;
        }
    }

    private class AppSettings
    {
        public string Language { get; set; } = "EN";
    }

    #endregion

    /// <summary>
    /// Get display name based on current language
    /// </summary>
    public string GetDisplayName(string nameEn, string nameKo, string? nameJa = null)
    {
        return CurrentLanguage switch
        {
            AppLanguage.KO => nameKo,
            AppLanguage.JA => nameJa ?? nameEn,
            _ => nameEn
        };
    }

    /// <summary>
    /// Get secondary display name (shown smaller, below primary)
    /// EN mode shows empty string, KO/JA mode shows English name
    /// </summary>
    public string GetSecondaryName(string nameEn, string nameKo, string? nameJa = null)
    {
        return CurrentLanguage == AppLanguage.EN ? string.Empty : nameEn;
    }

    /// <summary>
    /// Check if secondary name should be visible (KO or JA mode)
    /// </summary>
    public bool ShowSecondaryName => CurrentLanguage != AppLanguage.EN;

    #region UI String Resources

    // Header
    public string AppSubtitle => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 & 은신처 트래커",
        AppLanguage.JA => "クエスト＆ハイドアウトトラッカー",
        _ => "Quest & Hideout Tracker"
    };

    public string RefreshData => CurrentLanguage switch
    {
        AppLanguage.KO => "데이터 새로고침",
        AppLanguage.JA => "データ更新",
        _ => "Refresh Data"
    };

    public string ResetProgress => CurrentLanguage switch
    {
        AppLanguage.KO => "진행 초기화",
        AppLanguage.JA => "進行状況リセット",
        _ => "Reset Progress"
    };

    // Tab Headers
    public string TabQuests => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        _ => "QUESTS"
    };

    public string TabHideout => CurrentLanguage switch
    {
        AppLanguage.KO => "은신처",
        AppLanguage.JA => "ハイドアウト",
        _ => "HIDEOUT"
    };

    public string TabRequiredItems => CurrentLanguage switch
    {
        AppLanguage.KO => "필요 아이템",
        AppLanguage.JA => "必要アイテム",
        _ => "REQUIRED ITEMS"
    };

    // Quest Tab
    public string SearchQuestsPlaceholder => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 검색 (영어/한국어/상인)...",
        AppLanguage.JA => "クエスト検索（EN/JA/トレーダー）...",
        _ => "Search quests (EN/KO/Trader)..."
    };

    public string HideCompleted => CurrentLanguage switch
    {
        AppLanguage.KO => "완료 숨기기",
        AppLanguage.JA => "完了を隠す",
        _ => "Hide Completed"
    };

    public string SearchAndComplete => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 빠른 시작",
        AppLanguage.JA => "クエストクイックスタート",
        _ => "Quick Start Quest"
    };

    public string SearchAndCompleteDesc => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트를 검색하여 모든 선행 퀘스트를 자동 완료하고 해당 퀘스트를 진행중으로 설정합니다",
        AppLanguage.JA => "クエストを検索し、すべての前提クエストを自動完了して進行中に設定します",
        _ => "Search for a quest, auto-complete all prerequisites, and set it as in progress"
    };

    public string SearchQuest => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 찾기",
        AppLanguage.JA => "クエスト検索",
        _ => "Find Quest"
    };

    public string SelectQuest => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 선택",
        AppLanguage.JA => "クエストを選択",
        _ => "Select a Quest"
    };

    public string Available => CurrentLanguage switch
    {
        AppLanguage.KO => "진행 가능",
        AppLanguage.JA => "開始可能",
        _ => "Available"
    };

    public string Items => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템",
        AppLanguage.JA => "アイテム",
        _ => "items"
    };

    public string Prerequisites => CurrentLanguage switch
    {
        AppLanguage.KO => "선행 퀘스트",
        AppLanguage.JA => "前提クエスト",
        _ => "Prerequisites"
    };

    public string Objectives => CurrentLanguage switch
    {
        AppLanguage.KO => "목표",
        AppLanguage.JA => "目標",
        _ => "Objectives"
    };

    public string Level => CurrentLanguage switch
    {
        AppLanguage.KO => "레벨",
        AppLanguage.JA => "レベル",
        _ => "Level"
    };

    // Hideout Tab
    public string SelectStation => CurrentLanguage switch
    {
        AppLanguage.KO => "시설 선택",
        AppLanguage.JA => "施設を選択",
        _ => "Select a Station"
    };

    public string CurrentLevel => CurrentLanguage switch
    {
        AppLanguage.KO => "현재 레벨:",
        AppLanguage.JA => "現在のレベル:",
        _ => "Current Level:"
    };

    public string NextLevelRequirements => CurrentLanguage switch
    {
        AppLanguage.KO => "다음 레벨 요구사항",
        AppLanguage.JA => "次のレベル要件",
        _ => "Next Level Requirements"
    };

    public string MaxLevelReached => CurrentLanguage switch
    {
        AppLanguage.KO => "최대 레벨 도달",
        AppLanguage.JA => "最大レベル到達",
        _ => "Max Level Reached"
    };

    public string StationMaxUpgraded => CurrentLanguage switch
    {
        AppLanguage.KO => "이 시설은 최대 업그레이드 되었습니다!",
        AppLanguage.JA => "この施設は最大レベルです！",
        _ => "This station is fully upgraded!"
    };

    public string RequiredStations => CurrentLanguage switch
    {
        AppLanguage.KO => "필요 시설",
        AppLanguage.JA => "必要施設",
        _ => "Required Stations"
    };

    public string RequiredTraders => CurrentLanguage switch
    {
        AppLanguage.KO => "필요 상인",
        AppLanguage.JA => "必要トレーダー",
        _ => "Required Traders"
    };

    public string RequiredItems => CurrentLanguage switch
    {
        AppLanguage.KO => "필요 아이템",
        AppLanguage.JA => "必要アイテム",
        _ => "Required Items"
    };

    // Required Items Tab
    public string TotalItems => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 아이템",
        AppLanguage.JA => "全アイテム",
        _ => "Total Items"
    };

    public string QuestItems => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 아이템",
        AppLanguage.JA => "クエストアイテム",
        _ => "Quest Items"
    };

    public string HideoutItems => CurrentLanguage switch
    {
        AppLanguage.KO => "은신처 아이템",
        AppLanguage.JA => "ハイドアウトアイテム",
        _ => "Hideout Items"
    };

    public string FirRequired => CurrentLanguage switch
    {
        AppLanguage.KO => "FIR 필요",
        AppLanguage.JA => "FIR必須",
        _ => "FIR Required"
    };

    public string SearchItemsPlaceholder => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 검색...",
        AppLanguage.JA => "アイテム検索...",
        _ => "Search items..."
    };

    public string FirOnly => CurrentLanguage switch
    {
        AppLanguage.KO => "FIR만",
        AppLanguage.JA => "FIRのみ",
        _ => "FIR Only"
    };

    public string QuestItemsFilter => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 아이템",
        AppLanguage.JA => "クエストアイテム",
        _ => "Quest Items"
    };

    public string HideoutItemsFilter => CurrentLanguage switch
    {
        AppLanguage.KO => "은신처 아이템",
        AppLanguage.JA => "ハイドアウトアイテム",
        _ => "Hideout Items"
    };

    public string SelectItem => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 선택",
        AppLanguage.JA => "アイテムを選択",
        _ => "Select an Item"
    };

    public string Quest => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        _ => "Quest"
    };

    public string Hideout => CurrentLanguage switch
    {
        AppLanguage.KO => "은신처",
        AppLanguage.JA => "ハイドアウト",
        _ => "Hideout"
    };

    public string Total => CurrentLanguage switch
    {
        AppLanguage.KO => "합계",
        AppLanguage.JA => "合計",
        _ => "Total"
    };

    public string Quests => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        _ => "Quests"
    };

    // Status messages
    public string LoadingData => CurrentLanguage switch
    {
        AppLanguage.KO => "데이터 로딩 중...",
        AppLanguage.JA => "データ読み込み中...",
        _ => "Loading data..."
    };

    public string DataLoadedSuccessfully => CurrentLanguage switch
    {
        AppLanguage.KO => "데이터 로드 완료",
        AppLanguage.JA => "データ読み込み完了",
        _ => "Data loaded successfully"
    };

    public string FailedToLoadData => CurrentLanguage switch
    {
        AppLanguage.KO => "데이터 로드 실패",
        AppLanguage.JA => "データ読み込み失敗",
        _ => "Failed to load data"
    };

    public string FetchingDataFromApi => CurrentLanguage switch
    {
        AppLanguage.KO => "API에서 데이터 가져오는 중...",
        AppLanguage.JA => "APIからデータ取得中...",
        _ => "Fetching data from API..."
    };

    public string DataRefreshedSuccessfully => CurrentLanguage switch
    {
        AppLanguage.KO => "데이터 새로고침 완료",
        AppLanguage.JA => "データ更新完了",
        _ => "Data refreshed successfully"
    };

    public string FailedToRefreshData => CurrentLanguage switch
    {
        AppLanguage.KO => "데이터 새로고침 실패",
        AppLanguage.JA => "データ更新失敗",
        _ => "Failed to refresh data"
    };

    public string ProgressReset => CurrentLanguage switch
    {
        AppLanguage.KO => "진행 초기화됨",
        AppLanguage.JA => "進行状況リセット完了",
        _ => "Progress reset"
    };

    // Dialogs
    public string SearchQuestTitle => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 검색",
        AppLanguage.JA => "クエスト検索",
        _ => "Search Quest"
    };

    public string EnterQuestName => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 이름 입력 (영어/한국어)...",
        AppLanguage.JA => "クエスト名を入力（EN/JA）...",
        _ => "Enter quest name (EN/KO)..."
    };

    public string SetInProgress => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 시작",
        AppLanguage.JA => "クエスト開始",
        _ => "Start Quest"
    };

    public string Confirm => CurrentLanguage switch
    {
        AppLanguage.KO => "확인",
        AppLanguage.JA => "確認",
        _ => "Confirm"
    };

    public string Error => CurrentLanguage switch
    {
        AppLanguage.KO => "오류",
        AppLanguage.JA => "エラー",
        _ => "Error"
    };

    public string RefreshDataConfirm => CurrentLanguage switch
    {
        AppLanguage.KO => "API에서 새 데이터를 가져옵니다. 계속하시겠습니까?",
        AppLanguage.JA => "APIから最新データを取得します。続行しますか？",
        _ => "This will fetch fresh data from the API. Continue?"
    };

    public string ResetProgressConfirm => CurrentLanguage switch
    {
        AppLanguage.KO => "모든 진행 상황이 초기화됩니다. 계속하시겠습니까?",
        AppLanguage.JA => "すべての進行状況がリセットされます。続行しますか？",
        _ => "This will reset all your progress. Are you sure?"
    };

    // Format strings
    public string FormatQuestProgress(int completed, int total) => CurrentLanguage switch
    {
        AppLanguage.KO => $"퀘스트: {completed}/{total}",
        AppLanguage.JA => $"クエスト: {completed}/{total}",
        _ => $"Quests: {completed}/{total}"
    };

    public string FormatHideoutProgress(int current, int total) => CurrentLanguage switch
    {
        AppLanguage.KO => $"은신처: {current}/{total}",
        AppLanguage.JA => $"ハイドアウト: {current}/{total}",
        _ => $"Hideout: {current}/{total}"
    };

    public string FormatLevelRequirement(int level) => CurrentLanguage switch
    {
        AppLanguage.KO => $"레벨 {level} 요구사항",
        AppLanguage.JA => $"レベル {level} 要件",
        _ => $"Level {level} Requirements"
    };

    public string FormatInProgressStatus(string questName, int prereqCount) => CurrentLanguage switch
    {
        AppLanguage.KO => $"시작됨: {questName} (선행 퀘스트 {prereqCount}개 완료)",
        AppLanguage.JA => $"開始: {questName}（前提クエスト{prereqCount}件完了）",
        _ => $"Started: {questName} ({prereqCount} prerequisites completed)"
    };

    public string FormatItemCount(int count) => CurrentLanguage switch
    {
        AppLanguage.KO => $"{count}개 아이템",
        AppLanguage.JA => $"{count}個のアイテム",
        _ => $"{count} items"
    };

    public string FormatTotalDetails(int total, int quest, int hideout) => CurrentLanguage switch
    {
        AppLanguage.KO => $"합계: {total} (퀘스트: {quest}, 은신처: {hideout})",
        AppLanguage.JA => $"合計: {total}（クエスト: {quest}、ハイドアウト: {hideout}）",
        _ => $"Total: {total} (Quest: {quest}, Hideout: {hideout})"
    };

    public string FormatSetInProgressConfirm(string nameEn, string nameKo, string? nameJa = null) => CurrentLanguage switch
    {
        AppLanguage.KO => $"'{nameKo}'를 시작하시겠습니까?",
        AppLanguage.JA => $"'{nameJa ?? nameEn}'を開始しますか？",
        _ => $"Start '{nameEn}'?"
    };

    public string FormatPrerequisiteCompleteCount(int count) => CurrentLanguage switch
    {
        AppLanguage.KO => $"다음 선행 퀘스트 {count}개가 완료 처리됩니다:",
        AppLanguage.JA => $"次の前提クエスト{count}件が完了扱いになります:",
        _ => $"The following {count} prerequisite quest(s) will be marked as completed:"
    };

    public string FormatAndMore(int count) => CurrentLanguage switch
    {
        AppLanguage.KO => $"  ... 외 {count}개",
        AppLanguage.JA => $"  ... 他{count}件",
        _ => $"  ... and {count} more"
    };

    public string FormatLevelDisplay(int level) => CurrentLanguage switch
    {
        AppLanguage.KO => $"레벨 {level}",
        AppLanguage.JA => $"Lv.{level}",
        _ => $"Lv.{level}"
    };

    public string FormatHideoutLevelDisplay(string stationName, int level) => CurrentLanguage switch
    {
        AppLanguage.KO => $"{stationName} 레벨 {level}",
        AppLanguage.JA => $"{stationName} Lv.{level}",
        _ => $"{stationName} Lv.{level}"
    };

    // Log Monitoring
    public string LogMonitoring => CurrentLanguage switch
    {
        AppLanguage.KO => "로그 모니터링",
        AppLanguage.JA => "ログ監視",
        _ => "Log Monitoring"
    };

    public string LogPathSettings => CurrentLanguage switch
    {
        AppLanguage.KO => "로그 경로 설정",
        AppLanguage.JA => "ログパス設定",
        _ => "Log Path Settings"
    };

    public string GameFolder => CurrentLanguage switch
    {
        AppLanguage.KO => "게임 폴더",
        AppLanguage.JA => "ゲームフォルダ",
        _ => "Game Folder"
    };

    public string LogsFolder => CurrentLanguage switch
    {
        AppLanguage.KO => "로그 폴더",
        AppLanguage.JA => "ログフォルダ",
        _ => "Logs Folder"
    };

    public string Browse => CurrentLanguage switch
    {
        AppLanguage.KO => "찾아보기",
        AppLanguage.JA => "参照",
        _ => "Browse"
    };

    public string AutoDetect => CurrentLanguage switch
    {
        AppLanguage.KO => "자동 감지",
        AppLanguage.JA => "自動検出",
        _ => "Auto-Detect"
    };

    public string LogStatusMonitoring => CurrentLanguage switch
    {
        AppLanguage.KO => "모니터링 중",
        AppLanguage.JA => "監視中",
        _ => "Monitoring"
    };

    public string LogStatusNotStarted => CurrentLanguage switch
    {
        AppLanguage.KO => "시작 안됨",
        AppLanguage.JA => "未開始",
        _ => "Not Started"
    };

    public string LogStatusError => CurrentLanguage switch
    {
        AppLanguage.KO => "오류",
        AppLanguage.JA => "エラー",
        _ => "Error"
    };

    public string LogStatusTooltipMonitoring => CurrentLanguage switch
    {
        AppLanguage.KO => "로그 모니터링 활성 - 퀘스트 완료가 자동으로 감지됩니다",
        AppLanguage.JA => "ログ監視有効 - クエスト完了が自動検出されます",
        _ => "Log monitoring active - Quest completions will be detected automatically"
    };

    public string LogStatusTooltipNotStarted => CurrentLanguage switch
    {
        AppLanguage.KO => "로그 모니터링 시작 안됨 - 클릭하여 설정",
        AppLanguage.JA => "ログ監視未開始 - クリックして設定",
        _ => "Log monitoring not started - Click to configure"
    };

    public string LogStatusTooltipError => CurrentLanguage switch
    {
        AppLanguage.KO => "로그 모니터링 오류 - 클릭하여 설정",
        AppLanguage.JA => "ログ監視エラー - クリックして設定",
        _ => "Log monitoring error - Click to configure"
    };

    public string StartMonitoring => CurrentLanguage switch
    {
        AppLanguage.KO => "모니터링 시작",
        AppLanguage.JA => "監視開始",
        _ => "Start Monitoring"
    };

    public string StopMonitoring => CurrentLanguage switch
    {
        AppLanguage.KO => "모니터링 중지",
        AppLanguage.JA => "監視停止",
        _ => "Stop Monitoring"
    };

    public string QuestCompletedFromLog => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 완료 (게임 로그에서 감지)",
        AppLanguage.JA => "クエスト完了（ゲームログから検出）",
        _ => "Quest completed (detected from game log)"
    };

    public string FormatQuestCompletedFromLog(string questName) => CurrentLanguage switch
    {
        AppLanguage.KO => $"'{questName}' 완료 (게임 로그에서 감지)",
        AppLanguage.JA => $"'{questName}' 完了（ゲームログから検出）",
        _ => $"'{questName}' completed (detected from game log)"
    };

    public string SelectGameFolder => CurrentLanguage switch
    {
        AppLanguage.KO => "타르코프 게임 폴더 선택",
        AppLanguage.JA => "タルコフゲームフォルダを選択",
        _ => "Select Tarkov game folder"
    };

    public string InvalidGameFolder => CurrentLanguage switch
    {
        AppLanguage.KO => "선택한 폴더가 유효한 타르코프 설치 폴더가 아닙니다",
        AppLanguage.JA => "選択したフォルダは有効なタルコフインストールフォルダではありません",
        _ => "Selected folder doesn't appear to be a valid Tarkov installation"
    };

    public string Save => CurrentLanguage switch
    {
        AppLanguage.KO => "저장",
        AppLanguage.JA => "保存",
        _ => "Save"
    };

    public string Cancel => CurrentLanguage switch
    {
        AppLanguage.KO => "취소",
        AppLanguage.JA => "キャンセル",
        _ => "Cancel"
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

    // Game Path Detection
    public string GamePathNotFoundTitle => CurrentLanguage switch
    {
        AppLanguage.KO => "게임 경로를 찾을 수 없음",
        AppLanguage.JA => "ゲームパスが見つかりません",
        _ => "Game Path Not Found"
    };

    public string GamePathNotFoundMessage => CurrentLanguage switch
    {
        AppLanguage.KO => "Escape from Tarkov 설치 경로를 자동으로 찾을 수 없습니다.\n\n게임 폴더를 직접 선택하시겠습니까?\n\n(퀘스트 자동 완료 추적을 위해 필요합니다)",
        AppLanguage.JA => "Escape from Tarkovのインストールパスを自動検出できませんでした。\n\nゲームフォルダを手動で選択しますか？\n\n（クエスト自動完了追跡に必要です）",
        _ => "Escape from Tarkov installation could not be detected automatically.\n\nWould you like to select the game folder manually?\n\n(This is required for automatic quest completion tracking)"
    };

    public string InvalidGameFolderRetry => CurrentLanguage switch
    {
        AppLanguage.KO => "선택한 폴더가 유효한 타르코프 설치 폴더가 아닌 것 같습니다.\n\n다시 시도하시겠습니까?",
        AppLanguage.JA => "選択したフォルダは有効なタルコフインストールフォルダではないようです。\n\n再試行しますか？",
        _ => "The selected folder does not appear to be a valid Tarkov installation.\n\nWould you like to try again?"
    };

    public string LogMonitoringDisabled => CurrentLanguage switch
    {
        AppLanguage.KO => "로그 모니터링 비활성화됨 - 설정에서 구성하세요",
        AppLanguage.JA => "ログ監視無効 - 設定で構成してください",
        _ => "Log monitoring disabled - Configure in Settings"
    };

    public string AutoDetectFailed => CurrentLanguage switch
    {
        AppLanguage.KO => "타르코프 설치 경로를 자동으로 찾을 수 없습니다.\n\n찾아보기 버튼을 사용하여 게임 폴더를 직접 선택해주세요.",
        AppLanguage.JA => "タルコフのインストールパスを自動検出できませんでした。\n\n参照ボタンでゲームフォルダを手動で選択してください。",
        _ => "Could not automatically detect Tarkov installation.\n\nPlease select the game folder manually using the Browse button."
    };

    public string FormatGamePathDetected(string method, string path) => CurrentLanguage switch
    {
        AppLanguage.KO => $"{method}에서 게임 경로 감지됨: {path}",
        AppLanguage.JA => $"{method}でゲームパス検出: {path}",
        _ => $"Game path detected via {method}: {path}"
    };

    public string FormatGamePathSet(string path) => CurrentLanguage switch
    {
        AppLanguage.KO => $"게임 경로 설정됨: {path}",
        AppLanguage.JA => $"ゲームパス設定: {path}",
        _ => $"Game path set: {path}"
    };

    public string FormatAutoDetectSuccess(string method, string path) => CurrentLanguage switch
    {
        AppLanguage.KO => $"게임 폴더를 찾았습니다!\n\n방법: {method}\n경로: {path}",
        AppLanguage.JA => $"ゲームフォルダを検出しました！\n\n方法: {method}\nパス: {path}",
        _ => $"Game folder detected!\n\nMethod: {method}\nPath: {path}"
    };

    #endregion
}
