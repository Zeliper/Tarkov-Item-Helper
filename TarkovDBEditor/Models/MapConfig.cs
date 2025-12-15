using System.Text.Json.Serialization;

namespace TarkovDBEditor.Models;

/// <summary>
/// 맵의 개별 층(레벨) 설정.
/// SVG 파일 내의 레이어를 제어하는 데 사용됩니다.
/// </summary>
public class MapFloorConfig
{
    /// <summary>
    /// SVG에서 해당 층을 식별하는 그룹 ID (예: "basement", "main", "level2")
    /// </summary>
    public string LayerId { get; set; } = string.Empty;

    /// <summary>
    /// UI에 표시될 층 이름 (예: "Basement", "Main Floor", "Level 2")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 층 순서 (낮을수록 아래층, 0이 기본 층)
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// 기본으로 표시할 층인지 여부
    /// </summary>
    public bool IsDefault { get; set; } = false;
}

/// <summary>
/// 맵 좌표 변환 설정
/// </summary>
public class MapConfig
{
    /// <summary>
    /// 맵 이름 별칭 매핑 (다양한 이름을 표준 이름으로 변환)
    /// Key: 정규화된 별칭, Value: 표준 맵 이름
    /// </summary>
    private static readonly Dictionary<string, string> MapNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Labs / Lab 변형
        { "lab", "labs" },
        { "thelabs", "labs" },
        { "thelab", "labs" },
        // Labyrinth 변형
        { "thelabyrinth", "labyrinth" },
        // Streets 변형
        { "streets", "streetsoftarkov" },
        { "tarkovstreets", "streetsoftarkov" },
        // Ground Zero 변형
        { "groundzero", "groundzero" },
        { "gz", "groundzero" },
    };

    /// <summary>
    /// 맵 이름 정규화 (공백, 하이픈 제거 및 소문자 변환 후 별칭 적용)
    /// </summary>
    public static string NormalizeMapName(string? mapName)
    {
        if (string.IsNullOrEmpty(mapName))
            return string.Empty;

        var normalized = mapName.ToLowerInvariant().Replace(" ", "").Replace("-", "");

        // 별칭 매핑 적용
        if (MapNameAliases.TryGetValue(normalized, out var standardName))
            return standardName;

        return normalized;
    }

    /// <summary>
    /// 두 맵 이름이 동일한 맵을 가리키는지 확인 (정규화 후 비교)
    /// </summary>
    public static bool AreMapNamesEqual(string? mapName1, string? mapName2)
    {
        if (string.IsNullOrEmpty(mapName1) && string.IsNullOrEmpty(mapName2))
            return true;
        if (string.IsNullOrEmpty(mapName1) || string.IsNullOrEmpty(mapName2))
            return false;
        return NormalizeMapName(mapName1) == NormalizeMapName(mapName2);
    }

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
    /// 맵의 층(레벨) 설정 목록.
    /// SVG 파일에 여러 층이 있는 맵(Labs, Interchange, Factory, Reserve)에서 사용됩니다.
    /// null이거나 비어있으면 단일 층 맵으로 처리됩니다.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MapFloorConfig>? Floors { get; set; }

    /// <summary>
    /// 플레이어 마커 전용 좌표 변환 행렬 [a, b, c, d, tx, ty].
    /// tarkov-market.com 등 외부 소스의 좌표 시스템과 호환을 위해 사용됩니다.
    /// null이면 CalibratedTransform을 사용합니다.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? PlayerMarkerTransform { get; set; }

    /// <summary>
    /// Tarkov Market API의 SVG 좌표 범위 [minX, maxX, minY, maxY].
    /// API 마커의 geometry.x, geometry.y를 화면 좌표로 변환할 때 사용합니다.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? SvgBounds { get; set; }

    /// <summary>
    /// Tarkov Market API의 SVG 좌표를 맵 픽셀 좌표로 변환.
    /// SvgBounds가 설정되어 있어야 합니다.
    /// </summary>
    public (double screenX, double screenY) SvgToScreen(double svgX, double svgY)
    {
        if (SvgBounds == null || SvgBounds.Length < 4)
        {
            // Fallback: 변환 없이 중앙 기준
            return (ImageWidth / 2.0, ImageHeight / 2.0);
        }

        var minX = SvgBounds[0];
        var maxX = SvgBounds[1];
        var minY = SvgBounds[2];
        var maxY = SvgBounds[3];

        // SVG 좌표를 0~1 범위로 정규화
        var normalizedX = (svgX - minX) / (maxX - minX);
        var normalizedY = (svgY - minY) / (maxY - minY);

        // 픽셀 좌표로 변환
        var screenX = normalizedX * ImageWidth;
        var screenY = normalizedY * ImageHeight;

        return (screenX, screenY);
    }

    /// <summary>
    /// 맵 픽셀 좌표를 Tarkov Market API의 SVG 좌표로 변환 (역변환).
    /// SvgBounds가 설정되어 있어야 합니다.
    /// </summary>
    public (double svgX, double svgY) ScreenToSvg(double screenX, double screenY)
    {
        if (SvgBounds == null || SvgBounds.Length < 4)
        {
            return (0, 0);
        }

        var minX = SvgBounds[0];
        var maxX = SvgBounds[1];
        var minY = SvgBounds[2];
        var maxY = SvgBounds[3];

        // 픽셀 좌표를 0~1 범위로 정규화
        var normalizedX = screenX / ImageWidth;
        var normalizedY = screenY / ImageHeight;

        // SVG 좌표로 변환
        var svgX = normalizedX * (maxX - minX) + minX;
        var svgY = normalizedY * (maxY - minY) + minY;

        return (svgX, svgY);
    }

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
    /// 게임 좌표를 플레이어 마커용 맵 픽셀 좌표로 변환.
    /// PlayerMarkerTransform이 있으면 사용하고, 없으면 CalibratedTransform 사용.
    /// </summary>
    public (double screenX, double screenY) GameToScreenForPlayer(double gameX, double gameZ)
    {
        var transform = PlayerMarkerTransform ?? CalibratedTransform;

        if (transform == null || transform.Length < 6)
        {
            // Fallback: 단순 변환 (중앙 기준)
            return (ImageWidth / 2.0 + gameX, ImageHeight / 2.0 + gameZ);
        }

        var a = transform[0];
        var b = transform[1];
        var c = transform[2];
        var d = transform[3];
        var tx = transform[4];
        var ty = transform[5];

        var screenX = a * gameX + b * gameZ + tx;
        var screenY = c * gameX + d * gameZ + ty;

        return (screenX, screenY);
    }

    /// <summary>
    /// 맵 픽셀 좌표를 플레이어 좌표계 게임 좌표로 변환 (역행렬).
    /// PlayerMarkerTransform이 있으면 사용하고, 없으면 CalibratedTransform 사용.
    /// Floor 범위 설정 시 플레이어 좌표와 일치하는 좌표계로 저장할 때 사용.
    /// </summary>
    public (double gameX, double gameZ) ScreenToGameForPlayer(double screenX, double screenY)
    {
        var transform = PlayerMarkerTransform ?? CalibratedTransform;

        if (transform == null || transform.Length < 6)
        {
            // Fallback: 단순 역변환
            return (screenX - ImageWidth / 2.0, screenY - ImageHeight / 2.0);
        }

        var a = transform[0];
        var b = transform[1];
        var c = transform[2];
        var d = transform[3];
        var tx = transform[4];
        var ty = transform[5];

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
