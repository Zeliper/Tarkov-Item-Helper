using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Pages;

/// <summary>
/// Quest Drawer 그룹 ViewModel (퀘스트별 그룹)
/// </summary>
public class QuestDrawerGroup : System.ComponentModel.INotifyPropertyChanged
{
    public string QuestId { get; }
    public string QuestName { get; }
    public bool IsCompleted { get; }
    public bool IsVisible { get; set; } = true; // 맵에 표시 여부
    public bool IsHighlighted { get; set; } // 하이라이트 여부
    public List<QuestDrawerItem> Objectives { get; }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public QuestDrawerGroup(string questId, string questName, bool isCompleted, List<QuestDrawerItem> objectives)
    {
        QuestId = questId;
        QuestName = questName;
        IsCompleted = isCompleted;
        Objectives = objectives;
    }

    public int ObjectiveCount => Objectives.Count;
    public int CompletedCount => Objectives.Count(o => o.IsCompleted);
    public string ProgressText => $"{CompletedCount}/{ObjectiveCount}";

    /// <summary>
    /// 진행률 (0.0 ~ 1.0)
    /// </summary>
    public double ProgressPercent => ObjectiveCount > 0 ? (double)CompletedCount / ObjectiveCount : 0;

    /// <summary>
    /// 선택된 항목 여부
    /// </summary>
    public bool IsSelected { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Quest Drawer 아이템 ViewModel
/// </summary>
public class QuestDrawerItem
{
    public TaskObjectiveWithLocation Objective { get; }
    public bool IsCompleted { get; }

    public QuestDrawerItem(TaskObjectiveWithLocation objective, bool isCompleted)
    {
        Objective = objective;
        IsCompleted = isCompleted;
    }

    /// <summary>
    /// 표시용 퀘스트 이름
    /// </summary>
    public string TaskDisplayName =>
        !string.IsNullOrEmpty(Objective.TaskNameKo) ? Objective.TaskNameKo : Objective.TaskName;

    /// <summary>
    /// 표시용 목표 설명 (짧게)
    /// </summary>
    public string DescriptionDisplay
    {
        get
        {
            var desc = !string.IsNullOrEmpty(Objective.DescriptionKo)
                ? Objective.DescriptionKo
                : Objective.Description;

            // 최대 60자로 제한
            if (desc.Length > 60)
                desc = desc.Substring(0, 57) + "...";

            return desc;
        }
    }

    /// <summary>
    /// 목표 타입 아이콘 (이모지)
    /// </summary>
    public string TypeIcon => Objective.Type switch
    {
        "visit" => "📍",      // 방문
        "mark" => "🎯",       // 마킹
        "plantItem" => "📦",  // 아이템 설치
        "extract" => "🚪",    // 탈출
        "findItem" => "🔍",   // 아이템 찾기
        "giveItem" => "🎁",   // 아이템 전달
        "shoot" => "💀",      // 처치
        "skill" => "📈",      // 스킬
        "buildWeapon" => "🔧", // 무기 조립
        "traderLevel" => "💼", // 트레이더 레벨
        _ => "📋"             // 기타
    };

    /// <summary>
    /// 위치 정보가 있는지 여부
    /// </summary>
    public bool HasLocation => Objective.Locations.Any(l => l.Z.HasValue);

    /// <summary>
    /// 첫 번째 위치의 맵 이름
    /// </summary>
    public string MapName => Objective.Locations.FirstOrDefault()?.MapName ?? "";

    /// <summary>
    /// 맵 이름 짧은 태그
    /// </summary>
    public string MapTag
    {
        get
        {
            var map = MapName.ToLowerInvariant();
            return map switch
            {
                "customs" => "CUS",
                "factory" => "FAC",
                "interchange" => "INT",
                "woods" => "WOD",
                "shoreline" => "SHR",
                "reserve" => "RSV",
                "lighthouse" => "LHT",
                "streets of tarkov" => "STR",
                "ground zero" => "GZ",
                "labs" => "LAB",
                _ => map.Length > 3 ? map.Substring(0, 3).ToUpperInvariant() : map.ToUpperInvariant()
            };
        }
    }

    /// <summary>
    /// 맵 태그 표시 여부
    /// </summary>
    public bool ShowMapTag => !string.IsNullOrEmpty(MapName);
}

/// <summary>
/// 문자열이 비어있으면 Visible, 있으면 Collapsed (Watermark용)
/// </summary>
public class StringToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// bool을 Visibility로 변환
/// </summary>
public class BoolToVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 진행률(0.0~1.0)을 프로그레스 바 너비로 변환 (최대 120px)
/// </summary>
public class ProgressWidthConverter : System.Windows.Data.IValueConverter
{
    private const double MaxWidth = 120.0;

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is double percent)
        {
            return Math.Max(0, Math.Min(MaxWidth, percent * MaxWidth));
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
