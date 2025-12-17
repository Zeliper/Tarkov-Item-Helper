# Logging System PRD (Product Requirements Document)

## 개요

TarkovHelper 애플리케이션에 체계적인 로깅 시스템을 도입하여 디버깅, 모니터링, 문제 해결을 용이하게 합니다.

## 현재 상태

- `System.Diagnostics.Debug.WriteLine()` 사용 (23개 파일에서 사용 중)
- Debug 빌드에서만 출력됨
- 파일 저장 없음
- 로그 레벨 구분 없음

## 목표

1. **구조화된 로그 관리**: 로그를 날짜/인스턴스별로 체계적으로 저장
2. **로그 레벨 분리**: Debug, Info, Warning, Error, Critical 레벨 구분
3. **빌드별 설정**: Debug/Release 빌드에 따른 다른 로깅 정책
4. **사용자 설정**: Release 빌드에서 로그 레벨 조절 가능

---

## 요구사항

### 1. 로그 저장 위치 및 구조

```
[실행 폴더]/Logs/
├── 2025-12-17-001/           # 날짜-인스턴스번호
│   ├── debug.log             # Debug 레벨 로그
│   ├── info.log              # Info 레벨 로그
│   ├── warning.log           # Warning 레벨 로그
│   ├── error.log             # Error 레벨 로그
│   └── all.log               # 모든 레벨 통합 로그
├── 2025-12-17-002/           # 같은 날 두 번째 실행
│   └── ...
└── 2025-12-18-001/
    └── ...
```

**인스턴스 번호 규칙:**
- 같은 날짜에 프로그램을 여러 번 실행할 경우 순차적으로 증가 (001, 002, ...)
- 최대 999개 인스턴스 지원
- 앱 시작 시 해당 날짜의 기존 폴더를 확인하고 다음 번호 부여

### 2. 로그 레벨 정의

| 레벨 | 값 | 설명 | 예시 |
|------|-----|------|------|
| **Trace** | 0 | 매우 상세한 디버깅 정보 | 메서드 진입/종료, 변수 값 |
| **Debug** | 1 | 디버깅용 정보 | DB 쿼리, 상태 변경 |
| **Info** | 2 | 일반 정보성 메시지 | 앱 시작, 페이지 전환 |
| **Warning** | 3 | 잠재적 문제 | 느린 응답, 재시도 |
| **Error** | 4 | 오류 발생 | 예외, 실패 |
| **Critical** | 5 | 치명적 오류 | 앱 크래시, 데이터 손상 |
| **None** | 6 | 로깅 비활성화 | - |

### 3. 빌드별 기본 로그 레벨

| 빌드 모드 | 파일 로깅 레벨 | 콘솔 출력 |
|-----------|---------------|-----------|
| **Debug** | Trace (모든 로그) | 활성화 |
| **Release** | Warning (Warning 이상만) | 비활성화 |

### 4. 사용자 설정 (Release 빌드)

**설정 위치:** `user_data.db` → `UserSettings` 테이블

| 설정 키 | 설명 | 기본값 |
|---------|------|--------|
| `logging.level` | 로그 레벨 (0-6) | 3 (Warning) |
| `logging.maxDays` | 로그 보관 일수 | 7 |
| `logging.maxSizeMB` | 최대 로그 폴더 크기 (MB) | 100 |

**설정 UI:**
- 설정 페이지에 "로깅" 섹션 추가
- 로그 레벨 드롭다운 (None/Critical/Error/Warning/Info/Debug)
- 로그 폴더 열기 버튼
- 로그 삭제 버튼

### 5. 로그 포맷

```
[2025-12-17 14:30:45.123] [INFO] [MainWindow] Application started
[2025-12-17 14:30:45.456] [DEBUG] [QuestDbService] Loaded 245 quests from database
[2025-12-17 14:30:46.789] [ERROR] [MapTrackerService] Failed to connect: Connection refused
    Exception: System.Net.Sockets.SocketException
    at MapTrackerService.Connect() in Services\MapTrackerService.cs:line 123
```

**포맷 구성:**
- `[타임스탬프]` - ISO 8601 형식, 밀리초 포함
- `[레벨]` - 대문자 5자 고정 (TRACE, DEBUG, INFO, WARN, ERROR, CRIT)
- `[소스]` - 클래스명 또는 컴포넌트명
- `메시지` - 실제 로그 내용
- 예외 시 스택 트레이스 포함 (들여쓰기)

### 6. 로그 파일 관리

**자동 정리:**
- 앱 시작 시 오래된 로그 폴더 삭제 (설정된 보관 일수 기준)
- 총 로그 크기가 설정된 최대 크기를 초과하면 오래된 것부터 삭제

**파일 로테이션:**
- 단일 로그 파일이 10MB를 초과하면 `debug.log.1`, `debug.log.2` 등으로 로테이션
- 레벨별 파일당 최대 5개 로테이션

---

## 기술 설계

### 1. 클래스 구조

```csharp
// Services/Logging/ILogger.cs
public interface ILogger
{
    void Trace(string message);
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? ex = null);
    void Critical(string message, Exception? ex = null);
}

// Services/Logging/LogLevel.cs
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6
}

// Services/Logging/LoggingService.cs
public class LoggingService : ILogger
{
    private static LoggingService? _instance;
    public static LoggingService Instance => _instance ??= new LoggingService();

    private readonly string _logDirectory;
    private readonly string _sessionFolder;
    private LogLevel _minimumLevel;

    // 싱글톤 패턴, 세션 폴더 생성, 파일 관리
}

// Services/Logging/LoggerFactory.cs
public static class Log
{
    public static ILogger For<T>() => LoggingService.Instance.CreateLogger<T>();
    public static ILogger For(string category) => LoggingService.Instance.CreateLogger(category);
}
```

### 2. 사용 예시

```csharp
// 기존 코드
System.Diagnostics.Debug.WriteLine($"[MainWindow] Loaded {tasks.Count} quests from DB");

// 새로운 코드
private static readonly ILogger _log = Log.For<MainWindow>();

_log.Info($"Loaded {tasks.Count} quests from DB");

// 예외 처리
try
{
    // ...
}
catch (Exception ex)
{
    _log.Error("Failed to load quests", ex);
}
```

### 3. 조건부 컴파일

```csharp
public LoggingService()
{
    #if DEBUG
        _minimumLevel = LogLevel.Trace;
        _enableConsoleOutput = true;
    #else
        _minimumLevel = LoadLevelFromSettings() ?? LogLevel.Warning;
        _enableConsoleOutput = false;
    #endif
}
```

### 4. 설정 저장 (UserDataDbService 연동)

```csharp
// 설정 로드
var levelStr = SettingsService.Instance.GetValue("logging.level", "3");
var level = (LogLevel)int.Parse(levelStr);

// 설정 저장
SettingsService.Instance.SetValue("logging.level", ((int)level).ToString());
```

---

## 마이그레이션 계획

### Phase 1: 기본 구조 구축
1. `Services/Logging/` 폴더 및 기본 클래스 생성
2. 로그 폴더 구조 생성 로직 구현
3. 파일 쓰기 구현 (비동기, 버퍼링)

### Phase 2: 기존 코드 마이그레이션
1. `Debug.WriteLine` → `_log.Debug()` 변환
2. `Console.WriteLine` → `_log.Info()` 변환
3. 각 서비스/페이지에 logger 인스턴스 추가

### Phase 3: 설정 UI 추가
1. 설정 페이지에 로깅 섹션 추가
2. 로그 레벨 선택 컨트롤
3. 로그 폴더 관리 버튼

### Phase 4: 고급 기능
1. 로그 자동 정리 구현
2. 파일 로테이션 구현
3. 성능 최적화 (배치 쓰기)

---

## 파일 목록 (생성/수정 예정)

### 신규 생성
```
Services/Logging/
├── ILogger.cs              # 로거 인터페이스
├── LogLevel.cs             # 로그 레벨 열거형
├── LoggingService.cs       # 메인 로깅 서비스
├── LoggerFactory.cs        # 로거 팩토리 (Log.For<T>())
├── FileLogWriter.cs        # 파일 쓰기 담당
└── LogCleanupService.cs    # 오래된 로그 정리
```

### 수정 대상 (Debug.WriteLine 사용 파일)
- `MainWindow.xaml.cs`
- `Services/ConfigMigrationService.cs`
- `Services/DatabaseUpdateService.cs`
- `Services/FloorDetectionService.cs`
- `Services/QuestDbService.cs`
- `Services/ItemDbService.cs`
- `Services/MapMarkerDbService.cs`
- `Services/TraderDbService.cs`
- `Services/HideoutDbService.cs`
- `Services/QuestObjectiveDbService.cs`
- `Services/QuestProgressService.cs`
- `Services/MapTrackerService.cs`
- `Services/LogSyncService.cs`
- `Services/SettingsService.cs`
- `Services/LocalizationService.cs`
- `Services/ItemInventoryService.cs`
- `Services/HideoutProgressService.cs`
- `Services/UserDataDbService.cs`
- `Services/MigrationService.cs`
- `Pages/MapTrackerPage.xaml.cs`
- `Pages/ItemsPage.xaml.cs`
- `Pages/CollectorPage.xaml.cs`

---

## 비기능적 요구사항

### 성능
- 로깅으로 인한 UI 지연 최소화 (비동기 쓰기)
- 메모리 사용 최적화 (버퍼 크기 제한)
- 디스크 I/O 최소화 (배치 쓰기, 1초 간격)

### 안정성
- 로그 쓰기 실패 시 앱 크래시 방지
- 디스크 공간 부족 시 graceful 처리
- 동시 접근 안전 (thread-safe)

### 보안
- 민감 정보 로깅 금지 (비밀번호, 토큰 등)
- 로그 파일 접근 권한 관리

---

## 참고

### 기존 로깅 패턴 예시 (현재 코드)
```csharp
// Services/DatabaseUpdateService.cs
Debug.WriteLine($"[DatabaseUpdateService] Local version: {LocalVersion}");
Debug.WriteLine("[DatabaseUpdateService] No local version file found");
Debug.WriteLine($"[DatabaseUpdateService] Error loading local version: {ex.Message}");

// MainWindow.xaml.cs
System.Diagnostics.Debug.WriteLine($"[MainWindow] Loaded {tasks.Count} quests from DB");
System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to load quests: {ex.Message}");
```

### 변환 후 예시
```csharp
// Services/DatabaseUpdateService.cs
private static readonly ILogger _log = Log.For<DatabaseUpdateService>();

_log.Debug($"Local version: {LocalVersion}");
_log.Warning("No local version file found");
_log.Error($"Error loading local version: {ex.Message}");

// MainWindow.xaml.cs
private static readonly ILogger _log = Log.For<MainWindow>();

_log.Info($"Loaded {tasks.Count} quests from DB");
_log.Error($"Failed to load quests: {ex.Message}");
```

---

## 일정

| 단계 | 내용 | 예상 작업량 |
|------|------|------------|
| Phase 1 | 기본 구조 구축 | 4-6시간 |
| Phase 2 | 기존 코드 마이그레이션 | 2-3시간 |
| Phase 3 | 설정 UI 추가 | 2-3시간 |
| Phase 4 | 고급 기능 | 3-4시간 |

**총 예상**: 11-16시간

---

## 승인

| 역할 | 이름 | 날짜 |
|------|------|------|
| 작성자 | Claude | 2025-12-17 |
| 검토자 | | |
| 승인자 | | |
