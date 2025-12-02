using System.Text.Json.Serialization;

namespace TarkovHelper.Models
{
    /// <summary>
    /// Trader data from tarkov.dev API with multilingual support
    /// </summary>
    public class TarkovTrader
    {
        /// <summary>
        /// tarkov.dev trader ID
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// English trader name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Korean trader name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        /// <summary>
        /// Japanese trader name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        /// <summary>
        /// Normalized name (URL-friendly format from API)
        /// </summary>
        [JsonPropertyName("normalizedName")]
        public string NormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// Trader image URL
        /// </summary>
        [JsonPropertyName("imageLink")]
        public string? ImageLink { get; set; }

        /// <summary>
        /// Trader image URL (4x resolution)
        /// </summary>
        [JsonPropertyName("image4xLink")]
        public string? Image4xLink { get; set; }
    }
}
