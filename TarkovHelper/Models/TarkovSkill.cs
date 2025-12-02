using System.Text.Json.Serialization;

namespace TarkovHelper.Models
{
    /// <summary>
    /// Skill data from tarkov.dev API with multilingual support
    /// </summary>
    public class TarkovSkill
    {
        /// <summary>
        /// tarkov.dev skill ID (e.g., "Sniper", "Health")
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// English skill name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Korean skill name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        /// <summary>
        /// Japanese skill name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        /// <summary>
        /// Normalized name for matching (generated from English name)
        /// </summary>
        [JsonPropertyName("normalizedName")]
        public string NormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// Skill icon URL
        /// </summary>
        [JsonPropertyName("imageLink")]
        public string? ImageLink { get; set; }
    }
}
