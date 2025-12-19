using System.Text.Json;
using Microsoft.Data.Sqlite;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services;

/// <summary>
/// API 참조 마커 DB 관리 서비스
/// Tarkov Market API에서 가져온 마커를 저장하고 조회
/// </summary>
public class ApiMarkerService
{
    private static ApiMarkerService? _instance;
    public static ApiMarkerService Instance => _instance ??= new ApiMarkerService();

    private readonly DatabaseService _db = DatabaseService.Instance;
    private bool _tableInitialized = false;

    private ApiMarkerService() { }

    /// <summary>
    /// ApiMarkers 테이블이 없으면 생성
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        if (!_db.IsConnected || _tableInitialized) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            CREATE TABLE IF NOT EXISTS ApiMarkers (
                Id TEXT PRIMARY KEY,
                TarkovMarketUid TEXT NOT NULL,

                -- 마커 기본 정보
                Name TEXT NOT NULL,
                NameKo TEXT,
                Category TEXT NOT NULL,
                SubCategory TEXT,

                -- 위치 정보
                MapKey TEXT NOT NULL,
                X REAL NOT NULL,    
                Y REAL,
                Z REAL NOT NULL,
                FloorId TEXT,

                -- 퀘스트 연관 정보 (DB Quests 테이블과 매칭용)
                QuestBsgId TEXT,
                QuestNameEn TEXT,
                ObjectiveDescription TEXT,

                -- 메타 정보
                ImportedAt TEXT NOT NULL,

                -- 승인 상태
                IsApproved INTEGER NOT NULL DEFAULT 0,
                ApprovedAt TEXT,

                UNIQUE(TarkovMarketUid)
            )";

        await using var cmd = new SqliteCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();

        // 인덱스 생성
        var indexSql = @"
            CREATE INDEX IF NOT EXISTS idx_apimarkers_mapkey ON ApiMarkers(MapKey);
            CREATE INDEX IF NOT EXISTS idx_apimarkers_bsgid ON ApiMarkers(QuestBsgId);
            CREATE INDEX IF NOT EXISTS idx_apimarkers_questname ON ApiMarkers(QuestNameEn)";
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
            new() { Name = "TarkovMarketUid", DisplayName = "Market UID", Type = ColumnType.Text, IsRequired = true, SortOrder = 1 },
            new() { Name = "Name", DisplayName = "Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 2 },
            new() { Name = "NameKo", DisplayName = "Name (KO)", Type = ColumnType.Text, SortOrder = 3 },
            new() { Name = "Category", DisplayName = "Category", Type = ColumnType.Text, IsRequired = true, SortOrder = 4 },
            new() { Name = "SubCategory", DisplayName = "SubCategory", Type = ColumnType.Text, SortOrder = 5 },
            new() { Name = "MapKey", DisplayName = "Map", Type = ColumnType.Text, IsRequired = true, SortOrder = 6 },
            new() { Name = "X", DisplayName = "X", Type = ColumnType.Real, IsRequired = true, SortOrder = 7 },
            new() { Name = "Y", DisplayName = "Y", Type = ColumnType.Real, SortOrder = 8 },
            new() { Name = "Z", DisplayName = "Z", Type = ColumnType.Real, IsRequired = true, SortOrder = 9 },
            new() { Name = "FloorId", DisplayName = "Floor", Type = ColumnType.Text, SortOrder = 10 },
            new() { Name = "QuestBsgId", DisplayName = "Quest BSG ID", Type = ColumnType.Text, SortOrder = 11 },
            new() { Name = "QuestNameEn", DisplayName = "Quest Name", Type = ColumnType.Text, SortOrder = 12 },
            new() { Name = "ObjectiveDescription", DisplayName = "Objective", Type = ColumnType.Text, SortOrder = 13 },
            new() { Name = "ImportedAt", DisplayName = "Imported At", Type = ColumnType.DateTime, SortOrder = 14 },
            new() { Name = "IsApproved", DisplayName = "Approved", Type = ColumnType.Boolean, SortOrder = 15 },
            new() { Name = "ApprovedAt", DisplayName = "Approved At", Type = ColumnType.DateTime, SortOrder = 16 }
        };

        var schemaJson = JsonSerializer.Serialize(columns);

        var checkSql = "SELECT COUNT(*) FROM _schema_meta WHERE TableName = @TableName";
        await using var checkCmd = new SqliteCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("@TableName", "ApiMarkers");
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

        if (count == 0)
        {
            var insertSql = @"
                INSERT INTO _schema_meta (TableName, DisplayName, SchemaJson, CreatedAt, UpdatedAt)
                VALUES (@TableName, @DisplayName, @SchemaJson, @Now, @Now)";
            await using var insertCmd = new SqliteCommand(insertSql, connection);
            insertCmd.Parameters.AddWithValue("@TableName", "ApiMarkers");
            insertCmd.Parameters.AddWithValue("@DisplayName", "API Markers (Reference)");
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
            updateCmd.Parameters.AddWithValue("@TableName", "ApiMarkers");
            updateCmd.Parameters.AddWithValue("@SchemaJson", schemaJson);
            updateCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
            await updateCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// 마커 일괄 저장 (UPSERT by TarkovMarketUid)
    /// </summary>
    public async Task SaveMarkersAsync(IEnumerable<ApiMarker> markers)
    {
        if (!_db.IsConnected) return;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var marker in markers)
            {
                var sql = @"
                    INSERT INTO ApiMarkers (
                        Id, TarkovMarketUid, Name, NameKo, Category, SubCategory,
                        MapKey, X, Y, Z, FloorId,
                        QuestBsgId, QuestNameEn, ObjectiveDescription, ImportedAt,
                        IsApproved, ApprovedAt
                    )
                    VALUES (
                        @Id, @TarkovMarketUid, @Name, @NameKo, @Category, @SubCategory,
                        @MapKey, @X, @Y, @Z, @FloorId,
                        @QuestBsgId, @QuestNameEn, @ObjectiveDescription, @ImportedAt,
                        @IsApproved, @ApprovedAt
                    )
                    ON CONFLICT(TarkovMarketUid) DO UPDATE SET
                        Name = @Name,
                        NameKo = @NameKo,
                        Category = @Category,
                        SubCategory = @SubCategory,
                        MapKey = @MapKey,
                        X = @X,
                        Y = @Y,
                        Z = @Z,
                        FloorId = @FloorId,
                        QuestBsgId = @QuestBsgId,
                        QuestNameEn = @QuestNameEn,
                        ObjectiveDescription = @ObjectiveDescription,
                        ImportedAt = @ImportedAt";

                await using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", marker.Id);
                cmd.Parameters.AddWithValue("@TarkovMarketUid", marker.TarkovMarketUid);
                cmd.Parameters.AddWithValue("@Name", marker.Name);
                cmd.Parameters.AddWithValue("@NameKo", (object?)marker.NameKo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Category", marker.Category);
                cmd.Parameters.AddWithValue("@SubCategory", (object?)marker.SubCategory ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MapKey", marker.MapKey);
                cmd.Parameters.AddWithValue("@X", marker.X);
                cmd.Parameters.AddWithValue("@Y", (object?)marker.Y ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Z", marker.Z);
                cmd.Parameters.AddWithValue("@FloorId", (object?)marker.FloorId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@QuestBsgId", (object?)marker.QuestBsgId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@QuestNameEn", (object?)marker.QuestNameEn ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ObjectiveDescription", (object?)marker.ObjectiveDescription ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ImportedAt", marker.ImportedAt.ToString("o"));
                cmd.Parameters.AddWithValue("@IsApproved", marker.IsApproved ? 1 : 0);
                cmd.Parameters.AddWithValue("@ApprovedAt", marker.ApprovedAt.HasValue ? marker.ApprovedAt.Value.ToString("o") : DBNull.Value);

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
    /// 맵별 마커 조회
    /// </summary>
    public async Task<List<ApiMarker>> GetByMapKeyAsync(string mapKey)
    {
        var markers = new List<ApiMarker>();
        if (!_db.IsConnected) return markers;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT Id, TarkovMarketUid, Name, NameKo, Category, SubCategory,
                           MapKey, X, Y, Z, FloorId,
                           QuestBsgId, QuestNameEn, ObjectiveDescription, ImportedAt,
                           IsApproved, ApprovedAt
                    FROM ApiMarkers WHERE MapKey = @MapKey";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MapKey", mapKey);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            markers.Add(ReadMarker(reader));
        }

        return markers;
    }

    /// <summary>
    /// BSG ID로 마커 조회 (퀘스트 매칭용)
    /// </summary>
    public async Task<List<ApiMarker>> GetByQuestBsgIdAsync(string bsgId)
    {
        var markers = new List<ApiMarker>();
        if (!_db.IsConnected || string.IsNullOrEmpty(bsgId)) return markers;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"SELECT Id, TarkovMarketUid, Name, NameKo, Category, SubCategory,
                           MapKey, X, Y, Z, FloorId,
                           QuestBsgId, QuestNameEn, ObjectiveDescription, ImportedAt,
                           IsApproved, ApprovedAt
                    FROM ApiMarkers WHERE QuestBsgId = @BsgId";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@BsgId", bsgId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            markers.Add(ReadMarker(reader));
        }

        return markers;
    }

    /// <summary>
    /// 퀘스트명으로 마커 조회 (fallback 매칭용)
    /// </summary>
    public async Task<List<ApiMarker>> GetByQuestNameAsync(string questName)
    {
        var markers = new List<ApiMarker>();
        if (!_db.IsConnected || string.IsNullOrEmpty(questName)) return markers;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // LIKE 검색으로 유사 매칭
        var sql = @"SELECT Id, TarkovMarketUid, Name, NameKo, Category, SubCategory,
                           MapKey, X, Y, Z, FloorId,
                           QuestBsgId, QuestNameEn, ObjectiveDescription, ImportedAt,
                           IsApproved, ApprovedAt
                    FROM ApiMarkers WHERE QuestNameEn LIKE @QuestName";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@QuestName", $"%{questName}%");

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            markers.Add(ReadMarker(reader));
        }

        return markers;
    }

    /// <summary>
    /// 맵별 마커 삭제 (재import용)
    /// </summary>
    public async Task DeleteByMapKeyAsync(string mapKey)
    {
        if (!_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM ApiMarkers WHERE MapKey = @MapKey";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MapKey", mapKey);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 마커 삭제
    /// </summary>
    public async Task DeleteAllAsync()
    {
        if (!_db.IsConnected) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM ApiMarkers";
        await using var cmd = new SqliteCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 맵별 마커 개수 조회
    /// </summary>
    public async Task<int> GetCountByMapKeyAsync(string mapKey)
    {
        if (!_db.IsConnected) return 0;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT COUNT(*) FROM ApiMarkers WHERE MapKey = @MapKey";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@MapKey", mapKey);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static ApiMarker ReadMarker(SqliteDataReader reader)
    {
        return new ApiMarker
        {
            Id = reader.GetString(0),
            TarkovMarketUid = reader.GetString(1),
            Name = reader.GetString(2),
            NameKo = reader.IsDBNull(3) ? null : reader.GetString(3),
            Category = reader.GetString(4),
            SubCategory = reader.IsDBNull(5) ? null : reader.GetString(5),
            MapKey = reader.GetString(6),
            X = reader.GetDouble(7),
            Y = reader.IsDBNull(8) ? null : reader.GetDouble(8),
            Z = reader.GetDouble(9),
            FloorId = reader.IsDBNull(10) ? null : reader.GetString(10),
            QuestBsgId = reader.IsDBNull(11) ? null : reader.GetString(11),
            QuestNameEn = reader.IsDBNull(12) ? null : reader.GetString(12),
            ObjectiveDescription = reader.IsDBNull(13) ? null : reader.GetString(13),
            ImportedAt = DateTime.TryParse(reader.GetString(14), out var dt) ? dt : DateTime.MinValue,
            IsApproved = !reader.IsDBNull(15) && reader.GetInt64(15) != 0,
            ApprovedAt = reader.IsDBNull(16) ? null : DateTime.TryParse(reader.GetString(16), out var adt) ? adt : null
        };
    }

    /// <summary>
    /// API 마커 승인 상태 업데이트
    /// </summary>
    public async Task UpdateApprovalAsync(string markerId, bool isApproved)
    {
        if (!_db.IsConnected || string.IsNullOrEmpty(markerId)) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = isApproved
            ? "UPDATE ApiMarkers SET IsApproved = 1, ApprovedAt = @ApprovedAt WHERE Id = @Id"
            : "UPDATE ApiMarkers SET IsApproved = 0, ApprovedAt = NULL WHERE Id = @Id";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Id", markerId);
        if (isApproved)
        {
            cmd.Parameters.AddWithValue("@ApprovedAt", DateTime.UtcNow.ToString("o"));
        }
        await cmd.ExecuteNonQueryAsync();
    }
}
