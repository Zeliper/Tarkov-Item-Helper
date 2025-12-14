using System.Text.Json;
using Microsoft.Data.Sqlite;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services;

/// <summary>
/// 앱 설정을 DB에 저장/로드하는 서비스.
/// Singleton 패턴으로 구현.
/// </summary>
public class AppSettingsService
{
    private static AppSettingsService? _instance;
    public static AppSettingsService Instance => _instance ??= new AppSettingsService();

    private readonly DatabaseService _db = DatabaseService.Instance;
    private bool _tableInitialized = false;
    private readonly Dictionary<string, string> _cache = new();

    // 설정 키 상수
    public const string ScreenshotWatcherPath = "ScreenshotWatcherPath";
    public const string ScreenshotWatcherPattern = "ScreenshotWatcherPattern";
    public const string ScreenshotWatcherEnabled = "ScreenshotWatcherEnabled";

    private AppSettingsService() { }

    /// <summary>
    /// AppSettings 테이블이 없으면 생성
    /// </summary>
    public async Task EnsureTableExistsAsync()
    {
        if (!_db.IsConnected || _tableInitialized) return;

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            )";

        await using var cmd = new SqliteCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();

        // 스키마 메타 등록
        await RegisterSchemaMetaAsync(connection);

        // 캐시 로드
        await LoadCacheAsync(connection);

        _tableInitialized = true;
    }

    private async Task RegisterSchemaMetaAsync(SqliteConnection connection)
    {
        var columns = new List<ColumnSchema>
        {
            new() { Name = "Key", DisplayName = "Key", Type = ColumnType.Text, IsPrimaryKey = true, SortOrder = 0 },
            new() { Name = "Value", DisplayName = "Value", Type = ColumnType.Text, IsRequired = true, SortOrder = 1 },
            new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 2 }
        };

        var schemaJson = JsonSerializer.Serialize(columns);

        var checkSql = "SELECT COUNT(*) FROM _schema_meta WHERE TableName = @TableName";
        await using var checkCmd = new SqliteCommand(checkSql, connection);
        checkCmd.Parameters.AddWithValue("@TableName", "AppSettings");
        var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

        if (count == 0)
        {
            var insertSql = @"
                INSERT INTO _schema_meta (TableName, DisplayName, SchemaJson, CreatedAt, UpdatedAt)
                VALUES (@TableName, @DisplayName, @SchemaJson, @Now, @Now)";
            await using var insertCmd = new SqliteCommand(insertSql, connection);
            insertCmd.Parameters.AddWithValue("@TableName", "AppSettings");
            insertCmd.Parameters.AddWithValue("@DisplayName", "App Settings");
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
            updateCmd.Parameters.AddWithValue("@TableName", "AppSettings");
            updateCmd.Parameters.AddWithValue("@SchemaJson", schemaJson);
            updateCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
            await updateCmd.ExecuteNonQueryAsync();
        }
    }

    private async Task LoadCacheAsync(SqliteConnection connection)
    {
        _cache.Clear();

        var sql = "SELECT Key, Value FROM AppSettings";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);
            _cache[key] = value;
        }
    }

    /// <summary>
    /// 설정값 가져오기
    /// </summary>
    public async Task<string?> GetAsync(string key)
    {
        if (!_db.IsConnected) return null;

        await EnsureTableExistsAsync();

        if (_cache.TryGetValue(key, out var cachedValue))
            return cachedValue;

        return null;
    }

    /// <summary>
    /// 설정값 가져오기 (기본값 포함)
    /// </summary>
    public async Task<string> GetAsync(string key, string defaultValue)
    {
        var value = await GetAsync(key);
        return value ?? defaultValue;
    }

    /// <summary>
    /// 설정값 저장하기
    /// </summary>
    public async Task SetAsync(string key, string value)
    {
        if (!_db.IsConnected) return;

        await EnsureTableExistsAsync();

        var connectionString = $"Data Source={_db.DatabasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO AppSettings (Key, Value, UpdatedAt)
            VALUES (@Key, @Value, @Now)
            ON CONFLICT(Key) DO UPDATE SET
                Value = @Value,
                UpdatedAt = @Now";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Key", key);
        cmd.Parameters.AddWithValue("@Value", value);
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();

        // 캐시 업데이트
        _cache[key] = value;
    }

    /// <summary>
    /// Bool 설정값 가져오기
    /// </summary>
    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var value = await GetAsync(key);
        if (value == null) return defaultValue;
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Bool 설정값 저장하기
    /// </summary>
    public async Task SetBoolAsync(string key, bool value)
    {
        await SetAsync(key, value ? "1" : "0");
    }

    /// <summary>
    /// 캐시 초기화 (DB 변경 시 호출)
    /// </summary>
    public void ResetCache()
    {
        _cache.Clear();
        _tableInitialized = false;
    }
}
