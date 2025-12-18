using System.Windows;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;

namespace TarkovHelper.Windows.Dialogs;

/// <summary>
/// Overlay settings window
/// </summary>
public partial class OverlaySettingsWindow : Window
{
    private readonly OverlayMiniMapSettings _settings;
    private readonly OverlayMiniMapWindow? _overlayWindow;
    private bool _isInitializing = true;

    /// <summary>
    /// Settings applied event
    /// </summary>
    public event Action<OverlayMiniMapSettings>? SettingsApplied;

    public OverlaySettingsWindow(OverlayMiniMapSettings settings, OverlayMiniMapWindow? overlayWindow)
    {
        InitializeComponent();

        _settings = settings.Clone();
        _overlayWindow = overlayWindow;

        LoadSettings();
        _isInitializing = false;
    }

    private void LoadSettings()
    {
        SliderOpacity.Value = _settings.Opacity * 100;
        SliderZoom.Value = _settings.ZoomLevel * 100;

        RbFixed.IsChecked = _settings.ViewMode == MiniMapViewMode.Fixed;
        RbTracking.IsChecked = _settings.ViewMode == MiniMapViewMode.PlayerTracking;

        ChkQuestMarkers.IsChecked = _settings.ShowQuestMarkers;
        ChkExtractMarkers.IsChecked = _settings.ShowExtractMarkers;
        ChkClickThrough.IsChecked = _settings.ClickThrough;

        // SettingsService에서 MapPage와 동기화되는 설정 로드
        var settingsService = SettingsService.Instance;
        ChkShowPmcExtracts.IsChecked = settingsService.MapShowPmcExtracts;
        ChkShowScavExtracts.IsChecked = settingsService.MapShowScavExtracts;
        ChkShowTransitExtracts.IsChecked = settingsService.MapShowTransits;
        ChkHideCompletedObjectives.IsChecked = settingsService.MapHideCompletedObjectives;

        UpdateDisplays();
    }

    private void UpdateDisplays()
    {
        if (TxtOpacity != null)
            TxtOpacity.Text = $"{(int)SliderOpacity.Value}%";
        if (TxtZoom != null)
            TxtZoom.Text = $"{SliderZoom.Value / 100:F2}x";
    }

    private void ApplySettings()
    {
        if (_isInitializing) return;

        _settings.Opacity = SliderOpacity.Value / 100.0;
        _settings.ZoomLevel = SliderZoom.Value / 100.0;
        _settings.ViewMode = RbTracking.IsChecked == true ? MiniMapViewMode.PlayerTracking : MiniMapViewMode.Fixed;
        _settings.ShowQuestMarkers = ChkQuestMarkers.IsChecked == true;
        _settings.ShowExtractMarkers = ChkExtractMarkers.IsChecked == true;
        _settings.ClickThrough = ChkClickThrough.IsChecked == true;

        SettingsApplied?.Invoke(_settings);

        // Apply to overlay window immediately
        ApplyToOverlay();
    }

    private void ApplyToOverlay()
    {
        if (_overlayWindow == null) return;

        // Update opacity directly
        _overlayWindow.Dispatcher.Invoke(() =>
        {
            if (_overlayWindow.FindName("MainBorder") is System.Windows.Controls.Border border)
            {
                border.Opacity = _settings.Opacity;
            }
        });
    }

    #region Event Handlers

    private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateDisplays();
        ApplySettings();
    }

    private void SliderZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateDisplays();
        ApplySettings();
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
    }

    private void Marker_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        _overlayWindow?.RefreshMap();
    }

    private void ClickThrough_Changed(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        _overlayWindow?.ToggleClickThrough();
    }

    private void ExtractFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        // SettingsService에 저장 (MapPage와 동기화)
        var settingsService = SettingsService.Instance;
        settingsService.MapShowPmcExtracts = ChkShowPmcExtracts.IsChecked == true;
        settingsService.MapShowScavExtracts = ChkShowScavExtracts.IsChecked == true;
        settingsService.MapShowTransits = ChkShowTransitExtracts.IsChecked == true;

        // 오버레이 맵 새로고침
        _overlayWindow?.RefreshMap();
    }

    private void HideCompleted_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        // SettingsService에 저장 (MapPage와 동기화)
        SettingsService.Instance.MapHideCompletedObjectives = ChkHideCompletedObjectives.IsChecked == true;

        // 오버레이 맵 새로고침
        _overlayWindow?.RefreshMap();
    }

    private void BtnCenterPlayer_Click(object sender, RoutedEventArgs e)
    {
        _overlayWindow?.ToggleViewMode();

        // Update UI to reflect the change
        if (_overlayWindow != null)
        {
            RbTracking.IsChecked = true;
        }
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        _settings.ResetToDefaults();
        _isInitializing = true;
        LoadSettings();
        _isInitializing = false;
        ApplySettings();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    #endregion
}
