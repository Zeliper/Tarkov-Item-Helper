using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services;

/// <summary>
/// Tarkov Market API 서비스
/// 마커 데이터 가져오기 및 디코딩
/// </summary>
public class TarkovMarketService : IDisposable
{
    private readonly HttpClient _httpClient;
    private const string MarkersApiBase = "https://tarkov-market.com/api/be/markers/list";
    private const string QuestsApiBase = "https://tarkov-market.com/api/be/quests/list";

    // 캐시된 퀘스트 목록 (앱 실행 중 유지)
    private List<TarkovMarketQuest>? _cachedQuests;

    private readonly string _cacheDir;

    /// <summary>
    /// 지원하는 맵 이름 목록 (API용)
    /// </summary>
    public static readonly Dictionary<string, string> MapNameMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Customs", "customs" },
        { "Factory", "factory" },
        { "Interchange", "interchange" },
        { "Labs", "labs" },
        { "Lighthouse", "lighthouse" },
        { "Reserve", "reserve" },
        { "Shoreline", "shoreline" },
        { "StreetsOfTarkov", "streets" },
        { "Woods", "woods" },
        { "GroundZero", "ground-zero" },
        { "Labyrinth", "labyrinth" }
    };

    public TarkovMarketService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TarkovDBEditor/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        _cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "tarkov_market");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// 특정 맵의 마커 데이터 가져오기
    /// </summary>
    public async Task<List<TarkovMarketMarker>> FetchMarkersAsync(
        string mapKey,
        bool useCache = true,
        CancellationToken cancellationToken = default)
    {
        // API용 맵 이름 변환
        if (!MapNameMapping.TryGetValue(mapKey, out var apiMapName))
        {
            apiMapName = mapKey.ToLowerInvariant();
        }

        // 캐시 확인
        var cacheFile = Path.Combine(_cacheDir, $"markers_{apiMapName}.json");
        if (useCache && File.Exists(cacheFile))
        {
            var cacheAge = DateTime.Now - File.GetLastWriteTime(cacheFile);
            if (cacheAge.TotalHours < 24) // 24시간 캐시
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(cacheFile, cancellationToken);
                    var cachedMarkers = JsonSerializer.Deserialize<List<TarkovMarketMarker>>(cachedJson);
                    if (cachedMarkers != null)
                    {
                        return cachedMarkers;
                    }
                }
                catch
                {
                    // 캐시 읽기 실패 시 API 호출
                }
            }
        }

        // API 호출
        var url = $"{MarkersApiBase}?map={apiMapName}";
        System.Diagnostics.Debug.WriteLine($"[FetchMarkers] Calling API: {url}");

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        System.Diagnostics.Debug.WriteLine($"[FetchMarkers] Response length: {json.Length}");

        var apiResponse = JsonSerializer.Deserialize<TarkovMarketMarkersResponse>(json);

        if (apiResponse == null)
        {
            System.Diagnostics.Debug.WriteLine("[FetchMarkers] Failed to parse API response");
            return new List<TarkovMarketMarker>();
        }

        if (string.IsNullOrEmpty(apiResponse.Markers))
        {
            System.Diagnostics.Debug.WriteLine("[FetchMarkers] Markers field is empty");
            return new List<TarkovMarketMarker>();
        }

        System.Diagnostics.Debug.WriteLine($"[FetchMarkers] Markers field length: {apiResponse.Markers.Length}");

        // 난독화 디코딩
        var markers = DecodeMarkers(apiResponse.Markers);
        if (markers == null)
        {
            System.Diagnostics.Debug.WriteLine("[FetchMarkers] DecodeMarkers returned null");
            return new List<TarkovMarketMarker>();
        }

        // 캐시 저장
        try
        {
            var cacheJson = JsonSerializer.Serialize(markers, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, cacheJson, cancellationToken);
        }
        catch
        {
            // 캐시 저장 실패는 무시
        }

        return markers;
    }

    /// <summary>
    /// 퀘스트 데이터 가져오기 (마커의 questUid와 매칭용)
    /// </summary>
    public async Task<List<TarkovMarketQuest>> FetchQuestsAsync(
        bool useCache = true,
        CancellationToken cancellationToken = default)
    {
        // 메모리 캐시 확인
        if (useCache && _cachedQuests != null)
        {
            return _cachedQuests;
        }

        // 파일 캐시 확인
        var cacheFile = Path.Combine(_cacheDir, "quests.json");
        if (useCache && File.Exists(cacheFile))
        {
            var cacheAge = DateTime.Now - File.GetLastWriteTime(cacheFile);
            if (cacheAge.TotalHours < 24) // 24시간 캐시
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(cacheFile, cancellationToken);
                    var cachedQuests = JsonSerializer.Deserialize<List<TarkovMarketQuest>>(cachedJson);
                    if (cachedQuests != null)
                    {
                        _cachedQuests = cachedQuests;
                        return cachedQuests;
                    }
                }
                catch
                {
                    // 캐시 읽기 실패 시 API 호출
                }
            }
        }

        // API 호출
        System.Diagnostics.Debug.WriteLine($"[FetchQuests] Calling API: {QuestsApiBase}");

        var response = await _httpClient.GetAsync(QuestsApiBase, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        System.Diagnostics.Debug.WriteLine($"[FetchQuests] Response length: {json.Length}");

        var apiResponse = JsonSerializer.Deserialize<TarkovMarketQuestsResponse>(json);

        if (apiResponse == null || string.IsNullOrEmpty(apiResponse.Quests))
        {
            System.Diagnostics.Debug.WriteLine("[FetchQuests] Quests field is empty");
            return new List<TarkovMarketQuest>();
        }

        // 난독화 디코딩
        var quests = DecodeQuests(apiResponse.Quests);
        if (quests == null)
        {
            System.Diagnostics.Debug.WriteLine("[FetchQuests] DecodeQuests returned null");
            return new List<TarkovMarketQuest>();
        }

        // 메모리 캐시
        _cachedQuests = quests;

        // 파일 캐시 저장
        try
        {
            var cacheJson = JsonSerializer.Serialize(quests, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, cacheJson, cancellationToken);
        }
        catch
        {
            // 캐시 저장 실패는 무시
        }

        return quests;
    }

    /// <summary>
    /// questUid로 퀘스트 찾기
    /// </summary>
    public TarkovMarketQuest? FindQuestByUid(string? questUid)
    {
        if (string.IsNullOrEmpty(questUid) || _cachedQuests == null)
        {
            return null;
        }

        return _cachedQuests.FirstOrDefault(q => q.Uid == questUid);
    }

    /// <summary>
    /// 난독화된 마커 데이터 디코딩
    /// 알고리즘: index 5~9 (5글자) 제거 → Base64 디코드 → URL 디코드 → JSON 파싱
    /// </summary>
    public static List<TarkovMarketMarker>? DecodeMarkers(string encoded)
    {
        try
        {
            if (string.IsNullOrEmpty(encoded) || encoded.Length < 11)
            {
                System.Diagnostics.Debug.WriteLine($"[DecodeMarkers] Invalid input: length={encoded?.Length ?? 0}");
                return null;
            }

            // 1. index 5~9 (5글자) 제거
            var processed = encoded.Substring(0, 5) + encoded.Substring(10);
            System.Diagnostics.Debug.WriteLine($"[DecodeMarkers] Processed: {processed.Substring(0, Math.Min(50, processed.Length))}...");

            // 2. Base64 디코드
            var bytes = Convert.FromBase64String(processed);
            var urlEncoded = Encoding.UTF8.GetString(bytes);
            System.Diagnostics.Debug.WriteLine($"[DecodeMarkers] URL encoded: {urlEncoded.Substring(0, Math.Min(100, urlEncoded.Length))}...");

            // 3. URL 디코드
            var json = Uri.UnescapeDataString(urlEncoded);
            System.Diagnostics.Debug.WriteLine($"[DecodeMarkers] JSON length: {json.Length}");

            // 4. JSON 파싱
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };
            var result = JsonSerializer.Deserialize<List<TarkovMarketMarker>>(json, options);
            System.Diagnostics.Debug.WriteLine($"[DecodeMarkers] Parsed {result?.Count ?? 0} markers");
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DecodeMarkers] Error: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[DecodeMarkers] Stack: {ex.StackTrace}");
            return null;
        }
    }

    /// <summary>
    /// 난독화된 퀘스트 데이터 디코딩
    /// 알고리즘: index 5~9 (5글자) 제거 → Base64 디코드 → URL 디코드 → JSON 파싱
    /// </summary>
    public static List<TarkovMarketQuest>? DecodeQuests(string encoded)
    {
        try
        {
            if (string.IsNullOrEmpty(encoded) || encoded.Length < 11)
            {
                System.Diagnostics.Debug.WriteLine($"[DecodeQuests] Invalid input: length={encoded?.Length ?? 0}");
                return null;
            }

            // 1. index 5~9 (5글자) 제거
            var processed = encoded.Substring(0, 5) + encoded.Substring(10);

            // 2. Base64 디코드
            var bytes = Convert.FromBase64String(processed);
            var urlEncoded = Encoding.UTF8.GetString(bytes);

            // 3. URL 디코드
            var json = Uri.UnescapeDataString(urlEncoded);
            System.Diagnostics.Debug.WriteLine($"[DecodeQuests] JSON length: {json.Length}");

            // 4. JSON 파싱
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
            };
            var result = JsonSerializer.Deserialize<List<TarkovMarketQuest>>(json, options);
            System.Diagnostics.Debug.WriteLine($"[DecodeQuests] Parsed {result?.Count ?? 0} quests");
            return result;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DecodeQuests] Error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 캐시 삭제
    /// </summary>
    public void ClearCache()
    {
        try
        {
            _cachedQuests = null; // 메모리 캐시 삭제

            if (Directory.Exists(_cacheDir))
            {
                foreach (var file in Directory.GetFiles(_cacheDir, "markers_*.json"))
                {
                    File.Delete(file);
                }

                // 퀘스트 캐시 삭제
                var questCacheFile = Path.Combine(_cacheDir, "quests.json");
                if (File.Exists(questCacheFile))
                {
                    File.Delete(questCacheFile);
                }
            }
        }
        catch
        {
            // 삭제 실패 무시
        }
    }

    /// <summary>
    /// 캐시 정보 가져오기
    /// </summary>
    public Dictionary<string, DateTime> GetCacheInfo()
    {
        var result = new Dictionary<string, DateTime>();

        if (Directory.Exists(_cacheDir))
        {
            foreach (var file in Directory.GetFiles(_cacheDir, "markers_*.json"))
            {
                var mapName = Path.GetFileNameWithoutExtension(file).Replace("markers_", "");
                result[mapName] = File.GetLastWriteTime(file);
            }
        }

        return result;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// 좌표 변환 서비스
/// Affine 변환 행렬 계산 및 적용
/// </summary>
public static class CoordinateTransformService
{
    /// <summary>
    /// 매칭된 참조점들로 Affine 변환 행렬 계산
    /// 최소 3개의 참조점 필요
    /// </summary>
    /// <param name="referencePoints">참조점 목록 (DB좌표, API SVG좌표)</param>
    /// <returns>변환 행렬 [a, b, c, d, tx, ty] 또는 실패 시 null</returns>
    public static double[]? CalculateAffineTransform(
        List<(double dbX, double dbZ, double svgX, double svgY)> referencePoints)
    {
        if (referencePoints.Count < 3)
        {
            return null;
        }

        int n = referencePoints.Count;

        // 최소제곱법으로 Affine 변환 계산
        // [gameX] = [a  b] [svgX] + [tx]
        // [gameZ]   [c  d] [svgY]   [ty]

        // X 변환 (gameX = a*svgX + b*svgY + tx)
        // Y 변환 (gameZ = c*svgX + d*svgY + ty)

        // 행렬 A: [[svgX, svgY, 1], ...] (n x 3)
        // 벡터 bX: [gameX, ...] (n x 1)
        // 벡터 bZ: [gameZ, ...] (n x 1)

        // A^T * A * x = A^T * b 형태로 풀기

        // A^T * A (3x3)
        double sumX2 = 0, sumY2 = 0, sumXY = 0, sumX = 0, sumY = 0;
        double sumGameX = 0, sumGameZ = 0;
        double sumXGameX = 0, sumYGameX = 0;
        double sumXGameZ = 0, sumYGameZ = 0;

        foreach (var (dbX, dbZ, svgX, svgY) in referencePoints)
        {
            sumX2 += svgX * svgX;
            sumY2 += svgY * svgY;
            sumXY += svgX * svgY;
            sumX += svgX;
            sumY += svgY;
            sumGameX += dbX;
            sumGameZ += dbZ;
            sumXGameX += svgX * dbX;
            sumYGameX += svgY * dbX;
            sumXGameZ += svgX * dbZ;
            sumYGameZ += svgY * dbZ;
        }

        // 3x3 행렬 (A^T * A)
        // [sumX2,  sumXY, sumX ]
        // [sumXY,  sumY2, sumY ]
        // [sumX,   sumY,  n    ]

        // 3x3 역행렬 계산
        var det = sumX2 * (sumY2 * n - sumY * sumY)
                - sumXY * (sumXY * n - sumY * sumX)
                + sumX * (sumXY * sumY - sumY2 * sumX);

        if (Math.Abs(det) < 1e-10)
        {
            return null; // 특이 행렬
        }

        // 역행렬의 각 요소
        double invA11 = (sumY2 * n - sumY * sumY) / det;
        double invA12 = -(sumXY * n - sumY * sumX) / det;
        double invA13 = (sumXY * sumY - sumY2 * sumX) / det;
        double invA21 = -(sumXY * n - sumX * sumY) / det;
        double invA22 = (sumX2 * n - sumX * sumX) / det;
        double invA23 = -(sumX2 * sumY - sumXY * sumX) / det;
        double invA31 = (sumXY * sumY - sumX * sumY2) / det;
        double invA32 = -(sumX2 * sumY - sumX * sumXY) / det;
        double invA33 = (sumX2 * sumY2 - sumXY * sumXY) / det;

        // X 변환 계수 (a, b, tx)
        double a = invA11 * sumXGameX + invA12 * sumYGameX + invA13 * sumGameX;
        double b = invA21 * sumXGameX + invA22 * sumYGameX + invA23 * sumGameX;
        double tx = invA31 * sumXGameX + invA32 * sumYGameX + invA33 * sumGameX;

        // Z 변환 계수 (c, d, ty)
        double c = invA11 * sumXGameZ + invA12 * sumYGameZ + invA13 * sumGameZ;
        double d = invA21 * sumXGameZ + invA22 * sumYGameZ + invA23 * sumGameZ;
        double ty = invA31 * sumXGameZ + invA32 * sumYGameZ + invA33 * sumGameZ;

        return new[] { a, b, c, d, tx, ty };
    }

    /// <summary>
    /// 변환 행렬로 SVG 좌표를 게임 좌표로 변환
    /// </summary>
    public static (double gameX, double gameZ) TransformSvgToGame(
        double svgX, double svgY, double[] transform)
    {
        if (transform == null || transform.Length < 6)
        {
            return (svgX, svgY);
        }

        var a = transform[0];
        var b = transform[1];
        var c = transform[2];
        var d = transform[3];
        var tx = transform[4];
        var ty = transform[5];

        var gameX = a * svgX + b * svgY + tx;
        var gameZ = c * svgX + d * svgY + ty;

        return (gameX, gameZ);
    }

    /// <summary>
    /// 변환 오차 계산
    /// </summary>
    public static double CalculateError(
        List<(double dbX, double dbZ, double svgX, double svgY)> referencePoints,
        double[] transform)
    {
        if (referencePoints.Count == 0 || transform == null)
        {
            return double.MaxValue;
        }

        double totalError = 0;
        foreach (var (dbX, dbZ, svgX, svgY) in referencePoints)
        {
            var (calcX, calcZ) = TransformSvgToGame(svgX, svgY, transform);
            var dx = calcX - dbX;
            var dz = calcZ - dbZ;
            totalError += Math.Sqrt(dx * dx + dz * dz);
        }

        return totalError / referencePoints.Count;
    }

    #region Delaunay Triangulation & Barycentric Interpolation

    /// <summary>
    /// 삼각형 구조체
    /// </summary>
    public class Triangle
    {
        public int I0, I1, I2; // 참조점 인덱스
        public double X0, Y0, X1, Y1, X2, Y2; // SVG 좌표
        public double DbX0, DbZ0, DbX1, DbZ1, DbX2, DbZ2; // DB 좌표

        public Triangle(int i0, int i1, int i2,
            (double svgX, double svgY, double dbX, double dbZ) p0,
            (double svgX, double svgY, double dbX, double dbZ) p1,
            (double svgX, double svgY, double dbX, double dbZ) p2)
        {
            I0 = i0; I1 = i1; I2 = i2;
            X0 = p0.svgX; Y0 = p0.svgY; DbX0 = p0.dbX; DbZ0 = p0.dbZ;
            X1 = p1.svgX; Y1 = p1.svgY; DbX1 = p1.dbX; DbZ1 = p1.dbZ;
            X2 = p2.svgX; Y2 = p2.svgY; DbX2 = p2.dbX; DbZ2 = p2.dbZ;
        }
    }

    /// <summary>
    /// Delaunay 삼각분할 생성 (Bowyer-Watson 알고리즘)
    /// </summary>
    public static List<Triangle> CreateDelaunayTriangulation(
        List<(double svgX, double svgY, double dbX, double dbZ)> points)
    {
        if (points.Count < 3)
            return new List<Triangle>();

        // 슈퍼 삼각형 생성 (모든 점을 포함하는 큰 삼각형)
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var p in points)
        {
            minX = Math.Min(minX, p.svgX);
            minY = Math.Min(minY, p.svgY);
            maxX = Math.Max(maxX, p.svgX);
            maxY = Math.Max(maxY, p.svgY);
        }

        double dx = maxX - minX;
        double dy = maxY - minY;
        double deltaMax = Math.Max(dx, dy) * 2;

        // 슈퍼 삼각형 꼭지점 (충분히 크게)
        var superP0 = (svgX: minX - deltaMax, svgY: minY - deltaMax, dbX: 0.0, dbZ: 0.0);
        var superP1 = (svgX: minX + dx / 2, svgY: maxY + deltaMax, dbX: 0.0, dbZ: 0.0);
        var superP2 = (svgX: maxX + deltaMax, svgY: minY - deltaMax, dbX: 0.0, dbZ: 0.0);

        // 작업용 리스트
        var allPoints = new List<(double svgX, double svgY, double dbX, double dbZ)> { superP0, superP1, superP2 };
        allPoints.AddRange(points);

        var triangles = new List<(int i0, int i1, int i2)> { (0, 1, 2) };

        // 각 점을 하나씩 추가
        for (int i = 3; i < allPoints.Count; i++)
        {
            var p = allPoints[i];
            var badTriangles = new List<(int i0, int i1, int i2)>();

            // 현재 점이 외접원 내부에 있는 삼각형 찾기
            foreach (var tri in triangles)
            {
                if (IsPointInCircumcircle(p.svgX, p.svgY,
                    allPoints[tri.i0].svgX, allPoints[tri.i0].svgY,
                    allPoints[tri.i1].svgX, allPoints[tri.i1].svgY,
                    allPoints[tri.i2].svgX, allPoints[tri.i2].svgY))
                {
                    badTriangles.Add(tri);
                }
            }

            // 다각형 경계 찾기 (bad triangles의 외곽 엣지)
            var polygon = new List<(int, int)>();
            foreach (var tri in badTriangles)
            {
                var edges = new[] { (tri.i0, tri.i1), (tri.i1, tri.i2), (tri.i2, tri.i0) };
                foreach (var edge in edges)
                {
                    bool isShared = false;
                    foreach (var otherTri in badTriangles)
                    {
                        if (tri.Equals(otherTri)) continue;
                        var otherEdges = new[] { (otherTri.i0, otherTri.i1), (otherTri.i1, otherTri.i2), (otherTri.i2, otherTri.i0) };
                        foreach (var otherEdge in otherEdges)
                        {
                            if ((edge.Item1 == otherEdge.Item1 && edge.Item2 == otherEdge.Item2) ||
                                (edge.Item1 == otherEdge.Item2 && edge.Item2 == otherEdge.Item1))
                            {
                                isShared = true;
                                break;
                            }
                        }
                        if (isShared) break;
                    }
                    if (!isShared)
                        polygon.Add(edge);
                }
            }

            // Bad triangles 제거
            foreach (var bad in badTriangles)
                triangles.Remove(bad);

            // 새 삼각형 생성
            foreach (var edge in polygon)
                triangles.Add((edge.Item1, edge.Item2, i));
        }

        // 슈퍼 삼각형과 연결된 삼각형 제거 (인덱스 0, 1, 2)
        triangles.RemoveAll(t => t.i0 < 3 || t.i1 < 3 || t.i2 < 3);

        // 결과 삼각형 리스트 생성 (인덱스 조정: -3)
        var result = new List<Triangle>();
        foreach (var tri in triangles)
        {
            var p0 = points[tri.i0 - 3];
            var p1 = points[tri.i1 - 3];
            var p2 = points[tri.i2 - 3];
            result.Add(new Triangle(tri.i0 - 3, tri.i1 - 3, tri.i2 - 3,
                (p0.svgX, p0.svgY, p0.dbX, p0.dbZ),
                (p1.svgX, p1.svgY, p1.dbX, p1.dbZ),
                (p2.svgX, p2.svgY, p2.dbX, p2.dbZ)));
        }

        return result;
    }

    /// <summary>
    /// 점이 외접원 내부에 있는지 확인
    /// </summary>
    private static bool IsPointInCircumcircle(double px, double py,
        double x0, double y0, double x1, double y1, double x2, double y2)
    {
        double ax = x0 - px, ay = y0 - py;
        double bx = x1 - px, by = y1 - py;
        double cx = x2 - px, cy = y2 - py;

        double det = (ax * ax + ay * ay) * (bx * cy - cx * by)
                   - (bx * bx + by * by) * (ax * cy - cx * ay)
                   + (cx * cx + cy * cy) * (ax * by - bx * ay);

        // 반시계 방향이면 양수, 시계 방향이면 음수
        double orientation = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
        return orientation > 0 ? det > 0 : det < 0;
    }

    /// <summary>
    /// 점이 삼각형 내부에 있는지 확인하고 Barycentric 좌표 반환
    /// </summary>
    public static (bool isInside, double u, double v, double w) GetBarycentricCoordinates(
        double px, double py, Triangle tri)
    {
        double x0 = tri.X0, y0 = tri.Y0;
        double x1 = tri.X1, y1 = tri.Y1;
        double x2 = tri.X2, y2 = tri.Y2;

        double v0x = x2 - x0, v0y = y2 - y0;
        double v1x = x1 - x0, v1y = y1 - y0;
        double v2x = px - x0, v2y = py - y0;

        double dot00 = v0x * v0x + v0y * v0y;
        double dot01 = v0x * v1x + v0y * v1y;
        double dot02 = v0x * v2x + v0y * v2y;
        double dot11 = v1x * v1x + v1y * v1y;
        double dot12 = v1x * v2x + v1y * v2y;

        double invDenom = 1.0 / (dot00 * dot11 - dot01 * dot01);
        double u = (dot11 * dot02 - dot01 * dot12) * invDenom; // weight for p2
        double v = (dot00 * dot12 - dot01 * dot02) * invDenom; // weight for p1
        double w = 1.0 - u - v; // weight for p0

        // 내부 판정 (약간의 여유 허용)
        const double epsilon = -0.0001;
        bool isInside = u >= epsilon && v >= epsilon && w >= epsilon;

        return (isInside, w, v, u); // (p0 weight, p1 weight, p2 weight)
    }

    /// <summary>
    /// Barycentric 좌표로 DB 좌표 보간
    /// </summary>
    public static (double dbX, double dbZ) InterpolateWithBarycentric(
        double u, double v, double w, Triangle tri)
    {
        double dbX = u * tri.DbX0 + v * tri.DbX1 + w * tri.DbX2;
        double dbZ = u * tri.DbZ0 + v * tri.DbZ1 + w * tri.DbZ2;
        return (dbX, dbZ);
    }

    /// <summary>
    /// IDW (Inverse Distance Weighting) 보간
    /// 삼각형 외부의 점에 대한 폴백
    /// </summary>
    public static (double dbX, double dbZ) InterpolateWithIDW(
        double svgX, double svgY,
        List<(double svgX, double svgY, double dbX, double dbZ)> referencePoints,
        double power = 2.0)
    {
        double sumWeightX = 0, sumWeightZ = 0, sumWeights = 0;

        foreach (var p in referencePoints)
        {
            double dx = svgX - p.svgX;
            double dy = svgY - p.svgY;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < 0.0001) // 거의 같은 위치
            {
                return (p.dbX, p.dbZ);
            }

            double weight = 1.0 / Math.Pow(dist, power);
            sumWeightX += weight * p.dbX;
            sumWeightZ += weight * p.dbZ;
            sumWeights += weight;
        }

        return (sumWeightX / sumWeights, sumWeightZ / sumWeights);
    }

    /// <summary>
    /// Delaunay + Barycentric + IDW 통합 보간
    /// </summary>
    public static (double dbX, double dbZ) InterpolatePoint(
        double svgX, double svgY,
        List<Triangle> triangles,
        List<(double svgX, double svgY, double dbX, double dbZ)> referencePoints)
    {
        // 1. 속한 삼각형 찾기
        foreach (var tri in triangles)
        {
            var (isInside, u, v, w) = GetBarycentricCoordinates(svgX, svgY, tri);
            if (isInside)
            {
                return InterpolateWithBarycentric(u, v, w, tri);
            }
        }

        // 2. 삼각형 외부면 IDW 폴백
        return InterpolateWithIDW(svgX, svgY, referencePoints);
    }

    #endregion
}

/// <summary>
/// 마커 매칭 서비스
/// </summary>
public static class MarkerMatchingService
{
    /// <summary>
    /// DB 마커와 API 마커 자동 매칭
    /// 같은 이름/타입의 마커가 여러 개 있을 때 거리 기반으로 매칭
    /// </summary>
    public static List<MarkerMatchResult> AutoMatch(
        List<MapMarker> dbMarkers,
        List<TarkovMarketMarker> apiMarkers)
    {
        var results = new List<MarkerMatchResult>();
        var usedApiMarkers = new HashSet<string>();
        var usedDbMarkers = new HashSet<string>();

        // 1단계: 고유하게 매칭되는 마커들 먼저 처리 (1:1 매칭)
        var uniqueMatches = FindUniqueMatches(dbMarkers, apiMarkers);
        foreach (var match in uniqueMatches)
        {
            results.Add(match);
            usedApiMarkers.Add(match.ApiMarker.Uid);
            usedDbMarkers.Add(match.DbMarker.Id);
        }

        // 고유 매칭이 3개 이상이면 임시 Transform 계산
        double[]? tempTransform = null;
        if (uniqueMatches.Count >= 3)
        {
            var refPoints = uniqueMatches
                .Where(m => m.ApiMarker.Geometry != null)
                .Select(m => (m.DbMarker.X, m.DbMarker.Z, m.ApiMarker.Geometry!.X, m.ApiMarker.Geometry!.Y))
                .ToList();

            if (refPoints.Count >= 3)
            {
                tempTransform = CoordinateTransformService.CalculateAffineTransform(refPoints);
            }
        }

        // 2단계: 중복 마커 처리 (같은 타입+유사한 이름의 마커가 여러 개인 경우)
        var remainingDbMarkers = dbMarkers.Where(m => !usedDbMarkers.Contains(m.Id)).ToList();
        var remainingApiMarkers = apiMarkers.Where(m => !usedApiMarkers.Contains(m.Uid) && m.Geometry != null).ToList();

        // 타입별로 그룹화
        var dbByType = remainingDbMarkers.GroupBy(m => m.MarkerType).ToDictionary(g => g.Key, g => g.ToList());
        var apiByType = remainingApiMarkers.GroupBy(m => m.MappedMarkerType).Where(g => g.Key.HasValue).ToDictionary(g => g.Key!.Value, g => g.ToList());

        foreach (var (markerType, dbGroup) in dbByType)
        {
            if (!apiByType.TryGetValue(markerType, out var apiGroup))
            {
                continue;
            }

            // 이름 유사도가 높은 쌍들을 찾기
            var potentialPairs = new List<(MapMarker db, TarkovMarketMarker api, double similarity)>();
            foreach (var db in dbGroup)
            {
                foreach (var api in apiGroup)
                {
                    if (usedApiMarkers.Contains(api.Uid)) continue;

                    var similarity = CalculateNameSimilarity(db.Name, api.Name);
                    if (similarity > 0.3)
                    {
                        potentialPairs.Add((db, api, similarity));
                    }
                }
            }

            if (potentialPairs.Count == 0) continue;

            // Transform이 있으면 거리 기반 매칭 사용
            if (tempTransform != null)
            {
                // 거리 기반 최적 매칭 (그리디 알고리즘)
                var distanceMatches = MatchByDistance(potentialPairs, tempTransform, usedApiMarkers, usedDbMarkers);
                foreach (var match in distanceMatches)
                {
                    results.Add(match);
                    usedApiMarkers.Add(match.ApiMarker.Uid);
                    usedDbMarkers.Add(match.DbMarker.Id);
                }
            }
            else
            {
                // Transform이 없으면 이름 유사도 기반 (기존 로직)
                var orderedPairs = potentialPairs.OrderByDescending(p => p.similarity).ToList();
                foreach (var (db, api, similarity) in orderedPairs)
                {
                    if (usedApiMarkers.Contains(api.Uid) || usedDbMarkers.Contains(db.Id)) continue;

                    results.Add(new MarkerMatchResult
                    {
                        DbMarker = db,
                        ApiMarker = api,
                        NameSimilarity = similarity,
                        IsReferencePoint = false,
                        IsManualMatch = false
                    });
                    usedApiMarkers.Add(api.Uid);
                    usedDbMarkers.Add(db.Id);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// 고유하게 매칭되는 마커 찾기 (같은 타입에서 이름이 유일하게 매칭되는 경우)
    /// </summary>
    private static List<MarkerMatchResult> FindUniqueMatches(
        List<MapMarker> dbMarkers,
        List<TarkovMarketMarker> apiMarkers)
    {
        var results = new List<MarkerMatchResult>();
        var usedApiMarkers = new HashSet<string>();

        // 타입별로 그룹화
        var dbByType = dbMarkers.GroupBy(m => m.MarkerType).ToDictionary(g => g.Key, g => g.ToList());
        var apiByType = apiMarkers.Where(m => m.Geometry != null && m.MappedMarkerType.HasValue)
            .GroupBy(m => m.MappedMarkerType!.Value).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (markerType, dbGroup) in dbByType)
        {
            if (!apiByType.TryGetValue(markerType, out var apiGroup))
            {
                continue;
            }

            foreach (var dbMarker in dbGroup)
            {
                // 이 DB 마커와 유사한 API 마커들
                var similarApiMarkers = apiGroup
                    .Where(api => !usedApiMarkers.Contains(api.Uid))
                    .Select(api => new { Api = api, Similarity = CalculateNameSimilarity(dbMarker.Name, api.Name) })
                    .Where(x => x.Similarity > 0.5) // 50% 이상 유사도
                    .OrderByDescending(x => x.Similarity)
                    .ToList();

                // 유일하게 매칭되는 경우만 (1개의 후보만 있거나, 최고 유사도가 확연히 높은 경우)
                if (similarApiMarkers.Count == 1)
                {
                    results.Add(new MarkerMatchResult
                    {
                        DbMarker = dbMarker,
                        ApiMarker = similarApiMarkers[0].Api,
                        NameSimilarity = similarApiMarkers[0].Similarity,
                        IsReferencePoint = false,
                        IsManualMatch = false
                    });
                    usedApiMarkers.Add(similarApiMarkers[0].Api.Uid);
                }
                else if (similarApiMarkers.Count > 1 &&
                         similarApiMarkers[0].Similarity > 0.9 &&
                         similarApiMarkers[0].Similarity - similarApiMarkers[1].Similarity > 0.2)
                {
                    // 첫 번째가 90% 이상이고 두 번째보다 20% 이상 높으면 고유 매칭으로 처리
                    results.Add(new MarkerMatchResult
                    {
                        DbMarker = dbMarker,
                        ApiMarker = similarApiMarkers[0].Api,
                        NameSimilarity = similarApiMarkers[0].Similarity,
                        IsReferencePoint = false,
                        IsManualMatch = false
                    });
                    usedApiMarkers.Add(similarApiMarkers[0].Api.Uid);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Transform을 사용한 거리 기반 매칭
    /// </summary>
    private static List<MarkerMatchResult> MatchByDistance(
        List<(MapMarker db, TarkovMarketMarker api, double similarity)> potentialPairs,
        double[] transform,
        HashSet<string> usedApiMarkers,
        HashSet<string> usedDbMarkers)
    {
        var results = new List<MarkerMatchResult>();

        // 각 쌍에 대해 거리 계산
        var pairsWithDistance = potentialPairs
            .Where(p => !usedApiMarkers.Contains(p.api.Uid) && !usedDbMarkers.Contains(p.db.Id))
            .Select(p =>
            {
                var (gameX, gameZ) = CoordinateTransformService.TransformSvgToGame(
                    p.api.Geometry!.X, p.api.Geometry!.Y, transform);
                var dx = gameX - p.db.X;
                var dz = gameZ - p.db.Z;
                var distance = Math.Sqrt(dx * dx + dz * dz);
                return (p.db, p.api, p.similarity, distance);
            })
            .OrderBy(p => p.distance) // 거리순 정렬
            .ToList();

        // 그리디하게 가장 가까운 쌍부터 매칭
        var localUsedApi = new HashSet<string>(usedApiMarkers);
        var localUsedDb = new HashSet<string>(usedDbMarkers);

        foreach (var (db, api, similarity, distance) in pairsWithDistance)
        {
            if (localUsedApi.Contains(api.Uid) || localUsedDb.Contains(db.Id)) continue;

            results.Add(new MarkerMatchResult
            {
                DbMarker = db,
                ApiMarker = api,
                NameSimilarity = similarity,
                DistanceError = distance,
                IsReferencePoint = false,
                IsManualMatch = false
            });

            localUsedApi.Add(api.Uid);
            localUsedDb.Add(db.Id);
        }

        return results;
    }

    /// <summary>
    /// 이름 유사도 계산 (0~1)
    /// </summary>
    public static double CalculateNameSimilarity(string name1, string name2)
    {
        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
        {
            return 0;
        }

        // 정규화
        var n1 = NormalizeName(name1);
        var n2 = NormalizeName(name2);

        // 정확히 일치
        if (n1 == n2)
        {
            return 1.0;
        }

        // 포함 관계
        if (n1.Contains(n2) || n2.Contains(n1))
        {
            return 0.8;
        }

        // Levenshtein 거리 기반 유사도
        var distance = LevenshteinDistance(n1, n2);
        var maxLen = Math.Max(n1.Length, n2.Length);
        return 1.0 - (double)distance / maxLen;
    }

    private static string NormalizeName(string name)
    {
        return name.ToLowerInvariant()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .Replace("'", "")
            .Replace("\"", "");
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) d[i, 0] = i;
        for (var j = 0; j <= n; j++) d[0, j] = j;

        for (var j = 1; j <= n; j++)
        {
            for (var i = 1; i <= m; i++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }

    /// <summary>
    /// API 마커의 Level을 FloorId로 변환
    /// </summary>
    public static string? MapLevelToFloorId(int? level, string mapKey, List<MapFloorConfig>? floors)
    {
        if (floors == null || floors.Count == 0)
        {
            return null;
        }

        // level이 없으면 기본 층
        if (!level.HasValue)
        {
            return floors.FirstOrDefault(f => f.IsDefault)?.LayerId ?? "main";
        }

        // level 값에 따른 매핑 (맵마다 다를 수 있음)
        var floorIndex = level.Value;

        // Order 기준으로 정렬된 층 목록
        var sortedFloors = floors.OrderBy(f => f.Order).ToList();

        // level 1이 보통 main (Order 0)에 해당
        // level 0 또는 음수는 basement 등
        // level 2, 3은 level2, level3에 해당

        if (floorIndex <= 0)
        {
            // 지하층 찾기
            var basementFloor = sortedFloors.FirstOrDefault(f => f.Order < 0);
            return basementFloor?.LayerId ?? sortedFloors.FirstOrDefault()?.LayerId;
        }
        else if (floorIndex == 1)
        {
            // 메인 층
            return sortedFloors.FirstOrDefault(f => f.Order == 0)?.LayerId ?? "main";
        }
        else
        {
            // 상위 층
            var upperFloor = sortedFloors.FirstOrDefault(f => f.Order == floorIndex - 1);
            return upperFloor?.LayerId ?? sortedFloors.LastOrDefault()?.LayerId;
        }
    }
}
