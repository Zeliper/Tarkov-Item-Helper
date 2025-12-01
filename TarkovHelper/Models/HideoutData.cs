using System.Text.Json.Serialization;

namespace TarkovHelper.Models;

/// <summary>
/// Hideout 스테이션 데이터 (영어/한글 이름 포함)
/// </summary>
public class HideoutData
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

    [JsonPropertyName("imageLink")]
    public string? ImageLink { get; set; }

    [JsonPropertyName("levels")]
    public List<HideoutLevel> Levels { get; set; } = [];
}

/// <summary>
/// Hideout 레벨 데이터
/// </summary>
public class HideoutLevel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("constructionTime")]
    public int ConstructionTime { get; set; }

    [JsonPropertyName("descriptionEn")]
    public string DescriptionEn { get; set; } = string.Empty;

    [JsonPropertyName("descriptionKo")]
    public string DescriptionKo { get; set; } = string.Empty;

    [JsonPropertyName("descriptionJa")]
    public string DescriptionJa { get; set; } = string.Empty;

    [JsonPropertyName("itemRequirements")]
    public List<HideoutItemRequirement> ItemRequirements { get; set; } = [];

    [JsonPropertyName("stationLevelRequirements")]
    public List<HideoutStationRequirement> StationLevelRequirements { get; set; } = [];

    [JsonPropertyName("traderRequirements")]
    public List<HideoutTraderRequirement> TraderRequirements { get; set; } = [];

    [JsonPropertyName("skillRequirements")]
    public List<HideoutSkillRequirement> SkillRequirements { get; set; } = [];
}

/// <summary>
/// Hideout 아이템 요구사항
/// </summary>
public class HideoutItemRequirement
{
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    [JsonPropertyName("itemNameEn")]
    public string ItemNameEn { get; set; } = string.Empty;

    [JsonPropertyName("itemNameKo")]
    public string ItemNameKo { get; set; } = string.Empty;

    [JsonPropertyName("itemNameJa")]
    public string ItemNameJa { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>
    /// attributes에서 추출한 FIR 요구사항 여부
    /// </summary>
    [JsonPropertyName("foundInRaid")]
    public bool FoundInRaid { get; set; }
}

/// <summary>
/// Hideout 스테이션 레벨 요구사항
/// </summary>
public class HideoutStationRequirement
{
    [JsonPropertyName("stationId")]
    public string StationId { get; set; } = string.Empty;

    [JsonPropertyName("stationNameEn")]
    public string StationNameEn { get; set; } = string.Empty;

    [JsonPropertyName("stationNameKo")]
    public string StationNameKo { get; set; } = string.Empty;

    [JsonPropertyName("stationNameJa")]
    public string StationNameJa { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }
}

/// <summary>
/// Hideout 트레이더 요구사항
/// </summary>
public class HideoutTraderRequirement
{
    [JsonPropertyName("traderId")]
    public string TraderId { get; set; } = string.Empty;

    [JsonPropertyName("traderNameEn")]
    public string TraderNameEn { get; set; } = string.Empty;

    [JsonPropertyName("traderNameKo")]
    public string TraderNameKo { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }
}

/// <summary>
/// Hideout 스킬 요구사항
/// </summary>
public class HideoutSkillRequirement
{
    [JsonPropertyName("skillNameEn")]
    public string SkillNameEn { get; set; } = string.Empty;

    [JsonPropertyName("skillNameKo")]
    public string SkillNameKo { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; }
}

/// <summary>
/// Hideout 데이터셋
/// </summary>
public class HideoutDataset
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("hideouts")]
    public List<HideoutData> Hideouts { get; set; } = [];
}
