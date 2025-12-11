using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TarkovDBEditor.Models;

/// <summary>
/// Represents a row of data with dynamic columns
/// </summary>
public class DataRow : INotifyPropertyChanged
{
    private readonly Dictionary<string, object?> _values = new();

    public object? this[string columnName]
    {
        get => _values.TryGetValue(columnName, out var value) ? value : null;
        set
        {
            _values[columnName] = value;
            OnPropertyChanged(columnName);
        }
    }

    public Dictionary<string, object?> Values => _values;

    public bool TryGetValue(string columnName, out object? value)
        => _values.TryGetValue(columnName, out value);

    public void SetValue(string columnName, object? value)
    {
        _values[columnName] = value;
        OnPropertyChanged(columnName);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Lookup item for foreign key dropdowns (WPF bindable)
/// </summary>
public class LookupItem
{
    public object Id { get; set; } = null!;
    public string Display { get; set; } = "";

    public override string ToString() => Display;
}
