using System.Text.Json.Serialization;

namespace TarkovHelper.Models;

/// <summary>
/// Map floor (level) configuration.
/// Used to control layers in SVG files.
/// </summary>
public class MapFloorConfig
{
    /// <summary>
    /// SVG group ID for this floor (e.g., "basement", "main", "level2")
    /// </summary>
    public string LayerId { get; set; } = string.Empty;

    /// <summary>
    /// Display name for UI (e.g., "Basement", "Main Floor", "Level 2")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Floor order (lower = lower floor, 0 = default floor)
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// Whether this is the default visible floor
    /// </summary>
    public bool IsDefault { get; set; } = false;
}

/// <summary>
/// Map coordinate transformation configuration
/// </summary>
public class MapConfig
{
    /// <summary>
    /// Map identifier key (e.g., "Woods", "Customs")
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Display name for UI
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// SVG filename (relative to Assets/DB/Maps/)
    /// </summary>
    public string SvgFileName { get; set; } = string.Empty;

    /// <summary>
    /// Map image pixel width
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>
    /// Map image pixel height
    /// </summary>
    public int ImageHeight { get; set; }

    /// <summary>
    /// Map alias list (for matching objective MapName)
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Aliases { get; set; }

    /// <summary>
    /// Calibrated coordinate transform matrix [a, b, c, d, tx, ty].
    /// Formula: screenX = a*gameX + b*gameZ + tx, screenY = c*gameX + d*gameZ + ty
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? CalibratedTransform { get; set; }

    /// <summary>
    /// Floor configuration list.
    /// Used for multi-floor maps (Labs, Interchange, Factory, Reserve).
    /// Null or empty means single floor map.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<MapFloorConfig>? Floors { get; set; }

    /// <summary>
    /// Player marker coordinate transform matrix [a, b, c, d, tx, ty].
    /// Used for compatibility with external coordinate systems.
    /// If null, uses CalibratedTransform.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? PlayerMarkerTransform { get; set; }

    /// <summary>
    /// SVG coordinate bounds [minX, maxX, minY, maxY].
    /// Used for converting API marker coordinates to screen coordinates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double[]? SvgBounds { get; set; }

    /// <summary>
    /// Convert game coordinates to map pixel coordinates
    /// </summary>
    public (double screenX, double screenY) GameToScreen(double gameX, double gameZ)
    {
        if (CalibratedTransform == null || CalibratedTransform.Length < 6)
        {
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
    /// Convert map pixel coordinates to game coordinates (inverse transform)
    /// </summary>
    public (double gameX, double gameZ) ScreenToGame(double screenX, double screenY)
    {
        if (CalibratedTransform == null || CalibratedTransform.Length < 6)
        {
            return (screenX - ImageWidth / 2.0, screenY - ImageHeight / 2.0);
        }

        var a = CalibratedTransform[0];
        var b = CalibratedTransform[1];
        var c = CalibratedTransform[2];
        var d = CalibratedTransform[3];
        var tx = CalibratedTransform[4];
        var ty = CalibratedTransform[5];

        var det = a * d - b * c;
        if (Math.Abs(det) < 1e-10)
        {
            return (0, 0);
        }

        var invA = d / det;
        var invB = -b / det;
        var invC = -c / det;
        var invD = a / det;

        var dx = screenX - tx;
        var dy = screenY - ty;

        var gameX = invA * dx + invB * dy;
        var gameZ = invC * dx + invD * dy;

        return (gameX, gameZ);
    }

    /// <summary>
    /// Convert game coordinates to player marker map pixel coordinates.
    /// Uses PlayerMarkerTransform if available, otherwise CalibratedTransform.
    /// </summary>
    public (double screenX, double screenY) GameToScreenForPlayer(double gameX, double gameZ)
    {
        var transform = PlayerMarkerTransform ?? CalibratedTransform;

        if (transform == null || transform.Length < 6)
        {
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
    /// Convert map pixel coordinates to player coordinate system game coordinates (inverse).
    /// Uses PlayerMarkerTransform if available, otherwise CalibratedTransform.
    /// </summary>
    public (double gameX, double gameZ) ScreenToGameForPlayer(double screenX, double screenY)
    {
        var transform = PlayerMarkerTransform ?? CalibratedTransform;

        if (transform == null || transform.Length < 6)
        {
            return (screenX - ImageWidth / 2.0, screenY - ImageHeight / 2.0);
        }

        var a = transform[0];
        var b = transform[1];
        var c = transform[2];
        var d = transform[3];
        var tx = transform[4];
        var ty = transform[5];

        var det = a * d - b * c;
        if (Math.Abs(det) < 1e-10)
        {
            return (0, 0);
        }

        var invA = d / det;
        var invB = -b / det;
        var invC = -c / det;
        var invD = a / det;

        var dx = screenX - tx;
        var dy = screenY - ty;

        var gameX = invA * dx + invB * dy;
        var gameZ = invC * dx + invD * dy;

        return (gameX, gameZ);
    }

    /// <summary>
    /// Check if the given map name matches this config
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
/// Map configuration list
/// </summary>
public class MapConfigList
{
    public List<MapConfig> Maps { get; set; } = new();

    /// <summary>
    /// Find config by map name
    /// </summary>
    public MapConfig? FindByMapName(string? mapName)
    {
        if (string.IsNullOrEmpty(mapName))
            return null;

        return Maps.FirstOrDefault(m => m.MatchesMapName(mapName));
    }
}
