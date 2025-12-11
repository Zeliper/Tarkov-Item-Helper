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
    /// Wiki 데이터를 기반으로 .db 파일의 Items, Quests 테이블을 생성/업데이트하는 서비스
    /// Revision 체크를 통해 변경된 데이터만 업데이트하고 로그를 남김
    /// </summary>
    public class RefreshDataService : IDisposable
    {
        private readonly string _wikiDataDir;
        private readonly string _logDir;
        private readonly string _revisionPath;

        public RefreshDataService(string? basePath = null)
        {
            basePath ??= AppDomain.CurrentDomain.BaseDirectory;
            _wikiDataDir = Path.Combine(basePath, "wiki_data");
            _logDir = Path.Combine(basePath, "logs");
            _revisionPath = Path.Combine(_wikiDataDir, "revision.json");

            Directory.CreateDirectory(_wikiDataDir);
            Directory.CreateDirectory(_logDir);
        }

        #region Revision Management

        /// <summary>
        /// 현재 저장된 리비전 정보 로드
        /// </summary>
        public async Task<RevisionInfo> LoadRevisionAsync(CancellationToken cancellationToken = default)
        {
            if (File.Exists(_revisionPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_revisionPath, cancellationToken);
                    return JsonSerializer.Deserialize<RevisionInfo>(json) ?? new RevisionInfo();
                }
                catch
                {
                    return new RevisionInfo();
                }
            }
            return new RevisionInfo();
        }

        /// <summary>
        /// 리비전 정보 저장
        /// </summary>
        public async Task SaveRevisionAsync(RevisionInfo revision, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(revision, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_revisionPath, json, cancellationToken);
        }

        #endregion

        #region Refresh Data

        /// <summary>
        /// Wiki 데이터를 가져와 .db 파일에 Items, Quests 테이블을 생성/업데이트
        /// </summary>
        public async Task<RefreshResult> RefreshDataAsync(
            string databasePath,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new RefreshResult
            {
                StartedAt = DateTime.Now,
                DatabasePath = databasePath
            };

            var logBuilder = new StringBuilder();
            logBuilder.AppendLine($"=== RefreshData Started at {result.StartedAt:yyyy-MM-dd HH:mm:ss} ===");
            logBuilder.AppendLine($"Database: {databasePath}");
            logBuilder.AppendLine();

            try
            {
                // 리비전 정보 로드
                var currentRevision = await LoadRevisionAsync(cancellationToken);
                logBuilder.AppendLine($"Current Revision - Items: {currentRevision.ItemsRevision ?? "N/A"}, Quests: {currentRevision.QuestsRevision ?? "N/A"}");

                // Wiki 데이터 수집 (Items)
                progress?.Invoke("Fetching Wiki item categories...");
                var itemsResult = await FetchAndProcessItemsAsync(progress, cancellationToken);
                logBuilder.AppendLine($"Items fetched: {itemsResult.Items.Count} items");
                logBuilder.AppendLine($"Icons: {itemsResult.IconsDownloaded} downloaded, {itemsResult.IconsFailed} failed, {itemsResult.IconsCached} cached");

                // 실패한 아이콘 다운로드 로깅
                if (itemsResult.FailedIconDownloads.Count > 0)
                {
                    logBuilder.AppendLine();
                    logBuilder.AppendLine($"=== Failed Icon Downloads ({itemsResult.FailedIconDownloads.Count}) ===");
                    foreach (var (wikiId, (url, error)) in itemsResult.FailedIconDownloads.Take(50)) // 최대 50개만 로깅
                    {
                        logBuilder.AppendLine($"  [{wikiId}] {url}");
                        logBuilder.AppendLine($"    Error: {error}");
                    }
                    if (itemsResult.FailedIconDownloads.Count > 50)
                    {
                        logBuilder.AppendLine($"  ... and {itemsResult.FailedIconDownloads.Count - 50} more");
                    }
                }

                // Wiki 데이터 수집 (Quests)
                progress?.Invoke("Fetching Wiki quests...");
                var questsResult = await FetchAndProcessQuestsAsync(itemsResult.Items, progress, cancellationToken);
                logBuilder.AppendLine($"Quests fetched: {questsResult.Quests.Count} quests");

                // 새 리비전 생성
                var newRevision = new RevisionInfo
                {
                    ItemsRevision = itemsResult.Revision,
                    QuestsRevision = questsResult.Revision,
                    LastUpdated = DateTime.UtcNow
                };

                // 리비전 비교 (로그용)
                bool itemsChanged = currentRevision.ItemsRevision != newRevision.ItemsRevision;
                bool questsChanged = currentRevision.QuestsRevision != newRevision.QuestsRevision;

                logBuilder.AppendLine();
                logBuilder.AppendLine($"New Revision - Items: {newRevision.ItemsRevision}, Quests: {newRevision.QuestsRevision}");
                logBuilder.AppendLine($"Items Changed: {itemsChanged}, Quests Changed: {questsChanged}");

                // DB는 항상 초기화 및 업데이트 (Items, Quests, QuestRequirements, QuestObjectives 테이블)
                progress?.Invoke("Updating database (Items, Quests, QuestRequirements & QuestObjectives tables)...");
                await UpdateDatabaseAsync(
                    databasePath,
                    itemsResult.Items,
                    questsResult.Quests,
                    questsResult.Requirements,
                    questsResult.Objectives,
                    logBuilder,
                    progress,
                    cancellationToken);

                result.ItemsUpdated = true;
                result.QuestsUpdated = true;
                result.ItemsCount = itemsResult.Items.Count;
                result.QuestsCount = questsResult.Quests.Count;

                // 리비전 저장
                await SaveRevisionAsync(newRevision, cancellationToken);
                logBuilder.AppendLine();
                logBuilder.AppendLine("Revision info saved.");

                result.Success = true;
                result.CompletedAt = DateTime.Now;

                logBuilder.AppendLine();
                logBuilder.AppendLine($"=== RefreshData Completed at {result.CompletedAt:yyyy-MM-dd HH:mm:ss} ===");
                logBuilder.AppendLine($"Duration: {(result.CompletedAt - result.StartedAt).TotalSeconds:F1} seconds");
                logBuilder.AppendLine($"Items Updated: {result.ItemsUpdated} ({result.ItemsCount} items)");
                logBuilder.AppendLine($"Quests Updated: {result.QuestsUpdated} ({result.QuestsCount} quests)");
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
            var logFileName = $"refresh_{result.StartedAt:yyyyMMdd_HHmmss}.log";
            var logPath = Path.Combine(_logDir, logFileName);
            await File.WriteAllTextAsync(logPath, logBuilder.ToString(), cancellationToken);
            result.LogPath = logPath;

            return result;
        }

        /// <summary>
        /// Wiki에서 아이템 데이터 수집 및 처리
        /// </summary>
        private async Task<ItemsFetchResult> FetchAndProcessItemsAsync(
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var wikiService = new TarkovWikiDataService();
            using var cacheService = new WikiCacheService(_wikiDataDir);

            // 캐시 로드
            await cacheService.LoadCacheAsync();

            // 제외할 아이템 가져오기
            var excludedItems = await wikiService.GetExcludedItemsAsync(progress);

            // 카테고리 데이터 가져오기
            var (categoryResult, tree, allCategoryDirectItems) = await wikiService.ExportAllCategoryDataAsync(progress);

            // 카테고리 구조 빌드
            var structure = wikiService.BuildCategoryStructure(tree, allCategoryDirectItems);

            // 모든 후보 아이템
            var allCandidateItems = structure.LeafCategories
                .SelectMany(lc => lc.Value.Items)
                .Distinct()
                .ToList();

            // 페이지 캐시 업데이트
            progress?.Invoke("Updating page cache...");
            var cacheUpdateResult = await cacheService.UpdatePageCacheAsync(allCandidateItems, progress);

            // Infobox 없는 페이지 필터링
            var pagesWithoutInfobox = cacheService.GetPagesWithoutInfoboxFromCache(allCandidateItems);

            // 아이템 목록 빌드
            var itemList = wikiService.BuildItemList(structure, tree, excludedItems, pagesWithoutInfobox);

            // 아이콘 URL 가져오기
            var itemNames = itemList.Items.Select(i => i.Name).ToList();
            var iconUrls = await cacheService.GetIconUrlsAsync(itemNames, progress);
            foreach (var item in itemList.Items)
            {
                if (iconUrls.TryGetValue(item.Name, out var iconUrl))
                {
                    item.IconUrl = iconUrl;
                }
            }

            // 아이콘 이미지 다운로드 (캐시에 없는 것만)
            progress?.Invoke("Downloading missing icon images...");
            var iconItems = itemList.Items
                .Where(i => !string.IsNullOrEmpty(i.IconUrl))
                .Select(i => (i.Id, i.IconUrl))
                .ToList();
            var downloadResult = await cacheService.DownloadIconsAsync(iconItems, progress, cancellationToken);
            progress?.Invoke($"Icons: {downloadResult.Downloaded} downloaded, {downloadResult.Failed} failed, {downloadResult.AlreadyDownloaded} cached");

            // tarkov.dev 데이터로 enrichment
            progress?.Invoke("Enriching with tarkov.dev data...");
            using var devService = new TarkovDevDataService();
            var devItems = await devService.FetchAllLanguagesAsync(progress, cancellationToken);

            var enrichedItems = new List<DbItem>();
            foreach (var item in itemList.Items)
            {
                var dbItem = new DbItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    WikiPageLink = item.WikiPageLink,
                    IconUrl = item.IconUrl,
                    Category = item.Category,
                    Categories = string.Join("|", item.Categories)
                };

                // tarkov.dev 매칭
                var normalizedLink = NormalizeWikiLink(item.WikiPageLink);
                if (!string.IsNullOrEmpty(normalizedLink) && devItems.TryGetValue(normalizedLink, out var devItem))
                {
                    dbItem.BsgId = devItem.BsgId;
                    dbItem.NameEN = devItem.NameEN;
                    dbItem.NameKO = devItem.NameKO;
                    dbItem.NameJA = devItem.NameJA;
                    dbItem.ShortNameEN = devItem.ShortNameEN;
                    dbItem.ShortNameKO = devItem.ShortNameKO;
                    dbItem.ShortNameJA = devItem.ShortNameJA;
                }
                else
                {
                    dbItem.NameEN = item.Name;
                    dbItem.NameKO = item.Name;
                    dbItem.NameJA = item.Name;
                }

                enrichedItems.Add(dbItem);
            }

            // 실패한 다운로드 정보 가져오기
            var failedDownloads = cacheService.GetAndClearFailedDownloads();

            // 캐시 저장
            await cacheService.SaveCacheAsync();

            // 리비전 생성 (아이템 수 + 최종 수정 시간 해시)
            var revision = $"{enrichedItems.Count}_{DateTime.UtcNow:yyyyMMddHH}";

            return new ItemsFetchResult
            {
                Items = enrichedItems,
                Revision = revision,
                IconsDownloaded = downloadResult.Downloaded,
                IconsFailed = downloadResult.Failed,
                IconsCached = downloadResult.AlreadyDownloaded,
                FailedIconDownloads = failedDownloads
            };
        }

        /// <summary>
        /// Wiki에서 퀘스트 데이터 수집 및 처리
        /// </summary>
        private async Task<QuestsFetchResult> FetchAndProcessQuestsAsync(
            List<DbItem> items,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // 아이템 이름 -> ID 매핑 (Objective의 ItemName을 ItemId로 변환용)
            var itemNameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                // Wiki Name으로 매핑
                if (!string.IsNullOrEmpty(item.Name) && !itemNameToId.ContainsKey(item.Name))
                    itemNameToId[item.Name] = item.Id;
                // NameEN으로도 매핑 (다국어 지원)
                if (!string.IsNullOrEmpty(item.NameEN) && !itemNameToId.ContainsKey(item.NameEN))
                    itemNameToId[item.NameEN] = item.Id;
            }
            using var questService = new WikiQuestService(_wikiDataDir);

            // 캐시 로드
            await questService.LoadCacheAsync();

            // 퀘스트 목록 가져오기
            var questPages = await questService.GetAllQuestPagesAsync(progress, cancellationToken);

            // 퀘스트 캐시 업데이트
            progress?.Invoke("Updating quest cache...");
            await questService.UpdateQuestCacheAsync(questPages, progress);

            // 캐시 저장
            await questService.SaveCacheAsync();

            // 캐시에서 Trader 정보 가져오기
            var cachedQuests = questService.GetCachedQuests();

            // tarkov.dev 데이터 가져오기
            progress?.Invoke("Fetching tarkov.dev quest data...");
            var devQuests = await questService.FetchTarkovDevQuestsAsync(progress, cancellationToken);

            // 퀘스트 매칭 및 DB 데이터 생성
            var dbQuests = new List<DbQuest>();
            var devQuestsByNormalizedName = devQuests.Values
                .Where(q => !string.IsNullOrEmpty(q.NormalizedName))
                .ToDictionary(q => q.NormalizedName!, q => q, StringComparer.OrdinalIgnoreCase);

            // 퀘스트 이름 -> ID 매핑 (requirements 파싱용)
            var questNameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var questName in questPages)
            {
                var encodedName = Uri.EscapeDataString(questName.Replace(" ", "_"))
                    .Replace("%28", "(").Replace("%29", ")");
                var wikiPageLink = $"https://escapefromtarkov.fandom.com/wiki/{encodedName}";
                var id = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(wikiPageLink));

                questNameToId[questName] = id;

                var dbQuest = new DbQuest
                {
                    Id = id,
                    Name = questName,
                    WikiPageLink = wikiPageLink
                };

                // 캐시에서 Trader (givenby), MinLevel, MinScavKarma 가져오기
                if (cachedQuests.TryGetValue(questName, out var cached))
                {
                    // 캐시된 Trader가 있으면 사용, 없으면 PageContent에서 직접 파싱
                    var trader = cached.Trader;
                    if (string.IsNullOrEmpty(trader) && !string.IsNullOrEmpty(cached.PageContent))
                    {
                        trader = ExtractTraderFromContent(cached.PageContent);
                    }
                    dbQuest.Trader = trader;

                    // MinLevel, MinScavKarma - 캐시에 있으면 사용, 없으면 PageContent에서 파싱
                    dbQuest.MinLevel = cached.MinLevel ?? WikiQuestService.ExtractMinLevel(cached.PageContent ?? "");
                    dbQuest.MinScavKarma = cached.MinScavKarma ?? WikiQuestService.ExtractMinScavKarma(cached.PageContent ?? "");
                }

                // tarkov.dev 매칭
                TarkovDevQuest? devQuest = null;
                if (devQuests.TryGetValue(wikiPageLink, out devQuest) ||
                    devQuestsByNormalizedName.TryGetValue(NormalizeQuestName(questName), out devQuest))
                {
                    dbQuest.BsgId = devQuest.Id;
                    dbQuest.NameEN = devQuest.NameEN;
                    dbQuest.NameKO = devQuest.NameKO;
                    dbQuest.NameJA = devQuest.NameJA;
                    // Trader는 캐시에서 이미 설정됨 (Wiki givenby 우선)
                }
                else
                {
                    dbQuest.NameEN = questName;
                    dbQuest.NameKO = questName;
                    dbQuest.NameJA = questName;
                }

                dbQuests.Add(dbQuest);
            }

            // 퀘스트 선행 조건(requirements) 파싱
            progress?.Invoke("Parsing quest requirements...");
            var dbRequirements = new List<DbQuestRequirement>();

            foreach (var questName in questPages)
            {
                if (!questNameToId.TryGetValue(questName, out var questId))
                    continue;

                if (!cachedQuests.TryGetValue(questName, out var cached) || string.IsNullOrEmpty(cached.PageContent))
                    continue;

                var parsedReqs = WikiQuestService.ExtractPreviousQuests(cached.PageContent);

                foreach (var req in parsedReqs)
                {
                    // 선행 퀘스트 이름으로 ID 찾기
                    if (!questNameToId.TryGetValue(req.QuestName, out var requiredQuestId))
                    {
                        // (quest) 접미사 추가해서 다시 시도
                        if (!questNameToId.TryGetValue($"{req.QuestName} (quest)", out requiredQuestId))
                            continue; // 매칭 실패 - 스킵
                    }

                    dbRequirements.Add(new DbQuestRequirement
                    {
                        QuestId = questId,
                        RequiredQuestId = requiredQuestId,
                        RequirementType = req.RequirementType,
                        DelayMinutes = req.DelayMinutes,
                        GroupId = req.GroupId
                    });
                }
            }

            progress?.Invoke($"Parsed {dbRequirements.Count} quest requirements");

            // 퀘스트 목표(objectives) 파싱
            progress?.Invoke("Parsing quest objectives...");
            var dbObjectives = new List<DbQuestObjective>();

            foreach (var questName in questPages)
            {
                if (!questNameToId.TryGetValue(questName, out var questId))
                    continue;

                if (!cachedQuests.TryGetValue(questName, out var cached) || string.IsNullOrEmpty(cached.PageContent))
                    continue;

                var parsedObjs = WikiQuestService.ExtractObjectives(cached.PageContent);

                foreach (var obj in parsedObjs)
                {
                    // ItemName으로 ItemId 매핑
                    string? itemId = null;
                    if (!string.IsNullOrEmpty(obj.ItemName))
                    {
                        itemNameToId.TryGetValue(obj.ItemName, out itemId);
                    }

                    dbObjectives.Add(new DbQuestObjective
                    {
                        QuestId = questId,
                        SortOrder = obj.SortOrder,
                        ObjectiveType = obj.Type.ToString(),
                        Description = obj.Description,
                        TargetType = obj.TargetType,
                        TargetCount = obj.TargetCount,
                        ItemId = itemId,
                        ItemName = obj.ItemName,
                        RequiresFIR = obj.RequiresFIR,
                        MapName = obj.MapName,
                        LocationName = obj.LocationName,
                        Conditions = obj.Conditions
                    });
                }
            }

            progress?.Invoke($"Parsed {dbObjectives.Count} quest objectives");

            // 리비전 생성
            var revision = $"{dbQuests.Count}_{DateTime.UtcNow:yyyyMMddHH}";

            return new QuestsFetchResult
            {
                Quests = dbQuests,
                Requirements = dbRequirements,
                Objectives = dbObjectives,
                Revision = revision
            };
        }

        /// <summary>
        /// 데이터베이스 업데이트
        /// </summary>
        private async Task UpdateDatabaseAsync(
            string databasePath,
            List<DbItem>? items,
            List<DbQuest>? quests,
            List<DbQuestRequirement>? questRequirements,
            List<DbQuestObjective>? questObjectives,
            StringBuilder logBuilder,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            await connection.OpenAsync(cancellationToken);

            using var transaction = connection.BeginTransaction();

            try
            {
                // _schema_meta 테이블 확인/생성
                await EnsureSchemaMetaTableAsync(connection, transaction);

                // Items 테이블 업데이트
                if (items != null && items.Count > 0)
                {
                    progress?.Invoke($"Updating Items table ({items.Count} items)...");
                    logBuilder.AppendLine();
                    logBuilder.AppendLine($"=== Items Table Update ===");

                    await CreateItemsTableIfNotExistsAsync(connection, transaction);
                    await RegisterItemsSchemaAsync(connection, transaction);
                    var itemStats = await UpsertItemsAsync(connection, transaction, items, logBuilder);

                    logBuilder.AppendLine($"Inserted: {itemStats.Inserted}, Updated: {itemStats.Updated}, Deleted: {itemStats.Deleted}");
                }

                // Quests 테이블 업데이트
                if (quests != null && quests.Count > 0)
                {
                    progress?.Invoke($"Updating Quests table ({quests.Count} quests)...");
                    logBuilder.AppendLine();
                    logBuilder.AppendLine($"=== Quests Table Update ===");

                    await CreateQuestsTableIfNotExistsAsync(connection, transaction);
                    await RegisterQuestsSchemaAsync(connection, transaction);
                    var questStats = await UpsertQuestsAsync(connection, transaction, quests, logBuilder);

                    logBuilder.AppendLine($"Inserted: {questStats.Inserted}, Updated: {questStats.Updated}, Deleted: {questStats.Deleted}");
                }

                // QuestRequirements 테이블 업데이트
                if (questRequirements != null && questRequirements.Count > 0)
                {
                    progress?.Invoke($"Updating QuestRequirements table ({questRequirements.Count} requirements)...");
                    logBuilder.AppendLine();
                    logBuilder.AppendLine($"=== QuestRequirements Table Update ===");

                    await CreateQuestRequirementsTableIfNotExistsAsync(connection, transaction);
                    await RegisterQuestRequirementsSchemaAsync(connection, transaction);
                    var reqStats = await UpsertQuestRequirementsAsync(connection, transaction, questRequirements, logBuilder);

                    logBuilder.AppendLine($"Inserted: {reqStats.Inserted}, Updated: {reqStats.Updated}, Deleted: {reqStats.Deleted}");
                }

                // QuestObjectives 테이블 업데이트
                if (questObjectives != null && questObjectives.Count > 0)
                {
                    progress?.Invoke($"Updating QuestObjectives table ({questObjectives.Count} objectives)...");
                    logBuilder.AppendLine();
                    logBuilder.AppendLine($"=== QuestObjectives Table Update ===");

                    await CreateQuestObjectivesTableIfNotExistsAsync(connection, transaction);
                    await RegisterQuestObjectivesSchemaAsync(connection, transaction);
                    var objStats = await UpsertQuestObjectivesAsync(connection, transaction, questObjectives, logBuilder);

                    logBuilder.AppendLine($"Inserted: {objStats.Inserted}, Updated: {objStats.Updated}, Deleted: {objStats.Deleted}");
                }

                transaction.Commit();
                progress?.Invoke("Database update completed.");
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

        private async Task RegisterItemsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, IsRequired = true, SortOrder = 0 },
                new() { Name = "BsgId", DisplayName = "BSG ID", Type = ColumnType.Text, SortOrder = 1 },
                new() { Name = "Name", DisplayName = "Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 2 },
                new() { Name = "NameEN", DisplayName = "Name (EN)", Type = ColumnType.Text, SortOrder = 3 },
                new() { Name = "NameKO", DisplayName = "Name (KO)", Type = ColumnType.Text, SortOrder = 4 },
                new() { Name = "NameJA", DisplayName = "Name (JA)", Type = ColumnType.Text, SortOrder = 5 },
                new() { Name = "ShortNameEN", DisplayName = "Short (EN)", Type = ColumnType.Text, SortOrder = 6 },
                new() { Name = "ShortNameKO", DisplayName = "Short (KO)", Type = ColumnType.Text, SortOrder = 7 },
                new() { Name = "ShortNameJA", DisplayName = "Short (JA)", Type = ColumnType.Text, SortOrder = 8 },
                new() { Name = "WikiPageLink", DisplayName = "Wiki Link", Type = ColumnType.Text, SortOrder = 9 },
                new() { Name = "IconUrl", DisplayName = "Icon URL", Type = ColumnType.Text, SortOrder = 10 },
                new() { Name = "Category", DisplayName = "Category", Type = ColumnType.Text, SortOrder = 11 },
                new() { Name = "Categories", DisplayName = "Categories", Type = ColumnType.Text, SortOrder = 12 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 13 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "Items", "Items", schemaJson);
        }

        private async Task RegisterQuestsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, IsRequired = true, SortOrder = 0 },
                new() { Name = "BsgId", DisplayName = "BSG ID", Type = ColumnType.Text, SortOrder = 1 },
                new() { Name = "Name", DisplayName = "Name", Type = ColumnType.Text, IsRequired = true, SortOrder = 2 },
                new() { Name = "NameEN", DisplayName = "Name (EN)", Type = ColumnType.Text, SortOrder = 3 },
                new() { Name = "NameKO", DisplayName = "Name (KO)", Type = ColumnType.Text, SortOrder = 4 },
                new() { Name = "NameJA", DisplayName = "Name (JA)", Type = ColumnType.Text, SortOrder = 5 },
                new() { Name = "WikiPageLink", DisplayName = "Wiki Link", Type = ColumnType.Text, SortOrder = 6 },
                new() { Name = "Trader", DisplayName = "Trader", Type = ColumnType.Text, SortOrder = 7 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 8 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "Quests", "Quests", schemaJson);
        }

        private async Task UpsertSchemaMetaAsync(SqliteConnection connection, SqliteTransaction transaction, string tableName, string displayName, string schemaJson)
        {
            // Check if exists
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
                    UPDATE _schema_meta SET SchemaJson = @SchemaJson, UpdatedAt = @Now
                    WHERE TableName = @TableName";
                using var updateCmd = new SqliteCommand(updateSql, connection, transaction);
                updateCmd.Parameters.AddWithValue("@TableName", tableName);
                updateCmd.Parameters.AddWithValue("@SchemaJson", schemaJson);
                updateCmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("o"));
                await updateCmd.ExecuteNonQueryAsync();
            }
        }

        private async Task CreateItemsTableIfNotExistsAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS Items (
                    Id TEXT PRIMARY KEY,
                    BsgId TEXT,
                    Name TEXT NOT NULL,
                    NameEN TEXT,
                    NameKO TEXT,
                    NameJA TEXT,
                    ShortNameEN TEXT,
                    ShortNameKO TEXT,
                    ShortNameJA TEXT,
                    WikiPageLink TEXT,
                    IconUrl TEXT,
                    Category TEXT,
                    Categories TEXT,
                    UpdatedAt TEXT
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();
        }

        private async Task CreateQuestsTableIfNotExistsAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var sql = @"
                CREATE TABLE IF NOT EXISTS Quests (
                    Id TEXT PRIMARY KEY,
                    BsgId TEXT,
                    Name TEXT NOT NULL,
                    NameEN TEXT,
                    NameKO TEXT,
                    NameJA TEXT,
                    WikiPageLink TEXT,
                    Trader TEXT,
                    MinLevel INTEGER,
                    MinLevelApproved INTEGER NOT NULL DEFAULT 0,
                    MinLevelApprovedAt TEXT,
                    MinScavKarma INTEGER,
                    MinScavKarmaApproved INTEGER NOT NULL DEFAULT 0,
                    MinScavKarmaApprovedAt TEXT,
                    UpdatedAt TEXT
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();

            // 기존 테이블에 새 컬럼 추가 (이미 있으면 무시)
            var newColumns = new[]
            {
                "ALTER TABLE Quests ADD COLUMN MinLevel INTEGER",
                "ALTER TABLE Quests ADD COLUMN MinLevelApproved INTEGER NOT NULL DEFAULT 0",
                "ALTER TABLE Quests ADD COLUMN MinLevelApprovedAt TEXT",
                "ALTER TABLE Quests ADD COLUMN MinScavKarma INTEGER",
                "ALTER TABLE Quests ADD COLUMN MinScavKarmaApproved INTEGER NOT NULL DEFAULT 0",
                "ALTER TABLE Quests ADD COLUMN MinScavKarmaApprovedAt TEXT"
            };

            foreach (var alterSql in newColumns)
            {
                try
                {
                    using var alterCmd = new SqliteCommand(alterSql, connection, transaction);
                    await alterCmd.ExecuteNonQueryAsync();
                }
                catch { /* 이미 존재 */ }
            }
        }

        private async Task CreateQuestRequirementsTableIfNotExistsAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            // 기존 auto-increment 테이블이 있으면 마이그레이션
            await MigrateQuestRequirementsTableAsync(connection, transaction);

            var sql = @"
                CREATE TABLE IF NOT EXISTS QuestRequirements (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    RequiredQuestId TEXT NOT NULL,
                    RequirementType TEXT NOT NULL DEFAULT 'Complete',
                    DelayMinutes INTEGER,
                    GroupId INTEGER NOT NULL DEFAULT 0,
                    ContentHash TEXT,
                    IsApproved INTEGER NOT NULL DEFAULT 0,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
                    FOREIGN KEY (RequiredQuestId) REFERENCES Quests(Id) ON DELETE CASCADE
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();

            // 인덱스 생성
            var indexSql = @"
                CREATE INDEX IF NOT EXISTS idx_questreq_questid ON QuestRequirements(QuestId);
                CREATE INDEX IF NOT EXISTS idx_questreq_requiredid ON QuestRequirements(RequiredQuestId)";
            using var indexCmd = new SqliteCommand(indexSql, connection, transaction);
            await indexCmd.ExecuteNonQueryAsync();
        }

        private async Task MigrateQuestRequirementsTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            // 테이블이 존재하고 Id가 INTEGER 타입이면 마이그레이션 필요
            try
            {
                using var checkCmd = new SqliteCommand("PRAGMA table_info(QuestRequirements)", connection, transaction);
                using var reader = await checkCmd.ExecuteReaderAsync();
                bool needsMigration = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    var colType = reader.GetString(2);
                    if (colName == "Id" && colType.ToUpper() == "INTEGER")
                    {
                        needsMigration = true;
                        break;
                    }
                }
                reader.Close();

                if (needsMigration)
                {
                    // 기존 테이블 삭제 (새 스키마로 재생성)
                    using var dropCmd = new SqliteCommand("DROP TABLE IF EXISTS QuestRequirements", connection, transaction);
                    await dropCmd.ExecuteNonQueryAsync();
                }
            }
            catch { /* 테이블이 없으면 무시 */ }
        }

        private async Task RegisterQuestRequirementsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, SortOrder = 0 },
                new() { Name = "QuestId", DisplayName = "Quest ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "Quests", ForeignKeyColumn = "Id", SortOrder = 1 },
                new() { Name = "RequiredQuestId", DisplayName = "Required Quest ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "Quests", ForeignKeyColumn = "Id", SortOrder = 2 },
                new() { Name = "RequirementType", DisplayName = "Type", Type = ColumnType.Text, IsRequired = true, SortOrder = 3 },
                new() { Name = "DelayMinutes", DisplayName = "Delay (min)", Type = ColumnType.Integer, SortOrder = 4 },
                new() { Name = "GroupId", DisplayName = "Group ID", Type = ColumnType.Integer, IsRequired = true, SortOrder = 5 },
                new() { Name = "ContentHash", DisplayName = "Content Hash", Type = ColumnType.Text, SortOrder = 6 },
                new() { Name = "IsApproved", DisplayName = "Approved", Type = ColumnType.Boolean, IsRequired = true, SortOrder = 7 },
                new() { Name = "ApprovedAt", DisplayName = "Approved At", Type = ColumnType.DateTime, SortOrder = 8 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 9 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "QuestRequirements", "Quest Requirements", schemaJson);
        }

        private async Task CreateQuestObjectivesTableIfNotExistsAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            // 기존 auto-increment 테이블이 있으면 마이그레이션
            await MigrateQuestObjectivesTableAsync(connection, transaction);

            var sql = @"
                CREATE TABLE IF NOT EXISTS QuestObjectives (
                    Id TEXT PRIMARY KEY,
                    QuestId TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    ObjectiveType TEXT NOT NULL DEFAULT 'Custom',
                    Description TEXT NOT NULL,
                    TargetType TEXT,
                    TargetCount INTEGER,
                    ItemId TEXT,
                    ItemName TEXT,
                    RequiresFIR INTEGER NOT NULL DEFAULT 0,
                    MapName TEXT,
                    LocationName TEXT,
                    LocationPoints TEXT,
                    Conditions TEXT,
                    ContentHash TEXT,
                    IsApproved INTEGER NOT NULL DEFAULT 0,
                    ApprovedAt TEXT,
                    UpdatedAt TEXT,
                    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
                    FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE SET NULL
                )";

            using var cmd = new SqliteCommand(sql, connection, transaction);
            await cmd.ExecuteNonQueryAsync();

            // 인덱스 생성
            var indexSql = @"
                CREATE INDEX IF NOT EXISTS idx_questobj_questid ON QuestObjectives(QuestId);
                CREATE INDEX IF NOT EXISTS idx_questobj_itemid ON QuestObjectives(ItemId);
                CREATE INDEX IF NOT EXISTS idx_questobj_map ON QuestObjectives(MapName)";
            using var indexCmd = new SqliteCommand(indexSql, connection, transaction);
            await indexCmd.ExecuteNonQueryAsync();
        }

        private async Task MigrateQuestObjectivesTableAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            // 테이블이 존재하고 Id가 INTEGER 타입이면 마이그레이션 필요
            try
            {
                using var checkCmd = new SqliteCommand("PRAGMA table_info(QuestObjectives)", connection, transaction);
                using var reader = await checkCmd.ExecuteReaderAsync();
                bool needsMigration = false;
                while (await reader.ReadAsync())
                {
                    var colName = reader.GetString(1);
                    var colType = reader.GetString(2);
                    if (colName == "Id" && colType.ToUpper() == "INTEGER")
                    {
                        needsMigration = true;
                        break;
                    }
                }
                reader.Close();

                if (needsMigration)
                {
                    // 기존 테이블 삭제 (새 스키마로 재생성)
                    using var dropCmd = new SqliteCommand("DROP TABLE IF EXISTS QuestObjectives", connection, transaction);
                    await dropCmd.ExecuteNonQueryAsync();
                }
            }
            catch { /* 테이블이 없으면 무시 */ }
        }

        private async Task RegisterQuestObjectivesSchemaAsync(SqliteConnection connection, SqliteTransaction transaction)
        {
            var columns = new List<ColumnSchema>
            {
                new() { Name = "Id", DisplayName = "ID", Type = ColumnType.Text, IsPrimaryKey = true, SortOrder = 0 },
                new() { Name = "QuestId", DisplayName = "Quest ID", Type = ColumnType.Text, IsRequired = true, ForeignKeyTable = "Quests", ForeignKeyColumn = "Id", SortOrder = 1 },
                new() { Name = "SortOrder", DisplayName = "Order", Type = ColumnType.Integer, IsRequired = true, SortOrder = 2 },
                new() { Name = "ObjectiveType", DisplayName = "Type", Type = ColumnType.Text, IsRequired = true, SortOrder = 3 },
                new() { Name = "Description", DisplayName = "Description", Type = ColumnType.Text, IsRequired = true, SortOrder = 4 },
                new() { Name = "TargetType", DisplayName = "Target Type", Type = ColumnType.Text, SortOrder = 5 },
                new() { Name = "TargetCount", DisplayName = "Count", Type = ColumnType.Integer, SortOrder = 6 },
                new() { Name = "ItemId", DisplayName = "Item ID", Type = ColumnType.Text, ForeignKeyTable = "Items", ForeignKeyColumn = "Id", SortOrder = 7 },
                new() { Name = "ItemName", DisplayName = "Item Name", Type = ColumnType.Text, SortOrder = 8 },
                new() { Name = "RequiresFIR", DisplayName = "FIR", Type = ColumnType.Boolean, IsRequired = true, SortOrder = 9 },
                new() { Name = "MapName", DisplayName = "Map", Type = ColumnType.Text, SortOrder = 10 },
                new() { Name = "LocationName", DisplayName = "Location", Type = ColumnType.Text, SortOrder = 11 },
                new() { Name = "LocationPoints", DisplayName = "Location Points", Type = ColumnType.Json, SortOrder = 12 },
                new() { Name = "Conditions", DisplayName = "Conditions", Type = ColumnType.Text, SortOrder = 13 },
                new() { Name = "ContentHash", DisplayName = "Content Hash", Type = ColumnType.Text, SortOrder = 14 },
                new() { Name = "IsApproved", DisplayName = "Approved", Type = ColumnType.Boolean, IsRequired = true, SortOrder = 15 },
                new() { Name = "ApprovedAt", DisplayName = "Approved At", Type = ColumnType.DateTime, SortOrder = 16 },
                new() { Name = "UpdatedAt", DisplayName = "Updated At", Type = ColumnType.DateTime, SortOrder = 17 }
            };

            var schemaJson = JsonSerializer.Serialize(columns);
            await UpsertSchemaMetaAsync(connection, transaction, "QuestObjectives", "Quest Objectives", schemaJson);
        }

        private async Task<UpsertStats> UpsertQuestObjectivesAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            List<DbQuestObjective> objectives,
            StringBuilder logBuilder)
        {
            var stats = new UpsertStats();
            var now = DateTime.UtcNow.ToString("o");

            // 기존 데이터 로드 (Id 기준으로 승인 상태 및 좌표 유지)
            var existingData = new Dictionary<string, (bool IsApproved, string? ApprovedAt, string? ContentHash, string? LocationPoints)>();
            var existingIds = new HashSet<string>();
            var selectSql = "SELECT Id, IsApproved, ApprovedAt, ContentHash, LocationPoints FROM QuestObjectives";
            using (var selectCmd = new SqliteCommand(selectSql, connection, transaction))
            using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var id = reader.GetString(0);
                    var isApproved = !reader.IsDBNull(1) && reader.GetInt64(1) != 0;
                    var approvedAt = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var contentHash = reader.IsDBNull(3) ? null : reader.GetString(3);
                    var locationPoints = reader.IsDBNull(4) ? null : reader.GetString(4);
                    existingIds.Add(id);
                    existingData[id] = (isApproved, approvedAt, contentHash, locationPoints);
                }
            }

            // 새로 가져온 objective ID 집합
            var newIds = new HashSet<string>();
            foreach (var obj in objectives)
            {
                obj.Id = obj.ComputeId();
                newIds.Add(obj.Id);
            }

            // DB에 있지만 새 목록에 없는 항목 삭제
            var idsToDelete = existingIds.Except(newIds).ToList();
            foreach (var idToDelete in idsToDelete)
            {
                using var deleteCmd = new SqliteCommand("DELETE FROM QuestObjectives WHERE Id = @Id", connection, transaction);
                deleteCmd.Parameters.AddWithValue("@Id", idToDelete);
                await deleteCmd.ExecuteNonQueryAsync();
                stats.Deleted++;
            }

            // Upsert (기존 승인 상태 및 좌표 유지, 변경 시 승인 해제)
            foreach (var obj in objectives)
            {
                var newHash = obj.ComputeContentHash();
                bool exists = existingIds.Contains(obj.Id);

                bool isApproved = false;
                string? approvedAt = null;
                string? locationPoints = null;

                // 기존 데이터 확인
                if (exists && existingData.TryGetValue(obj.Id, out var existing))
                {
                    // 해시가 같으면 승인 상태 유지, 다르면 승인 해제
                    if (existing.ContentHash == newHash && existing.IsApproved)
                    {
                        isApproved = true;
                        approvedAt = existing.ApprovedAt;
                        stats.Unchanged++;
                    }
                    else if (existing.IsApproved)
                    {
                        logBuilder.AppendLine($"  [CHANGED] {obj.Id} - approval reset due to content change");
                    }

                    // 좌표 정보는 항상 유지 (사용자가 입력한 값)
                    locationPoints = existing.LocationPoints;
                }

                if (!exists)
                {
                    // INSERT
                    var insertSql = @"
                        INSERT INTO QuestObjectives (
                            Id, QuestId, SortOrder, ObjectiveType, Description, TargetType, TargetCount,
                            ItemId, ItemName, RequiresFIR, MapName, LocationName, LocationPoints,
                            Conditions, ContentHash, IsApproved, ApprovedAt, UpdatedAt
                        ) VALUES (
                            @Id, @QuestId, @SortOrder, @ObjectiveType, @Description, @TargetType, @TargetCount,
                            @ItemId, @ItemName, @RequiresFIR, @MapName, @LocationName, @LocationPoints,
                            @Conditions, @ContentHash, @IsApproved, @ApprovedAt, @UpdatedAt
                        )";

                    using var insertCmd = new SqliteCommand(insertSql, connection, transaction);
                    AddObjectiveParameters(insertCmd, obj, newHash, isApproved, approvedAt, locationPoints, now);
                    await insertCmd.ExecuteNonQueryAsync();
                    stats.Inserted++;
                }
                else
                {
                    // UPDATE
                    var updateSql = @"
                        UPDATE QuestObjectives SET
                            QuestId = @QuestId, SortOrder = @SortOrder, ObjectiveType = @ObjectiveType,
                            Description = @Description, TargetType = @TargetType, TargetCount = @TargetCount,
                            ItemId = @ItemId, ItemName = @ItemName, RequiresFIR = @RequiresFIR,
                            MapName = @MapName, LocationName = @LocationName, LocationPoints = @LocationPoints,
                            Conditions = @Conditions, ContentHash = @ContentHash,
                            IsApproved = @IsApproved, ApprovedAt = @ApprovedAt, UpdatedAt = @UpdatedAt
                        WHERE Id = @Id";

                    using var updateCmd = new SqliteCommand(updateSql, connection, transaction);
                    AddObjectiveParameters(updateCmd, obj, newHash, isApproved, approvedAt, locationPoints, now);
                    await updateCmd.ExecuteNonQueryAsync();
                    stats.Updated++;
                }
            }

            logBuilder.AppendLine($"  Objectives: {stats.Inserted} inserted, {stats.Updated} updated, {stats.Deleted} deleted, {stats.Unchanged} approvals preserved");
            return stats;
        }

        private void AddObjectiveParameters(SqliteCommand cmd, DbQuestObjective obj, string contentHash,
            bool isApproved, string? approvedAt, string? locationPoints, string now)
        {
            cmd.Parameters.AddWithValue("@Id", obj.Id);
            cmd.Parameters.AddWithValue("@QuestId", obj.QuestId);
            cmd.Parameters.AddWithValue("@SortOrder", obj.SortOrder);
            cmd.Parameters.AddWithValue("@ObjectiveType", obj.ObjectiveType);
            cmd.Parameters.AddWithValue("@Description", obj.Description);
            cmd.Parameters.AddWithValue("@TargetType", (object?)obj.TargetType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@TargetCount", (object?)obj.TargetCount ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ItemId", (object?)obj.ItemId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ItemName", (object?)obj.ItemName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RequiresFIR", obj.RequiresFIR ? 1 : 0);
            cmd.Parameters.AddWithValue("@MapName", (object?)obj.MapName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationName", (object?)obj.LocationName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@LocationPoints", (object?)locationPoints ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Conditions", (object?)obj.Conditions ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ContentHash", contentHash);
            cmd.Parameters.AddWithValue("@IsApproved", isApproved ? 1 : 0);
            cmd.Parameters.AddWithValue("@ApprovedAt", (object?)approvedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedAt", now);
        }

        private async Task<UpsertStats> UpsertItemsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            List<DbItem> items,
            StringBuilder logBuilder)
        {
            var stats = new UpsertStats();
            var now = DateTime.UtcNow.ToString("o");

            // 현재 DB에 있는 모든 아이템 ID 조회
            var existingIds = new HashSet<string>();
            var selectAllSql = "SELECT Id FROM Items";
            using (var selectAllCmd = new SqliteCommand(selectAllSql, connection, transaction))
            using (var reader = await selectAllCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    existingIds.Add(reader.GetString(0));
                }
            }

            // 새로 가져온 아이템 ID 집합
            var newItemIds = new HashSet<string>(items.Select(i => i.Id));

            // DB에 있지만 새 목록에 없는 아이템 삭제
            var idsToDelete = existingIds.Except(newItemIds).ToList();
            if (idsToDelete.Count > 0)
            {
                foreach (var idToDelete in idsToDelete)
                {
                    var deleteSql = "DELETE FROM Items WHERE Id = @Id";
                    using var deleteCmd = new SqliteCommand(deleteSql, connection, transaction);
                    deleteCmd.Parameters.AddWithValue("@Id", idToDelete);
                    await deleteCmd.ExecuteNonQueryAsync();
                    stats.Deleted++;
                    logBuilder.AppendLine($"  [DELETE] Id: {idToDelete}");
                }
            }

            foreach (var item in items)
            {
                bool exists = existingIds.Contains(item.Id);

                if (!exists)
                {
                    // INSERT
                    var insertSql = @"
                        INSERT INTO Items (Id, BsgId, Name, NameEN, NameKO, NameJA, ShortNameEN, ShortNameKO, ShortNameJA, WikiPageLink, IconUrl, Category, Categories, UpdatedAt)
                        VALUES (@Id, @BsgId, @Name, @NameEN, @NameKO, @NameJA, @ShortNameEN, @ShortNameKO, @ShortNameJA, @WikiPageLink, @IconUrl, @Category, @Categories, @UpdatedAt)";

                    using var insertCmd = new SqliteCommand(insertSql, connection, transaction);
                    AddItemParameters(insertCmd, item, now);
                    await insertCmd.ExecuteNonQueryAsync();
                    stats.Inserted++;
                }
                else
                {
                    // 항상 UPDATE (모든 필드 갱신)
                    var updateSql = @"
                        UPDATE Items SET
                            BsgId = @BsgId, Name = @Name, NameEN = @NameEN, NameKO = @NameKO, NameJA = @NameJA,
                            ShortNameEN = @ShortNameEN, ShortNameKO = @ShortNameKO, ShortNameJA = @ShortNameJA,
                            WikiPageLink = @WikiPageLink, IconUrl = @IconUrl, Category = @Category, Categories = @Categories, UpdatedAt = @UpdatedAt
                        WHERE Id = @Id";

                    using var updateCmd = new SqliteCommand(updateSql, connection, transaction);
                    AddItemParameters(updateCmd, item, now);
                    await updateCmd.ExecuteNonQueryAsync();
                    stats.Updated++;
                }
            }

            return stats;
        }

        private void AddItemParameters(SqliteCommand cmd, DbItem item, string now)
        {
            cmd.Parameters.AddWithValue("@Id", item.Id);
            cmd.Parameters.AddWithValue("@BsgId", (object?)item.BsgId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Name", item.Name);
            cmd.Parameters.AddWithValue("@NameEN", (object?)item.NameEN ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NameKO", (object?)item.NameKO ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NameJA", (object?)item.NameJA ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ShortNameEN", (object?)item.ShortNameEN ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ShortNameKO", (object?)item.ShortNameKO ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ShortNameJA", (object?)item.ShortNameJA ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@WikiPageLink", (object?)item.WikiPageLink ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IconUrl", (object?)item.IconUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Category", (object?)item.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Categories", (object?)item.Categories ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedAt", now);
        }

        private async Task<UpsertStats> UpsertQuestsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            List<DbQuest> quests,
            StringBuilder logBuilder)
        {
            var stats = new UpsertStats();
            var now = DateTime.UtcNow.ToString("o");

            // 현재 DB에 있는 모든 퀘스트 ID 조회
            var existingIds = new HashSet<string>();
            var selectAllSql = "SELECT Id FROM Quests";
            using (var selectAllCmd = new SqliteCommand(selectAllSql, connection, transaction))
            using (var reader = await selectAllCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    existingIds.Add(reader.GetString(0));
                }
            }

            // 새로 가져온 퀘스트 ID 집합
            var newQuestIds = new HashSet<string>(quests.Select(q => q.Id));

            // DB에 있지만 새 목록에 없는 퀘스트 삭제
            var idsToDelete = existingIds.Except(newQuestIds).ToList();
            if (idsToDelete.Count > 0)
            {
                foreach (var idToDelete in idsToDelete)
                {
                    var deleteSql = "DELETE FROM Quests WHERE Id = @Id";
                    using var deleteCmd = new SqliteCommand(deleteSql, connection, transaction);
                    deleteCmd.Parameters.AddWithValue("@Id", idToDelete);
                    await deleteCmd.ExecuteNonQueryAsync();
                    stats.Deleted++;
                    logBuilder.AppendLine($"  [DELETE] Id: {idToDelete}");
                }
            }

            foreach (var quest in quests)
            {
                bool exists = existingIds.Contains(quest.Id);

                if (!exists)
                {
                    var insertSql = @"
                        INSERT INTO Quests (Id, BsgId, Name, NameEN, NameKO, NameJA, WikiPageLink, Trader, MinLevel, MinScavKarma, UpdatedAt)
                        VALUES (@Id, @BsgId, @Name, @NameEN, @NameKO, @NameJA, @WikiPageLink, @Trader, @MinLevel, @MinScavKarma, @UpdatedAt)";

                    using var insertCmd = new SqliteCommand(insertSql, connection, transaction);
                    AddQuestParameters(insertCmd, quest, now);
                    await insertCmd.ExecuteNonQueryAsync();
                    stats.Inserted++;
                    logBuilder.AppendLine($"  [INSERT] {quest.Name}");
                }
                else
                {
                    // 항상 UPDATE (모든 필드 갱신, 단 승인 상태는 유지)
                    var updateSql = @"
                        UPDATE Quests SET
                            BsgId = @BsgId, Name = @Name, NameEN = @NameEN, NameKO = @NameKO, NameJA = @NameJA,
                            WikiPageLink = @WikiPageLink, Trader = @Trader, MinLevel = @MinLevel, MinScavKarma = @MinScavKarma, UpdatedAt = @UpdatedAt
                        WHERE Id = @Id";

                    using var updateCmd = new SqliteCommand(updateSql, connection, transaction);
                    AddQuestParameters(updateCmd, quest, now);
                    await updateCmd.ExecuteNonQueryAsync();
                    stats.Updated++;
                }
            }

            return stats;
        }

        private void AddQuestParameters(SqliteCommand cmd, DbQuest quest, string now)
        {
            cmd.Parameters.AddWithValue("@Id", quest.Id);
            cmd.Parameters.AddWithValue("@BsgId", (object?)quest.BsgId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Name", quest.Name);
            cmd.Parameters.AddWithValue("@NameEN", (object?)quest.NameEN ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NameKO", (object?)quest.NameKO ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NameJA", (object?)quest.NameJA ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@WikiPageLink", (object?)quest.WikiPageLink ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Trader", (object?)quest.Trader ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MinLevel", (object?)quest.MinLevel ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@MinScavKarma", (object?)quest.MinScavKarma ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedAt", now);
        }

        private async Task<UpsertStats> UpsertQuestRequirementsAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            List<DbQuestRequirement> requirements,
            StringBuilder logBuilder)
        {
            var stats = new UpsertStats();
            var now = DateTime.UtcNow.ToString("o");

            // 기존 데이터 로드 (Id 기준으로 승인 상태 유지)
            var existingData = new Dictionary<string, (bool IsApproved, string? ApprovedAt, string? ContentHash)>();
            var existingIds = new HashSet<string>();
            var selectSql = "SELECT Id, IsApproved, ApprovedAt, ContentHash FROM QuestRequirements";
            using (var selectCmd = new SqliteCommand(selectSql, connection, transaction))
            using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var id = reader.GetString(0);
                    var isApproved = !reader.IsDBNull(1) && reader.GetInt64(1) != 0;
                    var approvedAt = reader.IsDBNull(2) ? null : reader.GetString(2);
                    var contentHash = reader.IsDBNull(3) ? null : reader.GetString(3);
                    existingIds.Add(id);
                    existingData[id] = (isApproved, approvedAt, contentHash);
                }
            }

            // 새로 가져온 requirement ID 집합
            var newIds = new HashSet<string>();
            foreach (var req in requirements)
            {
                req.Id = req.ComputeId();
                newIds.Add(req.Id);
            }

            // DB에 있지만 새 목록에 없는 항목 삭제
            var idsToDelete = existingIds.Except(newIds).ToList();
            foreach (var idToDelete in idsToDelete)
            {
                using var deleteCmd = new SqliteCommand("DELETE FROM QuestRequirements WHERE Id = @Id", connection, transaction);
                deleteCmd.Parameters.AddWithValue("@Id", idToDelete);
                await deleteCmd.ExecuteNonQueryAsync();
                stats.Deleted++;
            }

            // Upsert (기존 승인 상태 유지, 변경 시 승인 해제)
            foreach (var req in requirements)
            {
                var newHash = req.ComputeContentHash();
                bool exists = existingIds.Contains(req.Id);

                bool isApproved = false;
                string? approvedAt = null;

                // 기존 승인 상태 확인
                if (exists && existingData.TryGetValue(req.Id, out var existing))
                {
                    // 해시가 같으면 승인 상태 유지, 다르면 승인 해제
                    if (existing.ContentHash == newHash && existing.IsApproved)
                    {
                        isApproved = true;
                        approvedAt = existing.ApprovedAt;
                        stats.Unchanged++;
                    }
                    else if (existing.IsApproved)
                    {
                        // 승인되어 있었지만 내용이 변경됨
                        logBuilder.AppendLine($"  [CHANGED] {req.Id} - approval reset due to content change");
                    }
                }

                if (!exists)
                {
                    // INSERT
                    var insertSql = @"
                        INSERT INTO QuestRequirements (Id, QuestId, RequiredQuestId, RequirementType, DelayMinutes, GroupId, ContentHash, IsApproved, ApprovedAt, UpdatedAt)
                        VALUES (@Id, @QuestId, @RequiredQuestId, @RequirementType, @DelayMinutes, @GroupId, @ContentHash, @IsApproved, @ApprovedAt, @UpdatedAt)";

                    using var insertCmd = new SqliteCommand(insertSql, connection, transaction);
                    AddRequirementParameters(insertCmd, req, newHash, isApproved, approvedAt, now);
                    await insertCmd.ExecuteNonQueryAsync();
                    stats.Inserted++;
                }
                else
                {
                    // UPDATE
                    var updateSql = @"
                        UPDATE QuestRequirements SET
                            QuestId = @QuestId, RequiredQuestId = @RequiredQuestId, RequirementType = @RequirementType,
                            DelayMinutes = @DelayMinutes, GroupId = @GroupId, ContentHash = @ContentHash,
                            IsApproved = @IsApproved, ApprovedAt = @ApprovedAt, UpdatedAt = @UpdatedAt
                        WHERE Id = @Id";

                    using var updateCmd = new SqliteCommand(updateSql, connection, transaction);
                    AddRequirementParameters(updateCmd, req, newHash, isApproved, approvedAt, now);
                    await updateCmd.ExecuteNonQueryAsync();
                    stats.Updated++;
                }
            }

            logBuilder.AppendLine($"  Requirements: {stats.Inserted} inserted, {stats.Updated} updated, {stats.Deleted} deleted, {stats.Unchanged} approvals preserved");
            return stats;
        }

        private void AddRequirementParameters(SqliteCommand cmd, DbQuestRequirement req, string contentHash,
            bool isApproved, string? approvedAt, string now)
        {
            cmd.Parameters.AddWithValue("@Id", req.Id);
            cmd.Parameters.AddWithValue("@QuestId", req.QuestId);
            cmd.Parameters.AddWithValue("@RequiredQuestId", req.RequiredQuestId);
            cmd.Parameters.AddWithValue("@RequirementType", req.RequirementType);
            cmd.Parameters.AddWithValue("@DelayMinutes", (object?)req.DelayMinutes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@GroupId", req.GroupId);
            cmd.Parameters.AddWithValue("@ContentHash", contentHash);
            cmd.Parameters.AddWithValue("@IsApproved", isApproved ? 1 : 0);
            cmd.Parameters.AddWithValue("@ApprovedAt", (object?)approvedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@UpdatedAt", now);
        }

        #endregion

        #region Helper Methods

        private static string NormalizeWikiLink(string wikiLink)
        {
            if (string.IsNullOrEmpty(wikiLink))
                return wikiLink;

            try
            {
                return Uri.UnescapeDataString(wikiLink);
            }
            catch
            {
                return wikiLink;
            }
        }

        private static string NormalizeQuestName(string questName)
        {
            var normalized = questName.ToLowerInvariant();

            if (normalized.EndsWith(" (quest)"))
                normalized = normalized.Substring(0, normalized.Length - 8);

            normalized = normalized.Replace(" ", "-");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[^a-z0-9\-]", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"-+", "-");
            normalized = normalized.Trim('-');

            return normalized;
        }

        /// <summary>
        /// PageContent에서 Trader (given by) 파싱 - 캐시 데이터에서 항상 실행
        /// </summary>
        private static string? ExtractTraderFromContent(string content)
        {
            // |given by = [[Ragman]] 또는 |givenby = [[Prapor]] 형식에서 트레이더 이름 추출
            var match = System.Text.RegularExpressions.Regex.Match(
                content, @"\|given\s*by\s*=\s*\[\[([^\]|]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
                return match.Groups[1].Value.Trim();

            // 링크 없이 직접 트레이더 이름만 있는 경우
            match = System.Text.RegularExpressions.Regex.Match(
                content, @"\|given\s*by\s*=\s*([^\|\}\[\]\n]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var trader = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(trader))
                    return trader;
            }

            return null;
        }

        /// <summary>
        /// PageContent에서 Icon 파일명 파싱 - 캐시 데이터에서 항상 실행
        /// </summary>
        private static string? ExtractIconFromContent(string content)
        {
            if (string.IsNullOrEmpty(content))
                return null;

            var match = System.Text.RegularExpressions.Regex.Match(
                content, @"\|icon\s*=\s*([^\|\}\n]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var iconValue = match.Groups[1].Value.Trim();

                // 파일명만 추출 (File: 접두사 제거, [[]] 제거)
                iconValue = System.Text.RegularExpressions.Regex.Replace(iconValue, @"^\[\[(?:File:|Image:)?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                iconValue = System.Text.RegularExpressions.Regex.Replace(iconValue, @"\]\]$", "");
                iconValue = System.Text.RegularExpressions.Regex.Replace(iconValue, @"^(?:File:|Image:)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // 파이프 이후 제거
                var pipeIndex = iconValue.IndexOf('|');
                if (pipeIndex > 0)
                    iconValue = iconValue.Substring(0, pipeIndex);

                iconValue = iconValue.Trim();

                if (!string.IsNullOrEmpty(iconValue) &&
                    (iconValue.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                     iconValue.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     iconValue.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                     iconValue.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ||
                     iconValue.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)))
                {
                    return iconValue;
                }
            }

            return null;
        }

        #endregion

        public void Dispose()
        {
            // Nothing to dispose currently
        }
    }

    #region Models

    public class RevisionInfo
    {
        [JsonPropertyName("itemsRevision")]
        public string? ItemsRevision { get; set; }

        [JsonPropertyName("questsRevision")]
        public string? QuestsRevision { get; set; }

        [JsonPropertyName("lastUpdated")]
        public DateTime? LastUpdated { get; set; }
    }

    public class RefreshResult
    {
        public bool Success { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public string? DatabasePath { get; set; }
        public string? LogPath { get; set; }
        public string? ErrorMessage { get; set; }
        public bool ItemsUpdated { get; set; }
        public bool QuestsUpdated { get; set; }
        public int ItemsCount { get; set; }
        public int QuestsCount { get; set; }
    }

    public class ItemsFetchResult
    {
        public List<DbItem> Items { get; set; } = new();
        public string Revision { get; set; } = "";
        public int IconsDownloaded { get; set; }
        public int IconsFailed { get; set; }
        public int IconsCached { get; set; }
        public Dictionary<string, (string Url, string Error)> FailedIconDownloads { get; set; } = new();
    }

    public class QuestsFetchResult
    {
        public List<DbQuest> Quests { get; set; } = new();
        public List<DbQuestRequirement> Requirements { get; set; } = new();
        public List<DbQuestObjective> Objectives { get; set; } = new();
        public string Revision { get; set; } = "";
    }

    public class DbItem
    {
        public string Id { get; set; } = "";
        public string? BsgId { get; set; }
        public string Name { get; set; } = "";
        public string? NameEN { get; set; }
        public string? NameKO { get; set; }
        public string? NameJA { get; set; }
        public string? ShortNameEN { get; set; }
        public string? ShortNameKO { get; set; }
        public string? ShortNameJA { get; set; }
        public string? WikiPageLink { get; set; }
        public string? IconUrl { get; set; }
        public string? Category { get; set; }
        public string? Categories { get; set; }
    }

    public class DbQuest
    {
        public string Id { get; set; } = "";
        public string? BsgId { get; set; }
        public string Name { get; set; } = "";
        public string? NameEN { get; set; }
        public string? NameKO { get; set; }
        public string? NameJA { get; set; }
        public string? WikiPageLink { get; set; }
        public string? Trader { get; set; }
        public int? MinLevel { get; set; }
        public int? MinScavKarma { get; set; }
    }

    public class UpsertStats
    {
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Deleted { get; set; }
    }

    /// <summary>
    /// 퀘스트 선행 조건 데이터 모델
    /// </summary>
    public class DbQuestRequirement
    {
        public string Id { get; set; } = ""; // Hash-based ID (QuestId + RequiredQuestId + GroupId)
        public string QuestId { get; set; } = "";
        public string RequiredQuestId { get; set; } = "";
        public string RequirementType { get; set; } = "Complete"; // Complete, Accept, Fail
        public int? DelayMinutes { get; set; } // 시간 지연 (분 단위)
        public int GroupId { get; set; } // OR 그룹 ID (같은 그룹 내에서는 OR 조건)
        public string? ContentHash { get; set; } // 변경 감지용 해시
        public bool IsApproved { get; set; } // 사용자 승인 여부
        public DateTime? ApprovedAt { get; set; } // 승인 시간

        /// <summary>
        /// 고유 ID 생성 (QuestId + RequiredQuestId + GroupId 기반 해시)
        /// </summary>
        public string ComputeId()
        {
            var data = $"REQ|{QuestId}|{RequiredQuestId}|{GroupId}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash).Substring(0, 22).Replace("/", "_").Replace("+", "-");
        }

        /// <summary>
        /// 현재 데이터의 해시 생성 (변경 감지용)
        /// </summary>
        public string ComputeContentHash()
        {
            var data = $"{QuestId}|{RequiredQuestId}|{RequirementType}|{DelayMinutes}|{GroupId}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }
    }

    /// <summary>
    /// 퀘스트 목표 데이터 모델
    /// </summary>
    public class DbQuestObjective
    {
        public string Id { get; set; } = ""; // Hash-based ID (QuestId + SortOrder)
        public string QuestId { get; set; } = "";
        public int SortOrder { get; set; }
        public string ObjectiveType { get; set; } = "Custom"; // Kill, Collect, HandOver, Visit, Marking, Stash, Survive, Build, Custom
        public string Description { get; set; } = "";

        // 타겟 정보
        public string? TargetType { get; set; }  // Scav, PMC, Boss, Item 등
        public int? TargetCount { get; set; }

        // 아이템 정보
        public string? ItemId { get; set; }      // FK: Items.Id
        public string? ItemName { get; set; }    // Wiki 아이템 이름 (매칭용)
        public bool RequiresFIR { get; set; }    // Found in Raid 필요 여부

        // 맵/위치 정보
        public string? MapName { get; set; }     // Customs, Factory, Shoreline 등
        public string? LocationName { get; set; } // 위치 설명 텍스트
        public double? LocationX { get; set; }   // X 좌표 (추후 입력)
        public double? LocationY { get; set; }   // Y 좌표
        public double? LocationZ { get; set; }   // Z 좌표
        public double? LocationRadius { get; set; } // 범위 반경 (추후 입력)

        // 조건
        public string? Conditions { get; set; }  // 추가 조건 (JSON 또는 텍스트)

        // 승인 상태
        public string? ContentHash { get; set; }
        public bool IsApproved { get; set; }
        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// 고유 ID 생성 (QuestId + SortOrder 기반 해시)
        /// </summary>
        public string ComputeId()
        {
            var data = $"OBJ|{QuestId}|{SortOrder}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash).Substring(0, 22).Replace("/", "_").Replace("+", "-");
        }

        /// <summary>
        /// 현재 데이터의 해시 생성 (변경 감지용)
        /// </summary>
        public string ComputeContentHash()
        {
            var data = $"{QuestId}|{SortOrder}|{ObjectiveType}|{Description}|{TargetType}|{TargetCount}|{ItemName}|{RequiresFIR}|{MapName}|{LocationName}|{Conditions}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
            return Convert.ToBase64String(hash).Substring(0, 16);
        }
    }

    #endregion
}
