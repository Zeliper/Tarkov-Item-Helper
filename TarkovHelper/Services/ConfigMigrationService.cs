using System.IO;
using System.Text.Json;
using TarkovHelper.Debug;
using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// 외부 Config 폴더에서 데이터를 마이그레이션하는 서비스
/// (이전 버전 TarkovHelper에서 현재 DB로 데이터 가져오기)
/// </summary>
public sealed class ConfigMigrationService
{
    private static ConfigMigrationService? _instance;
    public static ConfigMigrationService Instance => _instance ??= new ConfigMigrationService();

    // 매핑 실패한 항목 추적
    private List<string> _unmappedQuests = new();
    private List<string> _unmappedHideouts = new();

    private ConfigMigrationService() { }

    /// <summary>
    /// 현재 앱 Config 폴더에 마이그레이션이 필요한 JSON 파일이 있는지 확인
    /// </summary>
    public bool NeedsAutoMigration()
    {
        return IsValidConfigFolder(AppEnv.ConfigPath);
    }

    /// <summary>
    /// 현재 앱 Config 폴더에서 자동 마이그레이션 실행 (앱 시작 시)
    /// </summary>
    public async Task<MigrationResult> MigrateFromCurrentConfigAsync(IProgress<string>? progress = null)
    {
        var result = await MigrateFromConfigFolderAsync(AppEnv.ConfigPath, progress, deleteAfterMigration: true);
        return result;
    }

    /// <summary>
    /// 마이그레이션 결과
    /// </summary>
    public class MigrationResult
    {
        public bool Success { get; set; }
        public int QuestProgressCount { get; set; }
        public int HideoutProgressCount { get; set; }
        public int ItemInventoryCount { get; set; }
        public int SettingsCount { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public int TotalCount => QuestProgressCount + HideoutProgressCount + ItemInventoryCount + SettingsCount;
        public bool HasErrors => Errors.Count > 0;
        public bool HasWarnings => Warnings.Count > 0;
    }

    /// <summary>
    /// Config 폴더가 유효한지 확인
    /// </summary>
    public bool IsValidConfigFolder(string path)
    {
        if (!Directory.Exists(path)) return false;

        // 최소한 하나의 알려진 파일이 있어야 함
        var knownFiles = new[]
        {
            "quest_progress.json",
            "hideout_progress.json",
            "item_inventory.json",
            "app_settings.json"
        };

        return knownFiles.Any(f => File.Exists(Path.Combine(path, f)));
    }

    /// <summary>
    /// Config 폴더에서 어떤 데이터가 있는지 미리보기
    /// </summary>
    public MigrationResult PreviewMigration(string configFolderPath)
    {
        var result = new MigrationResult { Success = true };

        if (!IsValidConfigFolder(configFolderPath))
        {
            result.Success = false;
            result.Errors.Add("Invalid Config folder. No recognized files found.");
            return result;
        }

        // Quest Progress
        var questPath = Path.Combine(configFolderPath, "quest_progress.json");
        if (File.Exists(questPath))
        {
            try
            {
                var json = File.ReadAllText(questPath);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                result.QuestProgressCount = data?.Count ?? 0;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Quest progress file error: {ex.Message}");
            }
        }

        // Hideout Progress
        var hideoutPath = Path.Combine(configFolderPath, "hideout_progress.json");
        if (File.Exists(hideoutPath))
        {
            try
            {
                var json = File.ReadAllText(hideoutPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("modules", out var modulesElement))
                {
                    result.HideoutProgressCount = modulesElement.EnumerateObject().Count();
                }
                else
                {
                    // Old format
                    var data = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                    result.HideoutProgressCount = data?.Count ?? 0;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Hideout progress file error: {ex.Message}");
            }
        }

        // Item Inventory
        var inventoryPath = Path.Combine(configFolderPath, "item_inventory.json");
        if (File.Exists(inventoryPath))
        {
            try
            {
                var json = File.ReadAllText(inventoryPath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("items", out var itemsElement))
                {
                    result.ItemInventoryCount = itemsElement.EnumerateObject().Count();
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Item inventory file error: {ex.Message}");
            }
        }

        // App Settings
        var settingsPath = Path.Combine(configFolderPath, "app_settings.json");
        if (File.Exists(settingsPath))
        {
            try
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = JsonDocument.Parse(json);
                result.SettingsCount = doc.RootElement.EnumerateObject()
                    .Count(p => !p.Value.ValueKind.Equals(JsonValueKind.Null));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Settings file error: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Config 폴더에서 데이터 마이그레이션 실행
    /// </summary>
    /// <param name="configFolderPath">Config 폴더 경로</param>
    /// <param name="progress">진행 상황 보고</param>
    /// <param name="deleteAfterMigration">마이그레이션 후 JSON 파일 삭제 여부</param>
    public async Task<MigrationResult> MigrateFromConfigFolderAsync(
        string configFolderPath,
        IProgress<string>? progress = null,
        bool deleteAfterMigration = false)
    {
        var result = new MigrationResult { Success = true };

        // 매핑 실패 목록 초기화
        _unmappedQuests.Clear();
        _unmappedHideouts.Clear();

        if (!IsValidConfigFolder(configFolderPath))
        {
            result.Success = false;
            result.Errors.Add("Invalid Config folder");
            return result;
        }

        var userDataDb = UserDataDbService.Instance;

        // 1. Quest Progress (NormalizedName → ID 매핑 필요)
        progress?.Report("Migrating quest progress...");
        var questMigrationResult = await MigrateQuestProgressAsync(configFolderPath, userDataDb);
        result.QuestProgressCount = questMigrationResult.count;
        if (questMigrationResult.error != null)
            result.Warnings.Add(questMigrationResult.error);

        // 2. Hideout Progress (NormalizedName 매핑)
        progress?.Report("Migrating hideout progress...");
        var hideoutMigrationResult = await MigrateHideoutProgressAsync(configFolderPath, userDataDb);
        result.HideoutProgressCount = hideoutMigrationResult.count;
        if (hideoutMigrationResult.error != null)
            result.Warnings.Add(hideoutMigrationResult.error);

        // 3. Item Inventory
        progress?.Report("Migrating item inventory...");
        var inventoryMigrationResult = await MigrateItemInventoryAsync(configFolderPath, userDataDb);
        result.ItemInventoryCount = inventoryMigrationResult.count;
        if (inventoryMigrationResult.error != null)
            result.Warnings.Add(inventoryMigrationResult.error);

        // 4. App Settings
        progress?.Report("Migrating settings...");
        var settingsMigrationResult = await MigrateAppSettingsAsync(configFolderPath, userDataDb);
        result.SettingsCount = settingsMigrationResult.count;
        if (settingsMigrationResult.error != null)
            result.Warnings.Add(settingsMigrationResult.error);

        // 매핑 실패 항목 경고 추가
        if (_unmappedQuests.Count > 0)
        {
            var sample = _unmappedQuests.Take(5).ToList();
            var more = _unmappedQuests.Count > 5 ? $" and {_unmappedQuests.Count - 5} more" : "";
            result.Warnings.Add($"Could not match {_unmappedQuests.Count} quest(s): {string.Join(", ", sample)}{more}");
        }

        if (_unmappedHideouts.Count > 0)
        {
            result.Warnings.Add($"Could not match hideout station(s): {string.Join(", ", _unmappedHideouts)}");
        }

        // 마이그레이션 후 JSON 파일 삭제 (자동 마이그레이션 시)
        if (deleteAfterMigration && result.TotalCount > 0)
        {
            DeleteMigratedJsonFiles(configFolderPath);
        }

        progress?.Report("Migration complete!");

        return result;
    }

    /// <summary>
    /// 마이그레이션된 JSON 파일 삭제
    /// </summary>
    private void DeleteMigratedJsonFiles(string configFolderPath)
    {
        var filesToDelete = new[]
        {
            "quest_progress.json",
            "quest_progress_v2.json",
            "objective_progress.json",
            "hideout_progress.json",
            "item_inventory.json",
            "app_settings.json"
        };

        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "migration_log.txt");
        var deleteLog = new System.Text.StringBuilder();
        deleteLog.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Deleting migrated JSON files from: {configFolderPath}");

        foreach (var fileName in filesToDelete)
        {
            try
            {
                var filePath = Path.Combine(configFolderPath, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    deleteLog.AppendLine($"  Deleted: {fileName}");
                    System.Diagnostics.Debug.WriteLine($"[ConfigMigrationService] Deleted: {filePath}");
                }
            }
            catch (Exception ex)
            {
                deleteLog.AppendLine($"  Failed to delete {fileName}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ConfigMigrationService] Failed to delete {fileName}: {ex.Message}");
            }
        }

        deleteLog.AppendLine();
        File.AppendAllText(logPath, deleteLog.ToString());
    }

    private async Task<(int count, string? error)> MigrateQuestProgressAsync(string configFolderPath, UserDataDbService userDataDb)
    {
        var filePath = Path.Combine(configFolderPath, "quest_progress.json");
        if (!File.Exists(filePath))
            return (0, null);

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (data == null || data.Count == 0)
                return (0, null);

            // QuestDbService에서 NormalizedName → Quest 매핑 가져오기
            var questDbService = QuestDbService.Instance;
            var progressItems = new List<(string Id, string? NormalizedName, QuestStatus Status)>();

            foreach (var kvp in data)
            {
                if (!Enum.TryParse<QuestStatus>(kvp.Value, out var status))
                    continue;

                var normalizedName = kvp.Key;

                // NormalizedName으로 퀘스트 찾기
                var quest = questDbService.GetQuestByNormalizedName(normalizedName);

                if (quest != null)
                {
                    // 퀘스트 찾음 - 실제 ID 사용
                    var questId = quest.Ids?.FirstOrDefault() ?? normalizedName;
                    progressItems.Add((questId, normalizedName, status));
                }
                else
                {
                    // 퀘스트를 찾지 못함 - NormalizedName을 ID로 사용 (호환성 유지)
                    // 향후 reconcile에서 매핑 시도
                    progressItems.Add((normalizedName, normalizedName, status));
                    _unmappedQuests.Add(normalizedName);
                }
            }

            if (progressItems.Count > 0)
            {
                await userDataDb.SaveQuestProgressBatchAsync(progressItems);
            }

            return (progressItems.Count - _unmappedQuests.Count, null);
        }
        catch (Exception ex)
        {
            return (0, $"Quest progress migration error: {ex.Message}");
        }
    }

    private async Task<(int count, string? error)> MigrateHideoutProgressAsync(string configFolderPath, UserDataDbService userDataDb)
    {
        var filePath = Path.Combine(configFolderPath, "hideout_progress.json");
        if (!File.Exists(filePath))
            return (0, null);

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            Dictionary<string, int>? modules = null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("modules", out var modulesElement))
            {
                modules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in modulesElement.EnumerateObject())
                {
                    if (prop.Value.TryGetInt32(out var level))
                    {
                        modules[prop.Name] = level;
                    }
                }
            }
            else
            {
                modules = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            }

            if (modules == null || modules.Count == 0)
                return (0, null);

            // HideoutDbService에서 NormalizedName → Station 매핑 가져오기
            var hideoutDbService = HideoutDbService.Instance;
            var allStations = hideoutDbService.AllStations;

            // NormalizedName으로 Station 찾기 위한 룩업 생성
            var stationByNormalizedName = allStations
                .Where(s => !string.IsNullOrEmpty(s.NormalizedName))
                .ToDictionary(s => s.NormalizedName!, s => s, StringComparer.OrdinalIgnoreCase);

            var successCount = 0;

            foreach (var kvp in modules)
            {
                var normalizedName = kvp.Key;
                var level = kvp.Value;

                // NormalizedName으로 스테이션 찾기
                if (stationByNormalizedName.TryGetValue(normalizedName, out var station))
                {
                    // HideoutProgress는 StationId (NormalizedName)를 사용
                    await userDataDb.SaveHideoutProgressAsync(station.NormalizedName!, level);
                    successCount++;
                }
                else
                {
                    // 스테이션을 찾지 못함 - 그대로 저장 시도
                    await userDataDb.SaveHideoutProgressAsync(normalizedName, level);
                    _unmappedHideouts.Add(normalizedName);
                }
            }

            return (successCount, null);
        }
        catch (Exception ex)
        {
            return (0, $"Hideout progress migration error: {ex.Message}");
        }
    }

    private async Task<(int count, string? error)> MigrateItemInventoryAsync(string configFolderPath, UserDataDbService userDataDb)
    {
        var filePath = Path.Combine(configFolderPath, "item_inventory.json");
        if (!File.Exists(filePath))
            return (0, null);

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("items", out var itemsElement))
                return (0, null);

            var count = 0;
            foreach (var prop in itemsElement.EnumerateObject())
            {
                var itemName = prop.Name;
                var firQty = 0;
                var nonFirQty = 0;

                if (prop.Value.TryGetProperty("firQuantity", out var firElement))
                    firQty = firElement.GetInt32();
                if (prop.Value.TryGetProperty("nonFirQuantity", out var nonFirElement))
                    nonFirQty = nonFirElement.GetInt32();

                if (firQty > 0 || nonFirQty > 0)
                {
                    await userDataDb.SaveItemInventoryAsync(itemName, firQty, nonFirQty);
                    count++;
                }
            }

            return (count, null);
        }
        catch (Exception ex)
        {
            return (0, $"Item inventory migration error: {ex.Message}");
        }
    }

    private async Task<(int count, string? error)> MigrateAppSettingsAsync(string configFolderPath, UserDataDbService userDataDb)
    {
        var filePath = Path.Combine(configFolderPath, "app_settings.json");
        if (!File.Exists(filePath))
            return (0, null);

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            using var doc = JsonDocument.Parse(json);

            var count = 0;
            var settingsService = SettingsService.Instance;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Null)
                    continue;

                switch (prop.Name)
                {
                    case "playerLevel":
                        if (prop.Value.TryGetInt32(out var level))
                        {
                            settingsService.PlayerLevel = level;
                            count++;
                        }
                        break;

                    case "scavRep":
                        if (prop.Value.TryGetDouble(out var scavRep))
                        {
                            settingsService.ScavRep = scavRep;
                            count++;
                        }
                        break;

                    case "dspDecodeCount":
                        if (prop.Value.TryGetInt32(out var dspCount))
                        {
                            settingsService.DspDecodeCount = dspCount;
                            count++;
                        }
                        break;

                    case "logFolderPath":
                        var logPath = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(logPath))
                        {
                            settingsService.LogFolderPath = logPath;
                            count++;
                        }
                        break;

                    case "baseFontSize":
                        if (prop.Value.TryGetDouble(out var fontSize))
                        {
                            settingsService.BaseFontSize = fontSize;
                            count++;
                        }
                        break;

                    case "hideWipeWarning":
                        if (prop.Value.TryGetInt32(out var hideWarning) ||
                            (prop.Value.ValueKind == JsonValueKind.True || prop.Value.ValueKind == JsonValueKind.False))
                        {
                            settingsService.HideWipeWarning = prop.Value.ValueKind == JsonValueKind.True || hideWarning == 1;
                            count++;
                        }
                        break;
                }
            }

            return (count, null);
        }
        catch (Exception ex)
        {
            return (0, $"Settings migration error: {ex.Message}");
        }
    }
}
