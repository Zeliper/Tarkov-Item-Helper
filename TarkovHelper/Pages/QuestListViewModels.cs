using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Pages
{
    /// <summary>
    /// Quest list view model for display
    /// </summary>
    public class QuestViewModel
    {
        public TarkovTask Task { get; set; } = null!;
        public string DisplayName { get; set; } = string.Empty;
        public string SubtitleName { get; set; } = string.Empty;
        public Visibility SubtitleVisibility { get; set; } = Visibility.Collapsed;
        public string TraderInitial { get; set; } = string.Empty;
        public QuestStatus Status { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public Brush StatusBackground { get; set; } = Brushes.Gray;
        public Visibility CompleteButtonVisibility { get; set; } = Visibility.Visible;
        public bool IsKappaRequired { get; set; }
        public Visibility KappaBadgeVisibility => IsKappaRequired ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Required item view model
    /// </summary>
    public class RequiredItemViewModel
    {
        public string DisplayText { get; set; } = string.Empty;
        public bool FoundInRaid { get; set; }
        public Visibility FirVisibility => FoundInRaid ? Visibility.Visible : Visibility.Collapsed;
        public BitmapImage? IconSource { get; set; }
        public string RequirementType { get; set; } = string.Empty;
        public Visibility RequirementTypeVisibility =>
            string.IsNullOrEmpty(RequirementType) ? Visibility.Collapsed : Visibility.Visible;

        // Navigation identifier (use ItemId for cross-tab navigation)
        public string ItemId { get; set; } = string.Empty;

        // Fulfillment status
        public bool IsFulfilled { get; set; }
        public TextDecorationCollection? TextDecorations => IsFulfilled ? System.Windows.TextDecorations.Strikethrough : null;
        public double ItemOpacity => IsFulfilled ? 0.6 : 1.0;
        public Visibility FulfilledVisibility => IsFulfilled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Prerequisite group view model for displaying OR/AND grouped prerequisites
    /// </summary>
    public class PrerequisiteGroupViewModel
    {
        public int GroupId { get; set; }
        public bool IsOrGroup => GroupId > 0;
        public string GroupLabel => IsOrGroup ? "OR" : "";
        public Visibility OrLabelVisibility => IsOrGroup ? Visibility.Visible : Visibility.Collapsed;
        public Brush OrGroupBackground => IsOrGroup ? new SolidColorBrush(Color.FromArgb(30, 33, 150, 243)) : Brushes.Transparent;
        public List<PrerequisiteItemViewModel> Items { get; set; } = new();
    }

    /// <summary>
    /// Single prerequisite item view model
    /// </summary>
    public class PrerequisiteItemViewModel
    {
        public TarkovTask? Task { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public Brush StatusBackground { get; set; } = Brushes.Gray;
        public bool IsOrItem { get; set; }
        public string OrSeparator => IsOrItem ? " OR " : "";
        public Visibility OrSeparatorVisibility => IsOrItem ? Visibility.Visible : Visibility.Collapsed;
        public string BulletText => IsOrItem ? "" : "• ";
    }

    /// <summary>
    /// Recommendation view model for display
    /// </summary>
    public class RecommendationViewModel
    {
        public QuestRecommendation Recommendation { get; set; } = null!;
        public string QuestName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string TypeText { get; set; } = string.Empty;
        public Brush TypeBackground { get; set; } = Brushes.Gray;
        public string TraderInitial { get; set; } = string.Empty;
        public bool IsKappaRequired { get; set; }
        public Visibility KappaBadgeVisibility => IsKappaRequired ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Guide image view model with loading state
    /// </summary>
    public class GuideImageViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        private BitmapImage? _imageSource;
        private bool _isLoading = true;

        public string FileName { get; set; } = string.Empty;
        public string? Caption { get; set; }

        public BitmapImage? ImageSource
        {
            get => _imageSource;
            set
            {
                _imageSource = value;
                OnPropertyChanged(nameof(ImageSource));
                OnPropertyChanged(nameof(ImageVisibility));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(ImageVisibility));
            }
        }

        public Visibility CaptionVisibility =>
            string.IsNullOrEmpty(Caption) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility LoadingVisibility =>
            IsLoading ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ImageVisibility =>
            IsLoading ? Visibility.Collapsed : Visibility.Visible;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
}
