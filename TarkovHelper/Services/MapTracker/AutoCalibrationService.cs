using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Services.MapTracker;

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
    /// 기존 수동 보정 데이터(CalibrationPoints)를 사용하여 구→신 매핑을 계산합니다.
    /// </summary>
    /// <param name="mapConfig">맵 설정</param>
    /// <returns>보정 결과</returns>
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

        // 각 보정 포인트에 대해 구 지도 좌표 계산
        var correspondences = new List<(double oldX, double oldY, double newX, double newY)>();

        foreach (var point in mapConfig.CalibrationPoints)
        {
            var oldPos = _oldTransform.TransformToOldScreen(mapConfig.Key, point.GameX, point.GameZ);
            if (oldPos == null)
                continue;

            // 구 좌표를 신 맵 비율로 스케일링
            var scaledPos = _oldTransform.ScaleToNewMap(mapConfig.Key, oldPos.Value.x, oldPos.Value.y);
            if (scaledPos == null)
                continue;

            // 대응점: 스케일링된 구 좌표 → 수동 보정된 신 좌표
            correspondences.Add((scaledPos.Value.x, scaledPos.Value.y, point.ScreenX, point.ScreenY));
        }

        if (correspondences.Count < 3)
        {
            result.ErrorMessage = "유효한 대응점이 3개 미만입니다.";
            return result;
        }

        // 구→신 매핑 계산
        var mapping = _comparison.CalculateOldToNewMapping(correspondences);
        if (mapping == null)
        {
            result.ErrorMessage = "매핑 계산 실패";
            return result;
        }

        // 오차 분석
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
    /// <param name="mapKey">맵 키</param>
    /// <param name="gameX">게임 X 좌표</param>
    /// <param name="gameZ">게임 Z 좌표</param>
    /// <param name="mapping">구→신 매핑 행렬</param>
    /// <returns>보정된 신 지도 좌표</returns>
    public (double x, double y)? TransformWithMapping(string mapKey, double gameX, double gameZ, double[] mapping)
    {
        // 1. 게임 좌표 → 구 지도 좌표
        var oldPos = _oldTransform.TransformToOldScreen(mapKey, gameX, gameZ);
        if (oldPos == null)
            return null;

        // 2. 구 좌표를 신 맵 비율로 스케일링
        var scaledPos = _oldTransform.ScaleToNewMap(mapKey, oldPos.Value.x, oldPos.Value.y);
        if (scaledPos == null)
            return null;

        // 3. 매핑 적용
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
    /// 보정 결과를 기반으로 새로운 CalibratedTransform을 계산합니다.
    /// 이 값은 MapConfig에 저장하여 IDW 없이도 정확한 변환을 수행할 수 있습니다.
    /// </summary>
    public double[]? CalculateNewCalibratedTransform(string mapKey, double[] oldToNewMapping)
    {
        var reference = _oldTransform.GetReferenceData(mapKey);
        if (reference == null || oldToNewMapping == null || oldToNewMapping.Length < 6)
            return null;

        // 여러 테스트 포인트에서 게임좌표→신좌표 매핑을 수집
        var gameToScreenPoints = new List<(double gameX, double gameZ, double screenX, double screenY)>();

        // 맵 영역을 그리드로 나눠서 테스트 포인트 생성
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

        // 게임좌표 → 화면좌표 affine 변환 계산
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
    /// <summary>
    /// 맵 키
    /// </summary>
    public string MapKey { get; set; } = string.Empty;

    /// <summary>
    /// 성공 여부
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 오류 메시지 (실패 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 사용된 참조 포인트 수
    /// </summary>
    public int ReferencePointCount { get; set; }

    /// <summary>
    /// 구→신 매핑 행렬 [a, b, c, d, tx, ty]
    /// </summary>
    public double[]? OldToNewMapping { get; set; }

    /// <summary>
    /// 오차 분석 결과
    /// </summary>
    public MappingAnalysis? Analysis { get; set; }
}
