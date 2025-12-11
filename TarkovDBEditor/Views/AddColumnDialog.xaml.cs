using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Views;

public partial class AddColumnDialog : Window
{
    public ColumnSchema ColumnSchema { get; private set; }
    private readonly List<TableSchema> _tables;

    public AddColumnDialog(ColumnSchema column, List<TableSchema> tables)
    {
        InitializeComponent();

        ColumnSchema = column;
        _tables = tables;

        ColumnNameBox.Text = column.Name;
        DisplayNameBox.Text = column.DisplayName;
        TypeComboBox.SelectedItem = column.Type;
        RequiredCheck.IsChecked = column.IsRequired;
        DefaultValueBox.Text = column.DefaultValue;

        // Populate FK table dropdown
        FKTableComboBox.ItemsSource = _tables.Select(t => t.Name).ToList();

        if (column.IsForeignKey)
        {
            ForeignKeyCheck.IsChecked = true;
            FKTableComboBox.SelectedItem = column.ForeignKeyTable;
            FKColumnComboBox.SelectedItem = column.ForeignKeyColumn;
        }
    }

    private void ForeignKeyCheck_Changed(object sender, RoutedEventArgs e)
    {
        ForeignKeyPanel.Visibility = ForeignKeyCheck.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void FKTableComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FKTableComboBox.SelectedItem is string tableName)
        {
            var table = _tables.FirstOrDefault(t => t.Name == tableName);
            if (table != null)
            {
                FKColumnComboBox.ItemsSource = table.Columns.Select(c => c.Name).ToList();
                // Default to primary key column
                var pkCol = table.Columns.FirstOrDefault(c => c.IsPrimaryKey);
                if (pkCol != null)
                {
                    FKColumnComboBox.SelectedItem = pkCol.Name;
                }
            }
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ColumnNameBox.Text))
        {
            MessageBox.Show("Column name is required.", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ColumnSchema.Name = ColumnNameBox.Text.Trim().Replace(" ", "_");
        ColumnSchema.DisplayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text)
            ? ColumnNameBox.Text
            : DisplayNameBox.Text.Trim();
        ColumnSchema.Type = (ColumnType)TypeComboBox.SelectedItem;
        ColumnSchema.IsRequired = RequiredCheck.IsChecked == true;
        ColumnSchema.DefaultValue = DefaultValueBox.Text;

        if (ForeignKeyCheck.IsChecked == true)
        {
            if (FKTableComboBox.SelectedItem == null || FKColumnComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select foreign key table and column.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ColumnSchema.ForeignKeyTable = FKTableComboBox.SelectedItem.ToString();
            ColumnSchema.ForeignKeyColumn = FKColumnComboBox.SelectedItem.ToString();
        }
        else
        {
            ColumnSchema.ForeignKeyTable = null;
            ColumnSchema.ForeignKeyColumn = null;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
