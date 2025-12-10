namespace TarkovHelper.Models.MapTracker;

/// <summary>
/// 맵의 개별 층(레벨) 설정.
/// SVG 파일 내의 <g id="..."> 레이어를 제어하는 데 사용됩니다.
/// </summary>
public sealed class MapFloorConfig
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
