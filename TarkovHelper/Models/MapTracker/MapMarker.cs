namespace TarkovHelper.Models.MapTracker;

/// <summary>
/// 맵 마커 타입
/// </summary>
public enum MapMarkerType
{
    /// <summary>
    /// PMC 스폰 지점
    /// </summary>
    PmcSpawn,

    /// <summary>
    /// Scav 스폰 지점
    /// </summary>
    ScavSpawn,

    /// <summary>
    /// PMC 탈출구
    /// </summary>
    PmcExtraction,

    /// <summary>
    /// Scav 탈출구
    /// </summary>
    ScavExtraction,

    /// <summary>
    /// 공용 탈출구 (PMC + Scav)
    /// </summary>
    SharedExtraction,

    /// <summary>
    /// Transit (맵 이동)
    /// </summary>
    Transit,

    /// <summary>
    /// 보스 스폰 지점
    /// </summary>
    BossSpawn,

    /// <summary>
    /// 레이더 스폰 지점
    /// </summary>
    RaiderSpawn,

    /// <summary>
    /// 레버/스위치
    /// </summary>
    Lever,

    /// <summary>
    /// 키 필요 장소
    /// </summary>
    Keys
}

/// <summary>
/// 맵 마커 정보 (DB에서 로드)
/// </summary>
public sealed class MapMarker
{
    /// <summary>
    /// 마커 고유 ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 마커 이름 (영어)
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 마커 이름 (한국어)
    /// </summary>
    public string? NameKo { get; set; }

    /// <summary>
    /// 마커 타입
    /// </summary>
    public MapMarkerType MarkerType { get; set; } = MapMarkerType.PmcExtraction;

    /// <summary>
    /// 맵 키 (예: "Woods", "Customs")
    /// </summary>
    public string MapKey { get; set; } = string.Empty;

    /// <summary>
    /// X 좌표 (게임 좌표)
    /// </summary>
    public double X { get; set; }

    /// <summary>
    /// Y 좌표 (높이)
    /// </summary>
    public double Y { get; set; }

    /// <summary>
    /// Z 좌표 (게임 좌표)
    /// </summary>
    public double Z { get; set; }

    /// <summary>
    /// 층 ID (다층 맵용)
    /// </summary>
    public string? FloorId { get; set; }

    /// <summary>
    /// 마커 타입에 따른 색상 반환
    /// </summary>
    public static (byte R, byte G, byte B) GetMarkerColor(MapMarkerType type)
    {
        return type switch
        {
            MapMarkerType.PmcSpawn => (76, 175, 80),         // Green
            MapMarkerType.PmcExtraction => (76, 175, 80),    // Green
            MapMarkerType.SharedExtraction => (76, 175, 80), // Green
            MapMarkerType.ScavSpawn => (180, 180, 180),      // Light Gray
            MapMarkerType.ScavExtraction => (180, 180, 180), // Light Gray
            MapMarkerType.Transit => (255, 152, 0),          // Orange
            MapMarkerType.BossSpawn => (244, 67, 54),        // Red
            MapMarkerType.RaiderSpawn => (156, 39, 176),     // Purple
            MapMarkerType.Lever => (255, 235, 59),           // Yellow
            MapMarkerType.Keys => (33, 150, 243),            // Blue
            _ => (158, 158, 158)                              // Gray
        };
    }

    /// <summary>
    /// 탈출구 타입인지 확인
    /// </summary>
    public bool IsExtraction => MarkerType is MapMarkerType.PmcExtraction
                                          or MapMarkerType.ScavExtraction
                                          or MapMarkerType.SharedExtraction;

    /// <summary>
    /// 트랜짓 타입인지 확인
    /// </summary>
    public bool IsTransit => MarkerType == MapMarkerType.Transit;

    /// <summary>
    /// ExtractFaction으로 변환 (탈출구 타입일 경우)
    /// </summary>
    public ExtractFaction ToExtractFaction()
    {
        return MarkerType switch
        {
            MapMarkerType.PmcExtraction => ExtractFaction.Pmc,
            MapMarkerType.ScavExtraction => ExtractFaction.Scav,
            MapMarkerType.SharedExtraction => ExtractFaction.Shared,
            _ => ExtractFaction.Shared
        };
    }

    /// <summary>
    /// 마커 타입에 해당하는 아이콘 파일명 반환
    /// </summary>
    public static string GetIconFileName(MapMarkerType type)
    {
        return type switch
        {
            MapMarkerType.PmcSpawn => "PMC Spawn.webp",
            MapMarkerType.ScavSpawn => "SCAV Spawn.webp",
            MapMarkerType.PmcExtraction => "PMC Extraction.webp",
            MapMarkerType.ScavExtraction => "SCAV Extraction.webp",
            MapMarkerType.SharedExtraction => "PMC Extraction.webp", // Use PMC icon for shared
            MapMarkerType.Transit => "Transit.webp",
            MapMarkerType.BossSpawn => "BOSS Spawn.webp",
            MapMarkerType.RaiderSpawn => "Raider Spawn.webp",
            MapMarkerType.Lever => "Lever.webp",
            MapMarkerType.Keys => "Keys.webp",
            _ => "PMC Spawn.webp"
        };
    }

    /// <summary>
    /// 마커 타입 표시명
    /// </summary>
    public static string GetMarkerTypeName(MapMarkerType type)
    {
        return type switch
        {
            MapMarkerType.PmcSpawn => "PMC Spawn",
            MapMarkerType.ScavSpawn => "Scav Spawn",
            MapMarkerType.PmcExtraction => "PMC Extraction",
            MapMarkerType.ScavExtraction => "Scav Extraction",
            MapMarkerType.SharedExtraction => "Shared Extraction",
            MapMarkerType.Transit => "Transit",
            MapMarkerType.BossSpawn => "Boss Spawn",
            MapMarkerType.RaiderSpawn => "Raider Spawn",
            MapMarkerType.Lever => "Lever",
            MapMarkerType.Keys => "Keys",
            _ => "Unknown"
        };
    }
}
