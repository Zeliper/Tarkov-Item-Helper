using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TarkovDBEditor.Models;
using TarkovDBEditor.ViewModels;

namespace TarkovDBEditor.Views;

public partial class AddApiPointsDialog : Window
{
    public List<ApiReferenceMarkerItem> SelectedMarkers { get; private set; } = new();
    public bool IsOrPoint { get; private set; }

    public AddApiPointsDialog(
        QuestObjectiveItem objective,
        IEnumerable<ApiReferenceMarkerItem> availableMarkers)
    {
        InitializeComponent();

        // Set objective info
        var desc = objective.Description.Length > 100
            ? objective.Description.Substring(0, 100) + "..."
            : objective.Description;
        ObjectiveDescText.Text = $"[{objective.ObjectiveType}] {desc}";

        // Set markers list
        var markersList = availableMarkers.ToList();
        MarkersList.ItemsSource = markersList;

        if (markersList.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
        }
    }

    private void MarkersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        var count = MarkersList.SelectedItems.Count;
        SelectionCountText.Text = $" ({count} selected)";
        AddButton.IsEnabled = count > 0;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        MarkersList.SelectAll();
        UpdateSelectionCount();
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        MarkersList.UnselectAll();
        UpdateSelectionCount();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SelectedMarkers = MarkersList.SelectedItems.Cast<ApiReferenceMarkerItem>().ToList();
        IsOrPoint = OrPointRadio.IsChecked == true;
        DialogResult = true;
        Close();
    }
}
