using System.ComponentModel;
using System.IO;
using System.Text.Json;
using TarkovHelper.Debug;

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
/// Settings are stored in user_data.db (UserSettings table)
/// </summary>
public class LocalizationService : INotifyPropertyChanged
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

    #region Quest Recommendations

    public string RecommendedQuests => CurrentLanguage switch
    {
        AppLanguage.KO => "추천 퀘스트",
        AppLanguage.JA => "おすすめクエスト",
        _ => "Recommended Quests"
    };

    public string ReadyToComplete => CurrentLanguage switch
    {
        AppLanguage.KO => "지금 완료 가능",
        AppLanguage.JA => "今すぐ完了可能",
        _ => "Ready to Complete"
    };

    public string ItemHandInOnly => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 제출만",
        AppLanguage.JA => "アイテム提出のみ",
        _ => "Item Hand-in Only"
    };

    public string KappaPriority => CurrentLanguage switch
    {
        AppLanguage.KO => "카파 필수",
        AppLanguage.JA => "Kappa必須",
        _ => "Kappa Priority"
    };

    public string UnlocksMany => CurrentLanguage switch
    {
        AppLanguage.KO => "다수 해금",
        AppLanguage.JA => "複数解放",
        _ => "Unlocks Many"
    };

    public string EasyQuest => CurrentLanguage switch
    {
        AppLanguage.KO => "쉬운 퀘스트",
        AppLanguage.JA => "簡単なクエスト",
        _ => "Easy Quest"
    };

    public string NoRecommendations => CurrentLanguage switch
    {
        AppLanguage.KO => "현재 추천 퀘스트가 없습니다",
        AppLanguage.JA => "現在おすすめクエストはありません",
        _ => "No recommendations at this time"
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

    public string ItemsOwned => CurrentLanguage switch
    {
        AppLanguage.KO => "보유",
        AppLanguage.JA => "所持",
        _ => "owned"
    };

    public string ItemsNeeded => CurrentLanguage switch
    {
        AppLanguage.KO => "필요",
        AppLanguage.JA => "必要",
        _ => "needed"
    };

    public string UnlocksQuests => CurrentLanguage switch
    {
        AppLanguage.KO => "개 퀘스트 해금",
        AppLanguage.JA => "クエスト解放",
        _ => "quest(s) unlock"
    };

    #endregion

    #region Map Page - Quest Drawer

    public string Quest => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        _ => "Quest"
    };

    public string QuestPanelTooltip => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 패널 열기/닫기 (Q)",
        AppLanguage.JA => "クエストパネル開閉 (Q)",
        _ => "Open/Close Quest Panel (Q)"
    };

    public string ShortcutHelp => CurrentLanguage switch
    {
        AppLanguage.KO => "단축키 도움말",
        AppLanguage.JA => "ショートカットヘルプ",
        _ => "Shortcut Help"
    };

    public string DisplayOptions => CurrentLanguage switch
    {
        AppLanguage.KO => "표시 옵션",
        AppLanguage.JA => "表示オプション",
        _ => "Display Options"
    };

    public string Close => CurrentLanguage switch
    {
        AppLanguage.KO => "닫기",
        AppLanguage.JA => "閉じる",
        _ => "Close"
    };

    public string CloseWithShortcut => CurrentLanguage switch
    {
        AppLanguage.KO => "닫기 (Q)",
        AppLanguage.JA => "閉じる (Q)",
        _ => "Close (Q)"
    };

    public string SearchPlaceholder => CurrentLanguage switch
    {
        AppLanguage.KO => "🔍 검색...",
        AppLanguage.JA => "🔍 検索...",
        _ => "🔍 Search..."
    };

    public string Incomplete => CurrentLanguage switch
    {
        AppLanguage.KO => "미완료",
        AppLanguage.JA => "未完了",
        _ => "Incomplete"
    };

    public string CurrentMap => CurrentLanguage switch
    {
        AppLanguage.KO => "현재 맵",
        AppLanguage.JA => "現在のマップ",
        _ => "Current Map"
    };

    public string SortByName => CurrentLanguage switch
    {
        AppLanguage.KO => "이름",
        AppLanguage.JA => "名前",
        _ => "Name"
    };

    public string SortByProgress => CurrentLanguage switch
    {
        AppLanguage.KO => "진행률",
        AppLanguage.JA => "進捗",
        _ => "Progress"
    };

    public string SortByCount => CurrentLanguage switch
    {
        AppLanguage.KO => "개수",
        AppLanguage.JA => "個数",
        _ => "Count"
    };

    public string NoQuestsToDisplay => CurrentLanguage switch
    {
        AppLanguage.KO => "표시할 퀘스트 없음",
        AppLanguage.JA => "表示するクエストがありません",
        _ => "No quests to display"
    };

    public string TryAdjustingFilters => CurrentLanguage switch
    {
        AppLanguage.KO => "필터를 조정해 보세요",
        AppLanguage.JA => "フィルターを調整してください",
        _ => "Try adjusting filters"
    };

    public string MarkAllComplete => CurrentLanguage switch
    {
        AppLanguage.KO => "모두 완료",
        AppLanguage.JA => "すべて完了",
        _ => "Complete All"
    };

    public string MarkAllIncomplete => CurrentLanguage switch
    {
        AppLanguage.KO => "모두 미완료",
        AppLanguage.JA => "すべて未完了",
        _ => "Mark All Incomplete"
    };

    public string HideFromMap => CurrentLanguage switch
    {
        AppLanguage.KO => "맵에서 숨기기",
        AppLanguage.JA => "マップから隠す",
        _ => "Hide from Map"
    };

    public string ShowHideOnMap => CurrentLanguage switch
    {
        AppLanguage.KO => "맵에 표시/숨김",
        AppLanguage.JA => "マップに表示/非表示",
        _ => "Show/Hide on Map"
    };

    public string ViewOnMap => CurrentLanguage switch
    {
        AppLanguage.KO => "맵에서 보기",
        AppLanguage.JA => "マップで表示",
        _ => "View on Map"
    };

    // Keyboard Hints
    public string OpenClose => CurrentLanguage switch
    {
        AppLanguage.KO => "열기/닫기",
        AppLanguage.JA => "開閉",
        _ => "Open/Close"
    };

    public string Move => CurrentLanguage switch
    {
        AppLanguage.KO => "이동",
        AppLanguage.JA => "移動",
        _ => "Move"
    };

    public string Select => CurrentLanguage switch
    {
        AppLanguage.KO => "선택",
        AppLanguage.JA => "選択",
        _ => "Select"
    };

    public string GoToMap => CurrentLanguage switch
    {
        AppLanguage.KO => "맵이동",
        AppLanguage.JA => "マップ移動",
        _ => "Go to Map"
    };

    public string ToggleComplete => CurrentLanguage switch
    {
        AppLanguage.KO => "완료토글",
        AppLanguage.JA => "完了切替",
        _ => "Toggle Complete"
    };

    public string Click => CurrentLanguage switch
    {
        AppLanguage.KO => "클릭",
        AppLanguage.JA => "クリック",
        _ => "Click"
    };

    public string RightClick => CurrentLanguage switch
    {
        AppLanguage.KO => "우클릭",
        AppLanguage.JA => "右クリック",
        _ => "Right-click"
    };

    #endregion

    #region Map Page - Map Area

    public string Scroll => CurrentLanguage switch
    {
        AppLanguage.KO => "스크롤",
        AppLanguage.JA => "スクロール",
        _ => "Scroll"
    };

    public string Zoom => CurrentLanguage switch
    {
        AppLanguage.KO => "줌",
        AppLanguage.JA => "ズーム",
        _ => "Zoom"
    };

    public string Drag => CurrentLanguage switch
    {
        AppLanguage.KO => "드래그",
        AppLanguage.JA => "ドラッグ",
        _ => "Drag"
    };

    public string Reset => CurrentLanguage switch
    {
        AppLanguage.KO => "리셋",
        AppLanguage.JA => "リセット",
        _ => "Reset"
    };

    public string LoadingMap => CurrentLanguage switch
    {
        AppLanguage.KO => "맵 로딩 중...",
        AppLanguage.JA => "マップ読み込み中...",
        _ => "Loading map..."
    };

    public string ZoomInTooltip => CurrentLanguage switch
    {
        AppLanguage.KO => "확대 (Scroll Up)",
        AppLanguage.JA => "拡大 (Scroll Up)",
        _ => "Zoom In (Scroll Up)"
    };

    public string ZoomOutTooltip => CurrentLanguage switch
    {
        AppLanguage.KO => "축소 (Scroll Down)",
        AppLanguage.JA => "縮小 (Scroll Down)",
        _ => "Zoom Out (Scroll Down)"
    };

    public string ResetViewTooltip => CurrentLanguage switch
    {
        AppLanguage.KO => "뷰 초기화 (R)",
        AppLanguage.JA => "ビューリセット (R)",
        _ => "Reset View (R)"
    };

    #endregion

    #region Map Page - Legend

    public string MapLegend => CurrentLanguage switch
    {
        AppLanguage.KO => "맵 범례",
        AppLanguage.JA => "マップ凡例",
        _ => "Map Legend"
    };

    public string Extract => CurrentLanguage switch
    {
        AppLanguage.KO => "탈출구",
        AppLanguage.JA => "脱出口",
        _ => "Extract"
    };

    public string TransitPoint => CurrentLanguage switch
    {
        AppLanguage.KO => "환승 지점",
        AppLanguage.JA => "乗り換え地点",
        _ => "Transit Point"
    };

    public string QuestObjective => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 목표",
        AppLanguage.JA => "クエスト目標",
        _ => "Quest Objective"
    };

    public string QuestType => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 타입",
        AppLanguage.JA => "クエストタイプ",
        _ => "Quest Type"
    };

    public string Visit => CurrentLanguage switch
    {
        AppLanguage.KO => "방문",
        AppLanguage.JA => "訪問",
        _ => "Visit"
    };

    public string Mark => CurrentLanguage switch
    {
        AppLanguage.KO => "마킹",
        AppLanguage.JA => "マーキング",
        _ => "Mark"
    };

    public string PlantItem => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 설치",
        AppLanguage.JA => "アイテム設置",
        _ => "Plant Item"
    };

    public string Kill => CurrentLanguage switch
    {
        AppLanguage.KO => "처치",
        AppLanguage.JA => "撃破",
        _ => "Kill"
    };

    #endregion

    #region Map Page - Quest Filter

    public string QuestTypeFilter => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 타입 필터",
        AppLanguage.JA => "クエストタイプフィルター",
        _ => "Quest Type Filter"
    };

    public string VisitType => CurrentLanguage switch
    {
        AppLanguage.KO => "방문 (Visit)",
        AppLanguage.JA => "訪問 (Visit)",
        _ => "Visit"
    };

    public string MarkType => CurrentLanguage switch
    {
        AppLanguage.KO => "마킹 (Mark)",
        AppLanguage.JA => "マーキング (Mark)",
        _ => "Mark"
    };

    public string PlantType => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 설치 (Plant)",
        AppLanguage.JA => "アイテム設置 (Plant)",
        _ => "Plant Item"
    };

    public string ExtractType => CurrentLanguage switch
    {
        AppLanguage.KO => "탈출 (Extract)",
        AppLanguage.JA => "脱出 (Extract)",
        _ => "Extract"
    };

    public string FindType => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 찾기 (Find)",
        AppLanguage.JA => "アイテム発見 (Find)",
        _ => "Find Item"
    };

    public string KillType => CurrentLanguage switch
    {
        AppLanguage.KO => "처치 (Kill)",
        AppLanguage.JA => "撃破 (Kill)",
        _ => "Kill"
    };

    public string OtherType => CurrentLanguage switch
    {
        AppLanguage.KO => "기타 (Other)",
        AppLanguage.JA => "その他 (Other)",
        _ => "Other"
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

    #endregion

    #region Map Page - Minimap

    public string Minimap => CurrentLanguage switch
    {
        AppLanguage.KO => "미니맵",
        AppLanguage.JA => "ミニマップ",
        _ => "Minimap"
    };

    #endregion

    #region Map Page - Settings

    public string SettingsTitle => CurrentLanguage switch
    {
        AppLanguage.KO => "⚙ 설정",
        AppLanguage.JA => "⚙ 設定",
        _ => "⚙ Settings"
    };

    public string SettingsTooltip => CurrentLanguage switch
    {
        AppLanguage.KO => "설정 (레이어, 마커 크기, 트래커)",
        AppLanguage.JA => "設定 (レイヤー、マーカーサイズ、トラッカー)",
        _ => "Settings (Layers, Marker Size, Tracker)"
    };

    public string TabDisplay => CurrentLanguage switch
    {
        AppLanguage.KO => "표시",
        AppLanguage.JA => "表示",
        _ => "Display"
    };

    public string TabMarker => CurrentLanguage switch
    {
        AppLanguage.KO => "마커",
        AppLanguage.JA => "マーカー",
        _ => "Marker"
    };

    public string TabTracker => CurrentLanguage switch
    {
        AppLanguage.KO => "트래커",
        AppLanguage.JA => "トラッカー",
        _ => "Tracker"
    };

    public string TabShortcuts => CurrentLanguage switch
    {
        AppLanguage.KO => "단축키",
        AppLanguage.JA => "ショートカット",
        _ => "Shortcuts"
    };

    // Display Tab
    public string Layers => CurrentLanguage switch
    {
        AppLanguage.KO => "레이어",
        AppLanguage.JA => "レイヤー",
        _ => "Layers"
    };

    public string Trail => CurrentLanguage switch
    {
        AppLanguage.KO => "이동 경로",
        AppLanguage.JA => "移動経路",
        _ => "Trail"
    };

    public string ShowMinimap => CurrentLanguage switch
    {
        AppLanguage.KO => "미니맵 표시",
        AppLanguage.JA => "ミニマップ表示",
        _ => "Show Minimap"
    };

    public string MinimapSize => CurrentLanguage switch
    {
        AppLanguage.KO => "미니맵 크기",
        AppLanguage.JA => "ミニマップサイズ",
        _ => "Minimap Size"
    };

    public string QuestFilter => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 필터",
        AppLanguage.JA => "クエストフィルター",
        _ => "Quest Filter"
    };

    public string Legend => CurrentLanguage switch
    {
        AppLanguage.KO => "범례",
        AppLanguage.JA => "凡例",
        _ => "Legend"
    };

    // Marker Tab
    public string MarkerSize => CurrentLanguage switch
    {
        AppLanguage.KO => "마커 크기",
        AppLanguage.JA => "マーカーサイズ",
        _ => "Marker Size"
    };

    public string MarkerOpacity => CurrentLanguage switch
    {
        AppLanguage.KO => "마커 투명도",
        AppLanguage.JA => "マーカー透明度",
        _ => "Marker Opacity"
    };

    public string QuestDisplay => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 표시",
        AppLanguage.JA => "クエスト表示",
        _ => "Quest Display"
    };

    public string AutoHideCompleted => CurrentLanguage switch
    {
        AppLanguage.KO => "완료 퀘스트 자동 숨김",
        AppLanguage.JA => "完了クエストを自動非表示",
        _ => "Auto-hide Completed Quests"
    };

    public string FadeCompleted => CurrentLanguage switch
    {
        AppLanguage.KO => "완료 퀘스트 흐리게",
        AppLanguage.JA => "完了クエストを薄く表示",
        _ => "Fade Completed Quests"
    };

    public string ShowMarkerLabels => CurrentLanguage switch
    {
        AppLanguage.KO => "마커 라벨 표시",
        AppLanguage.JA => "マーカーラベル表示",
        _ => "Show Marker Labels"
    };

    // Tracker Tab
    public string TrackerStatus => CurrentLanguage switch
    {
        AppLanguage.KO => "트래커 상태",
        AppLanguage.JA => "トラッカー状態",
        _ => "Tracker Status"
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

    public string NoFolderSelected => CurrentLanguage switch
    {
        AppLanguage.KO => "폴더 미선택",
        AppLanguage.JA => "フォルダ未選択",
        _ => "No folder selected"
    };

    public string Folder => CurrentLanguage switch
    {
        AppLanguage.KO => "폴더",
        AppLanguage.JA => "フォルダ",
        _ => "Folder"
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

    public string SelectScreenshotFolder => CurrentLanguage switch
    {
        AppLanguage.KO => "스크린샷 폴더 선택",
        AppLanguage.JA => "スクリーンショットフォルダ選択",
        _ => "Select Screenshot Folder"
    };

    public string OpenFolder => CurrentLanguage switch
    {
        AppLanguage.KO => "폴더 열기",
        AppLanguage.JA => "フォルダを開く",
        _ => "Open Folder"
    };

    public string StartStopTracking => CurrentLanguage switch
    {
        AppLanguage.KO => "트래킹 시작/중지",
        AppLanguage.JA => "トラッキング開始/停止",
        _ => "Start/Stop Tracking"
    };

    public string ClearPath => CurrentLanguage switch
    {
        AppLanguage.KO => "경로 초기화",
        AppLanguage.JA => "経路クリア",
        _ => "Clear Path"
    };

    public string PathSettings => CurrentLanguage switch
    {
        AppLanguage.KO => "경로 설정",
        AppLanguage.JA => "経路設定",
        _ => "Path Settings"
    };

    public string PathColor => CurrentLanguage switch
    {
        AppLanguage.KO => "경로 색상",
        AppLanguage.JA => "経路色",
        _ => "Path Color"
    };

    public string PathThickness => CurrentLanguage switch
    {
        AppLanguage.KO => "경로 두께",
        AppLanguage.JA => "経路太さ",
        _ => "Path Thickness"
    };

    public string Automation => CurrentLanguage switch
    {
        AppLanguage.KO => "자동화",
        AppLanguage.JA => "自動化",
        _ => "Automation"
    };

    public string AutoTrackOnMapLoad => CurrentLanguage switch
    {
        AppLanguage.KO => "맵 로드시 자동 추적",
        AppLanguage.JA => "マップ読み込み時に自動追跡",
        _ => "Auto Track on Map Load"
    };

    // Shortcuts Tab
    public string MapControls => CurrentLanguage switch
    {
        AppLanguage.KO => "맵 조작",
        AppLanguage.JA => "マップ操作",
        _ => "Map Controls"
    };

    public string ZoomInOut => CurrentLanguage switch
    {
        AppLanguage.KO => "확대/축소",
        AppLanguage.JA => "拡大/縮小",
        _ => "Zoom In/Out"
    };

    public string PanMap => CurrentLanguage switch
    {
        AppLanguage.KO => "맵 이동",
        AppLanguage.JA => "マップ移動",
        _ => "Pan Map"
    };

    public string LayerToggle => CurrentLanguage switch
    {
        AppLanguage.KO => "레이어 토글",
        AppLanguage.JA => "レイヤー切替",
        _ => "Layer Toggle"
    };

    public string ShowHideExtracts => CurrentLanguage switch
    {
        AppLanguage.KO => "탈출구 표시/숨김",
        AppLanguage.JA => "脱出口表示/非表示",
        _ => "Show/Hide Extracts"
    };

    public string ShowHideTransit => CurrentLanguage switch
    {
        AppLanguage.KO => "환승 표시/숨김",
        AppLanguage.JA => "乗り換え表示/非表示",
        _ => "Show/Hide Transit"
    };

    public string ShowHideQuests => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 표시/숨김",
        AppLanguage.JA => "クエスト表示/非表示",
        _ => "Show/Hide Quests"
    };

    public string Panel => CurrentLanguage switch
    {
        AppLanguage.KO => "패널",
        AppLanguage.JA => "パネル",
        _ => "Panel"
    };

    public string QuestPanel => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 패널",
        AppLanguage.JA => "クエストパネル",
        _ => "Quest Panel"
    };

    public string FloorChange => CurrentLanguage switch
    {
        AppLanguage.KO => "층 변경 (다층맵)",
        AppLanguage.JA => "階層変更 (多層マップ)",
        _ => "Floor Change (Multi-floor)"
    };

    // Footer
    public string ResetAll => CurrentLanguage switch
    {
        AppLanguage.KO => "초기화",
        AppLanguage.JA => "リセット",
        _ => "Reset"
    };

    public string ResetAllSettings => CurrentLanguage switch
    {
        AppLanguage.KO => "모든 설정 초기화",
        AppLanguage.JA => "すべての設定をリセット",
        _ => "Reset All Settings"
    };

    #endregion

    #region Map Page - Status Bar

    public string SelectMap => CurrentLanguage switch
    {
        AppLanguage.KO => "맵 선택",
        AppLanguage.JA => "マップ選択",
        _ => "Select Map"
    };

    public string CopyCoordinates => CurrentLanguage switch
    {
        AppLanguage.KO => "좌표 복사",
        AppLanguage.JA => "座標コピー",
        _ => "Copy Coordinates"
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

    #endregion
}
