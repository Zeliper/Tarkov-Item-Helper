using System.Text.Json.Serialization;

namespace TarkovHelper.Models
{
    /// <summary>
    /// Item data from tarkov.dev API with multilingual support
    /// </summary>
    public class TarkovItem
    {
        /// <summary>
        /// tarkov.dev item ID
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// English item name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Korean item name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        /// <summary>
        /// Japanese item name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        /// <summary>
        /// Short name (e.g., "M4A1" for Colt M4A1)
        /// </summary>
        [JsonPropertyName("shortName")]
        public string? ShortName { get; set; }

        /// <summary>
        /// Normalized name (URL-friendly format from API)
        /// </summary>
        [JsonPropertyName("normalizedName")]
        public string NormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// Small icon URL
        /// </summary>
        [JsonPropertyName("iconLink")]
        public string? IconLink { get; set; }

        /// <summary>
        /// Grid image URL (larger icon for inventory display)
        /// </summary>
        [JsonPropertyName("gridImageLink")]
        public string? GridImageLink { get; set; }

        /// <summary>
        /// Wiki page link
        /// </summary>
        [JsonPropertyName("wikiLink")]
        public string? WikiLink { get; set; }
    }
}
