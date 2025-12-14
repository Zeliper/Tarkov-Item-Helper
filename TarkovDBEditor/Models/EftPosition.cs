namespace TarkovDBEditor.Models;

/// <summary>
/// EFT 월드 좌표 정보를 나타내는 모델.
/// 스크린샷 파일명에서 파싱된 좌표 데이터를 담습니다.
/// </summary>
public sealed class EftPosition
{
    /// <summary>
    /// 월드 좌표 X (게임 내 좌표)
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// 월드 좌표 Y (높이)
    /// </summary>
    public double Y { get; init; }

    /// <summary>
    /// 월드 좌표 Z (게임 내 좌표)
    /// </summary>
    public double Z { get; init; }

    /// <summary>
    /// 플레이어가 바라보는 방향 (0~360도, 선택적)
    /// </summary>
    public double? Angle { get; init; }

    /// <summary>
    /// 스크린샷이 생성된 시간
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>
    /// 원본 파일 이름
    /// </summary>
    public string OriginalFileName { get; init; } = string.Empty;

    public override string ToString()
    {
        var angleStr = Angle.HasValue ? $", Angle: {Angle:F1}°" : "";
        return $"X: {X:F2}, Y: {Y:F2}, Z: {Z:F2}{angleStr}";
    }
}
