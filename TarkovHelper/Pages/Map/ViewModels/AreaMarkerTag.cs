using TarkovHelper.Models.Map;

namespace TarkovHelper.Pages.Map.ViewModels;

/// <summary>
/// Area 마커용 태그 클래스
/// </summary>
public class AreaMarkerTag
{
    public TaskObjectiveWithLocation? Objective { get; set; }
    public bool IsArea { get; set; }
}
