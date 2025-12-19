namespace TarkovHelper.Services;

/// <summary>
/// Items page specific localization strings.
/// </summary>
public partial class LocalizationService
{
    #region Items Page - Filter Labels

    public string ItemsSearchPlaceholder => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 검색...",
        AppLanguage.JA => "アイテムを検索...",
        _ => "Search items..."
    };

    public string ItemsFilterAll => CurrentLanguage switch
    {
        AppLanguage.KO => "전체",
        AppLanguage.JA => "すべて",
        _ => "All"
    };

    public string ItemsFilterQuest => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        _ => "Quest"
    };

    public string ItemsFilterHideout => CurrentLanguage switch
    {
        AppLanguage.KO => "은신처",
        AppLanguage.JA => "ハイドアウト",
        _ => "Hideout"
    };

    public string ItemsFilterAllCategories => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 카테고리",
        AppLanguage.JA => "全カテゴリ",
        _ => "All Categories"
    };

    public string ItemsFilterAllStatus => CurrentLanguage switch
    {
        AppLanguage.KO => "전체 상태",
        AppLanguage.JA => "全ステータス",
        _ => "All Status"
    };

    public string ItemsFilterNotStarted => CurrentLanguage switch
    {
        AppLanguage.KO => "미시작",
        AppLanguage.JA => "未開始",
        _ => "Not Started"
    };

    public string ItemsFilterInProgress => CurrentLanguage switch
    {
        AppLanguage.KO => "진행 중",
        AppLanguage.JA => "進行中",
        _ => "In Progress"
    };

    public string ItemsFilterFulfilled => CurrentLanguage switch
    {
        AppLanguage.KO => "완료",
        AppLanguage.JA => "完了",
        _ => "Fulfilled"
    };

    public string ItemsFilterFirOnly => CurrentLanguage switch
    {
        AppLanguage.KO => "FIR만",
        AppLanguage.JA => "FIRのみ",
        _ => "FIR Only"
    };

    public string ItemsFilterHideFulfilled => CurrentLanguage switch
    {
        AppLanguage.KO => "완료 숨기기",
        AppLanguage.JA => "完了を非表示",
        _ => "Hide Fulfilled"
    };

    public string ItemsSortName => CurrentLanguage switch
    {
        AppLanguage.KO => "이름",
        AppLanguage.JA => "名前",
        _ => "Name"
    };

    public string ItemsSortTotalCount => CurrentLanguage switch
    {
        AppLanguage.KO => "총 수량",
        AppLanguage.JA => "合計数",
        _ => "Total Count"
    };

    public string ItemsSortQuestCount => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 수량",
        AppLanguage.JA => "クエスト数",
        _ => "Quest Count"
    };

    public string ItemsSortProgress => CurrentLanguage switch
    {
        AppLanguage.KO => "진행도",
        AppLanguage.JA => "進捗",
        _ => "Progress"
    };

    #endregion

    #region Items Page - Column Headers

    public string ItemsHeaderItemName => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 이름",
        AppLanguage.JA => "アイテム名",
        _ => "Item Name"
    };

    public string ItemsHeaderQuest => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트",
        AppLanguage.JA => "クエスト",
        _ => "Quest"
    };

    public string ItemsHeaderHideout => CurrentLanguage switch
    {
        AppLanguage.KO => "은신처",
        AppLanguage.JA => "ハイドアウト",
        _ => "Hideout"
    };

    public string ItemsHeaderTotal => CurrentLanguage switch
    {
        AppLanguage.KO => "합계",
        AppLanguage.JA => "合計",
        _ => "Total"
    };

    public string ItemsHeaderNeed => CurrentLanguage switch
    {
        AppLanguage.KO => "필요",
        AppLanguage.JA => "必要",
        _ => "Need"
    };

    public string ItemsHeaderOwned => CurrentLanguage switch
    {
        AppLanguage.KO => "보유:",
        AppLanguage.JA => "所持:",
        _ => "Owned:"
    };

    #endregion

    #region Items Page - Detail Panel

    public string ItemsSelectItem => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템을 선택하면 상세 정보가 표시됩니다",
        AppLanguage.JA => "アイテムを選択すると詳細が表示されます",
        _ => "Select an item to view details"
    };

    public string ItemsOpenWiki => CurrentLanguage switch
    {
        AppLanguage.KO => "위키 열기",
        AppLanguage.JA => "Wikiを開く",
        _ => "Open Wiki"
    };

    public string ItemsYourInventory => CurrentLanguage switch
    {
        AppLanguage.KO => "보유 아이템",
        AppLanguage.JA => "所持アイテム",
        _ => "Your Inventory"
    };

    public string ItemsProgress => CurrentLanguage switch
    {
        AppLanguage.KO => "진행도",
        AppLanguage.JA => "進捗",
        _ => "Progress"
    };

    public string ItemsRequiredForQuests => CurrentLanguage switch
    {
        AppLanguage.KO => "퀘스트 필요 항목",
        AppLanguage.JA => "クエストで必要",
        _ => "Required for Quests"
    };

    public string ItemsRequiredForHideout => CurrentLanguage switch
    {
        AppLanguage.KO => "은신처 필요 항목",
        AppLanguage.JA => "ハイドアウトで必要",
        _ => "Required for Hideout"
    };

    public string ItemsLevel => CurrentLanguage switch
    {
        AppLanguage.KO => "레벨",
        AppLanguage.JA => "レベル",
        _ => "Level"
    };

    #endregion

    #region Items Page - Loading

    public string ItemsLoading => CurrentLanguage switch
    {
        AppLanguage.KO => "아이템 데이터 로딩 중...",
        AppLanguage.JA => "アイテムデータを読み込み中...",
        _ => "Loading items data..."
    };

    #endregion

    #region Item Categories - Parent Categories

    /// <summary>
    /// Get localized category name. Returns English name as fallback for unknown categories.
    /// </summary>
    public string GetCategoryName(string categoryKey)
    {
        return CurrentLanguage switch
        {
            AppLanguage.KO => GetCategoryNameKO(categoryKey),
            AppLanguage.JA => GetCategoryNameJA(categoryKey),
            _ => categoryKey // English is the key itself
        };
    }

    private static string GetCategoryNameKO(string key) => key switch
    {
        "All Categories" => "전체 카테고리",
        "Provisions" => "식량",
        "Medical" => "의료품",
        "Gear" => "장비",
        "Barter" => "물물교환",
        "Info & Keys" => "정보 & 열쇠",
        "Containers" => "컨테이너",
        "Money" => "화폐",
        "Ammo" => "탄약",
        "Weapon Mods" => "무기 부품",
        "Optics" => "광학장비",
        "Tactical" => "전술장비",
        "Helmet Mods" => "헬멧 부품",
        "Weapons" => "무기",
        "Quest Items" => "퀘스트 아이템",
        "Misc" => "기타",
        "Other" => "기타",
        _ => key // Fallback to English
    };

    private static string GetCategoryNameJA(string key) => key switch
    {
        "All Categories" => "全カテゴリ",
        "Provisions" => "食料品",
        "Medical" => "医療品",
        "Gear" => "装備",
        "Barter" => "物々交換",
        "Info & Keys" => "情報 & 鍵",
        "Containers" => "コンテナ",
        "Money" => "通貨",
        "Ammo" => "弾薬",
        "Weapon Mods" => "武器パーツ",
        "Optics" => "光学機器",
        "Tactical" => "タクティカル",
        "Helmet Mods" => "ヘルメットパーツ",
        "Weapons" => "武器",
        "Quest Items" => "クエストアイテム",
        "Misc" => "その他",
        "Other" => "その他",
        _ => key // Fallback to English
    };

    #endregion
}
