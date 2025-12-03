using System.Text.RegularExpressions;
using TarkovHelper.Models;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Service for parsing quest data from wiki (.wiki) files
    /// Extracts quest relationships, required items, level/skill requirements
    /// </summary>
    public static class WikiQuestParser
    {
        #region Quest Relationships

        /// <summary>
        /// Parse previous quest(s) from wiki content
        /// Combines infobox "previous" field and "Must complete:" section in Requirements
        /// </summary>
        /// <param name="wikiContent">Raw wiki file content</param>
        /// <returns>List of prerequisite quest normalized names</returns>
        public static List<string>? ParsePreviousQuests(string wikiContent)
        {
            var allQuestNames = new List<string>();

            // Pattern 1: |previous = [[Quest Name]] or [[Quest1]]<br/>[[Quest2]]
            var match = Regex.Match(wikiContent, @"\|previous\s*=\s*([^\|\}]*?)(?=\||\}\})", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value))
            {
                var value = match.Groups[1].Value.Trim();
                // Skip self-referencing links like [[Quest#Requirements|See requirements]]
                if (!value.Contains("#Requirements", StringComparison.OrdinalIgnoreCase))
                {
                    var questNames = ExtractWikiLinks(value);
                    allQuestNames.AddRange(questNames);
                }
            }

            // Pattern 2: ==Requirements== section with "Must complete:" list
            // Example:
            // ==Requirements==
            // * Must complete:
            // ** [[Quest Name 1]]
            // ** [[Quest Name 2]]
            var reqMatch = Regex.Match(wikiContent,
                @"==\s*Requirements\s*==.*?\*\s*Must\s+complete[:\s]*\n((?:\*\*\s*\[\[[^\]]+\]\]\s*\n?)+)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (reqMatch.Success)
            {
                var questNames = ExtractWikiLinks(reqMatch.Groups[1].Value);
                allQuestNames.AddRange(questNames);
            }

            if (allQuestNames.Count == 0)
                return null;

            // Convert to normalized names and remove duplicates
            return allQuestNames.Select(NormalizedNameGenerator.Generate).Distinct().ToList();
        }

        /// <summary>
        /// Parse follow-up quest(s) from wiki content
        /// Combines "leads to" and "related2" fields (related2 is used for "Requirement for" relationships)
        /// </summary>
        /// <param name="wikiContent">Raw wiki file content</param>
        /// <returns>List of follow-up quest normalized names</returns>
        public static List<string>? ParseLeadsToQuests(string wikiContent)
        {
            var allQuestNames = new List<string>();

            // Pattern 1: |leads to = [[Quest Name]] or [[Quest1]]<br/>[[Quest2]]
            var leadsToMatch = Regex.Match(wikiContent, @"\|leads\s+to\s*=\s*([^\|\}]*?)(?=\||\}\})", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (leadsToMatch.Success && !string.IsNullOrWhiteSpace(leadsToMatch.Groups[1].Value))
            {
                var questNames = ExtractWikiLinks(leadsToMatch.Groups[1].Value.Trim());
                allQuestNames.AddRange(questNames);
            }

            // Pattern 2: |related2 = [[Quest Name]] (used for "Requirement for" relationships like Prestige quests)
            var related2Match = Regex.Match(wikiContent, @"\|related2\s*=\s*([^\|\}]*?)(?=\||\}\})", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (related2Match.Success && !string.IsNullOrWhiteSpace(related2Match.Groups[1].Value))
            {
                var questNames = ExtractWikiLinks(related2Match.Groups[1].Value.Trim());
                allQuestNames.AddRange(questNames);
            }

            if (allQuestNames.Count == 0)
                return null;

            // Convert to normalized names and remove duplicates
            return allQuestNames.Select(NormalizedNameGenerator.Generate).Distinct().ToList();
        }

        /// <summary>
        /// Extract quest/item names from wiki links
        /// Handles: [[Name]], [[Name|Display]], multiple links separated by &lt;br/&gt;
        /// </summary>
        private static List<string> ExtractWikiLinks(string value)
        {
            var result = new List<string>();

            // Split by <br/> or <br /> first
            var parts = Regex.Split(value, @"<br\s*/?>", RegexOptions.IgnoreCase);

            foreach (var part in parts)
            {
                // Find all [[...]] patterns
                var matches = Regex.Matches(part, @"\[\[([^\]]+)\]\]");
                foreach (Match match in matches)
                {
                    var linkContent = match.Groups[1].Value;

                    // Handle [[Name|Display]] format - take only the Name part
                    var pipeIndex = linkContent.IndexOf('|');
                    var questName = pipeIndex >= 0 ? linkContent.Substring(0, pipeIndex) : linkContent;

                    // Remove time delays like "(+10hr)"
                    questName = Regex.Replace(questName, @"\s*\(\+\d+\s*hr?\)", "", RegexOptions.IgnoreCase);

                    questName = questName.Trim();
                    if (!string.IsNullOrEmpty(questName))
                    {
                        result.Add(questName);
                    }
                }
            }

            return result;
        }

        #endregion

        #region Level Requirements

        /// <summary>
        /// Parse required player level from wiki content
        /// </summary>
        /// <param name="wikiContent">Raw wiki file content</param>
        /// <returns>Required level or null if not specified</returns>
        public static int? ParseRequiredLevel(string wikiContent)
        {
            // Pattern: "Must be level X to start this quest"
            var match = Regex.Match(wikiContent, @"Must\s+be\s+level\s+(\d+)\s+to\s+start", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var level))
            {
                return level;
            }

            // Alternative pattern: |level = X (in infobox)
            match = Regex.Match(wikiContent, @"\|level\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out level))
            {
                return level;
            }

            return null;
        }

        #endregion

        #region Skill Requirements

        /// <summary>
        /// Parse skill requirements from wiki content
        /// </summary>
        /// <param name="wikiContent">Raw wiki file content</param>
        /// <returns>List of skill requirements or null if none</returns>
        public static List<SkillRequirement>? ParseSkillRequirements(string wikiContent)
        {
            var results = new List<SkillRequirement>();

            // Pattern 1: "Reach the required [[Skill Name|Skill skill]] level of X"
            var matches = Regex.Matches(wikiContent,
                @"Reach\s+the\s+required\s+\[\[([^\]|]+)(?:\|[^\]]+)?\]\]\s*(?:skill\s+)?level\s+of\s+(\d+)",
                RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var skillName = match.Groups[1].Value.Trim();
                if (int.TryParse(match.Groups[2].Value, out var level))
                {
                    results.Add(new SkillRequirement
                    {
                        SkillNormalizedName = NormalizedNameGenerator.Generate(skillName),
                        Level = level
                    });
                }
            }

            // Pattern 2: "reach level X in the [[Skill Name]] skill"
            matches = Regex.Matches(wikiContent,
                @"reach\s+level\s+(\d+)\s+in\s+the\s+\[\[([^\]|]+)(?:\|[^\]]+)?\]\]",
                RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var skillName = match.Groups[2].Value.Trim();
                if (int.TryParse(match.Groups[1].Value, out var level))
                {
                    // Avoid duplicates
                    var normalizedName = NormalizedNameGenerator.Generate(skillName);
                    if (!results.Any(r => r.SkillNormalizedName == normalizedName))
                    {
                        results.Add(new SkillRequirement
                        {
                            SkillNormalizedName = normalizedName,
                            Level = level
                        });
                    }
                }
            }

            return results.Count > 0 ? results : null;
        }

        #endregion

        #region Required Items

        /// <summary>
        /// Parse required items from the "Related Quest Items" table in wiki content
        /// </summary>
        /// <param name="wikiContent">Raw wiki file content</param>
        /// <returns>List of required items or null if none</returns>
        public static List<QuestItem>? ParseRequiredItems(string wikiContent)
        {
            var results = new List<QuestItem>();

            // Find the wikitable with "Related Quest Items"
            // Match "wikitable", "wikitable sortable", etc.
            var tableMatch = Regex.Match(wikiContent,
                @"\{\|\s*class\s*=\s*""wikitable[^""]*"".*?Related\s+Quest\s+Items.*?\|\}",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!tableMatch.Success)
                return null;

            var tableContent = tableMatch.Value;

            // Split by row separator "|-"
            var rows = Regex.Split(tableContent, @"\|-");

            // Track rowspan Notes for merged cells
            string? rowspanNotes = null;
            int rowspanRemaining = 0;

            // Skip header rows (first 2 usually: header declaration and column headers)
            for (int i = 2; i < rows.Length; i++)
            {
                var row = rows[i].Trim();
                if (string.IsNullOrWhiteSpace(row) || row.StartsWith("|}"))
                    continue;

                // Check for rowspan in Notes column and extract the span count
                var rowspanMatch = Regex.Match(row, @"rowspan\s*=\s*""?(\d+)""?\s*\|", RegexOptions.IgnoreCase);
                if (rowspanMatch.Success && int.TryParse(rowspanMatch.Groups[1].Value, out var spanCount))
                {
                    rowspanRemaining = spanCount;
                    // Extract the Notes content after the rowspan declaration
                    var rowspanContentMatch = Regex.Match(row,
                        @"rowspan\s*=\s*""?\d+""?\s*\|(.*?)$",
                        RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    rowspanNotes = rowspanContentMatch.Success ? rowspanContentMatch.Groups[1].Value : null;
                }

                // Determine which Notes to use for this row
                string? effectiveNotes = null;
                if (rowspanRemaining > 0)
                {
                    effectiveNotes = rowspanNotes;
                    rowspanRemaining--;
                }

                var item = ParseTableRow(row, effectiveNotes);
                if (item != null)
                {
                    results.Add(item);
                }
            }

            return results.Count > 0 ? results : null;
        }

        /// <summary>
        /// Parse a single row from the Related Quest Items table
        /// Table columns: Icon | Name | Quantity | Requirements | FIR | Notes
        /// </summary>
        /// <param name="row">Raw row content</param>
        /// <param name="effectiveNotes">Notes content (may be from rowspan merged cell)</param>
        private static QuestItem? ParseTableRow(string row, string? effectiveNotes = null)
        {
            // Use effectiveNotes if provided (from rowspan), otherwise check the row itself
            var notesToCheck = effectiveNotes ?? row;

            // Skip quest-only items (items that can only be found during the quest)
            // These are identified by notes like "quest item" or "can only be found if the quest is active"
            if (notesToCheck.Contains("quest item", StringComparison.OrdinalIgnoreCase) ||
                notesToCheck.Contains("can only be found if the quest is active", StringComparison.OrdinalIgnoreCase) ||
                notesToCheck.Contains("only be found when this quest is active", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Split row into cells by newline + | or !
            // Table format: !Icon | Name | Quantity | Requirements | !FIR | Notes
            var cells = Regex.Split(row, @"\n\s*[\|!]")
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            if (cells.Count < 5)
                return null;

            // Parse by column position (skip Icon column which is first)
            string? itemId = null;
            string? itemName = null;
            int amount = 0;
            string requirement = "";
            bool foundInRaid = false;
            string? notes = null;

            int columnIndex = 0;
            foreach (var cell in cells)
            {
                // Column 0: Icon (skip)
                if (columnIndex == 0 && (cell.Contains("[[File:") || cell.Contains("File:")))
                {
                    columnIndex++;
                    continue;
                }

                // Column 1: Name - can be {{itemId}} template or [[Item Name]]
                if (columnIndex == 1)
                {
                    // Try {{itemId}} template first (e.g., {{5ed51652f6c34d2cc26336a1}})
                    var templateMatch = Regex.Match(cell, @"\{\{([a-f0-9]{24})\}\}");
                    if (templateMatch.Success)
                    {
                        itemId = templateMatch.Groups[1].Value;
                        columnIndex++;
                        continue;
                    }

                    // Try [[Item Name]] or [[Item Name|Display]] format
                    var linkMatch = Regex.Match(cell, @"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]");
                    if (linkMatch.Success)
                    {
                        var linkName = linkMatch.Groups[1].Value.Trim();
                        var displayName = linkMatch.Groups[2].Success ? linkMatch.Groups[2].Value.Trim() : null;

                        // Special case: Dogtag uses display name for BEAR/USEC distinction
                        // Example: [[Dogtag|BEAR Dogtag]] should use "BEAR Dogtag"
                        string potentialName;
                        if (displayName != null && linkName.Equals("Dogtag", StringComparison.OrdinalIgnoreCase))
                        {
                            potentialName = displayName;
                        }
                        else
                        {
                            potentialName = linkName;
                        }

                        if (!potentialName.Contains("Found in raid") &&
                            !potentialName.Contains("skill") &&
                            !IsLocationName(potentialName))
                        {
                            itemName = potentialName;
                        }
                    }
                    columnIndex++;
                    continue;
                }

                // Column 2: Quantity
                if (columnIndex == 2)
                {
                    if (int.TryParse(cell.Trim(), out var parsedAmount))
                    {
                        amount = parsedAmount;
                    }
                    columnIndex++;
                    continue;
                }

                // Column 3: Requirements (Handover item, Required, Optional)
                if (columnIndex == 3)
                {
                    if (cell.Contains("Handover", StringComparison.OrdinalIgnoreCase))
                        requirement = "Handover";
                    else if (cell.Contains("Required", StringComparison.OrdinalIgnoreCase))
                        requirement = "Required";
                    else if (cell.Contains("Optional", StringComparison.OrdinalIgnoreCase))
                        requirement = "Optional";
                    columnIndex++;
                    continue;
                }

                // Column 4: Found in Raid (Yes/No with color)
                if (columnIndex == 4)
                {
                    // <font color="red">Yes</font> or <font color="green">No</font>
                    foundInRaid = cell.Contains("Yes", StringComparison.OrdinalIgnoreCase) &&
                                  !cell.Contains("N/A", StringComparison.OrdinalIgnoreCase);
                    columnIndex++;
                    continue;
                }

                // Column 5: Notes - capture for level requirement parsing
                if (columnIndex == 5)
                {
                    notes = cell;
                    columnIndex++;
                    continue;
                }

                columnIndex++;
            }

            // Must have either itemId or itemName
            if (string.IsNullOrEmpty(itemId) && string.IsNullOrEmpty(itemName))
                return null;

            // Use effectiveNotes if available (from rowspan), otherwise use parsed notes
            var finalNotes = effectiveNotes ?? notes;

            // Parse dogtag level requirement from notes
            // Pattern: "Needs to be level XX or higher" or "level XX or higher"
            int? dogtagMinLevel = null;
            if (!string.IsNullOrEmpty(finalNotes))
            {
                var levelMatch = Regex.Match(finalNotes, @"level\s+(\d+)\s+or\s+higher", RegexOptions.IgnoreCase);
                if (levelMatch.Success && int.TryParse(levelMatch.Groups[1].Value, out var minLevel))
                {
                    dogtagMinLevel = minLevel;
                }
            }

            return new QuestItem
            {
                // If we have itemId, store it directly for lookup; otherwise use normalized name
                ItemNormalizedName = !string.IsNullOrEmpty(itemId)
                    ? $"id:{itemId}"  // Prefix with "id:" to indicate this is an item ID
                    : NormalizedNameGenerator.Generate(itemName!),
                Amount = amount > 0 ? amount : 1,
                Requirement = string.IsNullOrEmpty(requirement) ? "Required" : requirement,
                FoundInRaid = foundInRaid,
                DogtagMinLevel = dogtagMinLevel
            };
        }

        /// <summary>
        /// Check if a name is a location name (to filter out non-item links)
        /// </summary>
        private static bool IsLocationName(string name)
        {
            var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Customs", "Factory", "Woods", "Shoreline", "Interchange", "Reserve",
                "Labs", "Streets of Tarkov", "Lighthouse", "Ground Zero"
            };
            return locations.Contains(name);
        }

        #endregion

        #region Full Parse

        /// <summary>
        /// Parse all quest data from wiki content
        /// </summary>
        /// <param name="wikiContent">Raw wiki file content</param>
        /// <returns>Parsed quest data</returns>
        public static WikiQuestData ParseAll(string wikiContent)
        {
            var (guideText, guideImages) = ParseGuide(wikiContent);

            return new WikiQuestData
            {
                Previous = ParsePreviousQuests(wikiContent),
                LeadsTo = ParseLeadsToQuests(wikiContent),
                Maps = ParseMaps(wikiContent),
                RequiredLevel = ParseRequiredLevel(wikiContent),
                RequiredSkills = ParseSkillRequirements(wikiContent),
                RequiredItems = ParseRequiredItems(wikiContent),
                GuideText = guideText,
                GuideImages = guideImages
            };
        }

        #endregion

        #region Guide Section

        /// <summary>
        /// Parse guide text and gallery images from wiki content
        /// </summary>
        /// <param name="wikiContent">Raw wiki file content</param>
        /// <returns>Tuple of (guide text, list of guide images)</returns>
        public static (string? GuideText, List<GuideImage>? GuideImages) ParseGuide(string wikiContent)
        {
            // Find the Guide section
            var guideMatch = Regex.Match(wikiContent,
                @"==\s*Guide\s*==\s*(.*?)(?=\n==|\{\{Navbox|\[\[Category:|\z)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!guideMatch.Success)
                return (null, null);

            var guideContent = guideMatch.Groups[1].Value;

            // Extract text (remove tables, galleries, HTML tags)
            var guideText = ExtractGuideText(guideContent);

            // Extract gallery images
            var guideImages = ExtractGalleryImages(guideContent);

            return (
                string.IsNullOrWhiteSpace(guideText) ? null : guideText,
                guideImages.Count > 0 ? guideImages : null
            );
        }

        /// <summary>
        /// Extract plain text from guide content, removing wiki markup
        /// </summary>
        private static string ExtractGuideText(string guideContent)
        {
            var text = guideContent;

            // Remove wikitables {| ... |}
            text = Regex.Replace(text, @"\{\|.*?\|\}", "", RegexOptions.Singleline);

            // Remove gallery tags and their content
            text = Regex.Replace(text, @"<gallery[^>]*>.*?</gallery>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // Remove <li> and similar HTML tags
            text = Regex.Replace(text, @"<[^>]+>", "", RegexOptions.Singleline);

            // Convert wiki links [[Link|Text]] to just Text, or [[Link]] to Link
            text = Regex.Replace(text, @"\[\[([^\]|]+)\|([^\]]+)\]\]", "$2");
            text = Regex.Replace(text, @"\[\[([^\]]+)\]\]", "$1");

            // Remove {{...}} templates
            text = Regex.Replace(text, @"\{\{[^\}]+\}\}", "", RegexOptions.Singleline);

            // Remove <code>...</code> tags but keep content
            text = Regex.Replace(text, @"<code>([^<]*)</code>", "$1", RegexOptions.IgnoreCase);

            // Clean up multiple newlines and whitespace
            text = Regex.Replace(text, @"\n\s*\n", "\n\n");
            text = text.Trim();

            return text;
        }

        /// <summary>
        /// Extract images from gallery tags
        /// </summary>
        private static List<GuideImage> ExtractGalleryImages(string guideContent)
        {
            var images = new List<GuideImage>();

            // Find all gallery tags
            var galleryMatches = Regex.Matches(guideContent,
                @"<gallery[^>]*>(.*?)</gallery>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            foreach (Match galleryMatch in galleryMatches)
            {
                var galleryContent = galleryMatch.Groups[1].Value;

                // Parse each line in gallery: File:name.png|caption
                var lines = galleryContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine))
                        continue;

                    // Pattern: File:filename.ext|caption or just File:filename.ext
                    var fileMatch = Regex.Match(trimmedLine, @"File:([^|]+)(?:\|(.*))?", RegexOptions.IgnoreCase);
                    if (fileMatch.Success)
                    {
                        var fileName = fileMatch.Groups[1].Value.Trim();
                        var caption = fileMatch.Groups[2].Success ? fileMatch.Groups[2].Value.Trim() : null;

                        images.Add(new GuideImage
                        {
                            FileName = fileName,
                            Caption = string.IsNullOrEmpty(caption) ? null : caption
                        });
                    }
                }
            }

            return images;
        }

        #endregion

        #region Maps

        /// <summary>
        /// Parse map location(s) from wiki content
        /// </summary>
        /// <param name="wikiContent">Raw wiki file content</param>
        /// <returns>List of map normalized names</returns>
        public static List<string>? ParseMaps(string wikiContent)
        {
            // Pattern: |location = [[Map Name]] or [[Map1]], [[Map2]] or [[Map1]]<br/>[[Map2]]
            var match = Regex.Match(wikiContent, @"\|location\s*=\s*([^\|\}]*?)(?=\||\}\})", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
                return null;

            var value = match.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(value))
                return null;

            var mapNames = ExtractWikiLinks(value);
            if (mapNames.Count == 0)
                return null;

            // Filter out non-map entries (like "any location" or similar)
            var validMaps = mapNames
                .Where(m => !m.Contains("any", StringComparison.OrdinalIgnoreCase))
                .Select(NormalizedNameGenerator.Generate)
                .Distinct()
                .ToList();

            return validMaps.Count > 0 ? validMaps : null;
        }

        #endregion
    }

    /// <summary>
    /// Parsed quest data from wiki
    /// </summary>
    public class WikiQuestData
    {
        public List<string>? Previous { get; set; }
        public List<string>? LeadsTo { get; set; }
        public List<string>? Maps { get; set; }
        public int? RequiredLevel { get; set; }
        public List<SkillRequirement>? RequiredSkills { get; set; }
        public List<QuestItem>? RequiredItems { get; set; }
        public string? GuideText { get; set; }
        public List<GuideImage>? GuideImages { get; set; }
    }
}
