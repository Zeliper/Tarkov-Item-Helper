using TarkovHelper.Models.Map;

namespace TarkovHelper.Services.Map;

/// <summary>
/// 게임 좌표를 SVG viewBox 좌표로 변환하는 서비스.
/// tarkov.dev의 Leaflet 좌표 변환 방식을 사용합니다.
///
/// [좌표 변환 과정]
/// 1. 게임 좌표 (position.x, position.z) → Leaflet (lng, lat)
/// 2. CoordinateRotation 각도로 회전 적용
/// 3. CRS Transform: pixel = scale * coord + margin
/// 4. SVG bounds 픽셀 영역을 viewBox(0,0)~(width,height)로 정규화
/// </summary>
public sealed class MapCoordinateTransformer : IMapCoordinateTransformer
{
    private Dictionary<string, MapConfig> _mapConfigs = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _aliasToKey = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 빈 맵 설정으로 변환기를 생성합니다.
    /// </summary>
    public MapCoordinateTransformer()
    {
    }

    /// <summary>
    /// 지정된 맵 설정으로 변환기를 생성합니다.
    /// </summary>
    public MapCoordinateTransformer(IEnumerable<MapConfig> maps)
    {
        UpdateMaps(maps);
    }

    /// <inheritdoc />
    public bool TryTransform(EftPosition worldPosition, out ScreenPosition? screenPosition)
    {
        // 스크린샷 파일명: X=position.x, Y=position.y(높이), Z=position.z
        return TryTransformGameCoordinate(
            worldPosition.MapName,
            worldPosition.X,
            worldPosition.Y,
            worldPosition.Z,
            worldPosition.Angle,
            out screenPosition);
    }

    /// <inheritdoc />
    public bool TryTransform(string mapKey, double worldX, double worldY, double? angle, out ScreenPosition? screenPosition)
    {
        // worldX = position.x, worldY = position.z (이 오버로드는 높이 정보 없음)
        return TryTransformGameCoordinate(mapKey, worldX, 0, worldY, angle, out screenPosition);
    }

    /// <inheritdoc />
    public bool TryTransformApiCoordinate(string mapKey, double apiX, double apiY, double? apiZ, out ScreenPosition? screenPosition)
    {
        // API 좌표: apiX=position.x, apiY=position.y(높이), apiZ=position.z
        return TryTransformGameCoordinate(mapKey, apiX, apiY, apiZ, null, out screenPosition);
    }

    /// <summary>
    /// 플레이어 마커 전용 좌표 변환.
    /// playerMarkerTransform이 있으면 우선 사용합니다.
    /// </summary>
    public bool TryTransformPlayerPosition(EftPosition worldPosition, out ScreenPosition? screenPosition)
    {
        return TryTransformPlayerPosition(
            worldPosition.MapName,
            worldPosition.X,
            worldPosition.Y,
            worldPosition.Z,
            worldPosition.Angle,
            out screenPosition);
    }

    /// <summary>
    /// 플레이어 마커 전용 좌표 변환.
    /// playerMarkerTransform이 있으면 우선 사용합니다.
    /// </summary>
    public bool TryTransformPlayerPosition(string mapKey, double gameX, double gameY, double? gameZ, double? angle, out ScreenPosition? screenPosition)
    {
        screenPosition = null;

        var config = GetMapConfig(mapKey);
        if (config == null)
            return false;

        try
        {
            double finalX, finalY;

            // playerMarkerTransform이 있으면 우선 사용
            if (config.PlayerMarkerTransform != null && config.PlayerMarkerTransform.Length >= 6)
            {
                var a = config.PlayerMarkerTransform[0];
                var b = config.PlayerMarkerTransform[1];
                var c = config.PlayerMarkerTransform[2];
                var d = config.PlayerMarkerTransform[3];
                var tx = config.PlayerMarkerTransform[4];
                var ty = config.PlayerMarkerTransform[5];

                finalX = a * gameX + b * (gameZ ?? 0) + tx;
                finalY = c * gameX + d * (gameZ ?? 0) + ty;
            }
            // calibratedTransform이 있으면 IDW 보정을 적용한 변환 사용
            else if (config.CalibratedTransform != null && config.CalibratedTransform.Length >= 6)
            {
                var calibrationService = MapCalibrationService.Instance;
                (finalX, finalY) = calibrationService.ApplyCalibratedTransformWithIDW(
                    config.CalibratedTransform,
                    config.CalibrationPoints,
                    gameX,
                    gameZ ?? 0);
            }
            else
            {
                // 기존 Transform 방식으로 폴백
                return TryTransformGameCoordinate(mapKey, gameX, gameY, gameZ, angle, out screenPosition);
            }

            screenPosition = new ScreenPosition
            {
                MapKey = config.Key,
                X = finalX,
                Y = finalY,
                Angle = angle,
                OriginalPosition = new EftPosition
                {
                    MapName = mapKey,
                    X = gameX,
                    Y = gameY,
                    Z = gameZ,
                    Angle = angle,
                    Timestamp = DateTime.Now
                }
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 게임 좌표를 SVG viewBox 좌표로 변환합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="gameX">게임 position.x 좌표</param>
    /// <param name="gameY">게임 position.y 좌표 (높이)</param>
    /// <param name="gameZ">게임 position.z 좌표</param>
    /// <param name="angle">플레이어 방향 각도 (선택)</param>
    /// <param name="screenPosition">변환된 화면 좌표</param>
    /// <returns>변환 성공 여부</returns>
    private bool TryTransformGameCoordinate(string mapKey, double gameX, double gameY, double? gameZ, double? angle, out ScreenPosition? screenPosition)
    {
        screenPosition = null;

        var config = GetMapConfig(mapKey);
        if (config == null)
            return false;

        try
        {
            double finalX, finalY;

            // 보정된 변환이 있으면 IDW 보정을 적용한 변환 사용
            if (config.CalibratedTransform != null && config.CalibratedTransform.Length >= 6)
            {
                var calibrationService = MapCalibrationService.Instance;
                (finalX, finalY) = calibrationService.ApplyCalibratedTransformWithIDW(
                    config.CalibratedTransform,
                    config.CalibrationPoints,
                    gameX,
                    gameZ ?? 0);
            }
            else
            {
                // 기존 Transform 방식 사용
                if (config.Transform == null || config.Transform.Length < 4)
                    return false;

                if (config.SvgBounds == null || config.SvgBounds.Length < 2)
                    return false;

                // 1. 게임 좌표 → Leaflet 좌표 (lat=z, lng=x)
                var lat = gameZ ?? 0;
                var lng = gameX;

                // 2. 회전 적용
                var (rotatedLng, rotatedLat) = ApplyRotation(lng, lat, config.CoordinateRotation);

                // 3. CRS Transform (Y축 반전 포함)
                var scaleX = config.Transform[0];
                var marginX = config.Transform[1];
                var scaleY = config.Transform[2] * -1;
                var marginY = config.Transform[3];

                var markerPixelX = scaleX * rotatedLng + marginX;
                var markerPixelY = scaleY * rotatedLat + marginY;

                // 4. SVG bounds → pixel bounds
                var (svgPixelXMin, svgPixelXMax, svgPixelYMin, svgPixelYMax) =
                    CalculateSvgPixelBounds(config, scaleX, marginX, scaleY, marginY);

                // 5. ViewBox 좌표로 정규화
                var normalizedX = (markerPixelX - svgPixelXMin) / (svgPixelXMax - svgPixelXMin);
                var normalizedY = (markerPixelY - svgPixelYMin) / (svgPixelYMax - svgPixelYMin);

                finalX = normalizedX * config.ImageWidth;
                finalY = normalizedY * config.ImageHeight;
            }

            screenPosition = new ScreenPosition
            {
                MapKey = config.Key,
                X = finalX,
                Y = finalY,
                Angle = angle,
                OriginalPosition = new EftPosition
                {
                    MapName = mapKey,
                    X = gameX,
                    Y = gameY,
                    Z = gameZ,
                    Angle = angle,
                    Timestamp = DateTime.Now
                }
            };

            return true;
        }
        catch
        {
            return false;
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
    /// SvgBounds는 1차원 배열 [lng1, lat1, lng2, lat2] 형식입니다.
    /// </summary>
    private static (double xMin, double xMax, double yMin, double yMax) CalculateSvgPixelBounds(
        MapConfig config, double scaleX, double marginX, double scaleY, double marginY)
    {
        // SvgBounds: [lng1, lat1, lng2, lat2]
        var svgLng1 = config.SvgBounds![0];
        var svgLat1 = config.SvgBounds[1];
        var svgLng2 = config.SvgBounds[2];
        var svgLat2 = config.SvgBounds[3];

        var (svgRotatedLng1, svgRotatedLat1) = ApplyRotation(svgLng1, svgLat1, config.CoordinateRotation);
        var (svgRotatedLng2, svgRotatedLat2) = ApplyRotation(svgLng2, svgLat2, config.CoordinateRotation);

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

    /// <inheritdoc />
    public void UpdateMaps(IEnumerable<MapConfig> maps)
    {
        _mapConfigs.Clear();
        _aliasToKey.Clear();

        foreach (var map in maps)
        {
            if (string.IsNullOrWhiteSpace(map.Key))
                continue;

            _mapConfigs[map.Key] = map;
            _aliasToKey[map.Key] = map.Key;

            if (map.Aliases != null)
            {
                foreach (var alias in map.Aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias))
                        _aliasToKey[alias] = map.Key;
                }
            }
        }
    }

    /// <inheritdoc />
    public MapConfig? GetMapConfig(string mapKey)
    {
        if (string.IsNullOrWhiteSpace(mapKey))
            return null;

        if (_aliasToKey.TryGetValue(mapKey, out var actualKey))
            return _mapConfigs.GetValueOrDefault(actualKey);

        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllMapKeys()
    {
        return _mapConfigs.Keys.ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public string? ResolveMapKey(string mapNameOrAlias)
    {
        if (string.IsNullOrWhiteSpace(mapNameOrAlias))
            return null;

        if (_aliasToKey.TryGetValue(mapNameOrAlias, out var actualKey))
            return actualKey;

        return null;
    }

    /// <summary>
    /// 구 지도 변환을 기반으로 게임 좌표를 신 지도 좌표로 변환합니다.
    /// 수동 보정 데이터가 있으면 구→신 매핑을 적용합니다.
    /// </summary>
    /// <param name="mapKey">맵 키</param>
    /// <param name="gameX">게임 X 좌표</param>
    /// <param name="gameY">게임 Y 좌표 (높이)</param>
    /// <param name="gameZ">게임 Z 좌표</param>
    /// <param name="angle">방향 각도</param>
    /// <param name="screenPosition">변환된 화면 좌표</param>
    /// <returns>변환 성공 여부</returns>
    public bool TryTransformWithAutoCalibration(string mapKey, double gameX, double gameY, double gameZ, double? angle, out ScreenPosition? screenPosition)
    {
        screenPosition = null;

        var config = GetMapConfig(mapKey);
        if (config == null)
            return false;

        try
        {
            double finalX, finalY;

            // 수동 보정 데이터가 있으면 자동 보정 사용
            if (config.CalibrationPoints != null && config.CalibrationPoints.Count >= 3)
            {
                var autoCalibration = AutoCalibrationService.Instance;
                var result = autoCalibration.CalibrateFromExistingPoints(config);

                if (result.Success && result.OldToNewMapping != null)
                {
                    var transformed = autoCalibration.TransformWithMapping(mapKey, gameX, gameZ, result.OldToNewMapping);
                    if (transformed != null)
                    {
                        finalX = transformed.Value.x;
                        finalY = transformed.Value.y;
                    }
                    else
                    {
                        // 폴백: IDW 변환
                        return TryTransformGameCoordinate(mapKey, gameX, gameY, gameZ, angle, out screenPosition);
                    }
                }
                else
                {
                    // 폴백: IDW 변환
                    return TryTransformGameCoordinate(mapKey, gameX, gameY, gameZ, angle, out screenPosition);
                }
            }
            else
            {
                // 보정 데이터 없으면 구 지도 기반 단순 변환
                var oldTransform = OldMapTransformService.Instance;
                var result = oldTransform.TransformToNewScreenSimple(mapKey, gameX, gameZ);
                if (result == null)
                    return false;

                finalX = result.Value.x;
                finalY = result.Value.y;
            }

            screenPosition = new ScreenPosition
            {
                MapKey = config.Key,
                X = finalX,
                Y = finalY,
                Angle = angle,
                OriginalPosition = new EftPosition
                {
                    MapName = mapKey,
                    X = gameX,
                    Y = gameY,
                    Z = gameZ,
                    Angle = angle,
                    Timestamp = DateTime.Now
                }
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 맵의 자동 보정 결과를 가져옵니다.
    /// </summary>
    public AutoCalibrationResult? GetAutoCalibrationResult(string mapKey)
    {
        var config = GetMapConfig(mapKey);
        if (config == null)
            return null;

        return AutoCalibrationService.Instance.CalibrateFromExistingPoints(config);
    }

    /// <summary>
    /// 모든 맵의 자동 보정 결과를 가져옵니다.
    /// </summary>
    public Dictionary<string, AutoCalibrationResult> GetAllAutoCalibrationResults()
    {
        return AutoCalibrationService.Instance.CalibrateAllMaps(_mapConfigs.Values);
    }
}
