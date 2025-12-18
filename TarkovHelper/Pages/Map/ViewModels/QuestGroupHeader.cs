using System.Windows;
using System.Windows.Media;

namespace TarkovHelper.Pages.Map.ViewModels;

/// <summary>
/// 퀘스트 그룹 헤더 (그룹화 시 사용)
/// </summary>
public class QuestGroupHeader
{
    public string QuestName { get; set; } = string.Empty;
    public string QuestId { get; set; } = string.Empty;
    public string Progress { get; set; } = string.Empty;
    public bool IsFullyCompleted { get; set; }
    public Brush HeaderBrush => IsFullyCompleted
        ? new SolidColorBrush(Color.FromRgb(76, 175, 80))  // Green
        : new SolidColorBrush(Color.FromRgb(197, 168, 74)); // Accent
    public TextDecorationCollection? TextDecoration => IsFullyCompleted ? TextDecorations.Strikethrough : null;
    public double Opacity => IsFullyCompleted ? 0.6 : 1.0;

    /// <summary>
    /// Wiki 링크 (DB의 WikiPageLink 사용)
    /// </summary>
    public string? WikiLink { get; set; }

    public bool HasWikiLink => !string.IsNullOrEmpty(WikiLink);
}
