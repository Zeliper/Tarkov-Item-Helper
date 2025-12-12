using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services;

/// <summary>
/// 마커 병합 결과
/// </summary>
public class MergeResult
{
    public bool Success { get; set; }
    public int TotalInExternal { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Map Marker DB 관리 서비스
/// </summary>
public class MapMarkerService
{
    private static MapMarkerService? _instance;
    public static MapMarkerService Instance => _instance ??= new MapMarkerService();

    private readonly DatabaseService _db = DatabaseService.Instance;
    private bool _tableInitialized = false;

    private MapMarkerService() { }

    /// <summary>
    /// MapMarkers 테이블이 없으면 생성
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        if (!_db.IsConnected || _tableInitialized) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            CREATE TABLE IF NOT EXISTS MapMarkers (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                NameKo TEXT,
                MarkerType TEXT NOT NULL,
                MapKey TEXT NOT NULL,
                X REAL NOT NULL DEFAULT 0,
                Y REAL NOT NULL DEFAULT 0,
                Z REAL NOT NULL DEFAULT 0,
                FloorId TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )";

        await using var cmd = new SqliteCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();

        // 인덱스 생성
        var indexSql = @"
            CREATE INDEX IF NOT EXISTS idx_mapmarkers_mapkey ON MapMarkers(MapKey);
            CREATE INDEX IF NOT EXISTS idx_mapmarkers_type ON MapMarkers(MarkerType)";
        await using var indexCmd = new SqliteCommand(indexSql, connection);
        await indexCmd.ExecuteNonQueryAsync();

        // 스키마 메타 등록 (Tables 목록에 표시하기 위함)
        await RegisterSchemaMetaAsync(connection);

        _tableInitialized = true;
    }

    private async Task RegisterSchemaMetaAsync(SqliteConnection connection)
    {
        var columns = new List<ColumnSchema>
        {
            new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, SortOrder = 0 },
            new() { Name = "Name", DisplayName = "Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 1 },
            new() { Name = "NameKo", DisplayName = "Name (KO)", Type = ColumnType.Text, SortOrder = 2 },
            new() { Name = "MarkerType", DisplayName = "Type", Type = ColumnType.Text, IsRequired = true, SortOrder = 3 },
            new() { Name = "MapKey", DisplayName = "Map", Type = ColumnType.Text, IsRequired = true, SortOrder = 4 },
            new() { Name = "X", DisplayName = "X", Type = ColumnType.Real, IsRequired = true, SortOrder = 5 },
            new() { Name = "Y", DisplayName = "Y", Type = ColumnType.Real, IsRequired = true, SortOrder = 6 },
            new() { Name = "Z", DisplayName = "Z", Type = ColumnType.Real, IsRequired = true, SortOrder = 7 },
            new() { Name = "FloorId", DisplayName = "Floor", Type = ColumnType.Text, SortOrder = 8 },
            new() { Name = "CreatedAt", DisplayName = "Created At", Type = ColumnType.DateTime, SortOrder = 9 },
            new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 10 }
        };

        var schemaJson = JsonSerializer.Serialize(columns);

        // Check if exists
        var checkSql = "SELECT COUNT(*) FROM _schema_meta WHERE TableName = @TableName";
        await using var checkCmd = new SqliteCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("@TableName", "MapMarkers");
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

        if (count == 0)
        {
            var insertSql = @"
                INSERT INTO _schema_meta (TableName, DisplayName, SchemaJson, CreatedAt, UpdatedAt)
                VALUES (@TableName, @DisplayName, @SchemaJson, @Now, @Now)";
            await using var insertCmd = new SqliteCommand(insertSql, connection);
            insertCmd.Parameters.AddWithValue("@TableName", "MapMarkers");
            insertCmd.Parameters.AddWithValue("@DisplayName", "Map Markers");
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
            updateCmd.Parameters.AddWithValue("@TableName", "MapMarkers");
            updateCmd.Parameters.AddWithValue("@SchemaJson", schemaJson);
            updateCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
            await updateCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// 모든 마커 로드
    /// </summary>
    public async Task<List<MapMarker>> LoadAllMarkersAsync()
    {
        var markers = new List<MapMarker>();
        if (!_db.IsConnected) return markers;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId FROM MapMarkers";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var marker = new MapMarker
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                NameKo = reader.IsDBNull(2) ? null : reader.GetString(2),
                MarkerType = Enum.TryParse<MapMarkerType>(reader.GetString(3), out var type) ? type : MapMarkerType.PmcExtraction,
                MapKey = reader.GetString(4),
                X = reader.GetDouble(5),
                Y = reader.GetDouble(6),
                Z = reader.GetDouble(7),
                FloorId = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
            markers.Add(marker);
        }

        return markers;
    }

    /// <summary>
    /// 특정 맵의 마커 로드
    /// </summary>
    public async Task<List<MapMarker>> LoadMarkersByMapAsync(string mapKey)
    {
        var markers = new List<MapMarker>();
        if (!_db.IsConnected) return markers;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId FROM MapMarkers WHERE MapKey = @MapKey";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MapKey", mapKey);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var marker = new MapMarker
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                NameKo = reader.IsDBNull(2) ? null : reader.GetString(2),
                MarkerType = Enum.TryParse<MapMarkerType>(reader.GetString(3), out var type) ? type : MapMarkerType.PmcExtraction,
                MapKey = reader.GetString(4),
                X = reader.GetDouble(5),
                Y = reader.GetDouble(6),
                Z = reader.GetDouble(7),
                FloorId = reader.IsDBNull(8) ? null : reader.GetString(8)
            };
            markers.Add(marker);
        }

        return markers;
    }

    /// <summary>
    /// 마커 추가 또는 업데이트
    /// </summary>
    public async Task SaveMarkerAsync(MapMarker marker)
    {
        if (!_db.IsConnected) return;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var now = DateTime.UtcNow.ToString("o");

        // UPSERT (INSERT OR REPLACE)
        var sql = @"
            INSERT INTO MapMarkers (Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId, CreatedAt, UpdatedAt)
            VALUES (@Id, @Name, @NameKo, @MarkerType, @MapKey, @X, @Y, @Z, @FloorId, @CreatedAt, @UpdatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = @Name,
                NameKo = @NameKo,
                MarkerType = @MarkerType,
                MapKey = @MapKey,
                X = @X,
                Y = @Y,
                Z = @Z,
                FloorId = @FloorId,
                UpdatedAt = @UpdatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", marker.Id);
        cmd.Parameters.AddWithValue("@Name", marker.Name);
        cmd.Parameters.AddWithValue("@NameKo", (object?)marker.NameKo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MarkerType", marker.MarkerType.ToString());
        cmd.Parameters.AddWithValue("@MapKey", marker.MapKey);
        cmd.Parameters.AddWithValue("@X", marker.X);
        cmd.Parameters.AddWithValue("@Y", marker.Y);
        cmd.Parameters.AddWithValue("@Z", marker.Z);
        cmd.Parameters.AddWithValue("@FloorId", (object?)marker.FloorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAt", now);
        cmd.Parameters.AddWithValue("@UpdatedAt", now);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 여러 마커 일괄 저장
    /// </summary>
    public async Task SaveMarkersAsync(IEnumerable<MapMarker> markers)
    {
        if (!_db.IsConnected) return;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var now = DateTime.UtcNow.ToString("o");

        await using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var marker in markers)
            {
                var sql = @"
                    INSERT INTO MapMarkers (Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId, CreatedAt, UpdatedAt)
                    VALUES (@Id, @Name, @NameKo, @MarkerType, @MapKey, @X, @Y, @Z, @FloorId, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(Id) DO UPDATE SET
                        Name = @Name,
                        NameKo = @NameKo,
                        MarkerType = @MarkerType,
                        MapKey = @MapKey,
                        X = @X,
                        Y = @Y,
                        Z = @Z,
                        FloorId = @FloorId,
                        UpdatedAt = @UpdatedAt";

                await using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", marker.Id);
                cmd.Parameters.AddWithValue("@Name", marker.Name);
                cmd.Parameters.AddWithValue("@NameKo", (object?)marker.NameKo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MarkerType", marker.MarkerType.ToString());
                cmd.Parameters.AddWithValue("@MapKey", marker.MapKey);
                cmd.Parameters.AddWithValue("@X", marker.X);
                cmd.Parameters.AddWithValue("@Y", marker.Y);
                cmd.Parameters.AddWithValue("@Z", marker.Z);
                cmd.Parameters.AddWithValue("@FloorId", (object?)marker.FloorId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedAt", now);
                cmd.Parameters.AddWithValue("@UpdatedAt", now);

                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 마커 삭제
    /// </summary>
    public async Task DeleteMarkerAsync(string markerId)
    {
        if (!_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM MapMarkers WHERE Id = @Id";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", markerId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 특정 맵의 모든 마커 삭제
    /// </summary>
    public async Task DeleteMarkersByMapAsync(string mapKey)
    {
        if (!_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM MapMarkers WHERE MapKey = @MapKey";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MapKey", mapKey);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 외부 DB에서 마커를 읽어와 현재 DB에 병합 (UPSERT)
    /// </summary>
    /// <param name="externalDbPath">외부 DB 파일 경로</param>
    /// <returns>병합 결과 (추가/업데이트/실패 개수)</returns>
    public async Task<MergeResult> MergeMarkersFromExternalDbAsync(string externalDbPath)
    {
        var result = new MergeResult();

        if (!_db.IsConnected)
        {
            result.ErrorMessage = "No database connected";
            return result;
        }

        if (!System.IO.File.Exists(externalDbPath))
        {
            result.ErrorMessage = $"External database not found: {externalDbPath}";
            return result;
        }

        await EnsureTableExistsAsync();

        // 외부 DB에서 마커 읽기
        var externalMarkers = new List<MapMarker>();
        var externalConnectionString = $"Data Source={externalDbPath}";

        try
        {
            await using var extConnection = new SqliteConnection(externalConnectionString);
            await extConnection.OpenAsync();

            // 테이블 이름 확인 (Map_Marker 또는 MapMarkers)
            var tableName = await GetMarkerTableNameAsync(extConnection);
            if (tableName == null)
            {
                result.ErrorMessage = "No marker table found in external database (looked for 'MapMarkers' and 'Map_Marker')";
                return result;
            }

            // 컬럼 이름 확인 (외부 DB의 스키마에 따라 다를 수 있음)
            var columns = await GetTableColumnsAsync(extConnection, tableName);

            // SQL 쿼리 생성 (컬럼 존재 여부에 따라 동적으로)
            var hasNameKo = columns.Contains("NameKo") || columns.Contains("NameKO");
            var hasFloorId = columns.Contains("FloorId");
            var hasY = columns.Contains("Y");

            var nameKoColumn = columns.Contains("NameKo") ? "NameKo" : (columns.Contains("NameKO") ? "NameKO" : null);

            var sql = $@"SELECT Id, Name, {(nameKoColumn != null ? nameKoColumn + ", " : "")}MarkerType, MapKey, X, {(hasY ? "Y, " : "")}Z{(hasFloorId ? ", FloorId" : "")} FROM {tableName}";

            await using var cmd = new SqliteCommand(sql, extConnection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var marker = new MapMarker
                {
                    Id = reader.GetString(reader.GetOrdinal("Id")),
                    Name = reader.GetString(reader.GetOrdinal("Name")),
                    MarkerType = Enum.TryParse<MapMarkerType>(reader.GetString(reader.GetOrdinal("MarkerType")), out var type)
                        ? type
                        : MapMarkerType.PmcExtraction,
                    MapKey = reader.GetString(reader.GetOrdinal("MapKey")),
                    X = reader.GetDouble(reader.GetOrdinal("X")),
                    Y = hasY ? reader.GetDouble(reader.GetOrdinal("Y")) : 0,
                    Z = reader.GetDouble(reader.GetOrdinal("Z"))
                };

                if (nameKoColumn != null)
                {
                    var ordinal = reader.GetOrdinal(nameKoColumn);
                    marker.NameKo = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                }

                if (hasFloorId)
                {
                    var ordinal = reader.GetOrdinal("FloorId");
                    marker.FloorId = reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
                }

                externalMarkers.Add(marker);
            }

            result.TotalInExternal = externalMarkers.Count;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Failed to read external database: {ex.Message}";
            return result;
        }

        // 현재 DB에 병합
        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // 기존 마커 ID 목록 조회
        var existingIds = new HashSet<string>();
        var selectSql = "SELECT Id FROM MapMarkers";
        await using (var selectCmd = new SqliteCommand(selectSql, connection))
        await using (var reader = await selectCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                existingIds.Add(reader.GetString(0));
            }
        }

        var now = DateTime.UtcNow.ToString("o");

        await using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var marker in externalMarkers)
            {
                var isNew = !existingIds.Contains(marker.Id);

                var sql = @"
                    INSERT INTO MapMarkers (Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId, CreatedAt, UpdatedAt)
                    VALUES (@Id, @Name, @NameKo, @MarkerType, @MapKey, @X, @Y, @Z, @FloorId, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(Id) DO UPDATE SET
                        Name = @Name,
                        NameKo = @NameKo,
                        MarkerType = @MarkerType,
                        MapKey = @MapKey,
                        X = @X,
                        Y = @Y,
                        Z = @Z,
                        FloorId = @FloorId,
                        UpdatedAt = @UpdatedAt";

                await using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", marker.Id);
                cmd.Parameters.AddWithValue("@Name", marker.Name);
                cmd.Parameters.AddWithValue("@NameKo", (object?)marker.NameKo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MarkerType", marker.MarkerType.ToString());
                cmd.Parameters.AddWithValue("@MapKey", marker.MapKey);
                cmd.Parameters.AddWithValue("@X", marker.X);
                cmd.Parameters.AddWithValue("@Y", marker.Y);
                cmd.Parameters.AddWithValue("@Z", marker.Z);
                cmd.Parameters.AddWithValue("@FloorId", (object?)marker.FloorId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedAt", now);
                cmd.Parameters.AddWithValue("@UpdatedAt", now);

                await cmd.ExecuteNonQueryAsync();

                if (isNew)
                    result.Added++;
                else
                    result.Updated++;
            }

            transaction.Commit();
            result.Success = true;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            result.ErrorMessage = $"Failed to merge markers: {ex.Message}";
        }

        return result;
    }

    private async Task<string?> GetMarkerTableNameAsync(SqliteConnection connection)
    {
        // MapMarkers 테이블 확인
        var checkSql = "SELECT name FROM sqlite_master WHERE type='table' AND name IN ('MapMarkers', 'Map_Marker')";
        await using var cmd = new SqliteCommand(checkSql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            return reader.GetString(0);
        }

        return null;
    }

    private async Task<HashSet<string>> GetTableColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sql = $"PRAGMA table_info({tableName})";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1)); // column name is at index 1
        }

        return columns;
    }

    /// <summary>
    /// 기존 마커 목록과 동기화 (없는 것 삭제, 있는 것 업데이트)
    /// </summary>
    public async Task SyncMarkersAsync(IEnumerable<MapMarker> markers)
    {
        if (!_db.IsConnected) return;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var now = DateTime.UtcNow.ToString("o");
        var markerIds = new HashSet<string>(markers.Select(m => m.Id));

        await using var transaction = connection.BeginTransaction();

        try
        {
            // 기존 ID 목록 조회
            var existingIds = new HashSet<string>();
            var selectSql = "SELECT Id FROM MapMarkers";
            await using (var selectCmd = new SqliteCommand(selectSql, connection, transaction))
            await using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    existingIds.Add(reader.GetString(0));
                }
            }

            // 새 목록에 없는 마커 삭제
            var idsToDelete = existingIds.Except(markerIds);
            foreach (var id in idsToDelete)
            {
                var deleteSql = "DELETE FROM MapMarkers WHERE Id = @Id";
                await using var deleteCmd = new SqliteCommand(deleteSql, connection, transaction);
                deleteCmd.Parameters.AddWithValue("@Id", id);
                await deleteCmd.ExecuteNonQueryAsync();
            }

            // 마커 저장/업데이트
            foreach (var marker in markers)
            {
                var sql = @"
                    INSERT INTO MapMarkers (Id, Name, NameKo, MarkerType, MapKey, X, Y, Z, FloorId, CreatedAt, UpdatedAt)
                    VALUES (@Id, @Name, @NameKo, @MarkerType, @MapKey, @X, @Y, @Z, @FloorId, @CreatedAt, @UpdatedAt)
                    ON CONFLICT(Id) DO UPDATE SET
                        Name = @Name,
                        NameKo = @NameKo,
                        MarkerType = @MarkerType,
                        MapKey = @MapKey,
                        X = @X,
                        Y = @Y,
                        Z = @Z,
                        FloorId = @FloorId,
                        UpdatedAt = @UpdatedAt";

                await using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", marker.Id);
                cmd.Parameters.AddWithValue("@Name", marker.Name);
                cmd.Parameters.AddWithValue("@NameKo", (object?)marker.NameKo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MarkerType", marker.MarkerType.ToString());
                cmd.Parameters.AddWithValue("@MapKey", marker.MapKey);
                cmd.Parameters.AddWithValue("@X", marker.X);
                cmd.Parameters.AddWithValue("@Y", marker.Y);
                cmd.Parameters.AddWithValue("@Z", marker.Z);
                cmd.Parameters.AddWithValue("@FloorId", (object?)marker.FloorId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@CreatedAt", now);
                cmd.Parameters.AddWithValue("@UpdatedAt", now);

                await cmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
