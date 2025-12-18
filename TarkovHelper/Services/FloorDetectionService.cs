using System.IO;
using Microsoft.Data.Sqlite;
using TarkovHelper.Services.Logging;

namespace TarkovHelper.Services;

/// <summary>
/// Y 좌표 기반 자동 층 감지 서비스.
/// MapFloorLocations 테이블에서 층별 Y 좌표 범위를 로드합니다.
/// </summary>
public sealed class FloorDetectionService
{
    private static readonly ILogger _log = Log.For<FloorDetectionService>();
    private static FloorDetectionService? _instance;
    public static FloorDetectionService Instance => _instance ??= new FloorDetectionService();

    private readonly string _databasePath;
    private Dictionary<string, List<FloorYRange>> _floorRangesByMap = new(StringComparer.OrdinalIgnoreCase);
    private bool _isLoaded;

    /// <summary>
    /// 데이터가 로드되었는지 여부
    /// </summary>
    public bool IsLoaded => _isLoaded;

    private FloorDetectionService()
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
        _log.Debug("Refreshing floor ranges data...");
        // 기존 데이터를 클리어하지 않음 - LoadFloorRangesAsync()에서 atomic swap으로 교체
        await LoadFloorRangesAsync();
    }

    /// <summary>
    /// DB에서 층 범위 데이터를 로드합니다.
    /// </summary>
    public async Task<bool> LoadFloorRangesAsync()
    {
        if (!File.Exists(_databasePath))
        {
            _log.Warning($"Database not found: {_databasePath}");
            return false;
        }

        try
        {
            var connectionString = $"Data Source={_databasePath};Mode=ReadOnly";
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // MapFloorLocations 테이블 존재 여부 확인
            var checkSql = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MapFloorLocations'";
            await using var checkCmd = new SqliteCommand(checkSql, connection);
            if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) == 0)
            {
                _log.Warning("MapFloorLocations table not found");
                return false;
            }

            // 층 범위 로드 (Priority 내림차순) - XZ 좌표 포함
            var sql = "SELECT MapKey, FloorId, MinY, MaxY, MinX, MaxX, MinZ, MaxZ, Priority FROM MapFloorLocations ORDER BY MapKey, Priority DESC";
            await using var cmd = new SqliteCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            // 새 딕셔너리 빌드 (기존 데이터 유지하면서)
            var newFloorRangesByMap = new Dictionary<string, List<FloorYRange>>(StringComparer.OrdinalIgnoreCase);

            while (await reader.ReadAsync())
            {
                var mapKey = reader.GetString(0);
                var range = new FloorYRange
                {
                    FloorId = reader.GetString(1),
                    MinY = reader.GetDouble(2),
                    MaxY = reader.GetDouble(3),
                    MinX = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    MaxX = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    MinZ = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    MaxZ = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    Priority = reader.GetInt32(8)
                };

                if (!newFloorRangesByMap.TryGetValue(mapKey, out var ranges))
                {
                    ranges = new List<FloorYRange>();
                    newFloorRangesByMap[mapKey] = ranges;
                }
                ranges.Add(range);
            }

            // Atomic swap - 모든 데이터가 준비된 후 한 번에 교체
            _floorRangesByMap = newFloorRangesByMap;
            _isLoaded = true;
            _log.Info($"Loaded floor ranges for {_floorRangesByMap.Count} maps");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error($"Error loading floor ranges: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// X, Y, Z 좌표를 기반으로 층을 감지합니다.
    /// XZ 범위가 설정된 영역은 해당 범위 내에서만 층이 감지됩니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="x">X 좌표</param>
    /// <param name="y">Y 좌표 (높이)</param>
    /// <param name="z">Z 좌표</param>
    /// <returns>감지된 층 ID (없으면 null)</returns>
    public string? DetectFloor(string mapKey, double x, double y, double z)
    {
        if (!_floorRangesByMap.TryGetValue(mapKey, out var ranges))
            return null;

        // Priority 순서대로 (내림차순) 체크
        foreach (var range in ranges)
        {
            if (range.Contains(x, y, z))
                return range.FloorId;
        }

        return null;
    }

    /// <summary>
    /// Y 좌표만으로 층을 감지합니다 (XZ 범위가 없는 영역만 체크).
    /// 하위 호환성을 위해 유지됩니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="y">Y 좌표 (높이)</param>
    /// <returns>감지된 층 ID (없으면 null)</returns>
    [Obsolete("Use DetectFloor(mapKey, x, y, z) for accurate floor detection with XZ bounds")]
    public string? DetectFloor(string mapKey, double y)
    {
        if (!_floorRangesByMap.TryGetValue(mapKey, out var ranges))
            return null;

        // Priority 순서대로 (내림차순) 체크 - XZ 범위가 없는 것만
        foreach (var range in ranges)
        {
            if (!range.HasXZBounds && y >= range.MinY && y <= range.MaxY)
                return range.FloorId;
        }

        return null;
    }

    /// <summary>
    /// 특정 맵에 층 데이터가 있는지 확인합니다.
    /// </summary>
    public bool HasFloorData(string mapKey)
    {
        return _floorRangesByMap.ContainsKey(mapKey);
    }

    /// <summary>
    /// 특정 맵의 모든 층 범위를 반환합니다.
    /// </summary>
    public IReadOnlyList<FloorYRange> GetFloorRanges(string mapKey)
    {
        if (_floorRangesByMap.TryGetValue(mapKey, out var ranges))
            return ranges;
        return Array.Empty<FloorYRange>();
    }
}

/// <summary>
/// 층별 좌표 범위 (Y 필수, XZ 선택적)
/// </summary>
public class FloorYRange
{
    /// <summary>
    /// 층 ID (SVG 레이어 ID와 매칭)
    /// </summary>
    public string FloorId { get; set; } = string.Empty;

    /// <summary>
    /// 최소 Y 좌표 (필수)
    /// </summary>
    public double MinY { get; set; }

    /// <summary>
    /// 최대 Y 좌표 (필수)
    /// </summary>
    public double MaxY { get; set; }

    /// <summary>
    /// 최소 X 좌표 (선택적 - 특정 영역 지정용)
    /// </summary>
    public double? MinX { get; set; }

    /// <summary>
    /// 최대 X 좌표 (선택적 - 특정 영역 지정용)
    /// </summary>
    public double? MaxX { get; set; }

    /// <summary>
    /// 최소 Z 좌표 (선택적 - 특정 영역 지정용)
    /// </summary>
    public double? MinZ { get; set; }

    /// <summary>
    /// 최대 Z 좌표 (선택적 - 특정 영역 지정용)
    /// </summary>
    public double? MaxZ { get; set; }

    /// <summary>
    /// 우선순위 (높을수록 먼저 체크)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// XZ 범위가 지정되어 있는지 여부
    /// </summary>
    public bool HasXZBounds => MinX.HasValue && MaxX.HasValue && MinZ.HasValue && MaxZ.HasValue;

    /// <summary>
    /// 좌표가 이 Floor 영역에 해당하는지 확인
    /// </summary>
    public bool Contains(double x, double y, double z)
    {
        // Y 범위 확인 (필수)
        if (y < MinY || y > MaxY)
            return false;

        // XZ 범위 확인 (선택적)
        if (HasXZBounds)
        {
            if (x < MinX!.Value || x > MaxX!.Value)
                return false;
            if (z < MinZ!.Value || z > MaxZ!.Value)
                return false;
        }

        return true;
    }
}
