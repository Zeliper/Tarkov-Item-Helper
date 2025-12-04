using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace TarkovHelper.Models;

/// <summary>
/// View model for quest selection in the In-Progress Quest Input overlay
/// </summary>
public class QuestSelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public TarkovTask Quest { get; set; } = null!;
    public string DisplayName { get; set; } = string.Empty;
    public string SubtitleName { get; set; } = string.Empty;
    public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
    public string TraderName { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsEnabled => !IsCompleted;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }
    }

    public Brush TextColor => IsCompleted
        ? new SolidColorBrush(Color.FromRgb(128, 128, 128))
        : new SolidColorBrush(Color.FromRgb(224, 224, 224));

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// View model for prerequisite quest display
/// </summary>
public class PrerequisitePreviewItem
{
    public TarkovTask Quest { get; set; } = null!;
    public string DisplayName { get; set; } = string.Empty;
    public string SubtitleName { get; set; } = string.Empty;
    public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
    public string TraderName { get; set; } = string.Empty;
}
