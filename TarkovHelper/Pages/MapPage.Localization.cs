using System.Windows.Controls;
using TarkovHelper.Services;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - Localization partial class
/// </summary>
public partial class MapPage : UserControl
{
    #region Localization

    private void OnLanguageChanged(object? sender, AppLanguage e)
    {
        UpdateLocalization();
    }

    /// <summary>
    /// Updates all UI text elements with localized strings
    /// </summary>
    private void UpdateLocalization()
    {
        // Top Toolbar
        if (BtnToggleQuestPanel != null)
            BtnToggleQuestPanel.ToolTip = _loc.QuestPanelTooltip;
        if (BtnSettings != null)
            BtnSettings.ToolTip = _loc.SettingsTooltip;

        // Drawer
        UpdateDrawerLocalization();

        // Settings Panel
        UpdateSettingsLocalization();

        // Quest Filter Popup
        UpdateQuestFilterLocalization();

        // Legend Popup
        UpdateLegendLocalization();

        // Status Bar
        UpdateStatusBarLocalization();

        // Map Area
        UpdateMapAreaLocalization();

        // Zoom Controls
        if (BtnZoomIn != null)
            BtnZoomIn.ToolTip = _loc.ZoomInTooltip;
        if (BtnZoomOut != null)
            BtnZoomOut.ToolTip = _loc.ZoomOutTooltip;
        if (BtnResetView != null)
            BtnResetView.ToolTip = _loc.ResetViewTooltip;

        // Refresh quest drawer to update any dynamic text
        if (_isDrawerOpen)
        {
            RefreshQuestDrawer();
        }
    }

    private void UpdateDrawerLocalization()
    {
        // Drawer title and header
        if (DrawerTitleText != null)
            DrawerTitleText.Text = _loc.Quest;
        if (HeaderQuestLabel != null)
            HeaderQuestLabel.Text = _loc.Quest;

        // Update drawer help button
        if (BtnDrawerHelp != null)
            BtnDrawerHelp.ToolTip = _loc.ShortcutHelp;

        // Search placeholder
        if (SearchPlaceholderText != null)
            SearchPlaceholderText.Text = _loc.SearchPlaceholder;

        // Filter checkboxes
        if (FilterIncompleteText != null)
            FilterIncompleteText.Text = _loc.Incomplete;
        if (FilterCurrentMapText != null)
            FilterCurrentMapText.Text = _loc.CurrentMap;

        // Empty state text
        if (EmptyStateTitle != null)
            EmptyStateTitle.Text = _loc.NoQuestsToDisplay;
        if (EmptyStateSubtitle != null)
            EmptyStateSubtitle.Text = _loc.TryAdjustingFilters;

        // Help hints
        if (HelpOpenCloseText != null)
            HelpOpenCloseText.Text = $" {_loc.OpenClose}";
        if (HelpMoveText != null)
            HelpMoveText.Text = $" {_loc.Move}";
        if (HelpSelectText != null)
            HelpSelectText.Text = $" {_loc.Select}";
        if (HelpClickKey != null)
            HelpClickKey.Text = _loc.Click;
        if (HelpClickText != null)
            HelpClickText.Text = $" {_loc.GoToMap}";
        if (HelpRightClickKey != null)
            HelpRightClickKey.Text = _loc.RightClick;
        if (HelpRightClickText != null)
            HelpRightClickText.Text = $" {_loc.ToggleComplete}";
    }

    private void UpdateSettingsLocalization()
    {
        // Settings Header
        if (SettingsHeaderText != null)
            SettingsHeaderText.Text = _loc.SettingsTitle;

        // Settings Tab Headers
        if (TabDisplay != null)
            TabDisplay.Content = _loc.TabDisplay;
        if (TabMarker != null)
            TabMarker.Content = _loc.TabMarker;
        if (TabTracker != null)
            TabTracker.Content = _loc.TabTracker;
        if (TabShortcuts != null)
            TabShortcuts.Content = _loc.TabShortcuts;

        // Display Tab - Layers
        if (SettingsLayersLabel != null)
            SettingsLayersLabel.Text = _loc.Layers;
        if (SettingsExtractLabel != null)
            SettingsExtractLabel.Text = _loc.Extract;
        if (SettingsTransitLabel != null)
            SettingsTransitLabel.Text = _loc.TransitPoint;
        if (SettingsQuestObjLabel != null)
            SettingsQuestObjLabel.Text = _loc.QuestObjective;
        if (SettingsTrailLabel != null)
            SettingsTrailLabel.Text = _loc.Trail;

        // Display Tab - Minimap
        if (SettingsMinimapLabel != null)
            SettingsMinimapLabel.Text = _loc.Minimap;
        if (SettingsShowMinimapLabel != null)
            SettingsShowMinimapLabel.Text = _loc.ShowMinimap;
        if (SettingsMinimapSizeLabel != null)
            SettingsMinimapSizeLabel.Text = _loc.MinimapSize;

        // Display Tab - Quick Actions
        if (SettingsQuestFilterLabel != null)
            SettingsQuestFilterLabel.Text = _loc.QuestFilter;
        if (SettingsLegendLabel != null)
            SettingsLegendLabel.Text = _loc.Legend;

        // Marker Tab
        if (SettingsMarkerSizeLabel != null)
            SettingsMarkerSizeLabel.Text = _loc.MarkerSize;
        if (SettingsMarkerOpacityLabel != null)
            SettingsMarkerOpacityLabel.Text = _loc.MarkerOpacity;
        if (SettingsQuestDisplayLabel != null)
            SettingsQuestDisplayLabel.Text = _loc.QuestDisplay;
        if (SettingsAutoHideCompletedLabel != null)
            SettingsAutoHideCompletedLabel.Text = _loc.AutoHideCompleted;
        if (SettingsFadeCompletedLabel != null)
            SettingsFadeCompletedLabel.Text = _loc.FadeCompleted;
        if (SettingsShowLabelsLabel != null)
            SettingsShowLabelsLabel.Text = _loc.ShowMarkerLabels;

        // Tracker Tab
        if (SettingsTrackerStatusLabel != null)
            SettingsTrackerStatusLabel.Text = _loc.TrackerStatus;
        if (TrackingStatusText != null && !_trackerService.IsWatching)
            TrackingStatusText.Text = _loc.Waiting;
        if (TrackingFolderText != null && string.IsNullOrEmpty(_trackerService.Settings.ScreenshotFolderPath))
            TrackingFolderText.Text = _loc.NoFolderSelected;
        if (BtnSelectFolderText != null)
            BtnSelectFolderText.Text = _loc.Folder;
        if (BtnOpenFolderText != null)
            BtnOpenFolderText.Text = _loc.Open;
        if (TrackingButtonText != null)
            TrackingButtonText.Text = _trackerService.IsWatching ? _loc.Stop : _loc.Start;

        // Tracker Tab - Trail Settings
        if (SettingsTrailConfigLabel != null)
            SettingsTrailConfigLabel.Text = _loc.PathSettings;
        if (SettingsTrailColorLabel != null)
            SettingsTrailColorLabel.Text = _loc.PathColor;
        if (SettingsTrailThicknessLabel != null)
            SettingsTrailThicknessLabel.Text = _loc.PathThickness;

        // Tracker Tab - Automation
        if (SettingsAutomationLabel != null)
            SettingsAutomationLabel.Text = _loc.Automation;
        if (SettingsAutoTrackLabel != null)
            SettingsAutoTrackLabel.Text = _loc.AutoTrackOnMapLoad;

        // Shortcuts Tab - Map Controls
        if (ShortcutsMapControlLabel != null)
            ShortcutsMapControlLabel.Text = _loc.MapControls;
        if (ShortcutsZoomLabel != null)
            ShortcutsZoomLabel.Text = _loc.ZoomInOut;
        if (ShortcutsZoomKey != null)
            ShortcutsZoomKey.Text = _loc.Scroll;
        if (ShortcutsPanLabel != null)
            ShortcutsPanLabel.Text = _loc.PanMap;
        if (ShortcutsPanKey != null)
            ShortcutsPanKey.Text = _loc.Drag;
        if (ShortcutsResetLabel != null)
            ShortcutsResetLabel.Text = _loc.ResetView;

        // Shortcuts Tab - Layer Toggle
        if (ShortcutsLayerToggleLabel != null)
            ShortcutsLayerToggleLabel.Text = _loc.LayerToggle;
        if (ShortcutsExtractLabel != null)
            ShortcutsExtractLabel.Text = _loc.ShowHideExtracts;
        if (ShortcutsTransitLabel != null)
            ShortcutsTransitLabel.Text = _loc.ShowHideTransit;
        if (ShortcutsQuestLabel != null)
            ShortcutsQuestLabel.Text = _loc.ShowHideQuests;

        // Shortcuts Tab - Panel
        if (ShortcutsPanelLabel != null)
            ShortcutsPanelLabel.Text = _loc.Panel;
        if (ShortcutsQuestPanelLabel != null)
            ShortcutsQuestPanelLabel.Text = _loc.QuestPanel;
        if (ShortcutsFloorChangeLabel != null)
            ShortcutsFloorChangeLabel.Text = _loc.FloorChange;

        // Settings Footer
        if (BtnResetSettingsFooter != null)
        {
            BtnResetSettingsFooter.Content = _loc.ResetAll;
            BtnResetSettingsFooter.ToolTip = _loc.ResetAllSettings;
        }
        if (BtnCloseSettingsFooter != null)
            BtnCloseSettingsFooter.Content = _loc.Close;
    }

    private void UpdateQuestFilterLocalization()
    {
        // Filter Popup Title
        if (FilterPopupTitle != null)
            FilterPopupTitle.Text = _loc.QuestTypeFilter;

        // Filter Types
        if (FilterVisitText != null)
            FilterVisitText.Text = _loc.VisitType;
        if (FilterMarkText != null)
            FilterMarkText.Text = _loc.MarkType;
        if (FilterPlantText != null)
            FilterPlantText.Text = _loc.PlantType;
        if (FilterExtractText != null)
            FilterExtractText.Text = _loc.ExtractType;
        if (FilterFindText != null)
            FilterFindText.Text = _loc.FindType;
        if (FilterKillText != null)
            FilterKillText.Text = _loc.KillType;
        if (FilterOtherText != null)
            FilterOtherText.Text = _loc.OtherType;
    }

    private void UpdateLegendLocalization()
    {
        // Legend Title
        if (LegendTitle != null)
            LegendTitle.Text = _loc.MapLegend;

        // Legend Items
        if (LegendExtract != null)
            LegendExtract.Text = _loc.Extract;
        if (LegendTransit != null)
            LegendTransit.Text = _loc.TransitPoint;
        if (LegendQuestObjective != null)
            LegendQuestObjective.Text = _loc.QuestObjective;

        // Quest Types
        if (LegendQuestType != null)
            LegendQuestType.Text = _loc.QuestType;
        if (LegendVisit != null)
            LegendVisit.Text = _loc.Visit;
        if (LegendMark != null)
            LegendMark.Text = _loc.Mark;
        if (LegendPlantItem != null)
            LegendPlantItem.Text = _loc.PlantItem;
        if (LegendExtractType != null)
            LegendExtractType.Text = _loc.Extract;
        if (LegendKill != null)
            LegendKill.Text = _loc.Kill;
    }

    private void UpdateStatusBarLocalization()
    {
        // Status Bar - Map Name (when no map selected)
        if (CurrentMapName != null && _currentMapConfig == null)
            CurrentMapName.Text = _loc.SelectMap;

        // Status Bar Tooltips
        if (ExtractCountPanel != null)
            ExtractCountPanel.ToolTip = _loc.Extract;
        if (TransitCountPanel != null)
            TransitCountPanel.ToolTip = _loc.TransitPoint;
        if (QuestCountPanel != null)
            QuestCountPanel.ToolTip = _loc.QuestObjective;
        if (BtnCopyCoords != null)
            BtnCopyCoords.ToolTip = _loc.CopyCoordinates;
        if (TrackingStatusPanel != null)
            TrackingStatusPanel.ToolTip = _loc.TrackerStatus;
    }

    private void UpdateMapAreaLocalization()
    {
        // Loading text
        if (LoadingText != null)
            LoadingText.Text = _loc.LoadingMap;

        // Minimap labels
        if (MinimapLabel != null)
            MinimapLabel.Text = _loc.Minimap;

        // Map hint texts
        if (MapHintScrollKey != null)
            MapHintScrollKey.Text = _loc.Scroll;
        if (MapHintZoomText != null)
            MapHintZoomText.Text = $" {_loc.Zoom}";
        if (MapHintDragKey != null)
            MapHintDragKey.Text = _loc.Drag;
        if (MapHintMoveText != null)
            MapHintMoveText.Text = $" {_loc.Move}";
        if (MapHintResetText != null)
            MapHintResetText.Text = $" {_loc.Reset}";
    }

    #endregion
}
