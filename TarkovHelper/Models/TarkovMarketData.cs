using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TarkovHelper.Models
{
    /// <summary>
    /// Marker data from Tarkov Market API (markers/list endpoint)
    /// </summary>
    public class TarkovMarketMarker
    {
        [JsonPropertyName("uid")]
        public string Uid { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("subCategory")]
        public string SubCategory { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("map")]
        public string Map { get; set; } = string.Empty;

        [JsonPropertyName("level")]
        [JsonConverter(typeof(FlexibleIntConverter))]
        public int? Level { get; set; }

        [JsonPropertyName("geometry")]
        public MarkerGeometry? Geometry { get; set; }

        [JsonPropertyName("questUid")]
        public string? QuestUid { get; set; }

        [JsonPropertyName("itemsUid")]
        public List<string>? ItemsUid { get; set; }

        [JsonPropertyName("imgs")]
        public List<MarkerImage>? Imgs { get; set; }

        [JsonPropertyName("updated")]
        public DateTime? Updated { get; set; }

        [JsonPropertyName("name_l10n")]
        public Dictionary<string, string>? NameL10n { get; set; }

        [JsonPropertyName("desc_l10n")]
        public Dictionary<string, string>? DescL10n { get; set; }
    }

    /// <summary>
    /// Geometry coordinates for a marker (SVG viewBox coordinates)
    /// </summary>
    public class MarkerGeometry
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }
    }

    /// <summary>
    /// Image attached to a marker
    /// </summary>
    public class MarkerImage
    {
        [JsonPropertyName("img")]
        public string Img { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }
    }

    /// <summary>
    /// Quest data from Tarkov Market API (quests/list endpoint)
    /// </summary>
    public class TarkovMarketQuest
    {
        [JsonPropertyName("uid")]
        public string Uid { get; set; } = string.Empty;

        /// <summary>
        /// BSG Quest ID - matches tarkov.dev task.ids
        /// </summary>
        [JsonPropertyName("bsgId")]
        public string BsgId { get; set; } = string.Empty;

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("ruName")]
        public string? RuName { get; set; }

        [JsonPropertyName("trader")]
        public string Trader { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("wikiUrl")]
        public string? WikiUrl { get; set; }

        [JsonPropertyName("reqLevel")]
        public JsonElement? ReqLevel { get; set; }

        [JsonPropertyName("reqLL")]
        public JsonElement? ReqLL { get; set; }

        [JsonPropertyName("reqRep")]
        public JsonElement? ReqRep { get; set; }

        [JsonPropertyName("requiredForKappa")]
        public bool RequiredForKappa { get; set; }

        [JsonPropertyName("objectives")]
        public List<object>? Objectives { get; set; }

        [JsonPropertyName("enObjectives")]
        public List<string>? EnObjectives { get; set; }

        [JsonPropertyName("ruObjectives")]
        public List<string>? RuObjectives { get; set; }

        [JsonPropertyName("updated")]
        public DateTime? Updated { get; set; }
    }

    /// <summary>
    /// Response wrapper for markers/list API
    /// </summary>
    public class TarkovMarketMarkersResponse
    {
        [JsonPropertyName("markers")]
        public string Markers { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response wrapper for quests/list API
    /// </summary>
    public class TarkovMarketQuestsResponse
    {
        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;

        [JsonPropertyName("user")]
        public string? User { get; set; }

        [JsonPropertyName("quests")]
        public string Quests { get; set; } = string.Empty;
    }

    /// <summary>
    /// Quest mismatch information for reporting
    /// </summary>
    public class QuestMismatchInfo
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("wikiName")]
        public string? WikiName { get; set; }

        [JsonPropertyName("tarkovMarketName")]
        public string? TarkovMarketName { get; set; }

        [JsonPropertyName("tarkovDevId")]
        public string? TarkovDevId { get; set; }

        [JsonPropertyName("tarkovMarketBsgId")]
        public string? TarkovMarketBsgId { get; set; }

        [JsonPropertyName("tarkovMarketUid")]
        public string? TarkovMarketUid { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Report of mismatches between wiki/tarkov.dev and Tarkov Market data
    /// </summary>
    public class QuestMismatchReport
    {
        [JsonPropertyName("generatedAt")]
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("totalWikiQuests")]
        public int TotalWikiQuests { get; set; }

        [JsonPropertyName("totalTarkovMarketQuests")]
        public int TotalTarkovMarketQuests { get; set; }

        [JsonPropertyName("matchedQuests")]
        public int MatchedQuests { get; set; }

        [JsonPropertyName("mismatches")]
        public List<QuestMismatchInfo> Mismatches { get; set; } = new();
    }

    /// <summary>
    /// JSON에서 int? 필드를 유연하게 파싱하는 컨버터.
    /// 빈 문자열, null, 또는 숫자를 처리합니다.
    /// </summary>
    public class FlexibleIntConverter : JsonConverter<int?>
    {
        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.Number)
                return reader.GetInt32();

            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString();
                if (string.IsNullOrWhiteSpace(str))
                    return null;

                if (int.TryParse(str, out var result))
                    return result;

                return null;
            }

            return null;
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteNumberValue(value.Value);
            else
                writer.WriteNullValue();
        }
    }
}
