using System.ComponentModel;

namespace TarkovHelper.Models;

/// <summary>
/// 퀘스트 + 은신처 통합 아이템 요구사항
/// </summary>
public class IntegratedItemRequirement : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 아이템 ID (tarkov.dev API ID)
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// 아이템 정규화된 이름
    /// </summary>
    public string ItemNormalizedName { get; set; } = string.Empty;

    /// <summary>
    /// 아이템 이름 (영어)
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// 아이템 이름 (한국어)
    /// </summary>
    public string? ItemNameKo { get; set; }

    /// <summary>
    /// 아이템 이름 (일본어)
    /// </summary>
    public string? ItemNameJa { get; set; }

    /// <summary>
    /// 아이템 아이콘 URL
    /// </summary>
    public string? IconLink { get; set; }

    /// <summary>
    /// 위키 링크
    /// </summary>
    public string? WikiLink { get; set; }

    // ===== 보유 현황 =====

    private int _ownedFir;
    /// <summary>
    /// 보유 중인 FIR 아이템 수량
    /// </summary>
    public int OwnedFir
    {
        get => _ownedFir;
        set
        {
            if (_ownedFir != value)
            {
                _ownedFir = value;
                OnPropertyChanged(nameof(OwnedFir));
                OnPropertyChanged(nameof(TotalOwned));
                OnPropertyChanged(nameof(Shortage));
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(IsFulfilled));
            }
        }
    }

    private int _ownedNonFir;
    /// <summary>
    /// 보유 중인 일반 아이템 수량
    /// </summary>
    public int OwnedNonFir
    {
        get => _ownedNonFir;
        set
        {
            if (_ownedNonFir != value)
            {
                _ownedNonFir = value;
                OnPropertyChanged(nameof(OwnedNonFir));
                OnPropertyChanged(nameof(TotalOwned));
                OnPropertyChanged(nameof(Shortage));
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(IsFulfilled));
            }
        }
    }

    /// <summary>
    /// 총 보유량
    /// </summary>
    public int TotalOwned => OwnedFir + OwnedNonFir;

    // ===== 퀘스트 필요 현황 =====

    /// <summary>
    /// 퀘스트에서 필요한 총 수량
    /// </summary>
    public int QuestRequired { get; set; }

    /// <summary>
    /// 퀘스트에서 필요한 FIR 수량
    /// </summary>
    public int QuestRequiredFir { get; set; }

    /// <summary>
    /// 퀘스트 출처 목록
    /// </summary>
    public List<QuestItemSource> QuestSources { get; set; } = new();

    // ===== 은신처 필요 현황 =====

    /// <summary>
    /// 은신처에서 필요한 총 수량
    /// </summary>
    public int HideoutRequired { get; set; }

    /// <summary>
    /// 은신처에서 필요한 FIR 수량
    /// </summary>
    public int HideoutRequiredFir { get; set; }

    /// <summary>
    /// 은신처 출처 목록
    /// </summary>
    public List<HideoutItemSource> HideoutSources { get; set; } = new();

    // ===== 계산된 속성 =====

    /// <summary>
    /// 총 필요량 (퀘스트 + 은신처)
    /// </summary>
    public int TotalRequired => QuestRequired + HideoutRequired;

    /// <summary>
    /// 총 FIR 필요량
    /// </summary>
    public int TotalRequiredFir => QuestRequiredFir + HideoutRequiredFir;

    /// <summary>
    /// 부족량
    /// </summary>
    public int Shortage => Math.Max(0, TotalRequired - TotalOwned);

    /// <summary>
    /// FIR 부족량
    /// </summary>
    public int FirShortage => Math.Max(0, TotalRequiredFir - OwnedFir);

    /// <summary>
    /// 진행률 (0.0 ~ 1.0)
    /// </summary>
    public double Progress => TotalRequired > 0 ? Math.Min(1.0, (double)TotalOwned / TotalRequired) : 1.0;

    /// <summary>
    /// 충족 여부
    /// </summary>
    public bool IsFulfilled => TotalOwned >= TotalRequired;

    /// <summary>
    /// 퀘스트에만 필요한지 여부
    /// </summary>
    public bool IsQuestOnly => QuestRequired > 0 && HideoutRequired == 0;

    /// <summary>
    /// 은신처에만 필요한지 여부
    /// </summary>
    public bool IsHideoutOnly => HideoutRequired > 0 && QuestRequired == 0;

    /// <summary>
    /// 둘 다 필요한지 여부
    /// </summary>
    public bool IsBothRequired => QuestRequired > 0 && HideoutRequired > 0;

    /// <summary>
    /// 현재 언어에 맞는 아이템 이름
    /// </summary>
    public string GetLocalizedName(string language)
    {
        return language switch
        {
            "ko" => ItemNameKo ?? ItemName,
            "ja" => ItemNameJa ?? ItemName,
            _ => ItemName
        };
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// 퀘스트 아이템 출처 정보
/// </summary>
public class QuestItemSource
{
    /// <summary>
    /// 퀘스트 ID
    /// </summary>
    public string QuestId { get; set; } = string.Empty;

    /// <summary>
    /// 퀘스트 정규화된 이름
    /// </summary>
    public string QuestNormalizedName { get; set; } = string.Empty;

    /// <summary>
    /// 퀘스트 이름 (영어)
    /// </summary>
    public string QuestName { get; set; } = string.Empty;

    /// <summary>
    /// 퀘스트 이름 (한국어)
    /// </summary>
    public string? QuestNameKo { get; set; }

    /// <summary>
    /// 퀘스트 이름 (일본어)
    /// </summary>
    public string? QuestNameJa { get; set; }

    /// <summary>
    /// 트레이더 이름
    /// </summary>
    public string TraderName { get; set; } = string.Empty;

    /// <summary>
    /// 필요 수량
    /// </summary>
    public int RequiredCount { get; set; }

    /// <summary>
    /// FIR 필요 여부
    /// </summary>
    public bool RequiresFir { get; set; }

    /// <summary>
    /// 충족 여부
    /// </summary>
    public bool IsFulfilled { get; set; }

    /// <summary>
    /// 퀘스트 완료 여부
    /// </summary>
    public bool IsQuestCompleted { get; set; }

    /// <summary>
    /// 현재 언어에 맞는 퀘스트 이름
    /// </summary>
    public string GetLocalizedName(string language)
    {
        return language switch
        {
            "ko" => QuestNameKo ?? QuestName,
            "ja" => QuestNameJa ?? QuestName,
            _ => QuestName
        };
    }
}

/// <summary>
/// 은신처 아이템 출처 정보
/// </summary>
public class HideoutItemSource
{
    /// <summary>
    /// 스테이션 ID
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// 스테이션 이름 (영어)
    /// </summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>
    /// 스테이션 이름 (한국어)
    /// </summary>
    public string? StationNameKo { get; set; }

    /// <summary>
    /// 스테이션 이름 (일본어)
    /// </summary>
    public string? StationNameJa { get; set; }

    /// <summary>
    /// 필요 레벨
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 필요 수량
    /// </summary>
    public int RequiredCount { get; set; }

    /// <summary>
    /// FIR 필요 여부
    /// </summary>
    public bool RequiresFir { get; set; }

    /// <summary>
    /// 충족 여부
    /// </summary>
    public bool IsFulfilled { get; set; }

    /// <summary>
    /// 이미 건설 완료 여부
    /// </summary>
    public bool IsBuilt { get; set; }

    /// <summary>
    /// 표시용 문자열 (예: "Workbench Lv2")
    /// </summary>
    public string DisplayName => $"{StationName} Lv{Level}";

    /// <summary>
    /// 현재 언어에 맞는 스테이션 이름
    /// </summary>
    public string GetLocalizedName(string language)
    {
        var name = language switch
        {
            "ko" => StationNameKo ?? StationName,
            "ja" => StationNameJa ?? StationName,
            _ => StationName
        };
        return $"{name} Lv{Level}";
    }
}
