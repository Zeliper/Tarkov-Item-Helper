using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace TarkovHelper.Services
{
    /// <summary>
    /// Helper class for parsing and rendering wiki markup text.
    /// Supports: [[Link|Text]], [[Link]], &lt;font color="..."&gt;text&lt;/font&gt;
    /// </summary>
    public static class WikiMarkupHelper
    {
        /// <summary>
        /// Parse wiki markup and populate a TextBlock with rich content.
        /// Supports wiki links ([[Link|Text]], [[Link]]) and font color tags.
        /// </summary>
        /// <param name="text">The wiki markup text to parse</param>
        /// <param name="textBlock">The TextBlock to populate with parsed content</param>
        /// <param name="defaultBrush">Default text color brush</param>
        /// <param name="linkBrush">Link text color brush</param>
        public static void ParseWikiMarkup(string text, TextBlock textBlock, Brush defaultBrush, Brush linkBrush)
        {
            // Pattern to match wiki links and font color tags
            var pattern = @"(\[\[([^\]|]+)(?:\|([^\]]+))?\]\])|(<font\s+color=""([^""]+)"">([^<]+)</font>)";
            var regex = new Regex(pattern, RegexOptions.IgnoreCase);

            int lastIndex = 0;
            var matches = regex.Matches(text);

            foreach (Match match in matches)
            {
                // Add text before match
                if (match.Index > lastIndex)
                {
                    var beforeText = text.Substring(lastIndex, match.Index - lastIndex);
                    textBlock.Inlines.Add(new Run(beforeText) { Foreground = defaultBrush });
                }

                if (match.Groups[1].Success)
                {
                    // Wiki link: [[Link|Text]] or [[Link]]
                    var linkTarget = match.Groups[2].Value;
                    var displayText = match.Groups[3].Success ? match.Groups[3].Value : linkTarget;

                    var hyperlink = new Hyperlink
                    {
                        Foreground = linkBrush,
                        TextDecorations = null
                    };
                    hyperlink.Tag = linkTarget;
                    hyperlink.Click += WikiLink_Click;
                    hyperlink.MouseEnter += (s, e) => ((Hyperlink)s).TextDecorations = System.Windows.TextDecorations.Underline;
                    hyperlink.MouseLeave += (s, e) => ((Hyperlink)s).TextDecorations = null;

                    // Parse display text for nested font tags
                    ParseHyperlinkContent(displayText, hyperlink, linkBrush);

                    textBlock.Inlines.Add(hyperlink);
                }
                else if (match.Groups[4].Success)
                {
                    // Font color: <font color="...">text</font>
                    var colorStr = match.Groups[5].Value;
                    var coloredText = match.Groups[6].Value;

                    Brush colorBrush;
                    try
                    {
                        colorBrush = new SolidColorBrush(
                            (Color)ColorConverter.ConvertFromString(colorStr));
                    }
                    catch
                    {
                        colorBrush = defaultBrush;
                    }

                    textBlock.Inlines.Add(new Run(coloredText) { Foreground = colorBrush });
                }

                lastIndex = match.Index + match.Length;
            }

            // Add remaining text
            if (lastIndex < text.Length)
            {
                var remainingText = text.Substring(lastIndex);
                textBlock.Inlines.Add(new Run(remainingText) { Foreground = defaultBrush });
            }
        }

        /// <summary>
        /// Parse content for a hyperlink, handling nested font color tags.
        /// </summary>
        private static void ParseHyperlinkContent(string displayText, Hyperlink hyperlink, Brush defaultBrush)
        {
            // Pattern to match font color tags
            var fontPattern = @"<font\s+color=""([^""]+)"">([^<]+)</font>";
            var fontRegex = new Regex(fontPattern, RegexOptions.IgnoreCase);

            int lastIndex = 0;
            var matches = fontRegex.Matches(displayText);

            foreach (Match match in matches)
            {
                // Add text before match
                if (match.Index > lastIndex)
                {
                    var beforeText = displayText.Substring(lastIndex, match.Index - lastIndex);
                    hyperlink.Inlines.Add(new Run(beforeText) { Foreground = defaultBrush });
                }

                // Add colored text
                var colorStr = match.Groups[1].Value;
                var coloredText = match.Groups[2].Value;

                Brush colorBrush;
                try
                {
                    colorBrush = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString(colorStr));
                }
                catch
                {
                    colorBrush = defaultBrush;
                }

                hyperlink.Inlines.Add(new Run(coloredText) { Foreground = colorBrush });
                lastIndex = match.Index + match.Length;
            }

            // Add remaining text
            if (lastIndex < displayText.Length)
            {
                var remainingText = displayText.Substring(lastIndex);
                hyperlink.Inlines.Add(new Run(remainingText) { Foreground = defaultBrush });
            }

            // If no content was added (no font tags and no plain text), add displayText as-is
            if (hyperlink.Inlines.Count == 0)
            {
                hyperlink.Inlines.Add(new Run(displayText) { Foreground = defaultBrush });
            }
        }

        /// <summary>
        /// Handle wiki link click to open in browser.
        /// </summary>
        private static void WikiLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Hyperlink hyperlink && hyperlink.Tag is string linkTarget)
            {
                var wikiUrl = $"https://escapefromtarkov.fandom.com/wiki/{Uri.EscapeDataString(linkTarget.Replace(" ", "_"))}";
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = wikiUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        /// <summary>
        /// Create a TextBlock with rich text from wiki markup (with bullet point).
        /// </summary>
        /// <param name="wikiText">The wiki markup text</param>
        /// <param name="fontFamily">Font family to use</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="defaultBrush">Default text color</param>
        /// <param name="accentBrush">Accent/link color</param>
        /// <returns>A populated TextBlock</returns>
        public static TextBlock CreateRichTextBlock(
            string wikiText,
            FontFamily fontFamily,
            double fontSize,
            Brush defaultBrush,
            Brush accentBrush)
        {
            var textBlock = new TextBlock
            {
                FontFamily = fontFamily,
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 2),
                MaxWidth = 300
            };

            // Add bullet point
            textBlock.Inlines.Add(new Run("• ")
            {
                Foreground = accentBrush,
                FontWeight = FontWeights.Bold
            });

            // Parse and add content
            ParseWikiMarkup(wikiText, textBlock, defaultBrush, accentBrush);

            return textBlock;
        }

        /// <summary>
        /// Create a TextBlock with rich text but without bullet point (for checkbox items).
        /// </summary>
        /// <param name="wikiText">The wiki markup text</param>
        /// <param name="fontFamily">Font family to use</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="defaultBrush">Default text color</param>
        /// <param name="accentBrush">Accent/link color</param>
        /// <param name="isCompleted">Whether the item is completed (applies strikethrough)</param>
        /// <param name="maxWidth">Maximum width of the TextBlock</param>
        /// <returns>A populated TextBlock</returns>
        public static TextBlock CreateRichTextBlockWithoutBullet(
            string wikiText,
            FontFamily fontFamily,
            double fontSize,
            Brush defaultBrush,
            Brush accentBrush,
            bool isCompleted = false,
            double maxWidth = 260)
        {
            var textBlock = new TextBlock
            {
                FontFamily = fontFamily,
                FontSize = fontSize,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = maxWidth,
                VerticalAlignment = VerticalAlignment.Top,
                TextDecorations = isCompleted ? System.Windows.TextDecorations.Strikethrough : null,
                Opacity = isCompleted ? 0.6 : 1.0
            };

            // Parse and add content (no bullet)
            ParseWikiMarkup(wikiText, textBlock, defaultBrush, accentBrush);

            return textBlock;
        }

        /// <summary>
        /// Check if the text contains the optional marker pattern.
        /// </summary>
        /// <param name="text">Text to check</param>
        /// <returns>True if optional pattern is found</returns>
        public static bool IsOptional(string text)
        {
            var optionalPattern = @"\(''Optional''\)\s*";
            return Regex.IsMatch(text, optionalPattern);
        }

        /// <summary>
        /// Remove the optional marker from text.
        /// </summary>
        /// <param name="text">Text with optional marker</param>
        /// <returns>Cleaned text</returns>
        public static string RemoveOptionalMarker(string text)
        {
            var optionalPattern = @"\(''Optional''\)\s*";
            return Regex.Replace(text, optionalPattern, "").Trim();
        }
    }
}
