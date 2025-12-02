using System.IO;
using System.Windows;
using TarkovHelper.Services;

namespace TarkovHelper.Debug;

/// <summary>
/// Debug 모드에서 Toolbox 창에 표시될 테스트 함수들을 정의합니다.
/// [TestMenu] 어트리뷰트가 붙은 public 메서드가 버튼으로 표시됩니다.
/// </summary>
public static class TestMenu
{
    /// <summary>
    /// MainWindow 인스턴스 (Toolbox에서 주입)
    /// </summary>
    public static Window? MainWindow { get; set; }

    [TestMenu("Refresh Data")]
    public static async Task RefreshData()
    {
        if (MainWindow is TarkovHelper.MainWindow mainWindow)
        {
            await mainWindow.RefreshDataWithOverlayAsync();
        }
        else
        {
            MessageBox.Show("MainWindow not available", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [TestMenu("Test Documents Parser")]
    public static Task TestDocumentsParser()
    {
        var wikiPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "QuestPages", "Documents.wiki");
        if (!File.Exists(wikiPath))
        {
            MessageBox.Show($"File not found: {wikiPath}", "Error");
            return Task.CompletedTask;
        }

        var content = File.ReadAllText(wikiPath);
        var items = WikiQuestParser.ParseRequiredItems(content);

        var result = items == null
            ? "No items found (all quest items correctly filtered)"
            : $"Found {items.Count} items:\n" + string.Join("\n", items.Select(i => $"- {i.ItemNormalizedName} x{i.Amount}"));

        MessageBox.Show(result, "Documents Parser Test");
        return Task.CompletedTask;
    }
}
