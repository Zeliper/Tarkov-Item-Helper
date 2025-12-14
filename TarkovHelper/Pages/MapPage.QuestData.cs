using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovHelper.Models.MapTracker;

namespace TarkovHelper.Pages;

/// <summary>
/// Map Page - Quest Data partial class
/// </summary>
public partial class MapPage : UserControl
{
    #region Quest Data

    /// <summary>
    /// 퀘스트 목표 데이터 로드 (DB에서)
    /// </summary>
    private async Task LoadQuestDataAsync()
    {
        try
        {
            StatusText.Text = "Loading quest objectives from DB...";

            await _dbService.LoadQuestObjectivesAsync();

            StatusText.Text = $"Loaded {_dbService.TotalObjectiveCount} quest objectives from DB";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Quest data load failed: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[MapPage] Quest data load error: {ex}");
        }
    }

    /// <summary>
    /// 현재 맵의 퀘스트 목표 업데이트
    /// </summary>
    private void UpdateCurrentMapQuestObjectives()
    {
        _currentMapQuestObjectives.Clear();

        if (_currentMapConfig == null || !_dbService.ObjectivesLoaded) return;

        // 맵 키로 필터링 (DbMapConfig.Key 사용)
        var mapKey = _currentMapConfig.Key;

        // DB에서 퀘스트 목표 가져와서 변환
        var dbObjectives = _dbService.GetObjectivesForMap(mapKey);
        _currentMapQuestObjectives = dbObjectives.Select(ConvertToTaskObjective).ToList();

        System.Diagnostics.Debug.WriteLine($"[MapPage] Map '{mapKey}': {_currentMapQuestObjectives.Count} quest objectives from DB");
    }

    /// <summary>
    /// DbQuestObjective를 TaskObjectiveWithLocation으로 변환
    /// </summary>
    private TaskObjectiveWithLocation ConvertToTaskObjective(DbQuestObjective dbObj)
    {
        var result = new TaskObjectiveWithLocation
        {
            ObjectiveId = dbObj.Id,
            Description = dbObj.Description,
            Type = "visit", // DB에서는 타입 정보가 없으므로 기본값
            TaskNormalizedName = dbObj.QuestId,
            TaskName = dbObj.QuestName ?? dbObj.QuestId,
            TaskNameKo = dbObj.QuestNameKo,
            Locations = new List<QuestObjectiveLocation>()
        };

        // LocationPoints를 QuestObjectiveLocation으로 변환
        // DB 좌표: X=수평X, Y=높이, Z=수평깊이
        foreach (var pt in dbObj.LocationPoints)
        {
            result.Locations.Add(new QuestObjectiveLocation
            {
                Id = $"{dbObj.Id}_{pt.X}_{pt.Z}",
                MapName = dbObj.EffectiveMapName ?? "",
                X = pt.X,
                Y = pt.Y,  // 높이
                Z = pt.Z   // 수평 깊이 (GameToScreen의 두 번째 파라미터)
            });
        }

        // OptionalPoints도 Locations에 추가 (별도 표시가 필요하면 나중에 분리)
        foreach (var pt in dbObj.OptionalPoints)
        {
            result.Locations.Add(new QuestObjectiveLocation
            {
                Id = $"{dbObj.Id}_opt_{pt.X}_{pt.Z}",
                MapName = dbObj.EffectiveMapName ?? "",
                X = pt.X,
                Y = pt.Y,
                Z = pt.Z
            });
        }

        return result;
    }

    /// <summary>
    /// 퀘스트 마커 다시 그리기
    /// </summary>
    private void RefreshQuestMarkers()
    {
        if (QuestMarkersCanvas == null) return;
        QuestMarkersCanvas.Children.Clear();
        _markersByObjectiveId.Clear(); // 마커 매핑 초기화

        if (!_showQuestMarkers || _currentMapConfig == null) return;

        UpdateCurrentMapQuestObjectives();

        var inverseScale = 1.0 / _zoomLevel;
        var hasFloors = _sortedFloors != null && _sortedFloors.Count > 0;

        var visibleCount = 0;
        foreach (var objective in _currentMapQuestObjectives)
        {
            // 숨긴 퀘스트 필터링
            if (_hiddenQuestIds.Contains(objective.TaskNormalizedName))
                continue;

            // 퀘스트 타입 필터링
            if (!IsQuestTypeEnabled(objective.Type))
                continue;

            // 현재 맵의 위치만 필터링
            var locationsForCurrentMap = objective.Locations
                .Where(loc => IsLocationOnCurrentMap(loc))
                .ToList();

            if (locationsForCurrentMap.Count == 0) continue;

            // 완료 여부 확인 (목표별)
            var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);
            objective.IsCompleted = isCompleted;

            // 목표 타입별 색상 (완료된 경우 흐리게)
            var objectiveColor = GetQuestTypeColor(objective.Type);
            var opacity = isCompleted ? 0.4 : 1.0;

            // Multi-point 렌더링 (TarkovDBEditor 방식)
            RenderQuestObjectiveArea(objective, locationsForCurrentMap, objectiveColor, inverseScale, hasFloors, opacity);
            visibleCount++;
        }

        // 카운트 업데이트 (표시 중인 퀘스트만)
        QuestMarkerCountText.Text = visibleCount.ToString();
    }

    /// <summary>
    /// 위치가 현재 맵에 있는지 확인
    /// </summary>
    private bool IsLocationOnCurrentMap(QuestObjectiveLocation location)
    {
        if (_currentMapConfig == null) return false;

        var mapKey = _currentMapConfig.Key.ToLowerInvariant();
        var locationMapName = location.MapNormalizedName?.ToLowerInvariant() ?? "";
        var locationMapNameAlt = location.MapName?.ToLowerInvariant() ?? "";

        return locationMapName == mapKey || locationMapNameAlt == mapKey;
    }

    /// <summary>
    /// 퀘스트 목표 영역 렌더링 (Multi-point 지원)
    /// </summary>
    private void RenderQuestObjectiveArea(
        TaskObjectiveWithLocation objective,
        List<QuestObjectiveLocation> locations,
        Color objectiveColor,
        double inverseScale,
        bool hasFloors,
        double opacity = 1.0)
    {
        // API에서는 층 정보를 제공하지 않으므로 모든 포인트를 사용
        var points = locations;

        // 마커 리스트 초기화
        if (!_markersByObjectiveId.ContainsKey(objective.ObjectiveId))
            _markersByObjectiveId[objective.ObjectiveId] = new List<FrameworkElement>();

        // 1. 3개 이상: Polygon (채워진 영역)
        if (points.Count >= 3)
        {
            var polygon = new Polygon
            {
                Fill = new SolidColorBrush(Color.FromArgb((byte)(60 * opacity), objectiveColor.R, objectiveColor.G, objectiveColor.B)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), objectiveColor.R, objectiveColor.G, objectiveColor.B)),
                StrokeThickness = 2 * inverseScale,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Tag = objective,
                Cursor = Cursors.Hand,
                ToolTip = CreateEnhancedTooltip(objective),
                ContextMenu = CreateMarkerContextMenu(objective)
            };
            polygon.MouseLeftButtonDown += QuestMarker_Click;
            polygon.MouseEnter += QuestMarker_MouseEnter;
            polygon.MouseLeave += QuestMarker_MouseLeave;
            polygon.MouseRightButtonDown += QuestMarker_RightClick;

            foreach (var point in points)
            {
                var screenCoords = _currentMapConfig!.GameToScreen(point.X, point.Z ?? 0);
                if (screenCoords == null) continue;
                polygon.Points.Add(new Point(screenCoords.Value.screenX, screenCoords.Value.screenY));
            }

            if (polygon.Points.Count >= 3)
            {
                QuestMarkersCanvas.Children.Add(polygon);
                _markersByObjectiveId[objective.ObjectiveId].Add(polygon);

                // Centroid에 라벨 추가
                AddAreaLabel(objective, points, objectiveColor, inverseScale, opacity);

                // 완료된 경우 체크마크 오버레이 추가
                if (objective.IsCompleted)
                {
                    var centroid = GetCentroid(points);
                    if (centroid != null)
                        AddCompletionCheckmark(centroid.Value.screenX, centroid.Value.screenY, inverseScale);
                }
            }
        }
        // 2. 2개: Line
        else if (points.Count == 2)
        {
            var p1 = _currentMapConfig!.GameToScreen(points[0].X, points[0].Z ?? 0);
            var p2 = _currentMapConfig.GameToScreen(points[1].X, points[1].Z ?? 0);

            if (p1 != null && p2 != null)
            {
                var line = new Line
                {
                    X1 = p1.Value.screenX, Y1 = p1.Value.screenY,
                    X2 = p2.Value.screenX, Y2 = p2.Value.screenY,
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), objectiveColor.R, objectiveColor.G, objectiveColor.B)),
                    StrokeThickness = 3 * inverseScale,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    Tag = objective,
                    Cursor = Cursors.Hand,
                    ToolTip = CreateEnhancedTooltip(objective),
                    ContextMenu = CreateMarkerContextMenu(objective)
                };
                line.MouseLeftButtonDown += QuestMarker_Click;
                line.MouseEnter += QuestMarker_MouseEnter;
                line.MouseLeave += QuestMarker_MouseLeave;
                line.MouseRightButtonDown += QuestMarker_RightClick;

                QuestMarkersCanvas.Children.Add(line);
                _markersByObjectiveId[objective.ObjectiveId].Add(line);

                // 중간점에 라벨 추가
                var midX = (p1.Value.screenX + p2.Value.screenX) / 2;
                var midY = (p1.Value.screenY + p2.Value.screenY) / 2;
                AddQuestLabel(objective, midX, midY, objectiveColor, inverseScale, opacity);

                // 완료된 경우 체크마크 오버레이 추가
                if (objective.IsCompleted)
                {
                    AddCompletionCheckmark(midX, midY, inverseScale);
                }
            }
        }
        // 3. 1개: Diamond Marker
        else if (points.Count == 1)
        {
            var screenCoords = _currentMapConfig!.GameToScreen(points[0].X, points[0].Z ?? 0);
            if (screenCoords != null)
            {
                var marker = CreateDiamondMarker(screenCoords.Value.screenX, screenCoords.Value.screenY, objectiveColor, inverseScale, opacity, objective);
                QuestMarkersCanvas.Children.Add(marker);
                AddQuestLabel(objective, screenCoords.Value.screenX, screenCoords.Value.screenY, objectiveColor, inverseScale, opacity);

                // 완료된 경우 체크마크 오버레이 추가
                if (objective.IsCompleted)
                {
                    AddCompletionCheckmark(screenCoords.Value.screenX, screenCoords.Value.screenY, inverseScale);
                }
            }
        }
    }

    /// <summary>
    /// 포인트 목록의 중심 좌표 계산
    /// </summary>
    private (double screenX, double screenY)? GetCentroid(List<QuestObjectiveLocation> points)
    {
        if (points.Count == 0 || _currentMapConfig == null) return null;

        var avgX = points.Average(p => p.X);
        var avgZ = points.Average(p => p.Z ?? 0);

        return _currentMapConfig.GameToScreen(avgX, avgZ);
    }

    /// <summary>
    /// 완료 체크마크 오버레이 추가 - 앱 테마 적용
    /// </summary>
    private void AddCompletionCheckmark(double screenX, double screenY, double inverseScale)
    {
        var size = 20 * inverseScale;

        // 체크마크 배경 원
        var background = new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(ThemeSuccessColor),
            Stroke = new SolidColorBrush(ThemeBackgroundDark),
            StrokeThickness = 1.5 * inverseScale
        };

        // 드롭 섀도우
        background.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 4 * inverseScale,
            ShadowDepth = 1 * inverseScale,
            Opacity = 0.4
        };

        Canvas.SetLeft(background, screenX - size / 2);
        Canvas.SetTop(background, screenY - size / 2 - 18 * inverseScale); // 마커 위에 표시
        QuestMarkersCanvas.Children.Add(background);

        // 체크마크 텍스트
        var checkmark = new TextBlock
        {
            Text = "✓",
            Foreground = new SolidColorBrush(ThemeTextPrimary),
            FontSize = 12 * inverseScale,
            FontWeight = FontWeights.Bold
        };
        checkmark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(checkmark, screenX - checkmark.DesiredSize.Width / 2);
        Canvas.SetTop(checkmark, screenY - checkmark.DesiredSize.Height / 2 - 18 * inverseScale);
        QuestMarkersCanvas.Children.Add(checkmark);
    }

    /// <summary>
    /// 마름모 마커 생성 (단일 포인트용) - 개선된 스타일
    /// </summary>
    private Canvas CreateDiamondMarker(double screenX, double screenY, Color color, double inverseScale, double opacity, TaskObjectiveWithLocation? objective = null)
    {
        var size = 18 * inverseScale * _markerScale;
        var canvas = new Canvas { Width = 0, Height = 0 };

        // 글로우 효과 (배경 마름모)
        var glow = new Polygon
        {
            Points = new PointCollection
            {
                new Point(0, -size - 4 * inverseScale),
                new Point(size + 4 * inverseScale, 0),
                new Point(0, size + 4 * inverseScale),
                new Point(-size - 4 * inverseScale, 0)
            },
            Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 80), color.R, color.G, color.B)),
            Stroke = Brushes.Transparent
        };
        canvas.Children.Add(glow);

        // 메인 마름모
        var diamond = new Polygon
        {
            Points = new PointCollection
            {
                new Point(0, -size),
                new Point(size, 0),
                new Point(0, size),
                new Point(-size, 0)
            },
            Fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)),
            Stroke = new SolidColorBrush(Colors.White),
            StrokeThickness = 2.5 * inverseScale
        };

        // 드롭 섀도우 효과
        diamond.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 6 * inverseScale,
            ShadowDepth = 2 * inverseScale,
            Opacity = 0.6
        };

        canvas.Children.Add(diamond);
        Canvas.SetLeft(canvas, screenX);
        Canvas.SetTop(canvas, screenY);
        canvas.Opacity = opacity;

        // 상호작용 추가
        if (objective != null)
        {
            canvas.Tag = objective;
            canvas.Cursor = Cursors.Hand;
            canvas.ToolTip = CreateEnhancedTooltip(objective);
            canvas.ContextMenu = CreateMarkerContextMenu(objective);
            canvas.MouseLeftButtonDown += QuestMarker_Click;
            canvas.MouseEnter += QuestMarker_MouseEnter;
            canvas.MouseLeave += QuestMarker_MouseLeave;
            canvas.MouseRightButtonDown += QuestMarker_RightClick;

            // 마커 매핑에 추가
            if (!_markersByObjectiveId.ContainsKey(objective.ObjectiveId))
                _markersByObjectiveId[objective.ObjectiveId] = new List<FrameworkElement>();
            _markersByObjectiveId[objective.ObjectiveId].Add(canvas);
        }

        return canvas;
    }

    /// <summary>
    /// 영역 라벨 추가 (Centroid 위치)
    /// </summary>
    private void AddAreaLabel(TaskObjectiveWithLocation objective, List<QuestObjectiveLocation> points, Color color, double inverseScale, double opacity = 1.0)
    {
        // Centroid 계산 (tarkov.dev API: X=horizontal X, Z=horizontal depth)
        var avgX = points.Average(p => p.X);
        var avgZ = points.Average(p => p.Z ?? 0);

        var centroid = _currentMapConfig!.GameToScreen(avgX, avgZ);
        if (centroid == null) return;

        AddQuestLabel(objective, centroid.Value.screenX, centroid.Value.screenY, color, inverseScale, opacity);
    }

    /// <summary>
    /// 퀘스트 라벨 추가 - 개선된 스타일 (배경 + 그림자)
    /// </summary>
    private void AddQuestLabel(TaskObjectiveWithLocation objective, double screenX, double screenY, Color color, double inverseScale, double opacity)
    {
        var displayName = !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;

        // 완료 표시
        var statusIcon = objective.IsCompleted ? "✓ " : "";

        // 라벨 컨테이너 (배경 + 텍스트)
        var container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 35)),
            BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B)),
            BorderThickness = new Thickness(2 * inverseScale),
            CornerRadius = new CornerRadius(4 * inverseScale),
            Padding = new Thickness(8 * inverseScale, 4 * inverseScale, 8 * inverseScale, 4 * inverseScale),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 8 * inverseScale,
                ShadowDepth = 2 * inverseScale,
                Opacity = 0.7
            }
        };

        var textPanel = new StackPanel { Orientation = Orientation.Horizontal };

        // 완료 체크마크
        if (objective.IsCompleted)
        {
            textPanel.Children.Add(new TextBlock
            {
                Text = "✓ ",
                Foreground = new SolidColorBrush(Colors.LimeGreen),
                FontSize = 13 * inverseScale,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // 퀘스트 이름
        textPanel.Children.Add(new TextBlock
        {
            Text = displayName,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 13 * inverseScale,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        container.Child = textPanel;

        // 위치 설정 (마커 오른쪽에 배치)
        Canvas.SetLeft(container, screenX + 24 * inverseScale);
        Canvas.SetTop(container, screenY - 14 * inverseScale);
        container.Opacity = opacity;

        QuestMarkersCanvas.Children.Add(container);
    }

    /// <summary>
    /// 퀘스트 목표 타입별 색상
    /// </summary>
    private static Color GetQuestTypeColor(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "visit" => Color.FromRgb(33, 150, 243),       // 파랑 #2196F3
            "mark" => Color.FromRgb(76, 175, 80),         // 초록 #4CAF50
            "plantitem" => Color.FromRgb(255, 152, 0),    // 주황 #FF9800
            "extract" => Color.FromRgb(244, 67, 54),      // 빨강 #F44336
            "finditem" or "findquestitem" or "giveitem" => Color.FromRgb(255, 235, 59), // 노랑 #FFEB3B
            "kill" or "shoot" => Color.FromRgb(156, 39, 176), // 보라 #9C27B0
            _ => Color.FromRgb(255, 193, 7)               // 기본: 금색 #FFC107
        };
    }

    /// <summary>
    /// 퀘스트 마커 툴팁 생성
    /// </summary>
    private object CreateQuestTooltip(TaskObjectiveWithLocation objective)
    {
        var questName = !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;

        var description = !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;

        var typeDisplay = GetQuestTypeDisplayName(objective.Type);
        var statusText = objective.IsCompleted ? " ✓ 완료" : "";

        var panel = new StackPanel { MaxWidth = 300 };

        // 퀘스트 이름
        panel.Children.Add(new TextBlock
        {
            Text = questName + statusText,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(GetQuestTypeColor(objective.Type)),
            TextWrapping = TextWrapping.Wrap
        });

        // 목표 타입
        panel.Children.Add(new TextBlock
        {
            Text = $"[{typeDisplay}]",
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 4)
        });

        // 목표 설명
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White
        });

        return panel;
    }

    // 앱 테마 색상 (App.xaml과 동일)
    private static readonly Color ThemeBackgroundDark = Color.FromRgb(0x1a, 0x1a, 0x1a);
    private static readonly Color ThemeBackgroundMedium = Color.FromRgb(0x25, 0x25, 0x25);
    private static readonly Color ThemeBackgroundLight = Color.FromRgb(0x2d, 0x2d, 0x2d);
    private static readonly Color ThemeBorderColor = Color.FromRgb(0x3d, 0x3d, 0x3d);
    private static readonly Color ThemeTextPrimary = Color.FromRgb(0xe0, 0xe0, 0xe0);
    private static readonly Color ThemeTextSecondary = Color.FromRgb(0x9e, 0x9e, 0x9e);
    private static readonly Color ThemeAccentColor = Color.FromRgb(0xc5, 0xa8, 0x4a);
    private static readonly Color ThemeSuccessColor = Color.FromRgb(0x4c, 0xaf, 0x50);

    /// <summary>
    /// 개선된 퀘스트 마커 툴팁 생성 (진행률, 좌표, 위치 수 포함) - 앱 테마 적용
    /// </summary>
    private object CreateEnhancedTooltip(TaskObjectiveWithLocation objective)
    {
        var questName = !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;

        var description = !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;

        var typeDisplay = GetQuestTypeDisplayName(objective.Type);
        var typeColor = GetQuestTypeColor(objective.Type);
        var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);

        var border = new Border
        {
            Background = new SolidColorBrush(ThemeBackgroundMedium),
            BorderBrush = new SolidColorBrush(ThemeBorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            MaxWidth = 300
        };

        var panel = new StackPanel();

        // 헤더 (퀘스트 이름 + 상태 아이콘)
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        if (isCompleted)
        {
            headerPanel.Children.Add(new TextBlock
            {
                Text = "✓ ",
                Foreground = new SolidColorBrush(ThemeSuccessColor),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        headerPanel.Children.Add(new TextBlock
        {
            Text = questName,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = new SolidColorBrush(ThemeTextPrimary),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(headerPanel);

        // 목표 타입 뱃지
        var typeBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(40, typeColor.R, typeColor.G, typeColor.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, typeColor.R, typeColor.G, typeColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 2, 5, 2),
            Margin = new Thickness(0, 6, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        typeBadge.Child = new TextBlock
        {
            Text = typeDisplay,
            Foreground = new SolidColorBrush(typeColor),
            FontSize = 11
        };
        panel.Children.Add(typeBadge);

        // 목표 설명
        panel.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeTextPrimary),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6)
        });

        // 위치 수 정보
        var locationCount = objective.Locations.Count;
        if (locationCount > 1)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"📍 {locationCount}개 위치",
                Foreground = new SolidColorBrush(ThemeTextSecondary),
                FontSize = 11
            });
        }

        // 힌트
        var hintText = new TextBlock
        {
            Text = "클릭: 이동 | 우클릭: 메뉴",
            Foreground = new SolidColorBrush(ThemeTextSecondary),
            FontSize = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };
        panel.Children.Add(hintText);

        border.Child = panel;
        return border;
    }

    /// <summary>
    /// 마커 우클릭 컨텍스트 메뉴 생성 - 앱 테마 자동 적용 (App.xaml MenuItem 스타일)
    /// </summary>
    private ContextMenu CreateMarkerContextMenu(TaskObjectiveWithLocation objective)
    {
        var menu = new ContextMenu
        {
            Background = new SolidColorBrush(ThemeBackgroundMedium),
            BorderBrush = new SolidColorBrush(ThemeBorderColor),
            BorderThickness = new Thickness(1)
        };
        var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);

        // 완료/미완료 토글
        var completeMenuItem = new MenuItem
        {
            Header = isCompleted ? "미완료로 표시" : "완료로 표시",
            Tag = objective
        };
        completeMenuItem.Click += (s, e) =>
        {
            var obj = (s as MenuItem)?.Tag as TaskObjectiveWithLocation;
            if (obj != null)
            {
                var currentState = _progressService.IsObjectiveCompletedById(obj.ObjectiveId);
                _progressService.SetObjectiveCompletedById(obj.ObjectiveId, !currentState, obj.TaskNormalizedName);
                RefreshQuestMarkers();
                RefreshQuestDrawer();
            }
        };
        menu.Items.Add(completeMenuItem);

        menu.Items.Add(new Separator());

        // Drawer에서 보기
        var viewInDrawerMenuItem = new MenuItem
        {
            Header = "Drawer에서 보기",
            Tag = objective
        };
        viewInDrawerMenuItem.Click += (s, e) =>
        {
            var obj = (s as MenuItem)?.Tag as TaskObjectiveWithLocation;
            if (obj != null)
            {
                if (!_isDrawerOpen) OpenDrawer();
                ScrollToQuestInDrawer(obj.TaskNormalizedName);
            }
        };
        menu.Items.Add(viewInDrawerMenuItem);

        // 이 퀘스트 숨기기
        var hideQuestMenuItem = new MenuItem
        {
            Header = "이 퀘스트 숨기기",
            Tag = objective
        };
        hideQuestMenuItem.Click += (s, e) =>
        {
            var obj = (s as MenuItem)?.Tag as TaskObjectiveWithLocation;
            if (obj != null)
            {
                _hiddenQuestIds.Add(obj.TaskNormalizedName);
                _settings.MapHiddenQuests = _hiddenQuestIds; // Save to settings
                RefreshQuestMarkers();
                RefreshQuestDrawer();
            }
        };
        menu.Items.Add(hideQuestMenuItem);

        menu.Items.Add(new Separator());

        // 좌표 복사
        var copyCoordMenuItem = new MenuItem
        {
            Header = "좌표 복사",
            Tag = objective
        };
        copyCoordMenuItem.Click += (s, e) =>
        {
            var obj = (s as MenuItem)?.Tag as TaskObjectiveWithLocation;
            if (obj != null && obj.Locations.Count > 0)
            {
                var loc = obj.Locations[0];
                var coordText = $"X: {loc.X:F1}, Z: {loc.Z:F1}";
                System.Windows.Clipboard.SetText(coordText);
                StatusText.Text = $"좌표 복사됨: {coordText}";
            }
        };
        menu.Items.Add(copyCoordMenuItem);

        return menu;
    }

    /// <summary>
    /// 마커 마우스 진입 - Drawer 항목 강조
    /// </summary>
    private void QuestMarker_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            _hoveredObjectiveId = objective.ObjectiveId;

            // 마커 강조 효과
            HighlightMarker(element, true);

            // Drawer가 열려있으면 해당 퀘스트 강조
            if (_isDrawerOpen)
            {
                _highlightedQuestId = objective.TaskNormalizedName;
                RefreshQuestDrawer();
            }
        }
    }

    /// <summary>
    /// 마커 마우스 이탈 - Drawer 강조 해제
    /// </summary>
    private void QuestMarker_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            _hoveredObjectiveId = null;

            // 마커 강조 해제
            HighlightMarker(element, false);

            // Drawer 강조 해제
            if (_isDrawerOpen && _highlightedQuestId == objective.TaskNormalizedName)
            {
                _highlightedQuestId = null;
                RefreshQuestDrawer();
            }
        }
    }

    /// <summary>
    /// 마커 우클릭 핸들러
    /// </summary>
    private void QuestMarker_RightClick(object sender, MouseButtonEventArgs e)
    {
        // ContextMenu가 자동으로 표시됨
        e.Handled = true;
    }

    /// <summary>
    /// 마커 강조 효과 적용/해제 - 앱 테마 Accent 색상 사용
    /// </summary>
    private void HighlightMarker(FrameworkElement element, bool highlight)
    {
        if (element is Polygon polygon)
        {
            if (highlight)
            {
                polygon.StrokeThickness *= 1.5;
                polygon.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ThemeAccentColor,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            else
            {
                polygon.StrokeThickness /= 1.5;
                polygon.Effect = null;
            }
        }
        else if (element is Line line)
        {
            if (highlight)
            {
                line.StrokeThickness *= 1.5;
                line.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ThemeAccentColor,
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            else
            {
                line.StrokeThickness /= 1.5;
                line.Effect = null;
            }
        }
        else if (element is Canvas canvas)
        {
            if (highlight)
            {
                canvas.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ThemeAccentColor,
                    BlurRadius = 15,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
            else
            {
                canvas.Effect = null;
            }
        }
    }

    /// <summary>
    /// Drawer 아이템 호버 시작 - 해당 마커 펄스 애니메이션
    /// </summary>
    private void QuestDrawerItem_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerItem item)
        {
            var objectiveId = item.Objective.ObjectiveId;

            // 해당 목표의 마커들 찾아서 펄스 효과 시작
            if (_markersByObjectiveId.TryGetValue(objectiveId, out var markers))
            {
                foreach (var marker in markers)
                {
                    StartPulseAnimation(marker);
                }
            }
        }
    }

    /// <summary>
    /// Drawer 아이템 호버 종료 - 펄스 애니메이션 중지
    /// </summary>
    private void QuestDrawerItem_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is QuestDrawerItem item)
        {
            var objectiveId = item.Objective.ObjectiveId;

            // 해당 목표의 마커들 펄스 효과 중지
            if (_markersByObjectiveId.TryGetValue(objectiveId, out var markers))
            {
                foreach (var marker in markers)
                {
                    StopPulseAnimation(marker);
                }
            }
        }
    }

    /// <summary>
    /// 마커 펄스 애니메이션 시작
    /// </summary>
    private void StartPulseAnimation(FrameworkElement element)
    {
        // 기존 애니메이션 중지
        element.BeginAnimation(UIElement.OpacityProperty, null);

        // 펄스 애니메이션 생성
        var pulseAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1.0,
            To = 0.4,
            Duration = TimeSpan.FromMilliseconds(400),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            EasingFunction = new System.Windows.Media.Animation.SineEase()
        };

        // 마커 강조 효과 추가
        HighlightMarker(element, true);

        // 애니메이션 시작
        element.BeginAnimation(UIElement.OpacityProperty, pulseAnimation);
    }

    /// <summary>
    /// 마커 펄스 애니메이션 중지
    /// </summary>
    private void StopPulseAnimation(FrameworkElement element)
    {
        // 애니메이션 중지
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1.0;

        // 마커 강조 효과 해제
        HighlightMarker(element, false);
    }

    /// <summary>
    /// 퀘스트 목표 타입 표시 이름
    /// </summary>
    private static string GetQuestTypeDisplayName(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "visit" => "방문",
            "mark" => "마킹",
            "plantitem" => "아이템 설치",
            "extract" => "탈출",
            "finditem" => "아이템 찾기",
            "findquestitem" => "퀘스트 아이템 찾기",
            "giveitem" => "아이템 전달",
            "kill" or "shoot" => "처치",
            _ => type ?? "기타"
        };
    }

    /// <summary>
    /// 퀘스트 마커 클릭 이벤트 - Drawer 열고 해당 퀘스트로 스크롤
    /// </summary>
    private void QuestMarker_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            // Drawer 열기
            if (!_isDrawerOpen)
            {
                OpenDrawer();
            }

            // 해당 퀘스트 하이라이트 및 스크롤
            ScrollToQuestInDrawer(objective.TaskNormalizedName);

            e.Handled = true;
        }
    }

    /// <summary>
    /// Drawer에서 특정 퀘스트로 스크롤
    /// </summary>
    private void ScrollToQuestInDrawer(string questId)
    {
        _highlightedQuestId = questId;

        // ItemsSource에서 해당 그룹 찾기
        if (QuestObjectivesList.ItemsSource is List<QuestDrawerGroup> groups)
        {
            var targetGroup = groups.FirstOrDefault(g => g.QuestId == questId);
            if (targetGroup != null)
            {
                // 해당 아이템으로 스크롤
                var index = groups.IndexOf(targetGroup);
                if (index >= 0)
                {
                    // ItemsControl의 컨테이너 가져오기
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var container = QuestObjectivesList.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
                        container?.BringIntoView();

                        // 하이라이트 효과 (2초 후 해제)
                        RefreshQuestDrawer();
                        var timer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(2)
                        };
                        timer.Tick += (s, e) =>
                        {
                            _highlightedQuestId = null;
                            RefreshQuestDrawer();
                            timer.Stop();
                        };
                        timer.Start();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }
    }

    #endregion
}
