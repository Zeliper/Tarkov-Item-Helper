using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TarkovHelper.Models
{
    /// <summary>
    /// Task data merged from tarkov.dev API with multilingual support
    /// </summary>
    public class TarkovTask
    {
        /// <summary>
        /// tarkov.dev Task IDs (can be multiple for aliased/variant tasks)
        /// Null for wiki-only quests that don't exist in tarkov.dev API
        /// </summary>
        [JsonPropertyName("ids")]
        public List<string>? Ids { get; set; } = new();

        /// <summary>
        /// English task name
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Korean task name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameKo")]
        public string? NameKo { get; set; }

        /// <summary>
        /// Japanese task name (null if no translation available)
        /// </summary>
        [JsonPropertyName("nameJa")]
        public string? NameJa { get; set; }

        /// <summary>
        /// Whether this task is required for Kappa container
        /// </summary>
        [JsonPropertyName("reqKappa")]
        public bool ReqKappa { get; set; }

        /// <summary>
        /// Trader who gives this task
        /// </summary>
        [JsonPropertyName("trader")]
        public string Trader { get; set; } = string.Empty;

        /// <summary>
        /// Normalized name (URL-friendly format)
        /// Null for wiki-only quests that don't exist in tarkov.dev API
        /// </summary>
        [JsonPropertyName("normalizedName")]
        public string? NormalizedName { get; set; }
    }
}
