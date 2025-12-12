namespace TarkovDBEditor.Services;

/// <summary>
/// Thin Plate Spline (TPS) 2D 좌표 변환 서비스
/// SVG 좌표 → 게임 좌표 비선형 보간
///
/// 수학적 기반:
/// f(x, y) = a₀ + a₁x + a₂y + Σᵢ wᵢ · U(rᵢ)
/// U(r) = r² · ln(r) (Radial Basis Function)
/// </summary>
public class ThinPlateSplineTransform
{
    // 참조점 데이터
    private readonly List<(double srcX, double srcY)> _sourcePoints;
    private readonly List<(double tgtX, double tgtZ)> _targetPoints;
    private readonly int _n; // 참조점 개수

    // TPS 계수 (학습 결과)
    private double[]? _weightsX;    // X 변환 가중치 (N개)
    private double[]? _weightsZ;    // Z 변환 가중치 (N개)
    private double[]? _affineX;     // X Affine 계수 [a0, a1, a2]
    private double[]? _affineZ;     // Z Affine 계수 [a0, a1, a2]

    // 설정
    private readonly double _lambda;  // 정규화 파라미터
    private bool _isComputed;

    // 통계
    public double MeanError { get; private set; }
    public double MaxError { get; private set; }
    public int ReferencePointCount => _n;
    public double Lambda => _lambda;
    public bool IsComputed => _isComputed;

    /// <summary>
    /// 참조점으로 TPS 모델 생성
    /// </summary>
    /// <param name="referencePoints">참조점 목록 (SVG X, SVG Y, Game X, Game Z)</param>
    /// <param name="lambda">정규화 파라미터 (0 = 완벽 보간, >0 = 부드러운 근사)</param>
    public ThinPlateSplineTransform(
        List<(double svgX, double svgY, double gameX, double gameZ)> referencePoints,
        double lambda = 0.0)
    {
        if (referencePoints.Count < 3)
            throw new ArgumentException("TPS requires at least 3 reference points", nameof(referencePoints));

        _sourcePoints = referencePoints.Select(p => (p.svgX, p.svgY)).ToList();
        _targetPoints = referencePoints.Select(p => (p.gameX, p.gameZ)).ToList();
        _n = referencePoints.Count;
        _lambda = Math.Max(0, lambda);
        _isComputed = false;
    }

    /// <summary>
    /// TPS 계수 계산 (학습)
    /// </summary>
    /// <returns>성공 여부</returns>
    public bool Compute()
    {
        try
        {
            // 행렬 크기: (N+3) x (N+3)
            int size = _n + 3;
            var matrix = new double[size, size];
            var targetX = new double[size];
            var targetZ = new double[size];

            // K 행렬 구성 (N x N): K[i,j] = U(||pi - pj||)
            for (int i = 0; i < _n; i++)
            {
                for (int j = 0; j < _n; j++)
                {
                    if (i == j)
                    {
                        // 대각선: 정규화 파라미터 적용
                        matrix[i, j] = _lambda;
                    }
                    else
                    {
                        double r = Distance(_sourcePoints[i], _sourcePoints[j]);
                        matrix[i, j] = TpsBasisFunction(r);
                    }
                }
            }

            // P 행렬 구성 (N x 3): [1, x, y]
            for (int i = 0; i < _n; i++)
            {
                matrix[i, _n] = 1.0;
                matrix[i, _n + 1] = _sourcePoints[i].srcX;
                matrix[i, _n + 2] = _sourcePoints[i].srcY;

                // P^T (3 x N)
                matrix[_n, i] = 1.0;
                matrix[_n + 1, i] = _sourcePoints[i].srcX;
                matrix[_n + 2, i] = _sourcePoints[i].srcY;
            }

            // 우측 하단 3x3은 0 (이미 초기화됨)

            // 타겟 벡터 구성
            for (int i = 0; i < _n; i++)
            {
                targetX[i] = _targetPoints[i].tgtX;
                targetZ[i] = _targetPoints[i].tgtZ;
            }
            // 마지막 3개는 0 (이미 초기화됨)

            // 선형 시스템 풀기
            var solutionX = SolveLinearSystem(matrix, targetX);
            var solutionZ = SolveLinearSystem(matrix, targetZ);

            if (solutionX == null || solutionZ == null)
            {
                System.Diagnostics.Debug.WriteLine("[TPS] Failed to solve linear system");
                return false;
            }

            // 결과 분리: 가중치 (N개) + Affine 계수 (3개)
            _weightsX = solutionX.Take(_n).ToArray();
            _weightsZ = solutionZ.Take(_n).ToArray();
            _affineX = solutionX.Skip(_n).Take(3).ToArray();
            _affineZ = solutionZ.Skip(_n).Take(3).ToArray();

            _isComputed = true;

            // 오차 계산
            CalculateErrors();

            System.Diagnostics.Debug.WriteLine($"[TPS] Computed successfully: {_n} points, mean error={MeanError:F4}, max error={MaxError:F4}");

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TPS] Compute error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// SVG 좌표를 게임 좌표로 변환
    /// </summary>
    public (double gameX, double gameZ) Transform(double svgX, double svgY)
    {
        if (!_isComputed || _weightsX == null || _weightsZ == null ||
            _affineX == null || _affineZ == null)
        {
            throw new InvalidOperationException("TPS not computed. Call Compute() first.");
        }

        // Affine 부분: a0 + a1*x + a2*y
        double resultX = _affineX[0] + _affineX[1] * svgX + _affineX[2] * svgY;
        double resultZ = _affineZ[0] + _affineZ[1] * svgX + _affineZ[2] * svgY;

        // TPS 부분: Σ wi * U(ri)
        for (int i = 0; i < _n; i++)
        {
            double r = Distance((svgX, svgY), _sourcePoints[i]);
            if (r > 1e-10) // 0이 아닌 경우만 (U(0) = 0)
            {
                double u = TpsBasisFunction(r);
                resultX += _weightsX[i] * u;
                resultZ += _weightsZ[i] * u;
            }
        }

        return (resultX, resultZ);
    }

    /// <summary>
    /// 여러 점을 한 번에 변환 (배치 처리)
    /// </summary>
    public List<(double gameX, double gameZ)> TransformBatch(List<(double svgX, double svgY)> points)
    {
        return points.Select(p => Transform(p.svgX, p.svgY)).ToList();
    }

    /// <summary>
    /// 참조점에 대한 오차 계산
    /// </summary>
    private void CalculateErrors()
    {
        if (!_isComputed) return;

        double sumError = 0;
        double maxErr = 0;

        for (int i = 0; i < _n; i++)
        {
            var (calcX, calcZ) = Transform(_sourcePoints[i].srcX, _sourcePoints[i].srcY);
            var (targetX, targetZ) = _targetPoints[i];

            double dx = calcX - targetX;
            double dz = calcZ - targetZ;
            double error = Math.Sqrt(dx * dx + dz * dz);

            sumError += error;
            maxErr = Math.Max(maxErr, error);
        }

        MeanError = sumError / _n;
        MaxError = maxErr;
    }

    /// <summary>
    /// TPS 기본 함수: U(r) = r² * ln(r)
    /// </summary>
    private static double TpsBasisFunction(double r)
    {
        if (r < 1e-10) return 0.0;
        return r * r * Math.Log(r);
    }

    /// <summary>
    /// 두 점 사이의 유클리드 거리
    /// </summary>
    private static double Distance((double x, double y) p1, (double x, double y) p2)
    {
        double dx = p1.x - p2.x;
        double dy = p1.y - p2.y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 선형 시스템 Ax = b 풀기 (Gaussian Elimination with Partial Pivoting)
    /// </summary>
    private static double[]? SolveLinearSystem(double[,] A, double[] b)
    {
        int n = b.Length;

        // Augmented matrix 생성 (원본 보존)
        var aug = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                aug[i, j] = A[i, j];
            aug[i, n] = b[i];
        }

        // Gaussian elimination with partial pivoting
        for (int col = 0; col < n; col++)
        {
            // Pivot 선택: 현재 열에서 가장 큰 절대값 찾기
            int maxRow = col;
            double maxVal = Math.Abs(aug[col, col]);
            for (int row = col + 1; row < n; row++)
            {
                double val = Math.Abs(aug[row, col]);
                if (val > maxVal)
                {
                    maxVal = val;
                    maxRow = row;
                }
            }

            // Pivot이 너무 작으면 특이 행렬
            if (maxVal < 1e-12)
            {
                System.Diagnostics.Debug.WriteLine($"[TPS Solver] Near-singular matrix at column {col}");
                return null;
            }

            // Swap rows
            if (maxRow != col)
            {
                for (int j = col; j <= n; j++)
                {
                    (aug[col, j], aug[maxRow, j]) = (aug[maxRow, j], aug[col, j]);
                }
            }

            // Eliminate column
            for (int row = col + 1; row < n; row++)
            {
                double factor = aug[row, col] / aug[col, col];
                for (int j = col; j <= n; j++)
                {
                    aug[row, j] -= factor * aug[col, j];
                }
            }
        }

        // Back substitution
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            x[i] = aug[i, n];
            for (int j = i + 1; j < n; j++)
            {
                x[i] -= aug[i, j] * x[j];
            }
            x[i] /= aug[i, i];

            // NaN/Inf 체크
            if (double.IsNaN(x[i]) || double.IsInfinity(x[i]))
            {
                System.Diagnostics.Debug.WriteLine($"[TPS Solver] Invalid result at index {i}");
                return null;
            }
        }

        return x;
    }

    /// <summary>
    /// 디버그 정보 출력
    /// </summary>
    public string GetDebugInfo()
    {
        if (!_isComputed)
            return "TPS not computed";

        return $"TPS Transform:\n" +
               $"  Reference Points: {_n}\n" +
               $"  Lambda: {_lambda:E2}\n" +
               $"  Mean Error: {MeanError:F4}\n" +
               $"  Max Error: {MaxError:F4}\n" +
               $"  Affine X: [{_affineX![0]:F4}, {_affineX[1]:F4}, {_affineX[2]:F4}]\n" +
               $"  Affine Z: [{_affineZ![0]:F4}, {_affineZ[1]:F4}, {_affineZ[2]:F4}]";
    }
}

/// <summary>
/// TPS 변환 팩토리 (CoordinateTransformService 연동용)
/// </summary>
public static class TpsTransformFactory
{
    /// <summary>
    /// 참조점으로 TPS 변환 생성
    /// </summary>
    /// <param name="referencePoints">(DB X, DB Z, SVG X, SVG Y) 형식의 참조점</param>
    /// <param name="lambda">정규화 파라미터</param>
    /// <returns>성공 시 TPS 인스턴스, 실패 시 null</returns>
    public static ThinPlateSplineTransform? Create(
        List<(double dbX, double dbZ, double svgX, double svgY)> referencePoints,
        double lambda = 0.0)
    {
        if (referencePoints.Count < 3)
        {
            System.Diagnostics.Debug.WriteLine("[TPS Factory] Need at least 3 reference points");
            return null;
        }

        try
        {
            // (SVG X, SVG Y, Game X, Game Z) 형식으로 변환
            var tpsPoints = referencePoints
                .Select(p => (p.svgX, p.svgY, p.dbX, p.dbZ))
                .ToList();

            var tps = new ThinPlateSplineTransform(tpsPoints, lambda);

            if (tps.Compute())
            {
                return tps;
            }

            System.Diagnostics.Debug.WriteLine("[TPS Factory] Computation failed");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TPS Factory] Error: {ex.Message}");
            return null;
        }
    }
}
