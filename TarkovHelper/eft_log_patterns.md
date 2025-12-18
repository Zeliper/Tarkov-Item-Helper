# EFT 로그 분석 패턴 가이드

이 문서는 Escape from Tarkov 게임 로그에서 레이드 정보를 추출하는 방법을 설명합니다.

## 로그 폴더 구조

```
[EFT 설치 경로]/build/Logs/
├── log_YYYY.MM.DD_H-MM-SS_VERSION/          # 세션 폴더 (예: log_2025.12.02_19-46-45_1.0.0.2.42157)
│   ├── {timestamp} application_000.log      # 애플리케이션 로그 (맵, 모드, 프로파일 정보)
│   ├── {timestamp} network-connection_000.log  # 네트워크 연결 로그 (레이드 시작/종료)
│   ├── {timestamp} backend_000.log          # 백엔드 API 통신
│   ├── {timestamp} errors_000.log           # 에러 로그
│   └── ... (기타 로그 파일들)
```

## 주요 로그 파일

| 파일명 | 용도 | 핵심 정보 |
|--------|------|-----------|
| `application_000.log` | 게임 상태 변화 | 맵 로딩, 세션 모드, 프로파일 선택 |
| `network-connection_000.log` | 네트워크 연결 | 레이드 시작/종료 시간, 서버 IP |

---

## 1. 레이드 시작 감지

### 1.1 네트워크 연결 시작 (network-connection_000.log)

**패턴:**
```
{timestamp}|{version}|Info|network-connection|Connect (address: {IP}:{PORT})
```

**예시:**
```
2025-12-02 21:50:53.159|1.0.0.2.42157|Info|network-connection|Connect (address: 104.200.159.98:17001)
```

**정규식:**
```regex
(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\|.*\|Info\|network-connection\|Connect \(address: ([\d.]+):(\d+)\)
```

**추출 정보:**
- Group 1: 레이드 시작 시간 (예: `2025-12-02 21:50:53.159`)
- Group 2: 서버 IP (예: `104.200.159.98`)
- Group 3: 포트 (예: `17001`)

### 1.2 연결 상태 확인

레이드 시작 시 순차적으로 다음 로그가 나타납니다:

```
Connect (address: ...)           # 연결 시도
Exit to the 'Initial' state      # 초기 상태로 이동
Enter to the 'Connecting' state  # 연결 중
Enter to the 'Connected' state   # 연결 완료 (레이드 시작 확정)
```

**연결 완료 정규식:**
```regex
Enter to the 'Connected' state \(address: ([\d.]+):(\d+)
```

---

## 2. 레이드 종료 감지

### 2.1 네트워크 연결 종료 (network-connection_000.log)

**패턴:**
```
{timestamp}|{version}|Info|network-connection|Disconnect (address: {IP}:{PORT})
```

**예시:**
```
2025-12-02 22:00:45.851|1.0.0.2.42157|Info|network-connection|Disconnect (address: 104.200.159.98:17001)
```

**정규식:**
```regex
(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\|.*\|Info\|network-connection\|Disconnect \(address: ([\d.]+):(\d+)\)
```

### 2.2 연결 통계 (레이드 종료 시 기록)

**패턴:**
```
Statistics (address: {IP}:{PORT}, rtt: {RTT}, lose: {LOSS}, sent: {SENT}, received: {RECEIVED})
```

**예시:**
```
2025-12-02 22:00:45.851|1.0.0.2.42157|Info|network-connection|Statistics (address: 104.200.159.98:17001, rtt: 182.75, lose: -1.164153E-10, sent: 71065, received: 49602)
```

**정규식:**
```regex
Statistics \(address: ([\d.]+):(\d+), rtt: ([\d.]+), lose: ([^,]+), sent: (\d+), received: (\d+)\)
```

---

## 3. 맵 정보 감지

### 3.1 맵 프리셋 로딩 (application_000.log)

**패턴:**
```
{timestamp}|{version}|Info|application|scene preset path:maps/{MAP_BUNDLE}.bundle rcid:{SCENE_ID}
```

**예시:**
```
2025-12-02 21:48:09.835|1.0.0.2.42157|Info|application|scene preset path:maps/shopping_mall.bundle rcid:Shopping_Mall.ScenesPreset.asset
```

**정규식:**
```regex
scene preset path:maps/([^.]+)\.bundle
```

**맵 번들명 → 맵 이름 매핑:**

| 번들명 | 맵 이름 | 표시명 |
|--------|---------|--------|
| `shopping_mall` | Interchange | 인터체인지 |
| `laboratory` | The Lab | 연구소 |
| `tarkovstreets` | TarkovStreets | 타르코프 시가지 |
| `shoreline_preset` | Shoreline | 해안선 |
| `woods_preset` | Woods | 숲 |
| `lighthouse` | Lighthouse | 등대 |
| `factory_day_preset` | Factory (Day) | 팩토리 (주간) |
| `factory_night_preset` | Factory (Night) | 팩토리 (야간) |
| `customs_preset` | Customs | 세관 |
| `rezervbase` | Reserve | 리저브 |
| `sandbox_preset` | Ground Zero | 그라운드 제로 |
| `sandbox_high_preset` | Ground Zero (High) | 그라운드 제로 (고레벨) |

> **참고:** 일부 맵 번들명은 `_preset` 접미사가 붙습니다. 정규식 매칭 시 이를 고려해야 합니다.

### 3.2 TRACE-NetworkGameCreate (상세 레이드 정보)

**패턴:**
```
TRACE-NetworkGameCreate profileStatus: 'Profileid: {PROFILE_ID}, Status: {STATUS}, RaidMode: {MODE}, Ip: {IP}, Port: {PORT}, Location: {MAP}, Sid: {SESSION_ID}, GameMode: {GAME_MODE}, shortId: {SHORT_ID}'
```

**예시:**
```
2025-12-02 21:50:53.106|1.0.0.2.42157|Debug|application|TRACE-NetworkGameCreate profileStatus: 'Profileid: 69193861844e4f097e00ec2e, Status: Busy, RaidMode: Online, Ip: 104.200.159.98, Port: 17001, Location: Interchange, Sid: US-DAL03G010_692ee0420a302012001058bd_02.12.25_15-49-06, GameMode: deathmatch, shortId: RTCSZQ'
```

**정규식:**
```regex
TRACE-NetworkGameCreate profileStatus: 'Profileid: ([^,]+), Status: ([^,]+), RaidMode: ([^,]+), Ip: ([^,]+), Port: ([^,]+), Location: ([^,]+), Sid: ([^,]+), GameMode: ([^,]+), shortId: ([^']+)'
```

**추출 정보:**
- Group 1: 프로파일 ID (PMC/SCAV 구분에 사용)
- Group 2: 상태 (Busy = 레이드 중)
- Group 3: 레이드 모드 (Online)
- Group 4: 서버 IP
- Group 5: 포트
- Group 6: **맵 이름** (Location)
- Group 7: 세션 ID (Sid)
- Group 8: 게임 모드 (deathmatch)
- Group 9: 짧은 세션 ID

### 3.3 Transit 로그 (맵 전환/로드 완료)

**패턴:**
```
[Transit] Flag:{FLAG}, RaidId:{RAID_ID}, Count:{COUNT}, Locations:{LOCATION} ->
```

**예시:**
```
2025-12-03 03:52:34.311|1.0.0.2.42157|Info|application|[Transit] Flag:Common, RaidId:692f357049044ebb070b72e0, Count:0, Locations:factory4_day ->
```

**정규식:**
```regex
\[Transit\] Flag:([^,]+), RaidId:([^,]+), Count:(\d+), Locations:([^ ]+)
```

**Location 값 → 맵 매핑:**

| Location 값 | 맵 이름 | 비고 |
|-------------|---------|------|
| `bigmap` | Customs | |
| `factory4_day` | Factory (Day) | |
| `factory4_night` | Factory (Night) | |
| `Interchange` | Interchange | |
| `laboratory` | The Lab | |
| `Lighthouse` | Lighthouse | |
| `RezervBase` | Reserve | |
| `Shoreline` | Shoreline | |
| `TarkovStreets` | Streets of Tarkov | |
| `Woods` | Woods | |
| `Sandbox` | Ground Zero | |
| `Sandbox_high` | Ground Zero (High) | 15레벨 이상 |
| `Sandbox_start` | Ground Zero (Tutorial) | 튜토리얼/시작 지점 |

---

## 4. 게임 모드 감지 (PVE vs PVP)

### 4.1 세션 모드 (application_000.log)

**패턴:**
```
{timestamp}|{version}|Info|application|Session mode: {MODE}
```

**예시:**
```
2025-12-03 03:26:23.888|1.0.0.2.42157|Info|application|Session mode: Pve
```

**정규식:**
```regex
Session mode: (Pve|Pvp|Regular)
```

**모드 값:**
- `Pve`: PVE 모드
- `Pvp` 또는 `Regular`: PVP 모드

---

## 5. PMC vs SCAV 구분

### 5.1 프로파일 ID 패턴

EFT는 동일 계정에 대해 PMC와 SCAV에 서로 다른 프로파일 ID를 부여합니다.

**패턴:**
- PMC와 SCAV 프로파일 ID는 마지막 16진수 문자만 다름
- SCAV 프로파일 ID = PMC 프로파일 ID의 마지막 문자 + 1 (hex)
- 예: PMC가 `c`로 끝나면 SCAV는 `d`, PMC가 `d`로 끝나면 SCAV는 `e`

**예시 1:**
```
PMC:  69193861844e4f097e00ec2d
SCAV: 69193861844e4f097e00ec2e
```

**예시 2 (다른 플레이어):**
```
PMC:  6655cef5899e7271740f41dc
SCAV: 6655cef5899e7271740f41dd
```

### 5.2 SelectProfile 로그

**패턴:**
```
{timestamp}|{version}|Info|application|SelectProfile ProfileId:{PROFILE_ID} AccountId:{ACCOUNT_ID}
```

**정규식:**
```regex
SelectProfile ProfileId:([a-f0-9]+) AccountId:(\d+)
```

### 5.3 구분 로직

1. `SelectProfile` 로그에서 기본 PMC 프로파일 ID 확인
2. `TRACE-NetworkGameCreate`의 `Profileid`와 비교
3. 마지막 문자가 다르면 SCAV 레이드

**구분 알고리즘:**
```csharp
bool IsScavRaid(string pmcProfileId, string raidProfileId)
{
    if (pmcProfileId.Length != raidProfileId.Length)
        return false;

    // 마지막 문자만 다르고, SCAV는 PMC + 1 (hex)
    string pmcBase = pmcProfileId.Substring(0, pmcProfileId.Length - 1);
    string raidBase = raidProfileId.Substring(0, raidProfileId.Length - 1);

    if (pmcBase != raidBase)
        return false;

    char pmcLast = pmcProfileId[pmcProfileId.Length - 1];
    char raidLast = raidProfileId[raidProfileId.Length - 1];

    // SCAV 프로파일 ID는 PMC의 마지막 hex 문자 + 1
    // 예: PMC가 'c'면 SCAV는 'd', PMC가 'd'면 SCAV는 'e'
    int pmcHex = Convert.ToInt32(pmcLast.ToString(), 16);
    int raidHex = Convert.ToInt32(raidLast.ToString(), 16);

    return raidHex == pmcHex + 1;
}
```

---

## 6. 솔로 vs 파티 플레이 구분

### 6.1 Matching with group id (application_000.log)

`TRACE-NetworkGameCreate` 로그 직전에 나타나는 `Matching with group id:` 로그로 솔로/파티 플레이를 구분할 수 있습니다.

**패턴:**
```
{timestamp}|{version}|Debug|application|Matching with group id: {GROUP_ID}
```

### 6.2 솔로 플레이

**예시:**
```
2025-12-07 01:23:36.685|1.0.0.2.42157|Debug|application|Matching with group id:
2025-12-07 01:25:42.535|1.0.0.2.42157|Debug|application|TRACE-NetworkGameCreate profileStatus: '...'
```

- `group id:` 뒤에 **값이 비어있음**

### 6.3 파티 플레이

**예시:**
```
2025-12-07 17:00:00.514|1.0.0.2.42157|Debug|application|Matching with group id: 13759600
2025-12-07 17:03:06.354|1.0.0.2.42157|Debug|application|TRACE-NetworkGameCreate profileStatus: '...'
```

- `group id:` 뒤에 **파티장의 Account ID가 있음**

### 6.4 정규식

```regex
Matching with group id: (\d*)
```

**추출 정보:**
- Group 1이 비어있음 → **솔로 플레이**
- Group 1에 숫자가 있음 → **파티 플레이** (값 = 파티장 Account ID)

### 6.5 구분 알고리즘

```csharp
bool IsPartyRaid(string groupId)
{
    return !string.IsNullOrEmpty(groupId);
}

string? GetPartyLeaderAccountId(string groupId)
{
    return string.IsNullOrEmpty(groupId) ? null : groupId;
}
```

---

## 7. 로그 모니터링 구현 예시

### 7.1 상태 머신

```
[Idle] → (Session mode 감지) → [SessionStarted]
      → (scene preset 감지) → [MapLoading]
      → (Connect 감지) → [RaidStarting]
      → (Connected 감지) → [InRaid]
      → (Disconnect 감지) → [RaidEnded]
      → [Idle]
```

### 7.2 핵심 이벤트 순서

1. **세션 시작**: `Session mode: Pve/Pvp`
2. **프로파일 선택**: `SelectProfile ProfileId:...`
3. **맵 로딩 시작**: `scene preset path:maps/...`
4. **맵 로딩 완료**: `LocationLoaded:...`
5. **매칭 시작 (솔로/파티 구분)**: `Matching with group id: {GROUP_ID}`
6. **레이드 연결 시작**: `TRACE-NetworkGameCreate profileStatus:...`
7. **서버 연결**: `Connect (address: ...)`
8. **연결 완료**: `Enter to the 'Connected' state`
9. **레이드 진행 중**: (게임 플레이)
10. **레이드 종료**: `Disconnect (address: ...)`
11. **통계 기록**: `Statistics (address: ...)`

### 7.3 C# 구현 예시

```csharp
public class EftLogParser
{
    // 정규식 패턴
    private static readonly Regex SessionModeRegex = new(@"Session mode: (Pve|Pvp|Regular)");
    private static readonly Regex ScenePresetRegex = new(@"scene preset path:maps/([^.]+)\.bundle");
    private static readonly Regex ConnectRegex = new(@"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}).*Connect \(address: ([\d.]+):(\d+)\)");
    private static readonly Regex DisconnectRegex = new(@"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}).*Disconnect \(address: ([\d.]+):(\d+)\)");
    private static readonly Regex TraceNetworkRegex = new(@"TRACE-NetworkGameCreate profileStatus: 'Profileid: ([^,]+).*Location: ([^,]+).*Sid: ([^,]+)");
    private static readonly Regex SelectProfileRegex = new(@"SelectProfile ProfileId:([a-f0-9]+)");
    private static readonly Regex MatchingGroupRegex = new(@"Matching with group id: (\d*)");

    private readonly Dictionary<string, string> _mapBundleToName = new()
    {
        ["shopping_mall"] = "Interchange",
        ["laboratory"] = "The Lab",
        ["tarkovstreets"] = "Streets of Tarkov",
        ["shoreline_preset"] = "Shoreline",
        ["woods_preset"] = "Woods",
        ["lighthouse"] = "Lighthouse",
        ["factory_day_preset"] = "Factory (Day)",
        ["factory_night_preset"] = "Factory (Night)",
        ["customs_preset"] = "Customs",
        ["rezervbase"] = "Reserve",
        ["sandbox_preset"] = "Ground Zero",
        ["sandbox_high_preset"] = "Ground Zero (High)"
    };

    public RaidInfo? ParseLine(string line)
    {
        // 구현...
    }
}

public class RaidInfo
{
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string MapName { get; set; }
    public string ServerIp { get; set; }
    public string SessionId { get; set; }
    public bool IsPve { get; set; }
    public bool IsScav { get; set; }
    public bool IsParty { get; set; }              // 파티 플레이 여부
    public string? PartyLeaderAccountId { get; set; }  // 파티장 Account ID (솔로면 null)
    public TimeSpan? Duration => EndTime.HasValue ? EndTime - StartTime : null;
}
```

---

## 8. 주의사항

1. **로그 파일 인코딩**: UTF-8
2. **타임스탬프 형식**: 버전에 따라 다름
   - 구 버전 (~41771): `yyyy-MM-dd HH:mm:ss.fff +09:00` (타임존 포함)
   - 신 버전 (41787~): `yyyy-MM-dd HH:mm:ss.fff` (타임존 없음)
3. **로그 레벨**: `Info`, `Debug`, `Error`, `Warning`
4. **파일 회전**: 게임 세션마다 새 폴더 생성
5. **실시간 모니터링**: FileSystemWatcher 또는 tail -f 방식 사용
6. **레이드 종료 이유**: 로그에서 직접 확인 불가 (생존/사망/MIA 구분 어려움)

---

## 9. 에러 및 경고 패턴

### 9.1 네트워크 타임아웃 (network-connection_000.log)

**메시지 타임아웃:**
```
{timestamp}|{version}|Error|network-connection|Timeout: Messages timed out after not receiving any message for {TIME}ms (address: {IP}:{PORT})
```

**연결 타임아웃:**
```
{timestamp}|{version}|Error|network-connection|Timeout: Connection timed out after not receiving any message for {TIME}ms (address: {IP}:{PORT})
```

**정규식:**
```regex
Timeout: (Messages|Connection) timed out after not receiving any message for (\d+)ms \(address: ([\d.]+):(\d+)\)
```

### 9.2 스레드 처리 초과 (network-connection_000.log)

**패턴:**
```
{timestamp}|{version}|Error|network-connection|Thread processing exceeded the limit [{CURRENT}/{LIMIT}]
```

**예시:**
```
2025-11-16 21:42:17.095|1.0.0.0.41760|Error|network-connection|Thread processing exceeded the limit [2083/2000]
```

**정규식:**
```regex
Thread processing exceeded the limit \[(\d+)/(\d+)\]
```

---

## 10. Transit 로그 상세

### 10.1 맵 전환 (레이드 시작 시)

**패턴:**
```
{timestamp}|{version}|Info|application|[Transit] Flag:{FLAG}, RaidId:{RAID_ID}, Count:{COUNT}, Locations:{LOCATION} ->
```

**예시:**
```
2025-12-03 03:52:34.311|1.0.0.2.42157|Info|application|[Transit] Flag:Common, RaidId:692f357049044ebb070b72e0, Count:0, Locations:factory4_day ->
```

**Flag 값:**
- `Common`: 일반 레이드
- `None`: 튜토리얼/특수 상황

### 10.2 레이드 종료 (application_000.log)

**패턴:**
```
{timestamp}|{version}|Info|application|[Transit] `{PROFILE_ID}` Count:{COUNT}, EventPlayer:{BOOL}
```

**예시:**
```
2025-12-15 21:59:25.360|1.0.0.5.42334|Info|application|[Transit] `6655cef5899e7271740f41dc` Count:0, EventPlayer:False
```

**정규식:**
```regex
\[Transit\] `([a-f0-9]+)` Count:(\d+), EventPlayer:(True|False)
```

**추출 정보:**
- Group 1: 프로파일 ID (PMC/SCAV 구분 가능)
- Group 2: 카운트
- Group 3: 이벤트 플레이어 여부

---

## 11. 요약 표

| 정보 | 로그 파일 | 패턴 키워드 | 신뢰도 |
|------|-----------|-------------|--------|
| 레이드 시작 | network-connection | `Connect (address:` | 높음 |
| 레이드 종료 | network-connection | `Disconnect (address:` | 높음 |
| 맵 이름 | application | `TRACE-NetworkGameCreate.*Location:` | 높음 |
| 맵 이름 (대체) | application | `scene preset path:maps/` | 높음 |
| 맵 이름 (대체) | application | `[Transit].*Locations:` | 중간 |
| 게임 모드 | application | `Session mode:` | 높음 |
| PMC/SCAV | application | `TRACE-NetworkGameCreate.*Profileid:` | 중간 |
| 솔로/파티 | application | `Matching with group id:` | 높음 |
| 서버 IP | network-connection | `Connect (address:` | 높음 |
| 세션 ID | application | `TRACE-NetworkGameCreate.*Sid:` | 높음 |
| 네트워크 타임아웃 | network-connection | `Timeout:` | 높음 |
| 스레드 초과 | network-connection | `Thread processing exceeded` | 높음 |
| 레이드 종료 상세 | application | `[Transit] \`.*\`` | 높음 |
