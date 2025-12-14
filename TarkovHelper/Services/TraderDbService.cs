using System.IO;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;

namespace TarkovHelper.Services;

/// <summary>
/// SQLite DB에서 트레이더 데이터를 로드하는 서비스.
/// tarkov_data.db의 Traders 테이블 사용.
/// </summary>
public sealed class TraderDbService
{
    private static TraderDbService? _instance;
    public static TraderDbService Instance => _instance ??= new TraderDbService();

    private readonly string _databasePath;
    private List<TarkovTrader> _allTraders = new();
    private Dictionary<string, TarkovTrader> _tradersById = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, TarkovTrader> _tradersByName = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public int TraderCount => _allTraders.Count;

    private TraderDbService()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _databasePath = Path.Combine(appDir, "Assets", "tarkov_data.db");
    }

    /// <summary>
    /// DB가 존재하는지 확인
    /// </summary>
    public bool DatabaseExists => File.Exists(_databasePath);

    /// <summary>
    /// 모든 트레이더 반환
    /// </summary>
    public IReadOnlyList<TarkovTrader> AllTraders => _allTraders;

    /// <summary>
    /// ID로 트레이더 조회
    /// </summary>
    public TarkovTrader? GetTraderById(string id)
    {
        return _tradersById.TryGetValue(id, out var trader) ? trader : null;
    }

    /// <summary>
    /// 이름으로 트레이더 조회
    /// </summary>
    public TarkovTrader? GetTraderByName(string name)
    {
        return _tradersByName.TryGetValue(name, out var trader) ? trader : null;
    }

    /// <summary>
    /// DB에서 모든 트레이더를 로드합니다.
    /// </summary>
    public async Task<bool> LoadTradersAsync()
    {
        if (!DatabaseExists)
        {
            System.Diagnostics.Debug.WriteLine($"[TraderDbService] Database not found: {_databasePath}");
            return false;
        }

        try
        {
            var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // Traders 테이블 존재 여부 확인
            if (!await TableExistsAsync(connection, "Traders"))
            {
                System.Diagnostics.Debug.WriteLine("[TraderDbService] Traders table not found");
                return false;
            }

            // 트레이더 정보 로드
            var traders = await LoadTradersFromDbAsync(connection);

            // 캐시에 저장
            _allTraders = traders;
            _tradersById.Clear();
            _tradersByName.Clear();

            foreach (var trader in traders)
            {
                if (!string.IsNullOrEmpty(trader.Id))
                {
                    _tradersById[trader.Id] = trader;
                }
                if (!string.IsNullOrEmpty(trader.Name))
                {
                    _tradersByName[trader.Name] = trader;
                }
            }

            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[TraderDbService] Loaded {traders.Count} traders from DB");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TraderDbService] Error loading traders: {ex.Message}");
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
    /// DB에서 트레이더 정보 로드
    /// </summary>
    private async Task<List<TarkovTrader>> LoadTradersFromDbAsync(SqliteConnection connection)
    {
        var traders = new List<TarkovTrader>();

        var sql = @"
            SELECT
                Id, Name, NameKO, NameJA, NormalizedName, ImageLink
            FROM Traders
            ORDER BY Name";

        await using var cmd = new SqliteCommand(sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var trader = new TarkovTrader
            {
                Id = reader.GetString(0),
                Name = reader.IsDBNull(1) ? "" : reader.GetString(1),
                NameKo = reader.IsDBNull(2) ? null : reader.GetString(2),
                NameJa = reader.IsDBNull(3) ? null : reader.GetString(3),
                NormalizedName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ImageLink = reader.IsDBNull(5) ? null : reader.GetString(5)
            };
            traders.Add(trader);
        }

        return traders;
    }
}
