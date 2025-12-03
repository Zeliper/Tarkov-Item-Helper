# Quest Log Sync PRD (퀘스트 로그 동기화)

## Overview
타르코프 게임 로그 파일을 분석하여 퀘스트 진행 상태를 자동으로 동기화하는 기능 구현. 이전 로그 히스토리를 스캔하여 시작된 퀘스트를 감지하고, 해당 퀘스트의 선행 퀘스트를 자동 완료 처리하며, 완료된 퀘스트들의 상태를 업데이트한다.

## Reference
- 연계 문서: `docs/LOG_DATA_INTEGRATION.md`
- 기존 서비스: `QuestProgressService.cs`, `QuestGraphService.cs`

---

## Features

### 1. Log File Configuration (로그 파일 설정)

#### 1.1 Log Directory Setting
- 타르코프 로그 디렉토리 경로 설정 UI 제공
- 기본 경로: `%USERPROFILE%\AppData\Roaming\Battlestate Games\EFT\Logs\`
- 경로 유효성 검증 (해당 디렉토리에 로그 파일 존재 여부)
- 설정 저장 및 복원

#### 1.2 Target Log Files
- 주요 대상: `*_push-notifications_*.log` (퀘스트 이벤트 포함)
- 보조 대상: `*_backend_*.log` (레이드 시작/종료 감지)

### 2. Log History Scan (로그 히스토리 스캔)

#### 2.1 Initial Scan Trigger
- 앱 시작 시 자동 스캔 옵션
- 수동 "Sync from Logs" 버튼

#### 2.2 Scan Range
- 기본: 최신 세션의 로그 파일들 스캔
- 옵션: 전체 히스토리 스캔 (지정 기간)

#### 2.3 Scan Process
```
1. 로그 디렉토리의 push-notifications 파일 목록 조회
2. 날짜순 정렬 (오래된 것부터)
3. 각 파일을 순차적으로 파싱
4. 퀘스트 이벤트 추출 및 처리
5. 결과 요약 표시
```

### 3. Quest Event Parsing (퀘스트 이벤트 파싱)

#### 3.1 Log Line Format
```
{날짜} {시간}|{게임버전}|{레벨}|{카테고리}|{메시지}
```

**예시:**
```
2025-12-02 07:50:52.104|1.0.0.2.42157|Info|push-notifications|Got notification | new_message
```

#### 3.2 Quest Event JSON Structure
```json
{
  "type": "new_message",
  "dialogId": "5935c25fb3acc3127c3d8cd9",
  "message": {
    "type": 12,
    "templateId": "6160538a5b5c163161503c11 successMessageText 5935c25fb3acc3127c3d8cd9 0",
    "text": "quest started",
    "dt": 1764621873
  }
}
```

#### 3.3 Message Type Codes

| message.type | 의미 | templateId 패턴 | 처리 |
|--------------|------|-----------------|------|
| 10 | 퀘스트 시작 | `{questId} description` | Active 마킹 + 선행 퀘스트 완료 |
| 11 | 퀘스트 실패 | `{questId} failMessageText` | Failed 마킹 |
| 12 | 퀘스트 완료 | `{questId} successMessageText {traderId} 0` | Done 마킹 |

#### 3.4 Parsing Logic
```csharp
public class QuestLogEvent
{
    public string QuestId { get; set; }           // templateId의 첫 토큰
    public QuestEventType EventType { get; set; } // Started, Completed, Failed
    public string TraderId { get; set; }          // dialogId
    public DateTime Timestamp { get; set; }       // dt 유닉스 타임스탬프
}

public enum QuestEventType
{
    Started,    // message.type == 10
    Completed,  // message.type == 12
    Failed      // message.type == 11
}
```

### 4. Quest Sync Logic (퀘스트 동기화 로직)

#### 4.1 On Quest Started Event
```
1. questId로 TarkovTask 매칭 (TarkovTask.Ids 필드 검색)
2. 해당 퀘스트의 모든 선행 퀘스트 조회 (QuestGraphService.GetAllPrerequisites)
3. 선행 퀘스트들을 타임스탬프 역순으로 자동 완료 처리
4. 해당 퀘스트를 Active 상태로 마킹
```

**선행 퀘스트 자동완료 규칙:**
- 퀘스트가 "시작"되었다는 것은 모든 선행 퀘스트가 완료되었음을 의미
- 재귀적으로 모든 선행 체인을 완료 처리
- 이미 Done 상태인 퀘스트는 스킵

#### 4.2 On Quest Completed Event
```
1. questId로 TarkovTask 매칭
2. 해당 퀘스트를 Done 상태로 마킹
3. 선행 퀘스트가 미완료 상태이면 함께 완료 처리
```

#### 4.3 On Quest Failed Event
```
1. questId로 TarkovTask 매칭
2. 해당 퀘스트를 Failed 상태로 마킹
3. 선행 퀘스트 상태는 변경하지 않음
```

#### 4.4 Quest ID Matching

tarkov.dev API의 퀘스트 ID와 게임 로그의 questId 매칭:

```csharp
// TarkovTask 모델에 Ids 필드 활용
public class TarkovTask
{
    public List<string>? Ids { get; set; }  // 게임 내부 ID 목록
    public string? NormalizedName { get; set; }
    // ...
}

// 매칭 로직
var matchedTask = tasks.FirstOrDefault(t =>
    t.Ids?.Contains(questId, StringComparer.OrdinalIgnoreCase) == true
);
```

### 5. Trader ID Reference (트레이더 ID 참조)

| Trader ID | EN Name | KO Name |
|-----------|---------|---------|
| `54cb50c76803fa8b248b4571` | Prapor | 프라퍼 |
| `54cb57776803fa99248b456e` | Therapist | 테라피스트 |
| `58330581ace78e27b8b10cee` | Skier | 스키어 |
| `5935c25fb3acc3127c3d8cd9` | Peacekeeper | 피스키퍼 |
| `5a7c2eca46aef81a7ca2145d` | Mechanic | 메카닉 |
| `5ac3b934156ae10c4430e83c` | Ragman | 래그맨 |
| `5c0647fdd443bc2504c2d371` | Jaeger | 예거 |
| `638f541a29ffd1183d187f57` | Lightkeeper | 라이트키퍼 |
| `656f0f98d80a697f855d34b1` | Ref | 레프 |

### 6. User Interface

#### 6.1 Settings Page - Log Sync Section
```
+------------------------------------------------------------------+
| Log Sync Settings                                                 |
+------------------------------------------------------------------+
| Log Directory:                                                    |
| [C:\Users\...\Battlestate Games\EFT\Logs          ] [Browse]     |
|                                                                   |
| [✓] Auto-sync on app start                                       |
|                                                                   |
| [ Sync from Logs ]  [ Reset Progress ]                           |
+------------------------------------------------------------------+
```

#### 6.2 Sync Progress Dialog
```
+------------------------------------------------------------------+
| Syncing Quest Progress from Logs...                               |
+------------------------------------------------------------------+
| Scanning log files...                                             |
| ████████████░░░░░░░░░░░░░░░░░░░░░░░░░░  35%                      |
|                                                                   |
| Found: 42 quest events                                            |
| - Started: 28                                                     |
| - Completed: 12                                                   |
| - Failed: 2                                                       |
|                                                                   |
| Processing...                                                     |
+------------------------------------------------------------------+
| [Cancel]                                                          |
+------------------------------------------------------------------+
```

#### 6.3 Sync Result Summary
```
+------------------------------------------------------------------+
| Quest Sync Complete                                               |
+------------------------------------------------------------------+
| Summary:                                                          |
|                                                                   |
| Quests marked as completed: 35                                    |
|   - From log events: 12                                           |
|   - Auto-completed prerequisites: 23                              |
|                                                                   |
| Quests marked as failed: 2                                        |
|                                                                   |
| Unmatched quest IDs: 3                                            |
|   - 5a27b80086f7742e3a2a0001 (unknown)                           |
|   - 5a27b81286f7742e3a2a0002 (unknown)                           |
|   - 5a27b82586f7742e3a2a0003 (unknown)                           |
|                                                                   |
+------------------------------------------------------------------+
| [OK]                                                              |
+------------------------------------------------------------------+
```

### 7. Data Persistence

#### 7.1 Settings Storage
```json
{
  "logSyncSettings": {
    "logDirectory": "C:\\Users\\...\\Battlestate Games\\EFT\\Logs",
    "autoSyncOnStart": true,
    "lastSyncTimestamp": "2025-12-02T08:00:00Z",
    "lastScannedLogFile": "2025-12-02_07-45-00_1.0.0.2.42157 push-notifications_000.log"
  }
}
```

#### 7.2 Sync State
- 마지막 동기화 시점 저장
- 이미 처리된 로그 파일 목록 저장 (중복 처리 방지)
- 증분 동기화 지원 (새 로그만 처리)

### 8. Error Handling

#### 8.1 File Access Errors
- 게임 실행 중 파일 잠금 처리
- 읽기 전용 모드로 파일 접근
- 파일 접근 실패 시 graceful skip

#### 8.2 Parse Errors
- JSON 파싱 실패 시 해당 라인 스킵
- 알 수 없는 메시지 타입 무시
- 매칭 실패한 questId 로깅

#### 8.3 Quest ID Mismatch
- 매칭 실패한 ID들 별도 로깅
- 디버그 모드에서 상세 정보 표시
- 수동 매핑 옵션 (향후 확장)

---

## Technical Implementation

### Services Structure

```
Services/
├── LogSyncService.cs             # 메인 동기화 서비스
├── LogWatcher/
│   ├── TarkovLogParser.cs        # 로그 파일 파싱
│   ├── PushNotificationParser.cs # push-notifications 파싱
│   └── QuestEventExtractor.cs    # 퀘스트 이벤트 추출
└── Models/
    └── QuestLogEvent.cs          # 퀘스트 로그 이벤트 모델
```

### LogSyncService Interface

```csharp
public class LogSyncService
{
    private readonly QuestProgressService _progressService;
    private readonly QuestGraphService _graphService;

    public event EventHandler<SyncProgressEventArgs> SyncProgress;
    public event EventHandler<SyncCompletedEventArgs> SyncCompleted;

    // 설정
    public string LogDirectory { get; set; }
    public bool AutoSyncOnStart { get; set; }

    // 동기화
    public async Task<SyncResult> SyncFromLogsAsync(CancellationToken ct = default);
    public async Task<SyncResult> SyncIncrementalAsync(CancellationToken ct = default);

    // 로그 파싱
    public async Task<List<QuestLogEvent>> ParseLogFilesAsync(IEnumerable<string> filePaths);

    // 퀘스트 상태 적용
    public void ApplyQuestEvents(List<QuestLogEvent> events);
}

public class SyncResult
{
    public int TotalEventsFound { get; set; }
    public int QuestsCompleted { get; set; }
    public int QuestsFailed { get; set; }
    public int PrerequisitesAutoCompleted { get; set; }
    public List<string> UnmatchedQuestIds { get; set; }
    public List<string> Errors { get; set; }
}
```

### Quest Event Processing Flow

```
[Log File]
    │
    ▼
[TarkovLogParser] ─── 로그 라인 파싱
    │
    ▼
[PushNotificationParser] ─── JSON 추출 및 파싱
    │
    ▼
[QuestEventExtractor] ─── 퀘스트 이벤트 필터링
    │
    ▼
[QuestLogEvent] ─── 이벤트 객체 생성
    │
    ▼
[LogSyncService.ApplyQuestEvents]
    │
    ├─── Quest Started
    │       │
    │       ▼
    │    [QuestGraphService.GetAllPrerequisites]
    │       │
    │       ▼
    │    [QuestProgressService.CompleteQuest] × N (선행 퀘스트들)
    │
    ├─── Quest Completed
    │       │
    │       ▼
    │    [QuestProgressService.CompleteQuest]
    │
    └─── Quest Failed
            │
            ▼
         [QuestProgressService.FailQuest]
```

---

## Implementation Priority

### Phase 1: Core Infrastructure
1. `QuestLogEvent` 모델 생성
2. `TarkovLogParser` - 로그 라인 기본 파싱
3. `PushNotificationParser` - push-notifications JSON 파싱
4. `QuestEventExtractor` - 퀘스트 이벤트 추출

### Phase 2: Sync Logic
1. `LogSyncService` 기본 구조
2. Quest ID → TarkovTask 매칭 로직
3. 선행 퀘스트 자동완료 로직
4. `QuestProgressService` 연동

### Phase 3: UI Integration
1. Settings 페이지 로그 경로 설정 UI
2. Sync 버튼 및 진행률 표시
3. 동기화 결과 요약 다이얼로그

### Phase 4: Enhancement
1. 증분 동기화 (새 로그만 처리)
2. 자동 동기화 옵션
3. 실시간 로그 감시 (FileSystemWatcher) - 향후 확장

---

## Dependencies

### Existing
- `QuestProgressService` - 퀘스트 진행 상태 관리
- `QuestGraphService` - 퀘스트 선행 관계 조회
- `TarkovDataService` - 퀘스트 데이터 로드

### New
- `LogSyncService` - 로그 동기화 메인 서비스
- `TarkovLogParser` - 로그 파일 파싱
- `PushNotificationParser` - 알림 JSON 파싱

---

## Testing Considerations

### Test Cases
1. 정상적인 로그 파일 파싱
2. 다양한 퀘스트 이벤트 타입 처리
3. 선행 퀘스트 체인 자동완료
4. 매칭 실패한 questId 처리
5. 파일 접근 오류 처리
6. 빈 로그 파일 처리
7. 중복 이벤트 처리 (동일 퀘스트 여러번 완료)

### Sample Log Data
테스트용 샘플 로그 데이터는 `docs/LOG_DATA_INTEGRATION.md` 참조
