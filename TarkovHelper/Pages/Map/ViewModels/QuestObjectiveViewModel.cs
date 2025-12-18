using System.Windows;
using System.Windows.Media;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;

namespace TarkovHelper.Pages.Map.ViewModels;

/// <summary>
/// 퀘스트 목표 표시용 ViewModel
/// </summary>
public class QuestObjectiveViewModel
{
    public TaskObjectiveWithLocation Objective { get; }

    public string QuestName { get; }
    public string Description { get; }
    public string TypeDisplay { get; }
    public Brush TypeBrush { get; }
    public Visibility CompletedVisibility { get; }
    public bool IsSelected { get; }
    public Brush SelectionBorderBrush { get; }
    public Thickness SelectionBorderThickness { get; }

    // 체크박스용 프로퍼티
    public string ObjectiveId { get; }
    public bool IsChecked { get; set; }
    public TextDecorationCollection? TextDecoration { get; }
    public double ContentOpacity { get; }

    // 다른 맵 목표 표시용 프로퍼티
    public bool IsOnCurrentMap { get; }
    public string? OtherMapName { get; }
    public Visibility OtherMapBadgeVisibility { get; }
    public bool IsEnabled { get; }

    // 그룹화 표시용 프로퍼티
    public bool IsGrouped { get; set; }
    public Visibility QuestNameVisibility => IsGrouped ? Visibility.Collapsed : Visibility.Visible;
    public Thickness ItemMargin => IsGrouped ? new Thickness(16, 0, 0, 8) : new Thickness(0, 0, 0, 8);

    // 층 표시용 프로퍼티
    public string? FloorDisplay { get; }
    public Brush? FloorBrush { get; }
    public Visibility FloorBadgeVisibility { get; }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, bool isSelected = false)
        : this(objective, loc, null, isSelected, null, null, null)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected = false)
        : this(objective, loc, progressService, isSelected, null, null, null)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected, string? currentMapKey)
        : this(objective, loc, progressService, isSelected, currentMapKey, null, null)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected, string? currentMapKey, string? currentFloorId)
        : this(objective, loc, progressService, isSelected, currentMapKey, currentFloorId, null)
    {
    }

    public QuestObjectiveViewModel(TaskObjectiveWithLocation objective, LocalizationService loc, QuestProgressService? progressService, bool isSelected, string? currentMapKey, string? currentFloorId, (string arrow, string floorText, Color color)? floorIndicator)
    {
        Objective = objective;
        IsSelected = isSelected;
        ObjectiveId = objective.ObjectiveId;

        QuestName = loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;

        Description = loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;

        TypeDisplay = GetTypeDisplay(objective.Type);
        TypeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(objective.MarkerColor));

        // 체크박스 상태 설정 (ObjectiveId 기반 - 동일 설명 목표 개별 추적)
        if (progressService != null)
        {
            IsChecked = progressService.IsObjectiveCompletedById(objective.ObjectiveId);
        }
        else
        {
            IsChecked = objective.IsCompleted;
        }
        CompletedVisibility = IsChecked ? Visibility.Visible : Visibility.Collapsed;

        // 완료 시 스타일 변경
        TextDecoration = IsChecked ? TextDecorations.Strikethrough : null;
        ContentOpacity = IsChecked ? 0.5 : 1.0;

        // 선택 상태에 따른 테두리 스타일 (선택 시 1px 노란 테두리, 미선택 시 투명)
        SelectionBorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(255, 215, 0)) : Brushes.Transparent;
        SelectionBorderThickness = isSelected ? new Thickness(1.5) : new Thickness(0);

        // 현재 맵에 있는 목표인지 확인 (공백/하이픈 차이 무시)
        if (!string.IsNullOrEmpty(currentMapKey))
        {
            IsOnCurrentMap = objective.Locations.Any(loc =>
                MatchesMapKey(loc.MapName, currentMapKey) ||
                MatchesMapKey(loc.MapNormalizedName, currentMapKey));

            if (!IsOnCurrentMap && objective.Locations.Count > 0)
            {
                // 다른 맵 이름 표시
                var otherLocation = objective.Locations.FirstOrDefault();
                OtherMapName = otherLocation?.MapName ?? "Other Map";
                OtherMapBadgeVisibility = Visibility.Visible;
                IsEnabled = false;
            }
            else
            {
                OtherMapBadgeVisibility = Visibility.Collapsed;
                IsEnabled = true;
            }
        }
        else
        {
            IsOnCurrentMap = true;
            OtherMapBadgeVisibility = Visibility.Collapsed;
            IsEnabled = true;
        }

        // 층 정보 초기화 - floorIndicator가 있으면 화살표 포함 표시
        if (floorIndicator.HasValue)
        {
            var (arrow, floorText, indicatorColor) = floorIndicator.Value;
            FloorDisplay = $"{arrow}{floorText}";
            FloorBrush = new SolidColorBrush(indicatorColor);
            FloorBadgeVisibility = Visibility.Visible;
        }
        else if (objective.Locations.Count > 0)
        {
            var floorId = objective.Locations[0].FloorId;
            if (!string.IsNullOrEmpty(floorId) && !string.IsNullOrEmpty(currentFloorId) &&
                !string.Equals(floorId, currentFloorId, StringComparison.OrdinalIgnoreCase))
            {
                // 다른 층이지만 floorIndicator가 없는 경우 (fallback)
                FloorDisplay = GetFloorDisplayText(floorId);
                FloorBrush = new SolidColorBrush(GetFloorColor(floorId, currentFloorId));
                FloorBadgeVisibility = Visibility.Visible;
            }
            else
            {
                FloorBadgeVisibility = Visibility.Collapsed;
            }
        }
        else
        {
            FloorBadgeVisibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// FloorId를 표시용 텍스트로 변환합니다.
    /// B = basement (main보다 아래), G = Ground (main), 2F, 3F 등
    /// </summary>
    public static string GetFloorDisplayText(string floorId)
    {
        // FloorId 패턴: "basement", "main", "first", "second", "third", "roof" 등
        return floorId.ToLowerInvariant() switch
        {
            "basement" or "basement1" or "basement-1" or "b1" => "B",
            "basement2" or "basement-2" or "b2" => "B2",
            "basement3" or "basement-3" or "b3" => "B3",
            "main" or "ground" or "1" or "first" => "G",
            "second" or "2" => "2F",
            "third" or "3" => "3F",
            "roof" or "rooftop" => "RF",
            _ => floorId.Length <= 3 ? floorId.ToUpperInvariant() : floorId.Substring(0, 2).ToUpperInvariant()
        };
    }

    /// <summary>
    /// 층에 따른 색상을 반환합니다.
    /// </summary>
    public static Color GetFloorColor(string floorId, string? currentFloorId)
    {
        // 현재 층과 같으면 회색, 다르면 강조색
        if (!string.IsNullOrEmpty(currentFloorId) &&
            string.Equals(floorId, currentFloorId, StringComparison.OrdinalIgnoreCase))
        {
            return Color.FromRgb(128, 128, 128); // 회색
        }

        // 지하층은 파란색, 기본층은 초록색, 상층은 주황색
        var lowerFloorId = floorId.ToLowerInvariant();
        if (lowerFloorId.Contains("basement") || lowerFloorId.StartsWith("b"))
        {
            return Color.FromRgb(33, 150, 243); // 파란색
        }
        if (lowerFloorId == "main" || lowerFloorId == "ground" || lowerFloorId == "1" || lowerFloorId == "first")
        {
            return Color.FromRgb(76, 175, 80); // 초록색
        }
        return Color.FromRgb(255, 152, 0); // 주황색
    }

    private static string GetTypeDisplay(string type) => type switch
    {
        "visit" => "Visit",
        "mark" => "Mark",
        "plantItem" => "Plant",
        "extract" => "Extract",
        "findItem" => "Find",
        _ => type
    };

    /// <summary>
    /// 맵 이름 비교 (공백, 하이픈, 대소문자 차이 무시)
    /// </summary>
    private static bool MatchesMapKey(string? mapName, string mapKey)
    {
        if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(mapKey))
            return false;

        // 공백, 하이픈 제거 후 소문자로 비교
        var normalizedMapName = mapName.Replace(" ", "").Replace("-", "").ToLowerInvariant();
        var normalizedMapKey = mapKey.Replace(" ", "").Replace("-", "").ToLowerInvariant();
        return normalizedMapName == normalizedMapKey;
    }
}
