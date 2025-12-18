using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Pages.Components
{
    /// <summary>
    /// UserControl for displaying quest recommendations
    /// </summary>
    public partial class QuestRecommendationsPanel : UserControl
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;

        // Recommendation type brushes
        private static readonly Brush ReadyToCompleteBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
        private static readonly Brush ItemHandInBrush = new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue
        private static readonly Brush KappaPriorityBrush = new SolidColorBrush(Color.FromRgb(156, 39, 176)); // Purple
        private static readonly Brush UnlocksManyBrush = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
        private static readonly Brush EasyQuestBrush = new SolidColorBrush(Color.FromRgb(0, 188, 212)); // Cyan

        /// <summary>
        /// Event raised when a recommendation is clicked
        /// </summary>
        public event EventHandler<string>? RecommendationClicked;

        /// <summary>
        /// Function to get localized quest names (set by parent)
        /// </summary>
        public Func<TarkovTask, (string DisplayName, string Subtitle, bool ShowSubtitle)>? GetLocalizedNames { get; set; }

        public QuestRecommendationsPanel()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Refresh recommendations display
        /// </summary>
        public void UpdateRecommendations()
        {
            try
            {
                var recommendationService = QuestRecommendationService.Instance;
                var recommendations = recommendationService.GetRecommendations(5);

                if (recommendations.Count == 0)
                {
                    RecommendationsExpander.Visibility = Visibility.Collapsed;
                    return;
                }

                // Update header text with localization
                TxtRecommendationsHeader.Text = _loc.RecommendedQuests;
                TxtRecommendationCount.Text = recommendations.Count.ToString();
                TxtNoRecommendations.Text = _loc.NoRecommendations;

                // Create view models
                var recommendationVms = recommendations.Select(r => CreateRecommendationViewModel(r)).ToList();

                RecommendationsList.ItemsSource = recommendationVms;
                TxtNoRecommendations.Visibility = Visibility.Collapsed;
                RecommendationsExpander.Visibility = Visibility.Visible;
            }
            catch
            {
                // Hide recommendations section if service is not initialized
                RecommendationsExpander.Visibility = Visibility.Collapsed;
            }
        }

        private RecommendationViewModel CreateRecommendationViewModel(QuestRecommendation rec)
        {
            var displayName = rec.Quest.Name;

            // Use the localization function if provided
            if (GetLocalizedNames != null)
            {
                var (name, _, _) = GetLocalizedNames(rec.Quest);
                displayName = name;
            }

            return new RecommendationViewModel
            {
                Recommendation = rec,
                QuestName = displayName,
                Reason = rec.Reason,
                TypeText = GetRecommendationTypeText(rec.Type),
                TypeBackground = GetRecommendationTypeBrush(rec.Type),
                TraderInitial = GetTraderInitial(rec.Quest.Trader),
                IsKappaRequired = rec.Quest.ReqKappa
            };
        }

        private static string GetTraderInitial(string trader)
        {
            if (string.IsNullOrEmpty(trader)) return "?";
            return trader.Length >= 2 ? trader[..2].ToUpper() : trader.ToUpper();
        }

        private string GetRecommendationTypeText(RecommendationType type)
        {
            return type switch
            {
                RecommendationType.ReadyToComplete => _loc.ReadyToComplete,
                RecommendationType.ItemHandInOnly => _loc.ItemHandInOnly,
                RecommendationType.KappaPriority => _loc.KappaPriority,
                RecommendationType.UnlocksMany => _loc.UnlocksMany,
                RecommendationType.EasyQuest => _loc.EasyQuest,
                _ => "Unknown"
            };
        }

        private static Brush GetRecommendationTypeBrush(RecommendationType type)
        {
            return type switch
            {
                RecommendationType.ReadyToComplete => ReadyToCompleteBrush,
                RecommendationType.ItemHandInOnly => ItemHandInBrush,
                RecommendationType.KappaPriority => KappaPriorityBrush,
                RecommendationType.UnlocksMany => UnlocksManyBrush,
                RecommendationType.EasyQuest => EasyQuestBrush,
                _ => Brushes.Gray
            };
        }

        private void Recommendation_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is RecommendationViewModel vm)
            {
                var questNormalizedName = vm.Recommendation.Quest.NormalizedName;
                if (!string.IsNullOrEmpty(questNormalizedName))
                {
                    RecommendationClicked?.Invoke(this, questNormalizedName);
                }
            }
        }
    }
}
