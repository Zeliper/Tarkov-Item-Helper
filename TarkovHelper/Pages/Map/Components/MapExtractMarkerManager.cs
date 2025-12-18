using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TarkovHelper.Models;
using TarkovHelper.Models.Map;
using TarkovHelper.Services;
using TarkovHelper.Services.Map;

namespace TarkovHelper.Pages.Map.Components;

/// <summary>
/// 맵 페이지의 탈출구(Extract) 마커 관리를 담당하는 클래스.
/// 마커 생성, 필터링, 표시 등의 로직을 캡슐화합니다.
/// </summary>
public class MapExtractMarkerManager
{
    // 의존성 서비스
    private readonly Canvas _markersContainer;
    private readonly MapTrackerService _trackerService;
    private readonly ExtractService _extractService;
    private readonly LocalizationService _loc;

    // 마커 상태
    private readonly List<FrameworkElement> _extractMarkerElements = new();

    // 설정 및 상태
    private string? _currentMapKey;
    private string? _currentFloorId;
    private double _zoomLevel = 1.0;
    private bool _showExtractMarkers = true;
    private bool _showPmcExtracts = true;
    private bool _showScavExtracts = true;
    private bool _showTransitExtracts = true;
    private double _extractNameTextSize = 10.0;

    // 보정 모드 콜백 (MapCalibrationController에서 처리)
    public event Action<FrameworkElement, MapExtract>? CalibrationMarkerSetup;

    public MapExtractMarkerManager(
        Canvas markersContainer,
        MapTrackerService trackerService,
        ExtractService extractService,
        LocalizationService localizationService)
    {
        _markersContainer = markersContainer ?? throw new ArgumentNullException(nameof(markersContainer));
        _trackerService = trackerService ?? throw new ArgumentNullException(nameof(trackerService));
        _extractService = extractService ?? throw new ArgumentNullException(nameof(extractService));
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

    public void SetShowExtractMarkers(bool show)
    {
        _showExtractMarkers = show;
    }

    public void SetShowPmcExtracts(bool show)
    {
        _showPmcExtracts = show;
    }

    public void SetShowScavExtracts(bool show)
    {
        _showScavExtracts = show;
    }

    public void SetShowTransitExtracts(bool show)
    {
        _showTransitExtracts = show;
    }

    public void SetExtractNameTextSize(double size)
    {
        _extractNameTextSize = size;
    }

    #endregion

    #region Public Methods - Marker Management

    public void RefreshMarkers()
    {
        if (string.IsNullOrEmpty(_currentMapKey)) return;
        if (!_extractService.IsLoaded) return;

        // 기존 마커 제거
        ClearMarkers();

        if (!_showExtractMarkers) return;

        // 맵 설정 가져오기
        var config = _trackerService.GetMapConfig(_currentMapKey);
        if (config == null) return;

        // 디버그: CalibratedTransform 확인
        if (config.CalibratedTransform == null)
        {
            System.Diagnostics.Debug.WriteLine($"[MapExtractMarkerManager] WARNING: CalibratedTransform is NULL for map '{_currentMapKey}'");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[MapExtractMarkerManager] CalibratedTransform for '{_currentMapKey}': [{string.Join(", ", config.CalibratedTransform)}]");
        }

        // 현재 맵의 탈출구 가져오기 (MapConfig의 Aliases 사용)
        var extracts = _extractService.GetExtractsForMap(_currentMapKey, config);

        // 디버그: 로드된 탈출구 목록 출력
        System.Diagnostics.Debug.WriteLine($"[MapExtractMarkerManager] Loaded {extracts.Count} extracts for '{_currentMapKey}':");
        foreach (var e in extracts)
        {
            System.Diagnostics.Debug.WriteLine($"  - {e.Name} ({e.Faction}) FloorId={e.FloorId ?? "null"}");
        }

        // 모든 탈출구를 개별적으로 표시 (그룹화 없음)
        foreach (var extract in extracts)
        {
            // 진영 필터 적용
            if (!ShouldShowExtract(extract.Faction)) continue;

            // TarkovDBEditor 방식: config.GameToScreen 직접 사용
            var (screenX, screenY) = config.GameToScreen(extract.X, extract.Z);

            // 디버그: 탈출구 좌표 변환 결과 출력
            System.Diagnostics.Debug.WriteLine($"[MapExtractMarkerManager] Extract '{extract.Name}': Game({extract.X:F2}, {extract.Z:F2}) -> Screen({screenX:F2}, {screenY:F2})");

            var screenPos = new ScreenPosition
            {
                MapKey = _currentMapKey!,
                X = screenX,
                Y = screenY
            };

            // 층 정보 확인: 현재 선택된 층과 탈출구의 층 비교
            var isOnCurrentFloor = IsMarkerOnCurrentFloor(extract.FloorId);

            var marker = CreateExtractMarker(extract, screenPos, null, isOnCurrentFloor);
            _extractMarkerElements.Add(marker);
            _markersContainer.Children.Add(marker);
        }
    }

    public void ClearMarkers()
    {
        _extractMarkerElements.Clear();
        _markersContainer.Children.Clear();
    }

    public void UpdateMarkerScales()
    {
        var inverseScale = 1.0 / _zoomLevel;

        foreach (var marker in _extractMarkerElements)
        {
            if (marker is Canvas canvas)
            {
                canvas.RenderTransform = new ScaleTransform(inverseScale, inverseScale);
            }
        }
    }

    #endregion

    #region Private Methods - Marker Creation

    private FrameworkElement CreateExtractMarker(MapExtract extract, ScreenPosition screenPos, ExtractFaction? overrideFaction = null, bool isOnCurrentFloor = true)
    {
        // 맵별 마커 스케일 적용
        var mapConfig = _trackerService.GetMapConfig(_currentMapKey ?? "");
        var mapScale = mapConfig?.MarkerScale ?? 1.0;

        var baseSize = 20.0;
        var markerSize = baseSize * mapScale;

        // 진영 결정 (오버라이드 또는 기본)
        var faction = overrideFaction ?? extract.Faction;

        // 진영별 색상 설정
        var (fillColor, strokeColor) = GetExtractStyle(faction);

        // 다른 층의 마커는 색상을 흐리게 처리
        if (!isOnCurrentFloor)
        {
            fillColor = Color.FromArgb(100, fillColor.R, fillColor.G, fillColor.B);
            strokeColor = Color.FromArgb(150, strokeColor.R, strokeColor.G, strokeColor.B);
        }

        var canvas = new Canvas
        {
            Width = 0,
            Height = 0,
            Tag = extract
        };

        // 탈출구 이름 텍스트 (마커 위에 표시)
        var displayName = _loc.CurrentLanguage == AppLanguage.KO && !string.IsNullOrEmpty(extract.NameKo)
            ? extract.NameKo
            : extract.Name;

        var textSize = _extractNameTextSize * mapScale;

        // 이름과 층 뱃지를 담을 StackPanel
        var labelStackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // 이름 라벨
        var nameLabel = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
            CornerRadius = new CornerRadius(3 * mapScale),
            Padding = new Thickness(4 * mapScale, 2 * mapScale, 4 * mapScale, 2 * mapScale),
            Child = new TextBlock
            {
                Text = displayName,
                FontSize = textSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(fillColor),
                TextAlignment = TextAlignment.Center
            }
        };
        labelStackPanel.Children.Add(nameLabel);

        // 층 뱃지 (다층 맵에서 다른 층일 때만 표시) - GetFloorIndicator 사용하여 화살표 포함
        var floorInfo = GetFloorIndicator(extract.FloorId);
        if (floorInfo.HasValue)
        {
            var (arrow, floorText, indicatorColor) = floorInfo.Value;

            var floorBadge = new Border
            {
                Background = new SolidColorBrush(indicatorColor),
                CornerRadius = new CornerRadius(3 * mapScale),
                Padding = new Thickness(4 * mapScale, 2 * mapScale, 4 * mapScale, 2 * mapScale),
                Margin = new Thickness(3 * mapScale, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = $"{arrow}{floorText}",
                    FontSize = textSize * 0.9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextAlignment = TextAlignment.Center
                }
            };
            labelStackPanel.Children.Add(floorBadge);
        }

        // 이름 라벨 위치 측정 및 설정
        labelStackPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var labelWidth = labelStackPanel.DesiredSize.Width;
        var labelHeight = labelStackPanel.DesiredSize.Height;
        Canvas.SetLeft(labelStackPanel, -labelWidth / 2);
        Canvas.SetTop(labelStackPanel, -markerSize - labelHeight - 4 * mapScale);
        canvas.Children.Add(labelStackPanel);

        // 배경 원 (글로우 효과)
        var glowSize = markerSize * 1.5;
        var glow = new Ellipse
        {
            Width = glowSize,
            Height = glowSize,
            Fill = new SolidColorBrush(Color.FromArgb(80, fillColor.R, fillColor.G, fillColor.B))
        };
        Canvas.SetLeft(glow, -glowSize / 2);
        Canvas.SetTop(glow, -glowSize / 2);
        canvas.Children.Add(glow);

        // 메인 원
        var mainCircle = new Ellipse
        {
            Width = markerSize,
            Height = markerSize,
            Fill = new SolidColorBrush(fillColor),
            Stroke = new SolidColorBrush(strokeColor),
            StrokeThickness = 2 * mapScale
        };
        Canvas.SetLeft(mainCircle, -markerSize / 2);
        Canvas.SetTop(mainCircle, -markerSize / 2);
        canvas.Children.Add(mainCircle);

        // 탈출구 아이콘 (비상대피 아이콘)
        var iconSize = markerSize * 0.7;
        var iconPath = CreateExtractIcon(iconSize, strokeColor);
        Canvas.SetLeft(iconPath, -iconSize / 2);
        Canvas.SetTop(iconPath, -iconSize / 2);
        canvas.Children.Add(iconPath);

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

        // 툴팁
        var factionText = faction switch
        {
            ExtractFaction.Pmc => "PMC",
            ExtractFaction.Scav => "Scav",
            _ => "Extract"
        };
        var tooltipFloorText = !string.IsNullOrEmpty(extract.FloorId) ? $" ({extract.FloorId})" : "";
        var differentFloorIndicator = !isOnCurrentFloor ? " ⬆⬇" : "";
        canvas.ToolTip = $"[{factionText}] {displayName}{tooltipFloorText}{differentFloorIndicator}";
        canvas.Cursor = Cursors.Hand;

        // 보정 모드용 드래그 이벤트 설정 (콜백으로 전달)
        CalibrationMarkerSetup?.Invoke(canvas, extract);

        return canvas;
    }

    private static (Color fill, Color stroke) GetExtractStyle(ExtractFaction faction)
    {
        return faction switch
        {
            ExtractFaction.Pmc => (
                Color.FromRgb(76, 175, 80),    // Green
                Colors.White),
            ExtractFaction.Scav => (
                Color.FromRgb(158, 158, 158),  // Gray (회색)
                Colors.White),
            ExtractFaction.Shared => (
                Color.FromRgb(76, 175, 80),    // Green (Shared도 PMC로 처리)
                Colors.White),
            ExtractFaction.Transit => (
                Color.FromRgb(255, 152, 0),    // Orange (주황색)
                Colors.White),
            _ => (
                Color.FromRgb(158, 158, 158),  // Gray
                Colors.White)
        };
    }

    private static FrameworkElement CreateExtractIcon(double size, Color strokeColor)
    {
        // 비상대피 아이콘 (uxwing.com emergency-exit-icon)
        // 원본 viewBox: 0 0 108.01 122.88
        var path = new System.Windows.Shapes.Path
        {
            Fill = new SolidColorBrush(strokeColor),
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size
        };

        // uxwing.com emergency exit icon path data
        // 뛰는 사람 + 문 아이콘
        var pathData = "M.5,0H15a.51.51,0,0,1,.5.5V83.38L35.16,82h.22l.24,0c2.07-.14,3.65-.26,4.73-1.23l1.86-2.17a1.12,1.12,0,0,1,1.49-.18l9.35,6.28a1.15,1.15,0,0,1,.49,1c0,.55-.19.7-.61,1.08A11.28,11.28,0,0,0,51.78,88a27.27,27.27,0,0,1-3,3.1,15.84,15.84,0,0,1-3.68,2.45c-2.8,1.36-5.45,1.54-8.59,1.76l-.24,0-.21,0L15.5,96.77v25.61a.52.52,0,0,1-.5.5H.5a.51.51,0,0,1-.5-.5V.5A.5.5,0,0,1,.5,0ZM46,59.91l9-19.12-.89-.25a12.43,12.43,0,0,0-4.77-.82c-1.9.28-3.68,1.42-5.67,2.7-.83.53-1.69,1.09-2.62,1.63-.7.33-1.51.86-2.19,1.25l-8.7,5a1.11,1.11,0,0,1-1.51-.42l-5.48-9.64a1.1,1.1,0,0,1,.42-1.51c3.43-2,7.42-4,10.75-6.14,4-2.49,7.27-4.48,11.06-5.42s8-.8,13.89,1c2.12.59,4.55,1.48,6.55,2.2,1,.35,1.8.66,2.44.87,9.86,3.29,13.19,9.66,15.78,14.6,1.12,2.13,2.09,4,3.34,5,.51.42,1.67.27,3,.09a21.62,21.62,0,0,1,2.64-.23c4.32-.41,8.66-.66,13-1a1.1,1.1,0,0,1,1.18,1L108,61.86A1.11,1.11,0,0,1,107,63L95,63.9c-5.33.38-9.19.66-15-2.47l-.12-.07a23.23,23.23,0,0,1-7.21-8.5l0,0L65.73,68.4a63.9,63.9,0,0,0,5.85,5.32c6,5,11,9.21,9.38,20.43a23.89,23.89,0,0,1-.65,2.93c-.27,1-.56,1.9-.87,2.84-2.29,6.54-4.22,13.5-6.29,20.13a1.1,1.1,0,0,1-1,.81l-11.66.78a1,1,0,0,1-.39,0,1.12,1.12,0,0,1-.75-1.38c2.45-8.12,5-16.25,7.39-24.38a29,29,0,0,0,.87-3,7,7,0,0,0,.08-2.65l0-.24a4.16,4.16,0,0,0-.73-2.22,53.23,53.23,0,0,0-8.76-5.57c-3.75-2.07-7.41-4.08-10.25-7a12.15,12.15,0,0,1-3.59-7.36A14.76,14.76,0,0,1,46,59.91ZM80.07,6.13a12.29,12.29,0,0,1,13.1,11.39v0a12.29,12.29,0,0,1-24.52,1.72v0A12.3,12.3,0,0,1,80,6.13ZM3.34,35H6.69V51.09H3.34V35Z";

        path.Data = Geometry.Parse(pathData);

        return path;
    }

    #endregion

    #region Private Methods - Unused Grouping Methods (보정 모드에서 사용하지 않음)

    // 현재 사용하지 않지만, 향후 탈출구 그룹화가 필요할 경우를 위해 보존
#pragma warning disable IDE0051 // 사용되지 않는 private 멤버 제거
    private List<List<MapExtract>> GroupExtractsByPosition(List<MapExtract> extracts)
    {
        var groups = new List<List<MapExtract>>();
        var used = new HashSet<string>();

        foreach (var extract in extracts)
        {
            if (used.Contains(extract.Id)) continue;

            var group = new List<MapExtract> { extract };
            used.Add(extract.Id);

            // 같은 위치(근접)의 다른 탈출구 찾기
            // 단, PMC+Scav 공용 탈출구만 그룹화 (같은 이름 또는 다른 진영이면서 매우 가까운 경우)
            foreach (var other in extracts)
            {
                if (used.Contains(other.Id)) continue;

                // 거리 계산
                var distance = Math.Sqrt(
                    Math.Pow(extract.X - other.X, 2) +
                    Math.Pow(extract.Z - other.Z, 2));

                // 그룹화 조건:
                // 1. 같은 이름이고 10유닛 이내 (PMC/Scav 공용 탈출구)
                // 2. 다른 진영이고 10유닛 이내 (PMC+Scav 겹치는 경우)
                var sameName = string.Equals(extract.Name, other.Name, StringComparison.OrdinalIgnoreCase);
                var differentFaction = extract.Faction != other.Faction;

                if (distance < 10 && (sameName || differentFaction))
                {
                    group.Add(other);
                    used.Add(other.Id);
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    private (MapExtract extract, ExtractFaction faction) DetermineExtractDisplay(List<MapExtract> group)
    {
        if (group.Count == 1)
        {
            // Shared 탈출구는 PMC로 처리
            var faction = group[0].Faction == ExtractFaction.Shared ? ExtractFaction.Pmc : group[0].Faction;
            return (group[0], faction);
        }

        // PMC와 Scav 둘 다 있으면 PMC로 표시 (Shared도 PMC로 처리)
        var hasPmc = group.Any(e => e.Faction == ExtractFaction.Pmc || e.Faction == ExtractFaction.Shared);
        var hasScav = group.Any(e => e.Faction == ExtractFaction.Scav);

        if (hasPmc && hasScav)
        {
            // PMC 탈출구 정보를 기준으로, PMC로 표시
            var representative = group.FirstOrDefault(e => e.Faction == ExtractFaction.Pmc)
                ?? group.FirstOrDefault(e => e.Faction == ExtractFaction.Shared)
                ?? group[0];
            return (representative, ExtractFaction.Pmc);
        }

        // Shared는 PMC로 처리
        var resultFaction = group[0].Faction == ExtractFaction.Shared ? ExtractFaction.Pmc : group[0].Faction;
        return (group[0], resultFaction);
    }
#pragma warning restore IDE0051

    #endregion

    #region Private Methods - Helpers

    private bool ShouldShowExtract(ExtractFaction faction)
    {
        return faction switch
        {
            ExtractFaction.Pmc => _showPmcExtracts,
            ExtractFaction.Scav => _showScavExtracts,
            ExtractFaction.Shared => _showPmcExtracts, // Shared도 PMC 필터 사용
            ExtractFaction.Transit => _showTransitExtracts,
            _ => true
        };
    }

    private bool IsMarkerOnCurrentFloor(string? markerFloorId)
    {
        // 단일 층 맵이거나 층 선택이 없는 경우: 모든 마커를 현재 층으로 간주
        if (string.IsNullOrEmpty(_currentFloorId))
            return true;

        // 마커에 층 정보가 없는 경우: 기본 층(main)으로 간주
        if (string.IsNullOrEmpty(markerFloorId))
        {
            // 현재 선택된 층이 main이면 표시, 아니면 다른 층으로 처리
            return string.Equals(_currentFloorId, "main", StringComparison.OrdinalIgnoreCase);
        }

        // 층 ID 비교 (대소문자 무시)
        return string.Equals(_currentFloorId, markerFloorId, StringComparison.OrdinalIgnoreCase);
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
