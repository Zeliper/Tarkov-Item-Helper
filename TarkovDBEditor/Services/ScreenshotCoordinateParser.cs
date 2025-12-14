using System.Globalization;
using System.Text.RegularExpressions;
using TarkovDBEditor.Models;

namespace TarkovDBEditor.Services;

/// <summary>
/// 스크린샷 파일명에서 좌표를 파싱하는 서비스.
///
/// EFT 스크린샷 형식: "2025-12-04[00-40]_95.77, 2.44, -134.02_-0.02395, -0.85891, 0.03920, -0.51007_16.74 (0).png"
/// 정규식에서 다음 명명된 그룹을 사용합니다:
/// - x: X 좌표 (필수)
/// - y: Y 좌표 (필수)
/// - z: Z 좌표 (선택)
/// - qx, qy, qz, qw: 쿼터니언 회전값 (선택 - angle 계산에 사용)
/// </summary>
public sealed class ScreenshotCoordinateParser
{
    private Regex _regex;
    private string _currentPattern;

    /// <summary>
    /// 기본 패턴으로 파서를 생성합니다.
    /// </summary>
    public ScreenshotCoordinateParser() : this(DefaultPattern)
    {
    }

    /// <summary>
    /// 지정된 패턴으로 파서를 생성합니다.
    /// </summary>
    public ScreenshotCoordinateParser(string pattern)
    {
        _currentPattern = pattern;
        _regex = CreateRegex(pattern);
    }

    /// <summary>
    /// 현재 사용 중인 정규식 패턴
    /// </summary>
    public string CurrentPattern => _currentPattern;

    /// <summary>
    /// 기본 파일명 패턴.
    /// EFT 스크린샷 형식 (쿼터니언): "2025-12-04[00-40]_95.77, 2.44, -134.02_-0.02395, -0.85891, 0.03920, -0.51007_16.74 (0).png"
    /// </summary>
    public const string DefaultPattern =
        @"\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_(?<x>-?\d+\.?\d*),\s*(?<y>-?\d+\.?\d*),\s*(?<z>-?\d+\.?\d*)_(?<qx>-?\d+\.?\d*),\s*(?<qy>-?\d+\.?\d*),\s*(?<qz>-?\d+\.?\d*),\s*(?<qw>-?\d+\.?\d*)_";

    /// <summary>
    /// 파일명에서 좌표를 파싱합니다.
    /// </summary>
    public bool TryParse(string fileName, out EftPosition? position)
    {
        position = null;

        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        try
        {
            var match = _regex.Match(fileName);
            if (!match.Success)
                return false;

            // 필수 그룹 확인 (x, y)
            var xGroup = match.Groups["x"];
            var yGroup = match.Groups["y"];

            if (!xGroup.Success || !yGroup.Success)
                return false;

            // X, Y 좌표 파싱
            if (!double.TryParse(xGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                return false;

            if (!double.TryParse(yGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                return false;

            // Z 좌표 파싱 (선택적)
            double z = 0;
            var zGroup = match.Groups["z"];
            if (zGroup.Success)
            {
                double.TryParse(zGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            }

            // 쿼터니언에서 각도 계산 시도
            double? angle = TryParseQuaternionAngle(match);

            position = new EftPosition
            {
                X = x,
                Y = y,
                Z = z,
                Angle = angle,
                Timestamp = DateTime.Now,
                OriginalFileName = fileName
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 쿼터니언 그룹에서 Y축 회전 각도를 계산합니다.
    /// </summary>
    private static double? TryParseQuaternionAngle(Match match)
    {
        var qxGroup = match.Groups["qx"];
        var qyGroup = match.Groups["qy"];
        var qzGroup = match.Groups["qz"];
        var qwGroup = match.Groups["qw"];

        if (!qxGroup.Success || !qyGroup.Success || !qzGroup.Success || !qwGroup.Success)
            return null;

        if (!double.TryParse(qxGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var qx) ||
            !double.TryParse(qyGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var qy) ||
            !double.TryParse(qzGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var qz) ||
            !double.TryParse(qwGroup.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var qw))
            return null;

        // 쿼터니언에서 Y축 회전(Yaw) 각도 계산
        var siny_cosp = 2.0 * (qw * qy + qx * qz);
        var cosy_cosp = 1.0 - 2.0 * (qy * qy + qz * qz);
        var yaw = Math.Atan2(siny_cosp, cosy_cosp);

        // 라디안을 도(degree)로 변환 후 180도 추가 (EFT 좌표계 보정)
        var degrees = yaw * 180.0 / Math.PI + 180.0;
        return degrees;
    }

    /// <summary>
    /// 정규식 패턴을 업데이트합니다.
    /// </summary>
    public bool UpdatePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            var newRegex = CreateRegex(pattern);

            // 필수 그룹 확인 (x, y만 필수)
            var groupNames = newRegex.GetGroupNames();
            if (!groupNames.Contains("x") || !groupNames.Contains("y"))
            {
                return false;
            }

            _regex = newRegex;
            _currentPattern = pattern;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 정규식 객체 생성
    /// </summary>
    private static Regex CreateRegex(string pattern)
    {
        return new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 패턴 유효성 검사
    /// </summary>
    public static bool IsValidPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        try
        {
            var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
            var groupNames = regex.GetGroupNames();
            return groupNames.Contains("x") && groupNames.Contains("y");
        }
        catch
        {
            return false;
        }
    }
}
