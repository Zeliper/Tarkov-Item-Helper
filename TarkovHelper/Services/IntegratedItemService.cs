using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// 퀘스트 + 은신처 아이템 요구사항 통합 서비스
/// </summary>
public sealed class IntegratedItemService
{
    private static IntegratedItemService? _instance;
    public static IntegratedItemService Instance => _instance ??= new IntegratedItemService();

    // 의존 서비스
    private readonly QuestDbService _questDb;
    private readonly HideoutDbService _hideoutDb;
    private readonly ItemDbService _itemDb;
    private readonly QuestProgressService _questProgress;
    private readonly HideoutProgressService _hideoutProgress;
    private readonly ItemInventoryService _inventory;

    // 캐시
    private Dictionary<string, IntegratedItemRequirement>? _cache;
    private bool _isInitialized;

    /// <summary>
    /// 데이터 변경 시 발생하는 이벤트
    /// </summary>
    public event Action? DataChanged;

    private IntegratedItemService()
    {
        _questDb = QuestDbService.Instance;
        _hideoutDb = HideoutDbService.Instance;
        _itemDb = ItemDbService.Instance;
        _questProgress = QuestProgressService.Instance;
        _hideoutProgress = HideoutProgressService.Instance;
        _inventory = ItemInventoryService.Instance;

        // 이벤트 구독
        _questProgress.ProgressChanged += OnProgressChanged;
        _hideoutProgress.ProgressChanged += OnProgressChanged;
        _inventory.InventoryChanged += OnInventoryChanged;
    }

    private void OnProgressChanged(object? sender, EventArgs e) => Refresh();
    private void OnInventoryChanged(object? sender, EventArgs e)
    {
        if (_cache != null)
        {
            UpdateInventoryInfo();
            DataChanged?.Invoke();
        }
    }

    /// <summary>
    /// 서비스 초기화
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // 의존 서비스 초기화
        if (!_questDb.IsLoaded)
            await _questDb.LoadQuestsAsync();

        if (!_hideoutDb.IsLoaded)
            await _hideoutDb.LoadStationsAsync();

        if (!_itemDb.IsLoaded)
            await _itemDb.LoadItemsAsync();

        BuildCache();
        _isInitialized = true;
    }

    /// <summary>
    /// 캐시 다시 빌드
    /// </summary>
    public void Refresh()
    {
        BuildCache();
        DataChanged?.Invoke();
    }

    /// <summary>
    /// 모든 통합 아이템 요구사항 조회
    /// </summary>
    public List<IntegratedItemRequirement> GetAllRequirements()
    {
        EnsureInitialized();
        return _cache!.Values.ToList();
    }

    /// <summary>
    /// 부족한 아이템만 조회
    /// </summary>
    public List<IntegratedItemRequirement> GetShortageItems()
    {
        return GetAllRequirements()
            .Where(r => r.Shortage > 0)
            .OrderByDescending(r => r.Shortage)
            .ToList();
    }

    /// <summary>
    /// 퀘스트 전용 아이템 조회
    /// </summary>
    public List<IntegratedItemRequirement> GetQuestOnlyItems()
    {
        return GetAllRequirements()
            .Where(r => r.IsQuestOnly)
            .ToList();
    }

    /// <summary>
    /// 은신처 전용 아이템 조회
    /// </summary>
    public List<IntegratedItemRequirement> GetHideoutOnlyItems()
    {
        return GetAllRequirements()
            .Where(r => r.IsHideoutOnly)
            .ToList();
    }

    /// <summary>
    /// 둘 다 필요한 아이템 조회
    /// </summary>
    public List<IntegratedItemRequirement> GetBothRequiredItems()
    {
        return GetAllRequirements()
            .Where(r => r.IsBothRequired)
            .ToList();
    }

    /// <summary>
    /// 특정 아이템 요구사항 조회
    /// </summary>
    public IntegratedItemRequirement? GetItemRequirement(string itemNormalizedName)
    {
        EnsureInitialized();
        return _cache!.TryGetValue(itemNormalizedName, out var req) ? req : null;
    }

    /// <summary>
    /// 특정 퀘스트의 아이템 요구사항 조회
    /// </summary>
    public List<IntegratedItemRequirement> GetItemsForQuest(string questNormalizedName)
    {
        return GetAllRequirements()
            .Where(r => r.QuestSources.Any(q =>
                q.QuestNormalizedName.Equals(questNormalizedName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// 특정 은신처 모듈의 아이템 요구사항 조회
    /// </summary>
    public List<IntegratedItemRequirement> GetItemsForHideout(string stationId, int level)
    {
        return GetAllRequirements()
            .Where(r => r.HideoutSources.Any(h =>
                h.StationId.Equals(stationId, StringComparison.OrdinalIgnoreCase) && h.Level == level))
            .ToList();
    }

    /// <summary>
    /// 통계 정보 조회
    /// </summary>
    public IntegratedItemStats GetStats()
    {
        var all = GetAllRequirements();
        var shortage = all.Where(r => r.Shortage > 0).ToList();

        return new IntegratedItemStats
        {
            TotalUniqueItems = all.Count,
            TotalRequired = all.Sum(r => r.TotalRequired),
            TotalOwned = all.Sum(r => r.TotalOwned),
            TotalShortage = shortage.Sum(r => r.Shortage),
            ShortageItemCount = shortage.Count,
            QuestOnlyCount = all.Count(r => r.IsQuestOnly),
            HideoutOnlyCount = all.Count(r => r.IsHideoutOnly),
            BothRequiredCount = all.Count(r => r.IsBothRequired),
            FulfilledCount = all.Count(r => r.IsFulfilled)
        };
    }

    /// <summary>
    /// 캐시 빌드
    /// </summary>
    private void BuildCache()
    {
        _cache = new Dictionary<string, IntegratedItemRequirement>(StringComparer.OrdinalIgnoreCase);

        // 아이템 정보 룩업
        var itemLookup = _itemDb.GetItemLookup();

        // 1. 퀘스트 아이템 요구사항 수집
        foreach (var quest in _questDb.AllQuests)
        {
            if (quest.RequiredItems == null) continue;

            // 완료된 퀘스트는 건너뛰기
            var isCompleted = _questProgress.IsQuestCompleted(quest.NormalizedName ?? "");
            if (isCompleted) continue;

            foreach (var reqItem in quest.RequiredItems)
            {
                var normalizedName = reqItem.ItemNormalizedName;
                if (string.IsNullOrEmpty(normalizedName)) continue;

                var integrated = GetOrCreateIntegrated(normalizedName, itemLookup);

                integrated.QuestRequired += reqItem.Amount;
                if (reqItem.FoundInRaid)
                    integrated.QuestRequiredFir += reqItem.Amount;

                integrated.QuestSources.Add(new QuestItemSource
                {
                    QuestId = quest.Ids?.FirstOrDefault() ?? "",
                    QuestNormalizedName = quest.NormalizedName ?? "",
                    QuestName = quest.Name,
                    QuestNameKo = quest.NameKo,
                    QuestNameJa = quest.NameJa,
                    TraderName = quest.Trader ?? "",
                    RequiredCount = reqItem.Amount,
                    RequiresFir = reqItem.FoundInRaid,
                    IsQuestCompleted = isCompleted
                });
            }
        }

        // 2. 은신처 아이템 요구사항 수집
        foreach (var station in _hideoutDb.AllStations)
        {
            var currentLevel = _hideoutProgress.GetCurrentLevel(station.NormalizedName);

            foreach (var level in station.Levels)
            {
                // 이미 건설된 레벨은 건너뛰기
                if (level.Level <= currentLevel) continue;

                foreach (var reqItem in level.ItemRequirements)
                {
                    var normalizedName = reqItem.ItemNormalizedName;
                    if (string.IsNullOrEmpty(normalizedName))
                    {
                        // ItemNormalizedName이 없으면 ItemId로 조회 시도
                        if (!string.IsNullOrEmpty(reqItem.ItemId) && itemLookup.TryGetValue(reqItem.ItemId, out var item))
                        {
                            normalizedName = item.NormalizedName;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    var integrated = GetOrCreateIntegrated(normalizedName, itemLookup);

                    integrated.HideoutRequired += reqItem.Count;
                    if (reqItem.FoundInRaid)
                        integrated.HideoutRequiredFir += reqItem.Count;

                    integrated.HideoutSources.Add(new HideoutItemSource
                    {
                        StationId = station.Id,
                        StationName = station.Name,
                        StationNameKo = station.NameKo,
                        StationNameJa = station.NameJa,
                        Level = level.Level,
                        RequiredCount = reqItem.Count,
                        RequiresFir = reqItem.FoundInRaid,
                        IsBuilt = level.Level <= currentLevel
                    });
                }
            }
        }

        // 3. 인벤토리 정보 반영
        UpdateInventoryInfo();
    }

    /// <summary>
    /// 인벤토리 정보 업데이트
    /// </summary>
    private void UpdateInventoryInfo()
    {
        if (_cache == null) return;

        foreach (var (normalizedName, integrated) in _cache)
        {
            integrated.OwnedFir = _inventory.GetFirQuantity(normalizedName);
            integrated.OwnedNonFir = _inventory.GetNonFirQuantity(normalizedName);

            // 퀘스트 충족 상태 업데이트
            UpdateQuestFulfillment(integrated);

            // 은신처 충족 상태 업데이트
            UpdateHideoutFulfillment(integrated);
        }
    }

    /// <summary>
    /// 퀘스트 충족 상태 업데이트
    /// </summary>
    private void UpdateQuestFulfillment(IntegratedItemRequirement integrated)
    {
        var availableFir = integrated.OwnedFir;
        var availableNonFir = integrated.OwnedNonFir;

        foreach (var source in integrated.QuestSources)
        {
            if (source.RequiresFir)
            {
                source.IsFulfilled = availableFir >= source.RequiredCount;
                if (source.IsFulfilled)
                    availableFir -= source.RequiredCount;
            }
            else
            {
                var total = availableFir + availableNonFir;
                source.IsFulfilled = total >= source.RequiredCount;
                if (source.IsFulfilled)
                {
                    // 일반 먼저 사용
                    if (availableNonFir >= source.RequiredCount)
                        availableNonFir -= source.RequiredCount;
                    else
                    {
                        var fromFir = source.RequiredCount - availableNonFir;
                        availableNonFir = 0;
                        availableFir -= fromFir;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 은신처 충족 상태 업데이트
    /// </summary>
    private void UpdateHideoutFulfillment(IntegratedItemRequirement integrated)
    {
        var availableFir = integrated.OwnedFir;
        var availableNonFir = integrated.OwnedNonFir;

        // 퀘스트에서 사용된 양 계산
        foreach (var quest in integrated.QuestSources.Where(q => q.IsFulfilled))
        {
            if (quest.RequiresFir)
                availableFir -= quest.RequiredCount;
            else
                availableNonFir -= quest.RequiredCount;
        }

        availableFir = Math.Max(0, availableFir);
        availableNonFir = Math.Max(0, availableNonFir);

        foreach (var source in integrated.HideoutSources)
        {
            if (source.RequiresFir)
            {
                source.IsFulfilled = availableFir >= source.RequiredCount;
            }
            else
            {
                var total = availableFir + availableNonFir;
                source.IsFulfilled = total >= source.RequiredCount;
            }
        }
    }

    /// <summary>
    /// 통합 아이템 가져오기 또는 생성
    /// </summary>
    private IntegratedItemRequirement GetOrCreateIntegrated(
        string normalizedName,
        Dictionary<string, TarkovItem> itemLookup)
    {
        if (_cache!.TryGetValue(normalizedName, out var existing))
            return existing;

        var integrated = new IntegratedItemRequirement
        {
            ItemNormalizedName = normalizedName
        };

        // 아이템 정보 채우기
        if (itemLookup.TryGetValue(normalizedName, out var item))
        {
            integrated.ItemId = item.Id;
            integrated.ItemName = item.Name;
            integrated.ItemNameKo = item.NameKo;
            integrated.ItemNameJa = item.NameJa;
            integrated.IconLink = item.IconLink;
            integrated.WikiLink = item.WikiLink;
        }
        else
        {
            integrated.ItemName = normalizedName;
        }

        _cache[normalizedName] = integrated;
        return integrated;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized || _cache == null)
        {
            throw new InvalidOperationException("IntegratedItemService not initialized. Call InitializeAsync() first.");
        }
    }
}

/// <summary>
/// 통합 아이템 통계
/// </summary>
public class IntegratedItemStats
{
    /// <summary>
    /// 총 고유 아이템 수
    /// </summary>
    public int TotalUniqueItems { get; set; }

    /// <summary>
    /// 총 필요량
    /// </summary>
    public int TotalRequired { get; set; }

    /// <summary>
    /// 총 보유량
    /// </summary>
    public int TotalOwned { get; set; }

    /// <summary>
    /// 총 부족량
    /// </summary>
    public int TotalShortage { get; set; }

    /// <summary>
    /// 부족한 아이템 종류 수
    /// </summary>
    public int ShortageItemCount { get; set; }

    /// <summary>
    /// 퀘스트 전용 아이템 수
    /// </summary>
    public int QuestOnlyCount { get; set; }

    /// <summary>
    /// 은신처 전용 아이템 수
    /// </summary>
    public int HideoutOnlyCount { get; set; }

    /// <summary>
    /// 둘 다 필요한 아이템 수
    /// </summary>
    public int BothRequiredCount { get; set; }

    /// <summary>
    /// 충족된 아이템 수
    /// </summary>
    public int FulfilledCount { get; set; }

    /// <summary>
    /// 전체 진행률
    /// </summary>
    public double OverallProgress => TotalRequired > 0 ? (double)TotalOwned / TotalRequired : 1.0;
}
