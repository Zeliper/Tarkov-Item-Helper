using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Pages.Map.ViewModels;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;

namespace TarkovHelper.Pages.Map.Components;

/// <summary>
/// 맵 페이지의 퀘스트 마커 관리를 담당하는 클래스.
/// 마커 생성, 그룹화, 이벤트 처리 등의 로직을 캡슐화합니다.
/// </summary>
public class MapQuestMarkerManager
{
    // 의존성 서비스
    private readonly Canvas _markersContainer;
    private readonly MapTrackerService _trackerService;
    private readonly QuestObjectiveService _objectiveService;
    private readonly QuestProgressService _progressService;
    private readonly LocalizationService _loc;

    // 마커 상태
    private readonly List<FrameworkElement> _questMarkerElements = new();
    private readonly List<FrameworkElement> _groupedMarkerElements = new();
    private List<TaskObjectiveWithLocation> _currentMapObjectives = new();

    // 설정 및 상태
    private string? _currentMapKey;
    private string? _currentFloorId;
    private double _zoomLevel = 1.0;
    private bool _showQuestMarkers = true;
    private QuestMarkerStyle _questMarkerStyle = QuestMarkerStyle.DefaultWithName;
    private double _questNameTextSize = 12.0;
    private bool _hideCompletedObjectives = true;

    // 선택 상태
    private TaskObjectiveWithLocation? _selectedObjective;
    private FrameworkElement? _selectedMarkerElement;

    // 팝업
    private Popup? _markerGroupPopup;

    // 디바운싱 타이머 (줌 변경 시 그룹화 재계산용)
    private System.Windows.Threading.DispatcherTimer? _regroupDebounceTimer;

    // 이벤트
    public event EventHandler<TaskObjectiveWithLocation>? ObjectiveSelected;
    public event EventHandler<TaskObjectiveWithLocation>? FloorChangeRequested;
    public event Action<string>? StatusUpdated;

    public MapQuestMarkerManager(
        Canvas markersContainer,
        MapTrackerService trackerService,
        QuestObjectiveService objectiveService,
        QuestProgressService progressService,
        LocalizationService localizationService)
    {
        _markersContainer = markersContainer ?? throw new ArgumentNullException(nameof(markersContainer));
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _objectiveService = objectiveService ?? throw new ArgumentNullException(nameof(objectiveService));
        _progressService = progressService ?? throw new ArgumentNullException(nameof(progressService));
        _loc = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    #region Public Methods - Configuration

    public void SetCurrentMap(string? mapKey)
    {
        _currentMapKey = mapKey;
    }

    public void SetCurrentFloor(string? floorId)
    {
        _currentFloorId = floorId;
    }

    public void SetZoomLevel(double zoomLevel)
    {
        _zoomLevel = zoomLevel;
    }

    public void SetShowQuestMarkers(bool show)
    {
        _showQuestMarkers = show;
    }

    public void SetQuestMarkerStyle(QuestMarkerStyle style)
    {
        _questMarkerStyle = style;
    }

    public void SetQuestNameTextSize(double size)
    {
        _questNameTextSize = size;
    }

    public void SetHideCompletedObjectives(bool hide)
    {
        _hideCompletedObjectives = hide;
    }

    public void SetSelectedObjective(TaskObjectiveWithLocation? objective)
    {
        _selectedObjective = objective;
        UpdateMarkerHighlight();
    }

    #endregion

    #region Public Methods - Marker Management

    public void RefreshMarkers()
    {
        if (string.IsNullOrEmpty(_currentMapKey)) return;
        if (!_objectiveService.IsLoaded) return;

        // 기존 마커 제거
        ClearMarkers();

        if (!_showQuestMarkers) return;

        // 맵 설정 가져오기
        var config = _trackerService.GetMapConfig(_currentMapKey);
        if (config == null) return;

        // 현재 맵의 활성 퀘스트 목표 가져오기 (별칭 포함하여 검색)
        var mapNamesToSearch = new List<string> { _currentMapKey };
        if (config.Aliases != null)
        {
            mapNamesToSearch.AddRange(config.Aliases);
        }
        // 표시 이름도 추가
        if (!string.IsNullOrEmpty(config.DisplayName))
        {
            mapNamesToSearch.Add(config.DisplayName);
        }

        _currentMapObjectives = new List<TaskObjectiveWithLocation>();
        foreach (var mapName in mapNamesToSearch)
        {
            var objectives = _objectiveService.GetActiveObjectivesForMap(mapName, _progressService);
            foreach (var obj in objectives)
            {
                if (!_currentMapObjectives.Any(o => o.ObjectiveId == obj.ObjectiveId))
                {
                    _currentMapObjectives.Add(obj);
                }
            }
        }

        StatusUpdated?.Invoke($"Found {_currentMapObjectives.Count} active objectives for {_currentMapKey}");

        foreach (var objective in _currentMapObjectives)
        {
            // ObjectiveId 기반으로 완료 상태 확인 (동일 설명 목표 개별 추적)
            var isCompleted = _progressService.IsObjectiveCompletedById(objective.ObjectiveId);
            objective.IsCompleted = isCompleted;

            // 완료된 목표 숨기기 설정이 활성화되어 있으면 스킵
            if (_hideCompletedObjectives && isCompleted)
                continue;

            // 현재 맵에 해당하는 위치만 필터링
            var locationsOnCurrentMap = objective.Locations
                .Where(loc => IsLocationOnCurrentMap(loc, config))
                .ToList();

            foreach (var location in locationsOnCurrentMap)
            {
                // 층 정보 확인: 현재 선택된 층과 목표 위치의 층 비교
                var isOnCurrentFloor = IsMarkerOnCurrentFloor(location.FloorId);

                // Area(Polygon) 표시: Outline이 있으면 다각형으로 그리기
                if (location.Outline != null && location.Outline.Count >= 3)
                {
                    var areaMarker = CreateAreaMarker(objective, location, config, isOnCurrentFloor);
                    _questMarkerElements.Add(areaMarker);
                    _markersContainer.Children.Add(areaMarker);
                }
                else
                {
                    // 단일 포인트 마커
                    // TarkovDBEditor 방식: config.GameToScreen 직접 사용
                    // QuestObjectiveLocation: X = game X, Y = game Z (수평면), Z = game Y (높이)
                    var (screenX, screenY) = config.GameToScreen(location.X, location.Y);
                    var screenPos = new ScreenPosition
                    {
                        MapKey = _currentMapKey!,
                        X = screenX,
                        Y = screenY
                    };

                    // OR Pointer 여부 확인 (location.Id에 _opt_ 포함)
                    var isOrPointer = location.Id.Contains("_opt_");

                    var marker = isOrPointer
                        ? CreateOrPointerMarker(objective, location, screenPos, isOnCurrentFloor)
                        : CreateQuestMarker(objective, location, screenPos, isOnCurrentFloor);
                    _questMarkerElements.Add(marker);
                    _markersContainer.Children.Add(marker);
                }
            }
        }

        // 이름을 표시하는 스타일일 때만 겹침 감지 수행
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        if (showName)
        {
            // 레이아웃 완료 후 그룹화 수행 (Measure가 정확한 크기를 반환하도록)
            Application.Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, DetectAndGroupOverlappingMarkers);
        }
    }

    public void ClearMarkers()
    {
        // 그룹 마커 먼저 제거
        ClearGroupedMarkers();

        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas c)
            {
                c.MouseLeftButtonDown -= QuestMarker_Click;
                // 텍스트 Border의 클릭 이벤트도 해제
                foreach (var child in c.Children)
                {
                    if (child is Border border)
                    {
                        border.MouseLeftButtonDown -= QuestMarkerText_Click;
                    }
                }
            }
        }
        _questMarkerElements.Clear();
        _markersContainer.Children.Clear();
    }

    public void UpdateMarkerScales()
    {
        var inverseScale = 1.0 / _zoomLevel;

        // 퀘스트 마커 업데이트
        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
            }
        }

        // 그룹 마커 업데이트
        foreach (var marker in _groupedMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
            }
        }

        // 이름을 표시하는 스타일일 때 줌 변경에 따라 겹침 감지 재수행
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        if (showName && _questMarkerElements.Count > 0)
        {
            // 디바운싱: 연속된 줌 변경 시 마지막 변경 후 50ms 후에만 그룹화 수행
            _regroupDebounceTimer?.Stop();
            _regroupDebounceTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _regroupDebounceTimer.Tick += (s, e) =>
            {
                _regroupDebounceTimer?.Stop();
                _regroupDebounceTimer = null;
                DetectAndGroupOverlappingMarkers();
            };
            _regroupDebounceTimer.Start();
        }
    }

    #endregion

    #region Private Methods - Marker Creation

    private FrameworkElement CreateQuestMarker(TaskObjectiveWithLocation objective, QuestObjectiveLocation location, ScreenPosition screenPos, bool isOnCurrentFloor = true)
    {
        // 설정에서 커스텀 색상 가져오기
        var colorHex = _trackerService.Settings.GetMarkerColor(objective.Type) ?? objective.MarkerColor;
        var markerColor = (Color)ColorConverter.ConvertFromString(colorHex);

        // 다른 층의 마커는 색상을 흐리게 처리
        if (!isOnCurrentFloor)
        {
            markerColor = Color.FromArgb(100, markerColor.R, markerColor.G, markerColor.B);
        }

        var markerBrush = new SolidColorBrush(markerColor);
        var glowBrush = new SolidColorBrush(Color.FromArgb((byte)(isOnCurrentFloor ? 64 : 30), markerColor.R, markerColor.G, markerColor.B));

        // 초록색 원 스타일용 색상
        var greenColor = (Color)ColorConverter.ConvertFromString("#4CAF50");
        if (!isOnCurrentFloor)
        {
            greenColor = Color.FromArgb(100, greenColor.R, greenColor.G, greenColor.B);
        }
        var greenBrush = new SolidColorBrush(greenColor);
        var greenGlowBrush = new SolidColorBrush(Color.FromArgb((byte)(isOnCurrentFloor ? 64 : 30), greenColor.R, greenColor.G, greenColor.B));

        // 설정에서 마커 크기 가져오기
        var baseMarkerSize = _trackerService.Settings.MarkerSize;

        // 맵별 마커 스케일 적용
        var mapConfig = _trackerService.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;

        var markerSize = baseMarkerSize * mapScale;
        var glowSize = markerSize * 1.75;
        var centerSize = markerSize * 0.875;

        var canvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = objective
        };

        // 스타일에 따라 다르게 렌더링
        var useGreenCircle = _questMarkerStyle == QuestMarkerStyle.GreenCircle ||
                             _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;

        if (useGreenCircle)
        {
            // 초록색 원 (테두리만)
            var circleOuter = new Ellipse
            {
                Width = markerSize,
                Height = markerSize,
                Stroke = greenBrush,
                StrokeThickness = 3,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(circleOuter, -markerSize / 2);
            Canvas.SetTop(circleOuter, -markerSize / 2);
            canvas.Children.Add(circleOuter);
        }
        else
        {
            // 기본 스타일: 외곽 글로우 + 중심 원
            var glow = new Ellipse
            {
                Width = glowSize,
                Height = glowSize,
                Fill = glowBrush
            };
            Canvas.SetLeft(glow, -glowSize / 2);
            Canvas.SetTop(glow, -glowSize / 2);
            canvas.Children.Add(glow);

            var center = new Ellipse
            {
                Width = centerSize,
                Height = centerSize,
                Fill = markerBrush,
                Stroke = Brushes.White,
                StrokeThickness = 2
            };
            Canvas.SetLeft(center, -centerSize / 2);
            Canvas.SetTop(center, -centerSize / 2);
            canvas.Children.Add(center);
        }

        // 완료 상태 표시 (큰 체크마크 + 취소선)
        if (objective.IsCompleted)
        {
            // 완료 배경 오버레이 (반투명 회색)
            var completedOverlay = new Ellipse
            {
                Width = useGreenCircle ? markerSize : glowSize,
                Height = useGreenCircle ? markerSize : glowSize,
                Fill = new SolidColorBrush(Color.FromArgb(180, 50, 50, 50))
            };
            var overlaySize = useGreenCircle ? markerSize : glowSize;
            Canvas.SetLeft(completedOverlay, -overlaySize / 2);
            Canvas.SetTop(completedOverlay, -overlaySize / 2);
            canvas.Children.Add(completedOverlay);

            // 큰 체크마크
            var checkMarkSize = markerSize * 0.8;
            var checkMark = new TextBlock
            {
                Text = "✓",
                FontSize = checkMarkSize,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)) // 밝은 초록
            };
            // 체크마크 중앙 정렬
            checkMark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(checkMark, -checkMark.DesiredSize.Width / 2);
            Canvas.SetTop(checkMark, -checkMark.DesiredSize.Height / 2);
            canvas.Children.Add(checkMark);

            // 완료된 마커는 약간 반투명
            canvas.Opacity = 0.7;
        }

        // 퀘스트명 표시
        Border? floorBadgeBorder = null;  // 층 배지를 별도로 관리
        if (showName)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            // 층 정보 가져오기
            var floorInfo = GetFloorIndicator(location.FloorId);

            // 퀘스트명과 층 배지를 함께 담는 StackPanel (수직 중앙 정렬을 위해)
            var contentPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 퀘스트명 텍스트
            var nameText = new TextBlock
            {
                Text = questName,
                FontSize = _questNameTextSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var nameBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Child = nameText,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 텍스트 클릭 이벤트 추가
            nameBorder.Tag = objective;
            nameBorder.MouseLeftButtonDown += QuestMarkerText_Click;
            nameBorder.Cursor = Cursors.Hand;

            contentPanel.Children.Add(nameBorder);

            // 다른 층일 때 층 배지 추가
            if (floorInfo.HasValue)
            {
                var (arrow, floorText, indicatorColor) = floorInfo.Value;

                // 층 배지 (화살표 + 층 표시) - 불투명하게 유지하기 위해 별도 Border
                var floorBadgeText = new TextBlock
                {
                    Text = $"{arrow}{floorText}",
                    FontSize = _questNameTextSize * 0.85,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center
                };

                floorBadgeBorder = new Border
                {
                    Background = new SolidColorBrush(indicatorColor),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(3, 1, 3, 1),
                    Margin = new Thickness(4, 0, 0, 0),  // 왼쪽 여백으로 간격 확보
                    Child = floorBadgeText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = "FloorBadge"  // 식별용 태그
                };

                contentPanel.Children.Add(floorBadgeBorder);
            }

            // 전체 컨테이너 크기를 측정하여 중앙 정렬
            contentPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var totalWidth = contentPanel.DesiredSize.Width;

            // 컨테이너를 마커 중앙 아래에 위치
            Canvas.SetLeft(contentPanel, -totalWidth / 2);
            Canvas.SetTop(contentPanel, markerSize / 2 + 4);

            canvas.Children.Add(contentPanel);
        }

        // 위치 설정
        Canvas.SetLeft(canvas, screenPos.X);
        Canvas.SetTop(canvas, screenPos.Y);

        // 줌에 상관없이 고정 크기 유지를 위한 역스케일 적용
        var inverseScale = 1.0 / _zoomLevel;
        canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        canvas.RenderTransformOrigin = new Point(0, 0);

        // 다른 층의 마커는 반투명 처리 (층 배지 제외)
        if (!isOnCurrentFloor)
        {
            // canvas 전체 대신 개별 요소에 반투명 적용
            foreach (var child in canvas.Children)
            {
                if (child is FrameworkElement element)
                {
                    // StackPanel인 경우 내부 요소들을 개별 처리
                    if (element is StackPanel stackPanel)
                    {
                        foreach (var stackChild in stackPanel.Children)
                        {
                            if (stackChild is Border border)
                            {
                                // 층 배지는 불투명하게 유지
                                if (border.Tag as string == "FloorBadge")
                                {
                                    border.Opacity = 1.0;
                                }
                                else
                                {
                                    border.Opacity = 0.5;
                                }
                            }
                        }
                    }
                    // 층 배지는 불투명하게 유지
                    else if (element is Border border && border.Tag as string == "FloorBadge")
                    {
                        element.Opacity = 1.0;
                    }
                    else
                    {
                        element.Opacity = 0.5;
                    }
                }
            }
        }

        // 클릭 이벤트
        canvas.MouseLeftButtonDown += QuestMarker_Click;
        canvas.Cursor = Cursors.Hand;

        // 툴팁
        var tooltipDesc = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;
        var tooltipName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;
        var floorIndicator = !isOnCurrentFloor ? " ⬆⬇" : "";
        canvas.ToolTip = $"{tooltipName}\n{tooltipDesc}{floorIndicator}";

        return canvas;
    }

    private FrameworkElement CreateAreaMarker(TaskObjectiveWithLocation objective, QuestObjectiveLocation location, MapConfig config, bool isOnCurrentFloor = true)
    {
        // 설정에서 커스텀 색상 가져오기
        var colorHex = _trackerService.Settings.GetMarkerColor(objective.Type) ?? objective.MarkerColor;
        var markerColor = (Color)ColorConverter.ConvertFromString(colorHex);

        // 다른 층의 마커는 색상을 흐리게 처리
        byte alpha = isOnCurrentFloor ? (byte)60 : (byte)20;
        byte strokeAlpha = isOnCurrentFloor ? (byte)255 : (byte)100;

        // Area 마커임을 표시하는 태그
        var areaTag = new AreaMarkerTag { Objective = objective, IsArea = true };

        // 최상위 컨테이너 (역스케일 적용 안 함)
        var container = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = areaTag
        };

        // Polygon 그리기 (맵과 함께 줌됨)
        var polygon = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(alpha, markerColor.R, markerColor.G, markerColor.B)),
            Stroke = new SolidColorBrush(Color.FromArgb(strokeAlpha, markerColor.R, markerColor.G, markerColor.B)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 2 },
            Tag = "AreaPolygon" // UpdateMarkerScales에서 제외하기 위한 태그
        };

        // Outline 좌표를 스크린 좌표로 변환
        double sumX = 0, sumY = 0;
        foreach (var point in location.Outline!)
        {
            var (sx, sy) = config.GameToScreen(point.X, point.Y);
            polygon.Points.Add(new Point(sx, sy));
            sumX += sx;
            sumY += sy;
        }

        container.Children.Add(polygon);

        // 중심점 계산
        var centroidX = sumX / location.Outline.Count;
        var centroidY = sumY / location.Outline.Count;

        // 설정에서 마커 크기 가져오기
        var baseMarkerSize = _trackerService.Settings.MarkerSize;
        var mapConfig = _trackerService.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;
        var markerSize = baseMarkerSize * mapScale;

        // 중앙 마커용 캔버스 (역스케일 적용됨)
        var centerCanvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = objective
        };

        // 다이아몬드 마커
        var diamond = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb((byte)(isOnCurrentFloor ? 200 : 100), markerColor.R, markerColor.G, markerColor.B)),
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Points = new PointCollection
            {
                new Point(0, -markerSize / 2),
                new Point(markerSize / 2, 0),
                new Point(0, markerSize / 2),
                new Point(-markerSize / 2, 0)
            }
        };
        centerCanvas.Children.Add(diamond);

        // 퀘스트명 표시
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        if (showName)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            var nameText = new TextBlock
            {
                Text = questName,
                FontSize = _questNameTextSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center
            };

            var nameBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Child = nameText
            };

            nameBorder.Tag = objective;
            nameBorder.MouseLeftButtonDown += QuestMarkerText_Click;
            nameBorder.Cursor = Cursors.Hand;

            nameBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(nameBorder, -nameBorder.DesiredSize.Width / 2);
            Canvas.SetTop(nameBorder, markerSize / 2 + 4);
            centerCanvas.Children.Add(nameBorder);
        }

        // 완료 상태 표시
        if (objective.IsCompleted)
        {
            var checkMarkSize = markerSize * 0.8;
            var checkMark = new TextBlock
            {
                Text = "✓",
                FontSize = checkMarkSize,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80))
            };
            checkMark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(checkMark, -checkMark.DesiredSize.Width / 2);
            Canvas.SetTop(checkMark, -checkMark.DesiredSize.Height / 2);
            centerCanvas.Children.Add(checkMark);
            centerCanvas.Opacity = 0.7;
        }

        // 중앙 캔버스 위치 설정 및 역스케일 적용
        Canvas.SetLeft(centerCanvas, centroidX);
        Canvas.SetTop(centerCanvas, centroidY);
        var inverseScale = 1.0 / _zoomLevel;
        centerCanvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        centerCanvas.RenderTransformOrigin = new Point(0, 0);

        container.Children.Add(centerCanvas);

        // 다른 층의 마커는 반투명 처리
        if (!isOnCurrentFloor)
        {
            container.Opacity = 0.5;
        }

        // 클릭 이벤트
        container.MouseLeftButtonDown += QuestMarker_Click;
        container.Cursor = Cursors.Hand;

        // 툴팁
        var tooltipDesc = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;
        var tooltipName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;
        var floorIndicator = !isOnCurrentFloor ? " ⬆⬇" : "";
        container.ToolTip = $"[Area] {tooltipName}\n{tooltipDesc}{floorIndicator}";

        return container;
    }

    private FrameworkElement CreateOrPointerMarker(TaskObjectiveWithLocation objective, QuestObjectiveLocation location, ScreenPosition screenPos, bool isOnCurrentFloor = true)
    {
        // OR 마커는 주황색 (#FF5722)
        var orColor = Color.FromRgb(255, 87, 34);

        // 다른 층의 마커는 색상을 흐리게 처리
        if (!isOnCurrentFloor)
        {
            orColor = Color.FromArgb(100, orColor.R, orColor.G, orColor.B);
        }

        var markerBrush = new SolidColorBrush(orColor);

        // 설정에서 마커 크기 가져오기
        var baseMarkerSize = _trackerService.Settings.MarkerSize;
        var mapConfig = _trackerService.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;
        var markerSize = baseMarkerSize * mapScale;

        var canvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = objective
        };

        // 주황색 원형 마커
        var orCircle = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = markerBrush,
            Stroke = Brushes.White,
            StrokeThickness = 3
        };
        Canvas.SetLeft(orCircle, -markerSize / 2);
        Canvas.SetTop(orCircle, -markerSize / 2);
        canvas.Children.Add(orCircle);

        // OR 인덱스 추출 (예: "_opt_1" -> 1, "_opt_0" -> 1)
        var orIndex = 1;
        var idxMatch = System.Text.RegularExpressions.Regex.Match(location.Id, @"_opt_(\d+)");
        if (idxMatch.Success && int.TryParse(idxMatch.Groups[1].Value, out var parsedIdx))
        {
            // 0-indexed인 경우 1-indexed로 변환
            orIndex = parsedIdx + 1;
        }

        // OR 레이블
        var orLabel = new TextBlock
        {
            Text = $"OR{orIndex}",
            FontSize = _questNameTextSize * 0.9,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White
        };

        var orLabelBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 255, 87, 34)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = orLabel
        };

        orLabelBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(orLabelBorder, markerSize / 2 + 4);
        Canvas.SetTop(orLabelBorder, -orLabelBorder.DesiredSize.Height / 2);
        canvas.Children.Add(orLabelBorder);

        // 퀘스트명 표시
        var showName = _questMarkerStyle == QuestMarkerStyle.DefaultWithName ||
                       _questMarkerStyle == QuestMarkerStyle.GreenCircleWithName;
        if (showName)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            var nameText = new TextBlock
            {
                Text = questName,
                FontSize = _questNameTextSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center
            };

            var nameBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 3, 6, 3),
                Child = nameText
            };

            nameBorder.Tag = objective;
            nameBorder.MouseLeftButtonDown += QuestMarkerText_Click;
            nameBorder.Cursor = Cursors.Hand;

            nameBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(nameBorder, -nameBorder.DesiredSize.Width / 2);
            Canvas.SetTop(nameBorder, markerSize / 2 + 4);
            canvas.Children.Add(nameBorder);
        }

        // 완료 상태 표시
        if (objective.IsCompleted)
        {
            var checkMarkSize = markerSize * 0.8;
            var checkMark = new TextBlock
            {
                Text = "✓",
                FontSize = checkMarkSize,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80))
            };
            checkMark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(checkMark, -checkMark.DesiredSize.Width / 2);
            Canvas.SetTop(checkMark, -checkMark.DesiredSize.Height / 2);
            canvas.Children.Add(checkMark);
            canvas.Opacity = 0.7;
        }

        // 위치 설정
        Canvas.SetLeft(canvas, screenPos.X);
        Canvas.SetTop(canvas, screenPos.Y);

        // 줌에 상관없이 고정 크기 유지를 위한 역스케일 적용
        var inverseScale = 1.0 / _zoomLevel;
        canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        canvas.RenderTransformOrigin = new Point(0, 0);

        // 다른 층의 마커는 반투명 처리
        if (!isOnCurrentFloor)
        {
            canvas.Opacity = 0.5;
        }

        // 클릭 이벤트
        canvas.MouseLeftButtonDown += QuestMarker_Click;
        canvas.Cursor = Cursors.Hand;

        // 툴팁
        var tooltipDesc = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.DescriptionKo)
            ? objective.DescriptionKo
            : objective.Description;
        var tooltipName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
            ? objective.TaskNameKo
            : objective.TaskName;
        var floorIndicator = !isOnCurrentFloor ? " ⬆⬇" : "";
        canvas.ToolTip = $"[OR{orIndex}] {tooltipName}\n{tooltipDesc}{floorIndicator}";

        return canvas;
    }

    #endregion

    #region Private Methods - Grouping

    private void DetectAndGroupOverlappingMarkers()
    {
        // 기존 그룹 마커 제거
        ClearGroupedMarkers();

        // 모든 텍스트 컨테이너(StackPanel)를 먼저 보이게 복원
        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                ShowMarkerText(canvas);
            }
        }

        if (_questMarkerElements.Count < 2) return;

        // 마커와 텍스트 경계 정보 수집
        var markerInfos = new List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)>();

        foreach (var marker in _questMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                var objective = GetObjectiveFromTag(canvas.Tag);
                if (objective == null) continue;

                // Area 마커인 경우 내부 centerCanvas에서 텍스트 찾기
                Canvas? targetCanvas = canvas;
                if (canvas.Tag is AreaMarkerTag)
                {
                    foreach (var child in canvas.Children)
                    {
                        if (child is Canvas innerCanvas && innerCanvas.Tag is TaskObjectiveWithLocation)
                        {
                            targetCanvas = innerCanvas;
                            break;
                        }
                    }
                }

                // 텍스트 컨테이너 찾기 (StackPanel 또는 Border)
                FrameworkElement? textContainer = null;
                foreach (var child in targetCanvas.Children)
                {
                    // 새 방식: StackPanel (퀘스트명 + 층 배지)
                    if (child is StackPanel stackPanel)
                    {
                        textContainer = stackPanel;
                        break;
                    }
                    // 이전 방식 호환: 직접 Border
                    if (child is Border border && border.Tag is TaskObjectiveWithLocation)
                    {
                        textContainer = border;
                        break;
                    }
                }

                if (textContainer != null)
                {
                    // 마커의 화면 위치 (Area 마커는 centerCanvas의 위치 사용)
                    var markerX = Canvas.GetLeft(targetCanvas);
                    var markerY = Canvas.GetTop(targetCanvas);

                    // 텍스트 컨테이너의 상대 위치
                    var textLeft = Canvas.GetLeft(textContainer);
                    var textTop = Canvas.GetTop(textContainer);

                    // 줌 레벨에 따른 역스케일 고려
                    var textInverseScale = 1.0 / _zoomLevel;

                    // 텍스트 컨테이너의 실제 크기
                    textContainer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    var textWidth = textContainer.DesiredSize.Width;
                    var textHeight = textContainer.DesiredSize.Height;

                    // 실제 화면상의 텍스트 경계 계산 (줌 적용)
                    var bounds = new Rect(
                        markerX + textLeft * textInverseScale,
                        markerY + textTop * textInverseScale,
                        textWidth * textInverseScale,
                        textHeight * textInverseScale
                    );

                    markerInfos.Add((marker, bounds, objective));
                }
            }
        }

        // 겹치는 마커 그룹 찾기 (Union-Find 방식)
        var groups = FindOverlappingGroups(markerInfos);

        // 마커 크기 계산 (그룹 리스트 위치 예측용)
        var baseMarkerSize = _trackerService.Settings.MarkerSize;
        var mapConfig = _trackerService.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;
        var markerSize = baseMarkerSize * mapScale;
        var inverseScale = 1.0 / _zoomLevel;

        // 이미 처리된 마커 추적 (중복 그룹 인디케이터 방지)
        var processedObjectiveIds = new HashSet<string>();

        // 2개 이상 겹치는 그룹만 처리
        foreach (var group in groups.Where(g => g.Count >= 2).ToList())
        {
            // 이미 처리된 마커 필터링
            var filteredGroup = group.Where(g => !processedObjectiveIds.Contains(g.Objective.ObjectiveId)).ToList();
            if (filteredGroup.Count < 2) continue;

            // 그룹 리스트가 표시될 영역과 겹치는 텍스트를 반복적으로 추가
            var expandedGroup = ExpandGroupToIncludeOverlappingTexts(filteredGroup, markerInfos, markerSize, inverseScale);

            // 확장된 그룹에서도 이미 처리된 마커 제외
            expandedGroup = expandedGroup.Where(g => !processedObjectiveIds.Contains(g.Objective.ObjectiveId)).ToList();
            if (expandedGroup.Count < 2) continue;

            // 마커들의 위치 정보 수집
            var markerPositions = new List<(double X, double Y)>();
            foreach (var info in expandedGroup)
            {
                if (info.Marker is Canvas c)
                {
                    // Area 마커의 경우 centerCanvas의 위치 사용
                    var (posX, posY) = GetMarkerPosition(c);
                    markerPositions.Add((posX, posY));
                }
            }

            if (markerPositions.Count == 0) continue;

            // 마커들의 중심 X와 가장 아래 Y 계산
            var centerX = markerPositions.Average(p => p.X);
            var bottomY = markerPositions.Max(p => p.Y);

            // 그룹 내 각 마커의 텍스트만 숨기기 (마커 원은 그대로 유지)
            foreach (var info in expandedGroup)
            {
                if (info.Marker is Canvas c)
                {
                    HideMarkerText(c);
                }

                // 처리된 마커로 표시
                processedObjectiveIds.Add(info.Objective.ObjectiveId);
                // markerInfos에서 제거 (다른 그룹에서 중복 처리 방지)
                markerInfos.RemoveAll(m => m.Objective.ObjectiveId == info.Objective.ObjectiveId);
            }

            // 텍스트 그룹 인디케이터 생성 (마커 아래에 위치)
            var groupIndicator = CreateTextGroupIndicator(expandedGroup, centerX, bottomY);
            _groupedMarkerElements.Add(groupIndicator);
            _markersContainer.Children.Add(groupIndicator);
        }
    }

    private List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> ExpandGroupToIncludeOverlappingTexts(
        List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> initialGroup,
        List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> allMarkers,
        double markerSize, double inverseScale)
    {
        var expandedGroup = new List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)>(initialGroup);
        var groupObjectiveIds = new HashSet<string>(initialGroup.Select(g => g.Objective.ObjectiveId));

        bool changed;
        do
        {
            changed = false;

            // 현재 그룹의 마커 위치로 그룹 리스트 영역 계산
            var markerPositions = expandedGroup
                .Where(info => info.Marker is Canvas)
                .Select(info => (Canvas.GetLeft((Canvas)info.Marker), Canvas.GetTop((Canvas)info.Marker)))
                .ToList();

            if (markerPositions.Count == 0) break;

            var centerX = markerPositions.Average(p => p.Item1);
            var bottomY = markerPositions.Max(p => p.Item2);

            // 그룹 리스트의 예상 크기 계산 (항목당 약 20픽셀 높이, 최대 너비 200픽셀)
            var estimatedListHeight = expandedGroup.Count * 20 * inverseScale;
            var estimatedListWidth = 200 * inverseScale;

            // 그룹 리스트가 표시될 영역 (마커 아래)
            var groupListBounds = new Rect(
                centerX - estimatedListWidth / 2,
                bottomY + (markerSize / 2 + 4) * inverseScale,
                estimatedListWidth,
                estimatedListHeight
            );

            // 그룹에 포함되지 않은 마커들 중 그룹 리스트 영역과 겹치는 것 찾기
            foreach (var marker in allMarkers.ToList())
            {
                if (groupObjectiveIds.Contains(marker.Objective.ObjectiveId))
                    continue;

                if (groupListBounds.IntersectsWith(marker.TextBounds))
                {
                    expandedGroup.Add(marker);
                    groupObjectiveIds.Add(marker.Objective.ObjectiveId);
                    changed = true;
                }
            }
        } while (changed);

        return expandedGroup;
    }

    private List<List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)>> FindOverlappingGroups(
        List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> markerInfos)
    {
        var n = markerInfos.Count;
        var parent = Enumerable.Range(0, n).ToArray();

        int Find(int x)
        {
            if (parent[x] != x) parent[x] = Find(parent[x]);
            return parent[x];
        }

        void Union(int x, int y)
        {
            var px = Find(x);
            var py = Find(y);
            if (px != py) parent[px] = py;
        }

        // 겹치는 마커끼리 연결
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (markerInfos[i].TextBounds.IntersectsWith(markerInfos[j].TextBounds))
                {
                    Union(i, j);
                }
            }
        }

        // 그룹화
        var groups = new Dictionary<int, List<(FrameworkElement, Rect, TaskObjectiveWithLocation)>>();
        for (int i = 0; i < n; i++)
        {
            var root = Find(i);
            if (!groups.ContainsKey(root))
                groups[root] = new List<(FrameworkElement, Rect, TaskObjectiveWithLocation)>();
            groups[root].Add(markerInfos[i]);
        }

        return groups.Values.ToList();
    }

    private FrameworkElement CreateTextGroupIndicator(
        List<(FrameworkElement Marker, Rect TextBounds, TaskObjectiveWithLocation Objective)> group,
        double centerX, double bottomY)
    {
        var canvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = group.Select(g => g.Objective).ToList()
        };

        // 리스트 형태로 퀘스트 이름들을 표시하는 StackPanel
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Vertical
        };

        foreach (var item in group)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(item.Objective.TaskNameKo)
                ? item.Objective.TaskNameKo
                : item.Objective.TaskName;

            // StackPanel으로 이름과 층 배지를 나란히 배치
            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 1, 0, 1)
            };

            var itemText = new TextBlock
            {
                Text = $"• {questName}",
                FontSize = _questNameTextSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 완료 상태 표시
            if (item.Objective.IsCompleted)
            {
                itemText.TextDecorations = TextDecorations.Strikethrough;
                itemText.Foreground = new SolidColorBrush(Colors.LightGray);
            }

            itemPanel.Children.Add(itemText);

            // 층 정보 추가 (위치가 있는 경우)
            if (item.Objective.Locations.Count > 0)
            {
                var location = item.Objective.Locations[0];
                var floorInfo = GetFloorIndicator(location.FloorId);
                if (floorInfo.HasValue)
                {
                    var (arrow, floorText, indicatorColor) = floorInfo.Value;
                    var floorBadge = new Border
                    {
                        Background = new SolidColorBrush(indicatorColor),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(3, 1, 3, 1),
                        Margin = new Thickness(4, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var floorBadgeText = new TextBlock
                    {
                        Text = $"{arrow}{floorText}",
                        FontSize = _questNameTextSize * 0.85,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White
                    };
                    floorBadge.Child = floorBadgeText;
                    itemPanel.Children.Add(floorBadge);
                }
            }

            // 개별 항목에 Tag 설정 (클릭 시 사용)
            var itemBorder = new Border
            {
                Child = itemPanel,
                Tag = item.Objective,
                Cursor = Cursors.Hand,
                Padding = new Thickness(2, 0, 2, 0)
            };

            // 호버 효과
            itemBorder.MouseEnter += (s, e) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            };
            itemBorder.MouseLeave += (s, e) =>
            {
                itemBorder.Background = Brushes.Transparent;
            };

            // 개별 클릭 이벤트
            itemBorder.MouseLeftButtonDown += GroupListItem_Click;

            stackPanel.Children.Add(itemBorder);
        }

        var groupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 30, 30, 30)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 4),
            Child = stackPanel,
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 70, 130, 180)),
            BorderThickness = new Thickness(1)
        };

        // 크기 측정
        groupBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = groupBorder.DesiredSize.Width;

        // 마커 크기 계산 (마커 아래에 배치하기 위함)
        var baseMarkerSize = _trackerService.Settings.MarkerSize;
        var mapConfig = _trackerService.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;
        var markerSize = baseMarkerSize * mapScale;

        // 마커 아래에 위치 (X는 중앙 정렬, Y는 마커 아래)
        Canvas.SetLeft(groupBorder, -textWidth / 2);
        Canvas.SetTop(groupBorder, markerSize / 2 + 4);  // 마커 반경 + 여백
        canvas.Children.Add(groupBorder);

        // 위치 설정 (마커들의 중심 X, 가장 아래 마커의 Y)
        Canvas.SetLeft(canvas, centerX);
        Canvas.SetTop(canvas, bottomY);

        // 줌에 상관없이 고정 크기 유지
        var inverseScale = 1.0 / _zoomLevel;
        canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
        canvas.RenderTransformOrigin = new Point(0, 0);

        return canvas;
    }

    private void ClearGroupedMarkers()
    {
        foreach (var marker in _groupedMarkerElements)
        {
            if (marker is Canvas c)
            {
                // 리스트 내 개별 항목의 이벤트 핸들러 해제
                foreach (var child in c.Children)
                {
                    if (child is Border groupBorder && groupBorder.Child is StackPanel stackPanel)
                    {
                        foreach (var item in stackPanel.Children)
                        {
                            if (item is Border itemBorder)
                            {
                                itemBorder.MouseLeftButtonDown -= GroupListItem_Click;
                            }
                        }
                    }
                }
            }
            _markersContainer.Children.Remove(marker);
        }
        _groupedMarkerElements.Clear();
        CloseGroupPopup();
    }

    #endregion

    #region Private Methods - Event Handlers

    private void QuestMarker_Click(object sender, MouseButtonEventArgs e)
    {
        TaskObjectiveWithLocation? objective = null;

        if (sender is FrameworkElement element)
        {
            // 일반 마커 또는 OR 마커
            if (element.Tag is TaskObjectiveWithLocation obj)
            {
                objective = obj;
            }
            // Area 마커
            else if (element.Tag is AreaMarkerTag areaTag)
            {
                objective = areaTag.Objective;
            }
        }

        if (objective != null)
        {
            FloorChangeRequested?.Invoke(this, objective);
            ObjectiveSelected?.Invoke(this, objective);
            e.Handled = true;
        }
    }

    private void QuestMarkerText_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is TaskObjectiveWithLocation objective)
        {
            FloorChangeRequested?.Invoke(this, objective);
            ObjectiveSelected?.Invoke(this, objective);
            e.Handled = true;
        }
    }

    private void GroupListItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.Tag is TaskObjectiveWithLocation objective)
        {
            ObjectiveSelected?.Invoke(this, objective);
            e.Handled = true;
        }
    }

    #endregion

    #region Private Methods - Popup Management

    private void ShowGroupPopup(Canvas indicator, List<TaskObjectiveWithLocation> objectives)
    {
        // 기존 팝업 닫기
        CloseGroupPopup();

        var stackPanel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
            MinWidth = 200
        };

        foreach (var objective in objectives)
        {
            var questName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(objective.TaskNameKo)
                ? objective.TaskNameKo
                : objective.TaskName;

            var itemBorder = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Tag = objective,
                Cursor = Cursors.Hand
            };

            var itemText = new TextBlock
            {
                Text = questName,
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };

            // 완료 상태 표시
            if (objective.IsCompleted)
            {
                itemText.TextDecorations = TextDecorations.Strikethrough;
                itemText.Foreground = new SolidColorBrush(Colors.Gray);
            }

            itemBorder.Child = itemText;

            // 호버 효과
            itemBorder.MouseEnter += (s, ev) =>
            {
                itemBorder.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            };
            itemBorder.MouseLeave += (s, ev) =>
            {
                itemBorder.Background = Brushes.Transparent;
            };

            // 클릭 시 퀘스트 선택 및 층 변경
            itemBorder.MouseLeftButtonDown += (s, ev) =>
            {
                if (itemBorder.Tag is TaskObjectiveWithLocation obj)
                {
                    CloseGroupPopup();
                    FloorChangeRequested?.Invoke(this, obj);
                    ObjectiveSelected?.Invoke(this, obj);
                    ev.Handled = true;
                }
            };

            stackPanel.Children.Add(itemBorder);
        }

        var popupBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 70, 130, 180)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = stackPanel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 3,
                Opacity = 0.5
            }
        };

        _markerGroupPopup = new Popup
        {
            Child = popupBorder,
            PlacementTarget = indicator,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            IsOpen = true
        };

        // 팝업 외부 클릭 시 닫기
        _markerGroupPopup.Closed += (s, ev) => _markerGroupPopup = null;
    }

    private void CloseGroupPopup()
    {
        if (_markerGroupPopup != null)
        {
            _markerGroupPopup.IsOpen = false;
            _markerGroupPopup = null;
        }
    }

    #endregion

    #region Private Methods - Highlighting

    private void UpdateMarkerHighlight()
    {
        // 이전 선택 마커 하이라이트 제거
        if (_selectedMarkerElement is Canvas prevCanvas)
        {
            RemoveMarkerHighlight(prevCanvas);
        }
        _selectedMarkerElement = null;

        // 새로운 선택 마커 하이라이트 추가
        if (_selectedObjective != null)
        {
            foreach (var marker in _questMarkerElements)
            {
                if (marker is Canvas canvas)
                {
                    var obj = GetObjectiveFromTag(canvas.Tag);
                    if (obj != null && obj.ObjectiveId == _selectedObjective.ObjectiveId)
                    {
                        AddMarkerHighlight(canvas);
                        _selectedMarkerElement = canvas;
                        break;
                    }
                }
            }
        }
    }

    private void AddMarkerHighlight(Canvas markerCanvas)
    {
        // 강조 표시용 외곽 링 추가
        var baseMarkerSize = _trackerService.Settings.MarkerSize;

        // 맵별 마커 스케일 적용
        var mapConfig = _trackerService.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;

        var markerSize = baseMarkerSize * mapScale;
        var highlightSize = markerSize * 2.5;

        var highlightRing = new Ellipse
        {
            Width = highlightSize,
            Height = highlightSize,
            Stroke = new SolidColorBrush(Colors.Yellow),
            StrokeThickness = 3,
            Fill = Brushes.Transparent,
            Tag = "HighlightRing"
        };
        Canvas.SetLeft(highlightRing, -highlightSize / 2);
        Canvas.SetTop(highlightRing, -highlightSize / 2);
        Panel.SetZIndex(highlightRing, -1);
        markerCanvas.Children.Insert(0, highlightRing);

        // 펄스 애니메이션 추가
        var scaleTransform = new ScaleTransform(1, 1, highlightSize / 2, highlightSize / 2);
        highlightRing.RenderTransform = scaleTransform;

        var pulseAnimation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.8,
            To = 1.2,
            Duration = TimeSpan.FromMilliseconds(800),
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnimation);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnimation);
    }

    private void RemoveMarkerHighlight(Canvas markerCanvas)
    {
        // HighlightRing 태그가 있는 요소 찾아 제거
        var highlightRing = markerCanvas.Children.OfType<Ellipse>()
            .FirstOrDefault(e => e.Tag as string == "HighlightRing");
        if (highlightRing != null)
        {
            markerCanvas.Children.Remove(highlightRing);
        }
    }

    #endregion

    #region Private Methods - Helpers

    private static TaskObjectiveWithLocation? GetObjectiveFromTag(object? tag)
    {
        return tag switch
        {
            TaskObjectiveWithLocation obj => obj,
            AreaMarkerTag areaTag => areaTag.Objective,
            _ => null
        };
    }

    private static (double X, double Y) GetMarkerPosition(Canvas markerCanvas)
    {
        // Area 마커인 경우 centerCanvas의 위치 반환
        if (markerCanvas.Tag is AreaMarkerTag)
        {
            foreach (var child in markerCanvas.Children)
            {
                if (child is Canvas centerCanvas && centerCanvas.Tag is TaskObjectiveWithLocation)
                {
                    return (Canvas.GetLeft(centerCanvas), Canvas.GetTop(centerCanvas));
                }
            }
        }

        // 일반 마커의 경우 canvas 자체의 위치 반환
        return (Canvas.GetLeft(markerCanvas), Canvas.GetTop(markerCanvas));
    }

    private static void HideMarkerText(Canvas markerCanvas)
    {
        SetMarkerTextVisibility(markerCanvas, Visibility.Collapsed);
    }

    private static void ShowMarkerText(Canvas markerCanvas)
    {
        SetMarkerTextVisibility(markerCanvas, Visibility.Visible);
    }

    private static void SetMarkerTextVisibility(Canvas markerCanvas, Visibility visibility)
    {
        // Area 마커인 경우 centerCanvas 내부 검사
        Canvas targetCanvas = markerCanvas;
        if (markerCanvas.Tag is AreaMarkerTag)
        {
            foreach (var child in markerCanvas.Children)
            {
                if (child is Canvas centerCanvas && centerCanvas.Tag is TaskObjectiveWithLocation)
                {
                    targetCanvas = centerCanvas;
                    break;
                }
            }
        }

        foreach (var child in targetCanvas.Children)
        {
            // 새 방식: StackPanel (퀘스트명 + 층 배지)
            if (child is StackPanel stackPanel)
            {
                stackPanel.Visibility = visibility;
            }
            // 이전 방식 호환: 직접 Border
            else if (child is Border border && border.Tag is TaskObjectiveWithLocation)
            {
                border.Visibility = visibility;
            }
        }
    }

    private bool IsLocationOnCurrentMap(QuestObjectiveLocation location, MapConfig config)
    {
        var mapNamesToCheck = new List<string> { _currentMapKey! };
        if (config.Aliases != null)
        {
            mapNamesToCheck.AddRange(config.Aliases);
        }
        if (!string.IsNullOrEmpty(config.DisplayName))
        {
            mapNamesToCheck.Add(config.DisplayName);
        }

        // 공백/하이픈 제거 후 비교
        var normalizedLocationMap = location.MapNormalizedName?.Replace(" ", "").Replace("-", "").ToLowerInvariant();
        foreach (var mapName in mapNamesToCheck)
        {
            var normalizedMapName = mapName.Replace(" ", "").Replace("-", "").ToLowerInvariant();
            if (normalizedLocationMap == normalizedMapName)
                return true;
        }

        return false;
    }

    private bool IsMarkerOnCurrentFloor(string? markerFloorId)
    {
        // 층 정보가 없으면 기본층(main)으로 간주
        if (string.IsNullOrEmpty(_currentMapKey) || string.IsNullOrEmpty(_currentFloorId))
            return true;

        var config = _trackerService.GetMapConfig(_currentMapKey);
        if (config?.Floors == null || config.Floors.Count == 0)
            return true;

        // 마커의 FloorId가 비어있으면 main으로 간주
        var effectiveMarkerFloorId = string.IsNullOrEmpty(markerFloorId) ? "main" : markerFloorId;

        // 층 ID 비교 (대소문자 무시)
        return string.Equals(_currentFloorId, effectiveMarkerFloorId, StringComparison.OrdinalIgnoreCase);
    }

    private (string arrow, string floorText, Color color)? GetFloorIndicator(string? markerFloorId)
    {
        if (string.IsNullOrEmpty(_currentMapKey) || string.IsNullOrEmpty(_currentFloorId))
            return null;

        var config = _trackerService.GetMapConfig(_currentMapKey);
        if (config?.Floors == null || config.Floors.Count == 0)
            return null;

        // 현재 층의 Order 가져오기
        var currentFloor = config.Floors.FirstOrDefault(f =>
            string.Equals(f.LayerId, _currentFloorId, StringComparison.OrdinalIgnoreCase));
        var currentOrder = currentFloor?.Order ?? 0;

        // 마커 층의 Order 가져오기 (FloorId가 없으면 main으로 간주)
        var effectiveFloorId = string.IsNullOrEmpty(markerFloorId) ? "main" : markerFloorId;
        var markerFloor = config.Floors.FirstOrDefault(f =>
            string.Equals(f.LayerId, effectiveFloorId, StringComparison.OrdinalIgnoreCase));
        var markerOrder = markerFloor?.Order ?? 0;

        // 같은 층이면 표시 안함
        if (currentOrder == markerOrder)
            return null;

        // 화살표 방향 결정 (마커가 현재 층보다 위에 있으면 ↑, 아래면 ↓)
        var isAbove = markerOrder > currentOrder;
        var arrow = isAbove ? "↑" : "↓";

        // 색상 결정 (위: 하늘색, 아래: 주황색)
        var color = isAbove
            ? Color.FromRgb(100, 181, 246)  // Light Blue
            : Color.FromRgb(255, 167, 38);  // Orange

        // 층 표시 문자 결정 (B: 지하, G: 기본층, 2/3: 2층/3층)
        string floorText;
        if (markerOrder < 0)
        {
            floorText = "B";
        }
        else if (markerOrder == 0)
        {
            floorText = "G";
        }
        else
        {
            floorText = (markerOrder + 1).ToString(); // Order 1 = 2층
        }

        return (arrow, floorText, color);
    }

    #endregion
}
