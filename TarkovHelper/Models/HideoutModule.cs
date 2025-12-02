using System.Text.Json.Serialization;

namespace TarkovHelper.Models
{
    /// <summary>
    /// Hideout station/module from tarkov.dev API
    /// </summary>
    public class HideoutModule
    {
        /// <summary>
        /// Unique ID from tarkov.dev API
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// English name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Korean name (null if no translation)
        /// </summary>
        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        /// <summary>
        /// Japanese name (null if no translation)
        /// </summary>
        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        /// <summary>
        /// Normalized name (URL-friendly)
        /// </summary>
        [JsonPropertyName("normalizedName")]
        public string NormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// Image URL for the module icon
        /// </summary>
        [JsonPropertyName("imageLink")]
        public string? ImageLink { get; set; }

        /// <summary>
        /// Maximum level for this module
        /// </summary>
        [JsonIgnore]
        public int MaxLevel => Levels?.Count ?? 0;

        /// <summary>
        /// All levels for this module
        /// </summary>
        [JsonPropertyName("levels")]
        public List<HideoutLevel> Levels { get; set; } = new();
    }

    /// <summary>
    /// A specific level of a hideout module
    /// </summary>
    public class HideoutLevel
    {
        /// <summary>
        /// Level number (1, 2, 3, etc.)
        /// </summary>
        [JsonPropertyName("level")]
        public int Level { get; set; }

        /// <summary>
        /// Construction time in seconds
        /// </summary>
        [JsonPropertyName("constructionTime")]
        public int ConstructionTime { get; set; }

        /// <summary>
        /// Items required for this level
        /// </summary>
        [JsonPropertyName("itemRequirements")]
        public List<HideoutItemRequirement> ItemRequirements { get; set; } = new();

        /// <summary>
        /// Other hideout stations required
        /// </summary>
        [JsonPropertyName("stationLevelRequirements")]
        public List<HideoutStationRequirement> StationLevelRequirements { get; set; } = new();

        /// <summary>
        /// Trader levels required
        /// </summary>
        [JsonPropertyName("traderRequirements")]
        public List<HideoutTraderRequirement> TraderRequirements { get; set; } = new();

        /// <summary>
        /// Skills required
        /// </summary>
        [JsonPropertyName("skillRequirements")]
        public List<HideoutSkillRequirement> SkillRequirements { get; set; } = new();
    }

    /// <summary>
    /// Item requirement for hideout construction
    /// </summary>
    public class HideoutItemRequirement
    {
        /// <summary>
        /// Item ID
        /// </summary>
        [JsonPropertyName("itemId")]
        public string ItemId { get; set; } = string.Empty;

        /// <summary>
        /// Item name (English)
        /// </summary>
        [JsonPropertyName("itemName")]
        public string ItemName { get; set; } = string.Empty;

        /// <summary>
        /// Item name (Korean)
        /// </summary>
        [JsonPropertyName("itemNameKo")]
        public string? ItemNameKo { get; set; }

        /// <summary>
        /// Item name (Japanese)
        /// </summary>
        [JsonPropertyName("itemNameJa")]
        public string? ItemNameJa { get; set; }

        /// <summary>
        /// Item normalized name for matching
        /// </summary>
        [JsonPropertyName("itemNormalizedName")]
        public string ItemNormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// Icon URL for the item
        /// </summary>
        [JsonPropertyName("iconLink")]
        public string? IconLink { get; set; }

        /// <summary>
        /// Number of items required
        /// </summary>
        [JsonPropertyName("count")]
        public int Count { get; set; }

        /// <summary>
        /// Whether the item must be Found in Raid
        /// </summary>
        [JsonPropertyName("foundInRaid")]
        public bool FoundInRaid { get; set; }
    }

    /// <summary>
    /// Other hideout station requirement
    /// </summary>
    public class HideoutStationRequirement
    {
        /// <summary>
        /// Station ID
        /// </summary>
        [JsonPropertyName("stationId")]
        public string StationId { get; set; } = string.Empty;

        /// <summary>
        /// Station name (English)
        /// </summary>
        [JsonPropertyName("stationName")]
        public string StationName { get; set; } = string.Empty;

        /// <summary>
        /// Station name (Korean)
        /// </summary>
        [JsonPropertyName("stationNameKo")]
        public string? StationNameKo { get; set; }

        /// <summary>
        /// Station name (Japanese)
        /// </summary>
        [JsonPropertyName("stationNameJa")]
        public string? StationNameJa { get; set; }

        /// <summary>
        /// Required level of the station
        /// </summary>
        [JsonPropertyName("level")]
        public int Level { get; set; }
    }

    /// <summary>
    /// Trader requirement for hideout construction
    /// </summary>
    public class HideoutTraderRequirement
    {
        /// <summary>
        /// Trader ID
        /// </summary>
        [JsonPropertyName("traderId")]
        public string TraderId { get; set; } = string.Empty;

        /// <summary>
        /// Trader name (English)
        /// </summary>
        [JsonPropertyName("traderName")]
        public string TraderName { get; set; } = string.Empty;

        /// <summary>
        /// Trader name (Korean)
        /// </summary>
        [JsonPropertyName("traderNameKo")]
        public string? TraderNameKo { get; set; }

        /// <summary>
        /// Trader name (Japanese)
        /// </summary>
        [JsonPropertyName("traderNameJa")]
        public string? TraderNameJa { get; set; }

        /// <summary>
        /// Required loyalty level
        /// </summary>
        [JsonPropertyName("level")]
        public int Level { get; set; }
    }

    /// <summary>
    /// Skill requirement for hideout construction
    /// </summary>
    public class HideoutSkillRequirement
    {
        /// <summary>
        /// Skill name (English)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Skill name (Korean)
        /// </summary>
        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        /// <summary>
        /// Skill name (Japanese)
        /// </summary>
        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        /// <summary>
        /// Required skill level
        /// </summary>
        [JsonPropertyName("level")]
        public int Level { get; set; }
    }

    /// <summary>
    /// Hideout progress data for persistence
    /// </summary>
    public class HideoutProgress
    {
        /// <summary>
        /// Version for migration purposes
        /// </summary>
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        /// <summary>
        /// Last updated timestamp
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Module progress: module normalized name -> current level (0 = not built)
        /// </summary>
        [JsonPropertyName("modules")]
        public Dictionary<string, int> Modules { get; set; } = new();
    }
}
