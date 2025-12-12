using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using TarkovDBEditor.Models;
using TarkovDBEditor.Services;

namespace TarkovDBEditor.ViewModels;

public enum QuestFilterMode
{
    All,
    PendingApproval,
    Approved,
    HasRequirements,
    NoRequirements
}

public enum QuestApprovalStatus
{
    NoRequirements,
    NoneApproved,
    PartialApproved,
    AllApproved
}

public class QuestRequirementsViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _db = DatabaseService.Instance;

    private ObservableCollection<QuestItem> _allQuests = new();
    private ObservableCollection<QuestItem> _filteredQuests = new();
    private ObservableCollection<QuestRequirementItem> _selectedQuestRequirements = new();
    private ObservableCollection<QuestObjectiveItem> _selectedQuestObjectives = new();
    private ObservableCollection<OptionalQuestItem> _selectedOptionalQuests = new();
    private ObservableCollection<QuestRequiredItemViewModel> _selectedRequiredItems = new();
    private QuestItem? _selectedQuest;
    private string _searchText = "";
    private QuestFilterMode _filterMode = QuestFilterMode.All;
    private string? _selectedMapFilter;
    private ObservableCollection<string> _availableMaps = new();

    // Quest별 Objective 맵 정보 캐시 (QuestId -> MapName 목록)
    private Dictionary<string, HashSet<string>> _questObjectiveMaps = new();

    public ObservableCollection<QuestItem> FilteredQuests
    {
        get => _filteredQuests;
        set { _filteredQuests = value; OnPropertyChanged(); }
    }
    
    public ObservableCollection<QuestRequirementItem> SelectedQuestRequirements
    {
        get => _selectedQuestRequirements;
        set { _selectedQuestRequirements = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoRequirements)); }
    }

    public ObservableCollection<QuestObjectiveItem> SelectedQuestObjectives
    {
        get => _selectedQuestObjectives;
        set { _selectedQuestObjectives = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoObjectives)); }
    }

    public ObservableCollection<OptionalQuestItem> SelectedOptionalQuests
    {
        get => _selectedOptionalQuests;
        set { _selectedOptionalQuests = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoOptionalQuests)); }
    }

    public ObservableCollection<QuestRequiredItemViewModel> SelectedRequiredItems
    {
        get => _selectedRequiredItems;
        set { _selectedRequiredItems = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasNoRequiredItems)); }
    }

    public QuestItem? SelectedQuest
    {
        get => _selectedQuest;
        set
        {
            _selectedQuest = value;
            OnPropertyChanged();
            LoadQuestRequirements();
            LoadQuestObjectives();
            LoadOptionalQuests();
            LoadRequiredItems();
        }
    }


    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public QuestFilterMode FilterMode
    {
        get => _filterMode;
        set
        {
            _filterMode = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public ObservableCollection<string> AvailableMaps
    {
        get => _availableMaps;
        set { _availableMaps = value; OnPropertyChanged(); }
    }

    public string? SelectedMapFilter
    {
        get => _selectedMapFilter;
        set
        {
            _selectedMapFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public bool HasNoRequirements => SelectedQuest != null && SelectedQuestRequirements.Count == 0;

    public bool HasNoObjectives => SelectedQuest != null && SelectedQuestObjectives.Count == 0;

    public bool HasNoOptionalQuests => SelectedQuest != null && SelectedOptionalQuests.Count == 0;

    public bool HasNoRequiredItems => SelectedQuest != null && SelectedRequiredItems.Count == 0;

    public async Task LoadDataAsync()
    {
        if (!_db.IsConnected) return;

        _allQuests.Clear();
        _questObjectiveMaps.Clear();
        var mapSet = new HashSet<string>();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Load all quests with MinLevel, MinScavKarma, and Location
        var questSql = @"SELECT Id, Name, NameEN, NameKO, NameJA, WikiPageLink, Trader, BsgId,
                         MinLevel, MinLevelApproved, MinLevelApprovedAt,
                         MinScavKarma, MinScavKarmaApproved, MinScavKarmaApprovedAt,
                         Location
                         FROM Quests ORDER BY Name";
        await using var questCmd = new SqliteCommand(questSql, connection);
        await using var questReader = await questCmd.ExecuteReaderAsync();

        var quests = new List<QuestItem>();
        while (await questReader.ReadAsync())
        {
            quests.Add(new QuestItem
            {
                Id = questReader.GetString(0),
                Name = questReader.GetString(1),
                NameEN = questReader.IsDBNull(2) ? null : questReader.GetString(2),
                NameKO = questReader.IsDBNull(3) ? null : questReader.GetString(3),
                NameJA = questReader.IsDBNull(4) ? null : questReader.GetString(4),
                WikiPageLink = questReader.IsDBNull(5) ? null : questReader.GetString(5),
                Trader = questReader.IsDBNull(6) ? null : questReader.GetString(6),
                BsgId = questReader.IsDBNull(7) ? null : questReader.GetString(7),
                MinLevel = questReader.IsDBNull(8) ? null : questReader.GetInt32(8),
                MinLevelApproved = !questReader.IsDBNull(9) && questReader.GetInt64(9) != 0,
                MinLevelApprovedAt = questReader.IsDBNull(10) ? null : DateTime.Parse(questReader.GetString(10)),
                MinScavKarma = questReader.IsDBNull(11) ? null : questReader.GetInt32(11),
                MinScavKarmaApproved = !questReader.IsDBNull(12) && questReader.GetInt64(12) != 0,
                MinScavKarmaApprovedAt = questReader.IsDBNull(13) ? null : DateTime.Parse(questReader.GetString(13)),
                Location = questReader.IsDBNull(14) ? null : questReader.GetString(14)
            });
        }

        // Load requirement counts per quest
        var countSql = @"
            SELECT QuestId, COUNT(*) as Total, SUM(CASE WHEN IsApproved = 1 THEN 1 ELSE 0 END) as Approved
            FROM QuestRequirements
            GROUP BY QuestId";
        await using var countCmd = new SqliteCommand(countSql, connection);
        await using var countReader = await countCmd.ExecuteReaderAsync();

        var reqCounts = new Dictionary<string, (int Total, int Approved)>();
        while (await countReader.ReadAsync())
        {
            var questId = countReader.GetString(0);
            var total = countReader.GetInt32(1);
            var approved = countReader.GetInt32(2);
            reqCounts[questId] = (total, approved);
        }

        // Load objective counts per quest
        var objCountSql = @"
            SELECT QuestId, COUNT(*) as Total, SUM(CASE WHEN IsApproved = 1 THEN 1 ELSE 0 END) as Approved
            FROM QuestObjectives
            GROUP BY QuestId";
        await using var objCountCmd = new SqliteCommand(objCountSql, connection);
        await using var objCountReader = await objCountCmd.ExecuteReaderAsync();

        var objCounts = new Dictionary<string, (int Total, int Approved)>();
        while (await objCountReader.ReadAsync())
        {
            var questId = objCountReader.GetString(0);
            var total = objCountReader.GetInt32(1);
            var approved = objCountReader.GetInt32(2);
            objCounts[questId] = (total, approved);
        }

        // Load optional quest counts per quest (테이블이 존재하는 경우에만)
        var optCounts = new Dictionary<string, (int Total, int Approved)>();
        try
        {
            var optCountSql = @"
                SELECT QuestId, COUNT(*) as Total, SUM(CASE WHEN IsApproved = 1 THEN 1 ELSE 0 END) as Approved
                FROM OptionalQuests
                GROUP BY QuestId";
            await using var optCountCmd = new SqliteCommand(optCountSql, connection);
            await using var optCountReader = await optCountCmd.ExecuteReaderAsync();

            while (await optCountReader.ReadAsync())
            {
                var questId = optCountReader.GetString(0);
                var total = optCountReader.GetInt32(1);
                var approved = optCountReader.GetInt32(2);
                optCounts[questId] = (total, approved);
            }
        }
        catch (SqliteException)
        {
            // OptionalQuests 테이블이 없으면 무시
        }

        // Load required items counts per quest (테이블이 존재하는 경우에만)
        var reqItemCounts = new Dictionary<string, (int Total, int Approved)>();
        try
        {
            var reqItemCountSql = @"
                SELECT QuestId, COUNT(*) as Total, SUM(CASE WHEN IsApproved = 1 THEN 1 ELSE 0 END) as Approved
                FROM QuestRequiredItems
                GROUP BY QuestId";
            await using var reqItemCountCmd = new SqliteCommand(reqItemCountSql, connection);
            await using var reqItemCountReader = await reqItemCountCmd.ExecuteReaderAsync();

            while (await reqItemCountReader.ReadAsync())
            {
                var questId = reqItemCountReader.GetString(0);
                var total = reqItemCountReader.GetInt32(1);
                var approved = reqItemCountReader.GetInt32(2);
                reqItemCounts[questId] = (total, approved);
            }
        }
        catch (SqliteException)
        {
            // QuestRequiredItems 테이블이 없으면 무시
        }

        // Load objectives' map names per quest for filtering
        try
        {
            var objMapSql = @"
                SELECT QuestId, MapName
                FROM QuestObjectives
                WHERE MapName IS NOT NULL AND MapName != ''";
            await using var objMapCmd = new SqliteCommand(objMapSql, connection);
            await using var objMapReader = await objMapCmd.ExecuteReaderAsync();

            while (await objMapReader.ReadAsync())
            {
                var questId = objMapReader.GetString(0);
                var mapName = objMapReader.GetString(1);

                if (!_questObjectiveMaps.ContainsKey(questId))
                    _questObjectiveMaps[questId] = new HashSet<string>();

                _questObjectiveMaps[questId].Add(mapName);
                mapSet.Add(mapName);
            }
        }
        catch (SqliteException)
        {
            // QuestObjectives 테이블이 없으면 무시
        }

        // Update quest items with counts
        foreach (var quest in quests)
        {
            if (reqCounts.TryGetValue(quest.Id, out var counts))
            {
                quest.QuestReqTotalCount = counts.Total;
                quest.QuestReqApprovedCount = counts.Approved;
            }
            else
            {
                quest.QuestReqTotalCount = 0;
                quest.QuestReqApprovedCount = 0;
            }

            if (objCounts.TryGetValue(quest.Id, out var objCount))
            {
                quest.QuestObjTotalCount = objCount.Total;
                quest.QuestObjApprovedCount = objCount.Approved;
            }
            else
            {
                quest.QuestObjTotalCount = 0;
                quest.QuestObjApprovedCount = 0;
            }

            if (optCounts.TryGetValue(quest.Id, out var optCount))
            {
                quest.OptionalQuestTotalCount = optCount.Total;
                quest.OptionalQuestApprovedCount = optCount.Approved;
            }
            else
            {
                quest.OptionalQuestTotalCount = 0;
                quest.OptionalQuestApprovedCount = 0;
            }

            if (reqItemCounts.TryGetValue(quest.Id, out var reqItemCount))
            {
                quest.RequiredItemTotalCount = reqItemCount.Total;
                quest.RequiredItemApprovedCount = reqItemCount.Approved;
            }
            else
            {
                quest.RequiredItemTotalCount = 0;
                quest.RequiredItemApprovedCount = 0;
            }

            // Add quest's Location to map set
            if (!string.IsNullOrEmpty(quest.Location))
            {
                mapSet.Add(quest.Location);
            }

            _allQuests.Add(quest);
        }

        // Update available maps (sorted)
        AvailableMaps = new ObservableCollection<string>(mapSet.OrderBy(m => m));

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = _allQuests.AsEnumerable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var searchLower = _searchText.ToLowerInvariant();
            filtered = filtered.Where(q =>
                q.Name.ToLowerInvariant().Contains(searchLower) ||
                (q.NameEN?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                (q.NameKO?.ToLowerInvariant().Contains(searchLower) ?? false) ||
                (q.Trader?.ToLowerInvariant().Contains(searchLower) ?? false));
        }

        // Apply mode filter
        filtered = _filterMode switch
        {
            QuestFilterMode.PendingApproval => filtered.Where(q => q.TotalRequirements > 0 && q.ApprovedCount < q.TotalRequirements),
            QuestFilterMode.Approved => filtered.Where(q => q.TotalRequirements > 0 && q.ApprovedCount == q.TotalRequirements),
            QuestFilterMode.HasRequirements => filtered.Where(q => q.TotalRequirements > 0),
            QuestFilterMode.NoRequirements => filtered.Where(q => q.TotalRequirements == 0),
            _ => filtered
        };

        // Apply map filter (Quest.Location OR Objectives.MapName)
        if (!string.IsNullOrEmpty(_selectedMapFilter))
        {
            filtered = filtered.Where(q =>
                // Quest's Location matches
                (q.Location != null && q.Location.Equals(_selectedMapFilter, StringComparison.OrdinalIgnoreCase)) ||
                // Or any Objective's MapName matches
                (_questObjectiveMaps.TryGetValue(q.Id, out var objMaps) &&
                 objMaps.Contains(_selectedMapFilter, StringComparer.OrdinalIgnoreCase)));
        }

        FilteredQuests = new ObservableCollection<QuestItem>(filtered);
        OnPropertyChanged(nameof(FilteredQuests));
    }

    private async void LoadQuestRequirements()
    {
        SelectedQuestRequirements.Clear();

        if (_selectedQuest == null || !_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT r.Id, r.RequiredQuestId, r.RequirementType, r.DelayMinutes, r.GroupId, r.IsApproved, r.ApprovedAt,
                   q.Name, q.WikiPageLink
            FROM QuestRequirements r
            LEFT JOIN Quests q ON r.RequiredQuestId = q.Id
            WHERE r.QuestId = @QuestId
            ORDER BY r.GroupId, r.RequirementType";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@QuestId", _selectedQuest.Id);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = new QuestRequirementItem
            {
                Id = reader.GetString(0),
                RequiredQuestId = reader.GetString(1),
                RequirementType = reader.GetString(2),
                DelayMinutes = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                GroupId = reader.GetInt32(4),
                IsApproved = !reader.IsDBNull(5) && reader.GetInt64(5) != 0,
                ApprovedAt = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                RequiredQuestName = reader.IsDBNull(7) ? "(Unknown)" : reader.GetString(7),
                RequiredQuestWikiLink = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
            SelectedQuestRequirements.Add(item);
        }

        OnPropertyChanged(nameof(HasNoRequirements));
    }

    private async void LoadQuestObjectives()
    {
        SelectedQuestObjectives.Clear();

        if (_selectedQuest == null || !_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT o.Id, o.QuestId, o.SortOrder, o.ObjectiveType, o.Description,
                   o.TargetType, o.TargetCount, o.ItemId, o.ItemName, o.RequiresFIR,
                   o.MapName, o.LocationName, o.LocationPoints,
                   o.Conditions, o.IsApproved, o.ApprovedAt
            FROM QuestObjectives o
            WHERE o.QuestId = @QuestId
            ORDER BY o.SortOrder";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@QuestId", _selectedQuest.Id);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var item = new QuestObjectiveItem
            {
                Id = reader.GetString(0),
                QuestId = reader.GetString(1),
                SortOrder = reader.GetInt32(2),
                ObjectiveType = reader.GetString(3),
                Description = reader.GetString(4),
                TargetType = reader.IsDBNull(5) ? null : reader.GetString(5),
                TargetCount = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ItemId = reader.IsDBNull(7) ? null : reader.GetString(7),
                ItemName = reader.IsDBNull(8) ? null : reader.GetString(8),
                RequiresFIR = !reader.IsDBNull(9) && reader.GetInt64(9) != 0,
                MapName = reader.IsDBNull(10) ? null : reader.GetString(10),
                LocationName = reader.IsDBNull(11) ? null : reader.GetString(11),
                LocationPointsJson = reader.IsDBNull(12) ? null : reader.GetString(12),
                Conditions = reader.IsDBNull(13) ? null : reader.GetString(13),
                IsApproved = !reader.IsDBNull(14) && reader.GetInt64(14) != 0,
                ApprovedAt = reader.IsDBNull(15) ? null : DateTime.Parse(reader.GetString(15)),
                // Quest의 Location을 fallback으로 설정 (Quest Item의 경우 MapName이 없을 때 사용)
                QuestLocation = _selectedQuest?.Location
            };
            SelectedQuestObjectives.Add(item);
        }

        OnPropertyChanged(nameof(HasNoObjectives));
    }

    private async void LoadOptionalQuests()
    {
        SelectedOptionalQuests.Clear();

        if (_selectedQuest == null || !_db.IsConnected) return;

        try
        {
            var connectionString = $"Data Source={_db.DatabasePath}";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT o.Id, o.QuestId, o.AlternativeQuestId, o.IsApproved, o.ApprovedAt,
                       q.Name, q.WikiPageLink, q.Trader
                FROM OptionalQuests o
                LEFT JOIN Quests q ON o.AlternativeQuestId = q.Id
                WHERE o.QuestId = @QuestId
                ORDER BY q.Name";

            await using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@QuestId", _selectedQuest.Id);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new OptionalQuestItem
                {
                    Id = reader.GetString(0),
                    QuestId = reader.GetString(1),
                    AlternativeQuestId = reader.GetString(2),
                    IsApproved = !reader.IsDBNull(3) && reader.GetInt64(3) != 0,
                    ApprovedAt = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                    AlternativeQuestName = reader.IsDBNull(5) ? "(Unknown)" : reader.GetString(5),
                    AlternativeQuestWikiLink = reader.IsDBNull(6) ? null : reader.GetString(6),
                    AlternativeQuestTrader = reader.IsDBNull(7) ? null : reader.GetString(7)
                };
                SelectedOptionalQuests.Add(item);
            }
        }
        catch (SqliteException)
        {
            // OptionalQuests 테이블이 없으면 무시
        }

        OnPropertyChanged(nameof(HasNoOptionalQuests));
    }

    private async void LoadRequiredItems()
    {
        SelectedRequiredItems.Clear();

        if (_selectedQuest == null || !_db.IsConnected) return;

        try
        {
            var connectionString = $"Data Source={_db.DatabasePath}";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var sql = @"
                SELECT r.Id, r.QuestId, r.ItemId, r.ItemName, r.Count, r.RequiresFIR,
                       r.RequirementType, r.SortOrder, r.DogtagMinLevel, r.IsApproved, r.ApprovedAt
                FROM QuestRequiredItems r
                WHERE r.QuestId = @QuestId
                ORDER BY r.SortOrder";

            await using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@QuestId", _selectedQuest.Id);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var item = new QuestRequiredItemViewModel
                {
                    Id = reader.GetString(0),
                    QuestId = reader.GetString(1),
                    ItemId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ItemName = reader.GetString(3),
                    Count = reader.GetInt32(4),
                    RequiresFIR = !reader.IsDBNull(5) && reader.GetInt64(5) != 0,
                    RequirementType = reader.GetString(6),
                    SortOrder = reader.GetInt32(7),
                    DogtagMinLevel = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                    IsApproved = !reader.IsDBNull(9) && reader.GetInt64(9) != 0,
                    ApprovedAt = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10))
                };
                SelectedRequiredItems.Add(item);
            }
        }
        catch (SqliteException)
        {
            // QuestRequiredItems 테이블이 없으면 무시
        }

        OnPropertyChanged(nameof(HasNoRequiredItems));
    }

    public async Task UpdateRequiredItemApprovalAsync(string requiredItemId, bool isApproved)
    {
        if (!_db.IsConnected)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateRequiredItemApprovalAsync] DB not connected!");
            return;
        }

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = isApproved
            ? "UPDATE QuestRequiredItems SET IsApproved = 1, ApprovedAt = @ApprovedAt WHERE Id = @Id"
            : "UPDATE QuestRequiredItems SET IsApproved = 0, ApprovedAt = NULL WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", requiredItemId);
        if (isApproved)
        {
            cmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow.ToString("o"));
        }
        var rows = await cmd.ExecuteNonQueryAsync();
        System.Diagnostics.Debug.WriteLine($"[UpdateRequiredItemApprovalAsync] Updated {rows} row(s) for RequiredItemId={requiredItemId}, IsApproved={isApproved}");

        // Update local count
        if (_selectedQuest != null)
        {
            _selectedQuest.RequiredItemApprovedCount = SelectedRequiredItems.Count(r => r.IsApproved);

            // Update in allQuests
            var questInList = _allQuests.FirstOrDefault(q => q.Id == _selectedQuest.Id);
            if (questInList != null)
            {
                questInList.RequiredItemApprovedCount = _selectedQuest.RequiredItemApprovedCount;
            }
        }
    }

    public async Task UpdateOptionalQuestApprovalAsync(string optionalQuestId, bool isApproved)
    {
        if (!_db.IsConnected)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateOptionalQuestApprovalAsync] DB not connected!");
            return;
        }

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = isApproved
            ? "UPDATE OptionalQuests SET IsApproved = 1, ApprovedAt = @ApprovedAt WHERE Id = @Id"
            : "UPDATE OptionalQuests SET IsApproved = 0, ApprovedAt = NULL WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", optionalQuestId);
        if (isApproved)
        {
            cmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow.ToString("o"));
        }
        var rows = await cmd.ExecuteNonQueryAsync();
        System.Diagnostics.Debug.WriteLine($"[UpdateOptionalQuestApprovalAsync] Updated {rows} row(s) for OptionalQuestId={optionalQuestId}, IsApproved={isApproved}");

        // Update local count
        if (_selectedQuest != null)
        {
            _selectedQuest.OptionalQuestApprovedCount = SelectedOptionalQuests.Count(o => o.IsApproved);

            // Update in allQuests
            var questInList = _allQuests.FirstOrDefault(q => q.Id == _selectedQuest.Id);
            if (questInList != null)
            {
                questInList.OptionalQuestApprovedCount = _selectedQuest.OptionalQuestApprovedCount;
            }
        }
    }

    public async Task UpdateObjectiveApprovalAsync(string objectiveId, bool isApproved)
    {
        if (!_db.IsConnected)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateObjectiveApprovalAsync] DB not connected!");
            return;
        }

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = isApproved
            ? "UPDATE QuestObjectives SET IsApproved = 1, ApprovedAt = @ApprovedAt WHERE Id = @Id"
            : "UPDATE QuestObjectives SET IsApproved = 0, ApprovedAt = NULL WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", objectiveId);
        if (isApproved)
        {
            cmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow.ToString("o"));
        }
        var rows = await cmd.ExecuteNonQueryAsync();
        System.Diagnostics.Debug.WriteLine($"[UpdateObjectiveApprovalAsync] Updated {rows} row(s) for ObjectiveId={objectiveId}, IsApproved={isApproved}");

        // Update local count
        if (_selectedQuest != null)
        {
            _selectedQuest.QuestObjApprovedCount = SelectedQuestObjectives.Count(o => o.IsApproved);

            // Update in allQuests
            var questInList = _allQuests.FirstOrDefault(q => q.Id == _selectedQuest.Id);
            if (questInList != null)
            {
                questInList.QuestObjApprovedCount = _selectedQuest.QuestObjApprovedCount;
            }
        }
    }

    public async Task UpdateObjectiveLocationPointsAsync(string objectiveId, string? locationPointsJson)
    {
        if (!_db.IsConnected)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateObjectiveLocationPointsAsync] DB not connected!");
            return;
        }

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"UPDATE QuestObjectives
                    SET LocationPoints = @LocationPoints
                    WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", objectiveId);
        cmd.Parameters.AddWithValue("@LocationPoints", (object?)locationPointsJson ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync();
        System.Diagnostics.Debug.WriteLine($"[UpdateObjectiveLocationPointsAsync] Updated {rows} row(s) for ObjectiveId={objectiveId}, LocationPointsJson={(locationPointsJson ?? "null")}");
    }

    public async Task UpdateApprovalAsync(string requirementId, bool isApproved)
    {
        if (!_db.IsConnected)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateApprovalAsync] DB not connected!");
            return;
        }

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = isApproved
            ? "UPDATE QuestRequirements SET IsApproved = 1, ApprovedAt = @ApprovedAt WHERE Id = @Id"
            : "UPDATE QuestRequirements SET IsApproved = 0, ApprovedAt = NULL WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", requirementId);
        if (isApproved)
        {
            cmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow.ToString("o"));
        }
        var rows = await cmd.ExecuteNonQueryAsync();
        System.Diagnostics.Debug.WriteLine($"[UpdateApprovalAsync] Updated {rows} row(s) for RequirementId={requirementId}, IsApproved={isApproved}");

        // Update local count
        if (_selectedQuest != null)
        {
            _selectedQuest.QuestReqApprovedCount = SelectedQuestRequirements.Count(r => r.IsApproved);

            // Update in allQuests
            var questInList = _allQuests.FirstOrDefault(q => q.Id == _selectedQuest.Id);
            if (questInList != null)
            {
                questInList.QuestReqApprovedCount = _selectedQuest.QuestReqApprovedCount;
            }
        }
    }

    public async Task UpdateMinLevelApprovalAsync(string questId, bool isApproved)
    {
        if (!_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = isApproved
            ? "UPDATE Quests SET MinLevelApproved = 1, MinLevelApprovedAt = @ApprovedAt WHERE Id = @Id"
            : "UPDATE Quests SET MinLevelApproved = 0, MinLevelApprovedAt = NULL WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", questId);
        if (isApproved)
        {
            cmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow.ToString("o"));
        }
        await cmd.ExecuteNonQueryAsync();

        // Update local quest
        if (_selectedQuest != null && _selectedQuest.Id == questId)
        {
            _selectedQuest.MinLevelApproved = isApproved;
            _selectedQuest.MinLevelApprovedAt = isApproved ? DateTime.UtcNow : null;
        }

        var questInList = _allQuests.FirstOrDefault(q => q.Id == questId);
        if (questInList != null)
        {
            questInList.MinLevelApproved = isApproved;
            questInList.MinLevelApprovedAt = isApproved ? DateTime.UtcNow : null;
        }
    }

    public async Task UpdateMinScavKarmaApprovalAsync(string questId, bool isApproved)
    {
        if (!_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = isApproved
            ? "UPDATE Quests SET MinScavKarmaApproved = 1, MinScavKarmaApprovedAt = @ApprovedAt WHERE Id = @Id"
            : "UPDATE Quests SET MinScavKarmaApproved = 0, MinScavKarmaApprovedAt = NULL WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", questId);
        if (isApproved)
        {
            cmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow.ToString("o"));
        }
        await cmd.ExecuteNonQueryAsync();

        // Update local quest
        if (_selectedQuest != null && _selectedQuest.Id == questId)
        {
            _selectedQuest.MinScavKarmaApproved = isApproved;
            _selectedQuest.MinScavKarmaApprovedAt = isApproved ? DateTime.UtcNow : null;
        }

        var questInList = _allQuests.FirstOrDefault(q => q.Id == questId);
        if (questInList != null)
        {
            questInList.MinScavKarmaApproved = isApproved;
            questInList.MinScavKarmaApprovedAt = isApproved ? DateTime.UtcNow : null;
        }
    }

    public (int Approved, int Total) GetApprovalProgress()
    {
        var total = _allQuests.Sum(q => q.TotalRequirements);
        var approved = _allQuests.Sum(q => q.ApprovedCount);
        return (approved, total);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class QuestItem : INotifyPropertyChanged
{
    private int _approvedCount;
    private int _totalRequirements;
    private int _objectiveApprovedCount;
    private int _totalObjectives;
    private int _optionalQuestApprovedCount;
    private int _totalOptionalQuests;
    private int _requiredItemApprovedCount;
    private int _totalRequiredItems;
    private int? _minLevel;
    private bool _minLevelApproved;
    private int? _minScavKarma;
    private bool _minScavKarmaApproved;

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? NameEN { get; set; }
    public string? NameKO { get; set; }
    public string? NameJA { get; set; }
    public string? WikiPageLink { get; set; }
    public string? Trader { get; set; }
    public string? Location { get; set; }
    public string? BsgId { get; set; }

    public string TraderAndLocation
    {
        get
        {
            if (string.IsNullOrEmpty(Trader) && string.IsNullOrEmpty(Location))
                return "";
            if (string.IsNullOrEmpty(Location))
                return Trader ?? "";
            if (string.IsNullOrEmpty(Trader))
                return $"[{Location}]";
            return $"{Trader} [{Location}]";
        }
    }

    public int? MinLevel
    {
        get => _minLevel;
        set { _minLevel = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMinLevel)); OnPropertyChanged(nameof(ApprovalStatus)); OnPropertyChanged(nameof(TotalRequirements)); }
    }

    public bool MinLevelApproved
    {
        get => _minLevelApproved;
        set { _minLevelApproved = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApprovalStatus)); OnPropertyChanged(nameof(ApprovedCount)); }
    }

    public DateTime? MinLevelApprovedAt { get; set; }

    public int? MinScavKarma
    {
        get => _minScavKarma;
        set { _minScavKarma = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMinScavKarma)); OnPropertyChanged(nameof(ApprovalStatus)); OnPropertyChanged(nameof(TotalRequirements)); }
    }

    public bool MinScavKarmaApproved
    {
        get => _minScavKarmaApproved;
        set { _minScavKarmaApproved = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApprovalStatus)); OnPropertyChanged(nameof(ApprovedCount)); }
    }

    public DateTime? MinScavKarmaApprovedAt { get; set; }

    public bool HasMinLevel => MinLevel.HasValue && MinLevel.Value > 0;
    public bool HasMinScavKarma => MinScavKarma.HasValue;

    public int TotalRequirements
    {
        get
        {
            var total = _totalRequirements + _totalObjectives + _totalOptionalQuests + _totalRequiredItems;
            if (HasMinLevel) total++;
            if (HasMinScavKarma) total++;
            return total;
        }
        set { _totalRequirements = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    public int ApprovedCount
    {
        get
        {
            var count = _approvedCount + _objectiveApprovedCount + _optionalQuestApprovedCount + _requiredItemApprovedCount;
            if (HasMinLevel && MinLevelApproved) count++;
            if (HasMinScavKarma && MinScavKarmaApproved) count++;
            return count;
        }
        set { _approvedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    // 퀘스트 요구사항 승인 수만 (MinLevel/MinScavKarma 제외)
    public int QuestReqApprovedCount
    {
        get => _approvedCount;
        set { _approvedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApprovedCount)); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    public int QuestReqTotalCount
    {
        get => _totalRequirements;
        set { _totalRequirements = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalRequirements)); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    // Objectives 승인 수
    public int QuestObjApprovedCount
    {
        get => _objectiveApprovedCount;
        set { _objectiveApprovedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApprovedCount)); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    public int QuestObjTotalCount
    {
        get => _totalObjectives;
        set { _totalObjectives = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalRequirements)); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    // Optional Quests (Other Choices) 승인 수
    public int OptionalQuestApprovedCount
    {
        get => _optionalQuestApprovedCount;
        set { _optionalQuestApprovedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApprovedCount)); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    public int OptionalQuestTotalCount
    {
        get => _totalOptionalQuests;
        set { _totalOptionalQuests = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalRequirements)); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    // Required Items 승인 수
    public int RequiredItemApprovedCount
    {
        get => _requiredItemApprovedCount;
        set { _requiredItemApprovedCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(ApprovedCount)); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    public int RequiredItemTotalCount
    {
        get => _totalRequiredItems;
        set { _totalRequiredItems = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalRequirements)); OnPropertyChanged(nameof(ApprovalStatus)); }
    }

    public QuestApprovalStatus ApprovalStatus
    {
        get
        {
            if (TotalRequirements == 0) return QuestApprovalStatus.NoRequirements;
            if (ApprovedCount == 0) return QuestApprovalStatus.NoneApproved;
            if (ApprovedCount == TotalRequirements) return QuestApprovalStatus.AllApproved;
            return QuestApprovalStatus.PartialApproved;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class QuestRequirementItem : INotifyPropertyChanged
{
    private bool _isApproved;

    public string Id { get; set; } = "";
    public string RequiredQuestId { get; set; } = "";
    public string RequiredQuestName { get; set; } = "";
    public string? RequiredQuestWikiLink { get; set; }
    public string RequirementType { get; set; } = "Complete";
    public int? DelayMinutes { get; set; }
    public int GroupId { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public bool IsApproved
    {
        get => _isApproved;
        set { _isApproved = value; OnPropertyChanged(); }
    }

    public bool HasDelay => DelayMinutes.HasValue && DelayMinutes.Value > 0;

    public string DelayDisplay
    {
        get
        {
            if (!DelayMinutes.HasValue) return "";
            var mins = DelayMinutes.Value;
            if (mins >= 60)
            {
                var hours = mins / 60;
                var remainder = mins % 60;
                return remainder > 0 ? $"{hours}h {remainder}m" : $"{hours}h";
            }
            return $"{mins}m";
        }
    }

    public string ApprovedAtDisplay
    {
        get
        {
            if (!ApprovedAt.HasValue) return "";
            return $"Approved: {ApprovedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class OptionalQuestItem : INotifyPropertyChanged
{
    private bool _isApproved;

    public string Id { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string AlternativeQuestId { get; set; } = "";
    public string AlternativeQuestName { get; set; } = "";
    public string? AlternativeQuestWikiLink { get; set; }
    public string? AlternativeQuestTrader { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public bool IsApproved
    {
        get => _isApproved;
        set { _isApproved = value; OnPropertyChanged(); }
    }

    public string ApprovedAtDisplay
    {
        get
        {
            if (!ApprovedAt.HasValue) return "";
            return $"Approved: {ApprovedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class QuestRequiredItemViewModel : INotifyPropertyChanged
{
    private bool _isApproved;

    public string Id { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string? ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int Count { get; set; } = 1;
    public bool RequiresFIR { get; set; }
    public string RequirementType { get; set; } = "Required";
    public int SortOrder { get; set; }
    public int? DogtagMinLevel { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public bool IsApproved
    {
        get => _isApproved;
        set { _isApproved = value; OnPropertyChanged(); }
    }

    public string CountDisplay => $"x{Count}";

    public string TypeDisplay => RequirementType switch
    {
        "Handover" => "Hand Over",
        "Required" => "Required",
        "Optional" => "Optional",
        _ => RequirementType
    };

    public string DogtagLevelDisplay => DogtagMinLevel.HasValue ? $"Lv.{DogtagMinLevel}+" : "";

    public bool HasItem => !string.IsNullOrEmpty(ItemId) || !string.IsNullOrEmpty(ItemName);

    public string ApprovedAtDisplay
    {
        get
        {
            if (!ApprovedAt.HasValue) return "";
            return $"Approved: {ApprovedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm}";
        }
    }

    // 아이템 아이콘 경로 (wiki_data/icons/{ItemId}.png)
    public string? ItemIconPath
    {
        get
        {
            if (string.IsNullOrEmpty(ItemId))
                return null;

            var basePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wiki_data", "icons");
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

            foreach (var ext in extensions)
            {
                var path = System.IO.Path.Combine(basePath, $"{ItemId}{ext}");
                if (System.IO.File.Exists(path))
                    return path;
            }

            return null;
        }
    }

    // 아이템 아이콘 이미지 (바인딩용)
    private System.Windows.Media.Imaging.BitmapImage? _itemIcon;
    public System.Windows.Media.Imaging.BitmapImage? ItemIcon
    {
        get
        {
            if (_itemIcon == null && !string.IsNullOrEmpty(ItemIconPath))
            {
                try
                {
                    _itemIcon = new System.Windows.Media.Imaging.BitmapImage();
                    _itemIcon.BeginInit();
                    _itemIcon.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    _itemIcon.UriSource = new Uri(ItemIconPath);
                    _itemIcon.DecodePixelWidth = 64;
                    _itemIcon.EndInit();
                    _itemIcon.Freeze();
                }
                catch
                {
                    _itemIcon = null;
                }
            }
            return _itemIcon;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
