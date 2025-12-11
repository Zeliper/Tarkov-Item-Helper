using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace TarkovDBEditor.Services;

/// <summary>
/// SVG 파일의 CSS 클래스 스타일을 인라인 스타일로 변환하는 전처리기.
/// SharpVectors가 CSS 클래스를 제대로 처리하지 못하는 문제를 해결합니다.
/// </summary>
public partial class SvgStylePreprocessor
{
    /// <summary>
    /// SVG 파일을 읽어서 CSS 클래스를 인라인 스타일로 변환합니다.
    /// </summary>
    public string ProcessSvgFile(string svgFilePath)
    {
        var svgContent = File.ReadAllText(svgFilePath);
        return ProcessSvgContent(svgContent);
    }

    /// <summary>
    /// SVG 콘텐츠의 CSS 클래스를 인라인 스타일로 변환합니다.
    /// </summary>
    public string ProcessSvgContent(string svgContent)
    {
        // 1. CSS 스타일 블록 추출 및 파싱
        var styleRules = ExtractAndParseCssStyles(svgContent);

        // 2. XML 파싱 및 클래스→스타일 변환
        var processedSvg = ConvertClassesToInlineStyles(svgContent, styleRules);

        return processedSvg;
    }

    /// <summary>
    /// SVG에서 &lt;style&gt; 태그의 CSS를 파싱합니다.
    /// </summary>
    private Dictionary<string, Dictionary<string, string>> ExtractAndParseCssStyles(string svgContent)
    {
        var rules = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        // <style> 태그 내용 추출
        var styleMatch = StyleTagRegex().Match(svgContent);
        if (!styleMatch.Success)
            return rules;

        var styleContent = styleMatch.Groups[1].Value;

        // CSS 규칙 파싱: .className { property: value; ... }
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
    /// CSS 속성 문자열을 파싱합니다.
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
    /// XML을 파싱하여 class 속성을 style 속성으로 변환합니다.
    /// </summary>
    private string ConvertClassesToInlineStyles(
        string svgContent,
        Dictionary<string, Dictionary<string, string>> styleRules)
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

        // class 속성이 있는 모든 요소 처리
        ProcessElementsWithClass(doc.DocumentElement!, styleRules);

        // <style> 태그 제거
        RemoveStyleTags(doc);

        // 결과 반환
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
    /// 요소와 그 자식들의 class 속성을 style로 변환합니다.
    /// </summary>
    private void ProcessElementsWithClass(
        XmlElement element,
        Dictionary<string, Dictionary<string, string>> styleRules)
    {
        // 현재 요소의 class 속성 처리
        var classAttr = element.GetAttribute("class");
        if (!string.IsNullOrWhiteSpace(classAttr))
        {
            var inlineStyle = BuildInlineStyle(classAttr, styleRules);
            if (!string.IsNullOrEmpty(inlineStyle))
            {
                // 기존 style 속성이 있으면 병합
                var existingStyle = element.GetAttribute("style");
                if (!string.IsNullOrEmpty(existingStyle))
                {
                    inlineStyle = MergeStyles(inlineStyle, existingStyle);
                }

                element.SetAttribute("style", inlineStyle);
            }
        }

        // 자식 요소들 재귀 처리
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childElement)
            {
                ProcessElementsWithClass(childElement, styleRules);
            }
        }
    }

    /// <summary>
    /// 클래스 목록에서 인라인 스타일 문자열을 생성합니다.
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
    /// 두 스타일 문자열을 병합합니다. 두 번째 스타일이 우선합니다.
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
    /// &lt;style&gt; 태그를 제거합니다.
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

    // Regex 패턴 (컴파일된 정규식)
    [GeneratedRegex(@"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleTagRegex();

    [GeneratedRegex(@"\.([a-zA-Z_][a-zA-Z0-9_-]*)\s*\{([^}]*)\}", RegexOptions.Multiline)]
    private static partial Regex CssRuleRegex();
}
