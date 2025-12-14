using System.Windows;
using Microsoft.Win32;
using TarkovDBEditor.Services;

namespace TarkovDBEditor.Views;

/// <summary>
/// Screenshot Watcher Settings Dialog - 스크린샷 폴더 및 패턴 설정
/// </summary>
public partial class ScreenshotWatcherSettingsDialog : Window
{
    private readonly AppSettingsService _settingsService = AppSettingsService.Instance;
    private readonly ScreenshotWatcherService _watcherService = ScreenshotWatcherService.Instance;

    public ScreenshotWatcherSettingsDialog()
    {
        InitializeComponent();

        Loaded += ScreenshotWatcherSettingsDialog_Loaded;
        Closed += ScreenshotWatcherSettingsDialog_Closed;
    }

    private async void ScreenshotWatcherSettingsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // 저장된 설정 로드
        var folderPath = await _settingsService.GetAsync(AppSettingsService.ScreenshotWatcherPath, "");
        var pattern = await _settingsService.GetAsync(AppSettingsService.ScreenshotWatcherPattern, ScreenshotCoordinateParser.DefaultPattern);

        FolderPathTextBox.Text = folderPath;
        PatternTextBox.Text = pattern;

        // 이벤트 구독
        _watcherService.PositionDetected += OnPositionDetected;
        _watcherService.StateChanged += OnWatcherStateChanged;

        UpdateWatcherStatus();
    }

    private void ScreenshotWatcherSettingsDialog_Closed(object? sender, EventArgs e)
    {
        // 이벤트 구독 해제
        _watcherService.PositionDetected -= OnPositionDetected;
        _watcherService.StateChanged -= OnWatcherStateChanged;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select EFT Screenshot Folder",
            ShowNewFolderButton = false
        };

        if (!string.IsNullOrEmpty(FolderPathTextBox.Text) && System.IO.Directory.Exists(FolderPathTextBox.Text))
        {
            dialog.SelectedPath = FolderPathTextBox.Text;
        }

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void AutoDetectButton_Click(object sender, RoutedEventArgs e)
    {
        var detectedPath = _watcherService.DetectDefaultScreenshotFolder();

        if (!string.IsNullOrEmpty(detectedPath))
        {
            FolderPathTextBox.Text = detectedPath;
            MessageBox.Show($"Detected EFT screenshot folder:\n{detectedPath}", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Could not auto-detect EFT screenshot folder.\nPlease select manually.", "Auto-Detect", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ResetPatternButton_Click(object sender, RoutedEventArgs e)
    {
        PatternTextBox.Text = ScreenshotCoordinateParser.DefaultPattern;
    }

    private void StartWatcherButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPath = FolderPathTextBox.Text.Trim();

        if (string.IsNullOrEmpty(folderPath))
        {
            MessageBox.Show("Please enter or select a screenshot folder path.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!System.IO.Directory.Exists(folderPath))
        {
            MessageBox.Show($"Folder does not exist:\n{folderPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 패턴 업데이트 시도
        var pattern = PatternTextBox.Text.Trim();
        if (!string.IsNullOrEmpty(pattern) && !_watcherService.UpdatePattern(pattern))
        {
            MessageBox.Show("Invalid regex pattern. Using default pattern.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            PatternTextBox.Text = ScreenshotCoordinateParser.DefaultPattern;
        }

        if (_watcherService.StartWatching(folderPath))
        {
            UpdateWatcherStatus();
        }
        else
        {
            MessageBox.Show("Failed to start watcher. Check the folder path.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopWatcherButton_Click(object sender, RoutedEventArgs e)
    {
        _watcherService.StopWatching();
        UpdateWatcherStatus();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var folderPath = FolderPathTextBox.Text.Trim();
        var pattern = PatternTextBox.Text.Trim();

        // 유효성 검사
        if (!string.IsNullOrEmpty(pattern) && !ScreenshotCoordinateParser.IsValidPattern(pattern))
        {
            MessageBox.Show("Invalid regex pattern. Pattern must contain 'x' and 'y' named groups.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 설정 저장
        await _settingsService.SetAsync(AppSettingsService.ScreenshotWatcherPath, folderPath);
        await _settingsService.SetAsync(AppSettingsService.ScreenshotWatcherPattern,
            string.IsNullOrEmpty(pattern) ? ScreenshotCoordinateParser.DefaultPattern : pattern);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnPositionDetected(object? sender, PositionDetectedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            LastPositionText.Text = $"Last Position: X:{e.Position.X:F1}, Y:{e.Position.Y:F1}, Z:{e.Position.Z:F1}" +
                (e.Position.Angle.HasValue ? $", Angle:{e.Position.Angle:F1}°" : "");
        });
    }

    private void OnWatcherStateChanged(object? sender, WatcherStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateWatcherStatus();
        });
    }

    private void UpdateWatcherStatus()
    {
        if (_watcherService.IsWatching)
        {
            WatcherStatusText.Text = $"Watcher Status: Running - {_watcherService.CurrentWatchPath}";
            WatcherStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x70, 0xA8, 0x00));
            StartWatcherButton.IsEnabled = false;
            StopWatcherButton.IsEnabled = true;
        }
        else
        {
            WatcherStatusText.Text = "Watcher Status: Not running";
            WatcherStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));
            StartWatcherButton.IsEnabled = true;
            StopWatcherButton.IsEnabled = false;
        }

        var position = _watcherService.CurrentPosition;
        if (position != null)
        {
            LastPositionText.Text = $"Last Position: X:{position.X:F1}, Y:{position.Y:F1}, Z:{position.Z:F1}" +
                (position.Angle.HasValue ? $", Angle:{position.Angle:F1}°" : "");
        }
        else
        {
            LastPositionText.Text = "Last Position: --";
        }
    }
}
