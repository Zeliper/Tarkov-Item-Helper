using System.Collections.Generic;
using System.Windows;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Views;

public partial class SelectObjectiveDialog : Window
{
    public QuestObjectiveItem? SelectedObjective { get; private set; }

    public SelectObjectiveDialog(IEnumerable<QuestObjectiveItem> objectives, string markerName)
    {
        InitializeComponent();

        ObjectivesList.ItemsSource = objectives;
        MarkerNameText.Text = markerName;

        // Select first item by default
        if (ObjectivesList.Items.Count > 0)
            ObjectivesList.SelectedIndex = 0;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (ObjectivesList.SelectedItem is QuestObjectiveItem selected)
        {
            SelectedObjective = selected;
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Please select an objective.", "Selection Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
