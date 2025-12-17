namespace TarkovHelper.Models.Map;

/// <summary>
/// 맵 이미지 위의 화면 좌표 정보.
/// 월드 좌표가 맵 이미지의 픽셀 좌표로 변환된 결과입니다.
/// </summary>
public sealed class ScreenPosition
{
    /// <summary>
    /// 맵 설정 키 (예: Woods, Customs 등)
    /// </summary>
    public string MapKey { get; init; } = string.Empty;

    /// <summary>
    /// 이미지 내 픽셀 X 좌표
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// 이미지 내 픽셀 Y 좌표
    /// </summary>
    public double Y { get; init; }

    /// <summary>
    /// 플레이어가 바라보는 방향 (0~360도, 선택적)
    /// </summary>
    public double? Angle { get; init; }

    /// <summary>
    /// 원본 월드 좌표
    /// </summary>
    public EftPosition? OriginalPosition { get; init; }

    public override string ToString()
    {
        var angleStr = Angle.HasValue ? $", Angle: {Angle:F1}°" : "";
        return $"[{MapKey}] PixelX: {X:F0}, PixelY: {Y:F0}{angleStr}";
    }
}
