using System.IO;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// SQLite DB에서 하이드아웃 데이터를 로드하는 서비스.
/// tarkov_data.db의 HideoutStations, HideoutLevels, HideoutItemRequirements 등 테이블 사용.
/// </summary>
public sealed class HideoutDbService
{
    private static HideoutDbService? _instance;
    public static HideoutDbService Instance => _instance ??= new HideoutDbService();

    private readonly string _databasePath;
    private List<HideoutModule> _allStations = new();
    private Dictionary<string, HideoutModule> _stationsById = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public int StationCount => _allStations.Count;

    private HideoutDbService()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _databasePath = Path.Combine(appDir, "Assets", "tarkov_data.db");
    }

    /// <summary>
    /// DB가 존재하는지 확인
    /// </summary>
    public bool DatabaseExists => File.Exists(_databasePath);

    /// <summary>
    /// 모든 하이드아웃 스테이션 반환
    /// </summary>
    public IReadOnlyList<HideoutModule> AllStations => _allStations;

    /// <summary>
    /// ID로 스테이션 조회
    /// </summary>
    public HideoutModule? GetStationById(string id)
    {
        return _stationsById.TryGetValue(id, out var station) ? station : null;
    }

    /// <summary>
    /// DB에서 모든 하이드아웃 스테이션을 로드합니다.
    /// </summary>
    public async Task<bool> LoadStationsAsync()
    {
        if (!DatabaseExists)
        {
            System.Diagnostics.Debug.WriteLine($"[HideoutDbService] Database not found: {_databasePath}");
            return false;
        }

        try
        {
            var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // HideoutStations 테이블 존재 여부 확인
            if (!await TableExistsAsync(connection, "HideoutStations"))
            {
                System.Diagnostics.Debug.WriteLine("[HideoutDbService] HideoutStations table not found");
                return false;
            }

            // 1. 기본 스테이션 정보 로드
            var stations = await LoadBaseStationsAsync(connection);
            var stationLookup = stations.ToDictionary(s => s.Id, s => s, StringComparer.OrdinalIgnoreCase);

            // 2. 레벨 정보 로드
            await LoadLevelsAsync(connection, stationLookup);

            // 캐시에 저장
            _allStations = stations;
            _stationsById.Clear();

            foreach (var station in stations)
            {
                if (!string.IsNullOrEmpty(station.Id))
                {
                    _stationsById[station.Id] = station;
                }
            }

            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[HideoutDbService] Loaded {stations.Count} hideout stations from DB");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HideoutDbService] Error loading stations: {ex.Message}");
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

    /// <summary>
    /// 기본 스테이션 정보 로드
    /// </summary>
    private async Task<List<HideoutModule>> LoadBaseStationsAsync(SqliteConnection connection)
    {
        var stations = new List<HideoutModule>();

        var sql = @"
            SELECT
                Id, Name, NameKO, NameJA, NormalizedName, ImageLink
            FROM HideoutStations
            ORDER BY Name";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var station = new HideoutModule
            {
                Id = reader.GetString(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                NameKo = reader.IsDBNull(2) ? null : reader.GetString(2),
                NameJa = reader.IsDBNull(3) ? null : reader.GetString(3),
                NormalizedName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ImageLink = reader.IsDBNull(5) ? null : reader.GetString(5),
                Levels = new List<HideoutLevel>()
            };
            stations.Add(station);
        }

        return stations;
    }

    /// <summary>
    /// 레벨 정보 로드
    /// </summary>
    private async Task LoadLevelsAsync(SqliteConnection connection, Dictionary<string, HideoutModule> stationLookup)
    {
        if (!await TableExistsAsync(connection, "HideoutLevels"))
        {
            System.Diagnostics.Debug.WriteLine("[HideoutDbService] HideoutLevels table not found");
            return;
        }

        var sql = @"
            SELECT
                StationId, Level, ConstructionTime
            FROM HideoutLevels
            ORDER BY StationId, Level";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        var levelLookup = new Dictionary<(string, int), HideoutLevel>();

        while (await reader.ReadAsync())
        {
            var stationId = reader.GetString(0);
            var levelNum = reader.GetInt32(1);
            var constructionTime = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

            if (stationLookup.TryGetValue(stationId, out var station))
            {
                var level = new HideoutLevel
                {
                    Level = levelNum,
                    ConstructionTime = constructionTime,
                    ItemRequirements = new List<HideoutItemRequirement>(),
                    StationLevelRequirements = new List<HideoutStationRequirement>(),
                    TraderRequirements = new List<HideoutTraderRequirement>(),
                    SkillRequirements = new List<HideoutSkillRequirement>()
                };
                station.Levels.Add(level);
                levelLookup[(stationId, levelNum)] = level;
            }
        }

        // Load item requirements
        await LoadItemRequirementsAsync(connection, levelLookup);

        // Load station requirements
        await LoadStationRequirementsAsync(connection, levelLookup, stationLookup);

        // Load trader requirements
        await LoadTraderRequirementsAsync(connection, levelLookup);

        // Load skill requirements
        await LoadSkillRequirementsAsync(connection, levelLookup);
    }

    private async Task LoadItemRequirementsAsync(SqliteConnection connection, Dictionary<(string, int), HideoutLevel> levelLookup)
    {
        if (!await TableExistsAsync(connection, "HideoutItemRequirements"))
            return;

        var sql = @"
            SELECT
                StationId, Level, ItemId, ItemName, ItemNameKO, ItemNameJA,
                IconLink, Count, FoundInRaid
            FROM HideoutItemRequirements";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var stationId = reader.GetString(0);
            var levelNum = reader.GetInt32(1);

            if (levelLookup.TryGetValue((stationId, levelNum), out var level))
            {
                var itemId = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var itemName = reader.IsDBNull(3) ? "" : reader.GetString(3);

                level.ItemRequirements.Add(new HideoutItemRequirement
                {
                    ItemId = itemId,
                    ItemName = itemName,
                    ItemNameKo = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ItemNameJa = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ItemNormalizedName = NormalizedNameGenerator.Generate(itemName), // Generate from name
                    IconLink = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Count = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
                    FoundInRaid = !reader.IsDBNull(8) && reader.GetInt32(8) == 1
                });
            }
        }
    }

    private async Task LoadStationRequirementsAsync(SqliteConnection connection, Dictionary<(string, int), HideoutLevel> levelLookup, Dictionary<string, HideoutModule> stationLookup)
    {
        if (!await TableExistsAsync(connection, "HideoutStationRequirements"))
            return;

        var sql = @"
            SELECT
                StationId, Level, RequiredStationId, RequiredLevel
            FROM HideoutStationRequirements";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var stationId = reader.GetString(0);
            var levelNum = reader.GetInt32(1);
            var requiredStationId = reader.GetString(2);
            var requiredLevel = reader.GetInt32(3);

            if (levelLookup.TryGetValue((stationId, levelNum), out var level))
            {
                var requiredStation = stationLookup.TryGetValue(requiredStationId, out var s) ? s : null;
                level.StationLevelRequirements.Add(new HideoutStationRequirement
                {
                    StationId = requiredStationId,
                    StationName = requiredStation?.Name ?? "",
                    StationNameKo = requiredStation?.NameKo,
                    StationNameJa = requiredStation?.NameJa,
                    Level = requiredLevel
                });
            }
        }
    }

    private async Task LoadTraderRequirementsAsync(SqliteConnection connection, Dictionary<(string, int), HideoutLevel> levelLookup)
    {
        if (!await TableExistsAsync(connection, "HideoutTraderRequirements"))
            return;

        var sql = @"
            SELECT
                StationId, Level, TraderId, TraderName, TraderNameKO, TraderNameJA, RequiredLevel
            FROM HideoutTraderRequirements";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var stationId = reader.GetString(0);
            var levelNum = reader.GetInt32(1);

            if (levelLookup.TryGetValue((stationId, levelNum), out var level))
            {
                level.TraderRequirements.Add(new HideoutTraderRequirement
                {
                    TraderId = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    TraderName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    TraderNameKo = reader.IsDBNull(4) ? null : reader.GetString(4),
                    TraderNameJa = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Level = reader.IsDBNull(6) ? 0 : reader.GetInt32(6)
                });
            }
        }
    }

    private async Task LoadSkillRequirementsAsync(SqliteConnection connection, Dictionary<(string, int), HideoutLevel> levelLookup)
    {
        if (!await TableExistsAsync(connection, "HideoutSkillRequirements"))
            return;

        var sql = @"
            SELECT
                StationId, Level, SkillName, SkillNameKO, SkillNameJA, RequiredLevel
            FROM HideoutSkillRequirements";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var stationId = reader.GetString(0);
            var levelNum = reader.GetInt32(1);

            if (levelLookup.TryGetValue((stationId, levelNum), out var level))
            {
                level.SkillRequirements.Add(new HideoutSkillRequirement
                {
                    Name = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    NameKo = reader.IsDBNull(3) ? null : reader.GetString(3),
                    NameJa = reader.IsDBNull(4) ? null : reader.GetString(4),
                    Level = reader.IsDBNull(5) ? 0 : reader.GetInt32(5)
                });
            }
        }
    }
}
