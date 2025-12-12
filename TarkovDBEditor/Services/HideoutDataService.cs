using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services
{
    /// <summary>
    /// Hideout 데이터를 tarkov.dev API에서 가져와 DB에 저장하는 서비스
    /// </summary>
    public class HideoutDataService : IDisposable
    {
        private readonly TarkovDevDataService _tarkovDevService;
        private readonly string _logDir;
        private readonly string _iconDir;

        public HideoutDataService(string? basePath = null)
        {
            basePath ??= AppDomain.CurrentDomain.BaseDirectory;
            _tarkovDevService = new TarkovDevDataService(Path.Combine(basePath, "wiki_data"));
            _logDir = Path.Combine(basePath, "logs");
            _iconDir = Path.Combine(basePath, "icons", "hideout");

            Directory.CreateDirectory(_logDir);
            Directory.CreateDirectory(_iconDir);
        }

        #region Main Refresh Method

        /// <summary>
        /// Hideout 데이터를 tarkov.dev에서 가져와 DB에 저장
        /// </summary>
        public async Task<HideoutRefreshResult> RefreshHideoutDataAsync(
            string databasePath,
            bool downloadIcons = true,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new HideoutRefreshResult
            {
                StartedAt = DateTime.Now,
                DatabasePath = databasePath
            };

            var logBuilder = new StringBuilder();
            logBuilder.AppendLine($"=== RefreshHideoutData Started at {result.StartedAt:yyyy-MM-dd HH:mm:ss} ===");
            logBuilder.AppendLine($"Database: {databasePath}");
            logBuilder.AppendLine();

            try
            {
                // 1. tarkov.dev에서 Hideout 데이터 가져오기 (캐시 또는 API)
                progress?.Invoke("Loading hideout data from tarkov.dev...");
                var stations = await _tarkovDevService.LoadCachedHideoutAsync(cancellationToken);

                if (stations == null || stations.Count == 0)
                {
                    progress?.Invoke("No cached hideout data found, fetching from API...");
                    stations = await _tarkovDevService.FetchAllHideoutAsync(progress, cancellationToken);
                    await _tarkovDevService.SaveHideoutCacheAsync(stations, cancellationToken);
                }

                logBuilder.AppendLine($"Hideout stations loaded: {stations.Count}");
                result.StationsCount = stations.Count;

                // 2. 아이콘 다운로드 (옵션)
                if (downloadIcons)
                {
                    progress?.Invoke("Downloading hideout icons...");
                    var iconResult = await DownloadHideoutIconsAsync(stations, progress, cancellationToken);
                    result.IconsDownloaded = iconResult.Downloaded;
                    result.IconsFailed = iconResult.Failed;
                    result.IconsCached = iconResult.Cached;
                    logBuilder.AppendLine($"Icons: {iconResult.Downloaded} downloaded, {iconResult.Failed} failed, {iconResult.Cached} cached");
                }

                // 3. DB 데이터 변환
                progress?.Invoke("Converting hideout data to DB format...");
                var dbStations = ConvertToDbStations(stations);
                var dbLevels = ConvertToDbLevels(stations);
                var dbItemReqs = ConvertToDbItemRequirements(stations);
                var dbStationReqs = ConvertToDbStationRequirements(stations);
                var dbTraderReqs = ConvertToDbTraderRequirements(stations);
                var dbSkillReqs = ConvertToDbSkillRequirements(stations);

                logBuilder.AppendLine($"DB Records: {dbStations.Count} stations, {dbLevels.Count} levels");
                logBuilder.AppendLine($"  ItemRequirements: {dbItemReqs.Count}");
                logBuilder.AppendLine($"  StationRequirements: {dbStationReqs.Count}");
                logBuilder.AppendLine($"  TraderRequirements: {dbTraderReqs.Count}");
                logBuilder.AppendLine($"  SkillRequirements: {dbSkillReqs.Count}");

                result.LevelsCount = dbLevels.Count;
                result.ItemRequirementsCount = dbItemReqs.Count;
                result.StationRequirementsCount = dbStationReqs.Count;
                result.TraderRequirementsCount = dbTraderReqs.Count;
                result.SkillRequirementsCount = dbSkillReqs.Count;

                // 4. DB 업데이트
                progress?.Invoke("Updating database...");
                await UpdateHideoutDatabaseAsync(
                    databasePath,
                    dbStations,
                    dbLevels,
                    dbItemReqs,
                    dbStationReqs,
                    dbTraderReqs,
                    dbSkillReqs,
                    logBuilder,
                    progress,
                    cancellationToken);

                result.Success = true;
                result.CompletedAt = DateTime.Now;

                logBuilder.AppendLine();
                logBuilder.AppendLine($"=== RefreshHideoutData Completed at {result.CompletedAt:yyyy-MM-dd HH:mm:ss} ===");
                logBuilder.AppendLine($"Duration: {(result.CompletedAt - result.StartedAt).TotalSeconds:F1} seconds");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.CompletedAt = DateTime.Now;

                logBuilder.AppendLine();
                logBuilder.AppendLine($"=== ERROR ===");
                logBuilder.AppendLine($"Message: {ex.Message}");
                logBuilder.AppendLine($"StackTrace: {ex.StackTrace}");
            }

            // 로그 파일 저장
            var logFileName = $"refresh_hideout_{result.StartedAt:yyyyMMdd_HHmmss}.log";
            var logPath = Path.Combine(_logDir, logFileName);
            await File.WriteAllTextAsync(logPath, logBuilder.ToString(), cancellationToken);
            result.LogPath = logPath;

            return result;
        }

        #endregion

        #region Icon Download

        /// <summary>
        /// Hideout 스테이션 아이콘 다운로드
        /// tarkov.dev의 imageLink를 사용하고, ID를 Base64 인코딩한 파일명으로 저장
        /// </summary>
        private async Task<HideoutIconDownloadResult> DownloadHideoutIconsAsync(
            List<TarkovDevHideoutStation> stations,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new HideoutIconDownloadResult();
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "TarkovDBEditor/1.0");

            var total = stations.Count(s => !string.IsNullOrEmpty(s.ImageLink));
            var current = 0;

            foreach (var station in stations)
            {
                if (string.IsNullOrEmpty(station.ImageLink))
                    continue;

                current++;
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // ID를 Base64 인코딩하여 파일명 생성
                    var encodedId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(station.Id))
                        .Replace("/", "_")
                        .Replace("+", "-")
                        .Replace("=", "");

                    var extension = GetExtensionFromUrl(station.ImageLink);
                    var fileName = $"{encodedId}{extension}";
                    var filePath = Path.Combine(_iconDir, fileName);

                    // 이미 존재하면 스킵
                    if (File.Exists(filePath))
                    {
                        result.Cached++;
                        continue;
                    }

                    progress?.Invoke($"Downloading icon {current}/{total}: {station.Name}");

                    var response = await httpClient.GetAsync(station.ImageLink, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken);
                    result.Downloaded++;
                }
                catch (Exception)
                {
                    result.Failed++;
                }
            }

            return result;
        }

        private static string GetExtensionFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var extension = Path.GetExtension(path);
                return string.IsNullOrEmpty(extension) ? ".png" : extension;
            }
            catch
            {
                return ".png";
            }
        }

        /// <summary>
        /// 스테이션 ID로 아이콘 파일 경로 가져오기
        /// </summary>
        public string? GetIconPath(string stationId)
        {
            var encodedId = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(stationId))
                .Replace("/", "_")
                .Replace("+", "-")
                .Replace("=", "");

            var possibleExtensions = new[] { ".png", ".webp", ".jpg", ".jpeg" };
            foreach (var ext in possibleExtensions)
            {
                var filePath = Path.Combine(_iconDir, $"{encodedId}{ext}");
                if (File.Exists(filePath))
                    return filePath;
            }

            return null;
        }

        #endregion

        #region Data Conversion

        private List<DbHideoutStation> ConvertToDbStations(List<TarkovDevHideoutStation> stations)
        {
            return stations.Select(s => new DbHideoutStation
            {
                Id = s.Id,
                Name = s.Name,
                NameKO = s.NameKo,
                NameJA = s.NameJa,
                NormalizedName = s.NormalizedName,
                ImageLink = s.ImageLink,
                MaxLevel = s.Levels?.Count ?? 0,
                UpdatedAt = DateTime.UtcNow.ToString("o")
            }).ToList();
        }

        private List<DbHideoutLevel> ConvertToDbLevels(List<TarkovDevHideoutStation> stations)
        {
            var result = new List<DbHideoutLevel>();

            foreach (var station in stations)
            {
                if (station.Levels == null) continue;

                foreach (var level in station.Levels)
                {
                    result.Add(new DbHideoutLevel
                    {
                        Id = $"{station.Id}_{level.Level}",
                        StationId = station.Id,
                        Level = level.Level,
                        ConstructionTime = level.ConstructionTime,
                        UpdatedAt = DateTime.UtcNow.ToString("o")
                    });
                }
            }

            return result;
        }

        private List<DbHideoutItemRequirement> ConvertToDbItemRequirements(List<TarkovDevHideoutStation> stations)
        {
            var result = new List<DbHideoutItemRequirement>();
            var sortOrder = 0;

            foreach (var station in stations)
            {
                if (station.Levels == null) continue;

                foreach (var level in station.Levels)
                {
                    if (level.ItemRequirements == null) continue;

                    foreach (var req in level.ItemRequirements)
                    {
                        result.Add(new DbHideoutItemRequirement
                        {
                            Id = $"{station.Id}_{level.Level}_{req.ItemId}",
                            StationId = station.Id,
                            Level = level.Level,
                            ItemId = req.ItemId,
                            ItemName = req.ItemName,
                            ItemNameKO = req.ItemNameKo,
                            ItemNameJA = req.ItemNameJa,
                            IconLink = req.IconLink,
                            Count = req.Count,
                            FoundInRaid = req.FoundInRaid ? 1 : 0,
                            SortOrder = sortOrder++,
                            UpdatedAt = DateTime.UtcNow.ToString("o")
                        });
                    }
                }
            }

            return result;
        }

        private List<DbHideoutStationRequirement> ConvertToDbStationRequirements(List<TarkovDevHideoutStation> stations)
        {
            var result = new List<DbHideoutStationRequirement>();
            var sortOrder = 0;

            foreach (var station in stations)
            {
                if (station.Levels == null) continue;

                foreach (var level in station.Levels)
                {
                    if (level.StationLevelRequirements == null) continue;

                    foreach (var req in level.StationLevelRequirements)
                    {
                        result.Add(new DbHideoutStationRequirement
                        {
                            Id = $"{station.Id}_{level.Level}_{req.StationId}",
                            StationId = station.Id,
                            Level = level.Level,
                            RequiredStationId = req.StationId,
                            RequiredStationName = req.StationName,
                            RequiredStationNameKO = req.StationNameKo,
                            RequiredStationNameJA = req.StationNameJa,
                            RequiredLevel = req.Level,
                            SortOrder = sortOrder++,
                            UpdatedAt = DateTime.UtcNow.ToString("o")
                        });
                    }
                }
            }

            return result;
        }

        private List<DbHideoutTraderRequirement> ConvertToDbTraderRequirements(List<TarkovDevHideoutStation> stations)
        {
            var result = new List<DbHideoutTraderRequirement>();
            var sortOrder = 0;

            foreach (var station in stations)
            {
                if (station.Levels == null) continue;

                foreach (var level in station.Levels)
                {
                    if (level.TraderRequirements == null) continue;

                    foreach (var req in level.TraderRequirements)
                    {
                        result.Add(new DbHideoutTraderRequirement
                        {
                            Id = $"{station.Id}_{level.Level}_{req.TraderId}",
                            StationId = station.Id,
                            Level = level.Level,
                            TraderId = req.TraderId,
                            TraderName = req.TraderName,
                            TraderNameKO = req.TraderNameKo,
                            TraderNameJA = req.TraderNameJa,
                            RequiredLevel = req.Level,
                            SortOrder = sortOrder++,
                            UpdatedAt = DateTime.UtcNow.ToString("o")
                        });
                    }
                }
            }

            return result;
        }

        private List<DbHideoutSkillRequirement> ConvertToDbSkillRequirements(List<TarkovDevHideoutStation> stations)
        {
            var result = new List<DbHideoutSkillRequirement>();
            var sortOrder = 0;

            foreach (var station in stations)
            {
                if (station.Levels == null) continue;

                foreach (var level in station.Levels)
                {
                    if (level.SkillRequirements == null) continue;

                    foreach (var req in level.SkillRequirements)
                    {
                        result.Add(new DbHideoutSkillRequirement
                        {
                            Id = $"{station.Id}_{level.Level}_{req.Name}",
                            StationId = station.Id,
                            Level = level.Level,
                            SkillName = req.Name,
                            SkillNameKO = req.NameKo,
                            SkillNameJA = req.NameJa,
                            RequiredLevel = req.Level,
                            SortOrder = sortOrder++,
                            UpdatedAt = DateTime.UtcNow.ToString("o")
                        });
                    }
                }
            }

            return result;
        }

        #endregion

        #region Database Operations

        private async Task UpdateHideoutDatabaseAsync(
            string databasePath,
            List<DbHideoutStation> stations,
            List<DbHideoutLevel> levels,
            List<DbHideoutItemRequirement> itemReqs,
            List<DbHideoutStationRequirement> stationReqs,
            List<DbHideoutTraderRequirement> traderReqs,
            List<DbHideoutSkillRequirement> skillReqs,
            StringBuilder? logBuilder,
            Action<string>? progress,
            CancellationToken cancellationToken)
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync(cancellationToken);

            using var transaction = connection.BeginTransaction();

            try
            {
                // _schema_meta 테이블 확인/생성
                await EnsureSchemaMetaTableAsync(connection, transaction);

                // HideoutStations 테이블
                progress?.Invoke($"Updating HideoutStations table ({stations.Count} stations)...");
                await CreateHideoutStationsTableAsync(connection, transaction);
                await RegisterHideoutStationsSchemaAsync(connection, transaction);
                var stationStats = await UpsertHideoutStationsAsync(connection, transaction, stations);
                logBuilder?.AppendLine($"HideoutStations - Inserted: {stationStats.Inserted}, Updated: {stationStats.Updated}");

                // HideoutLevels 테이블
                progress?.Invoke($"Updating HideoutLevels table ({levels.Count} levels)...");
                await CreateHideoutLevelsTableAsync(connection, transaction);
                await RegisterHideoutLevelsSchemaAsync(connection, transaction);
                var levelStats = await UpsertHideoutLevelsAsync(connection, transaction, levels);
                logBuilder?.AppendLine($"HideoutLevels - Inserted: {levelStats.Inserted}, Updated: {levelStats.Updated}");

                // HideoutItemRequirements 테이블
                progress?.Invoke($"Updating HideoutItemRequirements table ({itemReqs.Count} items)...");
                await CreateHideoutItemRequirementsTableAsync(connection, transaction);
                await RegisterHideoutItemRequirementsSchemaAsync(connection, transaction);
                var itemStats = await UpsertHideoutItemRequirementsAsync(connection, transaction, itemReqs);
                logBuilder?.AppendLine($"HideoutItemRequirements - Inserted: {itemStats.Inserted}, Updated: {itemStats.Updated}");

                // HideoutStationRequirements 테이블
                progress?.Invoke($"Updating HideoutStationRequirements table ({stationReqs.Count} requirements)...");
                await CreateHideoutStationRequirementsTableAsync(connection, transaction);
                await RegisterHideoutStationRequirementsSchemaAsync(connection, transaction);
                var stationReqStats = await UpsertHideoutStationRequirementsAsync(connection, transaction, stationReqs);
                logBuilder?.AppendLine($"HideoutStationRequirements - Inserted: {stationReqStats.Inserted}, Updated: {stationReqStats.Updated}");

                // HideoutTraderRequirements 테이블
                progress?.Invoke($"Updating HideoutTraderRequirements table ({traderReqs.Count} requirements)...");
                await CreateHideoutTraderRequirementsTableAsync(connection, transaction);
                await RegisterHideoutTraderRequirementsSchemaAsync(connection, transaction);
                var traderStats = await UpsertHideoutTraderRequirementsAsync(connection, transaction, traderReqs);
                logBuilder?.AppendLine($"HideoutTraderRequirements - Inserted: {traderStats.Inserted}, Updated: {traderStats.Updated}");

                // HideoutSkillRequirements 테이블
                progress?.Invoke($"Updating HideoutSkillRequirements table ({skillReqs.Count} requirements)...");
                await CreateHideoutSkillRequirementsTableAsync(connection, transaction);
                await RegisterHideoutSkillRequirementsSchemaAsync(connection, transaction);
                var skillStats = await UpsertHideoutSkillRequirementsAsync(connection, transaction, skillReqs);
                logBuilder?.AppendLine($"HideoutSkillRequirements - Inserted: {skillStats.Inserted}, Updated: {skillStats.Updated}");

                transaction.Commit();
                progress?.Invoke("Hideout database update completed.");
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private async Task EnsureSchemaMetaTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS _schema_meta (
                    TableName TEXT PRIMARY KEY,
                    DisplayName TEXT,
                    SchemaJson TEXT NOT NULL,
                    CreatedAt TEXT DEFAULT CURRENT_TIMESTAMP,
                    UpdatedAt TEXT DEFAULT CURRENT_TIMESTAMP
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();
        }

        #region Table Creation

        private async Task CreateHideoutStationsTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS HideoutStations (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    NameKO TEXT,
                    NameJA TEXT,
                    NormalizedName TEXT,
                    ImageLink TEXT,
                    MaxLevel INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CreateHideoutLevelsTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS HideoutLevels (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    ConstructionTime INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();

            // 인덱스 생성
            var indexSql = "CREATE INDEX IF NOT EXISTS idx_hideoutlevels_stationid ON HideoutLevels(StationId)";
            using var indexCmd = new SqliteCommand(indexSql, connection, transaction);
            await indexCmd.ExecuteNonQueryAsync();
        }

        private async Task CreateHideoutItemRequirementsTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS HideoutItemRequirements (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    ItemId TEXT NOT NULL,
                    ItemName TEXT NOT NULL,
                    ItemNameKO TEXT,
                    ItemNameJA TEXT,
                    IconLink TEXT,
                    Count INTEGER NOT NULL DEFAULT 1,
                    FoundInRaid INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();

            var indexSql = "CREATE INDEX IF NOT EXISTS idx_hideoutitemreq_stationid ON HideoutItemRequirements(StationId, Level)";
            using var indexCmd = new SqliteCommand(indexSql, connection, transaction);
            await indexCmd.ExecuteNonQueryAsync();
        }

        private async Task CreateHideoutStationRequirementsTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS HideoutStationRequirements (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    RequiredStationId TEXT NOT NULL,
                    RequiredStationName TEXT NOT NULL,
                    RequiredStationNameKO TEXT,
                    RequiredStationNameJA TEXT,
                    RequiredLevel INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE,
                    FOREIGN KEY (RequiredStationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();

            var indexSql = "CREATE INDEX IF NOT EXISTS idx_hideoutstationreq_stationid ON HideoutStationRequirements(StationId, Level)";
            using var indexCmd = new SqliteCommand(indexSql, connection, transaction);
            await indexCmd.ExecuteNonQueryAsync();
        }

        private async Task CreateHideoutTraderRequirementsTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS HideoutTraderRequirements (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    TraderId TEXT NOT NULL,
                    TraderName TEXT NOT NULL,
                    TraderNameKO TEXT,
                    TraderNameJA TEXT,
                    RequiredLevel INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();

            var indexSql = "CREATE INDEX IF NOT EXISTS idx_hideouttraderreq_stationid ON HideoutTraderRequirements(StationId, Level)";
            using var indexCmd = new SqliteCommand(indexSql, connection, transaction);
            await indexCmd.ExecuteNonQueryAsync();
        }

        private async Task CreateHideoutSkillRequirementsTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS HideoutSkillRequirements (
                    Id TEXT PRIMARY KEY,
                    StationId TEXT NOT NULL,
                    Level INTEGER NOT NULL,
                    SkillName TEXT NOT NULL,
                    SkillNameKO TEXT,
                    SkillNameJA TEXT,
                    RequiredLevel INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    UpdatedAt TEXT,
                    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();

            var indexSql = "CREATE INDEX IF NOT EXISTS idx_hideoutskillreq_stationid ON HideoutSkillRequirements(StationId, Level)";
            using var indexCmd = new SqliteCommand(indexSql, connection, transaction);
            await indexCmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region Schema Registration

        private async Task RegisterHideoutStationsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, IsRequired = true, SortOrder = 0 },
                new() { Name = "Name", DisplayName = "Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 1 },
                new() { Name = "NameKO", DisplayName = "Name (KO)", Type = ColumnType.Text, SortOrder = 2 },
                new() { Name = "NameJA", DisplayName = "Name (JA)", Type = ColumnType.Text, SortOrder = 3 },
                new() { Name = "NormalizedName", DisplayName = "Normalized Name", Type = ColumnType.Text, SortOrder = 4 },
                new() { Name = "ImageLink", DisplayName = "Image Link", Type = ColumnType.Text, SortOrder = 5 },
                new() { Name = "MaxLevel", DisplayName = "Max Level", Type = ColumnType.Integer, SortOrder = 6 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 7 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "HideoutStations", "Hideout Stations", schemaJson);
        }

        private async Task RegisterHideoutLevelsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, IsRequired = true, SortOrder = 0 },
                new() { Name = "StationId", DisplayName = "Station ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "HideoutStations", ForeignKeyColumn = "Id", SortOrder = 1 },
                new() { Name = "Level", DisplayName = "Level", Type = ColumnType.Integer, IsRequired = true, SortOrder = 2 },
                new() { Name = "ConstructionTime", DisplayName = "Construction Time (s)", Type = ColumnType.Integer, SortOrder = 3 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 4 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "HideoutLevels", "Hideout Levels", schemaJson);
        }

        private async Task RegisterHideoutItemRequirementsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, IsRequired = true, SortOrder = 0 },
                new() { Name = "StationId", DisplayName = "Station ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "HideoutStations", ForeignKeyColumn = "Id", SortOrder = 1 },
                new() { Name = "Level", DisplayName = "Level", Type = ColumnType.Integer, IsRequired = true, SortOrder = 2 },
                new() { Name = "ItemId", DisplayName = "Item ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "Items", ForeignKeyColumn = "Id", SortOrder = 3 },
                new() { Name = "ItemName", DisplayName = "Item Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 4 },
                new() { Name = "ItemNameKO", DisplayName = "Item Name (KO)", Type = ColumnType.Text, SortOrder = 5 },
                new() { Name = "ItemNameJA", DisplayName = "Item Name (JA)", Type = ColumnType.Text, SortOrder = 6 },
                new() { Name = "IconLink", DisplayName = "Icon Link", Type = ColumnType.Text, SortOrder = 7 },
                new() { Name = "Count", DisplayName = "Count", Type = ColumnType.Integer, SortOrder = 8 },
                new() { Name = "FoundInRaid", DisplayName = "Found In Raid", Type = ColumnType.Boolean, SortOrder = 9 },
                new() { Name = "SortOrder", DisplayName = "Sort Order", Type = ColumnType.Integer, SortOrder = 10 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 11 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "HideoutItemRequirements", "Hideout Item Requirements", schemaJson);
        }

        private async Task RegisterHideoutStationRequirementsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, IsRequired = true, SortOrder = 0 },
                new() { Name = "StationId", DisplayName = "Station ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "HideoutStations", ForeignKeyColumn = "Id", SortOrder = 1 },
                new() { Name = "Level", DisplayName = "Level", Type = ColumnType.Integer, IsRequired = true, SortOrder = 2 },
                new() { Name = "RequiredStationId", DisplayName = "Required Station ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "HideoutStations", ForeignKeyColumn = "Id", SortOrder = 3 },
                new() { Name = "RequiredStationName", DisplayName = "Required Station Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 4 },
                new() { Name = "RequiredStationNameKO", DisplayName = "Required Station (KO)", Type = ColumnType.Text, SortOrder = 5 },
                new() { Name = "RequiredStationNameJA", DisplayName = "Required Station (JA)", Type = ColumnType.Text, SortOrder = 6 },
                new() { Name = "RequiredLevel", DisplayName = "Required Level", Type = ColumnType.Integer, SortOrder = 7 },
                new() { Name = "SortOrder", DisplayName = "Sort Order", Type = ColumnType.Integer, SortOrder = 8 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 9 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "HideoutStationRequirements", "Hideout Station Requirements", schemaJson);
        }

        private async Task RegisterHideoutTraderRequirementsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, IsRequired = true, SortOrder = 0 },
                new() { Name = "StationId", DisplayName = "Station ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "HideoutStations", ForeignKeyColumn = "Id", SortOrder = 1 },
                new() { Name = "Level", DisplayName = "Level", Type = ColumnType.Integer, IsRequired = true, SortOrder = 2 },
                new() { Name = "TraderId", DisplayName = "Trader ID", Type = ColumnType.Text, IsRequired = true, SortOrder = 3 },
                new() { Name = "TraderName", DisplayName = "Trader Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 4 },
                new() { Name = "TraderNameKO", DisplayName = "Trader Name (KO)", Type = ColumnType.Text, SortOrder = 5 },
                new() { Name = "TraderNameJA", DisplayName = "Trader Name (JA)", Type = ColumnType.Text, SortOrder = 6 },
                new() { Name = "RequiredLevel", DisplayName = "Required Level", Type = ColumnType.Integer, SortOrder = 7 },
                new() { Name = "SortOrder", DisplayName = "Sort Order", Type = ColumnType.Integer, SortOrder = 8 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 9 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "HideoutTraderRequirements", "Hideout Trader Requirements", schemaJson);
        }

        private async Task RegisterHideoutSkillRequirementsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, IsRequired = true, SortOrder = 0 },
                new() { Name = "StationId", DisplayName = "Station ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "HideoutStations", ForeignKeyColumn = "Id", SortOrder = 1 },
                new() { Name = "Level", DisplayName = "Level", Type = ColumnType.Integer, IsRequired = true, SortOrder = 2 },
                new() { Name = "SkillName", DisplayName = "Skill Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 3 },
                new() { Name = "SkillNameKO", DisplayName = "Skill Name (KO)", Type = ColumnType.Text, SortOrder = 4 },
                new() { Name = "SkillNameJA", DisplayName = "Skill Name (JA)", Type = ColumnType.Text, SortOrder = 5 },
                new() { Name = "RequiredLevel", DisplayName = "Required Level", Type = ColumnType.Integer, SortOrder = 6 },
                new() { Name = "SortOrder", DisplayName = "Sort Order", Type = ColumnType.Integer, SortOrder = 7 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 8 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "HideoutSkillRequirements", "Hideout Skill Requirements", schemaJson);
        }

        private async Task UpsertSchemaMetaAsync(SqliteConnection connection, SqliteTransaction transaction, string tableName, string displayName, string schemaJson)
        {
            var checkSql = "SELECT COUNT(*) FROM _schema_meta WHERE TableName = @TableName";
            using var checkCmd = new SqliteCommand(checkSql, connection, transaction);
            checkCmd.Parameters.AddWithValue("@TableName", tableName);
            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (count == 0)
            {
                var insertSql = @"
                    INSERT INTO _schema_meta (TableName, DisplayName, SchemaJson, CreatedAt, UpdatedAt)
                    VALUES (@TableName, @DisplayName, @SchemaJson, @Now, @Now)";
                using var insertCmd = new SqliteCommand(insertSql, connection, transaction);
                insertCmd.Parameters.AddWithValue("@TableName", tableName);
                insertCmd.Parameters.AddWithValue("@DisplayName", displayName);
                insertCmd.Parameters.AddWithValue("@SchemaJson", schemaJson);
                insertCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
                await insertCmd.ExecuteNonQueryAsync();
            }
            else
            {
                var updateSql = @"
                    UPDATE _schema_meta
                    SET DisplayName = @DisplayName, SchemaJson = @SchemaJson, UpdatedAt = @Now
                    WHERE TableName = @TableName";
                using var updateCmd = new SqliteCommand(updateSql, connection, transaction);
                updateCmd.Parameters.AddWithValue("@TableName", tableName);
                updateCmd.Parameters.AddWithValue("@DisplayName", displayName);
                updateCmd.Parameters.AddWithValue("@SchemaJson", schemaJson);
                updateCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
                await updateCmd.ExecuteNonQueryAsync();
            }
        }

        #endregion

        #region Upsert Operations

        private async Task<HideoutUpsertStats> UpsertHideoutStationsAsync(SqliteConnection connection, SqliteTransaction transaction, List<DbHideoutStation> stations)
        {
            var stats = new HideoutUpsertStats();

            var sql = @"
                INSERT INTO HideoutStations (Id, Name, NameKO, NameJA, NormalizedName, ImageLink, MaxLevel, UpdatedAt)
                VALUES (@Id, @Name, @NameKO, @NameJA, @NormalizedName, @ImageLink, @MaxLevel, @UpdatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = excluded.Name,
                    NameKO = excluded.NameKO,
                    NameJA = excluded.NameJA,
                    NormalizedName = excluded.NormalizedName,
                    ImageLink = excluded.ImageLink,
                    MaxLevel = excluded.MaxLevel,
                    UpdatedAt = excluded.UpdatedAt";

            foreach (var station in stations)
            {
                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", station.Id);
                cmd.Parameters.AddWithValue("@Name", station.Name);
                cmd.Parameters.AddWithValue("@NameKO", (object?)station.NameKO ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NameJA", (object?)station.NameJA ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@NormalizedName", (object?)station.NormalizedName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ImageLink", (object?)station.ImageLink ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MaxLevel", station.MaxLevel);
                cmd.Parameters.AddWithValue("@UpdatedAt", station.UpdatedAt);

                var affected = await cmd.ExecuteNonQueryAsync();
                if (affected > 0) stats.Updated++;
                else stats.Inserted++;
            }

            return stats;
        }

        private async Task<HideoutUpsertStats> UpsertHideoutLevelsAsync(SqliteConnection connection, SqliteTransaction transaction, List<DbHideoutLevel> levels)
        {
            var stats = new HideoutUpsertStats();

            var sql = @"
                INSERT INTO HideoutLevels (Id, StationId, Level, ConstructionTime, UpdatedAt)
                VALUES (@Id, @StationId, @Level, @ConstructionTime, @UpdatedAt)
                ON CONFLICT(Id) DO UPDATE SET
                    StationId = excluded.StationId,
                    Level = excluded.Level,
                    ConstructionTime = excluded.ConstructionTime,
                    UpdatedAt = excluded.UpdatedAt";

            foreach (var level in levels)
            {
                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", level.Id);
                cmd.Parameters.AddWithValue("@StationId", level.StationId);
                cmd.Parameters.AddWithValue("@Level", level.Level);
                cmd.Parameters.AddWithValue("@ConstructionTime", level.ConstructionTime);
                cmd.Parameters.AddWithValue("@UpdatedAt", level.UpdatedAt);

                await cmd.ExecuteNonQueryAsync();
                stats.Updated++;
            }

            return stats;
        }

        private async Task<HideoutUpsertStats> UpsertHideoutItemRequirementsAsync(SqliteConnection connection, SqliteTransaction transaction, List<DbHideoutItemRequirement> requirements)
        {
            var stats = new HideoutUpsertStats();

            // 기존 데이터 삭제 후 새로 삽입 (간단한 동기화)
            var deleteSql = "DELETE FROM HideoutItemRequirements";
            using var deleteCmd = new SqliteCommand(deleteSql, connection, transaction);
            await deleteCmd.ExecuteNonQueryAsync();

            var sql = @"
                INSERT INTO HideoutItemRequirements (Id, StationId, Level, ItemId, ItemName, ItemNameKO, ItemNameJA, IconLink, Count, FoundInRaid, SortOrder, UpdatedAt)
                VALUES (@Id, @StationId, @Level, @ItemId, @ItemName, @ItemNameKO, @ItemNameJA, @IconLink, @Count, @FoundInRaid, @SortOrder, @UpdatedAt)";

            foreach (var req in requirements)
            {
                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", req.Id);
                cmd.Parameters.AddWithValue("@StationId", req.StationId);
                cmd.Parameters.AddWithValue("@Level", req.Level);
                cmd.Parameters.AddWithValue("@ItemId", req.ItemId);
                cmd.Parameters.AddWithValue("@ItemName", req.ItemName);
                cmd.Parameters.AddWithValue("@ItemNameKO", (object?)req.ItemNameKO ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ItemNameJA", (object?)req.ItemNameJA ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@IconLink", (object?)req.IconLink ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Count", req.Count);
                cmd.Parameters.AddWithValue("@FoundInRaid", req.FoundInRaid);
                cmd.Parameters.AddWithValue("@SortOrder", req.SortOrder);
                cmd.Parameters.AddWithValue("@UpdatedAt", req.UpdatedAt);

                await cmd.ExecuteNonQueryAsync();
                stats.Inserted++;
            }

            return stats;
        }

        private async Task<HideoutUpsertStats> UpsertHideoutStationRequirementsAsync(SqliteConnection connection, SqliteTransaction transaction, List<DbHideoutStationRequirement> requirements)
        {
            var stats = new HideoutUpsertStats();

            var deleteSql = "DELETE FROM HideoutStationRequirements";
            using var deleteCmd = new SqliteCommand(deleteSql, connection, transaction);
            await deleteCmd.ExecuteNonQueryAsync();

            var sql = @"
                INSERT INTO HideoutStationRequirements (Id, StationId, Level, RequiredStationId, RequiredStationName, RequiredStationNameKO, RequiredStationNameJA, RequiredLevel, SortOrder, UpdatedAt)
                VALUES (@Id, @StationId, @Level, @RequiredStationId, @RequiredStationName, @RequiredStationNameKO, @RequiredStationNameJA, @RequiredLevel, @SortOrder, @UpdatedAt)";

            foreach (var req in requirements)
            {
                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", req.Id);
                cmd.Parameters.AddWithValue("@StationId", req.StationId);
                cmd.Parameters.AddWithValue("@Level", req.Level);
                cmd.Parameters.AddWithValue("@RequiredStationId", req.RequiredStationId);
                cmd.Parameters.AddWithValue("@RequiredStationName", req.RequiredStationName);
                cmd.Parameters.AddWithValue("@RequiredStationNameKO", (object?)req.RequiredStationNameKO ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RequiredStationNameJA", (object?)req.RequiredStationNameJA ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RequiredLevel", req.RequiredLevel);
                cmd.Parameters.AddWithValue("@SortOrder", req.SortOrder);
                cmd.Parameters.AddWithValue("@UpdatedAt", req.UpdatedAt);

                await cmd.ExecuteNonQueryAsync();
                stats.Inserted++;
            }

            return stats;
        }

        private async Task<HideoutUpsertStats> UpsertHideoutTraderRequirementsAsync(SqliteConnection connection, SqliteTransaction transaction, List<DbHideoutTraderRequirement> requirements)
        {
            var stats = new HideoutUpsertStats();

            var deleteSql = "DELETE FROM HideoutTraderRequirements";
            using var deleteCmd = new SqliteCommand(deleteSql, connection, transaction);
            await deleteCmd.ExecuteNonQueryAsync();

            var sql = @"
                INSERT INTO HideoutTraderRequirements (Id, StationId, Level, TraderId, TraderName, TraderNameKO, TraderNameJA, RequiredLevel, SortOrder, UpdatedAt)
                VALUES (@Id, @StationId, @Level, @TraderId, @TraderName, @TraderNameKO, @TraderNameJA, @RequiredLevel, @SortOrder, @UpdatedAt)";

            foreach (var req in requirements)
            {
                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", req.Id);
                cmd.Parameters.AddWithValue("@StationId", req.StationId);
                cmd.Parameters.AddWithValue("@Level", req.Level);
                cmd.Parameters.AddWithValue("@TraderId", req.TraderId);
                cmd.Parameters.AddWithValue("@TraderName", req.TraderName);
                cmd.Parameters.AddWithValue("@TraderNameKO", (object?)req.TraderNameKO ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@TraderNameJA", (object?)req.TraderNameJA ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RequiredLevel", req.RequiredLevel);
                cmd.Parameters.AddWithValue("@SortOrder", req.SortOrder);
                cmd.Parameters.AddWithValue("@UpdatedAt", req.UpdatedAt);

                await cmd.ExecuteNonQueryAsync();
                stats.Inserted++;
            }

            return stats;
        }

        private async Task<HideoutUpsertStats> UpsertHideoutSkillRequirementsAsync(SqliteConnection connection, SqliteTransaction transaction, List<DbHideoutSkillRequirement> requirements)
        {
            var stats = new HideoutUpsertStats();

            var deleteSql = "DELETE FROM HideoutSkillRequirements";
            using var deleteCmd = new SqliteCommand(deleteSql, connection, transaction);
            await deleteCmd.ExecuteNonQueryAsync();

            var sql = @"
                INSERT INTO HideoutSkillRequirements (Id, StationId, Level, SkillName, SkillNameKO, SkillNameJA, RequiredLevel, SortOrder, UpdatedAt)
                VALUES (@Id, @StationId, @Level, @SkillName, @SkillNameKO, @SkillNameJA, @RequiredLevel, @SortOrder, @UpdatedAt)";

            foreach (var req in requirements)
            {
                using var cmd = new SqliteCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@Id", req.Id);
                cmd.Parameters.AddWithValue("@StationId", req.StationId);
                cmd.Parameters.AddWithValue("@Level", req.Level);
                cmd.Parameters.AddWithValue("@SkillName", req.SkillName);
                cmd.Parameters.AddWithValue("@SkillNameKO", (object?)req.SkillNameKO ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@SkillNameJA", (object?)req.SkillNameJA ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@RequiredLevel", req.RequiredLevel);
                cmd.Parameters.AddWithValue("@SortOrder", req.SortOrder);
                cmd.Parameters.AddWithValue("@UpdatedAt", req.UpdatedAt);

                await cmd.ExecuteNonQueryAsync();
                stats.Inserted++;
            }

            return stats;
        }

        #endregion

        #endregion

        public void Dispose()
        {
            _tarkovDevService.Dispose();
        }
    }

    #region Result Classes

    public class HideoutRefreshResult
    {
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string DatabasePath { get; set; } = "";
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? LogPath { get; set; }

        public int StationsCount { get; set; }
        public int LevelsCount { get; set; }
        public int ItemRequirementsCount { get; set; }
        public int StationRequirementsCount { get; set; }
        public int TraderRequirementsCount { get; set; }
        public int SkillRequirementsCount { get; set; }

        public int IconsDownloaded { get; set; }
        public int IconsFailed { get; set; }
        public int IconsCached { get; set; }
    }

    internal class HideoutIconDownloadResult
    {
        public int Downloaded { get; set; }
        public int Failed { get; set; }
        public int Cached { get; set; }
    }

    internal class HideoutUpsertStats
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Deleted { get; set; }
    }

    #endregion

    #region DB Models

    public class DbHideoutStation
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? NameKO { get; set; }
        public string? NameJA { get; set; }
        public string? NormalizedName { get; set; }
        public string? ImageLink { get; set; }
        public int MaxLevel { get; set; }
        public string? UpdatedAt { get; set; }
    }

    public class DbHideoutLevel
    {
        public string Id { get; set; } = "";
        public string StationId { get; set; } = "";
        public int Level { get; set; }
        public int ConstructionTime { get; set; }
        public string? UpdatedAt { get; set; }
    }

    public class DbHideoutItemRequirement
    {
        public string Id { get; set; } = "";
        public string StationId { get; set; } = "";
        public int Level { get; set; }
        public string ItemId { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string? ItemNameKO { get; set; }
        public string? ItemNameJA { get; set; }
        public string? IconLink { get; set; }
        public int Count { get; set; }
        public int FoundInRaid { get; set; }
        public int SortOrder { get; set; }
        public string? UpdatedAt { get; set; }
    }

    public class DbHideoutStationRequirement
    {
        public string Id { get; set; } = "";
        public string StationId { get; set; } = "";
        public int Level { get; set; }
        public string RequiredStationId { get; set; } = "";
        public string RequiredStationName { get; set; } = "";
        public string? RequiredStationNameKO { get; set; }
        public string? RequiredStationNameJA { get; set; }
        public int RequiredLevel { get; set; }
        public int SortOrder { get; set; }
        public string? UpdatedAt { get; set; }
    }

    public class DbHideoutTraderRequirement
    {
        public string Id { get; set; } = "";
        public string StationId { get; set; } = "";
        public int Level { get; set; }
        public string TraderId { get; set; } = "";
        public string TraderName { get; set; } = "";
        public string? TraderNameKO { get; set; }
        public string? TraderNameJA { get; set; }
        public int RequiredLevel { get; set; }
        public int SortOrder { get; set; }
        public string? UpdatedAt { get; set; }
    }

    public class DbHideoutSkillRequirement
    {
        public string Id { get; set; } = "";
        public string StationId { get; set; } = "";
        public int Level { get; set; }
        public string SkillName { get; set; } = "";
        public string? SkillNameKO { get; set; }
        public string? SkillNameJA { get; set; }
        public int RequiredLevel { get; set; }
        public int SortOrder { get; set; }
        public string? UpdatedAt { get; set; }
    }

    #endregion
}
