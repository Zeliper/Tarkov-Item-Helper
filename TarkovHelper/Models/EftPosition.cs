namespace TarkovHelper.Models;

/// <summary>
/// EFT world coordinate information.
/// Contains position data parsed from screenshot filenames.
/// </summary>
public sealed class EftPosition
{
    /// <summary>
    /// World coordinate X (game coordinate)
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// World coordinate Y (height)
    /// </summary>
    public double Y { get; init; }

    /// <summary>
    /// World coordinate Z (game coordinate)
    /// </summary>
    public double Z { get; init; }

    /// <summary>
    /// Player facing direction (0-360 degrees, optional)
    /// </summary>
    public double? Angle { get; init; }

    /// <summary>
    /// Time when screenshot was created
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>
    /// Original filename
    /// </summary>
    public string OriginalFileName { get; init; } = string.Empty;

    public override string ToString()
    {
        var angleStr = Angle.HasValue ? $", Angle: {Angle:F1}" : "";
        return $"X: {X:F2}, Y: {Y:F2}, Z: {Z:F2}{angleStr}";
    }
}
