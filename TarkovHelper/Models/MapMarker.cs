namespace TarkovHelper.Models;

/// <summary>
/// Map marker types
/// </summary>
public enum MarkerType
{
    PmcSpawn,
    ScavSpawn,
    PmcExtraction,
    ScavExtraction,
    SharedExtraction,
    Transit,
    BossSpawn,
    RaiderSpawn,
    Lever,
    Keys
}

/// <summary>
/// Map marker model for locations on the map
/// </summary>
public class MapMarker
{
    /// <summary>
    /// Unique marker ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Marker name (English)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Marker name (Korean)
    /// </summary>
    public string? NameKo { get; set; }

    /// <summary>
    /// Marker name (Japanese)
    /// </summary>
    public string? NameJa { get; set; }

    /// <summary>
    /// Marker type
    /// </summary>
    public MarkerType Type { get; set; }

    /// <summary>
    /// Map key this marker belongs to
    /// </summary>
    public string MapKey { get; set; } = string.Empty;

    /// <summary>
    /// Game X coordinate
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Game Y coordinate (height)
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Game Z coordinate
    /// </summary>
    public double Z { get; set; }

    /// <summary>
    /// Floor ID for multi-floor maps
    /// </summary>
    public string? FloorId { get; set; }

    /// <summary>
    /// Get icon filename for this marker type
    /// </summary>
    public string GetIconFileName()
    {
        return Type switch
        {
            MarkerType.PmcSpawn => "PMC Spawn.webp",
            MarkerType.ScavSpawn => "SCAV Spawn.webp",
            MarkerType.PmcExtraction => "PMC Extraction.webp",
            MarkerType.ScavExtraction => "SCAV Extraction.webp",
            MarkerType.SharedExtraction => "PMC Extraction.webp",
            MarkerType.Transit => "Transit.webp",
            MarkerType.BossSpawn => "BOSS Spawn.webp",
            MarkerType.RaiderSpawn => "Raider Spawn.webp",
            MarkerType.Lever => "Lever.webp",
            MarkerType.Keys => "Keys.webp",
            _ => "PMC Spawn.webp"
        };
    }

    /// <summary>
    /// Check if this is an extraction marker
    /// </summary>
    public bool IsExtraction => Type is MarkerType.PmcExtraction or MarkerType.ScavExtraction or MarkerType.SharedExtraction;

    /// <summary>
    /// Check if this is a spawn marker
    /// </summary>
    public bool IsSpawn => Type is MarkerType.PmcSpawn or MarkerType.ScavSpawn or MarkerType.BossSpawn or MarkerType.RaiderSpawn;

    /// <summary>
    /// Parse MarkerType from string
    /// </summary>
    public static MarkerType ParseType(string typeStr)
    {
        return typeStr switch
        {
            "PmcSpawn" => MarkerType.PmcSpawn,
            "ScavSpawn" => MarkerType.ScavSpawn,
            "PmcExtraction" => MarkerType.PmcExtraction,
            "ScavExtraction" => MarkerType.ScavExtraction,
            "SharedExtraction" => MarkerType.SharedExtraction,
            "Transit" => MarkerType.Transit,
            "BossSpawn" => MarkerType.BossSpawn,
            "RaiderSpawn" => MarkerType.RaiderSpawn,
            "Lever" => MarkerType.Lever,
            "Keys" => MarkerType.Keys,
            _ => MarkerType.PmcSpawn
        };
    }

    /// <summary>
    /// Get marker color as RGB tuple for the given marker type
    /// </summary>
    public static (byte R, byte G, byte B) GetMarkerColor(MarkerType type)
    {
        return type switch
        {
            MarkerType.PmcSpawn => (0xFF, 0x98, 0x00),       // Orange
            MarkerType.ScavSpawn => (0xFF, 0xC1, 0x07),      // Amber
            MarkerType.PmcExtraction => (0x4C, 0xAF, 0x50),  // Green
            MarkerType.ScavExtraction => (0x8B, 0xC3, 0x4A), // Light Green
            MarkerType.SharedExtraction => (0x00, 0xBC, 0xD4), // Cyan
            MarkerType.Transit => (0x21, 0x96, 0xF3),        // Blue
            MarkerType.BossSpawn => (0xF4, 0x43, 0x36),      // Red
            MarkerType.RaiderSpawn => (0xE9, 0x1E, 0x63),    // Pink
            MarkerType.Lever => (0x9C, 0x27, 0xB0),          // Purple
            MarkerType.Keys => (0xFF, 0xC1, 0x07),           // Amber
            _ => (0x9E, 0x9E, 0x9E)                          // Grey
        };
    }
}
