using System.Text.Json.Serialization;

namespace TarkovDBEditor.Models;

/// <summary>
/// 맵 좌표 변환 설정
/// </summary>
public class MapConfig
{
    /// <summary>
    /// 맵 식별 키 (예: "Woods", "Customs")
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// UI에 표시될 맵 이름
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// SVG 파일명 (Resources/Maps/ 폴더 기준)
    /// </summary>
    public string SvgFileName { get; set; } = string.Empty;

    /// <summary>
    /// 맵 이미지 픽셀 너비
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>
    /// 맵 이미지 픽셀 높이
    /// </summary>
    public int ImageHeight { get; set; }

    /// <summary>
    /// 맵 별칭 목록 (Objective의 MapName과 매칭용)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Aliases { get; set; }

    /// <summary>
    /// 보정된 좌표 변환 행렬 [a, b, c, d, tx, ty].
    /// 변환 공식: screenX = a*gameX + b*gameZ + tx, screenY = c*gameX + d*gameZ + ty
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? CalibratedTransform { get; set; }

    /// <summary>
    /// 게임 좌표를 맵 픽셀 좌표로 변환
    /// </summary>
    public (double screenX, double screenY) GameToScreen(double gameX, double gameZ)
    {
        if (CalibratedTransform == null || CalibratedTransform.Length < 6)
        {
            // Fallback: 단순 변환 (중앙 기준)
            return (ImageWidth / 2.0 + gameX, ImageHeight / 2.0 + gameZ);
        }

        var a = CalibratedTransform[0];
        var b = CalibratedTransform[1];
        var c = CalibratedTransform[2];
        var d = CalibratedTransform[3];
        var tx = CalibratedTransform[4];
        var ty = CalibratedTransform[5];

        var screenX = a * gameX + b * gameZ + tx;
        var screenY = c * gameX + d * gameZ + ty;

        return (screenX, screenY);
    }

    /// <summary>
    /// 맵 픽셀 좌표를 게임 좌표로 변환 (역행렬)
    /// </summary>
    public (double gameX, double gameZ) ScreenToGame(double screenX, double screenY)
    {
        if (CalibratedTransform == null || CalibratedTransform.Length < 6)
        {
            // Fallback: 단순 역변환
            return (screenX - ImageWidth / 2.0, screenY - ImageHeight / 2.0);
        }

        var a = CalibratedTransform[0];
        var b = CalibratedTransform[1];
        var c = CalibratedTransform[2];
        var d = CalibratedTransform[3];
        var tx = CalibratedTransform[4];
        var ty = CalibratedTransform[5];

        // 역행렬 계산: [a b; c d]^-1 = 1/det * [d -b; -c a]
        var det = a * d - b * c;
        if (Math.Abs(det) < 1e-10)
        {
            return (0, 0);
        }

        var invA = d / det;
        var invB = -b / det;
        var invC = -c / det;
        var invD = a / det;

        // 평행이동 보정 후 역변환
        var dx = screenX - tx;
        var dy = screenY - ty;

        var gameX = invA * dx + invB * dy;
        var gameZ = invC * dx + invD * dy;

        return (gameX, gameZ);
    }

    /// <summary>
    /// 주어진 맵 이름이 이 설정과 매칭되는지 확인
    /// </summary>
    public bool MatchesMapName(string? mapName)
    {
        if (string.IsNullOrEmpty(mapName))
            return false;

        var normalized = mapName.ToLowerInvariant().Replace(" ", "").Replace("-", "");

        if (Key.ToLowerInvariant().Replace(" ", "").Replace("-", "") == normalized)
            return true;

        if (DisplayName.ToLowerInvariant().Replace(" ", "").Replace("-", "") == normalized)
            return true;

        if (Aliases != null)
        {
            foreach (var alias in Aliases)
            {
                if (alias.ToLowerInvariant().Replace(" ", "").Replace("-", "") == normalized)
                    return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 맵 설정 목록
/// </summary>
public class MapConfigList
{
    public List<MapConfig> Maps { get; set; } = new();

    /// <summary>
    /// 맵 이름으로 설정 찾기
    /// </summary>
    public MapConfig? FindByMapName(string? mapName)
    {
        if (string.IsNullOrEmpty(mapName))
            return null;

        return Maps.FirstOrDefault(m => m.MatchesMapName(mapName));
    }
}
