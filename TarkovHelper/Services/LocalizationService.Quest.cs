namespace TarkovHelper.Services;

/// <summary>
/// Quest-related localization strings for LocalizationService.
/// Includes: In-Progress Quest Input, Quest Recommendations, etc.
/// </summary>
public partial class LocalizationService
{
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
}
