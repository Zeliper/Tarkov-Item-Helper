namespace TarkovHelper.Models.Map;

/// <summary>
/// 맵 좌표 보정용 레퍼런스 포인트.
/// 게임 좌표와 수동으로 조정된 화면 좌표를 저장합니다.
/// </summary>
public sealed class CalibrationPoint
{
    /// <summary>
    /// 포인트 식별자 (예: 탈출구 ID)
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 포인트 이름 (UI 표시용)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 게임 내 X 좌표 (position.x)
    /// </summary>
    public double GameX { get; set; }

    /// <summary>
    /// 게임 내 Z 좌표 (position.z)
    /// </summary>
    public double GameZ { get; set; }

    /// <summary>
    /// 수동으로 조정된 화면 X 좌표 (SVG viewBox 기준)
    /// </summary>
    public double ScreenX { get; set; }

    /// <summary>
    /// 수동으로 조정된 화면 Y 좌표 (SVG viewBox 기준)
    /// </summary>
    public double ScreenY { get; set; }
}
