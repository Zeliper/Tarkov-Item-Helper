using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TarkovDBEditor.Models;
using TarkovDBEditor.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace TarkovDBEditor.Views;

public partial class QuestRequirementsView : Window
{
    private readonly QuestRequirementsViewModel _viewModel;

    public QuestRequirementsView()
    {
        InitializeComponent();
        _viewModel = new QuestRequirementsViewModel();
        DataContext = _viewModel;

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadDataAsync();
            UpdateProgressText();
        };

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.FilteredQuests) ||
                e.PropertyName == nameof(_viewModel.SelectedQuestRequirements))
            {
                UpdateProgressText();
            }
        };
    }

    private void UpdateProgressText()
    {
        var (approved, total) = _viewModel.GetApprovalProgress();
        var percent = total > 0 ? (approved * 100 / total) : 0;
        ProgressText.Text = $"{approved}/{total} ({percent}%)";
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        _viewModel.FilterMode = FilterCombo.SelectedIndex switch
        {
            0 => QuestFilterMode.All,
            1 => QuestFilterMode.PendingApproval,
            2 => QuestFilterMode.Approved,
            3 => QuestFilterMode.HasRequirements,
            4 => QuestFilterMode.NoRequirements,
            _ => QuestFilterMode.All
        };
    }

    private void MapFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        _viewModel.SelectedMapFilter = MapFilterCombo.SelectedItem as string;
        UpdateProgressText();

        if (_viewModel.SelectedMapFilter != null)
        {
            StatusText.Text = $"Filtering by map: {_viewModel.SelectedMapFilter}";
        }
    }

    private void ClearMapFilter_Click(object sender, RoutedEventArgs e)
    {
        MapFilterCombo.SelectedItem = null;
        _viewModel.SelectedMapFilter = null;
        StatusText.Text = "Map filter cleared";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Handled by binding
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Refreshing...";
        await _viewModel.LoadDataAsync();
        UpdateProgressText();
        StatusText.Text = "Data refreshed";
    }

    private void OpenWiki_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest?.WikiPageLink != null)
        {
            OpenUrl(_viewModel.SelectedQuest.WikiPageLink);
        }
    }

    private void OpenRequiredQuestWiki_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is QuestRequirementItem req)
        {
            if (!string.IsNullOrEmpty(req.RequiredQuestWikiLink))
            {
                OpenUrl(req.RequiredQuestWikiLink);
            }
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is QuestRequirementItem req)
        {
            var isApproved = cb.IsChecked ?? false;
            req.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateApprovalAsync(req.Id, isApproved);
            UpdateProgressText();
        }
    }

    private async void MinLevelCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;
        if (sender is not CheckBox cb) return;

        var isApproved = cb.IsChecked ?? false;
        _viewModel.SelectedQuest.MinLevelApproved = isApproved; // Update model immediately
        await _viewModel.UpdateMinLevelApprovalAsync(_viewModel.SelectedQuest.Id, isApproved);
        UpdateProgressText();
        StatusText.Text = isApproved
            ? $"Approved MinLevel requirement for {_viewModel.SelectedQuest.Name}"
            : $"Unapproved MinLevel requirement for {_viewModel.SelectedQuest.Name}";
    }

    private async void MinScavKarmaCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;
        if (sender is not CheckBox cb) return;

        var isApproved = cb.IsChecked ?? false;
        _viewModel.SelectedQuest.MinScavKarmaApproved = isApproved; // Update model immediately
        await _viewModel.UpdateMinScavKarmaApprovalAsync(_viewModel.SelectedQuest.Id, isApproved);
        UpdateProgressText();
        StatusText.Text = isApproved
            ? $"Approved MinScavKarma requirement for {_viewModel.SelectedQuest.Name}"
            : $"Unapproved MinScavKarma requirement for {_viewModel.SelectedQuest.Name}";
    }

    private async void ApproveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;

        // Approve MinLevel if exists
        if (_viewModel.SelectedQuest.HasMinLevel && !_viewModel.SelectedQuest.MinLevelApproved)
        {
            _viewModel.SelectedQuest.MinLevelApproved = true;
            await _viewModel.UpdateMinLevelApprovalAsync(_viewModel.SelectedQuest.Id, true);
        }

        // Approve MinScavKarma if exists
        if (_viewModel.SelectedQuest.HasMinScavKarma && !_viewModel.SelectedQuest.MinScavKarmaApproved)
        {
            _viewModel.SelectedQuest.MinScavKarmaApproved = true;
            await _viewModel.UpdateMinScavKarmaApprovalAsync(_viewModel.SelectedQuest.Id, true);
        }

        // Approve quest requirements
        foreach (var req in _viewModel.SelectedQuestRequirements)
        {
            if (!req.IsApproved)
            {
                req.IsApproved = true;
                await _viewModel.UpdateApprovalAsync(req.Id, true);
            }
        }

        // Approve quest objectives
        foreach (var obj in _viewModel.SelectedQuestObjectives)
        {
            if (!obj.IsApproved)
            {
                obj.IsApproved = true;
                await _viewModel.UpdateObjectiveApprovalAsync(obj.Id, true);
            }
        }

        // Approve optional quests (other choices)
        foreach (var opt in _viewModel.SelectedOptionalQuests)
        {
            if (!opt.IsApproved)
            {
                opt.IsApproved = true;
                await _viewModel.UpdateOptionalQuestApprovalAsync(opt.Id, true);
            }
        }

        // Approve required items
        foreach (var item in _viewModel.SelectedRequiredItems)
        {
            if (!item.IsApproved)
            {
                item.IsApproved = true;
                await _viewModel.UpdateRequiredItemApprovalAsync(item.Id, true);
            }
        }

        UpdateProgressText();
        StatusText.Text = $"Approved all requirements for {_viewModel.SelectedQuest.Name}";
    }

    private async void UnapproveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;

        // Unapprove MinLevel if exists
        if (_viewModel.SelectedQuest.HasMinLevel && _viewModel.SelectedQuest.MinLevelApproved)
        {
            _viewModel.SelectedQuest.MinLevelApproved = false;
            await _viewModel.UpdateMinLevelApprovalAsync(_viewModel.SelectedQuest.Id, false);
        }

        // Unapprove MinScavKarma if exists
        if (_viewModel.SelectedQuest.HasMinScavKarma && _viewModel.SelectedQuest.MinScavKarmaApproved)
        {
            _viewModel.SelectedQuest.MinScavKarmaApproved = false;
            await _viewModel.UpdateMinScavKarmaApprovalAsync(_viewModel.SelectedQuest.Id, false);
        }

        // Unapprove quest requirements
        foreach (var req in _viewModel.SelectedQuestRequirements)
        {
            if (req.IsApproved)
            {
                req.IsApproved = false;
                await _viewModel.UpdateApprovalAsync(req.Id, false);
            }
        }

        // Unapprove quest objectives
        foreach (var obj in _viewModel.SelectedQuestObjectives)
        {
            if (obj.IsApproved)
            {
                obj.IsApproved = false;
                await _viewModel.UpdateObjectiveApprovalAsync(obj.Id, false);
            }
        }

        // Unapprove optional quests (other choices)
        foreach (var opt in _viewModel.SelectedOptionalQuests)
        {
            if (opt.IsApproved)
            {
                opt.IsApproved = false;
                await _viewModel.UpdateOptionalQuestApprovalAsync(opt.Id, false);
            }
        }

        // Unapprove required items
        foreach (var item in _viewModel.SelectedRequiredItems)
        {
            if (item.IsApproved)
            {
                item.IsApproved = false;
                await _viewModel.UpdateRequiredItemApprovalAsync(item.Id, false);
            }
        }

        UpdateProgressText();
        StatusText.Text = $"Unapproved all requirements for {_viewModel.SelectedQuest.Name}";
    }

    private async void ObjectiveApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is QuestObjectiveItem obj)
        {
            var isApproved = cb.IsChecked ?? false;
            obj.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateObjectiveApprovalAsync(obj.Id, isApproved);
            UpdateProgressText();
            StatusText.Text = isApproved
                ? $"Approved objective: {obj.Description.Substring(0, Math.Min(50, obj.Description.Length))}..."
                : $"Unapproved objective";
        }
    }

    private void ObjectivesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdatePointsPanel();
    }

    private async void ObjectivesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ObjectivesList.SelectedItem is not QuestObjectiveItem selectedObj)
            return;

        // Check if the objective has an effective map (MapName or QuestLocation)
        if (string.IsNullOrEmpty(selectedObj.EffectiveMapName))
        {
            MessageBox.Show("This objective does not have a map location specified.\nThe quest also doesn't have a location set.",
                "No Map", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Open the map editor window
        var mapEditor = new MapEditorWindow(selectedObj);
        var result = mapEditor.ShowDialog();

        if (result == true && mapEditor.WasSaved)
        {
            // Save to database
            var json = selectedObj.LocationPointsJson;
            await _viewModel.UpdateObjectiveLocationPointsAsync(selectedObj.Id, json);

            UpdatePointsPanel();

            var desc = selectedObj.Description.Length > 50
                ? selectedObj.Description.Substring(0, 50) + "..."
                : selectedObj.Description;
            StatusText.Text = $"Location points saved ({selectedObj.LocationPoints.Count} points) for: {desc}";
        }
    }

    private void UpdatePointsPanel()
    {
        if (ObjectivesList.SelectedItem is QuestObjectiveItem selectedObj)
        {
            PointsItemsControl.ItemsSource = selectedObj.LocationPoints;
            UpdatePolygonLabels(selectedObj.LocationPoints.Count);
        }
        else
        {
            PointsItemsControl.ItemsSource = null;
            UpdatePolygonLabels(0);
        }
    }

    private void UpdatePolygonLabels(int count)
    {
        var type = count switch
        {
            0 => "",
            1 => "(Point)",
            2 => "(Line)",
            3 => "(Triangle)",
            4 => "(Quad)",
            _ => $"(Polygon - {count} vertices)"
        };
        PolygonTypeLabel.Text = type;
        PointsCountLabel.Text = count > 0 ? $"{count} point(s)" : "No points";
    }

    private void AddPoint_Click(object sender, RoutedEventArgs e)
    {
        if (ObjectivesList.SelectedItem is not QuestObjectiveItem selectedObj)
        {
            MessageBox.Show("Please select an objective first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        selectedObj.LocationPoints.Add(new LocationPoint(0, 0, 0));
        UpdatePolygonLabels(selectedObj.LocationPoints.Count);
    }

    private void RemovePoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not LocationPoint point) return;
        if (ObjectivesList.SelectedItem is not QuestObjectiveItem selectedObj) return;

        selectedObj.LocationPoints.Remove(point);
        UpdatePolygonLabels(selectedObj.LocationPoints.Count);
    }

    private async void SaveLocationPoints_Click(object sender, RoutedEventArgs e)
    {
        if (ObjectivesList.SelectedItem is not QuestObjectiveItem selectedObj)
        {
            MessageBox.Show("Please select an objective first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var json = selectedObj.LocationPointsJson;
        await _viewModel.UpdateObjectiveLocationPointsAsync(selectedObj.Id, json);

        var desc = selectedObj.Description.Length > 50
            ? selectedObj.Description.Substring(0, 50) + "..."
            : selectedObj.Description;
        StatusText.Text = $"Location points saved ({selectedObj.LocationPoints.Count} points) for: {desc}";
    }

    private async void OptionalQuestApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is OptionalQuestItem opt)
        {
            var isApproved = cb.IsChecked ?? false;
            opt.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateOptionalQuestApprovalAsync(opt.Id, isApproved);
            UpdateProgressText();
            StatusText.Text = isApproved
                ? $"Approved alternative quest: {opt.AlternativeQuestName}"
                : $"Unapproved alternative quest: {opt.AlternativeQuestName}";
        }
    }

    private void OpenAlternativeQuestWiki_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OptionalQuestItem opt)
        {
            if (!string.IsNullOrEmpty(opt.AlternativeQuestWikiLink))
            {
                OpenUrl(opt.AlternativeQuestWikiLink);
            }
        }
    }

    private async void RequiredItemApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is QuestRequiredItemViewModel item)
        {
            var isApproved = cb.IsChecked ?? false;
            item.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateRequiredItemApprovalAsync(item.Id, isApproved);
            UpdateProgressText();
            StatusText.Text = isApproved
                ? $"Approved required item: {item.ItemName}"
                : $"Unapproved required item: {item.ItemName}";
        }
    }
}
