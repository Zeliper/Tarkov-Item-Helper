using System.IO;

namespace TarkovHelper.Models.MapTracker;

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
/// Data/map_tracker_settings.json 파일에 저장됩니다.
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
    ///
    /// [패턴 수정 가이드]
    /// 실제 파일명 형식에 맞게 수정하세요.
    ///
    /// 예시 파일명: "2023-09-22[13-00]_-49.9, 12.1, -51.8_0.0, -0.8, 0.1, -0.5_14.08.png"
    /// </summary>
    public string FileNamePattern { get; set; } = DefaultFileNamePattern;

    /// <summary>
    /// 파일 변경 감지 후 처리 대기 시간 (밀리초).
    /// 파일 쓰기 완료를 기다리는 디바운싱 시간입니다.
    /// </summary>
    public int DebounceDelayMs { get; set; } = 500;

    /// <summary>
    /// 맵 설정 목록
    /// </summary>
    public List<MapConfig> Maps { get; set; } = GetDefaultMaps();

    /// <summary>
    /// 퀘스트 마커 크기 (픽셀)
    /// </summary>
    public int MarkerSize { get; set; } = 16;

    /// <summary>
    /// 플레이어 위치 마커 크기 (픽셀)
    /// </summary>
    public int PlayerMarkerSize { get; set; } = 16;

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
    public double ExtractNameTextSize { get; set; } = 10.0;

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
    /// 기본 스크린샷 폴더 경로 반환.
    /// 여러 경로를 시도하여 실제 존재하는 폴더를 찾습니다.
    /// </summary>
    private static string GetDefaultScreenshotPath()
    {
        var detectedPath = TryDetectScreenshotFolder();
        if (!string.IsNullOrEmpty(detectedPath))
            return detectedPath;

        // 폴백: 기본 경로 반환
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documentsPath, "Escape from Tarkov", "Screenshots");
    }

    /// <summary>
    /// EFT 스크린샷 폴더를 자동으로 탐지합니다.
    /// 여러 경로 전략과 대소문자 변형을 시도합니다.
    /// </summary>
    /// <returns>탐지된 폴더 경로, 없으면 null</returns>
    public static string? TryDetectScreenshotFolder()
    {
        // EFT 폴더 이름 변형 (대소문자)
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

                    // OneDrive 루트에 바로 있는 경우
                    path = Path.Combine(oneDrivePath, variant, "Screenshots");
                    if (Directory.Exists(path))
                        return path;
                }
            }
        }

        // 전략 4: 일반 드라이브에서 EFT 폴더 탐색 (C:, D:, E:)
        var drives = new[] { "C:", "D:", "E:" };
        var commonPaths = new[]
        {
            @"Users\{user}\Documents",
            @"Games",
            @"Program Files\Battlestate Games",
            @"Battlestate Games"
        };

        var userName = Environment.UserName;
        foreach (var drive in drives)
        {
            foreach (var commonPath in commonPaths)
            {
                var basePath = Path.Combine(drive + "\\", commonPath.Replace("{user}", userName));
                foreach (var variant in eftFolderVariants)
                {
                    var path = Path.Combine(basePath, variant, "Screenshots");
                    try
                    {
                        if (Directory.Exists(path))
                            return path;
                    }
                    catch
                    {
                        // 권한 오류 무시
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 가능한 모든 스크린샷 폴더 경로를 반환합니다.
    /// UI에서 선택지로 제공할 수 있습니다.
    /// </summary>
    public static List<string> GetPossibleScreenshotPaths()
    {
        var paths = new List<string>();

        var eftFolderVariants = new[]
        {
            "Escape from Tarkov",
            "Escape From Tarkov"
        };

        // MyDocuments
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var variant in eftFolderVariants)
        {
            var path = Path.Combine(documentsPath, variant, "Screenshots");
            if (Directory.Exists(path) && !paths.Contains(path))
                paths.Add(path);
        }

        // UserProfile Documents
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var variant in eftFolderVariants)
        {
            var path = Path.Combine(userProfile, "Documents", variant, "Screenshots");
            if (Directory.Exists(path) && !paths.Contains(path))
                paths.Add(path);
        }

        // OneDrive
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
    ///
    /// [지원되는 그룹]
    /// - x, y: 좌표 (필수)
    /// - z: 높이 (선택)
    /// - map: 맵 이름 (선택 - 없으면 "Unknown")
    /// - angle: 방향 각도 (선택)
    /// - qx, qy, qz, qw: 쿼터니언 회전값 (선택 - angle 대신 사용 가능)
    /// </summary>
    private const string DefaultFileNamePattern =
        @"\d{4}-\d{2}-\d{2}\[\d{2}-\d{2}\]_(?<x>-?\d+\.?\d*),\s*(?<y>-?\d+\.?\d*),\s*(?<z>-?\d+\.?\d*)_(?<qx>-?\d+\.?\d*),\s*(?<qy>-?\d+\.?\d*),\s*(?<qz>-?\d+\.?\d*),\s*(?<qw>-?\d+\.?\d*)_";

    /// <summary>
    /// 기본 맵 설정 목록 생성.
    /// tarkov.dev의 maps.json 데이터 기반.
    /// SVG 파일 사용, Transform 좌표 변환 방식.
    /// </summary>
    private static List<MapConfig> GetDefaultMaps()
    {
        // tarkov.dev maps.json 데이터 기반
        // Transform: [scaleX, marginX, scaleY, marginY]
        // SvgBounds: [[maxLat, minLng], [minLat, maxLng]]
        return new List<MapConfig>
        {
            new()
            {
                Key = "Woods",
                DisplayName = "Woods",
                ImagePath = "Assets/Maps/Woods_tarkovdev.svg",
                ImageWidth = 1402,
                ImageHeight = 1421,
                Transform = [0.1855, 113.1, 0.1855, 167.8],
                CoordinateRotation = 180,
                SvgBounds = [[650, -945], [-695, 470]],
                Aliases = new List<string> { "woods", "WOODS" }
            },
            new()
            {
                Key = "Customs",
                DisplayName = "Customs",
                ImagePath = "Assets/Maps/Customs_tarkovdev.svg",
                ImageWidth = 1062,
                ImageHeight = 535,
                Transform = [0.239, 168.65, 0.239, 136.35],
                CoordinateRotation = 180,
                SvgBounds = [[698, -307], [-372, 237]],
                Aliases = new List<string> { "customs", "CUSTOMS", "bigmap" }
            },
            new()
            {
                Key = "Shoreline",
                DisplayName = "Shoreline",
                ImagePath = "Assets/Maps/Shoreline_tarkovdev.svg",
                ImageWidth = 1560,
                ImageHeight = 1032,
                Transform = [0.16, 83.2, 0.16, 111.1],
                CoordinateRotation = 180,
                SvgBounds = [[508, -415], [-1060, 618]],
                Aliases = new List<string> { "shoreline", "SHORELINE" }
            },
            new()
            {
                Key = "Interchange",
                DisplayName = "Interchange",
                ImagePath = "Assets/Maps/Interchange_tarkovdev.svg",
                ImageWidth = 977,
                ImageHeight = 977,
                Transform = [0.265, 150.6, 0.265, 134.6],
                CoordinateRotation = 180,
                SvgBounds = [[532.75, -442.75], [-364, 453.5]],
                Aliases = new List<string> { "interchange", "INTERCHANGE" }
            },
            new()
            {
                Key = "Reserve",
                DisplayName = "Reserve",
                ImagePath = "Assets/Maps/Reserve_tarkovdev.svg",
                ImageWidth = 827,
                ImageHeight = 761,
                Transform = [0.395, 122.0, 0.395, 137.65],
                CoordinateRotation = 180,
                SvgBounds = [[289, -338], [-303, 336]],
                Aliases = new List<string> { "reserve", "RESERVE", "RezervBase" }
            },
            new()
            {
                Key = "Lighthouse",
                DisplayName = "Lighthouse",
                ImagePath = "Assets/Maps/Lighthouse_tarkovdev.svg",
                ImageWidth = 1059,
                ImageHeight = 1723,
                Transform = [0.2, 0, 0.2, 0],
                CoordinateRotation = 180,
                SvgBounds = [[515, -998], [-545, 725]],
                Aliases = new List<string> { "lighthouse", "LIGHTHOUSE" }
            },
            new()
            {
                Key = "StreetsOfTarkov",
                DisplayName = "Streets of Tarkov",
                ImagePath = "Assets/Maps/StreetsOfTarkov_tarkovdev.svg",
                ImageWidth = 605,
                ImageHeight = 832,
                Transform = [0.38, 0, 0.38, 0],
                CoordinateRotation = 180,
                SvgBounds = [[323, -317], [-280, 554]],
                Aliases = new List<string> { "streets", "STREETS", "TarkovStreets", "streets-of-tarkov" }
            },
            new()
            {
                Key = "Factory",
                DisplayName = "Factory",
                ImagePath = "Assets/Maps/Factory_tarkovdev.svg",
                ImageWidth = 655 * 5,
                ImageHeight = 706 * 5,
                Transform = [1.629 * 5, 119.9 * 5, 1.629 * 5, 139.3 * 5],
                CoordinateRotation = 90,
                SvgBounds = [[79, -64.5], [-66.5, 67.4]],
                Aliases = new List<string> { "factory", "FACTORY", "factory4_day", "factory4_night" },
                MarkerScale = 5.0
            },
            new()
            {
                Key = "GroundZero",
                DisplayName = "Ground Zero",
                ImagePath = "Assets/Maps/GroundZero_tarkovdev.svg",
                ImageWidth = 1047 * 3,
                ImageHeight = 1465 * 3,
                Transform = [0.524 * 3, 167.3 * 3, 0.524 * 3, 65.1 * 3],
                CoordinateRotation = 180,
                SvgBounds = [[249, -124], [-99, 364]],
                Aliases = new List<string> { "groundzero", "GROUNDZERO", "Sandbox", "sandbox", "ground-zero", "ground-zero-21" },
                MarkerScale = 3.0
            },
            new()
            {
                Key = "Labs",
                DisplayName = "The Lab",
                ImagePath = "Assets/Maps/Labs_tarkovdev.svg",
                ImageWidth = 720,
                ImageHeight = 586,
                Transform = [0.575, 281.2, 0.575, 193.7],
                CoordinateRotation = 270,
                SvgBounds = [[-80, -477], [-287, -193]],
                Aliases = new List<string> { "labs", "LABS", "laboratory", "the-lab" }
            }
        };
    }
}
