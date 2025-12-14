using System.Text.Json;
using Microsoft.Data.Sqlite;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services;

/// <summary>
/// Map Floor Location DB 관리 서비스.
/// Y 좌표 (및 XZ 좌표)에 따라 Floor를 자동 감지하기 위한 설정을 관리합니다.
/// </summary>
public class FloorLocationService
{
    private static FloorLocationService? _instance;
    public static FloorLocationService Instance => _instance ??= new FloorLocationService();

    private readonly DatabaseService _db = DatabaseService.Instance;
    private bool _tableInitialized = false;
    private readonly Dictionary<string, List<MapFloorLocation>> _cache = new();

    private FloorLocationService() { }

    /// <summary>
    /// MapFloorLocations 테이블이 없으면 생성
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        if (!_db.IsConnected || _tableInitialized) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            CREATE TABLE IF NOT EXISTS MapFloorLocations (
                Id TEXT PRIMARY KEY,
                MapKey TEXT NOT NULL,
                FloorId TEXT NOT NULL,
                RegionName TEXT NOT NULL,
                MinY REAL NOT NULL,
                MaxY REAL NOT NULL,
                MinX REAL,
                MaxX REAL,
                MinZ REAL,
                MaxZ REAL,
                Priority INTEGER NOT NULL DEFAULT 0,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )";

        await using var cmd = new SqliteCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();

        // 인덱스 생성
        var indexSql = @"
            CREATE INDEX IF NOT EXISTS idx_floorlocations_mapkey ON MapFloorLocations(MapKey);
            CREATE INDEX IF NOT EXISTS idx_floorlocations_floorid ON MapFloorLocations(FloorId);
            CREATE INDEX IF NOT EXISTS idx_floorlocations_mapfloor ON MapFloorLocations(MapKey, FloorId)";
        await using var indexCmd = new SqliteCommand(indexSql, connection);
        await indexCmd.ExecuteNonQueryAsync();

        // 스키마 메타 등록
        await RegisterSchemaMetaAsync(connection);

        _tableInitialized = true;
    }

    private async Task RegisterSchemaMetaAsync(SqliteConnection connection)
    {
        var columns = new List<ColumnSchema>
        {
            new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, SortOrder = 0 },
            new() { Name = "MapKey", DisplayName = "Map", Type = ColumnType.Text, IsRequired = true, SortOrder = 1 },
            new() { Name = "FloorId", DisplayName = "Floor", Type = ColumnType.Text, IsRequired = true, SortOrder = 2 },
            new() { Name = "RegionName", DisplayName = "Region Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 3 },
            new() { Name = "MinY", DisplayName = "Min Y", Type = ColumnType.Real, IsRequired = true, SortOrder = 4 },
            new() { Name = "MaxY", DisplayName = "Max Y", Type = ColumnType.Real, IsRequired = true, SortOrder = 5 },
            new() { Name = "MinX", DisplayName = "Min X", Type = ColumnType.Real, SortOrder = 6 },
            new() { Name = "MaxX", DisplayName = "Max X", Type = ColumnType.Real, SortOrder = 7 },
            new() { Name = "MinZ", DisplayName = "Min Z", Type = ColumnType.Real, SortOrder = 8 },
            new() { Name = "MaxZ", DisplayName = "Max Z", Type = ColumnType.Real, SortOrder = 9 },
            new() { Name = "Priority", DisplayName = "Priority", Type = ColumnType.Integer, SortOrder = 10 },
            new() { Name = "CreatedAt", DisplayName = "Created At", Type = ColumnType.DateTime, SortOrder = 11 },
            new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 12 }
        };

        var schemaJson = JsonSerializer.Serialize(columns);

        var checkSql = "SELECT COUNT(*) FROM _schema_meta WHERE TableName = @TableName";
        await using var checkCmd = new SqliteCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("@TableName", "MapFloorLocations");
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

        if (count == 0)
        {
            var insertSql = @"
                INSERT INTO _schema_meta (TableName, DisplayName, SchemaJson, CreatedAt, UpdatedAt)
                VALUES (@TableName, @DisplayName, @SchemaJson, @Now, @Now)";
            await using var insertCmd = new SqliteCommand(insertSql, connection);
            insertCmd.Parameters.AddWithValue("@TableName", "MapFloorLocations");
            insertCmd.Parameters.AddWithValue("@DisplayName", "Map Floor Locations");
            insertCmd.Parameters.AddWithValue("@SchemaJson", schemaJson);
            insertCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
            await insertCmd.ExecuteNonQueryAsync();
        }
        else
        {
            var updateSql = @"
                UPDATE _schema_meta SET SchemaJson = @SchemaJson, UpdatedAt = @Now
                WHERE TableName = @TableName";
            await using var updateCmd = new SqliteCommand(updateSql, connection);
            updateCmd.Parameters.AddWithValue("@TableName", "MapFloorLocations");
            updateCmd.Parameters.AddWithValue("@SchemaJson", schemaJson);
            updateCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
            await updateCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// 특정 맵의 Floor Location 목록 로드
    /// </summary>
    public async Task<List<MapFloorLocation>> LoadByMapAsync(string mapKey)
    {
        var locations = new List<MapFloorLocation>();
        if (!_db.IsConnected) return locations;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT Id, MapKey, FloorId, RegionName, MinY, MaxY, MinX, MaxX, MinZ, MaxZ, Priority, CreatedAt, UpdatedAt
            FROM MapFloorLocations
            WHERE MapKey = @MapKey
            ORDER BY Priority DESC, RegionName";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MapKey", mapKey);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            locations.Add(ReadFloorLocation(reader));
        }

        // 캐시 업데이트
        _cache[mapKey] = locations;

        return locations;
    }

    /// <summary>
    /// 모든 Floor Location 로드
    /// </summary>
    public async Task<List<MapFloorLocation>> LoadAllAsync()
    {
        var locations = new List<MapFloorLocation>();
        if (!_db.IsConnected) return locations;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT Id, MapKey, FloorId, RegionName, MinY, MaxY, MinX, MaxX, MinZ, MaxZ, Priority, CreatedAt, UpdatedAt
            FROM MapFloorLocations
            ORDER BY MapKey, Priority DESC, RegionName";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            locations.Add(ReadFloorLocation(reader));
        }

        // 캐시 업데이트 (맵별로 그룹화)
        _cache.Clear();
        foreach (var group in locations.GroupBy(l => l.MapKey))
        {
            _cache[group.Key] = group.ToList();
        }

        return locations;
    }

    private static MapFloorLocation ReadFloorLocation(SqliteDataReader reader)
    {
        return new MapFloorLocation
        {
            Id = reader.GetString(0),
            MapKey = reader.GetString(1),
            FloorId = reader.GetString(2),
            RegionName = reader.GetString(3),
            MinY = reader.GetDouble(4),
            MaxY = reader.GetDouble(5),
            MinX = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            MaxX = reader.IsDBNull(7) ? null : reader.GetDouble(7),
            MinZ = reader.IsDBNull(8) ? null : reader.GetDouble(8),
            MaxZ = reader.IsDBNull(9) ? null : reader.GetDouble(9),
            Priority = reader.GetInt32(10),
            CreatedAt = DateTime.TryParse(reader.GetString(11), out var createdAt) ? createdAt : DateTime.UtcNow,
            UpdatedAt = DateTime.TryParse(reader.GetString(12), out var updatedAt) ? updatedAt : DateTime.UtcNow
        };
    }

    /// <summary>
    /// Floor Location 저장 (UPSERT)
    /// </summary>
    public async Task SaveAsync(MapFloorLocation location)
    {
        if (!_db.IsConnected) return;

        await EnsureTableExistsAsync();

        location.UpdatedAt = DateTime.UtcNow;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO MapFloorLocations (Id, MapKey, FloorId, RegionName, MinY, MaxY, MinX, MaxX, MinZ, MaxZ, Priority, CreatedAt, UpdatedAt)
            VALUES (@Id, @MapKey, @FloorId, @RegionName, @MinY, @MaxY, @MinX, @MaxX, @MinZ, @MaxZ, @Priority, @CreatedAt, @UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                MapKey = @MapKey,
                FloorId = @FloorId,
                RegionName = @RegionName,
                MinY = @MinY,
                MaxY = @MaxY,
                MinX = @MinX,
                MaxX = @MaxX,
                MinZ = @MinZ,
                MaxZ = @MaxZ,
                Priority = @Priority,
                UpdatedAt = @UpdatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", location.Id);
        cmd.Parameters.AddWithValue("@MapKey", location.MapKey);
        cmd.Parameters.AddWithValue("@FloorId", location.FloorId);
        cmd.Parameters.AddWithValue("@RegionName", location.RegionName);
        cmd.Parameters.AddWithValue("@MinY", location.MinY);
        cmd.Parameters.AddWithValue("@MaxY", location.MaxY);
        cmd.Parameters.AddWithValue("@MinX", (object?)location.MinX ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MaxX", (object?)location.MaxX ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MinZ", (object?)location.MinZ ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MaxZ", (object?)location.MaxZ ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Priority", location.Priority);
        cmd.Parameters.AddWithValue("@CreatedAt", location.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@UpdatedAt", location.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync();

        // 캐시 무효화
        _cache.Remove(location.MapKey);
    }

    /// <summary>
    /// Floor Location 삭제
    /// </summary>
    public async Task DeleteAsync(string id)
    {
        if (!_db.IsConnected) return;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // 삭제 전에 MapKey 조회 (캐시 무효화용)
        var selectSql = "SELECT MapKey FROM MapFloorLocations WHERE Id = @Id";
        await using var selectCmd = new SqliteCommand(selectSql, connection);
        selectCmd.Parameters.AddWithValue("@Id", id);
        var mapKey = await selectCmd.ExecuteScalarAsync() as string;

        var sql = "DELETE FROM MapFloorLocations WHERE Id = @Id";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", id);
        await cmd.ExecuteNonQueryAsync();

        // 캐시 무효화
        if (mapKey != null)
            _cache.Remove(mapKey);
    }

    /// <summary>
    /// 좌표에 해당하는 Floor 감지 (캐시에서)
    /// </summary>
    public string? DetectFloorFromCache(string mapKey, double x, double y, double z)
    {
        if (!_cache.TryGetValue(mapKey, out var locations) || locations.Count == 0)
            return null;

        // Priority 높은 순으로 정렬되어 있음
        foreach (var loc in locations)
        {
            if (loc.Contains(x, y, z))
                return loc.FloorId;
        }

        return null;
    }

    /// <summary>
    /// 좌표에 해당하는 Floor 감지 (DB에서 로드 후)
    /// </summary>
    public async Task<string?> DetectFloorAsync(string mapKey, double x, double y, double z)
    {
        // 캐시에 없으면 로드
        if (!_cache.ContainsKey(mapKey))
        {
            await LoadByMapAsync(mapKey);
        }

        return DetectFloorFromCache(mapKey, x, y, z);
    }

    /// <summary>
    /// 캐시 새로고침
    /// </summary>
    public async Task RefreshCacheAsync(string mapKey)
    {
        await LoadByMapAsync(mapKey);
    }

    /// <summary>
    /// 전체 캐시 새로고침
    /// </summary>
    public async Task RefreshAllCacheAsync()
    {
        await LoadAllAsync();
    }

    /// <summary>
    /// 캐시 초기화
    /// </summary>
    public void ResetCache()
    {
        _cache.Clear();
        _tableInitialized = false;
    }
}
