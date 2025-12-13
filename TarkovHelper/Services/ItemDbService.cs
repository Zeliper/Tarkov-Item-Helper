using System.IO;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// SQLite DB에서 아이템 데이터를 로드하는 서비스.
/// tarkov_data.db의 Items 테이블 사용.
/// </summary>
public sealed class ItemDbService
{
    private static ItemDbService? _instance;
    public static ItemDbService Instance => _instance ??= new ItemDbService();

    private readonly string _databasePath;
    private List<TarkovItem> _allItems = new();
    private Dictionary<string, TarkovItem> _itemsById = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, TarkovItem> _itemsByNormalizedName = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public int ItemCount => _allItems.Count;

    private ItemDbService()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _databasePath = Path.Combine(appDir, "Assets", "tarkov_data.db");
    }

    /// <summary>
    /// DB가 존재하는지 확인
    /// </summary>
    public bool DatabaseExists => File.Exists(_databasePath);

    /// <summary>
    /// 모든 아이템 반환
    /// </summary>
    public IReadOnlyList<TarkovItem> AllItems => _allItems;

    /// <summary>
    /// ID로 아이템 조회
    /// </summary>
    public TarkovItem? GetItemById(string id)
    {
        return _itemsById.TryGetValue(id, out var item) ? item : null;
    }

    /// <summary>
    /// NormalizedName으로 아이템 조회
    /// </summary>
    public TarkovItem? GetItemByNormalizedName(string normalizedName)
    {
        return _itemsByNormalizedName.TryGetValue(normalizedName, out var item) ? item : null;
    }

    /// <summary>
    /// 아이템 Lookup Dictionary 반환 (ID와 NormalizedName 모두 키로 사용)
    /// QuestRequiredItems에서 ItemId로 조회하거나 NormalizedName으로 조회할 수 있도록 함
    /// </summary>
    public Dictionary<string, TarkovItem> GetItemLookup()
    {
        var lookup = new Dictionary<string, TarkovItem>(StringComparer.OrdinalIgnoreCase);

        // NormalizedName으로 먼저 추가
        foreach (var kvp in _itemsByNormalizedName)
        {
            lookup.TryAdd(kvp.Key, kvp.Value);
        }

        // ID로도 추가 (QuestRequiredItems.ItemId가 tarkov.dev ID를 사용하므로)
        foreach (var kvp in _itemsById)
        {
            lookup.TryAdd(kvp.Key, kvp.Value);
        }

        // Dogtag aliases 추가 (wiki-parsed names -> API names)
        if (lookup.TryGetValue("dogtag-bear", out var bearDogtag))
        {
            lookup.TryAdd("bear-dogtag", bearDogtag);
        }
        if (lookup.TryGetValue("dogtag-usec", out var usecDogtag))
        {
            lookup.TryAdd("usec-dogtag", usecDogtag);
        }

        return lookup;
    }

    /// <summary>
    /// DB에서 모든 아이템을 로드합니다.
    /// </summary>
    public async Task<bool> LoadItemsAsync()
    {
        if (!DatabaseExists)
        {
            System.Diagnostics.Debug.WriteLine($"[ItemDbService] Database not found: {_databasePath}");
            return false;
        }

        try
        {
            var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // Items 테이블 존재 여부 확인
            if (!await TableExistsAsync(connection, "Items"))
            {
                System.Diagnostics.Debug.WriteLine("[ItemDbService] Items table not found");
                return false;
            }

            var items = await LoadItemsFromDbAsync(connection);

            // 캐시에 저장
            _allItems = items;
            _itemsById.Clear();
            _itemsByNormalizedName.Clear();

            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.Id))
                {
                    _itemsById[item.Id] = item;
                }
                if (!string.IsNullOrEmpty(item.NormalizedName))
                {
                    _itemsByNormalizedName[item.NormalizedName] = item;
                }
            }

            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[ItemDbService] Loaded {items.Count} items from DB");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ItemDbService] Error loading items: {ex.Message}");
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
    /// DB에서 아이템 로드
    /// Items 테이블 스키마:
    /// Id, BsgId, Name, NameEN, NameKO, NameJA, ShortNameEN, ShortNameKO, ShortNameJA, WikiPageLink, IconUrl, Category, Categories, UpdatedAt
    /// </summary>
    private async Task<List<TarkovItem>> LoadItemsFromDbAsync(SqliteConnection connection)
    {
        var items = new List<TarkovItem>();

        var sql = @"
            SELECT
                Id, BsgId, Name, NameEN, NameKO, NameJA,
                ShortNameEN, ShortNameKO, ShortNameJA,
                WikiPageLink, IconUrl, Category
            FROM Items
            ORDER BY Name";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var name = reader.IsDBNull(2) ? "" : reader.GetString(2);

            var item = new TarkovItem
            {
                Id = id,
                Name = name,
                NormalizedName = GenerateNormalizedName(name, id),
                NameKo = reader.IsDBNull(4) ? null : reader.GetString(4),
                NameJa = reader.IsDBNull(5) ? null : reader.GetString(5),
                ShortName = reader.IsDBNull(6) ? null : reader.GetString(6),
                WikiLink = reader.IsDBNull(9) ? null : reader.GetString(9),
                IconLink = reader.IsDBNull(10) ? null : reader.GetString(10)
            };

            items.Add(item);
        }

        return items;
    }

    /// <summary>
    /// Name에서 NormalizedName 생성
    /// </summary>
    private string GenerateNormalizedName(string name, string id)
    {
        // 먼저 name에서 생성 시도
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("'", "")
                .Replace("'", "")
                .Replace(".", "")
                .Replace(",", "")
                .Replace("?", "")
                .Replace("!", "")
                .Replace(":", "")
                .Replace("\"", "")
                .Replace("(", "")
                .Replace(")", "");
        }

        // name이 없으면 id 사용
        return id.ToLowerInvariant();
    }

    /// <summary>
    /// 데이터 새로고침
    /// </summary>
    public async Task RefreshAsync()
    {
        _isLoaded = false;
        _allItems.Clear();
        _itemsById.Clear();
        _itemsByNormalizedName.Clear();

        await LoadItemsAsync();
    }
}
