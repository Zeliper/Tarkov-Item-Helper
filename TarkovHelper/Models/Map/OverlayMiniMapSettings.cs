namespace TarkovHelper.Models.Map;

/// <summary>
/// 오버레이 미니맵 뷰 모드
/// </summary>
public enum MiniMapViewMode
{
    /// <summary>
    /// 고정 뷰 - 전체 맵 표시, 줌으로 조절
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// 플레이어 추적 뷰 - 플레이어가 항상 중앙
    /// </summary>
    PlayerTracking = 1
}

/// <summary>
/// 오버레이 미니맵 설정
/// </summary>
public sealed class OverlayMiniMapSettings
{
    /// <summary>
    /// 오버레이 활성화 여부
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 오버레이 X 위치 (화면 기준)
    /// </summary>
    public double PositionX { get; set; } = -1; // -1 = 자동 (우측 상단)

    /// <summary>
    /// 오버레이 Y 위치 (화면 기준)
    /// </summary>
    public double PositionY { get; set; } = -1; // -1 = 자동 (우측 상단)

    /// <summary>
    /// 오버레이 너비
    /// </summary>
    public double Width { get; set; } = 300;

    /// <summary>
    /// 오버레이 높이
    /// </summary>
    public double Height { get; set; } = 300;

    /// <summary>
    /// 투명도 (0.1 ~ 1.0)
    /// </summary>
    public double Opacity { get; set; } = 0.8;

    /// <summary>
    /// 줌 레벨 (0.25 ~ 4.0)
    /// </summary>
    public double ZoomLevel { get; set; } = 1.0;

    /// <summary>
    /// 뷰 모드 (고정/플레이어 추적)
    /// </summary>
    public MiniMapViewMode ViewMode { get; set; } = MiniMapViewMode.Fixed;

    /// <summary>
    /// Click-through 모드 (마우스 클릭 통과)
    /// </summary>
    public bool ClickThrough { get; set; } = false;

    /// <summary>
    /// 퀘스트 마커 표시 여부
    /// </summary>
    public bool ShowQuestMarkers { get; set; } = true;

    /// <summary>
    /// 탈출구 마커 표시 여부
    /// </summary>
    public bool ShowExtractMarkers { get; set; } = true;

    /// <summary>
    /// 맵 오프셋 X (고정 뷰 모드에서 팬 위치)
    /// </summary>
    public double MapOffsetX { get; set; } = 0;

    /// <summary>
    /// 맵 오프셋 Y (고정 뷰 모드에서 팬 위치)
    /// </summary>
    public double MapOffsetY { get; set; } = 0;

    /// <summary>
    /// 최소 너비
    /// </summary>
    public const double MinWidth = 200;

    /// <summary>
    /// 최대 너비
    /// </summary>
    public const double MaxWidth = 800;

    /// <summary>
    /// 최소 높이
    /// </summary>
    public const double MinHeight = 200;

    /// <summary>
    /// 최대 높이
    /// </summary>
    public const double MaxHeight = 800;

    /// <summary>
    /// 최소 투명도
    /// </summary>
    public const double MinOpacity = 0.1;

    /// <summary>
    /// 최대 투명도
    /// </summary>
    public const double MaxOpacity = 1.0;

    /// <summary>
    /// 최소 줌 레벨 (큰 맵이 작은 창에 맞도록 0.01까지 허용)
    /// </summary>
    public const double MinZoom = 0.01;

    /// <summary>
    /// 최대 줌 레벨
    /// </summary>
    public const double MaxZoom = 4.0;

    /// <summary>
    /// 줌 단계 (작은 줌 레벨에서도 적절하도록)
    /// </summary>
    public const double ZoomStep = 0.05;

    /// <summary>
    /// 기본 설정으로 초기화
    /// </summary>
    public void ResetToDefaults()
    {
        Enabled = false;
        PositionX = -1;
        PositionY = -1;
        Width = 300;
        Height = 300;
        Opacity = 0.8;
        ZoomLevel = 1.0;
        ViewMode = MiniMapViewMode.Fixed;
        ClickThrough = false;
        ShowQuestMarkers = true;
        ShowExtractMarkers = true;
        MapOffsetX = 0;
        MapOffsetY = 0;
    }

    /// <summary>
    /// 줌 레벨 증가
    /// </summary>
    public void ZoomIn()
    {
        ZoomLevel = Math.Min(MaxZoom, ZoomLevel + ZoomStep);
    }

    /// <summary>
    /// 줌 레벨 감소
    /// </summary>
    public void ZoomOut()
    {
        ZoomLevel = Math.Max(MinZoom, ZoomLevel - ZoomStep);
    }

    /// <summary>
    /// 뷰 모드 토글
    /// </summary>
    public void ToggleViewMode()
    {
        ViewMode = ViewMode == MiniMapViewMode.Fixed
            ? MiniMapViewMode.PlayerTracking
            : MiniMapViewMode.Fixed;
    }

    /// <summary>
    /// Click-through 모드 토글
    /// </summary>
    public void ToggleClickThrough()
    {
        ClickThrough = !ClickThrough;
    }

    /// <summary>
    /// 다른 설정에서 값 복사
    /// </summary>
    public void CopyFrom(OverlayMiniMapSettings other)
    {
        Opacity = other.Opacity;
        ZoomLevel = other.ZoomLevel;
        ViewMode = other.ViewMode;
        ClickThrough = other.ClickThrough;
        ShowQuestMarkers = other.ShowQuestMarkers;
        ShowExtractMarkers = other.ShowExtractMarkers;
    }

    /// <summary>
    /// 설정 복사본 생성
    /// </summary>
    public OverlayMiniMapSettings Clone()
    {
        return new OverlayMiniMapSettings
        {
            Enabled = Enabled,
            PositionX = PositionX,
            PositionY = PositionY,
            Width = Width,
            Height = Height,
            Opacity = Opacity,
            ZoomLevel = ZoomLevel,
            ViewMode = ViewMode,
            ClickThrough = ClickThrough,
            ShowQuestMarkers = ShowQuestMarkers,
            ShowExtractMarkers = ShowExtractMarkers,
            MapOffsetX = MapOffsetX,
            MapOffsetY = MapOffsetY
        };
    }
}
