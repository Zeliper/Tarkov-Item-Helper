using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// 맵 좌표 보정 서비스.
/// 레퍼런스 포인트를 기반으로 affine 변환 행렬을 계산합니다.
/// </summary>
public sealed class MapCalibrationService
{
    private static MapCalibrationService? _instance;
    public static MapCalibrationService Instance => _instance ??= new MapCalibrationService();

    private MapCalibrationService() { }

    /// <summary>
    /// 보정 포인트에서 affine 변환 행렬을 계산합니다.
    /// 최소 3개의 포인트가 필요합니다 (affine 변환은 6개의 매개변수가 있음).
    /// </summary>
    /// <param name="points">보정 포인트 목록</param>
    /// <returns>변환 행렬 [a, b, c, d, tx, ty] 또는 null</returns>
    public double[]? CalculateAffineTransform(List<CalibrationPoint> points)
    {
        if (points == null || points.Count < 3)
            return null;

        // 최소제곱법으로 affine 변환 계산
        // screenX = a*gameX + b*gameZ + tx
        // screenY = c*gameX + d*gameZ + ty

        var n = points.Count;

        // X 방향 계산 (screenX = a*gameX + b*gameZ + tx)
        var xResult = SolveLinearRegression(
            points.Select(p => p.GameX).ToArray(),
            points.Select(p => p.GameZ).ToArray(),
            points.Select(p => p.ScreenX).ToArray());

        // Y 방향 계산 (screenY = c*gameX + d*gameZ + ty)
        var yResult = SolveLinearRegression(
            points.Select(p => p.GameX).ToArray(),
            points.Select(p => p.GameZ).ToArray(),
            points.Select(p => p.ScreenY).ToArray());

        if (xResult == null || yResult == null)
            return null;

        // [a, b, c, d, tx, ty]
        return [xResult[0], xResult[1], yResult[0], yResult[1], xResult[2], yResult[2]];
    }

    /// <summary>
    /// 2변수 선형 회귀를 풀어 계수를 반환합니다.
    /// target = coef1*x1 + coef2*x2 + intercept
    /// </summary>
    private double[]? SolveLinearRegression(double[] x1, double[] x2, double[] target)
    {
        var n = x1.Length;
        if (n < 3) return null;

        // 정규 방정식 행렬 구성
        double sumX1 = 0, sumX2 = 0, sumT = 0;
        double sumX1X1 = 0, sumX2X2 = 0, sumX1X2 = 0;
        double sumX1T = 0, sumX2T = 0;

        for (int i = 0; i < n; i++)
        {
            sumX1 += x1[i];
            sumX2 += x2[i];
            sumT += target[i];
            sumX1X1 += x1[i] * x1[i];
            sumX2X2 += x2[i] * x2[i];
            sumX1X2 += x1[i] * x2[i];
            sumX1T += x1[i] * target[i];
            sumX2T += x2[i] * target[i];
        }

        // 3x3 행렬 풀기 (Cramer's rule 사용)
        var a = new double[,]
        {
            { sumX1X1, sumX1X2, sumX1 },
            { sumX1X2, sumX2X2, sumX2 },
            { sumX1, sumX2, n }
        };
        var b = new double[] { sumX1T, sumX2T, sumT };

        var result = SolveLinearSystem3x3(a, b);
        return result;
    }

    /// <summary>
    /// 3x3 선형 시스템을 풀어 해를 반환합니다.
    /// </summary>
    private double[]? SolveLinearSystem3x3(double[,] a, double[] b)
    {
        // Gaussian elimination with partial pivoting
        var n = 3;
        var aug = new double[n, n + 1];

        // Augmented matrix 구성
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                aug[i, j] = a[i, j];
            aug[i, n] = b[i];
        }

        // Forward elimination
        for (int col = 0; col < n; col++)
        {
            // Pivot 선택
            int maxRow = col;
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(aug[row, col]) > Math.Abs(aug[maxRow, col]))
                    maxRow = row;
            }

            // 행 교환
            if (maxRow != col)
            {
                for (int j = 0; j <= n; j++)
                    (aug[col, j], aug[maxRow, j]) = (aug[maxRow, j], aug[col, j]);
            }

            // 0으로 나누기 방지
            if (Math.Abs(aug[col, col]) < 1e-10)
                return null;

            // 제거
            for (int row = col + 1; row < n; row++)
            {
                var factor = aug[row, col] / aug[col, col];
                for (int j = col; j <= n; j++)
                    aug[row, j] -= factor * aug[col, j];
            }
        }

        // Back substitution
        var result = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            result[i] = aug[i, n];
            for (int j = i + 1; j < n; j++)
                result[i] -= aug[i, j] * result[j];
            result[i] /= aug[i, i];
        }

        return result;
    }

    /// <summary>
    /// 보정된 변환을 사용하여 게임 좌표를 화면 좌표로 변환합니다.
    /// 순수 affine 변환만 적용합니다.
    /// </summary>
    /// <param name="calibratedTransform">변환 행렬 [a, b, c, d, tx, ty]</param>
    /// <param name="gameX">게임 X 좌표</param>
    /// <param name="gameZ">게임 Z 좌표</param>
    /// <returns>(screenX, screenY)</returns>
    public (double screenX, double screenY) ApplyCalibratedTransform(double[] calibratedTransform, double gameX, double gameZ)
    {
        if (calibratedTransform == null || calibratedTransform.Length < 6)
            return (0, 0);

        var a = calibratedTransform[0];
        var b = calibratedTransform[1];
        var c = calibratedTransform[2];
        var d = calibratedTransform[3];
        var tx = calibratedTransform[4];
        var ty = calibratedTransform[5];

        var screenX = a * gameX + b * gameZ + tx;
        var screenY = c * gameX + d * gameZ + ty;

        return (screenX, screenY);
    }

    /// <summary>
    /// IDW(Inverse Distance Weighting) 보정을 적용하여 게임 좌표를 화면 좌표로 변환합니다.
    /// 보정 포인트 근처에서는 더 정확한 위치를 반환합니다.
    /// </summary>
    /// <param name="calibratedTransform">변환 행렬 [a, b, c, d, tx, ty]</param>
    /// <param name="calibrationPoints">보정 포인트 목록</param>
    /// <param name="gameX">게임 X 좌표</param>
    /// <param name="gameZ">게임 Z 좌표</param>
    /// <param name="power">IDW 거리 가중치 지수 (기본값 2.0)</param>
    /// <returns>(screenX, screenY)</returns>
    public (double screenX, double screenY) ApplyCalibratedTransformWithIDW(
        double[] calibratedTransform,
        List<CalibrationPoint>? calibrationPoints,
        double gameX,
        double gameZ,
        double power = 2.0)
    {
        // 보정 포인트가 없으면 순수 affine 변환 사용
        if (calibrationPoints == null || calibrationPoints.Count == 0)
            return ApplyCalibratedTransform(calibratedTransform, gameX, gameZ);

        // 1. 기본 affine 변환 결과 계산
        var (affineX, affineY) = ApplyCalibratedTransform(calibratedTransform, gameX, gameZ);

        // 2. 각 보정 포인트에 대한 오차 계산
        var errors = new List<(double errorX, double errorY, double distance)>();
        const double epsilon = 0.001; // 매우 가까운 점 처리용

        foreach (var point in calibrationPoints)
        {
            // 이 보정 포인트에서 affine 변환이 예측하는 위치
            var (predictedX, predictedY) = ApplyCalibratedTransform(calibratedTransform, point.GameX, point.GameZ);

            // 오차 = 실제 화면 위치 - 예측 위치
            var errorX = point.ScreenX - predictedX;
            var errorY = point.ScreenY - predictedY;

            // 게임 좌표 공간에서의 거리 계산
            var dx = gameX - point.GameX;
            var dz = gameZ - point.GameZ;
            var distance = Math.Sqrt(dx * dx + dz * dz);

            // 입력 좌표가 보정 포인트와 매우 가까우면 해당 포인트의 화면 좌표 직접 반환
            if (distance < epsilon)
                return (point.ScreenX, point.ScreenY);

            errors.Add((errorX, errorY, distance));
        }

        // 3. IDW로 가중 평균 오차 계산
        double weightedErrorX = 0, weightedErrorY = 0, totalWeight = 0;

        foreach (var (errorX, errorY, distance) in errors)
        {
            var weight = 1.0 / Math.Pow(distance, power);
            weightedErrorX += errorX * weight;
            weightedErrorY += errorY * weight;
            totalWeight += weight;
        }

        if (totalWeight > 0)
        {
            weightedErrorX /= totalWeight;
            weightedErrorY /= totalWeight;
        }

        // 4. 보정된 위치 반환
        return (affineX + weightedErrorX, affineY + weightedErrorY);
    }

    /// <summary>
    /// 맵 설정에 보정 포인트를 추가하고 변환 행렬을 재계산합니다.
    /// </summary>
    public bool AddCalibrationPoint(MapConfig config, CalibrationPoint point)
    {
        config.CalibrationPoints ??= new List<CalibrationPoint>();

        // 기존 포인트 업데이트 또는 추가
        var existing = config.CalibrationPoints.FirstOrDefault(p => p.Id == point.Id);
        if (existing != null)
        {
            existing.ScreenX = point.ScreenX;
            existing.ScreenY = point.ScreenY;
        }
        else
        {
            config.CalibrationPoints.Add(point);
        }

        // 3개 이상이면 변환 행렬 재계산
        if (config.CalibrationPoints.Count >= 3)
        {
            config.CalibratedTransform = CalculateAffineTransform(config.CalibrationPoints);
            return config.CalibratedTransform != null;
        }

        return false;
    }

    /// <summary>
    /// 맵 설정에서 보정 포인트를 제거합니다.
    /// </summary>
    public void RemoveCalibrationPoint(MapConfig config, string pointId)
    {
        if (config.CalibrationPoints == null) return;

        config.CalibrationPoints.RemoveAll(p => p.Id == pointId);

        // 재계산
        if (config.CalibrationPoints.Count >= 3)
        {
            config.CalibratedTransform = CalculateAffineTransform(config.CalibrationPoints);
        }
        else
        {
            config.CalibratedTransform = null;
        }
    }

    /// <summary>
    /// 모든 보정 포인트를 제거합니다.
    /// </summary>
    public void ClearCalibrationPoints(MapConfig config)
    {
        config.CalibrationPoints = null;
        config.CalibratedTransform = null;
    }
}
