using System.Net;
using System.Text.RegularExpressions;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Utility for generating and matching normalized names
    /// Used to match wiki item names to tarkov.dev API items
    /// </summary>
    public static class NormalizedNameGenerator
    {
        /// <summary>
        /// Manual overrides for quest normalizedNames that conflict with skills or other entities
        /// Maps API/generated normalizedName to wiki-style normalizedName
        /// </summary>
        private static readonly Dictionary<string, string> QuestNameOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            { "immunity", "immunity-quest" },  // Conflicts with Immunity skill
            { "reserve", "reserve-quest" },    // Conflicts with Reserve map
        };

        /// <summary>
        /// Manual overrides for wiki URLs where quest name differs from wiki page name
        /// Maps quest name to wiki page name (for disambiguation pages like "Immunity_(quest)")
        /// </summary>
        private static readonly Dictionary<string, string> WikiUrlOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Immunity", "Immunity_(quest)" },  // Quest page has disambiguation suffix (conflicts with skill)
            { "Reserve", "Reserve_(quest)" },    // Quest page has disambiguation suffix (conflicts with map)
        };

        /// <summary>
        /// Apply quest-specific normalizedName overrides
        /// </summary>
        public static string ApplyQuestOverride(string normalizedName)
        {
            if (string.IsNullOrEmpty(normalizedName))
                return normalizedName;

            return QuestNameOverrides.TryGetValue(normalizedName, out var overrideName)
                ? overrideName
                : normalizedName;
        }

        /// <summary>
        /// Get wiki page name for a quest (handles disambiguation pages)
        /// </summary>
        public static string GetWikiPageName(string questName)
        {
            if (string.IsNullOrEmpty(questName))
                return questName;

            return WikiUrlOverrides.TryGetValue(questName, out var wikiPageName)
                ? wikiPageName
                : questName;
        }

        /// <summary>
        /// Generate a normalized name from a wiki or display name
        /// Matches tarkov.dev API normalizedName format
        /// </summary>
        /// <param name="name">Original name (e.g., "MP-133 12ga pump-action shotgun")</param>
        /// <returns>Normalized name (e.g., "mp-133-12ga-pump-action-shotgun")</returns>
        public static string Generate(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            // Decode HTML entities first (e.g., &#39; -> ')
            var decoded = WebUtility.HtmlDecode(name);

            var result = decoded
                .ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")      // Remove apostrophes: "Huntsman's Path" -> "huntsmans-path"
                .Replace("'", "")      // Remove right single quotation mark (U+2019)
                .Replace("'", "")      // Remove left single quotation mark (U+2018)
                .Replace(":", "")      // Remove colons
                .Replace("?", "")      // Remove question marks
                .Replace(".", "")      // Remove periods
                .Replace(",", "")      // Remove commas
                .Replace("!", "")      // Remove exclamation marks
                .Replace("\"", "")     // Remove quotes
                .Replace("(", "")      // Remove parentheses
                .Replace(")", "")
                .Replace("[", "")      // Remove brackets
                .Replace("]", "")
                .Replace("&", "and");  // Replace ampersand with "and"

            // Fix multiple consecutive hyphens (e.g., "---" → "-")
            while (result.Contains("--"))
                result = result.Replace("--", "-");

            return result.Trim('-');   // Remove leading/trailing hyphens
        }

        /// <summary>
        /// Generate alternative normalized names for fuzzy matching
        /// Useful when wiki item names don't exactly match API names
        /// </summary>
        /// <param name="name">Original name</param>
        /// <returns>List of possible normalized names to try</returns>
        public static List<string> GenerateAlternatives(string name)
        {
            var alternatives = new List<string>();
            var primary = Generate(name);

            if (!string.IsNullOrEmpty(primary))
                alternatives.Add(primary);

            // Alternative 1: Remove parenthetical text
            var withoutParentheses = Regex.Replace(name, @"\s*\([^)]*\)", "");
            var normalized1 = Generate(withoutParentheses);
            if (!string.IsNullOrEmpty(normalized1) && !alternatives.Contains(normalized1))
                alternatives.Add(normalized1);

            // Alternative 2: Expand common abbreviations
            var expanded = name
                .Replace("12ga", "12-gauge")
                .Replace("7.62", "762")
                .Replace("5.56", "556")
                .Replace("9x19", "9x19mm");
            var normalized2 = Generate(expanded);
            if (!string.IsNullOrEmpty(normalized2) && !alternatives.Contains(normalized2))
                alternatives.Add(normalized2);

            return alternatives;
        }

        /// <summary>
        /// Normalize a quest name for file matching
        /// Handles special characters that may be encoded differently
        /// </summary>
        /// <param name="name">Quest name</param>
        /// <returns>Filename-safe normalized name</returns>
        public static string NormalizeForFilename(string name)
        {
            return name
                // Normalize various apostrophe types to standard single quote
                .Replace("'", "'")  // RIGHT SINGLE QUOTATION MARK (U+2019) -> '
                .Replace("'", "'")  // LEFT SINGLE QUOTATION MARK (U+2018) -> '
                .Replace("ʼ", "'")  // MODIFIER LETTER APOSTROPHE -> '
                // Replace invalid filename characters with underscore
                .Replace("?", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("\"", "_")
                .Replace("<", "_")
                .Replace(">", "_")
                .Replace("|", "_");
        }
    }
}
