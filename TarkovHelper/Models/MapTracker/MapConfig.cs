using System.Text.Json.Serialization;

namespace TarkovHelper.Models.MapTracker;

/// <summary>
/// 개별 맵의 좌표 변환 설정.
/// 월드 좌표를 이미지 픽셀 좌표로 변환하는데 필요한 정보를 담습니다.
///
/// [설정 수정 가이드]
/// - WorldMinX/MaxX, WorldMinY/MaxY: 게임 내 맵의 실제 좌표 범위입니다.
///   게임에서 맵의 구석 좌표를 확인하여 입력하세요.
/// - ImageWidth/Height: 사용하는 맵 이미지의 실제 픽셀 크기입니다.
/// - InvertY: EFT는 일반적으로 Y축이 반대이므로 true로 설정합니다.
/// - OffsetX/Y: 이미지에 여백이 있는 경우 조정값입니다.
/// </summary>
public sealed class MapConfig
{
    /// <summary>
    /// 맵 식별 키 (예: "Woods", "Customs")
    /// 스크린샷 파일명의 맵 이름과 매칭됩니다.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// UI에 표시될 맵 이름
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 맵 이미지 파일 경로 (Assets/Maps/ 폴더 기준 상대 경로 또는 절대 경로)
    /// </summary>
    public string ImagePath { get; set; } = string.Empty;

    /// <summary>
    /// 월드 좌표 X 최소값
    /// </summary>
    public double WorldMinX { get; set; }

    /// <summary>
    /// 월드 좌표 X 최대값
    /// </summary>
    public double WorldMaxX { get; set; }

    /// <summary>
    /// 월드 좌표 Y 최소값
    /// </summary>
    public double WorldMinY { get; set; }

    /// <summary>
    /// 월드 좌표 Y 최대값
    /// </summary>
    public double WorldMaxY { get; set; }

    /// <summary>
    /// 맵 이미지 픽셀 너비
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>
    /// 맵 이미지 픽셀 높이
    /// </summary>
    public int ImageHeight { get; set; }

    /// <summary>
    /// Y축 좌표 반전 여부 (EFT는 보통 true)
    /// </summary>
    public bool InvertY { get; set; } = true;

    /// <summary>
    /// X축 좌표 반전 여부
    /// </summary>
    public bool InvertX { get; set; } = false;

    /// <summary>
    /// 이미지 X 오프셋 (여백 보정용)
    /// </summary>
    public double OffsetX { get; set; } = 0;

    /// <summary>
    /// 이미지 Y 오프셋 (여백 보정용)
    /// </summary>
    public double OffsetY { get; set; } = 0;

    /// <summary>
    /// 맵 별칭 목록 (스크린샷 파일명에 다른 이름이 사용될 수 있음)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Aliases { get; set; }

    /// <summary>
    /// 좌표 변환 매개변수 [scaleX, offsetX, scaleY, offsetY].
    /// tarkov.dev의 transform 배열과 동일한 형식.
    /// null이면 기존 WorldMin/Max 방식 사용.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? Transform { get; set; }

    /// <summary>
    /// 좌표 회전 각도 (90, 180, 270).
    /// tarkov.dev의 coordinateRotation 값.
    /// </summary>
    public int CoordinateRotation { get; set; } = 180;

    /// <summary>
    /// SVG 좌표 범위 [[maxLat, minLng], [minLat, maxLng]].
    /// tarkov.dev의 svgBounds 배열과 동일한 형식.
    /// SVG가 화면에서 어느 위치/크기로 표시될지 결정합니다.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[][]? SvgBounds { get; set; }

    /// <summary>
    /// 마커 크기 배율.
    /// 확대된 맵(Factory, Ground Zero 등)에서 마커도 같은 비율로 확대합니다.
    /// 기본값: 1.0
    /// </summary>
    public double MarkerScale { get; set; } = 1.0;
}
