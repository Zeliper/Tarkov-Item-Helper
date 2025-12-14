using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// 사용자 데이터를 SQLite DB (user_data.db)에 저장/로드하는 서비스.
/// 퀘스트 진행, 목표 완료, 하이드아웃 진행, 아이템 인벤토리 등을 관리합니다.
/// </summary>
public sealed class UserDataDbService
{
    private static UserDataDbService? _instance;
    public static UserDataDbService Instance => _instance ??= new UserDataDbService();

    private readonly string _databasePath;
    private bool _isInitialized;

    public bool IsInitialized => _isInitialized;
    public string DatabasePath => _databasePath;

    /// <summary>
    /// 마이그레이션 진행 상황 이벤트
    /// </summary>
    public event Action<string>? MigrationProgress;

    /// <summary>
    /// 마이그레이션이 필요한지 확인
    /// </summary>
    public bool NeedsMigration()
    {
        var v2Path = Path.Combine(AppEnv.ConfigPath, "quest_progress_v2.json");
        var v1Path = Path.Combine(AppEnv.ConfigPath, "quest_progress.json");
        var objPath = Path.Combine(AppEnv.ConfigPath, "objective_progress.json");
        var hideoutPath = Path.Combine(AppEnv.ConfigPath, "hideout_progress.json");
        var inventoryPath = Path.Combine(AppEnv.ConfigPath, "item_inventory.json");

        return File.Exists(v2Path) || File.Exists(v1Path) || File.Exists(objPath) ||
               File.Exists(hideoutPath) || File.Exists(inventoryPath);
    }

    private void ReportProgress(string message)
    {
        MigrationProgress?.Invoke(message);
        System.Diagnostics.Debug.WriteLine($"[UserDataDbService] {message}");
    }

    private UserDataDbService()
    {
        _databasePath = Path.Combine(AppEnv.ConfigPath, "user_data.db");
    }

    /// <summary>
    /// DB 초기화 (테이블 생성)
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            var connectionString = $"Data Source={_databasePath}";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            await CreateTablesAsync(connection);

            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Initialized: {_databasePath}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Initialization failed: {ex.Message}");
            throw;
        }
    }

    private async Task CreateTablesAsync(SqliteConnection connection)
    {
        // First, check and fix ItemInventory schema if needed
        await MigrateItemInventorySchemaAsync(connection);

        var createTablesSql = @"
            -- 퀘스트 진행 상태
            CREATE TABLE IF NOT EXISTS QuestProgress (
                Id TEXT PRIMARY KEY,
                NormalizedName TEXT,
                Status TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            -- 퀘스트 목표 진행 상태
            CREATE TABLE IF NOT EXISTS ObjectiveProgress (
                Id TEXT PRIMARY KEY,
                QuestId TEXT,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );

            -- 아이템 인벤토리
            CREATE TABLE IF NOT EXISTS ItemInventory (
                ItemNormalizedName TEXT PRIMARY KEY,
                FirQuantity INTEGER NOT NULL DEFAULT 0,
                NonFirQuantity INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );

            -- 하이드아웃 진행
            CREATE TABLE IF NOT EXISTS HideoutProgress (
                StationId TEXT PRIMARY KEY,
                Level INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );

            -- 사용자 설정
            CREATE TABLE IF NOT EXISTS UserSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            -- 인덱스
            CREATE INDEX IF NOT EXISTS idx_quest_progress_normalized ON QuestProgress(NormalizedName);
            CREATE INDEX IF NOT EXISTS idx_objective_progress_quest ON ObjectiveProgress(QuestId);
        ";

        await using var cmd = new SqliteCommand(createTablesSql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// ItemInventory 테이블 스키마 마이그레이션 (오래된 스키마 수정)
    /// </summary>
    private async Task MigrateItemInventorySchemaAsync(SqliteConnection connection)
    {
        try
        {
            // Check if ItemInventory table exists
            var checkTableSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ItemInventory'";
            await using var checkCmd = new SqliteCommand(checkTableSql, connection);
            var tableExists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

            if (!tableExists) return;

            // Check if ItemNormalizedName column exists
            var checkColumnSql = "SELECT COUNT(*) FROM pragma_table_info('ItemInventory') WHERE name='ItemNormalizedName'";
            await using var checkColCmd = new SqliteCommand(checkColumnSql, connection);
            var columnExists = Convert.ToInt32(await checkColCmd.ExecuteScalarAsync()) > 0;

            if (!columnExists)
            {
                System.Diagnostics.Debug.WriteLine("[UserDataDbService] Migrating ItemInventory table schema...");

                // Drop the old table and recreate with correct schema
                var migrateSql = @"
                    DROP TABLE IF EXISTS ItemInventory;
                    CREATE TABLE ItemInventory (
                        ItemNormalizedName TEXT PRIMARY KEY,
                        FirQuantity INTEGER NOT NULL DEFAULT 0,
                        NonFirQuantity INTEGER NOT NULL DEFAULT 0,
                        UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );
                ";
                await using var migrateCmd = new SqliteCommand(migrateSql, connection);
                await migrateCmd.ExecuteNonQueryAsync();

                System.Diagnostics.Debug.WriteLine("[UserDataDbService] ItemInventory table migrated successfully");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] ItemInventory schema migration failed: {ex.Message}");
        }
    }

    #region Quest Progress

    /// <summary>
    /// 모든 퀘스트 진행 상태 로드
    /// </summary>
    public async Task<Dictionary<string, QuestStatus>> LoadQuestProgressAsync()
    {
        await InitializeAsync();

        var result = new Dictionary<string, QuestStatus>(StringComparer.OrdinalIgnoreCase);

        var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT Id, NormalizedName, Status FROM QuestProgress";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var normalizedName = reader.IsDBNull(1) ? null : reader.GetString(1);
            var statusStr = reader.GetString(2);

            if (Enum.TryParse<QuestStatus>(statusStr, out var status))
            {
                // NormalizedName을 키로 사용 (기존 호환성)
                var key = normalizedName ?? id;
                result[key] = status;
            }
        }

        return result;
    }

    /// <summary>
    /// 퀘스트 진행 상태 저장
    /// </summary>
    public async Task SaveQuestProgressAsync(string id, string? normalizedName, QuestStatus status)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO QuestProgress (Id, NormalizedName, Status, UpdatedAt)
            VALUES (@id, @normalizedName, @status, @updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                NormalizedName = @normalizedName,
                Status = @status,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@normalizedName", normalizedName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@status", status.ToString());
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 퀘스트 진행 상태 삭제 (리셋)
    /// </summary>
    public async Task DeleteQuestProgressAsync(string id)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM QuestProgress WHERE Id = @id OR NormalizedName = @id";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 퀘스트 진행 상태 삭제
    /// </summary>
    public async Task ClearAllQuestProgressAsync()
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = new SqliteCommand("DELETE FROM QuestProgress", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Objective Progress

    /// <summary>
    /// 모든 목표 진행 상태 로드
    /// </summary>
    public async Task<Dictionary<string, bool>> LoadObjectiveProgressAsync()
    {
        await InitializeAsync();

        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT Id, IsCompleted FROM ObjectiveProgress";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var isCompleted = reader.GetInt32(1) == 1;
            result[id] = isCompleted;
        }

        return result;
    }

    /// <summary>
    /// 목표 진행 상태 저장
    /// </summary>
    public async Task SaveObjectiveProgressAsync(string id, string? questId, bool isCompleted)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO ObjectiveProgress (Id, QuestId, IsCompleted, UpdatedAt)
            VALUES (@id, @questId, @isCompleted, @updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                QuestId = @questId,
                IsCompleted = @isCompleted,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@questId", questId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@isCompleted", isCompleted ? 1 : 0);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 목표 진행 상태 삭제
    /// </summary>
    public async Task DeleteObjectiveProgressAsync(string id)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM ObjectiveProgress WHERE Id = @id";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@id", id);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 퀘스트의 모든 목표 진행 상태 삭제
    /// </summary>
    public async Task DeleteObjectiveProgressByQuestAsync(string questId)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM ObjectiveProgress WHERE QuestId = @questId OR Id LIKE @pattern";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@questId", questId);
        cmd.Parameters.AddWithValue("@pattern", $"{questId}:%");

        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Hideout Progress

    /// <summary>
    /// 모든 하이드아웃 진행 상태 로드
    /// </summary>
    public async Task<Dictionary<string, int>> LoadHideoutProgressAsync()
    {
        await InitializeAsync();

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT StationId, Level FROM HideoutProgress";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var stationId = reader.GetString(0);
            var level = reader.GetInt32(1);
            result[stationId] = level;
        }

        return result;
    }

    /// <summary>
    /// 하이드아웃 진행 상태 저장
    /// </summary>
    public async Task SaveHideoutProgressAsync(string stationId, int level)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // 레벨이 0이면 삭제
        if (level == 0)
        {
            var deleteSql = "DELETE FROM HideoutProgress WHERE StationId = @stationId";
            await using var deleteCmd = new SqliteCommand(deleteSql, connection);
            deleteCmd.Parameters.AddWithValue("@stationId", stationId);
            await deleteCmd.ExecuteNonQueryAsync();
            return;
        }

        var sql = @"
            INSERT INTO HideoutProgress (StationId, Level, UpdatedAt)
            VALUES (@stationId, @level, @updatedAt)
            ON CONFLICT(StationId) DO UPDATE SET
                Level = @level,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@stationId", stationId);
        cmd.Parameters.AddWithValue("@level", level);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 하이드아웃 진행 상태 삭제
    /// </summary>
    public async Task ClearAllHideoutProgressAsync()
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = new SqliteCommand("DELETE FROM HideoutProgress", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region Item Inventory

    /// <summary>
    /// 모든 아이템 인벤토리 로드
    /// </summary>
    public async Task<Dictionary<string, (int FirQuantity, int NonFirQuantity)>> LoadItemInventoryAsync()
    {
        await InitializeAsync();

        var result = new Dictionary<string, (int FirQuantity, int NonFirQuantity)>(StringComparer.OrdinalIgnoreCase);

        var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT ItemNormalizedName, FirQuantity, NonFirQuantity FROM ItemInventory";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var itemName = reader.GetString(0);
            var firQty = reader.GetInt32(1);
            var nonFirQty = reader.GetInt32(2);
            result[itemName] = (firQty, nonFirQty);
        }

        return result;
    }

    /// <summary>
    /// 아이템 인벤토리 저장
    /// </summary>
    public async Task SaveItemInventoryAsync(string itemNormalizedName, int firQuantity, int nonFirQuantity)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // 둘 다 0이면 삭제
        if (firQuantity == 0 && nonFirQuantity == 0)
        {
            var deleteSql = "DELETE FROM ItemInventory WHERE ItemNormalizedName = @itemName";
            await using var deleteCmd = new SqliteCommand(deleteSql, connection);
            deleteCmd.Parameters.AddWithValue("@itemName", itemNormalizedName);
            await deleteCmd.ExecuteNonQueryAsync();
            return;
        }

        var sql = @"
            INSERT INTO ItemInventory (ItemNormalizedName, FirQuantity, NonFirQuantity, UpdatedAt)
            VALUES (@itemName, @firQty, @nonFirQty, @updatedAt)
            ON CONFLICT(ItemNormalizedName) DO UPDATE SET
                FirQuantity = @firQty,
                NonFirQuantity = @nonFirQty,
                UpdatedAt = @updatedAt";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@itemName", itemNormalizedName);
        cmd.Parameters.AddWithValue("@firQty", firQuantity);
        cmd.Parameters.AddWithValue("@nonFirQty", nonFirQuantity);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 아이템 인벤토리 삭제
    /// </summary>
    public async Task ClearAllItemInventoryAsync()
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var cmd = new SqliteCommand("DELETE FROM ItemInventory", connection);
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion

    #region JSON Migration

    /// <summary>
    /// 기존 JSON 파일들을 DB로 마이그레이션
    /// </summary>
    public async Task<bool> MigrateFromJsonAsync()
    {
        if (!NeedsMigration())
        {
            return false;
        }

        ReportProgress("데이터 마이그레이션을 시작합니다...");
        var migrated = false;

        // Quest Progress 마이그레이션
        ReportProgress("퀘스트 진행 데이터 마이그레이션 중...");
        migrated |= await MigrateQuestProgressJsonAsync();

        // Objective Progress 마이그레이션
        ReportProgress("목표 진행 데이터 마이그레이션 중...");
        migrated |= await MigrateObjectiveProgressJsonAsync();

        // Hideout Progress 마이그레이션
        ReportProgress("하이드아웃 진행 데이터 마이그레이션 중...");
        migrated |= await MigrateHideoutProgressJsonAsync();

        // Item Inventory 마이그레이션
        ReportProgress("아이템 인벤토리 데이터 마이그레이션 중...");
        migrated |= await MigrateItemInventoryJsonAsync();

        if (migrated)
        {
            ReportProgress("데이터 마이그레이션 완료!");
        }

        return migrated;
    }

    private async Task<bool> MigrateQuestProgressJsonAsync()
    {
        // V2 파일 먼저 확인
        var v2Path = Path.Combine(AppEnv.ConfigPath, "quest_progress_v2.json");
        var v1Path = Path.Combine(AppEnv.ConfigPath, "quest_progress.json");

        if (File.Exists(v2Path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(v2Path);
                var v2Data = JsonSerializer.Deserialize<QuestProgressDataV2>(json);

                if (v2Data != null)
                {
                    await InitializeAsync();

                    foreach (var entry in v2Data.CompletedQuests)
                    {
                        if (entry.IsValid)
                        {
                            await SaveQuestProgressAsync(
                                entry.Id ?? entry.NormalizedName!,
                                entry.NormalizedName,
                                QuestStatus.Done);
                        }
                    }

                    foreach (var entry in v2Data.FailedQuests)
                    {
                        if (entry.IsValid)
                        {
                            await SaveQuestProgressAsync(
                                entry.Id ?? entry.NormalizedName!,
                                entry.NormalizedName,
                                QuestStatus.Failed);
                        }
                    }

                    // 마이그레이션 완료 후 파일 삭제
                    File.Delete(v2Path);
                    System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {v2Path}");

                    // V1 파일도 있으면 삭제
                    if (File.Exists(v1Path))
                    {
                        File.Delete(v1Path);
                        System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Deleted legacy: {v1Path}");
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] V2 migration failed: {ex.Message}");
            }
        }
        else if (File.Exists(v1Path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(v1Path);
                var v1Data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (v1Data != null)
                {
                    await InitializeAsync();

                    foreach (var kvp in v1Data)
                    {
                        if (Enum.TryParse<QuestStatus>(kvp.Value, out var status))
                        {
                            await SaveQuestProgressAsync(kvp.Key, kvp.Key, status);
                        }
                    }

                    // 마이그레이션 완료 후 파일 삭제
                    File.Delete(v1Path);
                    System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {v1Path}");

                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] V1 migration failed: {ex.Message}");
            }
        }

        return false;
    }

    private async Task<bool> MigrateObjectiveProgressJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "objective_progress.json");

        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);

            if (data != null)
            {
                await InitializeAsync();

                foreach (var kvp in data)
                {
                    // 키 형식: "questName:index" 또는 "id:objectiveId"
                    string? questId = null;
                    if (kvp.Key.Contains(':'))
                    {
                        var parts = kvp.Key.Split(':');
                        if (parts[0] != "id")
                        {
                            questId = parts[0];
                        }
                    }

                    await SaveObjectiveProgressAsync(kvp.Key, questId, kvp.Value);
                }

                // 마이그레이션 완료 후 파일 삭제
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {filePath}");

                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Objective migration failed: {ex.Message}");
        }

        return false;
    }

    private async Task<bool> MigrateHideoutProgressJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "hideout_progress.json");

        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);

            if (data != null)
            {
                await InitializeAsync();

                foreach (var kvp in data)
                {
                    await SaveHideoutProgressAsync(kvp.Key, kvp.Value);
                }

                // 마이그레이션 완료 후 파일 삭제
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {filePath}");

                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Hideout migration failed: {ex.Message}");
        }

        return false;
    }

    private async Task<bool> MigrateItemInventoryJsonAsync()
    {
        var filePath = Path.Combine(AppEnv.ConfigPath, "item_inventory.json");

        if (!File.Exists(filePath))
            return false;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var data = JsonSerializer.Deserialize<ItemInventoryData>(json, options);

            if (data != null && data.Items.Count > 0)
            {
                await InitializeAsync();

                foreach (var kvp in data.Items)
                {
                    var inventory = kvp.Value;
                    await SaveItemInventoryAsync(
                        kvp.Key,
                        inventory.FirQuantity,
                        inventory.NonFirQuantity);
                }

                // 마이그레이션 완료 후 파일 삭제
                File.Delete(filePath);
                System.Diagnostics.Debug.WriteLine($"[UserDataDbService] Migrated and deleted: {filePath}");

                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UserDataDbService] ItemInventory migration failed: {ex.Message}");
        }

        return false;
    }

    #endregion

    #region User Settings

    /// <summary>
    /// 설정 값 조회
    /// </summary>
    public async Task<string?> GetSettingAsync(string key)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT Value FROM UserSettings WHERE Key = @key";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@key", key);

        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    /// <summary>
    /// 설정 값 저장
    /// </summary>
    public async Task SetSettingAsync(string key, string value)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            INSERT INTO UserSettings (Key, Value)
            VALUES (@key, @value)
            ON CONFLICT(Key) DO UPDATE SET Value = @value";

        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 설정 값 삭제
    /// </summary>
    public async Task DeleteSettingAsync(string key)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "DELETE FROM UserSettings WHERE Key = @key";
        await using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@key", key);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 모든 설정 조회
    /// </summary>
    public async Task<Dictionary<string, string>> GetAllSettingsAsync()
    {
        await InitializeAsync();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var sql = "SELECT Key, Value FROM UserSettings";
        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var value = reader.GetString(1);
            result[key] = value;
        }

        return result;
    }

    /// <summary>
    /// 동기 버전: 설정 값 조회 (초기화 시 사용)
    /// </summary>
    public string? GetSetting(string key)
    {
        if (!_isInitialized)
        {
            InitializeAsync().GetAwaiter().GetResult();
        }

        var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var sql = "SELECT Value FROM UserSettings WHERE Key = @key";
        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@key", key);

        var result = cmd.ExecuteScalar();
        return result as string;
    }

    /// <summary>
    /// 동기 버전: 설정 값 저장 (초기화 시 사용)
    /// </summary>
    public void SetSetting(string key, string value)
    {
        if (!_isInitialized)
        {
            InitializeAsync().GetAwaiter().GetResult();
        }

        var connectionString = $"Data Source={_databasePath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var sql = @"
            INSERT INTO UserSettings (Key, Value)
            VALUES (@key, @value)
            ON CONFLICT(Key) DO UPDATE SET Value = @value";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);

        cmd.ExecuteNonQuery();
    }

    #endregion

    #region Batch Operations

    /// <summary>
    /// 여러 퀘스트 진행 상태를 일괄 저장
    /// </summary>
    public async Task SaveQuestProgressBatchAsync(Dictionary<string, QuestStatus> progress,
        Func<string, string?>? getNormalizedName = null)
    {
        await InitializeAsync();

        var connectionString = $"Data Source={_databasePath}";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            var sql = @"
                INSERT INTO QuestProgress (Id, NormalizedName, Status, UpdatedAt)
                VALUES (@id, @normalizedName, @status, @updatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    NormalizedName = @normalizedName,
                    Status = @status,
                    UpdatedAt = @updatedAt";

            foreach (var kvp in progress)
            {
                await using var cmd = new SqliteCommand(sql, connection, (SqliteTransaction)transaction);
                var normalizedName = getNormalizedName?.Invoke(kvp.Key) ?? kvp.Key;

                cmd.Parameters.AddWithValue("@id", kvp.Key);
                cmd.Parameters.AddWithValue("@normalizedName", normalizedName);
                cmd.Parameters.AddWithValue("@status", kvp.Value.ToString());
                cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o"));

                await cmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    #endregion
}
