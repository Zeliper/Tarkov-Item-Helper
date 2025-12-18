using System.Windows;
using TarkovDBEditor.Services;

// Type disambiguation for WPF + WindowsForms
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace TarkovDBEditor.Views;

public partial class DataPublishWindow : Window
{
    private DataPublishService? _service;
    private DataPublishService.ComparisonResult? _comparisonResult;

    public DataPublishWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _service = new DataPublishService();

        TxtSourcePath.Text = _service.SourceBasePath;
        TxtTargetPath.Text = _service.TargetBasePath;

        await RefreshComparison();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshComparison();
    }

    private async Task RefreshComparison()
    {
        if (_service == null) return;

        IsEnabled = false;
        TxtStatus.Text = "Comparing files...";
        BtnPublish.IsEnabled = false;

        try
        {
            _comparisonResult = await _service.CompareAsync(
                progress => Dispatcher.Invoke(() => TxtStatus.Text = progress));

            if (_comparisonResult.Success)
            {
                UpdateUI();
            }
            else
            {
                TxtStatus.Text = $"Error: {_comparisonResult.ErrorMessage}";
                MessageBox.Show(
                    _comparisonResult.ErrorMessage,
                    "Comparison Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error: {ex.Message}";
            MessageBox.Show(
                ex.Message,
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void UpdateUI()
    {
        if (_comparisonResult == null) return;

        // Version info
        TxtCurrentVersion.Text = _comparisonResult.CurrentVersion ?? "-";
        TxtNewVersion.Text = _comparisonResult.NewVersion ?? "1.0.0";

        // Database section
        UpdateSectionStatus(
            DbStatusIcon, DbStatusText,
            _comparisonResult.DbChanged,
            _comparisonResult.DbExists ? "Changed" : "Not found",
            "No changes");

        if (_comparisonResult.DbExists)
        {
            TxtDbSourceInfo.Text = $"{FormatSize(_comparisonResult.SourceDbSize)} - Hash: {_comparisonResult.SourceDbHash?[..8]}...";
            TxtDbTargetInfo.Text = _comparisonResult.TargetDbHash != null
                ? $"{FormatSize(_comparisonResult.TargetDbSize)} - Hash: {_comparisonResult.TargetDbHash[..8]}..."
                : "Not found (will be created)";
        }
        else
        {
            TxtDbSourceInfo.Text = "Database not found in source";
            TxtDbTargetInfo.Text = "-";
        }

        // Map configs section
        UpdateSectionStatus(
            ConfigStatusIcon, ConfigStatusText,
            _comparisonResult.MapConfigsChanged,
            "Changed",
            "No changes");

        // Map SVGs section
        var svgChanges = _comparisonResult.MapSvgAdded + _comparisonResult.MapSvgUpdated;
        UpdateSectionStatus(
            MapSvgStatusIcon, MapSvgStatusText,
            svgChanges > 0,
            $"{_comparisonResult.MapSvgAdded} added, {_comparisonResult.MapSvgUpdated} updated",
            $"{_comparisonResult.MapSvgUnchanged} files (no changes)");

        MapSvgList.ItemsSource = _comparisonResult.MapSvgChanges
            .Select(c => new FileChangeDisplay(c))
            .ToList();

        // Marker icons section
        var markerChanges = _comparisonResult.MarkerIconAdded + _comparisonResult.MarkerIconUpdated;
        UpdateSectionStatus(
            MarkerIconStatusIcon, MarkerIconStatusText,
            markerChanges > 0,
            $"{_comparisonResult.MarkerIconAdded} added, {_comparisonResult.MarkerIconUpdated} updated",
            $"{_comparisonResult.MarkerIconUnchanged} files (no changes)");

        MarkerIconList.ItemsSource = _comparisonResult.MarkerIconChanges
            .Select(c => new FileChangeDisplay(c))
            .ToList();

        // Item icons section
        var itemIconChanges = _comparisonResult.ItemIconAdded + _comparisonResult.ItemIconUpdated;
        UpdateSectionStatus(
            ItemIconStatusIcon, ItemIconStatusText,
            itemIconChanges > 0,
            $"{_comparisonResult.ItemIconAdded} added, {_comparisonResult.ItemIconUpdated} updated",
            $"{_comparisonResult.ItemIconUnchanged} files (no changes)");

        ItemIconSummary.Text = itemIconChanges > 0
            ? $"Total {_comparisonResult.ItemIconAdded + _comparisonResult.ItemIconUpdated + _comparisonResult.ItemIconUnchanged} icon files. " +
              $"{_comparisonResult.ItemIconAdded} new, {_comparisonResult.ItemIconUpdated} updated will be copied."
            : $"Total {_comparisonResult.ItemIconUnchanged} icon files. All files are up to date.";

        // Hideout icons section
        var hideoutChanges = _comparisonResult.HideoutIconAdded + _comparisonResult.HideoutIconUpdated;
        UpdateSectionStatus(
            HideoutIconStatusIcon, HideoutIconStatusText,
            hideoutChanges > 0,
            $"{_comparisonResult.HideoutIconAdded} added, {_comparisonResult.HideoutIconUpdated} updated",
            $"{_comparisonResult.HideoutIconUnchanged} files (no changes)");

        HideoutIconList.ItemsSource = _comparisonResult.HideoutIconChanges
            .Select(c => new FileChangeDisplay(c))
            .ToList();

        // Summary
        if (_comparisonResult.HasAnyChanges)
        {
            TxtSummary.Text = $"Total {_comparisonResult.TotalChanges} changes to publish";
            TxtStatus.Text = "Review changes and click Publish to update TarkovHelper";
            BtnPublish.IsEnabled = true;
        }
        else
        {
            TxtSummary.Text = "All files are up to date";
            TxtStatus.Text = "No changes to publish";
            BtnPublish.IsEnabled = false;
        }
    }

    private void UpdateSectionStatus(
        System.Windows.Controls.TextBlock icon,
        System.Windows.Controls.TextBlock text,
        bool hasChanges,
        string changedText,
        string unchangedText)
    {
        if (hasChanges)
        {
            icon.Text = "●";
            icon.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // Yellow
            text.Text = $"- {changedText}";
            text.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
        }
        else
        {
            icon.Text = "✓";
            icon.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
            text.Text = $"- {unchangedText}";
            text.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        }
    }

    private async void BtnPublish_Click(object sender, RoutedEventArgs e)
    {
        if (_service == null || _comparisonResult == null) return;

        var newVersion = TxtNewVersion.Text.Trim();
        if (string.IsNullOrEmpty(newVersion))
        {
            MessageBox.Show("Please enter a new version number.", "Version Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"This will publish the following changes to TarkovHelper:\n\n" +
            $"• Database: {(_comparisonResult.DbChanged ? "Will be updated" : "No changes")}\n" +
            $"• Map Configs: {(_comparisonResult.MapConfigsChanged ? "Will be updated" : "No changes")}\n" +
            $"• Map SVGs: {_comparisonResult.MapSvgAdded} added, {_comparisonResult.MapSvgUpdated} updated\n" +
            $"• Marker Icons: {_comparisonResult.MarkerIconAdded} added, {_comparisonResult.MarkerIconUpdated} updated\n" +
            $"• Item Icons: {_comparisonResult.ItemIconAdded} added, {_comparisonResult.ItemIconUpdated} updated\n" +
            $"• Hideout Icons: {_comparisonResult.HideoutIconAdded} added, {_comparisonResult.HideoutIconUpdated} updated\n\n" +
            $"New version: {newVersion}\n\n" +
            $"Continue?",
            "Confirm Publish",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsEnabled = false;
        BtnPublish.IsEnabled = false;

        try
        {
            var result = await _service.PublishAsync(
                _comparisonResult,
                newVersion,
                progress => Dispatcher.Invoke(() => TxtStatus.Text = progress));

            if (result.Success)
            {
                TxtStatus.Text = $"Published successfully: {result.FilesCopied} files, {result.IconsCopied} icons";
                TxtCurrentVersion.Text = newVersion;
                TxtSummary.Text = "Published!";
                TxtSummary.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

                MessageBox.Show(
                    $"Publish completed successfully!\n\n" +
                    $"Files copied: {result.FilesCopied}\n" +
                    $"Icons copied: {result.IconsCopied}\n" +
                    $"New version: {result.NewVersion}\n\n" +
                    $"Target: {_service.TargetBasePath}",
                    "Publish Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Refresh to show updated state
                await RefreshComparison();
            }
            else
            {
                TxtStatus.Text = $"Publish failed: {result.ErrorMessage}";
                MessageBox.Show(
                    $"Publish failed:\n{result.ErrorMessage}\n\n" +
                    string.Join("\n", result.Errors),
                    "Publish Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Publish failed: {ex.Message}";
            MessageBox.Show(
                $"Publish failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _service?.Dispose();
        Close();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    protected override void OnClosed(EventArgs e)
    {
        _service?.Dispose();
        base.OnClosed(e);
    }
}

/// <summary>
/// Display model for file changes in the UI
/// </summary>
public class FileChangeDisplay
{
    public string FileName { get; set; }
    public string TypeText { get; set; }
    public Brush TypeColor { get; set; }

    public FileChangeDisplay(DataPublishService.FileChangeInfo info)
    {
        FileName = info.FileName;

        switch (info.Type)
        {
            case DataPublishService.ChangeType.Added:
                TypeText = "New";
                TypeColor = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
                break;
            case DataPublishService.ChangeType.Updated:
                TypeText = "Updated";
                TypeColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // Yellow
                break;
            default:
                TypeText = "No changes";
                TypeColor = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)); // Gray
                break;
        }
    }
}
