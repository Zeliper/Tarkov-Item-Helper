using System.Diagnostics;
using System.Windows;
using TarkovHelper.Services;

namespace TarkovHelper.Windows;

/// <summary>
/// Wipe warning dialog window.
/// Warns users about potential issues when syncing after an account wipe.
/// </summary>
public partial class WipeWarningDialog : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly string _logPath;

    /// <summary>
    /// Gets whether the user chose to continue with the sync.
    /// </summary>
    public bool ShouldContinue { get; private set; }

    /// <summary>
    /// Gets whether the user chose to hide this warning in the future.
    /// </summary>
    public bool DontShowAgain { get; private set; }

    public WipeWarningDialog(string logPath)
    {
        InitializeComponent();
        _logPath = logPath;
        TxtLogPath.Text = logPath;
        UpdateLocalizedText();
    }

    /// <summary>
    /// Show the wipe warning dialog and return whether to continue.
    /// </summary>
    /// <param name="logPath">The log folder path to display.</param>
    /// <param name="owner">Optional owner window for centering.</param>
    /// <returns>True if the user chose to continue, false otherwise.</returns>
    public static bool ShowWarning(string logPath, Window? owner = null)
    {
        var dialog = new WipeWarningDialog(logPath);
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        dialog.ShowDialog();

        // Save preference if user chose to hide warning
        if (dialog.DontShowAgain)
        {
            SettingsService.Instance.HideWipeWarning = true;
        }

        return dialog.ShouldContinue;
    }

    /// <summary>
    /// Update localized text based on current language.
    /// </summary>
    private void UpdateLocalizedText()
    {
        TxtTitle.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "퀘스트 동기화 전 확인",
            AppLanguage.JA => "クエスト同期前の確認",
            _ => "Before Quest Sync"
        };

        TxtMessage.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "최근 계정 초기화(와이프)를 진행하셨나요?",
            AppLanguage.JA => "最近アカウントをリセット（ワイプ）しましたか？",
            _ => "Have you recently reset your account (wipe)?"
        };

        TxtDescription.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "계정 초기화 후 동기화를 진행하면 이전 시즌의 로그가 섞여 퀘스트 진행 상태가 올바르지 않게 표시될 수 있습니다.",
            AppLanguage.JA => "アカウントリセット後に同期すると、以前のシーズンのログが混在し、クエストの進行状況が正しく表示されない場合があります。",
            _ => "If you sync after a wipe, logs from the previous season may mix and quest progress may be displayed incorrectly."
        };

        TxtLogFolderLabel.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "📁 로그 폴더 위치:",
            AppLanguage.JA => "📁 ログフォルダの場所:",
            _ => "📁 Log folder location:"
        };

        TxtRecommendation.Text = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "💡 권장 조치: 계정 초기화 이전 날짜의 로그 폴더를 삭제하거나 다른 위치로 백업해 주세요.",
            AppLanguage.JA => "💡 推奨: ワイプ前の日付のログフォルダを削除するか、別の場所にバックアップしてください。",
            _ => "💡 Recommended: Delete or backup log folders dated before the wipe."
        };

        ChkDontShowAgain.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "이 안내를 다시 보지 않기",
            AppLanguage.JA => "この案内を再び表示しない",
            _ => "Don't show this again"
        };

        BtnOpenFolder.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "폴더 열기",
            AppLanguage.JA => "フォルダを開く",
            _ => "Open Folder"
        };

        BtnContinue.Content = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => "계속 진행",
            AppLanguage.JA => "続行",
            _ => "Continue"
        };
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        ShouldContinue = false;
        DontShowAgain = ChkDontShowAgain.IsChecked == true;
        Close();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_logPath))
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", _logPath);
        }
        catch (Exception)
        {
            // Copy path to clipboard if can't open
            try
            {
                Clipboard.SetText(_logPath);
                MessageBox.Show(
                    _loc.CurrentLanguage switch
                    {
                        AppLanguage.KO => "폴더를 열 수 없습니다. 경로가 클립보드에 복사되었습니다.",
                        AppLanguage.JA => "フォルダを開けませんでした。パスがクリップボードにコピーされました。",
                        _ => "Could not open folder. Path has been copied to clipboard."
                    },
                    _loc.CurrentLanguage switch { AppLanguage.KO => "알림", AppLanguage.JA => "通知", _ => "Notice" },
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch
            {
                // Ignore clipboard errors
            }
        }
    }

    private void BtnContinue_Click(object sender, RoutedEventArgs e)
    {
        ShouldContinue = true;
        DontShowAgain = ChkDontShowAgain.IsChecked == true;
        Close();
    }
}
