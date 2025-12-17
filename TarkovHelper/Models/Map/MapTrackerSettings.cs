using System.IO;

namespace TarkovHelper.Models.Map;

/// <summary>
/// 퀘스트 마커 표시 스타일
/// </summary>
public enum QuestMarkerStyle
{
    /// <summary>
    /// 기본 스타일 (아이콘만)
    /// </summary>
    Default = 0,

    /// <summary>
    /// 초록색 원 테두리
    /// </summary>
    GreenCircle = 1,

    /// <summary>
    /// 기본 스타일 + 퀘스트명
    /// </summary>
    DefaultWithName = 2,

    /// <summary>
    /// 초록색 원 + 퀘스트명
    /// </summary>
    GreenCircleWithName = 3
}

/// <summary>
/// 맵 트래커 전체 설정.
/// UserSettings DB 테이블에 저장됩니다.
/// </summary>
public sealed class MapTrackerSettings
{
    /// <summary>
    /// EFT 스크린샷 폴더 경로.
    /// 기본값: C:\Users\{현재사용자}\Documents\Escape from Tarkov\Screenshots
    /// </summary>
    public string ScreenshotFolderPath { get; set; } = GetDefaultScreenshotPath();

    /// <summary>
    /// 스크린샷 파일명 파싱용 정규식 패턴.
    /// 필수 그룹: x, y
    /// 선택 그룹: z, map, angle, qx, qy, qz, qw
    /// </summary>
    public string FileNamePattern { get; set; } = DefaultFileNamePattern;

    /// <summary>
    /// 파일 변경 감지 후 처리 대기 시간 (밀리초).
    /// </summary>
    public int DebounceDelayMs { get; set; } = 500;

    /// <summary>
    /// 맵 설정 목록 (map_configs.json에서 로드됨)
    /// </summary>
    public List<MapConfig> Maps { get; set; } = new();

    /// <summary>
    /// 퀘스트 마커 크기 (픽셀)
    /// </summary>
    public int MarkerSize { get; set; } = 12;

    /// <summary>
    /// 플레이어 위치 마커 크기 (픽셀)
    /// </summary>
    public int PlayerMarkerSize { get; set; } = 12;

    /// <summary>
    /// 마커 색상 (ARGB hex, 예: "#FFFF0000" = 빨간색)
    /// </summary>
    public string MarkerColor { get; set; } = "#FFFF5722";

    /// <summary>
    /// 방향 표시 여부
    /// </summary>
    public bool ShowDirection { get; set; } = true;

    /// <summary>
    /// 이동 경로 표시 여부
    /// </summary>
    public bool ShowTrail { get; set; } = true;

    /// <summary>
    /// 이동 경로 최대 포인트 수
    /// </summary>
    public int MaxTrailPoints { get; set; } = 50;

    /// <summary>
    /// PMC 탈출구 표시 여부
    /// </summary>
    public bool ShowPmcExtracts { get; set; } = true;

    /// <summary>
    /// Scav 탈출구 표시 여부
    /// </summary>
    public bool ShowScavExtracts { get; set; } = true;

    /// <summary>
    /// 탈출구 이름 텍스트 크기
    /// </summary>
    public double ExtractNameTextSize { get; set; } = 16.0;

    /// <summary>
    /// 퀘스트 마커 표시 여부
    /// </summary>
    public bool ShowQuestMarkers { get; set; } = true;

    /// <summary>
    /// 퀘스트 마커 스타일
    /// </summary>
    public QuestMarkerStyle QuestMarkerStyle { get; set; } = QuestMarkerStyle.Default;

    /// <summary>
    /// 퀘스트명 텍스트 크기
    /// </summary>
    public double QuestNameTextSize { get; set; } = 12.0;

    /// <summary>
    /// 탈출구 마커 표시 여부
    /// </summary>
    public bool ShowExtractMarkers { get; set; } = true;

    /// <summary>
    /// 완료된 퀘스트 목표 마커 숨기기 여부
    /// </summary>
    public bool HideCompletedObjectives { get; set; } = false;

    /// <summary>
    /// 마지막으로 선택한 맵 키 (탭 전환 시 상태 유지용)
    /// </summary>
    public string? LastSelectedMapKey { get; set; }

    /// <summary>
    /// 마지막 줌 레벨 (탭 전환 시 상태 유지용)
    /// </summary>
    public double LastZoomLevel { get; set; } = 1.0;

    /// <summary>
    /// 마지막 맵 X 위치 (탭 전환 시 상태 유지용)
    /// </summary>
    public double LastTranslateX { get; set; } = 0;

    /// <summary>
    /// 마지막 맵 Y 위치 (탭 전환 시 상태 유지용)
    /// </summary>
    public double LastTranslateY { get; set; } = 0;

    /// <summary>
    /// 퀘스트 마커 색상 커스터마이징 (타입별 Hex 색상)
    /// </summary>
    public Dictionary<string, string> MarkerColors { get; set; } = new()
    {
        { "visit", "#4CAF50" },      // Green
        { "mark", "#FF9800" },       // Orange
        { "plantItem", "#9C27B0" },  // Purple
        { "extract", "#2196F3" },    // Blue
        { "findItem", "#FFEB3B" }    // Yellow
    };

    /// <summary>
    /// 특정 타입의 마커 색상을 가져옵니다.
    /// </summary>
    public string GetMarkerColor(string objectiveType)
    {
        return MarkerColors.TryGetValue(objectiveType, out var color) ? color : "#4CAF50";
    }

    /// <summary>
    /// 특정 타입의 마커 색상을 설정합니다.
    /// </summary>
    public void SetMarkerColor(string objectiveType, string hexColor)
    {
        MarkerColors[objectiveType] = hexColor;
    }

    /// <summary>
    /// 기본 스크린샷 폴더 경로 반환.
    /// </summary>
    private static string GetDefaultScreenshotPath()
    {
        var detectedPath = TryDetectScreenshotFolder();
        if (!string.IsNullOrEmpty(detectedPath))
            return detectedPath;

        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documentsPath, "Escape from Tarkov", "Screenshots");
    }

    /// <summary>
    /// EFT 스크린샷 폴더를 자동으로 탐지합니다.
    /// </summary>
    public static string? TryDetectScreenshotFolder()
    {
        var eftFolderVariants = new[]
        {
            "Escape from Tarkov",
            "Escape From Tarkov",
            "escape from tarkov"
        };

        // 전략 1: MyDocuments 경로
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var variant in eftFolderVariants)
        {
            var path = Path.Combine(documentsPath, variant, "Screenshots");
            if (Directory.Exists(path))
                return path;
        }

        // 전략 2: UserProfile 경로 (OneDrive 등 리디렉션된 경우)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documentsFolders = new[] { "Documents", "문서", "My Documents" };

        foreach (var docFolder in documentsFolders)
        {
            foreach (var variant in eftFolderVariants)
            {
                var path = Path.Combine(userProfile, docFolder, variant, "Screenshots");
                if (Directory.Exists(path))
                    return path;
            }
        }

        // 전략 3: OneDrive 문서 폴더
        var oneDrivePath = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrivePath))
        {
            foreach (var docFolder in documentsFolders)
            {
                foreach (var variant in eftFolderVariants)
                {
                    var path = Path.Combine(oneDrivePath, docFolder, variant, "Screenshots");
                    if (Directory.Exists(path))
                        return path;

                    path = Path.Combine(oneDrivePath, variant, "Screenshots");
                    if (Directory.Exists(path))
                        return path;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 가능한 모든 스크린샷 폴더 경로를 반환합니다.
    /// </summary>
    public static List<string> GetPossibleScreenshotPaths()
    {
        var paths = new List<string>();

        var eftFolderVariants = new[]
        {
            "Escape from Tarkov",
            "Escape From Tarkov"
        };

        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var variant in eftFolderVariants)
        {
            var path = Path.Combine(documentsPath, variant, "Screenshots");
            if (Directory.Exists(path) && !paths.Contains(path))
                paths.Add(path);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var variant in eftFolderVariants)
        {
            var path = Path.Combine(userProfile, "Documents", variant, "Screenshots");
            if (Directory.Exists(path) && !paths.Contains(path))
                paths.Add(path);
        }

        var oneDrivePath = Environment.GetEnvironmentVariable("OneDrive");
        if (!string.IsNullOrEmpty(oneDrivePath))
        {
            foreach (var variant in eftFolderVariants)
            {
                var path = Path.Combine(oneDrivePath, "Documents", variant, "Screenshots");
                if (Directory.Exists(path) && !paths.Contains(path))
                    paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// 기본 파일명 패턴.
    /// EFT 스크린샷 파일명 형식 (쿼터니언):
    /// - 형식: "2025-12-04[00-40]_95.77, 2.44, -134.02_-0.02395, -0.85891, 0.03920, -0.51007_16.74 (0).png"
    /// </summary>
    private const string DefaultFileNamePattern =
        @"\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_(?<x>-?\d+\.?\d*),\s*(?<y>-?\d+\.?\d*),\s*(?<z>-?\d+\.?\d*)_(?<qx>-?\d+\.?\d*),\s*(?<qy>-?\d+\.?\d*),\s*(?<qz>-?\d+\.?\d*),\s*(?<qw>-?\d+\.?\d*)_";
}
