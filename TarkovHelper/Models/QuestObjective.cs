using System.Text.Json;

namespace TarkovHelper.Models;

/// <summary>
/// Location point for quest objectives (polygon vertices, single points, or optional locations)
/// </summary>
public class LocationPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string? FloorId { get; set; }

    public LocationPoint() { }

    public LocationPoint(double x, double y, double z, string? floorId = null)
    {
        X = x;
        Y = y;
        Z = z;
        FloorId = floorId;
    }
}

/// <summary>
/// Quest objective type for categorization and display
/// </summary>
public enum QuestObjectiveType
{
    Custom,     // Default/uncategorized
    Kill,       // Kill targets
    Collect,    // Collect items (pick up)
    HandOver,   // Hand over items to trader
    Visit,      // Visit location
    Mark,       // Mark location with marker
    Stash,      // Stash items in location
    Survive,    // Survive and extract
    Build,      // Build hideout module
    Task        // Generic task
}

/// <summary>
/// Quest objective with map location data for display on the map
/// </summary>
public class QuestObjective
{
    public string Id { get; set; } = string.Empty;
    public string QuestId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Type of objective (Kill, Visit, Mark, etc.)
    /// </summary>
    public QuestObjectiveType ObjectiveType { get; set; } = QuestObjectiveType.Custom;

    /// <summary>
    /// Quest name (English)
    /// </summary>
    public string QuestName { get; set; } = string.Empty;

    /// <summary>
    /// Quest name (Korean)
    /// </summary>
    public string? QuestNameKo { get; set; }

    /// <summary>
    /// Quest name (Japanese)
    /// </summary>
    public string? QuestNameJa { get; set; }

    /// <summary>
    /// Trader name for the quest
    /// </summary>
    public string? TraderName { get; set; }

    /// <summary>
    /// Map name specified in the objective
    /// </summary>
    public string? MapName { get; set; }

    /// <summary>
    /// Quest's location (fallback when MapName is not set)
    /// </summary>
    public string? QuestLocation { get; set; }

    /// <summary>
    /// Effective map name - uses MapName if available, otherwise QuestLocation
    /// </summary>
    public string? EffectiveMapName => !string.IsNullOrEmpty(MapName) ? MapName : QuestLocation;

    /// <summary>
    /// Location points defining an area (polygon), line, or single point
    /// </summary>
    public List<LocationPoint> LocationPoints { get; set; } = new();

    /// <summary>
    /// Optional points for OR locations (alternative spots)
    /// </summary>
    public List<LocationPoint> OptionalPoints { get; set; } = new();

    /// <summary>
    /// JSON setter for LocationPoints
    /// </summary>
    public string? LocationPointsJson
    {
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                try
                {
                    var points = JsonSerializer.Deserialize<List<LocationPoint>>(value);
                    LocationPoints = points ?? new List<LocationPoint>();
                }
                catch
                {
                    LocationPoints = new List<LocationPoint>();
                }
            }
            else
            {
                LocationPoints = new List<LocationPoint>();
            }
        }
    }

    /// <summary>
    /// JSON setter for OptionalPoints
    /// </summary>
    public string? OptionalPointsJson
    {
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                try
                {
                    var points = JsonSerializer.Deserialize<List<LocationPoint>>(value);
                    OptionalPoints = points ?? new List<LocationPoint>();
                }
                catch
                {
                    OptionalPoints = new List<LocationPoint>();
                }
            }
            else
            {
                OptionalPoints = new List<LocationPoint>();
            }
        }
    }

    /// <summary>
    /// Whether this objective has location coordinates
    /// </summary>
    public bool HasCoordinates => LocationPoints.Count > 0;

    /// <summary>
    /// Whether this objective has optional points
    /// </summary>
    public bool HasOptionalPoints => OptionalPoints.Count > 0;
}
