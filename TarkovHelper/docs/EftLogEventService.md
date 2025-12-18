# EFT Log Event Service

EFT 게임 로그를 실시간 모니터링하여 레이드 이벤트를 감지하고 구독할 수 있는 서비스입니다.

## 개요

`EftRaidEventService`는 다음 기능을 제공합니다:
- **Profile ID 감지**: PMC/SCAV 프로파일 자동 감지
- **레이드 타입 구분**: PMC vs SCAV 플레이 구분
- **게임 모드 감지**: PVE vs PVP 모드
- **레이드 라이프사이클**: 매칭 → 연결 → 진행 → 종료
- **솔로/파티 구분**: 파티 플레이 감지 및 파티장 정보
- **레이드 히스토리**: user_data.db에 레이드 기록 저장

## 로그 파일 위치

```
%LOCALAPPDATA%\Battlestate Games\EFT\Logs\
└── log_YYYY.MM.DD_HH-MM-SS_VERSION/
    ├── {timestamp} application_000.log     # 프로파일, 맵, 세션 정보
    ├── {timestamp} network-connection_000.log  # 네트워크 연결/해제
    ├── {timestamp} backend_000.log         # 백엔드 API
    └── {timestamp} errors_000.log          # 에러 로그
```

## Profile ID 구조

EFT는 동일 계정에 대해 PMC와 SCAV에 서로 다른 프로파일 ID를 부여합니다:

- 프로파일 ID는 **24자리 hex 문자열**
- PMC와 SCAV는 **마지막 hex 문자만 다름**
- **SCAV ID = PMC ID + 1** (hex)

```
예시:
PMC:  69193861844e4f097e00ec2d
SCAV: 69193861844e4f097e00ec2e
                          ↑ 마지막 문자만 다름 (d → e)
```

### 구분 알고리즘

```csharp
bool IsScavProfile(string pmcProfileId, string raidProfileId)
{
    if (pmcProfileId.Length != raidProfileId.Length)
        return false;

    // 마지막 문자 제외한 부분이 같아야 함
    var pmcBase = pmcProfileId[..^1];
    var raidBase = raidProfileId[..^1];
    if (pmcBase != raidBase)
        return false;

    // SCAV = PMC + 1 (hex)
    var pmcLast = Convert.ToInt32(pmcProfileId[^1].ToString(), 16);
    var raidLast = Convert.ToInt32(raidProfileId[^1].ToString(), 16);
    return raidLast == pmcLast + 1;
}
```

## 이벤트 구독

### 사용 예시

```csharp
using TarkovHelper.Services;
using TarkovHelper.Models;

// 서비스 인스턴스 가져오기
var raidService = EftRaidEventService.Instance;

// 1. 프로파일 변경 이벤트
raidService.ProfileChanged += (sender, args) =>
{
    var profile = args.ProfileInfo;
    Console.WriteLine($"PMC ID: {profile.PmcProfileId}");
    Console.WriteLine($"SCAV ID: {profile.ScavProfileId}");
    Console.WriteLine($"Account ID: {profile.AccountId}");
};

// 2. 레이드 이벤트
raidService.RaidEvent += (sender, args) =>
{
    switch (args.EventType)
    {
        case EftRaidEventType.SessionModeDetected:
            Console.WriteLine($"Game mode: {args.Message}"); // PVE or PVP
            break;

        case EftRaidEventType.MatchingStarted:
            Console.WriteLine($"Matching: {args.Message}"); // Solo or Party
            break;

        case EftRaidEventType.RaidStarted:
            var raid = args.RaidInfo!;
            Console.WriteLine($"Map: {raid.MapKey}");
            Console.WriteLine($"Type: {raid.RaidType}"); // PMC or Scav
            Console.WriteLine($"Mode: {raid.GameMode}"); // PVE or PVP
            Console.WriteLine($"Party: {raid.IsParty}");
            break;

        case EftRaidEventType.RaidEnded:
            var endedRaid = args.RaidInfo!;
            Console.WriteLine($"Duration: {endedRaid.Duration?.TotalMinutes:F1} min");
            Console.WriteLine($"RTT: {endedRaid.Rtt:F1} ms");
            break;

        case EftRaidEventType.NetworkTimeout:
            Console.WriteLine($"Timeout: {args.Message}");
            break;
    }
};

// 3. 모니터링 상태 변경
raidService.MonitoringStateChanged += (sender, isMonitoring) =>
{
    Console.WriteLine($"Monitoring: {isMonitoring}");
};

// 모니터링 시작
raidService.StartMonitoring(); // 기본 경로 사용
// 또는
raidService.StartMonitoring(@"D:\Games\EFT\Logs"); // 커스텀 경로

// 모니터링 중지
raidService.StopMonitoring();
```

## 이벤트 타입

| 이벤트 | 설명 | RaidInfo |
|--------|------|----------|
| `SessionModeDetected` | PVE/PVP 모드 감지 | - |
| `ProfileSelected` | 프로파일 선택 (로그인) | - |
| `MatchingStarted` | 매칭 대기열 시작 | - |
| `MapLoadingStarted` | 맵 로딩 시작 | 부분 |
| `Connecting` | 서버 연결 중 | O |
| `Connected` | 연결 완료 | O |
| `RaidStarted` | 레이드 진입 | O |
| `Disconnected` | 서버 연결 해제 | O |
| `RaidEnded` | 레이드 종료 | O (전체) |
| `NetworkTimeout` | 네트워크 타임아웃 | O |
| `NetworkError` | 네트워크 에러 | O |

## 모델 정의

### RaidType (PMC/SCAV)

```csharp
public enum RaidType
{
    Unknown = 0,
    PMC = 1,
    Scav = 2
}
```

### GameMode (PVE/PVP)

```csharp
public enum GameMode
{
    Unknown = 0,
    PVP = 1,
    PVE = 2
}
```

### RaidState

```csharp
public enum RaidState
{
    Idle = 0,
    Matching = 1,
    Connecting = 2,
    InRaid = 3,
    Ended = 4
}
```

### EftRaidInfo

```csharp
public class EftRaidInfo
{
    public string? RaidId { get; set; }
    public string? SessionId { get; set; }
    public string? ShortId { get; set; }
    public string? ProfileId { get; set; }
    public RaidType RaidType { get; set; }
    public GameMode GameMode { get; set; }
    public string? MapName { get; set; }      // 로그 원본 값
    public string? MapKey { get; set; }       // map_configs.json 키
    public string? ServerIp { get; set; }
    public int ServerPort { get; set; }
    public bool IsParty { get; set; }
    public string? PartyLeaderAccountId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan? Duration { get; }        // 계산된 값
    public double? Rtt { get; set; }          // 네트워크 RTT (ms)
    public double? PacketLoss { get; set; }
    public long? PacketsSent { get; set; }
    public long? PacketsReceived { get; set; }
}
```

## 맵 매핑

EFT 로그 값 → `map_configs.json` 키 매핑:

| EFT 로그 값 | Map 탭 키 |
|------------|-----------|
| `woods`, `woods_preset` | **Woods** |
| `customs`, `customs_preset`, `bigmap` | **Customs** |
| `shoreline`, `shoreline_preset` | **Shoreline** |
| `interchange`, `shopping_mall` | **Interchange** |
| `reserve`, `rezervbase`, `rezerv_base_preset` | **Reserve** |
| `lighthouse`, `lighthouse_preset` | **Lighthouse** |
| `tarkovstreets`, `streets`, `city_preset` | **StreetsOfTarkov** |
| `factory`, `factory4_day`, `factory4_night`, `factory_*_preset` | **Factory** |
| `groundzero`, `sandbox`, `sandbox_high`, `sandbox_start`, `sandbox_*_preset` | **GroundZero** |
| `laboratory`, `laboratory_preset`, `labs`, `lab` | **Labs** |
| `labyrinth`, `labyrinth_preset` | **Labyrinth** |

> Day/Night, 레벨별 변형 모두 동일한 맵 키로 매핑됩니다.

## 데이터베이스 저장

### UserSettings 테이블 (프로파일 정보)

| 키 | 설명 |
|----|------|
| `eft.pmcProfileId` | PMC 프로파일 ID |
| `eft.scavProfileId` | SCAV 프로파일 ID |
| `eft.accountId` | 계정 ID |

### RaidHistory 테이블

```sql
CREATE TABLE RaidHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RaidId TEXT,
    SessionId TEXT,
    ShortId TEXT,
    ProfileId TEXT,
    RaidType INTEGER,        -- 1=PMC, 2=Scav
    GameMode INTEGER,        -- 1=PVP, 2=PVE
    MapName TEXT,
    MapKey TEXT,
    ServerIp TEXT,
    ServerPort INTEGER,
    IsParty INTEGER,
    PartyLeaderAccountId TEXT,
    StartTime TEXT,
    EndTime TEXT,
    DurationSeconds INTEGER,
    Rtt REAL,
    PacketLoss REAL,
    PacketsSent INTEGER,
    PacketsReceived INTEGER,
    CreatedAt TEXT
);
```

### 히스토리 조회

```csharp
var dbService = UserDataDbService.Instance;

// 최근 100개 레이드
var history = await dbService.GetRaidHistoryAsync(limit: 100);

// PMC 레이드만
var pmcRaids = await dbService.GetRaidHistoryAsync(raidType: RaidType.PMC);

// 특정 맵만
var customsRaids = await dbService.GetRaidHistoryAsync(mapKey: "Customs");

// 통계 조회
var (total, pmc, scav, party) = await dbService.GetRaidStatisticsAsync();
var last7Days = await dbService.GetRaidStatisticsAsync(since: DateTime.Now.AddDays(-7));

// 오래된 기록 정리 (30일 이전)
await dbService.CleanupRaidHistoryAsync(keepDays: 30);
```

## 로그 패턴 (참고)

### SelectProfile (프로파일 선택)

```
SelectProfile ProfileId:69193861844e4f097e00ec2d AccountId:12345678
```

### Session mode (게임 모드)

```
Session mode: Pve
Session mode: Pvp
```

### Matching with group id (솔로/파티)

```
Matching with group id:           # 솔로
Matching with group id: 13759600  # 파티 (값 = 파티장 Account ID)
```

### TRACE-NetworkGameCreate (레이드 정보)

```
TRACE-NetworkGameCreate profileStatus: 'Profileid: 69193861844e4f097e00ec2e,
Status: Busy, RaidMode: Online, Ip: 104.200.159.98, Port: 17001,
Location: Interchange, Sid: US-DAL03G010_xxx, GameMode: deathmatch, shortId: RTCSZQ'
```

### Connect/Disconnect (네트워크)

```
Connect (address: 104.200.159.98:17001)
Enter to the 'Connected' state (address: 104.200.159.98:17001)
Disconnect (address: 104.200.159.98:17001)
Statistics (address: 104.200.159.98:17001, rtt: 182.75, lose: -1.16, sent: 71065, received: 49602)
```

## 관련 파일

- `Models/EftRaidEvent.cs` - 이벤트 모델 정의
- `Services/EftRaidEventService.cs` - 메인 서비스
- `Services/LogSyncService.cs` - 퀘스트 동기화 (맵 감지 포함)
- `Services/Map/LogMapWatcherService.cs` - 맵 변경 감지
- `Services/UserDataDbService.cs` - RaidHistory DB 저장
