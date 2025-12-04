namespace TarkovHelper.Models.MapTracker;

/// <summary>
/// 맵 탈출구 정보
/// </summary>
public sealed class MapExtract
{
    /// <summary>
    /// 탈출구 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 탈출구 이름 (영어)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 탈출구 이름 (한국어)
    /// </summary>
    public string? NameKo { get; set; }

    /// <summary>
    /// 맵 ID
    /// </summary>
    public string MapId { get; set; } = string.Empty;

    /// <summary>
    /// 맵 이름
    /// </summary>
    public string MapName { get; set; } = string.Empty;

    /// <summary>
    /// 탈출구 타입 (pmc, scav, shared)
    /// </summary>
    public ExtractFaction Faction { get; set; } = ExtractFaction.Shared;

    /// <summary>
    /// X 좌표
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y 좌표 (높이)
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Z 좌표
    /// </summary>
    public double Z { get; set; }

    /// <summary>
    /// 탈출구 윤곽선 (있는 경우)
    /// </summary>
    public List<OutlinePoint>? Outline { get; set; }

    /// <summary>
    /// 상단 높이 (층 구분용)
    /// </summary>
    public double? Top { get; set; }

    /// <summary>
    /// 하단 높이 (층 구분용)
    /// </summary>
    public double? Bottom { get; set; }
}

/// <summary>
/// 탈출구 진영 타입
/// </summary>
public enum ExtractFaction
{
    /// <summary>
    /// PMC 전용 탈출구
    /// </summary>
    Pmc,

    /// <summary>
    /// Scav 전용 탈출구
    /// </summary>
    Scav,

    /// <summary>
    /// 공용 탈출구 (PMC + Scav) 또는 Co-op
    /// </summary>
    Shared
}
