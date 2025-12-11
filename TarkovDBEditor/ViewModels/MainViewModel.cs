using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;

namespace TarkovDBEditor.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _db = DatabaseService.Instance;

    private TableSchema? _selectedTable;
    private ObservableCollection<DataRow> _tableData = new();
    private DataRow? _selectedRow;
    private string _statusMessage = "Ready";

    public ObservableCollection<TableSchema> Tables { get; } = new();

    public TableSchema? SelectedTable
    {
        get => _selectedTable;
        set
        {
            _selectedTable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedTable));
            LoadTableData();
        }
    }

    public ObservableCollection<DataRow> TableData
    {
        get => _tableData;
        set { _tableData = value; OnPropertyChanged(); }
    }

    public DataRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            _selectedRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedRow));
        }
    }

    public bool HasSelectedTable => _selectedTable != null;
    public bool HasSelectedRow => _selectedRow != null;

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    // Commands
    public ICommand CreateTableCommand { get; }
    public ICommand DeleteTableCommand { get; }
    public ICommand AddColumnCommand { get; }
    public ICommand AddRowCommand { get; }
    public ICommand DeleteRowCommand { get; }
    public ICommand SaveRowCommand { get; }
    public ICommand RefreshCommand { get; }

    public MainViewModel()
    {
        CreateTableCommand = new RelayCommand(ExecuteCreateTable);
        DeleteTableCommand = new RelayCommand(ExecuteDeleteTable, () => HasSelectedTable);
        AddColumnCommand = new RelayCommand(ExecuteAddColumn, () => HasSelectedTable);
        AddRowCommand = new RelayCommand(ExecuteAddRow, () => HasSelectedTable);
        DeleteRowCommand = new RelayCommand(ExecuteDeleteRow, () => HasSelectedRow);
        SaveRowCommand = new RelayCommand(ExecuteSaveRow, () => HasSelectedRow);
        RefreshCommand = new RelayCommand(LoadTables);

        _db.SchemaChanged += (_, _) => LoadTables();
        _db.ErrorOccurred += (_, msg) => StatusMessage = $"Error: {msg}";

        LoadTables();
    }

    public void LoadTables()
    {
        Tables.Clear();
        foreach (var schema in _db.GetAllTableSchemas())
        {
            Tables.Add(schema);
        }

        if (SelectedTable != null)
        {
            SelectedTable = Tables.FirstOrDefault(t => t.Name == SelectedTable.Name);
        }

        StatusMessage = $"Loaded {Tables.Count} tables";
    }

    private void LoadTableData()
    {
        TableData.Clear();
        SelectedRow = null;

        if (SelectedTable == null) return;

        var data = _db.GetTableData(SelectedTable.Name);
        foreach (var row in data)
        {
            TableData.Add(row);
        }

        StatusMessage = $"Loaded {TableData.Count} rows from {SelectedTable.DisplayName}";
    }

    // These methods will be called from the View with dialogs
    public event EventHandler<TableSchema>? RequestCreateTableDialog;
    public event EventHandler<ColumnSchema>? RequestAddColumnDialog;
    public event EventHandler<DataRow>? RequestEditRowDialog;

    private void ExecuteCreateTable()
    {
        var newSchema = new TableSchema
        {
            Name = "",
            DisplayName = "",
            Columns = new ObservableCollection<ColumnSchema>
            {
                new ColumnSchema
                {
                    Name = "Id",
                    DisplayName = "ID",
                    Type = ColumnType.Integer,
                    IsPrimaryKey = true,
                    IsAutoIncrement = true,
                    IsRequired = true,
                    SortOrder = 0
                }
            }
        };
        RequestCreateTableDialog?.Invoke(this, newSchema);
    }

    public bool CreateTable(TableSchema schema)
    {
        if (string.IsNullOrWhiteSpace(schema.Name))
        {
            StatusMessage = "Table name is required";
            return false;
        }

        if (_db.CreateTable(schema))
        {
            StatusMessage = $"Created table: {schema.DisplayName}";
            return true;
        }
        return false;
    }

    private void ExecuteDeleteTable()
    {
        if (SelectedTable == null) return;

        if (_db.DeleteTable(SelectedTable.Name))
        {
            StatusMessage = $"Deleted table: {SelectedTable.DisplayName}";
            SelectedTable = null;
        }
    }

    private void ExecuteAddColumn()
    {
        if (SelectedTable == null) return;

        var newColumn = new ColumnSchema
        {
            Name = "",
            DisplayName = "",
            Type = ColumnType.Text,
            SortOrder = SelectedTable.Columns.Count
        };
        RequestAddColumnDialog?.Invoke(this, newColumn);
    }

    public bool AddColumn(ColumnSchema column)
    {
        if (SelectedTable == null || string.IsNullOrWhiteSpace(column.Name))
        {
            StatusMessage = "Column name is required";
            return false;
        }

        if (_db.AddColumn(SelectedTable.Name, column))
        {
            StatusMessage = $"Added column: {column.DisplayName}";
            LoadTableData();
            return true;
        }
        return false;
    }

    private void ExecuteAddRow()
    {
        if (SelectedTable == null) return;

        var newRow = new DataRow();
        foreach (var col in SelectedTable.Columns)
        {
            if (!col.IsAutoIncrement)
            {
                newRow[col.Name] = col.Type switch
                {
                    ColumnType.Integer => 0L,
                    ColumnType.Real => 0.0,
                    ColumnType.Boolean => false,
                    _ => ""
                };
            }
        }

        RequestEditRowDialog?.Invoke(this, newRow);
    }

    public bool InsertRow(DataRow row)
    {
        if (SelectedTable == null) return false;

        if (_db.InsertRow(SelectedTable.Name, row))
        {
            LoadTableData();
            StatusMessage = "Row inserted";
            return true;
        }
        return false;
    }

    private void ExecuteDeleteRow()
    {
        if (SelectedTable == null || SelectedRow == null) return;

        var pkColumn = SelectedTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (pkColumn == null)
        {
            StatusMessage = "No primary key defined";
            return;
        }

        if (SelectedRow.TryGetValue(pkColumn.Name, out var pkValue) && pkValue != null)
        {
            if (_db.DeleteRow(SelectedTable.Name, pkColumn.Name, pkValue))
            {
                LoadTableData();
                StatusMessage = "Row deleted";
            }
        }
    }

    private void ExecuteSaveRow()
    {
        if (SelectedTable == null || SelectedRow == null) return;

        var pkColumn = SelectedTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (pkColumn == null)
        {
            StatusMessage = "No primary key defined";
            return;
        }

        if (SelectedRow.TryGetValue(pkColumn.Name, out var pkValue) && pkValue != null)
        {
            if (_db.UpdateRow(SelectedTable.Name, SelectedRow, pkColumn.Name, pkValue))
            {
                StatusMessage = "Row saved";
            }
        }
    }

    public bool UpdateRow(DataRow row)
    {
        if (SelectedTable == null) return false;

        var pkColumn = SelectedTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
        if (pkColumn == null) return false;

        if (row.TryGetValue(pkColumn.Name, out var pkValue) && pkValue != null)
        {
            if (_db.UpdateRow(SelectedTable.Name, row, pkColumn.Name, pkValue))
            {
                LoadTableData();
                StatusMessage = "Row updated";
                return true;
            }
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
