using System.Text.Json.Serialization;

namespace TarkovHelper.Models;

/// <summary>
/// 아이템 데이터 (영어/한글 이름 포함)
/// </summary>
public class ItemData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("nameEn")]
    public string NameEn { get; set; } = string.Empty;

    [JsonPropertyName("nameKo")]
    public string NameKo { get; set; } = string.Empty;

    [JsonPropertyName("nameJa")]
    public string NameJa { get; set; } = string.Empty;

    [JsonPropertyName("shortNameEn")]
    public string ShortNameEn { get; set; } = string.Empty;

    [JsonPropertyName("shortNameKo")]
    public string ShortNameKo { get; set; } = string.Empty;

    [JsonPropertyName("shortNameJa")]
    public string ShortNameJa { get; set; } = string.Empty;

    [JsonPropertyName("normalizedName")]
    public string NormalizedName { get; set; } = string.Empty;

    [JsonPropertyName("wikiLink")]
    public string? WikiLink { get; set; }

    [JsonPropertyName("iconLink")]
    public string? IconLink { get; set; }

    [JsonPropertyName("gridImageLink")]
    public string? GridImageLink { get; set; }

    [JsonPropertyName("basePrice")]
    public int BasePrice { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("types")]
    public List<string> Types { get; set; } = [];

    [JsonPropertyName("categoryName")]
    public string? CategoryName { get; set; }
}

/// <summary>
/// 아이템 데이터셋
/// </summary>
public class ItemDataset
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("generatedAt")]
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("items")]
    public List<ItemData> Items { get; set; } = [];
}
