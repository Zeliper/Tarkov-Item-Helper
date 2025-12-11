using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TarkovDBEditor.Models;

/// <summary>
/// Represents a database table schema that can be dynamically modified
/// </summary>
public class TableSchema : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _displayName = string.Empty;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    public ObservableCollection<ColumnSchema> Columns { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Represents a column in a table schema
/// </summary>
public class ColumnSchema : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _displayName = string.Empty;
    private ColumnType _type = ColumnType.Text;
    private bool _isRequired;
    private bool _isPrimaryKey;
    private bool _isAutoIncrement;
    private string? _defaultValue;
    private string? _foreignKeyTable;
    private string? _foreignKeyColumn;
    private int _sortOrder;

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; OnPropertyChanged(); }
    }

    public ColumnType Type
    {
        get => _type;
        set { _type = value; OnPropertyChanged(); }
    }

    public bool IsRequired
    {
        get => _isRequired;
        set { _isRequired = value; OnPropertyChanged(); }
    }

    public bool IsPrimaryKey
    {
        get => _isPrimaryKey;
        set { _isPrimaryKey = value; OnPropertyChanged(); }
    }

    public bool IsAutoIncrement
    {
        get => _isAutoIncrement;
        set { _isAutoIncrement = value; OnPropertyChanged(); }
    }

    public string? DefaultValue
    {
        get => _defaultValue;
        set { _defaultValue = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Foreign key reference table name (null if not a foreign key)
    /// </summary>
    public string? ForeignKeyTable
    {
        get => _foreignKeyTable;
        set { _foreignKeyTable = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Foreign key reference column name (null if not a foreign key)
    /// </summary>
    public string? ForeignKeyColumn
    {
        get => _foreignKeyColumn;
        set { _foreignKeyColumn = value; OnPropertyChanged(); }
    }

    public int SortOrder
    {
        get => _sortOrder;
        set { _sortOrder = value; OnPropertyChanged(); }
    }

    public bool IsForeignKey => !string.IsNullOrEmpty(ForeignKeyTable);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Supported column data types
/// </summary>
public enum ColumnType
{
    Text,
    Integer,
    Real,
    Boolean,
    DateTime,
    Json  // For storing arrays or complex objects as JSON string
}
