using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;
using TarkovDBEditor.ViewModels;
using TarkovDBEditor.Views;

namespace TarkovDBEditor;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Subscribe to ViewModel events
        ViewModel.RequestCreateTableDialog += OnRequestCreateTableDialog;
        ViewModel.RequestAddColumnDialog += OnRequestAddColumnDialog;
        ViewModel.RequestEditRowDialog += OnRequestEditRowDialog;

        // Update DataGrid when table selection changes
        ViewModel.PropertyChanged += (s, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.SelectedTable))
            {
                UpdateDataGridColumns();
            }
        };
    }

    private void UpdateDataGridColumns()
    {
        DataGridView.Columns.Clear();

        if (ViewModel.SelectedTable == null) return;

        foreach (var col in ViewModel.SelectedTable.Columns)
        {
            DataGridColumn dgCol;

            if (col.Type == ColumnType.Boolean)
            {
                dgCol = new DataGridCheckBoxColumn
                {
                    Header = col.DisplayName,
                    Binding = new Binding($"[{col.Name}]")
                };
            }
            else if (col.IsForeignKey)
            {
                // For FK columns, create a ComboBox with lookup data
                var comboCol = new DataGridComboBoxColumn
                {
                    Header = $"{col.DisplayName} (FK)",
                };

                // Load lookup data
                var lookupData = DatabaseService.Instance.GetLookupData(
                    col.ForeignKeyTable!,
                    col.ForeignKeyColumn!);

                comboCol.ItemsSource = lookupData;
                comboCol.DisplayMemberPath = "Display";
                comboCol.SelectedValuePath = "Id";
                comboCol.SelectedValueBinding = new Binding($"[{col.Name}]");

                dgCol = comboCol;
            }
            else
            {
                dgCol = new DataGridTextColumn
                {
                    Header = col.DisplayName,
                    Binding = new Binding($"[{col.Name}]"),
                    IsReadOnly = col.IsAutoIncrement
                };
            }

            DataGridView.Columns.Add(dgCol);
        }
    }

    private void OnRequestCreateTableDialog(object? sender, TableSchema schema)
    {
        var dialog = new CreateTableDialog(schema) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.CreateTable(dialog.TableSchema);
        }
    }

    private void OnRequestAddColumnDialog(object? sender, ColumnSchema column)
    {
        var dialog = new AddColumnDialog(column, ViewModel.Tables.ToList()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.AddColumn(dialog.ColumnSchema);
            UpdateDataGridColumns();
        }
    }

    private void OnRequestEditRowDialog(object? sender, DataRow row)
    {
        if (ViewModel.SelectedTable == null) return;

        var dialog = new EditRowDialog(row, ViewModel.SelectedTable, ViewModel.Tables.ToList()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            // Check if this is a new row (no PK value) or existing
            var pkCol = ViewModel.SelectedTable.Columns.FirstOrDefault(c => c.IsPrimaryKey);
            if (pkCol != null && row.TryGetValue(pkCol.Name, out var pkVal) && pkVal != null && Convert.ToInt64(pkVal) > 0)
            {
                ViewModel.UpdateRow(dialog.DataRow);
            }
            else
            {
                ViewModel.InsertRow(dialog.DataRow);
            }
        }
    }
    private void NewDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SQLite Database|*.db",
            DefaultExt = ".db",
            FileName = "tarkov_data.db"
        };

        if (dialog.ShowDialog() == true)
        {
            DatabaseService.Instance.CreateDatabase(dialog.FileName);
            ViewModel.LoadTables();
            Title = $"Tarkov DB Editor - {dialog.FileName}";
        }
    }

    private void OpenDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "SQLite Database|*.db|All files|*.*",
            DefaultExt = ".db"
        };

        if (dialog.ShowDialog() == true)
        {
            DatabaseService.Instance.Connect(dialog.FileName);
            ViewModel.LoadTables();
            Title = $"Tarkov DB Editor - {dialog.FileName}";
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedRow == null || ViewModel.SelectedTable == null) return;

        var dialog = new EditRowDialog(ViewModel.SelectedRow, ViewModel.SelectedTable, ViewModel.Tables.ToList()) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.UpdateRow(dialog.DataRow);
        }
    }

    private async void MergeMapMarkers_Click(object sender, RoutedEventArgs e)
    {
        // 데이터베이스가 연결되어 있는지 확인
        if (!DatabaseService.Instance.IsConnected)
        {
            MessageBox.Show(
                "Please open or create a database first.",
                "No Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // 외부 DB 파일 선택
        var dialog = new OpenFileDialog
        {
            Filter = "SQLite Database|*.db|All files|*.*",
            DefaultExt = ".db",
            Title = "Select external database to merge markers from"
        };

        if (dialog.ShowDialog() != true) return;

        // 동일한 파일인지 확인
        if (string.Equals(Path.GetFullPath(dialog.FileName),
            Path.GetFullPath(DatabaseService.Instance.DatabasePath),
            StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Cannot merge from the same database that is currently open.",
                "Same Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ViewModel.StatusMessage = "Merging map markers...";
        IsEnabled = false;

        try
        {
            var result = await MapMarkerService.Instance.MergeMarkersFromExternalDbAsync(dialog.FileName);

            if (result.Success)
            {
                // 테이블 목록 새로고침
                ViewModel.LoadTables();

                var message = new System.Text.StringBuilder();
                message.AppendLine("Map markers merged successfully!");
                message.AppendLine();
                message.AppendLine($"Source: {Path.GetFileName(dialog.FileName)}");
                message.AppendLine($"Total markers in source: {result.TotalInExternal}");
                message.AppendLine();
                message.AppendLine($"Added (new): {result.Added}");
                message.AppendLine($"Updated (existing): {result.Updated}");

                ViewModel.StatusMessage = $"Merge complete: {result.Added} added, {result.Updated} updated";

                MessageBox.Show(
                    message.ToString(),
                    "Merge Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                ViewModel.StatusMessage = $"Merge failed: {result.ErrorMessage}";
                MessageBox.Show(
                    $"Merge failed:\n{result.ErrorMessage}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Merge failed: {ex.Message}";
            MessageBox.Show(
                $"Merge failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void ExportWikiItemCategories_Click(object sender, RoutedEventArgs e)
    {
        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wiki_data");
        var categoriesPath = Path.Combine(outputDir, "wiki_item_categories.json");
        var treePath = Path.Combine(outputDir, "wiki_category_tree.json");
        var structurePath = Path.Combine(outputDir, "wiki_category_structure.json");
        var itemsPath = Path.Combine(outputDir, "wiki_items.json");
        var missingPath = Path.Combine(outputDir, "dev_missing.json");
        var devOnlyPath = Path.Combine(outputDir, "dev_only.json");

        // Progress dialog 없이 간단히 처리
        ViewModel.StatusMessage = "Exporting Wiki item categories...";
        IsEnabled = false;

        try
        {
            using var wikiService = new TarkovWikiDataService();
            using var cacheService = new WikiCacheService(outputDir);

            // 캐시 로드
            ViewModel.StatusMessage = "Loading page cache...";
            await cacheService.LoadCacheAsync();
            var (cachedCount, withIconCount, withContentCount, withRevisionCount) = cacheService.GetCacheStats();
            ViewModel.StatusMessage = $"Cache loaded: {cachedCount} pages ({withContentCount} with content, {withRevisionCount} with revision, {withIconCount} with icons)";

            // 제외할 카테고리(Event content 등)의 아이템 목록 가져오기
            var excludedItems = await wikiService.GetExcludedItemsAsync(
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            var (result, tree, allCategoryDirectItems) = await wikiService.ExportAllCategoryDataAsync(
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            ViewModel.StatusMessage = "Building category structure...";
            var structure = wikiService.BuildCategoryStructure(tree, allCategoryDirectItems);

            // 모든 후보 아이템 목록
            var allCandidateItems = structure.LeafCategories
                .SelectMany(lc => lc.Value.Items)
                .Distinct()
                .ToList();

            // 페이지 캐시 업데이트 (리비전 체크로 변경된 페이지만 업데이트) - 먼저 실행
            ViewModel.StatusMessage = "Updating page cache (checking revisions)...";
            var cacheUpdateResult = await cacheService.UpdatePageCacheAsync(
                allCandidateItems,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            // 캐시에서 Infobox가 없는 페이지 찾기 (카테고리 설명 페이지 필터링)
            ViewModel.StatusMessage = "Checking for category description pages (from cache)...";
            var pagesWithoutInfobox = cacheService.GetPagesWithoutInfoboxFromCache(allCandidateItems);
            ViewModel.StatusMessage = $"Found {pagesWithoutInfobox.Count} pages without Infobox";

            ViewModel.StatusMessage = "Building item list...";
            var itemList = wikiService.BuildItemList(structure, tree, excludedItems, pagesWithoutInfobox);

            // 아이콘 URL 가져오기 (캐시된 콘텐츠 활용)
            ViewModel.StatusMessage = "Fetching icon URLs...";
            var itemNames = itemList.Items.Select(i => i.Name).ToList();
            var iconUrls = await cacheService.GetIconUrlsAsync(
                itemNames,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            // 아이템에 아이콘 URL 설정
            foreach (var item in itemList.Items)
            {
                if (iconUrls.TryGetValue(item.Name, out var iconUrl))
                {
                    item.IconUrl = iconUrl;
                }
            }

            // 캐시 저장
            ViewModel.StatusMessage = "Saving page cache...";
            await cacheService.SaveCacheAsync();

            ViewModel.StatusMessage = "Saving category JSON files...";

            await wikiService.SaveResultToJsonAsync(result, categoriesPath);
            await wikiService.SaveTreeToJsonAsync(tree, treePath);
            await wikiService.SaveStructureToJsonAsync(structure, structurePath);
            await wikiService.SaveItemListToJsonAsync(itemList, itemsPath);

            // tarkov.dev 데이터로 enrichment
            ViewModel.StatusMessage = "Fetching tarkov.dev data...";

            using var devService = new TarkovDevDataService();
            var enrichResult = await devService.EnrichWikiItemsAsync(
                itemsPath,
                itemsPath,  // 같은 파일에 덮어쓰기
                missingPath,
                devOnlyPath,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            // 아이콘 이미지 배치 다운로드
            ViewModel.StatusMessage = "Downloading icon images...";
            var iconItems = itemList.Items
                .Select(i => (i.Id, i.IconUrl))
                .ToList();
            var downloadResult = await cacheService.DownloadIconsAsync(
                iconItems,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            var itemsWithIcon = itemList.Items.Count(i => !string.IsNullOrEmpty(i.IconUrl));
            ViewModel.StatusMessage = $"Export complete: {enrichResult.MatchedCount}/{enrichResult.TotalItems} matched, {itemsWithIcon} with icons";

            MessageBox.Show(
                $"Export completed successfully!\n\n" +
                $"Root Category: {tree.RootCategory}\n" +
                $"Total Categories: {tree.Tree.Count}\n" +
                $"Leaf Categories: {structure.LeafCategories.Count}\n" +
                $"Total Items: {itemList.TotalItems}\n" +
                $"Duplicate Items: {structure.DuplicateItemCount}\n\n" +
                $"Page Cache (revision-based):\n" +
                $"- New pages: {cacheUpdateResult.NewPages}\n" +
                $"- Updated: {cacheUpdateResult.Updated}\n" +
                $"- Up-to-date (skipped): {cacheUpdateResult.UpToDate}\n" +
                $"- Failed: {cacheUpdateResult.Failed}\n\n" +
                $"Icon Data:\n" +
                $"- Items with icon URL: {itemsWithIcon}\n" +
                $"- Icons downloaded: {downloadResult.Downloaded}\n" +
                $"- Icons already cached: {downloadResult.AlreadyDownloaded}\n" +
                $"- Download failed: {downloadResult.Failed}\n\n" +
                $"tarkov.dev Enrichment:\n" +
                $"- Matched: {enrichResult.MatchedCount}\n" +
                $"- Wiki only (dev_missing): {enrichResult.MissingCount}\n" +
                $"- Dev only (dev_only): {enrichResult.DevOnlyCount}\n\n" +
                $"Saved to:\n" +
                $"- {itemsPath}\n" +
                $"- {missingPath}\n" +
                $"- {devOnlyPath}\n" +
                $"- {structurePath}\n" +
                $"- {treePath}\n" +
                $"- {categoriesPath}\n" +
                $"- wiki_data/icons/ (icon images)\n" +
                $"- wiki_data/cache/ (page cache)",
                "Wiki Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Export failed: {ex.Message}";
            MessageBox.Show(
                $"Export failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void FetchWikiData_Click(object sender, RoutedEventArgs e)
    {
        // 데이터베이스가 연결되어 있는지 확인
        if (!DatabaseService.Instance.IsConnected)
        {
            MessageBox.Show(
                "Please open or create a database first.",
                "No Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ViewModel.StatusMessage = "Fetching fresh data from Wiki (this may take a while)...";
        IsEnabled = false;

        try
        {
            using var refreshService = new RefreshDataService();
            var result = await refreshService.RefreshDataAsync(
                DatabaseService.Instance.DatabasePath,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            if (result.Success)
            {
                // 테이블 목록 새로고침
                ViewModel.LoadTables();

                var message = new System.Text.StringBuilder();
                message.AppendLine("Wiki data fetch completed successfully!");
                message.AppendLine();
                message.AppendLine($"Duration: {(result.CompletedAt - result.StartedAt).TotalSeconds:F1} seconds");
                message.AppendLine();

                if (result.ItemsUpdated)
                    message.AppendLine($"Items: {result.ItemsCount} items updated");
                else
                    message.AppendLine("Items: No changes detected");

                if (result.QuestsUpdated)
                    message.AppendLine($"Quests: {result.QuestsCount} quests updated");
                else
                    message.AppendLine("Quests: No changes detected");

                message.AppendLine();
                message.AppendLine($"Log saved to: {result.LogPath}");

                ViewModel.StatusMessage = $"Fetch complete: {result.ItemsCount} items, {result.QuestsCount} quests";

                MessageBox.Show(
                    message.ToString(),
                    "Fetch Wiki Data Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                ViewModel.StatusMessage = $"Fetch failed: {result.ErrorMessage}";
                MessageBox.Show(
                    $"Fetch failed:\n{result.ErrorMessage}\n\nLog saved to: {result.LogPath}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Fetch failed: {ex.Message}";
            MessageBox.Show(
                $"Fetch failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void RefreshDataFromCache_Click(object sender, RoutedEventArgs e)
    {
        // 데이터베이스가 연결되어 있는지 확인
        if (!DatabaseService.Instance.IsConnected)
        {
            MessageBox.Show(
                "Please open or create a database first.",
                "No Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ViewModel.StatusMessage = "Refreshing data from cache...";
        IsEnabled = false;

        try
        {
            using var refreshService = new RefreshDataService();
            using var tarkovDevService = new TarkovDevDataService();
            using var wikiCacheService = new WikiCacheService();
            var result = await refreshService.RefreshDataFromCacheAsync(
                DatabaseService.Instance.DatabasePath,
                tarkovDevService,
                wikiCacheService,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            if (result.Success)
            {
                // 테이블 목록 새로고침
                ViewModel.LoadTables();

                var message = new System.Text.StringBuilder();
                message.AppendLine("Data refresh from cache completed successfully!");
                message.AppendLine();
                message.AppendLine($"Duration: {(result.CompletedAt - result.StartedAt).TotalSeconds:F1} seconds");
                message.AppendLine();
                message.AppendLine($"Items: {result.ItemsCount} items");
                message.AppendLine($"Quests: {result.QuestsCount} quests");
                message.AppendLine();
                message.AppendLine($"Log saved to: {result.LogPath}");

                ViewModel.StatusMessage = $"Refresh complete: {result.ItemsCount} items, {result.QuestsCount} quests";

                MessageBox.Show(
                    message.ToString(),
                    "Refresh Data Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                ViewModel.StatusMessage = $"Refresh failed: {result.ErrorMessage}";
                MessageBox.Show(
                    $"Refresh failed:\n{result.ErrorMessage}\n\nLog saved to: {result.LogPath}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Refresh failed: {ex.Message}";
            MessageBox.Show(
                $"Refresh failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void RefreshHideoutData_Click(object sender, RoutedEventArgs e)
    {
        // 데이터베이스가 연결되어 있는지 확인
        if (!DatabaseService.Instance.IsConnected)
        {
            MessageBox.Show(
                "Please open or create a database first.",
                "No Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ViewModel.StatusMessage = "Refreshing hideout data from tarkov.dev...";
        IsEnabled = false;

        try
        {
            using var hideoutService = new HideoutDataService();
            var result = await hideoutService.RefreshHideoutDataAsync(
                DatabaseService.Instance.DatabasePath,
                downloadIcons: true,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            if (result.Success)
            {
                // 테이블 목록 새로고침
                ViewModel.LoadTables();

                var message = new System.Text.StringBuilder();
                message.AppendLine("Hideout data refresh completed successfully!");
                message.AppendLine();
                message.AppendLine($"Duration: {(result.CompletedAt - result.StartedAt).TotalSeconds:F1} seconds");
                message.AppendLine();
                message.AppendLine($"Stations: {result.StationsCount}");
                message.AppendLine($"Levels: {result.LevelsCount}");
                message.AppendLine($"Item Requirements: {result.ItemRequirementsCount}");
                message.AppendLine($"Station Requirements: {result.StationRequirementsCount}");
                message.AppendLine($"Trader Requirements: {result.TraderRequirementsCount}");
                message.AppendLine($"Skill Requirements: {result.SkillRequirementsCount}");
                message.AppendLine();
                message.AppendLine($"Icons: {result.IconsDownloaded} downloaded, {result.IconsFailed} failed, {result.IconsCached} cached");
                message.AppendLine();
                message.AppendLine($"Log saved to: {result.LogPath}");

                ViewModel.StatusMessage = $"Hideout refresh complete: {result.StationsCount} stations, {result.LevelsCount} levels";

                MessageBox.Show(
                    message.ToString(),
                    "Refresh Hideout Data Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                ViewModel.StatusMessage = $"Hideout refresh failed: {result.ErrorMessage}";
                MessageBox.Show(
                    $"Hideout refresh failed:\n{result.ErrorMessage}\n\nLog saved to: {result.LogPath}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Hideout refresh failed: {ex.Message}";
            MessageBox.Show(
                $"Hideout refresh failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void ExportWikiQuests_Click(object sender, RoutedEventArgs e)
    {
        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wiki_data");
        var questsPath = Path.Combine(outputDir, "wiki_quests.json");

        ViewModel.StatusMessage = "Exporting Wiki quests...";
        IsEnabled = false;

        try
        {
            using var questService = new WikiQuestService(outputDir);

            // 캐시 로드
            ViewModel.StatusMessage = "Loading quest cache...";
            await questService.LoadCacheAsync();
            var (cachedCount, withContentCount, withRevisionCount) = questService.GetCacheStats();
            ViewModel.StatusMessage = $"Cache loaded: {cachedCount} quests ({withContentCount} with content, {withRevisionCount} with revision)";

            // 퀘스트 목록 가져오기 (이벤트 퀘스트 제외)
            var questPages = await questService.GetAllQuestPagesAsync(
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            // 퀘스트 캐시 업데이트 (리비전 체크)
            ViewModel.StatusMessage = "Updating quest cache (checking revisions)...";
            var cacheUpdateResult = await questService.UpdateQuestCacheAsync(
                questPages,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            // 캐시 저장
            ViewModel.StatusMessage = "Saving quest cache...";
            await questService.SaveCacheAsync();

            // JSON 내보내기 (tarkov.dev 매칭 포함)
            ViewModel.StatusMessage = "Exporting quests with tarkov.dev data...";
            var exportResult = await questService.ExportQuestsAsync(
                questsPath,
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            ViewModel.StatusMessage = $"Quest export complete: {exportResult.TotalCount} quests ({exportResult.MatchedCount} matched)";

            MessageBox.Show(
                $"Quest export completed successfully!\n\n" +
                $"Quest Cache (revision-based):\n" +
                $"- New quests: {cacheUpdateResult.NewQuests}\n" +
                $"- Updated: {cacheUpdateResult.Updated}\n" +
                $"- Up-to-date (skipped): {cacheUpdateResult.UpToDate}\n" +
                $"- Failed: {cacheUpdateResult.Failed}\n\n" +
                $"tarkov.dev Enrichment:\n" +
                $"- Total quests: {exportResult.TotalCount}\n" +
                $"- Matched: {exportResult.MatchedCount}\n" +
                $"- Missing (wiki only): {exportResult.MissingCount}\n\n" +
                $"Saved to:\n" +
                $"- {questsPath}\n" +
                $"- wiki_data/quest_missing.json\n" +
                $"- wiki_data/cache/quest_cache.json",
                "Quest Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Quest export failed: {ex.Message}";
            MessageBox.Show(
                $"Quest export failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void OpenQuestRequirementsValidator_Click(object sender, RoutedEventArgs e)
    {
        // 데이터베이스가 연결되어 있는지 확인
        if (!DatabaseService.Instance.IsConnected)
        {
            MessageBox.Show(
                "Please open or create a database first.\n\nRun 'Debug > Refresh Data...' to initialize the database with quest data.",
                "No Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var window = new QuestRequirementsView { Owner = this };
        window.Show();
    }

    private void TestMap_Click(object sender, RoutedEventArgs e)
    {
        var window = new MapEditorWindow { Owner = this };
        window.Show();
    }

    private void OpenMapPreview_Click(object sender, RoutedEventArgs e)
    {
        var window = new MapPreviewWindow { Owner = this };
        window.Show();
    }

    private void OpenMapTransfer_Click(object sender, RoutedEventArgs e)
    {
        var window = new MapTransferWindow { Owner = this };
        window.Show();
    }

    private void FloorSetup_Click(object sender, RoutedEventArgs e)
    {
        // 데이터베이스가 연결되어 있는지 확인
        if (!DatabaseService.Instance.IsConnected)
        {
            MessageBox.Show(
                "Please open or create a database first.",
                "No Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var window = new FloorSetupWindow { Owner = this };
        window.Show();
    }

    private void ScreenshotWatcherSettings_Click(object sender, RoutedEventArgs e)
    {
        // 데이터베이스가 연결되어 있는지 확인
        if (!DatabaseService.Instance.IsConnected)
        {
            MessageBox.Show(
                "Please open or create a database first.",
                "No Database",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var window = new ScreenshotWatcherSettingsDialog { Owner = this };
        window.ShowDialog();
    }

    private async void CacheTarkovDevData_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.StatusMessage = "Checking tarkov.dev cache status...";

        using var devService = new TarkovDevDataService();
        var cacheInfo = devService.GetCacheInfo();

        // 캐시 상태 표시
        var cacheStatus = new System.Text.StringBuilder();
        cacheStatus.AppendLine("Current tarkov.dev cache status:");
        cacheStatus.AppendLine();

        if (cacheInfo.HasItemsCache)
            cacheStatus.AppendLine($"Items: {cacheInfo.ItemsCount} items (cached at {cacheInfo.ItemsCachedAt:yyyy-MM-dd HH:mm})");
        else
            cacheStatus.AppendLine("Items: No cache");

        if (cacheInfo.HasQuestsCache)
            cacheStatus.AppendLine($"Quests: {cacheInfo.QuestsCount} quests (cached at {cacheInfo.QuestsCachedAt:yyyy-MM-dd HH:mm})");
        else
            cacheStatus.AppendLine("Quests: No cache");

        cacheStatus.AppendLine();
        cacheStatus.AppendLine("Do you want to download and cache data from tarkov.dev?");
        cacheStatus.AppendLine("(This will replace existing cache)");

        var result = MessageBox.Show(
            cacheStatus.ToString(),
            "Cache Tarkov Dev Data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            ViewModel.StatusMessage = "Cache operation cancelled";
            return;
        }

        ViewModel.StatusMessage = "Downloading data from tarkov.dev...";
        IsEnabled = false;

        try
        {
            var cacheResult = await devService.CacheAllDataAsync(
                progress => Dispatcher.Invoke(() => ViewModel.StatusMessage = progress));

            var message = new System.Text.StringBuilder();
            message.AppendLine("Tarkov Dev data cache completed!");
            message.AppendLine();

            if (cacheResult.ItemsSuccess)
                message.AppendLine($"✓ Items: {cacheResult.ItemsCount} items cached");
            else
                message.AppendLine($"✗ Items failed: {cacheResult.ItemsError}");

            if (cacheResult.QuestsSuccess)
                message.AppendLine($"✓ Quests: {cacheResult.QuestsCount} quests cached");
            else
                message.AppendLine($"✗ Quests failed: {cacheResult.QuestsError}");

            message.AppendLine();
            message.AppendLine($"Cached at: {cacheResult.CachedAt:yyyy-MM-dd HH:mm:ss}");
            message.AppendLine();
            message.AppendLine("Cache files saved to:");
            message.AppendLine("- wiki_data/cache/tarkov_dev_items.json");
            message.AppendLine("- wiki_data/cache/tarkov_dev_quests.json");

            ViewModel.StatusMessage = $"Cache complete: {cacheResult.ItemsCount} items, {cacheResult.QuestsCount} quests";

            MessageBox.Show(
                message.ToString(),
                "Cache Complete",
                MessageBoxButton.OK,
                cacheResult.ItemsSuccess && cacheResult.QuestsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Cache failed: {ex.Message}";
            MessageBox.Show(
                $"Cache failed:\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }
}
