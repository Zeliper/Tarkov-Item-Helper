using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Services.MapTracker;

/// <summary>
/// 기존 tarkov.dev 맵의 좌표 변환 서비스.
/// 구 지도에서 정확했던 변환 로직을 그대로 사용합니다.
/// </summary>
public sealed class OldMapTransformService
{
    private static OldMapTransformService? _instance;
    public static OldMapTransformService Instance => _instance ??= new OldMapTransformService();

    private readonly Dictionary<string, OldMapReferenceData> _references;
    private readonly Dictionary<string, string> _aliasToKey;

    private OldMapTransformService()
    {
        _references = new Dictionary<string, OldMapReferenceData>(StringComparer.OrdinalIgnoreCase);
        _aliasToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in OldMapReferenceData.GetAllReferences())
        {
            _references[reference.MapKey] = reference;
            _aliasToKey[reference.MapKey] = reference.MapKey;
        }

        // 별칭 추가
        AddAlias("Woods", "woods", "WOODS");
        AddAlias("Customs", "customs", "CUSTOMS", "bigmap");
        AddAlias("Shoreline", "shoreline", "SHORELINE");
        AddAlias("Interchange", "interchange", "INTERCHANGE");
        AddAlias("Reserve", "reserve", "RESERVE", "RezervBase");
        AddAlias("Lighthouse", "lighthouse", "LIGHTHOUSE");
        AddAlias("StreetsOfTarkov", "streets", "STREETS", "TarkovStreets", "streets-of-tarkov");
        AddAlias("Factory", "factory", "FACTORY", "factory4_day", "factory4_night");
        AddAlias("GroundZero", "groundzero", "GROUNDZERO", "Sandbox", "sandbox", "ground-zero", "ground-zero-21");
        AddAlias("Labs", "labs", "LABS", "laboratory", "the-lab");
    }

    private void AddAlias(string mapKey, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            _aliasToKey[alias] = mapKey;
        }
    }

    /// <summary>
    /// 맵의 참조 데이터를 반환합니다.
    /// </summary>
    public OldMapReferenceData? GetReferenceData(string mapKey)
    {
        if (string.IsNullOrWhiteSpace(mapKey))
            return null;

        if (_aliasToKey.TryGetValue(mapKey, out var actualKey))
            return _references.GetValueOrDefault(actualKey);

        return null;
    }

    /// <summary>
    /// 게임 좌표를 구 지도 화면 좌표로 변환합니다.
    /// tarkov.dev의 원래 변환 로직을 사용합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="gameX">게임 position.x 좌표</param>
    /// <param name="gameZ">게임 position.z 좌표</param>
    /// <returns>구 지도에서의 화면 좌표, 변환 실패 시 null</returns>
    public (double x, double y)? TransformToOldScreen(string mapKey, double gameX, double gameZ)
    {
        var reference = GetReferenceData(mapKey);
        if (reference == null)
            return null;

        try
        {
            // 1. 게임 좌표 → Leaflet 좌표 (lat=z, lng=x)
            var lat = gameZ;
            var lng = gameX;

            // 2. 회전 적용
            var (rotatedLng, rotatedLat) = ApplyRotation(lng, lat, reference.OldCoordinateRotation);

            // 3. CRS Transform (Y축 반전 포함)
            var scaleX = reference.OldTransform[0];
            var marginX = reference.OldTransform[1];
            var scaleY = reference.OldTransform[2] * -1;
            var marginY = reference.OldTransform[3];

            var markerPixelX = scaleX * rotatedLng + marginX;
            var markerPixelY = scaleY * rotatedLat + marginY;

            // 4. SVG bounds → pixel bounds
            var (svgPixelXMin, svgPixelXMax, svgPixelYMin, svgPixelYMax) =
                CalculateSvgPixelBounds(reference, scaleX, marginX, scaleY, marginY);

            // 5. ViewBox 좌표로 정규화
            var normalizedX = (markerPixelX - svgPixelXMin) / (svgPixelXMax - svgPixelXMin);
            var normalizedY = (markerPixelY - svgPixelYMin) / (svgPixelYMax - svgPixelYMin);

            var screenX = normalizedX * reference.OldImageWidth;
            var screenY = normalizedY * reference.OldImageHeight;

            return (screenX, screenY);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 좌표에 회전을 적용합니다.
    /// </summary>
    private static (double rotatedLng, double rotatedLat) ApplyRotation(double lng, double lat, int rotationDegrees)
    {
        var angleInRadians = rotationDegrees * Math.PI / 180.0;
        var cosAngle = Math.Cos(angleInRadians);
        var sinAngle = Math.Sin(angleInRadians);

        var rotatedLng = lng * cosAngle - lat * sinAngle;
        var rotatedLat = lng * sinAngle + lat * cosAngle;

        return (rotatedLng, rotatedLat);
    }

    /// <summary>
    /// SVG bounds를 픽셀 좌표로 변환합니다.
    /// </summary>
    private static (double xMin, double xMax, double yMin, double yMax) CalculateSvgPixelBounds(
        OldMapReferenceData reference, double scaleX, double marginX, double scaleY, double marginY)
    {
        var svgLat1 = reference.OldSvgBounds[0][1];
        var svgLng1 = reference.OldSvgBounds[0][0];
        var svgLat2 = reference.OldSvgBounds[1][1];
        var svgLng2 = reference.OldSvgBounds[1][0];

        var (svgRotatedLng1, svgRotatedLat1) = ApplyRotation(svgLng1, svgLat1, reference.OldCoordinateRotation);
        var (svgRotatedLng2, svgRotatedLat2) = ApplyRotation(svgLng2, svgLat2, reference.OldCoordinateRotation);

        var svgPixelX1 = scaleX * svgRotatedLng1 + marginX;
        var svgPixelY1 = scaleY * svgRotatedLat1 + marginY;
        var svgPixelX2 = scaleX * svgRotatedLng2 + marginX;
        var svgPixelY2 = scaleY * svgRotatedLat2 + marginY;

        return (
            Math.Min(svgPixelX1, svgPixelX2),
            Math.Max(svgPixelX1, svgPixelX2),
            Math.Min(svgPixelY1, svgPixelY2),
            Math.Max(svgPixelY1, svgPixelY2)
        );
    }

    /// <summary>
    /// 구 지도 좌표를 신 지도 viewBox 비율로 스케일링합니다.
    /// 단순 비율 변환만 적용 (추가 보정 없음).
    /// </summary>
    public (double x, double y)? ScaleToNewMap(string mapKey, double oldScreenX, double oldScreenY)
    {
        var reference = GetReferenceData(mapKey);
        if (reference == null)
            return null;

        var ratioX = reference.NewImageWidth / reference.OldImageWidth;
        var ratioY = reference.NewImageHeight / reference.OldImageHeight;

        return (oldScreenX * ratioX, oldScreenY * ratioY);
    }

    /// <summary>
    /// 게임 좌표를 바로 신 지도 좌표로 변환합니다 (구 지도 거쳐서).
    /// 단순 비율 변환만 적용.
    /// </summary>
    public (double x, double y)? TransformToNewScreenSimple(string mapKey, double gameX, double gameZ)
    {
        var oldPos = TransformToOldScreen(mapKey, gameX, gameZ);
        if (oldPos == null)
            return null;

        return ScaleToNewMap(mapKey, oldPos.Value.x, oldPos.Value.y);
    }

    /// <summary>
    /// 모든 맵 키 목록을 반환합니다.
    /// </summary>
    public IReadOnlyList<string> GetAllMapKeys()
    {
        return _references.Keys.ToList().AsReadOnly();
    }
}
