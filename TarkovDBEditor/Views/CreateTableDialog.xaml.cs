using System.Collections.ObjectModel;
using System.Windows;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Views;

public partial class CreateTableDialog : Window
{
    public TableSchema TableSchema { get; private set; }
    private ObservableCollection<ColumnSchema> _columns;

    public CreateTableDialog(TableSchema schema)
    {
        InitializeComponent();

        TableSchema = schema;
        _columns = schema.Columns;

        TableNameBox.Text = schema.Name;
        DisplayNameBox.Text = schema.DisplayName;
        ColumnsListBox.ItemsSource = _columns;
    }

    private void AddColumn_Click(object sender, RoutedEventArgs e)
    {
        _columns.Add(new ColumnSchema
        {
            Name = $"Column{_columns.Count + 1}",
            DisplayName = $"Column {_columns.Count + 1}",
            Type = ColumnType.Text,
            SortOrder = _columns.Count
        });
    }

    private void RemoveColumn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is ColumnSchema col)
        {
            // Don't allow removing the primary key column
            if (col.IsPrimaryKey && col.IsAutoIncrement)
            {
                MessageBox.Show("Cannot remove the primary key column.", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _columns.Remove(col);
        }
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TableNameBox.Text))
        {
            MessageBox.Show("Table name is required.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Validate column names
        foreach (var col in _columns)
        {
            if (string.IsNullOrWhiteSpace(col.Name))
            {
                MessageBox.Show("All columns must have a name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Set display name if empty
            if (string.IsNullOrWhiteSpace(col.DisplayName))
            {
                col.DisplayName = col.Name;
            }
        }

        TableSchema.Name = TableNameBox.Text.Trim().Replace(" ", "_");
        TableSchema.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text)
            ? TableNameBox.Text
            : DisplayNameBox.Text.Trim();
        TableSchema.Columns = _columns;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
