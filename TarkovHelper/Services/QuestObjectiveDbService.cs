using System.IO;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// Service to load quest objectives with location data from tarkov_data.db
/// </summary>
public sealed class QuestObjectiveDbService
{
    private static QuestObjectiveDbService? _instance;
    public static QuestObjectiveDbService Instance => _instance ??= new QuestObjectiveDbService();

    private readonly string _databasePath;
    private readonly List<QuestObjective> _allObjectives = new();
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;

    private QuestObjectiveDbService()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _databasePath = Path.Combine(appDir, "Assets", "tarkov_data.db");
    }

    /// <summary>
    /// Get all loaded objectives
    /// </summary>
    public IReadOnlyList<QuestObjective> AllObjectives => _allObjectives;

    /// <summary>
    /// Get objectives for a specific map
    /// </summary>
    public List<QuestObjective> GetObjectivesForMap(string mapKey, MapConfig mapConfig)
    {
        return _allObjectives
            .Where(o => mapConfig.MatchesMapName(o.EffectiveMapName))
            .ToList();
    }

    /// <summary>
    /// Load all quest objectives with location data
    /// </summary>
    public async Task<bool> LoadObjectivesAsync()
    {
        if (!File.Exists(_databasePath))
        {
            System.Diagnostics.Debug.WriteLine($"[QuestObjectiveDbService] Database not found: {_databasePath}");
            return false;
        }

        try
        {
            var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // Check if QuestObjectives table exists
            if (!await TableExistsAsync(connection, "QuestObjectives"))
            {
                System.Diagnostics.Debug.WriteLine("[QuestObjectiveDbService] QuestObjectives table not found");
                return false;
            }

            // Check if columns exist
            var hasOptionalPoints = await ColumnExistsAsync(connection, "QuestObjectives", "OptionalPoints");
            var hasObjectiveType = await ColumnExistsAsync(connection, "QuestObjectives", "ObjectiveType");

            _allObjectives.Clear();

            // Check if Quests table has localization columns
            var hasQuestNameKo = await ColumnExistsAsync(connection, "Quests", "NameKo");
            var hasQuestNameJa = await ColumnExistsAsync(connection, "Quests", "NameJa");

            // Load objectives with location points and quest info
            var sql = $@"
                SELECT o.Id, o.QuestId, o.Description, o.MapName, o.LocationPoints,
                       q.Location as QuestLocation,
                       q.Name as QuestName,
                       {(hasQuestNameKo ? "q.NameKo as QuestNameKo," : "NULL as QuestNameKo,")}
                       {(hasQuestNameJa ? "q.NameJa as QuestNameJa," : "NULL as QuestNameJa,")}
                       q.Trader as TraderName
                       {(hasOptionalPoints ? ", o.OptionalPoints" : "")}
                       {(hasObjectiveType ? ", o.ObjectiveType" : "")}
                FROM QuestObjectives o
                LEFT JOIN Quests q ON o.QuestId = q.Id
                WHERE (o.LocationPoints IS NOT NULL AND o.LocationPoints != '')
                   {(hasOptionalPoints ? "OR (o.OptionalPoints IS NOT NULL AND o.OptionalPoints != '')" : "")}";

            await using var cmd = new SqliteCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var objective = new QuestObjective
                {
                    Id = reader.GetString(0),
                    QuestId = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    MapName = reader.IsDBNull(3) ? null : reader.GetString(3),
                    QuestLocation = reader.IsDBNull(5) ? null : reader.GetString(5),
                    QuestName = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    QuestNameKo = reader.IsDBNull(7) ? null : reader.GetString(7),
                    QuestNameJa = reader.IsDBNull(8) ? null : reader.GetString(8),
                    TraderName = reader.IsDBNull(9) ? null : reader.GetString(9)
                };

                // Parse LocationPoints JSON
                var locationJson = reader.IsDBNull(4) ? null : reader.GetString(4);
                objective.LocationPointsJson = locationJson;

                // Track column index for optional fields
                int nextIndex = 10;

                // Parse OptionalPoints JSON if column exists
                if (hasOptionalPoints && reader.FieldCount > nextIndex)
                {
                    var optionalJson = reader.IsDBNull(nextIndex) ? null : reader.GetString(nextIndex);
                    objective.OptionalPointsJson = optionalJson;
                    nextIndex++;
                }

                // Parse ObjectiveType if column exists
                if (hasObjectiveType && reader.FieldCount > nextIndex)
                {
                    var typeStr = reader.IsDBNull(nextIndex) ? "Custom" : reader.GetString(nextIndex);
                    objective.ObjectiveType = ParseObjectiveType(typeStr);
                }

                // Only add if has any coordinates
                if (objective.HasCoordinates || objective.HasOptionalPoints)
                {
                    _allObjectives.Add(objective);
                }
            }

            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[QuestObjectiveDbService] Loaded {_allObjectives.Count} objectives with location data");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[QuestObjectiveDbService] Error loading objectives: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        var sql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@name", tableName);
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return count > 0;
    }

    private async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName)
    {
        var sql = $"PRAGMA table_info({tableName})";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Parse ObjectiveType string from DB to enum
    /// </summary>
    private static QuestObjectiveType ParseObjectiveType(string typeStr)
    {
        return typeStr?.ToLowerInvariant() switch
        {
            "kill" => QuestObjectiveType.Kill,
            "collect" => QuestObjectiveType.Collect,
            "handover" => QuestObjectiveType.HandOver,
            "visit" => QuestObjectiveType.Visit,
            "mark" => QuestObjectiveType.Mark,
            "stash" => QuestObjectiveType.Stash,
            "survive" => QuestObjectiveType.Survive,
            "build" => QuestObjectiveType.Build,
            "task" => QuestObjectiveType.Task,
            _ => QuestObjectiveType.Custom
        };
    }
}
