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
    private QuestItem? _selectedQuest;
    private string _searchText = "";
    private QuestFilterMode _filterMode = QuestFilterMode.All;

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

    public QuestItem? SelectedQuest
    {
        get => _selectedQuest;
        set
        {
            _selectedQuest = value;
            OnPropertyChanged();
            LoadQuestRequirements();
            LoadQuestObjectives();
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

    public bool HasNoRequirements => SelectedQuest != null && SelectedQuestRequirements.Count == 0;

    public bool HasNoObjectives => SelectedQuest != null && SelectedQuestObjectives.Count == 0;

    public async Task LoadDataAsync()
    {
        if (!_db.IsConnected) return;

        _allQuests.Clear();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Load all quests with MinLevel and MinScavKarma
        var questSql = @"SELECT Id, Name, NameEN, NameKO, NameJA, WikiPageLink, Trader, BsgId,
                         MinLevel, MinLevelApproved, MinLevelApprovedAt,
                         MinScavKarma, MinScavKarmaApproved, MinScavKarmaApprovedAt
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
                MinScavKarmaApprovedAt = questReader.IsDBNull(13) ? null : DateTime.Parse(questReader.GetString(13))
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
            _allQuests.Add(quest);
        }

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
                Id = reader.GetInt64(0),
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
                Id = reader.GetInt64(0),
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
                ApprovedAt = reader.IsDBNull(15) ? null : DateTime.Parse(reader.GetString(15))
            };
            SelectedQuestObjectives.Add(item);
        }

        OnPropertyChanged(nameof(HasNoObjectives));
    }

    public async Task UpdateObjectiveApprovalAsync(long objectiveId, bool isApproved)
    {
        if (!_db.IsConnected) return;

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
        await cmd.ExecuteNonQueryAsync();

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

    public async Task UpdateObjectiveLocationPointsAsync(long objectiveId, string? locationPointsJson)
    {
        if (!_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"UPDATE QuestObjectives
                    SET LocationPoints = @LocationPoints
                    WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", objectiveId);
        cmd.Parameters.AddWithValue("@LocationPoints", (object?)locationPointsJson ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateApprovalAsync(long requirementId, bool isApproved)
    {
        if (!_db.IsConnected) return;

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
        await cmd.ExecuteNonQueryAsync();

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
    public string? BsgId { get; set; }

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
            var total = _totalRequirements + _totalObjectives;
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
            var count = _approvedCount + _objectiveApprovedCount;
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

    public long Id { get; set; }
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
