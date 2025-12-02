using System.Text;
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
        try
        {
            var tarkovService = TarkovDataService.Instance;
            var sb = new StringBuilder();

            sb.AppendLine("Starting data refresh...\n");

            var result = await tarkovService.RefreshAllDataAsync(message =>
            {
                sb.AppendLine(message);
            });

            sb.AppendLine();
            sb.AppendLine("=== Result Summary ===");

            if (result.Success)
            {
                sb.AppendLine($"Wiki Quests: {result.TotalQuestsInWiki}");
                sb.AppendLine($"Quest Pages: {result.QuestPagesDownloaded} downloaded, {result.QuestPagesSkipped} skipped, {result.QuestPagesFailed} failed");
                sb.AppendLine($"Total Tasks: {result.TotalTasksMerged}");
                sb.AppendLine($"  - With API ID: {result.TasksWithApiId}");
                sb.AppendLine($"  - Wiki-only: {result.WikiOnlyTasks}");
                sb.AppendLine($"  - Kappa Required: {result.KappaRequiredTasks}");

                if (result.MissingApiTasks > 0)
                {
                    sb.AppendLine($"  - API tasks without wiki: {result.MissingApiTasks}");
                }

                if (result.FailedQuestPages.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("=== Failed Quest Pages ===");
                    foreach (var quest in result.FailedQuestPages.Take(10))
                    {
                        sb.AppendLine($"  - {quest}");
                    }
                    if (result.FailedQuestPages.Count > 10)
                    {
                        sb.AppendLine($"  ... and {result.FailedQuestPages.Count - 10} more");
                    }
                }
            }
            else
            {
                sb.AppendLine($"Error: {result.ErrorMessage}");
            }

            MessageBox.Show(sb.ToString(), "Refresh Data", MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error refreshing data:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
