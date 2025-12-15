using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using TarkovDBEditor.Models;
using TarkovDBEditor.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace TarkovDBEditor.Views;

public partial class QuestRequirementsView : Window
{
    private readonly QuestRequirementsViewModel _viewModel;
    private bool _webViewInitialized = false;
    private string? _pendingWikiUrl = null;

    public QuestRequirementsView()
    {
        InitializeComponent();
        _viewModel = new QuestRequirementsViewModel();
        DataContext = _viewModel;

        Loaded += async (_, _) =>
        {
            await _viewModel.LoadDataAsync();
            UpdateProgressText();
            await InitializeWebViewAsync();
        };

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.FilteredQuests) ||
                e.PropertyName == nameof(_viewModel.SelectedQuestRequirements))
            {
                UpdateProgressText();
            }
            else if (e.PropertyName == nameof(_viewModel.SelectedQuest))
            {
                LoadWikiPage();
            }
        };
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await WikiWebView.EnsureCoreWebView2Async();
            WikiWebView.CoreWebView2.NavigationCompleted += WikiWebView_NavigationCompleted;
            _webViewInitialized = true;

            // If there was a pending URL, load it now
            if (!string.IsNullOrEmpty(_pendingWikiUrl))
            {
                WikiWebView.Source = new Uri(_pendingWikiUrl);
                _pendingWikiUrl = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebView2 Init Error] {ex.Message}");
            WikiEmptyText.Text = "WebView2 not available. Please install the WebView2 Runtime.";
            WikiEmptyText.Visibility = Visibility.Visible;
        }
    }

    private static string GetInjectionScript()
    {
        // CSS to show ONLY main.page__main and hide everything else
        const string css = @"
            /* Hide ALL body children first */
            body > * {
                display: none !important;
            }

            /* Show only the path to main.page__main */
            body > .main-container,
            body > .resizable-container,
            body > div:has(.page__main),
            .resizable-container,
            .resizable-container > .page,
            .page.has-right-rail {
                display: block !important;
            }

            /* Show main content area */
            main.page__main {
                display: block !important;
                width: 100% !important;
                max-width: 100% !important;
                padding: 0 !important;
                margin: 0 !important;
            }

            /* Main container styling */
            .main-container {
                margin: 0 !important;
                padding: 0 !important;
                width: 100% !important;
            }

            /* Hide sidebar */
            .page__right-rail,
            aside {
                display: none !important;
            }

            /* Hide unnecessary elements inside main */
            .page-side-tools__wrapper,
            .page-side-tools,
            .page-header__actions,
            .page-header__languages,
            .page-footer,
            .article-categories,
            .wds-collapsible-panel,
            .CategorySelect,
            #articleCategories {
                display: none !important;
            }

            /* Hide ads and popups */
            fandom-ad,
            [class*='fandom-ad'],
            .community-header-wrapper,
            .cnx-main-container,
            [class*='cnx-'],
            .ad-slot,
            .gpt-ad,
            [id*='google_ads'],
            [class*='sponsored'],
            .top_leaderboard-odyssey-wrapper,
            [class*='leaderboard'],
            .navbox,
            .va-navbox-border,
            .va-navbox-bottom,
            .global-footer {
                display: none !important;
            }

            /* Dark mode for body and content */
            html, body {
                background-color: #1E1E1E !important;
            }
            .page, .page__main, .mw-parser-output {
                background-color: #1E1E1E !important;
                color: #E0E0E0 !important;
            }

            /* Links */
            .mw-parser-output a {
                color: #6DB3F2 !important;
            }
            .mw-parser-output a:visited {
                color: #9B7ED9 !important;
            }

            /* Page header/title styling */
            .page-header {
                background-color: #1E1E1E !important;
                padding: 8px 0 !important;
            }
            .page-header__title {
                color: #FFFFFF !important;
            }
            .page-header__categories,
            .page-header__meta {
                display: none !important;
            }

            /* Infobox styling */
            .portable-infobox {
                background-color: #2D2D2D !important;
                border-color: #404040 !important;
            }
            .portable-infobox .pi-title,
            .portable-infobox .pi-header {
                background-color: #383838 !important;
                color: #E0E0E0 !important;
            }
            .portable-infobox .pi-data-label,
            .portable-infobox .pi-data-value,
            .portable-infobox .pi-secondary-font {
                color: #E0E0E0 !important;
                border-color: #404040 !important;
            }
            .portable-infobox a {
                color: #6DB3F2 !important;
            }

            /* Table styling */
            table, table.wikitable, .article-table {
                background-color: #2D2D2D !important;
                border-color: #404040 !important;
                color: #E0E0E0 !important;
            }
            table th, table.wikitable th {
                background-color: #383838 !important;
                color: #E0E0E0 !important;
            }
            table td, table th, table.wikitable td, table.wikitable th {
                border-color: #404040 !important;
            }

            /* Collapsible elements */
            .mw-collapsible {
                background-color: #2D2D2D !important;
                border-color: #404040 !important;
            }

            /* Hide specific sections */
            #Dialogue, #Rewards, #Trivia, #Gallery, #Behind_the_scenes, #Notes,
            span.mw-headline#Dialogue, span.mw-headline#Rewards, span.mw-headline#Trivia,
            span.mw-headline#Gallery, span.mw-headline#Behind_the_scenes, span.mw-headline#Notes {
                display: none !important;
            }
        ";

        // JavaScript to inject CSS and hide sections
        return $@"
            (function() {{
                // Remove existing style if any
                var existing = document.getElementById('tarkov-wiki-cleaner');
                if (existing) existing.remove();

                // Inject CSS immediately to <html> to ensure it's applied first
                var style = document.createElement('style');
                style.id = 'tarkov-wiki-cleaner';
                style.textContent = `{css.Replace("`", "\\`")}`;

                // Try to inject as early as possible
                var target = document.head || document.documentElement;
                if (target.firstChild) {{
                    target.insertBefore(style, target.firstChild);
                }} else {{
                    target.appendChild(style);
                }}

                // Hide sections by JavaScript
                function hideSections() {{
                    var sectionsToHide = ['Dialogue', 'Rewards', 'Trivia', 'Gallery', 'Behind_the_scenes', 'Notes'];
                    var headings = document.querySelectorAll('h2, h3');
                    headings.forEach(function(h) {{
                        var span = h.querySelector('.mw-headline');
                        if (span && sectionsToHide.includes(span.id)) {{
                            h.style.setProperty('display', 'none', 'important');
                            var el = h.nextElementSibling;
                            while (el && !el.matches('h2, h3')) {{
                                el.style.setProperty('display', 'none', 'important');
                                el = el.nextElementSibling;
                            }}
                        }}
                    }});
                }}

                // Run immediately
                hideSections();

                // Run again after short delays to catch dynamic content
                setTimeout(hideSections, 100);
                setTimeout(hideSections, 500);
                setTimeout(hideSections, 1000);

                // Also run on DOMContentLoaded if not yet fired
                if (document.readyState === 'loading') {{
                    document.addEventListener('DOMContentLoaded', hideSections);
                }}

                // Watch for dynamic content changes
                var observer = new MutationObserver(function() {{
                    hideSections();
                }});

                function startObserver() {{
                    if (document.body) {{
                        observer.observe(document.body, {{ childList: true, subtree: true }});
                    }}
                }}

                if (document.body) {{
                    startObserver();
                }} else {{
                    document.addEventListener('DOMContentLoaded', startObserver);
                }}
            }})();
        ";
    }

    private void LoadWikiPage()
    {
        if (_viewModel.SelectedQuest == null || string.IsNullOrEmpty(_viewModel.SelectedQuest.WikiPageLink))
        {
            WikiEmptyText.Text = "No wiki page available for this quest";
            WikiEmptyText.Visibility = Visibility.Visible;
            WikiWebView.Visibility = Visibility.Collapsed;
            WikiLoadingOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var wikiUrl = _viewModel.SelectedQuest.WikiPageLink;

        if (!_webViewInitialized)
        {
            // Store URL to load after initialization
            _pendingWikiUrl = wikiUrl;
            WikiEmptyText.Text = "Initializing browser...";
            WikiEmptyText.Visibility = Visibility.Visible;
            WikiWebView.Visibility = Visibility.Collapsed;
            return;
        }

        WikiEmptyText.Visibility = Visibility.Collapsed;
        WikiWebView.Visibility = Visibility.Visible;
        WikiLoadingOverlay.Visibility = Visibility.Visible;

        WikiWebView.Source = new Uri(wikiUrl);
    }

    private async void WikiWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            System.Diagnostics.Debug.WriteLine($"[Wiki Navigation Error] Status: {e.WebErrorStatus}");
        }

        // Inject CSS/JS after page loads
        try
        {
            await WikiWebView.CoreWebView2.ExecuteScriptAsync(GetInjectionScript());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Wiki Script Error] {ex.Message}");
        }

        WikiLoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void WikiRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_webViewInitialized && WikiWebView.Source != null)
        {
            WikiLoadingOverlay.Visibility = Visibility.Visible;
            WikiWebView.Reload();
        }
    }

    private void WikiOpenExternal_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest?.WikiPageLink != null)
        {
            OpenUrl(_viewModel.SelectedQuest.WikiPageLink);
        }
    }

    private void UpdateProgressText()
    {
        var (approved, total) = _viewModel.GetApprovalProgress();
        var percent = total > 0 ? (approved * 100 / total) : 0;
        ProgressText.Text = $"{approved}/{total} ({percent}%)";
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        _viewModel.FilterMode = FilterCombo.SelectedIndex switch
        {
            0 => QuestFilterMode.All,
            1 => QuestFilterMode.PendingApproval,
            2 => QuestFilterMode.Approved,
            3 => QuestFilterMode.HasRequirements,
            4 => QuestFilterMode.NoRequirements,
            _ => QuestFilterMode.All
        };
    }

    private void MapFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;

        _viewModel.SelectedMapFilter = MapFilterCombo.SelectedItem as string;
        UpdateProgressText();

        if (_viewModel.SelectedMapFilter != null)
        {
            StatusText.Text = $"Filtering by map: {_viewModel.SelectedMapFilter}";
        }
    }

    private void ClearMapFilter_Click(object sender, RoutedEventArgs e)
    {
        MapFilterCombo.SelectedItem = null;
        _viewModel.SelectedMapFilter = null;
        StatusText.Text = "Map filter cleared";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Handled by binding
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Refreshing...";
        await _viewModel.LoadDataAsync();
        UpdateProgressText();
        StatusText.Text = "Data refreshed";
    }

    private void OpenWiki_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest?.WikiPageLink != null)
        {
            OpenUrl(_viewModel.SelectedQuest.WikiPageLink);
        }
    }

    private void OpenRequiredQuestWiki_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is QuestRequirementItem req)
        {
            if (!string.IsNullOrEmpty(req.RequiredQuestWikiLink))
            {
                OpenUrl(req.RequiredQuestWikiLink);
            }
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is QuestRequirementItem req)
        {
            var isApproved = cb.IsChecked ?? false;
            req.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateApprovalAsync(req.Id, isApproved);
            UpdateProgressText();
        }
    }

    private async void MinLevelCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;
        if (sender is not CheckBox cb) return;

        var isApproved = cb.IsChecked ?? false;
        _viewModel.SelectedQuest.MinLevelApproved = isApproved; // Update model immediately
        await _viewModel.UpdateMinLevelApprovalAsync(_viewModel.SelectedQuest.Id, isApproved);
        UpdateProgressText();
        StatusText.Text = isApproved
            ? $"Approved MinLevel requirement for {_viewModel.SelectedQuest.Name}"
            : $"Unapproved MinLevel requirement for {_viewModel.SelectedQuest.Name}";
    }

    private async void MinScavKarmaCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;
        if (sender is not CheckBox cb) return;

        var isApproved = cb.IsChecked ?? false;
        _viewModel.SelectedQuest.MinScavKarmaApproved = isApproved; // Update model immediately
        await _viewModel.UpdateMinScavKarmaApprovalAsync(_viewModel.SelectedQuest.Id, isApproved);
        UpdateProgressText();
        StatusText.Text = isApproved
            ? $"Approved MinScavKarma requirement for {_viewModel.SelectedQuest.Name}"
            : $"Unapproved MinScavKarma requirement for {_viewModel.SelectedQuest.Name}";
    }

    private async void RequiredEditionCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;
        if (sender is not CheckBox cb) return;

        var isApproved = cb.IsChecked ?? false;
        _viewModel.SelectedQuest.RequiredEditionApproved = isApproved; // Update model immediately
        await _viewModel.UpdateRequiredEditionApprovalAsync(_viewModel.SelectedQuest.Id, isApproved);
        UpdateProgressText();
        StatusText.Text = isApproved
            ? $"Approved RequiredEdition ({_viewModel.SelectedQuest.RequiredEdition}) for {_viewModel.SelectedQuest.Name}"
            : $"Unapproved RequiredEdition requirement for {_viewModel.SelectedQuest.Name}";
    }

    private async void QuestApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;
        if (sender is not CheckBox cb) return;

        var isApproved = cb.IsChecked ?? false;
        _viewModel.SelectedQuest.IsApproved = isApproved; // Update model immediately
        await _viewModel.UpdateQuestApprovalAsync(_viewModel.SelectedQuest.Id, isApproved);
        UpdateProgressText();
        StatusText.Text = isApproved
            ? $"Approved quest: {_viewModel.SelectedQuest.Name}"
            : $"Unapproved quest: {_viewModel.SelectedQuest.Name}";
    }

    private async void ApproveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;

        var currentQuest = _viewModel.SelectedQuest;

        // Approve MinLevel if exists
        if (currentQuest.HasMinLevel && !currentQuest.MinLevelApproved)
        {
            currentQuest.MinLevelApproved = true;
            await _viewModel.UpdateMinLevelApprovalAsync(currentQuest.Id, true);
        }

        // Approve MinScavKarma if exists
        if (currentQuest.HasMinScavKarma && !currentQuest.MinScavKarmaApproved)
        {
            currentQuest.MinScavKarmaApproved = true;
            await _viewModel.UpdateMinScavKarmaApprovalAsync(currentQuest.Id, true);
        }

        // Approve RequiredEdition if exists
        if (currentQuest.HasRequiredEdition && !currentQuest.RequiredEditionApproved)
        {
            currentQuest.RequiredEditionApproved = true;
            await _viewModel.UpdateRequiredEditionApprovalAsync(currentQuest.Id, true);
        }

        // Approve quest requirements
        foreach (var req in _viewModel.SelectedQuestRequirements)
        {
            if (!req.IsApproved)
            {
                req.IsApproved = true;
                await _viewModel.UpdateApprovalAsync(req.Id, true);
            }
        }

        // Approve quest objectives
        foreach (var obj in _viewModel.SelectedQuestObjectives)
        {
            if (!obj.IsApproved)
            {
                obj.IsApproved = true;
                await _viewModel.UpdateObjectiveApprovalAsync(obj.Id, true);
            }
        }

        // Approve optional quests (other choices)
        foreach (var opt in _viewModel.SelectedOptionalQuests)
        {
            if (!opt.IsApproved)
            {
                opt.IsApproved = true;
                await _viewModel.UpdateOptionalQuestApprovalAsync(opt.Id, true);
            }
        }

        // Approve required items
        foreach (var item in _viewModel.SelectedRequiredItems)
        {
            if (!item.IsApproved)
            {
                item.IsApproved = true;
                await _viewModel.UpdateRequiredItemApprovalAsync(item.Id, true);
            }
        }

        // Approve the quest itself
        if (!currentQuest.IsApproved)
        {
            currentQuest.IsApproved = true;
            await _viewModel.UpdateQuestApprovalAsync(currentQuest.Id, true);
        }

        UpdateProgressText();
        StatusText.Text = $"Approved all requirements for {currentQuest.Name}";

        // If filter is active and quest no longer matches, select next item
        await SelectNextQuestIfNeeded(currentQuest);
    }

    private async void UnapproveAll_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedQuest == null) return;

        var currentQuest = _viewModel.SelectedQuest;

        // Unapprove the quest itself first
        if (currentQuest.IsApproved)
        {
            currentQuest.IsApproved = false;
            await _viewModel.UpdateQuestApprovalAsync(currentQuest.Id, false);
        }

        // Unapprove MinLevel if exists
        if (currentQuest.HasMinLevel && currentQuest.MinLevelApproved)
        {
            currentQuest.MinLevelApproved = false;
            await _viewModel.UpdateMinLevelApprovalAsync(currentQuest.Id, false);
        }

        // Unapprove MinScavKarma if exists
        if (currentQuest.HasMinScavKarma && currentQuest.MinScavKarmaApproved)
        {
            currentQuest.MinScavKarmaApproved = false;
            await _viewModel.UpdateMinScavKarmaApprovalAsync(currentQuest.Id, false);
        }

        // Unapprove RequiredEdition if exists
        if (currentQuest.HasRequiredEdition && currentQuest.RequiredEditionApproved)
        {
            currentQuest.RequiredEditionApproved = false;
            await _viewModel.UpdateRequiredEditionApprovalAsync(currentQuest.Id, false);
        }

        // Unapprove quest requirements
        foreach (var req in _viewModel.SelectedQuestRequirements)
        {
            if (req.IsApproved)
            {
                req.IsApproved = false;
                await _viewModel.UpdateApprovalAsync(req.Id, false);
            }
        }

        // Unapprove quest objectives
        foreach (var obj in _viewModel.SelectedQuestObjectives)
        {
            if (obj.IsApproved)
            {
                obj.IsApproved = false;
                await _viewModel.UpdateObjectiveApprovalAsync(obj.Id, false);
            }
        }

        // Unapprove optional quests (other choices)
        foreach (var opt in _viewModel.SelectedOptionalQuests)
        {
            if (opt.IsApproved)
            {
                opt.IsApproved = false;
                await _viewModel.UpdateOptionalQuestApprovalAsync(opt.Id, false);
            }
        }

        // Unapprove required items
        foreach (var item in _viewModel.SelectedRequiredItems)
        {
            if (item.IsApproved)
            {
                item.IsApproved = false;
                await _viewModel.UpdateRequiredItemApprovalAsync(item.Id, false);
            }
        }

        UpdateProgressText();
        StatusText.Text = $"Unapproved all requirements for {currentQuest.Name}";
    }

    private async Task SelectNextQuestIfNeeded(QuestItem currentQuest)
    {
        // Re-apply filter to update the list
        _viewModel.ApplyFilter();

        // Check if current quest is still in filtered list
        var filteredQuests = _viewModel.FilteredQuests;
        if (filteredQuests.Contains(currentQuest))
        {
            // Quest still matches filter, no need to change selection
            return;
        }

        // Quest no longer matches filter, select next available quest
        if (filteredQuests.Count > 0)
        {
            // Find the index where the current quest would have been
            var currentIndex = _viewModel.FilteredQuests.ToList().FindIndex(q =>
                string.Compare(q.Name, currentQuest.Name, StringComparison.OrdinalIgnoreCase) > 0);

            if (currentIndex < 0) currentIndex = 0;
            if (currentIndex >= filteredQuests.Count) currentIndex = filteredQuests.Count - 1;

            _viewModel.SelectedQuest = filteredQuests[currentIndex];
            QuestList.SelectedItem = _viewModel.SelectedQuest;
            QuestList.ScrollIntoView(_viewModel.SelectedQuest);

            StatusText.Text = $"Auto-selected next quest: {_viewModel.SelectedQuest.Name}";
        }
        else
        {
            // No more quests match the filter
            _viewModel.SelectedQuest = null;
            StatusText.Text = "No more quests matching the current filter";
        }

        await Task.CompletedTask;
    }

    private async void ObjectiveApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is QuestObjectiveItem obj)
        {
            var isApproved = cb.IsChecked ?? false;
            obj.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateObjectiveApprovalAsync(obj.Id, isApproved);
            UpdateProgressText();
            StatusText.Text = isApproved
                ? $"Approved objective: {obj.Description.Substring(0, Math.Min(50, obj.Description.Length))}..."
                : $"Unapproved objective";
        }
    }

    private async void ObjectivesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ObjectivesList.SelectedItem is not QuestObjectiveItem selectedObj)
            return;

        // Check if the objective has an effective map (MapName or QuestLocation)
        if (string.IsNullOrEmpty(selectedObj.EffectiveMapName))
        {
            MessageBox.Show("This objective does not have a map location specified.\nThe quest also doesn't have a location set.",
                "No Map", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Open the map editor window
        var mapEditor = new MapEditorWindow(selectedObj);
        var result = mapEditor.ShowDialog();

        if (result == true && mapEditor.WasSaved)
        {
            System.Diagnostics.Debug.WriteLine($"[ObjectivesList_MouseDoubleClick] Before save - LocationPoints: {selectedObj.LocationPoints.Count}, OptionalPoints: {selectedObj.OptionalPoints.Count}");

            // Save LocationPoints to database
            var locationJson = selectedObj.LocationPointsJson;
            System.Diagnostics.Debug.WriteLine($"[ObjectivesList_MouseDoubleClick] locationJson: {locationJson}");
            await _viewModel.UpdateObjectiveLocationPointsAsync(selectedObj.Id, locationJson);

            // Save OptionalPoints to database
            var optionalJson = selectedObj.OptionalPointsJson;
            System.Diagnostics.Debug.WriteLine($"[ObjectivesList_MouseDoubleClick] optionalJson: {optionalJson}");
            await _viewModel.UpdateObjectiveOptionalPointsAsync(selectedObj.Id, optionalJson);

            var desc = selectedObj.Description.Length > 50
                ? selectedObj.Description.Substring(0, 50) + "..."
                : selectedObj.Description;

            var pointInfo = new List<string>();
            if (selectedObj.LocationPoints.Count > 0)
                pointInfo.Add($"{selectedObj.LocationPoints.Count} area");
            if (selectedObj.OptionalPoints.Count > 0)
                pointInfo.Add($"{selectedObj.OptionalPoints.Count} OR");

            StatusText.Text = $"Points saved ({string.Join(", ", pointInfo)}) for: {desc}";

            System.Diagnostics.Debug.WriteLine($"[ObjectivesList_MouseDoubleClick] After save - StatusText: {StatusText.Text}");
            System.Diagnostics.Debug.WriteLine($"[ObjectivesList_MouseDoubleClick] HasOptionalPoints: {selectedObj.HasOptionalPoints}, OptionalPointsDisplay: {selectedObj.OptionalPointsDisplay}");

            // Force UI refresh for the updated item
            ObjectivesList.Items.Refresh();
        }
    }

    private async void OptionalQuestApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is OptionalQuestItem opt)
        {
            var isApproved = cb.IsChecked ?? false;
            opt.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateOptionalQuestApprovalAsync(opt.Id, isApproved);
            UpdateProgressText();
            StatusText.Text = isApproved
                ? $"Approved alternative quest: {opt.AlternativeQuestName}"
                : $"Unapproved alternative quest: {opt.AlternativeQuestName}";
        }
    }

    private void OpenAlternativeQuestWiki_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is OptionalQuestItem opt)
        {
            if (!string.IsNullOrEmpty(opt.AlternativeQuestWikiLink))
            {
                OpenUrl(opt.AlternativeQuestWikiLink);
            }
        }
    }

    private async void RequiredItemApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is QuestRequiredItemViewModel item)
        {
            var isApproved = cb.IsChecked ?? false;
            item.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateRequiredItemApprovalAsync(item.Id, isApproved);
            UpdateProgressText();
            StatusText.Text = isApproved
                ? $"Approved required item: {item.ItemName}"
                : $"Unapproved required item: {item.ItemName}";
        }
    }

    private async void ApiMarkerApprovalCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox cb && cb.DataContext is ApiReferenceMarkerItem item)
        {
            var isApproved = cb.IsChecked ?? false;
            item.IsApproved = isApproved; // Update model immediately
            await _viewModel.UpdateApiMarkerApprovalAsync(item.Id, isApproved);
            StatusText.Text = isApproved
                ? $"Approved API marker: {item.DisplayName}"
                : $"Unapproved API marker: {item.DisplayName}";
        }
    }

    private async void ApplyApiMarkerLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not ApiReferenceMarkerItem apiMarker) return;

        // Get the objectives for this quest
        var objectives = _viewModel.SelectedQuestObjectives.ToList();

        if (objectives.Count == 0)
        {
            MessageBox.Show("No objectives found for this quest.", "No Objectives", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // If only one objective, apply directly
        if (objectives.Count == 1)
        {
            var objective = objectives[0];
            await ApplyMarkerToObjective(apiMarker, objective);
            return;
        }

        // Show selection dialog for multiple objectives
        var dialog = new SelectObjectiveDialog(objectives, apiMarker.DisplayName);
        if (dialog.ShowDialog() == true && dialog.SelectedObjective != null)
        {
            await ApplyMarkerToObjective(apiMarker, dialog.SelectedObjective);
        }
    }

    private async Task ApplyMarkerToObjective(ApiReferenceMarkerItem apiMarker, QuestObjectiveItem objective)
    {
        // Check if the objective's map matches the marker's map (using normalized comparison)
        if (!string.IsNullOrEmpty(objective.EffectiveMapName) &&
            !MapConfig.AreMapNamesEqual(objective.EffectiveMapName, apiMarker.MapKey))
        {
            var result = MessageBox.Show(
                $"The objective's map ({objective.EffectiveMapName}) differs from the marker's map ({apiMarker.MapKey}).\n\nDo you want to apply anyway?",
                "Map Mismatch",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;
        }

        // Apply the location
        await _viewModel.ApplyApiMarkerLocationToObjectiveAsync(apiMarker, objective);

        // Refresh the UI
        ObjectivesList.Items.Refresh();

        var desc = objective.Description.Length > 40
            ? objective.Description.Substring(0, 40) + "..."
            : objective.Description;

        StatusText.Text = $"Applied API marker location ({apiMarker.X:F1}, {apiMarker.Z:F1}) to: {desc}";
    }
}
