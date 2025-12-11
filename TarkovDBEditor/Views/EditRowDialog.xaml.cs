using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace TarkovDBEditor.Views;

public partial class EditRowDialog : Window
{
    public DataRow DataRow { get; private set; }
    private readonly TableSchema _schema;
    private readonly List<TableSchema> _tables;
    private readonly Dictionary<string, FrameworkElement> _fieldControls = new();

    public EditRowDialog(DataRow row, TableSchema schema, List<TableSchema> tables)
    {
        InitializeComponent();

        DataRow = row;
        _schema = schema;
        _tables = tables;

        BuildFieldControls();
    }

    private void BuildFieldControls()
    {
        FieldsPanel.Children.Clear();
        _fieldControls.Clear();

        foreach (var col in _schema.Columns.OrderBy(c => c.SortOrder))
        {
            var fieldPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            // Label
            var label = new TextBlock
            {
                Text = col.DisplayName + (col.IsRequired ? " *" : ""),
                Foreground = FindResource("TextBrush") as System.Windows.Media.Brush,
                Margin = new Thickness(0, 0, 0, 4),
                FontWeight = col.IsPrimaryKey ? FontWeights.Bold : FontWeights.Normal
            };

            if (col.IsPrimaryKey)
            {
                label.Text += " (PK)";
            }

            fieldPanel.Children.Add(label);

            // Control based on column type
            FrameworkElement control;

            if (col.IsAutoIncrement)
            {
                // Read-only for auto-increment
                var idValue = DataRow[col.Name]?.ToString() ?? "(Auto)";
                control = new WpfTextBox
                {
                    Text = idValue,
                    IsReadOnly = true,
                    Background = FindResource("PrimaryBrush") as System.Windows.Media.Brush
                };
            }
            else if (col.IsForeignKey)
            {
                // ComboBox for foreign key
                var combo = new WpfComboBox();
                var lookupData = DatabaseService.Instance.GetLookupData(
                    col.ForeignKeyTable!,
                    col.ForeignKeyColumn!);

                // Add empty option
                var items = new List<LookupItem> { new LookupItem { Id = null!, Display = "(None)" } };
                items.AddRange(lookupData);

                combo.ItemsSource = items;
                combo.DisplayMemberPath = "Display";
                combo.SelectedValuePath = "Id";

                var currentValue = DataRow[col.Name];
                if (currentValue != null)
                {
                    combo.SelectedValue = currentValue;
                }
                else
                {
                    combo.SelectedIndex = 0;
                }

                control = combo;
            }
            else
            {
                control = col.Type switch
                {
                    ColumnType.Boolean => CreateBooleanControl(col),
                    ColumnType.Integer => CreateIntegerControl(col),
                    ColumnType.Real => CreateRealControl(col),
                    ColumnType.DateTime => CreateDateTimeControl(col),
                    ColumnType.Json => CreateJsonControl(col),
                    _ => CreateTextControl(col)
                };
            }

            _fieldControls[col.Name] = control;
            fieldPanel.Children.Add(control);
            FieldsPanel.Children.Add(fieldPanel);
        }
    }

    private WpfCheckBox CreateBooleanControl(ColumnSchema col)
    {
        var checkBox = new WpfCheckBox
        {
            Content = col.DisplayName,
            IsChecked = DataRow[col.Name] is bool b && b
        };
        return checkBox;
    }

    private WpfTextBox CreateIntegerControl(ColumnSchema col)
    {
        var textBox = new WpfTextBox
        {
            Text = DataRow[col.Name]?.ToString() ?? "0"
        };
        return textBox;
    }

    private WpfTextBox CreateRealControl(ColumnSchema col)
    {
        var textBox = new WpfTextBox
        {
            Text = DataRow[col.Name]?.ToString() ?? "0.0"
        };
        return textBox;
    }

    private WpfTextBox CreateDateTimeControl(ColumnSchema col)
    {
        var textBox = new WpfTextBox
        {
            Text = DataRow[col.Name]?.ToString() ?? ""
        };
        return textBox;
    }

    private WpfTextBox CreateJsonControl(ColumnSchema col)
    {
        var textBox = new WpfTextBox
        {
            Text = DataRow[col.Name]?.ToString() ?? "[]",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 80,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        return textBox;
    }

    private WpfTextBox CreateTextControl(ColumnSchema col)
    {
        var isLongText = col.Name.Contains("Text", System.StringComparison.OrdinalIgnoreCase) ||
                         col.Name.Contains("Description", System.StringComparison.OrdinalIgnoreCase) ||
                         col.Name.Contains("Guide", System.StringComparison.OrdinalIgnoreCase);

        var textBox = new WpfTextBox
        {
            Text = DataRow[col.Name]?.ToString() ?? ""
        };

        if (isLongText)
        {
            textBox.AcceptsReturn = true;
            textBox.TextWrapping = TextWrapping.Wrap;
            textBox.Height = 80;
            textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }

        return textBox;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validate required fields
        foreach (var col in _schema.Columns.Where(c => c.IsRequired && !c.IsAutoIncrement))
        {
            if (_fieldControls.TryGetValue(col.Name, out var control))
            {
                var value = GetControlValue(control, col);
                if (value == null || (value is string s && string.IsNullOrWhiteSpace(s)))
                {
                    MessageBox.Show($"{col.DisplayName} is required.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
        }

        // Collect values
        foreach (var col in _schema.Columns)
        {
            if (col.IsAutoIncrement) continue;

            if (_fieldControls.TryGetValue(col.Name, out var control))
            {
                DataRow[col.Name] = GetControlValue(control, col);
            }
        }

        DialogResult = true;
        Close();
    }

    private object? GetControlValue(FrameworkElement control, ColumnSchema col)
    {
        return control switch
        {
            WpfCheckBox cb => cb.IsChecked == true,
            WpfComboBox combo => combo.SelectedValue,
            WpfTextBox tb => col.Type switch
            {
                ColumnType.Integer => long.TryParse(tb.Text, out var i) ? i : 0L,
                ColumnType.Real => double.TryParse(tb.Text, out var d) ? d : 0.0,
                _ => tb.Text
            },
            _ => null
        };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
