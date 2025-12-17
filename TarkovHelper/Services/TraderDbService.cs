using System.IO;
using Microsoft.Data.Sqlite;
using TarkovHelper.Models;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// SQLite DB에서 트레이더 데이터를 로드하는 서비스.
/// tarkov_data.db의 Traders 테이블 사용.
/// </summary>
public sealed class TraderDbService
{
    private static readonly ILogger _log = Log.For<TraderDbService>();
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
        _databasePath = DatabaseUpdateService.Instance.DatabasePath;

        // 데이터베이스 업데이트 이벤트 구독
        DatabaseUpdateService.Instance.DatabaseUpdated += OnDatabaseUpdated;
    }

    /// <summary>
    /// 데이터베이스 업데이트 시 데이터 리로드
    /// </summary>
    private async void OnDatabaseUpdated(object? sender, EventArgs e)
    {
        _log.Info("Database updated, reloading data...");
        await RefreshAsync();
    }

    /// <summary>
    /// 데이터 새로고침 (기존 데이터를 유지하면서 새 데이터로 atomic swap)
    /// </summary>
    public async Task RefreshAsync()
    {
        _log.Debug("Refreshing trader data...");
        // 기존 데이터를 클리어하지 않음 - LoadTradersAsync()에서 atomic swap으로 교체
        await LoadTradersAsync();
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
            _log.Warning($"Database not found: {_databasePath}");
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
                _log.Warning("Traders table not found");
                return false;
            }

            // 트레이더 정보 로드
            var traders = await LoadTradersFromDbAsync(connection);

            // 새 딕셔너리 빌드 (기존 데이터 유지하면서)
            var newTradersById = new Dictionary<string, TarkovTrader>(StringComparer.OrdinalIgnoreCase);
            var newTradersByName = new Dictionary<string, TarkovTrader>(StringComparer.OrdinalIgnoreCase);

            foreach (var trader in traders)
            {
                if (!string.IsNullOrEmpty(trader.Id))
                {
                    newTradersById[trader.Id] = trader;
                }
                if (!string.IsNullOrEmpty(trader.Name))
                {
                    newTradersByName[trader.Name] = trader;
                }
            }

            // Atomic swap - 모든 데이터가 준비된 후 한 번에 교체
            _allTraders = traders;
            _tradersById = newTradersById;
            _tradersByName = newTradersByName;
            _isLoaded = true;
            _log.Info($"Loaded {traders.Count} traders from DB");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Error loading traders: {ex.Message}");
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
