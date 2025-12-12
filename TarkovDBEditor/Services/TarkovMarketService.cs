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
