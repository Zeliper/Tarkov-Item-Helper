using System.Windows;
using System.Windows.Media;
using TarkovHelper.Services;

namespace TarkovHelper.Windows;

/// <summary>
/// Migration result dialog window.
/// Displays the results of a data migration operation.
/// </summary>
public partial class MigrationResultDialog : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public MigrationResultDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the dialog with migration results.
    /// </summary>
    /// <param name="result">The migration result to display.</param>
    /// <param name="owner">Optional owner window for centering.</param>
    public static void Show(ConfigMigrationService.MigrationResult result, Window? owner = null)
    {
        var dialog = new MigrationResultDialog();
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        dialog.SetResult(result);
        dialog.ShowDialog();
    }

    /// <summary>
    /// Set the migration result to display.
    /// </summary>
    private void SetResult(ConfigMigrationService.MigrationResult result)
    {
        UpdateLocalizedText(result);

        // Update counts
        TxtMigrationQuestCount.Text = result.QuestProgressCount.ToString();
        TxtMigrationHideoutCount.Text = result.HideoutProgressCount.ToString();
        TxtMigrationInventoryCount.Text = result.ItemInventoryCount.ToString();
        TxtMigrationSettingsCount.Text = result.SettingsCount.ToString();

        TxtMigrationTotalCount.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => $"{result.TotalCount}개 항목",
            AppLanguage.JA => $"{result.TotalCount}件",
            _ => $"{result.TotalCount} items"
        };

        // Show warnings if any
        if (result.HasWarnings || result.HasErrors)
        {
            var allMessages = result.Errors.Concat(result.Warnings).ToList();
            MigrationWarningsList.ItemsSource = allMessages;
            MigrationWarningsSection.Visibility = Visibility.Visible;

            TxtMigrationWarningsHeader.Text = result.HasErrors
                ? _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "오류 및 경고",
                    AppLanguage.JA => "エラーと警告",
                    _ => "Errors & Warnings"
                }
                : _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => "경고",
                    AppLanguage.JA => "警告",
                    _ => "Warnings"
                };

            TxtMigrationWarningsHeader.Foreground = result.HasErrors
                ? new SolidColorBrush(Color.FromRgb(239, 83, 80)) // Red
                : new SolidColorBrush(Color.FromRgb(255, 167, 38)); // Orange
        }
        else
        {
            MigrationWarningsSection.Visibility = Visibility.Collapsed;
        }

        // Update icon based on result
        if (result.HasErrors)
        {
            TxtMigrationResultIcon.Text = "";
            TxtMigrationResultTitle.Foreground = new SolidColorBrush(Color.FromRgb(239, 83, 80));
        }
        else if (result.HasWarnings)
        {
            TxtMigrationResultIcon.Text = "";
            TxtMigrationResultTitle.Foreground = new SolidColorBrush(Color.FromRgb(255, 167, 38));
        }
        else
        {
            TxtMigrationResultIcon.Text = "";
            TxtMigrationResultTitle.Foreground = (Brush)FindResource("AccentBrush");
        }
    }

    /// <summary>
    /// Update localized text based on current language.
    /// </summary>
    private void UpdateLocalizedText(ConfigMigrationService.MigrationResult result)
    {
        TxtMigrationResultTitle.Text = result.HasErrors
            ? _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "마이그레이션 실패",
                AppLanguage.JA => "移行失敗",
                _ => "Migration Failed"
            }
            : _loc.CurrentLanguage switch
            {
                AppLanguage.KO => "마이그레이션 완료",
                AppLanguage.JA => "移行完了",
                _ => "Migration Complete"
            };

        TxtMigrationQuestLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 진행",
            AppLanguage.JA => "クエスト進行",
            _ => "Quest Progress"
        };

        TxtMigrationHideoutLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "하이드아웃 진행",
            AppLanguage.JA => "ハイドアウト進行",
            _ => "Hideout Progress"
        };

        TxtMigrationInventoryLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "아이템 인벤토리",
            AppLanguage.JA => "アイテムインベントリ",
            _ => "Item Inventory"
        };

        TxtMigrationSettingsLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "설정",
            AppLanguage.JA => "設定",
            _ => "Settings"
        };

        TxtMigrationTotalLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "총 가져온 항목: ",
            AppLanguage.JA => "インポート合計: ",
            _ => "Total imported: "
        };

        BtnOk.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "확인",
            AppLanguage.JA => "確認",
            _ => "OK"
        };
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
