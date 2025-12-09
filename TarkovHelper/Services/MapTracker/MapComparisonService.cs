using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// 구/신 지도 비교 분석 및 매핑 계산 서비스.
/// </summary>
public sealed class MapComparisonService
{
    private static MapComparisonService? _instance;
    public static MapComparisonService Instance => _instance ??= new MapComparisonService();

    private readonly OldMapTransformService _oldTransform;

    private MapComparisonService()
    {
        _oldTransform = OldMapTransformService.Instance;
    }

    /// <summary>
    /// 구→신 좌표 매핑을 계산합니다.
    /// 여러 대응점을 기반으로 affine 변환 행렬을 계산합니다.
    /// </summary>
    /// <param name="correspondences">대응점 목록 (oldX, oldY, newX, newY)</param>
    /// <returns>변환 행렬 [a, b, c, d, tx, ty] 또는 null</returns>
    public double[]? CalculateOldToNewMapping(List<(double oldX, double oldY, double newX, double newY)> correspondences)
    {
        if (correspondences == null || correspondences.Count < 3)
            return null;

        // 최소제곱법으로 affine 변환 계산
        // newX = a*oldX + b*oldY + tx
        // newY = c*oldX + d*oldY + ty

        var xResult = SolveLinearRegression(
            correspondences.Select(c => c.oldX).ToArray(),
            correspondences.Select(c => c.oldY).ToArray(),
            correspondences.Select(c => c.newX).ToArray());

        var yResult = SolveLinearRegression(
            correspondences.Select(c => c.oldX).ToArray(),
            correspondences.Select(c => c.oldY).ToArray(),
            correspondences.Select(c => c.newY).ToArray());

        if (xResult == null || yResult == null)
            return null;

        // [a, b, c, d, tx, ty]
        return [xResult[0], xResult[1], yResult[0], yResult[1], xResult[2], yResult[2]];
    }

    /// <summary>
    /// 매핑을 적용하여 구 좌표를 신 좌표로 변환합니다.
    /// </summary>
    public (double x, double y) ApplyMapping(double[] mapping, double oldX, double oldY)
    {
        if (mapping == null || mapping.Length < 6)
            return (oldX, oldY);

        var a = mapping[0];
        var b = mapping[1];
        var c = mapping[2];
        var d = mapping[3];
        var tx = mapping[4];
        var ty = mapping[5];

        var newX = a * oldX + b * oldY + tx;
        var newY = c * oldX + d * oldY + ty;

        return (newX, newY);
    }

    /// <summary>
    /// 매핑의 오차를 분석합니다.
    /// </summary>
    public MappingAnalysis AnalyzeMapping(
        double[] mapping,
        List<(double oldX, double oldY, double expectedNewX, double expectedNewY)> testPoints)
    {
        var errors = new List<(double x, double y, double error)>();
        double sumSquaredError = 0;
        double maxError = 0;
        double minError = double.MaxValue;

        foreach (var (oldX, oldY, expectedNewX, expectedNewY) in testPoints)
        {
            var (predictedX, predictedY) = ApplyMapping(mapping, oldX, oldY);
            var dx = predictedX - expectedNewX;
            var dy = predictedY - expectedNewY;
            var error = Math.Sqrt(dx * dx + dy * dy);

            errors.Add((oldX, oldY, error));
            sumSquaredError += error * error;
            maxError = Math.Max(maxError, error);
            minError = Math.Min(minError, error);
        }

        return new MappingAnalysis
        {
            MeanSquaredError = testPoints.Count > 0 ? sumSquaredError / testPoints.Count : 0,
            MeanError = testPoints.Count > 0 ? Math.Sqrt(sumSquaredError / testPoints.Count) : 0,
            MaxError = maxError,
            MinError = minError == double.MaxValue ? 0 : minError,
            ErrorDistribution = errors
        };
    }

    /// <summary>
    /// 게임 좌표에서 구 지도 좌표를 계산합니다.
    /// </summary>
    public (double x, double y)? GetOldScreenPosition(string mapKey, double gameX, double gameZ)
    {
        return _oldTransform.TransformToOldScreen(mapKey, gameX, gameZ);
    }

    /// <summary>
    /// 2변수 선형 회귀를 풀어 계수를 반환합니다.
    /// target = coef1*x1 + coef2*x2 + intercept
    /// </summary>
    private double[]? SolveLinearRegression(double[] x1, double[] x2, double[] target)
    {
        var n = x1.Length;
        if (n < 3) return null;

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

        var a = new double[,]
        {
            { sumX1X1, sumX1X2, sumX1 },
            { sumX1X2, sumX2X2, sumX2 },
            { sumX1, sumX2, n }
        };
        var b = new double[] { sumX1T, sumX2T, sumT };

        return SolveLinearSystem3x3(a, b);
    }

    /// <summary>
    /// 3x3 선형 시스템을 풀어 해를 반환합니다.
    /// </summary>
    private double[]? SolveLinearSystem3x3(double[,] a, double[] b)
    {
        var n = 3;
        var aug = new double[n, n + 1];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                aug[i, j] = a[i, j];
            aug[i, n] = b[i];
        }

        for (int col = 0; col < n; col++)
        {
            int maxRow = col;
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(aug[row, col]) > Math.Abs(aug[maxRow, col]))
                    maxRow = row;
            }

            if (maxRow != col)
            {
                for (int j = 0; j <= n; j++)
                    (aug[col, j], aug[maxRow, j]) = (aug[maxRow, j], aug[col, j]);
            }

            if (Math.Abs(aug[col, col]) < 1e-10)
                return null;

            for (int row = col + 1; row < n; row++)
            {
                var factor = aug[row, col] / aug[col, col];
                for (int j = col; j <= n; j++)
                    aug[row, j] -= factor * aug[col, j];
            }
        }

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
}

/// <summary>
/// 매핑 분석 결과
/// </summary>
public sealed class MappingAnalysis
{
    /// <summary>
    /// 평균 제곱 오차 (MSE)
    /// </summary>
    public double MeanSquaredError { get; set; }

    /// <summary>
    /// 평균 오차 (RMSE)
    /// </summary>
    public double MeanError { get; set; }

    /// <summary>
    /// 최대 오차
    /// </summary>
    public double MaxError { get; set; }

    /// <summary>
    /// 최소 오차
    /// </summary>
    public double MinError { get; set; }

    /// <summary>
    /// 오차 분포 (좌표별 오차 크기)
    /// </summary>
    public List<(double x, double y, double error)> ErrorDistribution { get; set; } = new();
}
