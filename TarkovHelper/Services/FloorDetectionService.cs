using System.IO;
using Microsoft.Data.Sqlite;

namespace TarkovHelper.Services;

/// <summary>
/// Y 좌표 기반 자동 층 감지 서비스.
/// MapFloorLocations 테이블에서 층별 Y 좌표 범위를 로드합니다.
/// </summary>
public sealed class FloorDetectionService
{
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
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _databasePath = Path.Combine(appDir, "Assets", "tarkov_data.db");
    }

    /// <summary>
    /// DB에서 층 범위 데이터를 로드합니다.
    /// </summary>
    public async Task<bool> LoadFloorRangesAsync()
    {
        if (!File.Exists(_databasePath))
        {
            System.Diagnostics.Debug.WriteLine($"[FloorDetectionService] Database not found: {_databasePath}");
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
                System.Diagnostics.Debug.WriteLine("[FloorDetectionService] MapFloorLocations table not found");
                return false;
            }

            // 층 범위 로드 (Priority 내림차순)
            var sql = "SELECT MapKey, FloorId, MinY, MaxY, Priority FROM MapFloorLocations ORDER BY MapKey, Priority DESC";
            await using var cmd = new SqliteCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            _floorRangesByMap.Clear();

            while (await reader.ReadAsync())
            {
                var mapKey = reader.GetString(0);
                var range = new FloorYRange
                {
                    FloorId = reader.GetString(1),
                    MinY = reader.GetDouble(2),
                    MaxY = reader.GetDouble(3),
                    Priority = reader.GetInt32(4)
                };

                if (!_floorRangesByMap.TryGetValue(mapKey, out var ranges))
                {
                    ranges = new List<FloorYRange>();
                    _floorRangesByMap[mapKey] = ranges;
                }
                ranges.Add(range);
            }

            _isLoaded = true;
            System.Diagnostics.Debug.WriteLine($"[FloorDetectionService] Loaded floor ranges for {_floorRangesByMap.Count} maps");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FloorDetectionService] Error loading floor ranges: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Y 좌표를 기반으로 층을 감지합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="y">Y 좌표 (높이)</param>
    /// <returns>감지된 층 ID (없으면 null)</returns>
    public string? DetectFloor(string mapKey, double y)
    {
        if (!_floorRangesByMap.TryGetValue(mapKey, out var ranges))
            return null;

        // Priority 순서대로 (내림차순) 체크
        foreach (var range in ranges)
        {
            if (y >= range.MinY && y <= range.MaxY)
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
/// 층별 Y 좌표 범위
/// </summary>
public class FloorYRange
{
    /// <summary>
    /// 층 ID (SVG 레이어 ID와 매칭)
    /// </summary>
    public string FloorId { get; set; } = string.Empty;

    /// <summary>
    /// 최소 Y 좌표
    /// </summary>
    public double MinY { get; set; }

    /// <summary>
    /// 최대 Y 좌표
    /// </summary>
    public double MaxY { get; set; }

    /// <summary>
    /// 우선순위 (높을수록 먼저 체크)
    /// </summary>
    public int Priority { get; set; }
}
