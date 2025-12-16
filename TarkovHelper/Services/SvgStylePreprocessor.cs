using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace TarkovHelper.Services;

/// <summary>
/// SVG preprocessor that converts CSS classes to inline styles.
/// Also supports floor layer filtering for multi-floor maps.
/// </summary>
public sealed partial class SvgStylePreprocessor
{
    /// <summary>
    /// Process SVG file and convert CSS classes to inline styles.
    /// </summary>
    public string ProcessSvgFile(string svgFilePath)
    {
        var svgContent = File.ReadAllText(svgFilePath);
        return ProcessSvgContent(svgContent);
    }

    /// <summary>
    /// Process SVG file with floor filtering.
    /// </summary>
    /// <param name="svgFilePath">SVG file path</param>
    /// <param name="visibleFloorIds">Floor IDs to show. null = show all.</param>
    /// <param name="allFloorIds">All floor IDs defined in map config.</param>
    /// <param name="backgroundFloorId">Floor to show as dimmed background (e.g., "main"). Ignored if dimAllOtherFloors is true.</param>
    /// <param name="backgroundOpacity">Background floor opacity (0.0 ~ 1.0). Default 0.3</param>
    /// <param name="dimAllOtherFloors">If true, dim all non-visible floors. Default false.</param>
    public string ProcessSvgFile(string svgFilePath, IEnumerable<string>? visibleFloorIds, IEnumerable<string>? allFloorIds = null, string? backgroundFloorId = null, double backgroundOpacity = 0.3, bool dimAllOtherFloors = false)
    {
        var svgContent = File.ReadAllText(svgFilePath);
        return ProcessSvgContent(svgContent, visibleFloorIds, allFloorIds, backgroundFloorId, backgroundOpacity, dimAllOtherFloors);
    }

    /// <summary>
    /// Process SVG content and convert CSS classes to inline styles.
    /// </summary>
    public string ProcessSvgContent(string svgContent)
    {
        return ProcessSvgContent(svgContent, null, null);
    }

    /// <summary>
    /// Process SVG content with floor filtering.
    /// </summary>
    public string ProcessSvgContent(string svgContent, IEnumerable<string>? visibleFloorIds, IEnumerable<string>? allFloorIds = null, string? backgroundFloorId = null, double backgroundOpacity = 0.3, bool dimAllOtherFloors = false)
    {
        // 1. Extract and parse CSS styles
        var styleRules = ExtractAndParseCssStyles(svgContent);

        // 2. Parse XML and convert class to style + floor filtering
        var processedSvg = ConvertClassesToInlineStyles(svgContent, styleRules, visibleFloorIds, allFloorIds, backgroundFloorId, backgroundOpacity, dimAllOtherFloors);

        return processedSvg;
    }

    /// <summary>
    /// Extract CSS from style tags.
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> ExtractAndParseCssStyles(string svgContent)
    {
        var rules = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        var styleMatch = StyleTagRegex().Match(svgContent);
        if (!styleMatch.Success)
            return rules;

        var styleContent = styleMatch.Groups[1].Value;

        var ruleMatches = CssRuleRegex().Matches(styleContent);

        foreach (Match match in ruleMatches)
        {
            var className = match.Groups[1].Value.Trim();
            var properties = match.Groups[2].Value.Trim();

            var propDict = ParseCssProperties(properties);
            rules[className] = propDict;
        }

        return rules;
    }

    /// <summary>
    /// Parse CSS property string.
    /// </summary>
    private Dictionary<string, string> ParseCssProperties(string properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var pairs = properties.Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var colonIndex = pair.IndexOf(':');
            if (colonIndex > 0)
            {
                var key = pair[..colonIndex].Trim();
                var value = pair[(colonIndex + 1)..].Trim();
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Convert class attributes to inline styles with floor filtering.
    /// </summary>
    private string ConvertClassesToInlineStyles(
        string svgContent,
        Dictionary<string, Dictionary<string, string>> styleRules,
        IEnumerable<string>? visibleFloorIds = null,
        IEnumerable<string>? allFloorIds = null,
        string? backgroundFloorId = null,
        double backgroundOpacity = 0.3,
        bool dimAllOtherFloors = false)
    {
        var doc = new XmlDocument();
        doc.PreserveWhitespace = true;

        try
        {
            doc.LoadXml(svgContent);
        }
        catch
        {
            return svgContent;
        }

        HashSet<string>? visibleFloors = visibleFloorIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string>? allFloors = allFloorIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        ProcessElementsWithClass(doc.DocumentElement!, styleRules, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity, dimAllOtherFloors);

        RemoveStyleTags(doc);

        using var stringWriter = new StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings
        {
            Indent = false,
            OmitXmlDeclaration = true
        });
        doc.Save(xmlWriter);

        return stringWriter.ToString();
    }

    /// <summary>
    /// Process elements with class attributes.
    /// </summary>
    private void ProcessElementsWithClass(
        XmlElement element,
        Dictionary<string, Dictionary<string, string>> styleRules,
        HashSet<string>? visibleFloors = null,
        HashSet<string>? allFloors = null,
        string? backgroundFloorId = null,
        double backgroundOpacity = 0.3,
        bool dimAllOtherFloors = false)
    {
        // Floor filtering for <g id="..."> elements
        if (visibleFloors != null && allFloors != null && element.Name == "g")
        {
            var elementId = element.GetAttribute("id");
            if (!string.IsNullOrEmpty(elementId) && allFloors.Contains(elementId))
            {
                var isVisible = visibleFloors.Contains(elementId);

                var isBackgroundLayer = !isVisible && (dimAllOtherFloors ||
                    (!string.IsNullOrEmpty(backgroundFloorId) &&
                     string.Equals(elementId, backgroundFloorId, StringComparison.OrdinalIgnoreCase)));

                var existingStyle = element.GetAttribute("style");

                string newStyle;
                if (isVisible)
                {
                    newStyle = "display:block;opacity:1";
                }
                else if (isBackgroundLayer)
                {
                    newStyle = $"display:block;opacity:{backgroundOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }
                else
                {
                    newStyle = "display:none";
                }

                if (!string.IsNullOrEmpty(existingStyle))
                {
                    existingStyle = RemoveStyleProperty(existingStyle, "display");
                    existingStyle = RemoveStyleProperty(existingStyle, "opacity");
                    element.SetAttribute("style", string.IsNullOrEmpty(existingStyle)
                        ? newStyle
                        : $"{existingStyle};{newStyle}");
                }
                else
                {
                    element.SetAttribute("style", newStyle);
                }
            }
        }

        // Process class attribute
        var classAttr = element.GetAttribute("class");
        if (!string.IsNullOrWhiteSpace(classAttr))
        {
            var inlineStyle = BuildInlineStyle(classAttr, styleRules);
            if (!string.IsNullOrEmpty(inlineStyle))
            {
                var existingStyle = element.GetAttribute("style");
                if (!string.IsNullOrEmpty(existingStyle))
                {
                    inlineStyle = MergeStyles(inlineStyle, existingStyle);
                }

                element.SetAttribute("style", inlineStyle);
            }
        }

        // Recursively process children
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childElement)
            {
                ProcessElementsWithClass(childElement, styleRules, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity, dimAllOtherFloors);
            }
        }
    }

    /// <summary>
    /// Remove a specific property from style string.
    /// </summary>
    private string RemoveStyleProperty(string style, string propertyName)
    {
        var props = ParseCssProperties(style);
        props.Remove(propertyName);
        return props.Count == 0 ? string.Empty : string.Join(";", props.Select(p => $"{p.Key}:{p.Value}"));
    }

    /// <summary>
    /// Build inline style from class attribute.
    /// </summary>
    private string BuildInlineStyle(string classAttribute, Dictionary<string, Dictionary<string, string>> styleRules)
    {
        var classes = classAttribute.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var mergedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var className in classes)
        {
            if (styleRules.TryGetValue(className, out var properties))
            {
                foreach (var prop in properties)
                {
                    mergedProperties[prop.Key] = prop.Value;
                }
            }
        }

        if (mergedProperties.Count == 0)
            return string.Empty;

        return string.Join(";", mergedProperties.Select(p => $"{p.Key}:{p.Value}"));
    }

    /// <summary>
    /// Merge two style strings. Override style takes precedence.
    /// </summary>
    private string MergeStyles(string baseStyle, string overrideStyle)
    {
        var baseProps = ParseCssProperties(baseStyle);
        var overrideProps = ParseCssProperties(overrideStyle);

        foreach (var prop in overrideProps)
        {
            baseProps[prop.Key] = prop.Value;
        }

        return string.Join(";", baseProps.Select(p => $"{p.Key}:{p.Value}"));
    }

    /// <summary>
    /// Remove style tags from document.
    /// </summary>
    private void RemoveStyleTags(XmlDocument doc)
    {
        var styleNodes = doc.GetElementsByTagName("style");
        var nodesToRemove = new List<XmlNode>();

        foreach (XmlNode node in styleNodes)
        {
            nodesToRemove.Add(node);
        }

        foreach (var node in nodesToRemove)
        {
            node.ParentNode?.RemoveChild(node);
        }
    }

    [GeneratedRegex(@"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"\.([a-zA-Z_][a-zA-Z0-9_-]*)\s*\{([^}]*)\}", RegexOptions.Multiline)]
    private static partial Regex CssRuleRegex();
}
