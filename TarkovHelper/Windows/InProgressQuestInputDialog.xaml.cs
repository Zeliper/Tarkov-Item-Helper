using System.Windows;
using System.Windows.Controls;
using TarkovHelper.Models;
using TarkovHelper.Services;

namespace TarkovHelper.Windows;

/// <summary>
/// Dialog for entering in-progress quests.
/// Allows user to select quests they are currently working on,
/// and auto-completes all prerequisites when applied.
/// </summary>
public partial class InProgressQuestInputDialog : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly QuestGraphService _graphService = QuestGraphService.Instance;
    private readonly QuestProgressService _progressService = QuestProgressService.Instance;

    private List<QuestSelectionItem>? _allQuestItems;
    private List<QuestSelectionItem>? _filteredQuestItems;
    private System.Windows.Threading.DispatcherTimer? _searchDebounceTimer;
    private List<TarkovTrader>? _cachedTraders;

    /// <summary>
    /// Result containing selected quests and prerequisites to complete.
    /// Null if cancelled.
    /// </summary>
    public InProgressQuestInputResult? Result { get; private set; }

    public InProgressQuestInputDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the dialog and return result.
    /// </summary>
    /// <param name="owner">Optional owner window for centering.</param>
    /// <returns>Result containing selected quests and prerequisites, or null if cancelled.</returns>
    public static InProgressQuestInputResult? ShowDialog(Window? owner)
    {
        var dialog = new InProgressQuestInputDialog();
        if (owner != null)
        {
            dialog.Owner = owner;
        }

        // Initialize data
        if (!dialog.InitializeData())
        {
            MessageBox.Show(
                dialog._loc.QuestDataNotLoaded,
                dialog._loc.CurrentLanguage switch { AppLanguage.KO => "Error", AppLanguage.JA => "Error", _ => "Error" },
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }

        dialog.ShowDialog();
        return dialog.Result;
    }

    private bool InitializeData()
    {
        // Check if quest data is loaded
        if (_graphService.GetAllTasks() == null || _graphService.GetAllTasks().Count == 0)
        {
            return false;
        }

        // Load traders data from DB
        LoadTraders();

        // Initialize quest list
        LoadQuestSelectionList();

        // Initialize trader filter
        LoadTraderFilter();

        // Clear search
        TxtQuestSearch.Text = string.Empty;

        // Update localized text
        UpdateLocalizedText();

        // Clear prerequisites preview
        PrerequisitesList.ItemsSource = null;
        UpdateSummaryCounts();

        return true;
    }

    private async void LoadTraders()
    {
        var traderDbService = TraderDbService.Instance;
        if (!traderDbService.IsLoaded)
        {
            await traderDbService.LoadTradersAsync();
        }
        _cachedTraders = traderDbService.AllTraders.ToList();
    }

    private void LoadQuestSelectionList()
    {
        var tasks = _graphService.GetAllTasks();

        _allQuestItems = tasks
            .Where(t => !string.IsNullOrEmpty(t.NormalizedName))
            .Where(t =>
            {
                var status = _progressService.GetStatus(t);
                return status != QuestStatus.Done && status != QuestStatus.Failed;
            })
            .Select(t =>
            {
                var (displayName, subtitleName, showSubtitle) = GetLocalizedQuestNames(t);
                return new QuestSelectionItem
                {
                    Quest = t,
                    DisplayName = displayName,
                    SubtitleName = subtitleName,
                    SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                    TraderName = GetLocalizedTraderName(t.Trader),
                    IsCompleted = false,
                    IsSelected = false
                };
            })
            .OrderBy(q => q.TraderName)
            .ThenBy(q => q.DisplayName)
            .ToList();

        _filteredQuestItems = _allQuestItems.ToList();
        QuestSelectionList.ItemsSource = _filteredQuestItems;
    }

    private void LoadTraderFilter()
    {
        var tasks = _graphService.GetAllTasks();

        var traders = tasks
            .Select(t => t.Trader)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        CmbQuestTraderFilter.Items.Clear();
        CmbQuestTraderFilter.Items.Add(new ComboBoxItem { Content = _loc.AllTraders, Tag = "All" });

        foreach (var trader in traders)
        {
            CmbQuestTraderFilter.Items.Add(new ComboBoxItem
            {
                Content = GetLocalizedTraderName(trader),
                Tag = trader
            });
        }

        CmbQuestTraderFilter.SelectedIndex = 0;
    }

    private void UpdateLocalizedText()
    {
        TxtTitle.Text = _loc.InProgressQuestInputTitle;
        TxtQuestSelectionHeader.Text = _loc.QuestSelection;
        TxtTraderFilterLabel.Text = _loc.TraderFilter;
        TxtPrerequisitesHeader.Text = _loc.PrerequisitesPreview;
        TxtPrerequisitesDesc.Text = _loc.PrerequisitesDescription;
        BtnCancel.Content = _loc.Cancel;
        BtnApply.Content = _loc.Apply;

        // Update "All" item in trader filter
        if (CmbQuestTraderFilter.Items.Count > 0 && CmbQuestTraderFilter.Items[0] is ComboBoxItem allItem)
        {
            allItem.Content = _loc.AllTraders;
        }
    }

    private (string DisplayName, string Subtitle, bool ShowSubtitle) GetLocalizedQuestNames(TarkovTask task)
    {
        var lang = _loc.CurrentLanguage;

        if (lang == AppLanguage.EN)
        {
            return (task.Name, string.Empty, false);
        }

        var localizedName = lang switch
        {
            AppLanguage.KO => task.NameKo,
            AppLanguage.JA => task.NameJa,
            _ => null
        };

        if (!string.IsNullOrEmpty(localizedName))
        {
            return (localizedName, task.Name, true);
        }

        return (task.Name, string.Empty, false);
    }

    private string GetLocalizedTraderName(string? trader)
    {
        if (string.IsNullOrEmpty(trader)) return string.Empty;

        if (_cachedTraders != null)
        {
            var traderData = _cachedTraders.FirstOrDefault(t =>
                string.Equals(t.Name, trader, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.NormalizedName, trader, StringComparison.OrdinalIgnoreCase));

            if (traderData != null)
            {
                return _loc.CurrentLanguage switch
                {
                    AppLanguage.KO => traderData.NameKo ?? traderData.Name,
                    AppLanguage.JA => traderData.NameJa ?? traderData.Name,
                    _ => traderData.Name
                };
            }
        }

        return trader;
    }

    private void FilterQuests()
    {
        if (_allQuestItems == null) return;

        var searchText = TxtQuestSearch.Text?.Trim() ?? string.Empty;
        var selectedTrader = (CmbQuestTraderFilter.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";

        _filteredQuestItems = _allQuestItems
            .Where(q =>
            {
                var matchesSearch = string.IsNullOrEmpty(searchText) ||
                    q.Quest.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    (q.Quest.NameKo?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (q.Quest.NameJa?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);

                var matchesTrader = selectedTrader == "All" ||
                    string.Equals(q.Quest.Trader, selectedTrader, StringComparison.OrdinalIgnoreCase);

                return matchesSearch && matchesTrader;
            })
            .ToList();

        QuestSelectionList.ItemsSource = _filteredQuestItems;
    }

    private void UpdatePrerequisitePreview()
    {
        if (_allQuestItems == null) return;

        var selectedQuests = _allQuestItems
            .Where(q => q.IsSelected)
            .Select(q => q.Quest)
            .ToList();

        var allPrereqs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var quest in selectedQuests)
        {
            if (string.IsNullOrEmpty(quest.NormalizedName)) continue;

            var prereqs = _graphService.GetAllPrerequisites(quest.NormalizedName);
            foreach (var prereq in prereqs)
            {
                if (_progressService.GetStatus(prereq) != QuestStatus.Done &&
                    !string.IsNullOrEmpty(prereq.NormalizedName))
                {
                    allPrereqs.Add(prereq.NormalizedName);
                }
            }
        }

        // Remove selected quests from prerequisites
        foreach (var quest in selectedQuests)
        {
            if (!string.IsNullOrEmpty(quest.NormalizedName))
            {
                allPrereqs.Remove(quest.NormalizedName);
            }
        }

        var prereqItems = allPrereqs
            .Select(name => _graphService.GetTask(name))
            .Where(t => t != null)
            .Select(t =>
            {
                var (displayName, subtitleName, showSubtitle) = GetLocalizedQuestNames(t!);
                return new PrerequisitePreviewItem
                {
                    Quest = t!,
                    DisplayName = displayName,
                    SubtitleName = subtitleName,
                    SubtitleVisibility = showSubtitle ? Visibility.Visible : Visibility.Collapsed,
                    TraderName = GetLocalizedTraderName(t!.Trader)
                };
            })
            .OrderBy(p => p.TraderName)
            .ThenBy(p => p.DisplayName)
            .ToList();

        PrerequisitesList.ItemsSource = prereqItems;
        UpdateSummaryCounts();
    }

    private void UpdateSummaryCounts()
    {
        var selectedCount = _allQuestItems?.Count(q => q.IsSelected) ?? 0;
        var prereqCount = (PrerequisitesList.ItemsSource as IEnumerable<PrerequisitePreviewItem>)?.Count() ?? 0;

        TxtSelectedQuestsCount.Text = string.Format(_loc.SelectedQuestsCount, selectedCount);
        TxtPrerequisitesCount.Text = string.Format(_loc.PrerequisitesToComplete, prereqCount);

        BtnApply.IsEnabled = selectedCount > 0;
    }

    private InProgressQuestInputResult BuildResult()
    {
        var selectedQuests = _allQuestItems?
            .Where(q => q.IsSelected)
            .Select(q => q.Quest)
            .ToList() ?? new List<TarkovTask>();

        var prerequisitesToComplete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var quest in selectedQuests)
        {
            if (string.IsNullOrEmpty(quest.NormalizedName)) continue;

            var prereqs = _graphService.GetAllPrerequisites(quest.NormalizedName);
            foreach (var prereq in prereqs)
            {
                if (_progressService.GetStatus(prereq) != QuestStatus.Done &&
                    !string.IsNullOrEmpty(prereq.NormalizedName))
                {
                    prerequisitesToComplete.Add(prereq.NormalizedName);
                }
            }
        }

        // Remove selected quests from prerequisites
        foreach (var quest in selectedQuests)
        {
            if (!string.IsNullOrEmpty(quest.NormalizedName))
            {
                prerequisitesToComplete.Remove(quest.NormalizedName);
            }
        }

        return new InProgressQuestInputResult
        {
            SelectedQuests = selectedQuests,
            PrerequisitesToComplete = prerequisitesToComplete.ToList()
        };
    }

    #region Event Handlers

    private void TxtQuestSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchDebounceTimer.Tick += (s, args) =>
        {
            _searchDebounceTimer.Stop();
            FilterQuests();
        };
        _searchDebounceTimer.Start();
    }

    private void CmbQuestTraderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_allQuestItems == null) return;
        FilterQuests();
    }

    private void QuestSelection_CheckChanged(object sender, RoutedEventArgs e)
    {
        UpdatePrerequisitePreview();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }

    private void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (_allQuestItems == null || _allQuestItems.Count(q => q.IsSelected) == 0)
        {
            MessageBox.Show(
                _loc.NoQuestsSelected,
                _loc.CurrentLanguage switch { AppLanguage.KO => "Notice", AppLanguage.JA => "Notice", _ => "Notice" },
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Result = BuildResult();
        Close();
    }

    #endregion
}

/// <summary>
/// Result from the InProgressQuestInputDialog.
/// </summary>
public class InProgressQuestInputResult
{
    /// <summary>
    /// Quests that user selected as in-progress.
    /// </summary>
    public List<TarkovTask> SelectedQuests { get; set; } = new();

    /// <summary>
    /// Prerequisites that need to be completed.
    /// </summary>
    public List<string> PrerequisitesToComplete { get; set; } = new();
}
