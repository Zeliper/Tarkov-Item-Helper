using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// SVG 파일의 CSS 클래스 스타일을 인라인 스타일로 변환하는 전처리기.
/// SharpVectors가 CSS 클래스를 제대로 처리하지 못하는 문제를 해결합니다.
/// 또한 특정 층(레이어)만 표시하도록 필터링할 수 있습니다.
/// </summary>
public partial class SvgStylePreprocessor
{
    /// <summary>
    /// SVG 파일을 읽어서 CSS 클래스를 인라인 스타일로 변환합니다.
    /// </summary>
    /// <param name="svgFilePath">SVG 파일 경로</param>
    /// <returns>변환된 SVG 문자열</returns>
    public string ProcessSvgFile(string svgFilePath)
    {
        var svgContent = File.ReadAllText(svgFilePath);
        return ProcessSvgContent(svgContent);
    }

    /// <summary>
    /// SVG 파일을 읽어서 CSS 클래스를 인라인 스타일로 변환하고,
    /// 특정 층(레이어)만 표시하도록 필터링합니다.
    /// </summary>
    /// <param name="svgFilePath">SVG 파일 경로</param>
    /// <param name="visibleFloorIds">표시할 층의 ID 목록. null이면 모든 층 표시.</param>
    /// <param name="allFloorIds">맵에 정의된 모든 층 ID 목록. 이 목록에 있는 층만 숨기기/보이기 처리됨.</param>
    /// <param name="backgroundFloorId">배경으로 반투명하게 표시할 층의 ID (예: "main"). null이면 배경 층 없음.</param>
    /// <param name="backgroundOpacity">배경 층의 투명도 (0.0 ~ 1.0). 기본값 0.3</param>
    /// <returns>변환된 SVG 문자열</returns>
    public string ProcessSvgFile(string svgFilePath, IEnumerable<string>? visibleFloorIds, IEnumerable<string>? allFloorIds = null, string? backgroundFloorId = null, double backgroundOpacity = 0.3)
    {
        var svgContent = File.ReadAllText(svgFilePath);
        return ProcessSvgContent(svgContent, visibleFloorIds, allFloorIds, backgroundFloorId, backgroundOpacity);
    }

    /// <summary>
    /// SVG 콘텐츠의 CSS 클래스를 인라인 스타일로 변환합니다.
    /// </summary>
    /// <param name="svgContent">원본 SVG 콘텐츠</param>
    /// <returns>변환된 SVG 콘텐츠</returns>
    public string ProcessSvgContent(string svgContent)
    {
        return ProcessSvgContent(svgContent, null, null);
    }

    /// <summary>
    /// SVG 콘텐츠의 CSS 클래스를 인라인 스타일로 변환하고,
    /// 특정 층(레이어)만 표시하도록 필터링합니다.
    /// </summary>
    /// <param name="svgContent">원본 SVG 콘텐츠</param>
    /// <param name="visibleFloorIds">표시할 층의 ID 목록. null이면 모든 층 표시.</param>
    /// <param name="allFloorIds">맵에 정의된 모든 층 ID 목록. 이 목록에 있는 층만 숨기기/보이기 처리됨.</param>
    /// <param name="backgroundFloorId">배경으로 반투명하게 표시할 층의 ID (예: "main"). null이면 배경 층 없음.</param>
    /// <param name="backgroundOpacity">배경 층의 투명도 (0.0 ~ 1.0). 기본값 0.3</param>
    /// <returns>변환된 SVG 콘텐츠</returns>
    public string ProcessSvgContent(string svgContent, IEnumerable<string>? visibleFloorIds, IEnumerable<string>? allFloorIds = null, string? backgroundFloorId = null, double backgroundOpacity = 0.3)
    {
        // 1. CSS 스타일 블록 추출 및 파싱
        var styleRules = ExtractAndParseCssStyles(svgContent);

        // 2. XML 파싱 및 클래스→스타일 변환 + 층 필터링
        var processedSvg = ConvertClassesToInlineStyles(svgContent, styleRules, visibleFloorIds, allFloorIds, backgroundFloorId, backgroundOpacity);

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

        // "fill:#144043;stroke:#000;stroke-width:2" 형식 파싱
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
    /// XML을 파싱하여 class 속성을 style 속성으로 변환하고, 층 필터링을 적용합니다.
    /// </summary>
    private string ConvertClassesToInlineStyles(
        string svgContent,
        Dictionary<string, Dictionary<string, string>> styleRules,
        IEnumerable<string>? visibleFloorIds = null,
        IEnumerable<string>? allFloorIds = null,
        string? backgroundFloorId = null,
        double backgroundOpacity = 0.3)
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

        // 네임스페이스 관리자 설정
        var nsmgr = new XmlNamespaceManager(doc.NameTable);
        nsmgr.AddNamespace("svg", "http://www.w3.org/2000/svg");

        // 층 필터링을 위한 HashSet 생성
        HashSet<string>? visibleFloors = visibleFloorIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string>? allFloors = allFloorIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // class 속성이 있는 모든 요소 처리 + 층 필터링
        ProcessElementsWithClass(doc.DocumentElement!, styleRules, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);

        // <style> 태그 제거 (선택적 - 이미 인라인으로 변환됨)
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
    /// 요소와 그 자식들의 class 속성을 style로 변환하고, 층 필터링을 적용합니다.
    /// </summary>
    /// <param name="element">처리할 XML 요소</param>
    /// <param name="styleRules">CSS 스타일 규칙</param>
    /// <param name="visibleFloors">표시할 층 ID 집합 (null이면 필터링 없음)</param>
    /// <param name="allFloors">모든 층 ID 집합 (이 집합에 있는 레이어만 숨기기/보이기 처리)</param>
    /// <param name="backgroundFloorId">배경으로 반투명하게 표시할 층의 ID</param>
    /// <param name="backgroundOpacity">배경 층의 투명도 (0.0 ~ 1.0)</param>
    private void ProcessElementsWithClass(
        XmlElement element,
        Dictionary<string, Dictionary<string, string>> styleRules,
        HashSet<string>? visibleFloors = null,
        HashSet<string>? allFloors = null,
        string? backgroundFloorId = null,
        double backgroundOpacity = 0.3)
    {
        // 층 필터링: <g id="..."> 요소에 대해 display/opacity 스타일 적용
        if (visibleFloors != null && allFloors != null && element.Name == "g")
        {
            var elementId = element.GetAttribute("id");
            if (!string.IsNullOrEmpty(elementId) && allFloors.Contains(elementId))
            {
                // 이 요소가 층 레이어인 경우
                var isVisible = visibleFloors.Contains(elementId);
                var isBackgroundLayer = !string.IsNullOrEmpty(backgroundFloorId) &&
                                        string.Equals(elementId, backgroundFloorId, StringComparison.OrdinalIgnoreCase) &&
                                        !visibleFloors.Contains(elementId); // 현재 선택된 층이 아닐 때만 배경으로 표시

                var existingStyle = element.GetAttribute("style");

                string newStyle;
                if (isVisible)
                {
                    // 선택된 층: display:block, opacity:1
                    newStyle = "display:block;opacity:1";
                }
                else if (isBackgroundLayer)
                {
                    // 배경 층: display:block, opacity 적용
                    newStyle = $"display:block;opacity:{backgroundOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }
                else
                {
                    // 숨길 층: display:none
                    newStyle = "display:none";
                }

                if (!string.IsNullOrEmpty(existingStyle))
                {
                    // 기존 스타일에서 display, opacity 속성 제거 후 새로 추가
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

            // class 속성 제거 (선택적)
            // element.RemoveAttribute("class");
        }

        // 자식 요소들 재귀 처리
        foreach (XmlNode child in element.ChildNodes)
        {
            if (child is XmlElement childElement)
            {
                ProcessElementsWithClass(childElement, styleRules, visibleFloors, allFloors, backgroundFloorId, backgroundOpacity);
            }
        }
    }

    /// <summary>
    /// 스타일 문자열에서 특정 속성을 제거합니다.
    /// </summary>
    private string RemoveStyleProperty(string style, string propertyName)
    {
        var props = ParseCssProperties(style);
        props.Remove(propertyName);
        return props.Count == 0 ? string.Empty : string.Join(";", props.Select(p => $"{p.Key}:{p.Value}"));
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
