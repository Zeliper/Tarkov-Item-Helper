namespace TarkovHelper.Models;

/// <summary>
/// 레이드 타입 (PMC/SCAV)
/// </summary>
public enum RaidType
{
    Unknown = 0,
    PMC = 1,
    Scav = 2
}

/// <summary>
/// 게임 모드 (PVE/PVP)
/// </summary>
public enum GameMode
{
    Unknown = 0,
    PVP = 1,
    PVE = 2
}

/// <summary>
/// 레이드 상태
/// </summary>
public enum RaidState
{
    Idle = 0,
    Matching = 1,
    Connecting = 2,
    InRaid = 3,
    Ended = 4
}

/// <summary>
/// EFT 프로파일 정보
/// </summary>
public class EftProfileInfo
{
    /// <summary>
    /// PMC 프로파일 ID (24자리 hex)
    /// </summary>
    public string? PmcProfileId { get; set; }

    /// <summary>
    /// SCAV 프로파일 ID (PMC ID + 1)
    /// </summary>
    public string? ScavProfileId { get; set; }

    /// <summary>
    /// 계정 ID
    /// </summary>
    public string? AccountId { get; set; }

    /// <summary>
    /// 마지막 업데이트 시간
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 주어진 프로파일 ID가 SCAV인지 확인
    /// </summary>
    public bool IsScavProfile(string profileId)
    {
        if (string.IsNullOrEmpty(PmcProfileId) || string.IsNullOrEmpty(profileId))
            return false;

        if (profileId.Length != PmcProfileId.Length)
            return false;

        // 마지막 문자만 비교
        var pmcBase = PmcProfileId[..^1];
        var raidBase = profileId[..^1];

        if (pmcBase != raidBase)
            return false;

        // SCAV 프로파일 ID는 PMC의 마지막 hex 문자 + 1
        var pmcLast = PmcProfileId[^1];
        var raidLast = profileId[^1];

        try
        {
            var pmcHex = Convert.ToInt32(pmcLast.ToString(), 16);
            var raidHex = Convert.ToInt32(raidLast.ToString(), 16);
            return raidHex == pmcHex + 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 주어진 프로파일 ID로 레이드 타입 결정
    /// </summary>
    public RaidType GetRaidType(string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
            return RaidType.Unknown;

        if (string.Equals(profileId, PmcProfileId, StringComparison.OrdinalIgnoreCase))
            return RaidType.PMC;

        if (IsScavProfile(profileId))
            return RaidType.Scav;

        return RaidType.Unknown;
    }
}

/// <summary>
/// 레이드 정보
/// </summary>
public class EftRaidInfo
{
    /// <summary>
    /// 레이드 고유 ID
    /// </summary>
    public string? RaidId { get; set; }

    /// <summary>
    /// 세션 ID (Sid)
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// 짧은 세션 ID (shortId)
    /// </summary>
    public string? ShortId { get; set; }

    /// <summary>
    /// 프로파일 ID
    /// </summary>
    public string? ProfileId { get; set; }

    /// <summary>
    /// 레이드 타입 (PMC/SCAV)
    /// </summary>
    public RaidType RaidType { get; set; }

    /// <summary>
    /// 게임 모드 (PVE/PVP)
    /// </summary>
    public GameMode GameMode { get; set; }

    /// <summary>
    /// 맵 이름 (Location)
    /// </summary>
    public string? MapName { get; set; }

    /// <summary>
    /// 맵 키 (map_configs.json의 key)
    /// </summary>
    public string? MapKey { get; set; }

    /// <summary>
    /// 서버 IP
    /// </summary>
    public string? ServerIp { get; set; }

    /// <summary>
    /// 서버 포트
    /// </summary>
    public int ServerPort { get; set; }

    /// <summary>
    /// 솔로/파티 플레이 여부
    /// </summary>
    public bool IsParty { get; set; }

    /// <summary>
    /// 파티장 Account ID (파티 플레이 시)
    /// </summary>
    public string? PartyLeaderAccountId { get; set; }

    /// <summary>
    /// 레이드 시작 시간
    /// </summary>
    public DateTime? StartTime { get; set; }

    /// <summary>
    /// 레이드 종료 시간
    /// </summary>
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// 레이드 상태
    /// </summary>
    public RaidState State { get; set; }

    /// <summary>
    /// 레이드 지속 시간
    /// </summary>
    public TimeSpan? Duration => EndTime.HasValue && StartTime.HasValue
        ? EndTime - StartTime
        : null;

    /// <summary>
    /// 네트워크 통계: RTT (ms)
    /// </summary>
    public double? Rtt { get; set; }

    /// <summary>
    /// 네트워크 통계: 패킷 로스
    /// </summary>
    public double? PacketLoss { get; set; }

    /// <summary>
    /// 네트워크 통계: 전송 패킷 수
    /// </summary>
    public long? PacketsSent { get; set; }

    /// <summary>
    /// 네트워크 통계: 수신 패킷 수
    /// </summary>
    public long? PacketsReceived { get; set; }
}

/// <summary>
/// 레이드 이벤트 타입
/// </summary>
public enum EftRaidEventType
{
    /// <summary>
    /// 세션 모드 감지 (PVE/PVP)
    /// </summary>
    SessionModeDetected,

    /// <summary>
    /// 프로파일 선택
    /// </summary>
    ProfileSelected,

    /// <summary>
    /// 매칭 시작
    /// </summary>
    MatchingStarted,

    /// <summary>
    /// 맵 로딩 시작
    /// </summary>
    MapLoadingStarted,

    /// <summary>
    /// 맵 로딩 완료
    /// </summary>
    MapLoadingCompleted,

    /// <summary>
    /// 서버 연결 시작
    /// </summary>
    Connecting,

    /// <summary>
    /// 서버 연결 완료 (레이드 진입)
    /// </summary>
    Connected,

    /// <summary>
    /// 레이드 시작 (InRaid 상태)
    /// </summary>
    RaidStarted,

    /// <summary>
    /// 서버 연결 해제 (레이드 종료)
    /// </summary>
    Disconnected,

    /// <summary>
    /// 레이드 종료 (통계 포함)
    /// </summary>
    RaidEnded,

    /// <summary>
    /// 네트워크 타임아웃
    /// </summary>
    NetworkTimeout,

    /// <summary>
    /// 네트워크 에러
    /// </summary>
    NetworkError
}

/// <summary>
/// 레이드 이벤트 인자
/// </summary>
public class EftRaidEventArgs : EventArgs
{
    /// <summary>
    /// 이벤트 타입
    /// </summary>
    public EftRaidEventType EventType { get; init; }

    /// <summary>
    /// 현재 레이드 정보
    /// </summary>
    public EftRaidInfo? RaidInfo { get; init; }

    /// <summary>
    /// 이벤트 발생 시간
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;

    /// <summary>
    /// 추가 메시지 (에러 등)
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// 프로파일 변경 이벤트 인자
/// </summary>
public class EftProfileEventArgs : EventArgs
{
    /// <summary>
    /// 새 프로파일 정보
    /// </summary>
    public EftProfileInfo ProfileInfo { get; init; } = null!;

    /// <summary>
    /// 이벤트 발생 시간
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
