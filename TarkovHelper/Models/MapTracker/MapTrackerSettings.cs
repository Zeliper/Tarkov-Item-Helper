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
    /// 트랜짓 마커 표시 여부
    /// </summary>
    public bool ShowTransitMarkers { get; set; } = true;

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
    /// tarkov.dev의 maps.json 데이터 기반, Tarkov Market SVG 맵에 맞게 조정됨.
    /// SVG 파일 사용, Transform 좌표 변환 방식.
    ///
    /// [IDW 보정 시스템]
    /// - CalibratedTransform: affine 변환 행렬 [a, b, c, d, tx, ty]
    /// - CalibrationPoints: IDW 보정용 기준점 목록
    /// - 보정 포인트 근처에서는 정확한 위치, 멀리서는 affine 변환 사용
    /// </summary>
    private static List<MapConfig> GetDefaultMaps()
    {
        return new List<MapConfig>
        {
            // Woods: Calibrated from 9 reference points
            new()
            {
                Key = "Woods",
                DisplayName = "Woods",
                ImagePath = "Assets/Maps/Woods.svg",
                ImageWidth = 4800,
                ImageHeight = 4800,
                CalibratedTransform = [-3.394478979463914, -0.00672470985271966, 0.028968077414678935, 3.3624781094363794, 2073.690129251411, 3161.542755683878],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "6145baca210bf473bb0ce242a54ce1fe82df9bfe", Name = "Railway Bridge to Tarkov", GameX = -730.43, GameZ = 129.32, ScreenX = 4507.261499135629, ScreenY = 3581.574058462227 },
                    new() { Id = "e6fd5a732d1fd0662221d75f31f0e2e8021ef4ca", Name = "UN Roadblock", GameX = -536.1243, GameZ = 286.7704, ScreenX = 3888.4934342540687, ScreenY = 4145.467383053658 },
                    new() { Id = "3f08df8a92b8d58e1011741f86a65c317180fef0", Name = "RUAF Gate", GameX = -141.730011, GameZ = 446.8, ScreenX = 2557.08951525033, ScreenY = 4566.385667830957 },
                    new() { Id = "e70d314d202e673b96671e810e54cef5657c16ca", Name = "Outskirts", GameX = 349.035645, GameZ = 358.580383, ScreenX = 887.8453252552437, ScreenY = 4418.023161101267 },
                    new() { Id = "e0dbb9eb8a01eadb9e4610d53ecd65ccd320336c", Name = "Power Line Passage (Flare)", GameX = 573.985657, GameZ = -85.2596, ScreenX = 90.20549372179698, ScreenY = 2874.8954055545114 },
                    new() { Id = "64f88a7c564eb38938c7b679f89ff265dfbf2685", Name = "Bridge V-Ex", GameX = -485.325745, GameZ = -504.061432, ScreenX = 3724.5698845364277, ScreenY = 1425.936716102201 },
                    new() { Id = "98dfbaebc7bedea817c9ea5ec2e36f42a36b7ebf", Name = "Northern UN Roadblock", GameX = -557.324341, GameZ = -67.0496, ScreenX = 3979.126270775343, ScreenY = 2919.6050236556907 },
                    new() { Id = "96ddc4bf8688f90f215226fd5788e1291ed13bfd", Name = "ZB-014", GameX = 447.24, GameZ = 57.46, ScreenX = 577.2153260768847, ScreenY = 3384.210512329618 },
                    new() { Id = "3c768283f1012c72aa4140252f44c538448eafa7", Name = "ZB-016", GameX = -389.159973, GameZ = 11.0449905, ScreenX = 3436.4610561661466, ScreenY = 3225.6920532843783 }
                },
                Aliases = new List<string> { "woods", "WOODS" }
            },
            // Customs: Calibrated from 10 reference points
            new()
            {
                Key = "Customs",
                DisplayName = "Customs",
                ImagePath = "Assets/Maps/Customs.svg",
                ImageWidth = 4400,
                ImageHeight = 3200,
                CalibratedTransform = [-3.6649134212117196, 0.2964105833041642, -0.09212328415752055, 4.934946350706538, 2940.3734739766946, 1626.687757738243],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "311ae28f72d9c9fe3be801b2d503088167e325c3", Name = "Smugglers' Boat", GameX = -41.5100021, GameZ = 122.67, ScreenX = 3077.4729835280586, ScreenY = 2264.1290736442556 },
                    new() { Id = "7c993e82fe52ccedbdf9591f92e3bf9fe4352a4e", Name = "Crossroads", GameX = -334.804016, GameZ = -87.97917, ScreenX = 4114.422492879879, ScreenY = 1185.629454497245 },
                    new() { Id = "e0e61c2ed23ae5387b02dfc1acb88dab0c83ff95", Name = "Old Gas Station", GameX = 309.499634, GameZ = -174.320175, ScreenX = 1795.6775665485686, ScreenY = 647.0758499782642 },
                    new() { Id = "b051486cb850a811e354ac34b2ec5790bd2a12c5", Name = "Railroad Passage (Flare)", GameX = 139.592712, GameZ = -324.7145, ScreenX = 2226.795658734746, ScreenY = 250.61668881078458 },
                    new() { Id = "feafaf9787defedcb6b7ea3da97f342870bc8073", Name = "Dorms V-Ex", GameX = 181.08, GameZ = 213.25, ScreenX = 2343.3793799705145, ScreenY = 2750.5968529435104 },
                    new() { Id = "b113904d09c836e3b6fb937ebd1201f30a5900e6", Name = "RUAF Roadblock", GameX = -10.250061, GameZ = -138.450043, ScreenX = 2997.7568112207655, ScreenY = 904.7830987995399 },
                    new() { Id = "0df2e62edcfb6e0f1c36fd7be585a5850dd83eed", Name = "Trailer Park", GameX = -313.720276, GameZ = -233.280121, ScreenX = 4065.418836323075, ScreenY = 448.34263122249587 },
                    new() { Id = "02cdf102e32c481d3b5fabcf1dcf3df3962522bb", Name = "ZB-1011", GameX = 621.4962, GameZ = -128.604919, ScreenX = 615.129400802532, ScreenY = 901.9988093519005 },
                    new() { Id = "908b7d419bb7152858c973b561a1ee7ed5f55a81", Name = "Smugglers' Bunker (ZB-1012)", GameX = 463.18, GameZ = -112.36, ScreenX = 1216.802358342799, ScreenY = 989.5474178139523 },
                    new() { Id = "d0781374be523027a2a8e1edf6fe08449c45c382", Name = "ZB-013", GameX = 200.9755, GameZ = -153.086456, ScreenX = 2194.6188985019653, ScreenY = 793.9527265768451 }
                },
                Aliases = new List<string> { "customs", "CUSTOMS", "bigmap" }
            },
            // Shoreline: Calibrated from 10 reference points
            new()
            {
                Key = "Shoreline",
                DisplayName = "Shoreline",
                ImagePath = "Assets/Maps/Shoreline.svg",
                ImageWidth = 3700,
                ImageHeight = 3100,
                CalibratedTransform = [-2.147451106044105, 0.015949683724444705, -0.0062445432176331545, 2.8002156918819003, 1256.0507944168528, 1290.7979930779154],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "11890c73b821b558b2198881ae9659c426a6b289", Name = "Path to Lighthouse", GameX = 448.9, GameZ = -254.6, ScreenX = 321.902269251867, ScreenY = 605.1816710498708 },
                    new() { Id = "efc0e77c1e0a83ff35aa27d8a091e31e770338ec", Name = "Climber's Trail", GameX = -214.3, GameZ = -361.8, ScreenX = 1686.1326717985007, ScreenY = 269.423369565235 },
                    new() { Id = "b4ff87efb1c1450520552d27addab7793688d475", Name = "Mountain Bunker", GameX = -390.956024, GameZ = -385.721, ScreenX = 2106.553972182023, ScreenY = 206.05901805051087 },
                    new() { Id = "e95d3547939574dfb73ce20064c5aff811bf02ea", Name = "Road to North V-Ex", GameX = -543.264038, GameZ = -379.651367, ScreenX = 2421.4827012065953, ScreenY = 222.7817325910521 },
                    new() { Id = "c5ae90597356b707d9d1d9522efe7a24fe7d4b71", Name = "Smugglers' Path (Co-op)", GameX = -729.4, GameZ = -255.1, ScreenX = 2785.5454598002066, ScreenY = 594.692665132328 },
                    new() { Id = "97440eb4ab5a337942dc616f5dde5905adadd627", Name = "Road to Customs", GameX = -859.05, GameZ = 0.49, ScreenX = 3111.1545877105737, ScreenY = 1288.413945512897 },
                    new() { Id = "57d9c15c96a8221867b52676d889e26a994b9512", Name = "Railway Bridge", GameX = -1029.29, GameZ = 307.59, ScreenX = 3481.3271447561056, ScreenY = 2160.4456613387797 },
                    new() { Id = "d21fd4e674ea08152c99c97fbe031b71269c58d6", Name = "Pier Boat", GameX = -332.5907, GameZ = 561.2576, ScreenX = 1989.4730147025382, ScreenY = 2885.266542154125 },
                    new() { Id = "52b4672a43e470435ec4722b09571d0bf7724f7e", Name = "Lighthouse", GameX = -458.1507, GameZ = 567.2876, ScreenX = 2250.6354103254102, ScreenY = 2881.805511990237 },
                    new() { Id = "5c32b0d555293798312b3a4bd805b34987b5f576", Name = "Tunnel", GameX = 376.36, GameZ = 319.25, ScreenX = 421.9311000259181, ScreenY = 2150.4464345756137 }
                },
                Aliases = new List<string> { "shoreline", "SHORELINE" }
            },
            // Interchange: Calibrated from 3 reference points
            new()
            {
                Key = "Interchange",
                DisplayName = "Interchange",
                ImagePath = "Assets/Maps/Interchange.svg",
                ImageWidth = 4000,
                ImageHeight = 3900,
                CalibratedTransform = [-4.3629135184885035, -0.46787647488898804, 0.01740221516511105, 4.08171562795115, 2309.0213766878187, 1987.6213844573472],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "fdc29c48395f3b30a7b2ba8ed645cbebfe69e539", Name = "Railway Exfil", GameX = 472.32, GameZ = -429.74, ScreenX = 449.3952999541217, ScreenY = 241.76432476840506 },
                    new() { Id = "2a589e01386c564a296894e93a3aaf18ec53207d", Name = "Emercom Checkpoint", GameX = -321.56, GameZ = 266.74, ScreenX = 3587.1584767810928, ScreenY = 3070.7823547485436 },
                    new() { Id = "1abefb699e1a8304defcd8b41dcbbeee14adde52", Name = "Power Station V-Ex", GameX = -251.92, GameZ = -367.13, ScreenX = 3579.8980404914364, ScreenY = 484.7171599232468 }
                },
                Aliases = new List<string> { "interchange", "INTERCHANGE" },
                Floors = new List<MapFloorConfig>
                {
                    new() { LayerId = "main", DisplayName = "Ground Floor", Order = 0, IsDefault = true },
                    new() { LayerId = "level2", DisplayName = "Level 2", Order = 1 },
                    new() { LayerId = "level3", DisplayName = "Level 3", Order = 2 }
                }
            },
            // Reserve: Calibrated from 5 reference points
            new()
            {
                Key = "Reserve",
                DisplayName = "Reserve",
                ImagePath = "Assets/Maps/Reserve.svg",
                ImageWidth = 3200,
                ImageHeight = 3000,
                CalibratedTransform = [-4.659551818591078, 1.4968674164044333, 1.4130686420734615, 6.194225988393516, 1605.6973392433374, 1531.5214718745203],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "bf8015e63fa543ce72fe16f5c3476be29ba80fcc", Name = "Bunker Hermetic Door", GameX = 61.9206619, GameZ = -190.541931, ScreenX = 984.6170059859683, ScreenY = 229.8934942794897 },
                    new() { Id = "adfd835074e6d2abf54127650c036eb1975e0c40", Name = "Heating Pipe", GameX = -35.26, GameZ = -180.12, ScreenX = 1526.4728075020091, ScreenY = 474.94102776430697 },
                    new() { Id = "c5a30892ba8b44107663736aaeca1a3f7ff61391", Name = "Armored Train", GameX = 144.986, GameZ = -147.352, ScreenX = 746.3142414481119, ScreenY = 935.3362610489966 },
                    new() { Id = "4cd3f8f23a3092756fb509287ccc5332aeea6453", Name = "Sewer Manhole", GameX = 40.18, GameZ = 76.45, ScreenX = 1500.1236024311852, ScreenY = 2071.4646994792292 },
                    new() { Id = "6eb9d336eb71fbf68c7d809b55dec62ec89e7627", Name = "Cliff Descent", GameX = -9.24, GameZ = 208.43, ScreenX = 1978.025404998793, ScreenY = 2788.1564818572683 }
                },
                Aliases = new List<string> { "reserve", "RESERVE", "RezervBase" },
                Floors = new List<MapFloorConfig>
                {
                    new() { LayerId = "bunker", DisplayName = "Bunker", Order = -1 },
                    new() { LayerId = "main", DisplayName = "Ground Floor", Order = 0, IsDefault = true },
                    new() { LayerId = "level2", DisplayName = "Level 2", Order = 1 },
                    new() { LayerId = "level3", DisplayName = "Level 3", Order = 2 },
                    new() { LayerId = "level4", DisplayName = "Level 4", Order = 3 },
                    new() { LayerId = "level5", DisplayName = "Level 5", Order = 4 }
                }
            },
            // Lighthouse: Calibrated from 8 reference points
            new()
            {
                Key = "Lighthouse",
                DisplayName = "Lighthouse",
                ImagePath = "Assets/Maps/Lighthouse.svg",
                ImageWidth = 3100,
                ImageHeight = 3700,
                CalibratedTransform = [-2.90420012444279, -0.03695247261865274, 0.07784072176584508, 2.177892974033182, 1528.3789482998613, 2306.9268299580804],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "7d6c864aa862997ba573be0fcf47b118e253d1e1", Name = "Southern Road", GameX = -295.8, GameZ = 420.6, ScreenX = 2385.1644476669867, ScreenY = 3202.731493671181 },
                    new() { Id = "a04ce346b08f0cea9faebb65d86a7b7797e2f25d", Name = "Side Tunnel (Co-Op)", GameX = -68.3, GameZ = 318.11, ScreenX = 1713.8989082419782, ScreenY = 3004.405647798567 },
                    new() { Id = "d76677800a142f800df6709b3b22946aeff43253", Name = "Mountain Pass", GameX = -172.35, GameZ = -6.39, ScreenX = 2018.196078053299, ScreenY = 2281.848882204766 },
                    new() { Id = "2cdd9d1604ebe77b3fa8bf1461530ca5b3551d8f", Name = "Path to Shoreline", GameX = -364.4, GameZ = -121.4, ScreenX = 2587.7655394250087, ScreenY = 1987.3916130929817 },
                    new() { Id = "86689a1e06268028c220b923fd7c676d7f9497dd", Name = "Passage by the Lake", GameX = -360, GameZ = -564.87, ScreenX = 2576.876065145296, ScreenY = 1052.882517332594 },
                    new() { Id = "39a8452ee1c2a3b68737781843c98e69ed1a59aa", Name = "Road to Military Base V-Ex", GameX = -328.951263, GameZ = -784.3581, ScreenX = 2529.599060519887, ScreenY = 593.4206767463497 },
                    new() { Id = "3a8cfedc77147d2e438f8a2c55a01ae3d6defb25", Name = "Armored Train", GameX = 6.27001953, GameZ = -873.78, ScreenX = 1547.2564821175263, ScreenY = 387.5453903573669 },
                    new() { Id = "000e0a2d35821648d27c6479d9358b0d227c6804", Name = "Northern Checkpoint", GameX = 113.22, GameZ = -989.23, ScreenX = 1234.4782373467715, ScreenY = 165.3459168333497 }
                },
                Aliases = new List<string> { "lighthouse", "LIGHTHOUSE" }
            },
            // Streets: Calibrated from 10 reference points
            new()
            {
                Key = "StreetsOfTarkov",
                DisplayName = "Streets of Tarkov",
                ImagePath = "Assets/Maps/StreetsOfTarkov.svg",
                ImageWidth = 3260,
                ImageHeight = 3500,
                CalibratedTransform = [-4.9755636069074605, 0.17678253000661656, -0.04605367100221113, 4.532788724198274, 1635.6669035346636, 1040.0215325356564],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "b17b3bec8b22bdd99d6e9589ed0d9a41f2f6aac8", Name = "Expo Checkpoint", GameX = 213.12, GameZ = -104.96, ScreenX = 592.2270278087038, ScreenY = 493.74562392279057 },
                    new() { Id = "cb867ea360a89dbc48694c43d193553d564529f9", Name = "Stylobate Building Elevator", GameX = -44.7435, GameZ = -72.15, ScreenX = 1954.801827227824, ScreenY = 625.5883334520674 },
                    new() { Id = "19b2c9157d56629da496df0bc42dbe5c49439e4c", Name = "Klimov Street (Flare)", GameX = -263.41, GameZ = 43.19, ScreenX = 2935.8111775141024, ScreenY = 1210.6144305042953 },
                    new() { Id = "2f79c92d99215405c202d8b7f1e6eace137f9b5f", Name = "Klimov Shopping Mall Exfil", GameX = -163.821014, GameZ = -5.65802, ScreenX = 2286.473287813365, ScreenY = 1279.1981590099547 },
                    new() { Id = "974e40db5a7ac4d1937688eb6415b2333f9a3766", Name = "Sewer River", GameX = -267.04, GameZ = 219.48, ScreenX = 3015.058505883944, ScreenY = 1958.6455775993406 },
                    new() { Id = "29cd42c5025edb6a619dd961fbc20ad639127189", Name = "Damaged House", GameX = -248.971, GameZ = 344.275, ScreenX = 2984.6441020824113, ScreenY = 2601.6607052275526 },
                    new() { Id = "65f6067829924e26e0cb31d4ac24fee1870ec944", Name = "Courtyard", GameX = -148.947, GameZ = 500.151978, ScreenX = 2507.209269209614, ScreenY = 3300.4035727558653 },
                    new() { Id = "264ee7ea338658549942113cbbf0a6ea31402975", Name = "Primorsky Ave Taxi V-Ex", GameX = -2.191315, GameZ = 461.232971, ScreenX = 1700.2340772251182, ScreenY = 3137.227782433984 },
                    new() { Id = "99e05bbf5d6f6dad97773774af735e37ae3edd44", Name = "Crash Site", GameX = 312.74, GameZ = 405.96, ScreenX = 108.43926082541756, ScreenY = 2919.6437725758583 },
                    new() { Id = "59f13279288d9ac630955c14dc3368b492061e7c", Name = "Collapsed Crane", GameX = 216.12, GameZ = 272.7, ScreenX = 612.7032571401758, ScreenY = 2248.459183130191 }
                },
                Aliases = new List<string> { "streets", "STREETS", "TarkovStreets", "streets-of-tarkov" }
            },
            // Factory: Calibrated from 4 reference points
            new()
            {
                Key = "Factory",
                DisplayName = "Factory",
                ImagePath = "Assets/Maps/Factory.svg",
                ImageWidth = 3600,
                ImageHeight = 3600,
                CalibratedTransform = [0.2798878415258037, -21.627674282585662, -22.72576612862695, 0.024192513858156503, 1780.53763466516, 1900.045614685979],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "7ae0a08942ce71d8d713ef3628c86619598160f4", Name = "Gate 3", GameX = 58.43222, GameZ = 63.29811, ScreenX = 431.79521305249887, ScreenY = 609.3579554763538 },
                    new() { Id = "4f953558d3d7bca7bd7b4d40a6429adc34cc1834", Name = "Gate 0", GameX = -63.66578, GameZ = 55.8781128, ScreenX = 551.3491129440645, ScreenY = 3322.0724829900637 },
                    new() { Id = "7bb46d641d59983a58440a7a58d65933d981c211", Name = "Med Tent Gate", GameX = -17.5257778, GameZ = -61.27189, ScreenX = 3104.439124319388, ScreenY = 2330.2033903158385 },
                    new() { Id = "ffb8b37a6390d2c3b00baeee3295492ea1e19a93", Name = "Cellars", GameX = 73.89422, GameZ = -29.0818882, ScreenX = 2425.516676313653, ScreenY = 177.16654345998157 }
                },
                Aliases = new List<string> { "factory", "FACTORY", "factory4_day", "factory4_night" },
                MarkerScale = 5.0,
                Floors = new List<MapFloorConfig>
                {
                    new() { LayerId = "basement", DisplayName = "Basement", Order = -1 },
                    new() { LayerId = "main", DisplayName = "Ground Floor", Order = 0, IsDefault = true },
                    new() { LayerId = "level2", DisplayName = "Level 2", Order = 1 },
                    new() { LayerId = "level3", DisplayName = "Level 3", Order = 2 }
                }
            },
            // GroundZero: Calibrated from 5 reference points
            new()
            {
                Key = "GroundZero",
                DisplayName = "Ground Zero",
                ImagePath = "Assets/Maps/GroundZero.svg",
                ImageWidth = 2800,
                ImageHeight = 3100,
                CalibratedTransform = [-6.95950558606309, 0.1838843128357617, 1.7246964250980257, 5.988803108076605, 2088.258569967093, 789.6971540291354],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "89ff19116feeafdf4a2b4e538cafb5364ffbe862", Name = "Mira Ave (Flare)", GameX = 218.225, GameZ = -38.5065842, ScreenX = 557.093360891625, ScreenY = 1029.5363583455805 },
                    new() { Id = "549ceec177747468ebe86bcfd5a486d1dce9ed64", Name = "Emercom Checkpoint", GameX = 151.625, GameZ = -97.45658, ScreenX = 1020.3546011631084, ScreenY = 328.355196886363 },
                    new() { Id = "118fa7e3eae47190871eb4ea20d4f3ca2718b73f", Name = "Scav Checkpoint (Co-op)", GameX = 25.2195644, GameZ = -79.5413742, ScreenX = 1905.8208281206896, ScreenY = 392.2477377739687 },
                    new() { Id = "d6870dda31937ff93662ce903afe1099b24011a1", Name = "Police Cordon V-Ex", GameX = -19.5134068, GameZ = 114.942139, ScreenX = 2229.440871704707, ScreenY = 1491.569730939868 },
                    new() { Id = "ab623005251ffcd3603a5df2610917a998d9a17e", Name = "Nakatani Basement Stairs", GameX = -16.1249924, GameZ = 335.063416, ScreenX = 2270.2410442375476, ScreenY = 2731.066808950358 }
                },
                Aliases = new List<string> { "groundzero", "GROUNDZERO", "Sandbox", "sandbox", "ground-zero", "ground-zero-21" },
                MarkerScale = 3.0
            },
            // Labs: Calibrated from 5 reference points
            new()
            {
                Key = "Labs",
                DisplayName = "The Lab",
                ImagePath = "Assets/Maps/Labs.svg",
                ImageWidth = 5500,
                ImageHeight = 4200,
                CalibratedTransform = [-0.06475489648812036, 15.461642379397855, 18.704109904840276, 0.31533351406309695, 7946.959753722009, 5877.909736136232],
                CalibrationPoints = new List<CalibrationPoint>
                {
                    new() { Id = "c5c6f818755ac844b4723b1a2cf057569da410ee", Name = "Parking Gate", GameX = -231.73, GameZ = -434.816376, ScreenX = 1310.2241588945453, ScreenY = 1422.0933740932383 },
                    new() { Id = "1a5ae89d5401d277f8b317c18951dfe0e1bfd695", Name = "Main Elevator", GameX = -282.304016, GameZ = -334.896, ScreenX = 2733.997401688906, ScreenY = 480.00396747518374 },
                    new() { Id = "c846e13f4bf7afe28c0cf214bd9e7b4685ba592f", Name = "Sewage Conduit", GameX = -122.889992, GameZ = -258.3245, ScreenX = 4011.244465227256, ScreenY = 3515.778232018718 },
                    new() { Id = "ed029f26c9d60bf00d926b4d7e7876b0591b411a", Name = "Medical Block Elevator", GameX = -112.423, GameZ = -343.986, ScreenX = 2607.884374465454, ScreenY = 3645.1769947510384 },
                    new() { Id = "4cfc8fb5003e428d6cb9f8492690068afba50c71", Name = "Cargo Elevator", GameX = -112.152, GameZ = -408.64, ScreenX = 1595.2620604394808, ScreenY = 3651.4213017493394 }
                },
                Aliases = new List<string> { "labs", "LABS", "laboratory", "the-lab" },
                Floors = new List<MapFloorConfig>
                {
                    new() { LayerId = "basement", DisplayName = "Basement", Order = -1 },
                    new() { LayerId = "main", DisplayName = "Main Floor", Order = 0, IsDefault = true },
                    new() { LayerId = "level2", DisplayName = "Level 2", Order = 1 }
                }
            }
        };
    }
}
