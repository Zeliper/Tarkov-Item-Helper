using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TarkovHelper.Models
{
    public class WikiQuest
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("wikiPath")]
        public string WikiPath { get; set; } = string.Empty;
    }

    public class WikiQuestsByTrader : Dictionary<string, List<WikiQuest>>
    {
    }

    /// <summary>
    /// Cache metadata for a single wiki page
    /// </summary>
    public class WikiPageCacheEntry
    {
        [JsonPropertyName("pageId")]
        public int PageId { get; set; }

        [JsonPropertyName("revisionId")]
        public int RevisionId { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("cachedAt")]
        public DateTime CachedAt { get; set; }
    }

    /// <summary>
    /// Cache metadata index for all wiki pages
    /// </summary>
    public class WikiCacheIndex : Dictionary<string, WikiPageCacheEntry>
    {
    }
}
