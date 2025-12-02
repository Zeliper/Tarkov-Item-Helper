using System.Text.Json.Serialization;

namespace TarkovHelper.Models
{
    /// <summary>
    /// Map data from tarkov.dev API with multilingual support
    /// </summary>
    public class TarkovMap
    {
        /// <summary>
        /// tarkov.dev map ID
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// English map name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Korean map name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        /// <summary>
        /// Japanese map name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        /// <summary>
        /// Normalized name (URL-friendly format from API)
        /// </summary>
        [JsonPropertyName("normalizedName")]
        public string NormalizedName { get; set; } = string.Empty;

        /// <summary>
        /// Wiki link for the map
        /// </summary>
        [JsonPropertyName("wiki")]
        public string? Wiki { get; set; }
    }
}
