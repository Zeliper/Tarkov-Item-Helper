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

    #region Map Tracker Page

    // 상단 컨트롤 바
    public string MapPositionTracker => CurrentLanguage switch
    {
        AppLanguage.KO => "맵 위치 트래커",
        AppLanguage.JA => "マップ位置トラッカー",
        _ => "Map Position Tracker"
    };

    public string MapLabel => CurrentLanguage switch
    {
        AppLanguage.KO => "맵:",
        AppLanguage.JA => "マップ:",
        _ => "Map:"
    };

    public string QuestMarkers => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 마커",
        AppLanguage.JA => "クエストマーカー",
        _ => "Quest Markers"
    };

    public string Extracts => CurrentLanguage switch
    {
        AppLanguage.KO => "탈출구",
        AppLanguage.JA => "脱出口",
        _ => "Extracts"
    };

    public string ClearTrail => CurrentLanguage switch
    {
        AppLanguage.KO => "경로 지우기",
        AppLanguage.JA => "軌跡クリア",
        _ => "Clear Trail"
    };

    public string FullScreen => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 화면",
        AppLanguage.JA => "全画面",
        _ => "Full Screen"
    };

    public string ExitFullScreen => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 화면 종료",
        AppLanguage.JA => "全画面終了",
        _ => "Exit Full Screen"
    };

    public string Settings => CurrentLanguage switch
    {
        AppLanguage.KO => "설정",
        AppLanguage.JA => "設定",
        _ => "Settings"
    };

    public string StartTracking => CurrentLanguage switch
    {
        AppLanguage.KO => "추적 시작",
        AppLanguage.JA => "追跡開始",
        _ => "Start Tracking"
    };

    public string StopTracking => CurrentLanguage switch
    {
        AppLanguage.KO => "추적 중지",
        AppLanguage.JA => "追跡停止",
        _ => "Stop Tracking"
    };

    // 상태 표시 바
    public string StatusWaiting => CurrentLanguage switch
    {
        AppLanguage.KO => "대기 중",
        AppLanguage.JA => "待機中",
        _ => "Waiting"
    };

    public string StatusTracking => CurrentLanguage switch
    {
        AppLanguage.KO => "추적 중",
        AppLanguage.JA => "追跡中",
        _ => "Tracking"
    };

    public string PositionLabel => CurrentLanguage switch
    {
        AppLanguage.KO => "위치:",
        AppLanguage.JA => "位置:",
        _ => "Position:"
    };

    public string LastUpdateLabel => CurrentLanguage switch
    {
        AppLanguage.KO => "마지막 업데이트:",
        AppLanguage.JA => "最終更新:",
        _ => "Last update:"
    };

    // 퀘스트 드로어
    public string QuestObjectives => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 목표",
        AppLanguage.JA => "クエスト目標",
        _ => "Quest Objectives"
    };

    public string ProgressOnThisMap => CurrentLanguage switch
    {
        AppLanguage.KO => "이 맵 진행률",
        AppLanguage.JA => "このマップの進捗",
        _ => "Progress on this map"
    };

    public string FilterAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체",
        AppLanguage.JA => "全て",
        _ => "All"
    };

    public string FilterIncomplete => CurrentLanguage switch
    {
        AppLanguage.KO => "미완료",
        AppLanguage.JA => "未完了",
        _ => "Incomplete"
    };

    public string FilterCompleted => CurrentLanguage switch
    {
        AppLanguage.KO => "완료",
        AppLanguage.JA => "完了",
        _ => "Completed"
    };

    public string FilterAllTypes => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 타입",
        AppLanguage.JA => "全タイプ",
        _ => "All Types"
    };

    public string FilterVisit => CurrentLanguage switch
    {
        AppLanguage.KO => "방문",
        AppLanguage.JA => "訪問",
        _ => "Visit"
    };

    public string FilterMark => CurrentLanguage switch
    {
        AppLanguage.KO => "마킹",
        AppLanguage.JA => "マーキング",
        _ => "Mark"
    };

    public string FilterPlant => CurrentLanguage switch
    {
        AppLanguage.KO => "설치",
        AppLanguage.JA => "設置",
        _ => "Plant"
    };

    public string FilterExtract => CurrentLanguage switch
    {
        AppLanguage.KO => "탈출",
        AppLanguage.JA => "脱出",
        _ => "Extract"
    };

    public string FilterFind => CurrentLanguage switch
    {
        AppLanguage.KO => "찾기",
        AppLanguage.JA => "発見",
        _ => "Find"
    };

    public string ThisMapOnly => CurrentLanguage switch
    {
        AppLanguage.KO => "이 맵만",
        AppLanguage.JA => "このマップのみ",
        _ => "This Map"
    };

    public string GroupByQuest => CurrentLanguage switch
    {
        AppLanguage.KO => "그룹화",
        AppLanguage.JA => "グループ化",
        _ => "Group"
    };

    // 설정 패널
    public string ScreenshotFolder => CurrentLanguage switch
    {
        AppLanguage.KO => "스크린샷 폴더",
        AppLanguage.JA => "スクリーンショットフォルダ",
        _ => "Screenshot Folder"
    };

    public string AutoDetect => CurrentLanguage switch
    {
        AppLanguage.KO => "자동 감지",
        AppLanguage.JA => "自動検出",
        _ => "Auto Detect"
    };

    public string Browse => CurrentLanguage switch
    {
        AppLanguage.KO => "찾아보기",
        AppLanguage.JA => "参照",
        _ => "Browse"
    };

    public string MarkerSettings => CurrentLanguage switch
    {
        AppLanguage.KO => "마커 설정",
        AppLanguage.JA => "マーカー設定",
        _ => "Marker Settings"
    };

    public string HideCompletedObjectives => CurrentLanguage switch
    {
        AppLanguage.KO => "완료된 목표 숨기기",
        AppLanguage.JA => "完了した目標を隠す",
        _ => "Hide Completed Objectives"
    };

    public string QuestStyle => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 스타일:",
        AppLanguage.JA => "クエストスタイル:",
        _ => "Quest Style:"
    };

    public string QuestNameSize => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트명:",
        AppLanguage.JA => "クエスト名:",
        _ => "Quest Name:"
    };

    public string QuestMarkerSize => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 마커:",
        AppLanguage.JA => "クエストマーカー:",
        _ => "Quest Marker:"
    };

    public string PlayerMarkerSize => CurrentLanguage switch
    {
        AppLanguage.KO => "플레이어 마커:",
        AppLanguage.JA => "プレイヤーマーカー:",
        _ => "Player Marker:"
    };

    public string ExtractSettings => CurrentLanguage switch
    {
        AppLanguage.KO => "탈출구 설정",
        AppLanguage.JA => "脱出口設定",
        _ => "Extract Settings"
    };

    public string PmcExtracts => CurrentLanguage switch
    {
        AppLanguage.KO => "PMC 탈출구",
        AppLanguage.JA => "PMC脱出口",
        _ => "PMC Extracts"
    };

    public string ScavExtracts => CurrentLanguage switch
    {
        AppLanguage.KO => "Scav 탈출구",
        AppLanguage.JA => "Scav脱出口",
        _ => "Scav Extracts"
    };

    public string ExtractNameSize => CurrentLanguage switch
    {
        AppLanguage.KO => "이름 크기:",
        AppLanguage.JA => "名前サイズ:",
        _ => "Name Size:"
    };

    // 마커 색상 설정
    public string MarkerColors => CurrentLanguage switch
    {
        AppLanguage.KO => "마커 색상",
        AppLanguage.JA => "マーカー色",
        _ => "Marker Colors"
    };

    public string ResetColors => CurrentLanguage switch
    {
        AppLanguage.KO => "색상 초기화",
        AppLanguage.JA => "色をリセット",
        _ => "Reset Colors"
    };

    // 맵 없음 안내
    public string NoMapImage => CurrentLanguage switch
    {
        AppLanguage.KO => "맵 이미지가 없습니다",
        AppLanguage.JA => "マップ画像がありません",
        _ => "No map image available"
    };

    public string AddMapImageHint => CurrentLanguage switch
    {
        AppLanguage.KO => "Assets/Maps/ 폴더에 맵 이미지를 추가하세요",
        AppLanguage.JA => "Assets/Maps/フォルダにマップ画像を追加してください",
        _ => "Add map image to Assets/Maps/ folder"
    };

    public string SetImagePathHint => CurrentLanguage switch
    {
        AppLanguage.KO => "또는 설정에서 이미지 경로를 지정하세요",
        AppLanguage.JA => "または設定で画像パスを指定してください",
        _ => "Or specify image path in settings"
    };

    public string ResetView => CurrentLanguage switch
    {
        AppLanguage.KO => "초기화",
        AppLanguage.JA => "リセット",
        _ => "Reset"
    };

    // 퀘스트 스타일 옵션
    public string StyleIconOnly => CurrentLanguage switch
    {
        AppLanguage.KO => "아이콘만",
        AppLanguage.JA => "アイコンのみ",
        _ => "Icon Only"
    };

    public string StyleGreenCircle => CurrentLanguage switch
    {
        AppLanguage.KO => "녹색 원",
        AppLanguage.JA => "緑の丸",
        _ => "Green Circle"
    };

    public string StyleIconWithName => CurrentLanguage switch
    {
        AppLanguage.KO => "아이콘 + 이름",
        AppLanguage.JA => "アイコン+名前",
        _ => "Icon + Name"
    };

    public string StyleCircleWithName => CurrentLanguage switch
    {
        AppLanguage.KO => "원 + 이름",
        AppLanguage.JA => "丸+名前",
        _ => "Circle + Name"
    };

    #endregion
}
