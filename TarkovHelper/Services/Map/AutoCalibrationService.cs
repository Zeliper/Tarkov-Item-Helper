using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// 자동 맵 보정 서비스.
/// 구 지도 변환을 기반으로 신 지도의 보정점을 자동 생성합니다.
/// </summary>
public sealed class AutoCalibrationService
{
    private static AutoCalibrationService? _instance;
    public static AutoCalibrationService Instance => _instance ??= new AutoCalibrationService();

    private readonly OldMapTransformService _oldTransform;
    private readonly MapComparisonService _comparison;

    private AutoCalibrationService()
    {
        _oldTransform = OldMapTransformService.Instance;
        _comparison = MapComparisonService.Instance;
    }

    /// <summary>
    /// 지정된 맵을 자동 보정합니다.
    /// </summary>
    public AutoCalibrationResult CalibrateFromExistingPoints(MapConfig mapConfig)
    {
        var result = new AutoCalibrationResult
        {
            MapKey = mapConfig.Key,
            Success = false
        };

        if (mapConfig.CalibrationPoints == null || mapConfig.CalibrationPoints.Count < 3)
        {
            result.ErrorMessage = "보정 포인트가 3개 이상 필요합니다.";
            return result;
        }

        var correspondences = new List<(double oldX, double oldY, double newX, double newY)>();

        foreach (var point in mapConfig.CalibrationPoints)
        {
            var oldPos = _oldTransform.TransformToOldScreen(mapConfig.Key, point.GameX, point.GameZ);
            if (oldPos == null)
                continue;

            var scaledPos = _oldTransform.ScaleToNewMap(mapConfig.Key, oldPos.Value.x, oldPos.Value.y);
            if (scaledPos == null)
                continue;

            correspondences.Add((scaledPos.Value.x, scaledPos.Value.y, point.ScreenX, point.ScreenY));
        }

        if (correspondences.Count < 3)
        {
            result.ErrorMessage = "유효한 대응점이 3개 미만입니다.";
            return result;
        }

        var mapping = _comparison.CalculateOldToNewMapping(correspondences);
        if (mapping == null)
        {
            result.ErrorMessage = "매핑 계산 실패";
            return result;
        }

        var testPoints = correspondences.Select(c => (c.oldX, c.oldY, c.newX, c.newY)).ToList();
        var analysis = _comparison.AnalyzeMapping(mapping, testPoints);

        result.Success = true;
        result.ReferencePointCount = correspondences.Count;
        result.OldToNewMapping = mapping;
        result.Analysis = analysis;

        return result;
    }

    /// <summary>
    /// 게임 좌표를 보정된 신 지도 좌표로 변환합니다.
    /// </summary>
    public (double x, double y)? TransformWithMapping(string mapKey, double gameX, double gameZ, double[] mapping)
    {
        var oldPos = _oldTransform.TransformToOldScreen(mapKey, gameX, gameZ);
        if (oldPos == null)
            return null;

        var scaledPos = _oldTransform.ScaleToNewMap(mapKey, oldPos.Value.x, oldPos.Value.y);
        if (scaledPos == null)
            return null;

        return _comparison.ApplyMapping(mapping, scaledPos.Value.x, scaledPos.Value.y);
    }

    /// <summary>
    /// 모든 맵을 자동 보정합니다.
    /// </summary>
    public Dictionary<string, AutoCalibrationResult> CalibrateAllMaps(IEnumerable<MapConfig> maps)
    {
        var results = new Dictionary<string, AutoCalibrationResult>();

        foreach (var map in maps)
        {
            if (string.IsNullOrWhiteSpace(map.Key))
                continue;

            var result = CalibrateFromExistingPoints(map);
            results[map.Key] = result;
        }

        return results;
    }

    /// <summary>
    /// 수동 보정 없이 구 지도 변환만으로 CalibratedTransform을 계산합니다.
    /// </summary>
    public double[]? CalculateTransformFromOldMap(string mapKey)
    {
        var reference = _oldTransform.GetReferenceData(mapKey);
        if (reference == null)
            return null;

        var gameToScreenPoints = new List<(double gameX, double gameZ, double screenX, double screenY)>();

        var bounds = reference.OldSvgBounds;
        var minLng = Math.Min(bounds[0][0], bounds[1][0]);
        var maxLng = Math.Max(bounds[0][0], bounds[1][0]);
        var minLat = Math.Min(bounds[0][1], bounds[1][1]);
        var maxLat = Math.Max(bounds[0][1], bounds[1][1]);

        const int gridSize = 5;
        for (int i = 0; i <= gridSize; i++)
        {
            for (int j = 0; j <= gridSize; j++)
            {
                var gameX = minLng + (maxLng - minLng) * i / gridSize;
                var gameZ = minLat + (maxLat - minLat) * j / gridSize;

                var result = _oldTransform.TransformToNewScreenSimple(mapKey, gameX, gameZ);
                if (result != null)
                {
                    gameToScreenPoints.Add((gameX, gameZ, result.Value.x, result.Value.y));
                }
            }
        }

        if (gameToScreenPoints.Count < 3)
            return null;

        return MapCalibrationService.Instance.CalculateAffineTransform(
            gameToScreenPoints.Select(p => new CalibrationPoint
            {
                GameX = p.gameX,
                GameZ = p.gameZ,
                ScreenX = p.screenX,
                ScreenY = p.screenY
            }).ToList());
    }

    /// <summary>
    /// 보정 결과를 기반으로 새로운 CalibratedTransform을 계산합니다.
    /// </summary>
    public double[]? CalculateNewCalibratedTransform(string mapKey, double[] oldToNewMapping)
    {
        var reference = _oldTransform.GetReferenceData(mapKey);
        if (reference == null || oldToNewMapping == null || oldToNewMapping.Length < 6)
            return null;

        var gameToScreenPoints = new List<(double gameX, double gameZ, double screenX, double screenY)>();

        var bounds = reference.OldSvgBounds;
        var minLng = Math.Min(bounds[0][0], bounds[1][0]);
        var maxLng = Math.Max(bounds[0][0], bounds[1][0]);
        var minLat = Math.Min(bounds[0][1], bounds[1][1]);
        var maxLat = Math.Max(bounds[0][1], bounds[1][1]);

        const int gridSize = 5;
        for (int i = 0; i <= gridSize; i++)
        {
            for (int j = 0; j <= gridSize; j++)
            {
                var gameX = minLng + (maxLng - minLng) * i / gridSize;
                var gameZ = minLat + (maxLat - minLat) * j / gridSize;

                var result = TransformWithMapping(mapKey, gameX, gameZ, oldToNewMapping);
                if (result != null)
                {
                    gameToScreenPoints.Add((gameX, gameZ, result.Value.x, result.Value.y));
                }
            }
        }

        if (gameToScreenPoints.Count < 3)
            return null;

        return MapCalibrationService.Instance.CalculateAffineTransform(
            gameToScreenPoints.Select(p => new CalibrationPoint
            {
                GameX = p.gameX,
                GameZ = p.gameZ,
                ScreenX = p.screenX,
                ScreenY = p.screenY
            }).ToList());
    }
}

/// <summary>
/// 자동 보정 결과
/// </summary>
public sealed class AutoCalibrationResult
{
    public string MapKey { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int ReferencePointCount { get; set; }
    public double[]? OldToNewMapping { get; set; }
    public MappingAnalysis? Analysis { get; set; }
}
