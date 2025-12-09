namespace TarkovHelper.Models.MapTracker;

/// <summary>
/// 기존 tarkov.dev 맵의 참조 데이터.
/// 정확한 좌표 변환을 위한 "정답" 데이터로 사용됩니다.
/// </summary>
public sealed class OldMapReferenceData
{
    /// <summary>
    /// 맵 키 (예: "Woods", "Customs")
    /// </summary>
    public required string MapKey { get; init; }

    /// <summary>
    /// 구 지도 이미지 너비 (픽셀)
    /// </summary>
    public required double OldImageWidth { get; init; }

    /// <summary>
    /// 구 지도 이미지 높이 (픽셀)
    /// </summary>
    public required double OldImageHeight { get; init; }

    /// <summary>
    /// 구 지도 변환 파라미터 [scaleX, marginX, scaleY, marginY]
    /// tarkov.dev CRS 변환 방식
    /// </summary>
    public required double[] OldTransform { get; init; }

    /// <summary>
    /// 구 지도 SVG bounds [[maxLng, minLat], [minLng, maxLat]]
    /// 게임 좌표 범위
    /// </summary>
    public required double[][] OldSvgBounds { get; init; }

    /// <summary>
    /// 좌표 회전 각도 (도)
    /// </summary>
    public required int OldCoordinateRotation { get; init; }

    /// <summary>
    /// 신 지도 이미지 너비 (픽셀)
    /// </summary>
    public required double NewImageWidth { get; init; }

    /// <summary>
    /// 신 지도 이미지 높이 (픽셀)
    /// </summary>
    public required double NewImageHeight { get; init; }

    /// <summary>
    /// 모든 맵의 참조 데이터를 반환합니다.
    /// tarkov.dev maps.json 기반 데이터.
    /// </summary>
    public static IReadOnlyList<OldMapReferenceData> GetAllReferences()
    {
        return new List<OldMapReferenceData>
        {
            new()
            {
                MapKey = "Woods",
                OldImageWidth = 8192,
                OldImageHeight = 8192,
                OldTransform = [0.635152, 387.2544, 0.626805, 566.996],
                OldSvgBounds = [[650, -945], [-695, 470]],
                OldCoordinateRotation = 180,
                NewImageWidth = 4800,
                NewImageHeight = 4800
            },
            new()
            {
                MapKey = "Customs",
                OldImageWidth = 5765,
                OldImageHeight = 4193,
                OldTransform = [0.989938, 698.548, 1.42898, 815.237],
                OldSvgBounds = [[698, -307], [-372, 237]],
                OldCoordinateRotation = 180,
                NewImageWidth = 4400,
                NewImageHeight = 3200
            },
            new()
            {
                MapKey = "Shoreline",
                OldImageWidth = 7680,
                OldImageHeight = 6400,
                OldTransform = [0.7904, 410.96, 1.001, 694.92],
                OldSvgBounds = [[508, -415], [-1060, 618]],
                OldCoordinateRotation = 180,
                NewImageWidth = 3700,
                NewImageHeight = 3100
            },
            new()
            {
                MapKey = "Interchange",
                OldImageWidth = 4000,
                OldImageHeight = 3900,
                OldTransform = [1.08491, 616.556, 1.05788, 537.323],
                OldSvgBounds = [[532.75, -442.75], [-364, 453.5]],
                OldCoordinateRotation = 180,
                NewImageWidth = 4000,
                NewImageHeight = 3900
            },
            new()
            {
                MapKey = "Reserve",
                OldImageWidth = 3200,
                OldImageHeight = 3000,
                OldTransform = [1.52747, 471.774, 1.5567, 542.479],
                OldSvgBounds = [[289, -338], [-303, 336]],
                OldCoordinateRotation = 180,
                NewImageWidth = 3200,
                NewImageHeight = 3000
            },
            new()
            {
                MapKey = "Lighthouse",
                OldImageWidth = 3100,
                OldImageHeight = 3700,
                OldTransform = [0.5852, 0, 0.4294, 0],
                OldSvgBounds = [[515, -998], [-545, 725]],
                OldCoordinateRotation = 180,
                NewImageWidth = 3100,
                NewImageHeight = 3700
            },
            new()
            {
                MapKey = "StreetsOfTarkov",
                OldImageWidth = 3260,
                OldImageHeight = 3500,
                OldTransform = [2.04668, 0, 1.59904, 0],
                OldSvgBounds = [[323, -317], [-280, 554]],
                OldCoordinateRotation = 180,
                NewImageWidth = 3260,
                NewImageHeight = 3500
            },
            new()
            {
                MapKey = "Factory",
                OldImageWidth = 3600,
                OldImageHeight = 3600,
                OldTransform = [44.8285, 3299.53, 41.5216, 3550.62],
                OldSvgBounds = [[79, -64.5], [-66.5, 67.4]],
                OldCoordinateRotation = 90,
                NewImageWidth = 3600,
                NewImageHeight = 3600
            },
            new()
            {
                MapKey = "GroundZero",
                OldImageWidth = 2800,
                OldImageHeight = 3100,
                OldTransform = [4.2051, 1342.58, 3.32583, 413.19],
                OldSvgBounds = [[249, -124], [-99, 364]],
                OldCoordinateRotation = 180,
                NewImageWidth = 2800,
                NewImageHeight = 3100
            },
            new()
            {
                MapKey = "Labs",
                OldImageWidth = 5500,
                OldImageHeight = 4200,
                OldTransform = [4.39242, 2148.09, 4.12102, 1388.25],
                OldSvgBounds = [[-80, -477], [-287, -193]],
                OldCoordinateRotation = 270,
                NewImageWidth = 5500,
                NewImageHeight = 4200
            }
        };
    }
}
