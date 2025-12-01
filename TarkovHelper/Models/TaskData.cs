using System.Text.Json.Serialization;

namespace TarkovHelper.Models;

/// <summary>
/// 퀘스트 데이터 (영어/한글 이름 포함)
/// </summary>
public class TaskData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameKo")]
    public string NameKo { get; set; } = string.Empty;

    [JsonPropertyName("nameJa")]
    public string NameJa { get; set; } = string.Empty;

    [JsonPropertyName("normalizedName")]
    public string NormalizedName { get; set; } = string.Empty;

    [JsonPropertyName("traderName")]
    public string TraderName { get; set; } = string.Empty;

    [JsonPropertyName("minPlayerLevel")]
    public int? MinPlayerLevel { get; set; }

    [JsonPropertyName("experience")]
    public int Experience { get; set; }

    [JsonPropertyName("kappaRequired")]
    public bool KappaRequired { get; set; }

    [JsonPropertyName("lightkeeperRequired")]
    public bool LightkeeperRequired { get; set; }

    [JsonPropertyName("wikiLink")]
    public string? WikiLink { get; set; }

    /// <summary>
    /// 선행 퀘스트 ID 목록
    /// </summary>
    [JsonPropertyName("prerequisiteTaskIds")]
    public List<string> PrerequisiteTaskIds { get; set; } = [];

    /// <summary>
    /// 후행 퀘스트 ID 목록 (이 퀘스트를 완료해야 시작 가능한 퀘스트들)
    /// </summary>
    [JsonPropertyName("followUpTaskIds")]
    public List<string> FollowUpTaskIds { get; set; } = [];

    /// <summary>
    /// 중복 퀘스트 ID 목록 (같은 이름이지만 다른 ID를 가진 퀘스트들)
    /// 게임 로그에서 퀘스트 완료 이벤트 감지 시 이 ID들도 체크
    /// </summary>
    [JsonPropertyName("alternativeIds")]
    public List<string> AlternativeIds { get; set; } = [];

    /// <summary>
    /// 퀘스트 목표 목록
    /// </summary>
    [JsonPropertyName("objectives")]
    public List<TaskObjective> Objectives { get; set; } = [];

    /// <summary>
    /// 필요한 아이템 목록 (모든 objectives에서 추출, 중복 제거)
    /// </summary>
    [JsonIgnore]
    public IEnumerable<ObjectiveItem> RequiredItems => Objectives
        .Where(o => o.IsItemObjective)
        .SelectMany(o => o.Items)
        .GroupBy(i => i.ItemId)
        .Select(g => new ObjectiveItem
        {
            ItemId = g.Key,
            Count = g.Sum(x => x.Count),
            FoundInRaid = g.Any(x => x.FoundInRaid)
        });
}

/// <summary>
/// 전체 퀘스트 데이터셋
/// </summary>
public class TaskDataset
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("tasks")]
    public List<TaskData> Tasks { get; set; } = [];
}
