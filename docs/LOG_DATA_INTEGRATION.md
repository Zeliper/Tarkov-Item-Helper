# Tarkov 게임 로그 - 데이터 연계 분석서

## 개요

이 문서는 Escape from Tarkov 게임 로그 파일과 TarkovHelper의 기존 데이터(퀘스트, 아이템, 트레이더)를 연계하여 활용할 수 있는 방안을 상세히 기술합니다.

---

## 1. 로그 파일 구조 분석

### 1.1 로그 파일 종류

| 파일명 패턴 | 설명 | 갱신 주기 |
|------------|------|----------|
| `{날짜}_{시간}_{버전} push-notifications_000.log` | 서버 푸시 알림 (퀘스트, 거래, 메시지) | 실시간 |
| `{날짜}_{시간}_{버전} backend_000.log` | API 요청/응답 기록 | 실시간 |
| `{날짜}_{시간}_{버전} output_000.log` | 게임 출력 로그 (레이드, 시스템) | 실시간 |
| `{날짜}_{시간}_{버전} application_000.log` | 애플리케이션 설정 및 상태 | 시작 시 |
| `{날짜}_{시간}_{버전} errors_000.log` | 에러 로그 | 발생 시 |

### 1.2 로그 포맷

```
{날짜} {시간}|{게임버전}|{레벨}|{카테고리}|{메시지}
```

**예시:**
```
2025-12-02 07:50:52.104|1.0.0.2.42157|Info|push-notifications|Got notification | RagfairOfferSold
```

---

## 2. Push Notifications 로그 연계

### 2.1 퀘스트 이벤트 (Quest Events)

#### 데이터 구조
```json
{
  "type": "new_message",
  "eventId": "692dfe31f1c03795170d3fa2",
  "dialogId": "5935c25fb3acc3127c3d8cd9",
  "message": {
    "_id": "692dfe31e993400b6e0184c242",
    "uid": "5935c25fb3acc3127c3d8cd9",
    "type": 12,
    "dt": 1764621873,
    "text": "quest started",
    "templateId": "6160538a5b5c163161503c11 successMessageText 5935c25fb3acc3127c3d8cd9 0",
    "items": { ... },
    "hasRewards": true
  }
}
```

#### 필드 매핑

| 로그 필드 | 설명 | 연계 데이터 | 매핑 방법 |
|----------|------|------------|----------|
| `dialogId` | 트레이더 고유 ID | `TarkovTrader.Id` | 직접 매칭 |
| `uid` | 발신자 ID (트레이더) | `TarkovTrader.Id` | 직접 매칭 |
| `templateId` | 퀘스트 ID + 메시지 타입 | `TarkovTask.Ids` | 첫 번째 토큰 추출 |
| `message.type` | 메시지 유형 코드 | - | 아래 표 참조 |
| `text` | 메시지 텍스트 | - | 상태 판별용 |

#### 메시지 타입 코드

| type 값 | 의미 | templateId 패턴 |
|---------|------|-----------------|
| 10 | 퀘스트 시작 | `{questId} description` |
| 11 | 퀘스트 실패 | `{questId} failMessageText` |
| 12 | 퀘스트 완료 | `{questId} successMessageText {traderId} 0` |
| 4 | 판매 완료 알림 | `5bdabfb886f7743e152e867e 0` |

#### 연계 구현 로직

```csharp
// templateId 파싱
string[] parts = templateId.Split(' ');
string questId = parts[0];  // 퀘스트 ID

// TarkovTask와 매칭
var matchedQuest = tasks.FirstOrDefault(t => t.Ids?.Contains(questId) == true);

// 트레이더 매칭
var trader = traders.FirstOrDefault(t => t.Id == dialogId);

// 메시지 타입으로 상태 판별
string status = parts.Length > 1 ? parts[1] : "";
// "successMessageText" -> 완료
// "failMessageText" -> 실패
// "description" -> 시작
```

---

### 2.2 플리마켓 판매 (Ragfair Sales)

#### 데이터 구조
```json
{
  "type": "RagfairOfferSold",
  "eventId": "692e1bcc11b874541301b749",
  "offerId": "692e1bc969a2bdd69406a655a",
  "handbookId": "5673de654bdc2d180f8b456d",
  "count": 1
}
```

#### 필드 매핑

| 로그 필드 | 설명 | 연계 데이터 | 매핑 방법 |
|----------|------|------------|----------|
| `handbookId` | 아이템 템플릿 ID | `TarkovItem.Id` | 직접 매칭 |
| `count` | 판매 수량 | - | 수량 계산용 |
| `offerId` | 거래 고유 ID | - | 거래 추적용 |

#### 연계 구현 로직

```csharp
// 판매된 아이템 식별
var soldItem = items.FirstOrDefault(i => i.Id == handbookId);

// 퀘스트 필요 아이템인지 확인
var questsNeedingItem = tasks.Where(t =>
    t.RequiredItems?.Any(ri => ri.ItemNormalizedName == soldItem?.NormalizedName) == true
).ToList();

if (questsNeedingItem.Any())
{
    // 경고: 퀘스트 필요 아이템 판매됨
    foreach (var quest in questsNeedingItem)
    {
        var requirement = quest.RequiredItems.First(ri =>
            ri.ItemNormalizedName == soldItem.NormalizedName);

        // FIR 필수 아이템이면 더 강한 경고
        if (requirement.FoundInRaid)
        {
            WarnCritical($"FIR 필수 아이템 '{soldItem.Name}' 판매됨! 퀘스트: {quest.Name}");
        }
    }
}
```

---

### 2.3 트레이더 메시지 (Trader Messages)

#### 데이터 구조 (판매 수익 수령)
```json
{
  "type": "new_message",
  "dialogId": "5ac3b934156ae10c4430e83c",
  "message": {
    "type": 4,
    "templateId": "5bdabfb886f7743e152e867e 0",
    "systemData": {
      "buyerNickname": "Baldy010",
      "soldItem": "5673de654bdc2d180f8b456d",
      "itemCount": 1
    },
    "items": {
      "data": [
        {
          "_tpl": "5449016a4bdc2d6f028b456f",
          "upd": { "StackObjectsCount": 9800 }
        }
      ]
    }
  }
}
```

#### 필드 매핑

| 로그 필드 | 설명 | 연계 데이터 | 매핑 방법 |
|----------|------|------------|----------|
| `dialogId` | 트레이더 ID | `TarkovTrader.Id` | 직접 매칭 |
| `systemData.soldItem` | 판매된 아이템 ID | `TarkovItem.Id` | 직접 매칭 |
| `items.data[].\_tpl` | 수령 아이템 ID | `TarkovItem.Id` | 직접 매칭 |
| `items.data[].upd.StackObjectsCount` | 수량 | - | 루블 등 스택 아이템 |

---

## 3. Backend 로그 연계

### 3.1 주요 API 엔드포인트

| 엔드포인트 | 설명 | 활용 |
|-----------|------|------|
| `/client/quest/list` | 플레이어 퀘스트 목록 | 현재 활성 퀘스트 파악 |
| `/client/quest/getMainQuestsList` | 메인 퀘스트 목록 | 전체 퀘스트 데이터 |
| `/client/items` | 아이템 데이터 | 아이템 정보 갱신 |
| `/client/trading/api/traderSettings` | 트레이더 설정 | 트레이더 레벨/호감도 |
| `/client/game/profile/list` | 프로필 목록 | 캐릭터 정보 |
| `/client/raid/configuration` | 레이드 설정 | 레이드 시작 감지 |
| `/client/hideout/production/recipes` | 은신처 제작법 | 제작 가능 아이템 |

### 3.2 레이드 시작 감지

```
---> Request HTTPS: /client/raid/configuration
```

이 요청이 감지되면 레이드가 시작되었음을 의미합니다.

---

## 4. Output 로그 연계

### 4.1 레이드 세션 정보

#### 데이터 패턴
```
[Transit] Flag:Common, RaidId:692dfe8b77b61ecd6f105495, Count:0, Locations:Woods
```

#### 필드 추출

| 필드 | 설명 | 활용 |
|------|------|------|
| `RaidId` | 레이드 고유 ID | 세션 추적 |
| `Locations` | 맵 이름 | 맵별 퀘스트 필터링 |

#### 연계 구현 로직

```csharp
// 맵 이름 추출
var match = Regex.Match(logLine, @"Locations:(\w+)");
if (match.Success)
{
    string currentMap = match.Groups[1].Value;  // "Woods"

    // 해당 맵 관련 퀘스트 필터링
    // (퀘스트 목표 위치가 현재 맵인 퀘스트 우선 표시)
}
```

### 4.2 아이템 생성/획득 정보

#### 데이터 패턴
```json
{
  "_tpl": "569668774bdc2da2298b4568",
  "upd": {
    "SpawnedInSession": true,
    "StackObjectsCount": 235
  }
}
```

#### 필드 매핑

| 로그 필드 | 설명 | 연계 데이터 | 매핑 방법 |
|----------|------|------------|----------|
| `_tpl` | 아이템 템플릿 ID | `TarkovItem.Id` | 직접 매칭 |
| `SpawnedInSession` | Found in Raid 여부 | `QuestItem.FoundInRaid` | 조건 확인 |
| `StackObjectsCount` | 수량 | `QuestItem.Amount` | 수량 비교 |

---

## 5. 트레이더 ID 매핑 테이블

| 트레이더 ID | 트레이더 이름 (EN) | 트레이더 이름 (KO) |
|------------|-------------------|-------------------|
| `54cb50c76803fa8b248b4571` | Prapor | 프라퍼 |
| `54cb57776803fa99248b456e` | Therapist | 테라피스트 |
| `58330581ace78e27b8b10cee` | Skier | 스키어 |
| `5935c25fb3acc3127c3d8cd9` | Peacekeeper | 피스키퍼 |
| `5a7c2eca46aef81a7ca2145d` | Mechanic | 메카닉 |
| `5ac3b934156ae10c4430e83c` | Ragman | 래그맨 |
| `5c0647fdd443bc2504c2d371` | Jaeger | 예거 |
| `638f541a29ffd1183d187f57` | Lightkeeper | 라이트키퍼 |
| `656f0f98d80a697f855d34b1` | Ref | 레프 |

---

## 6. 아이템 ID 예시 (자주 사용됨)

| 아이템 ID | 아이템 이름 | 비고 |
|----------|------------|------|
| `5449016a4bdc2d6f028b456f` | Roubles | 루블 (화폐) |
| `5696686a4bdc2da3298b456a` | Dollars | 달러 (화폐) |
| `569668774bdc2da2298b4568` | Euros | 유로 (화폐) |
| `5673de654bdc2d180f8b456d` | NaCl | 소금 (퀘스트 아이템) |

---

## 7. 구현 제안: LogIntegrationService

### 7.1 서비스 구조

```
Services/
├── LogIntegrationService.cs      # 메인 통합 서비스
├── LogWatcher/
│   ├── ILogWatcher.cs            # 로그 감시 인터페이스
│   ├── FileLogWatcher.cs         # 파일 시스템 감시
│   └── LogParser.cs              # 로그 파싱
├── EventHandlers/
│   ├── QuestEventHandler.cs      # 퀘스트 이벤트 처리
│   ├── RagfairEventHandler.cs    # 플리마켓 이벤트 처리
│   ├── RaidEventHandler.cs       # 레이드 이벤트 처리
│   └── ItemEventHandler.cs       # 아이템 이벤트 처리
└── Models/
    ├── LogEvent.cs               # 로그 이벤트 기본 모델
    ├── QuestLogEvent.cs          # 퀘스트 로그 이벤트
    ├── RagfairLogEvent.cs        # 플리마켓 로그 이벤트
    └── RaidLogEvent.cs           # 레이드 로그 이벤트
```

### 7.2 핵심 클래스 설계

```csharp
public class LogIntegrationService
{
    private readonly TarkovDataService _dataService;
    private readonly FileSystemWatcher _watcher;

    // 이벤트
    public event EventHandler<QuestCompletedEventArgs> QuestCompleted;
    public event EventHandler<QuestStartedEventArgs> QuestStarted;
    public event EventHandler<ItemSoldEventArgs> ItemSold;
    public event EventHandler<RaidStartedEventArgs> RaidStarted;
    public event EventHandler<FirItemAcquiredEventArgs> FirItemAcquired;

    // 경고
    public event EventHandler<QuestItemSoldWarningEventArgs> QuestItemSoldWarning;
}
```

### 7.3 로그 파싱 예시

```csharp
public class PushNotificationParser
{
    public LogEvent Parse(string logLine)
    {
        // JSON 부분 추출
        int jsonStart = logLine.IndexOf('{');
        if (jsonStart < 0) return null;

        string json = logLine.Substring(jsonStart);
        var notification = JsonSerializer.Deserialize<PushNotification>(json);

        return notification.Type switch
        {
            "RagfairOfferSold" => ParseRagfairSold(notification),
            "new_message" => ParseNewMessage(notification),
            _ => null
        };
    }

    private QuestLogEvent ParseQuestEvent(PushNotification notification)
    {
        var message = notification.Message;
        var templateParts = message.TemplateId.Split(' ');

        return new QuestLogEvent
        {
            QuestId = templateParts[0],
            TraderId = notification.DialogId,
            Status = DetermineQuestStatus(templateParts),
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(message.Dt).DateTime,
            HasRewards = message.HasRewards
        };
    }
}
```

---

## 8. 활용 시나리오

### 8.1 퀘스트 진행률 자동 추적

```
┌─────────────────────────────────────┐
│ 퀘스트 진행 상태                      │
├─────────────────────────────────────┤
│ ✅ Gunsmith - Part 1 (완료)          │
│ 🔄 Gunsmith - Part 2 (진행중)        │
│    └─ 자동 감지: 05:44:31            │
│ ⏳ Gunsmith - Part 3 (대기)          │
└─────────────────────────────────────┘
```

### 8.2 판매 경고 시스템

```
┌─────────────────────────────────────┐
│ ⚠️ 경고: 퀘스트 아이템 판매됨          │
├─────────────────────────────────────┤
│ 아이템: Flash Drive                  │
│ 수량: 1개                            │
│ 필요 퀘스트:                         │
│   - What's on the Flash Drive?      │
│     (FIR 필수, 2개 필요)             │
│   - Shaking Up Teller               │
│     (FIR 필수, 1개 필요)             │
└─────────────────────────────────────┘
```

### 8.3 맵 기반 퀘스트 추천

```
┌─────────────────────────────────────┐
│ 🗺️ 현재 맵: Woods                    │
├─────────────────────────────────────┤
│ 이 맵에서 할 수 있는 퀘스트:           │
│ 1. The Tarkov Shooter - Part 1      │
│ 2. The Huntsman Path - Secured      │
│ 3. Shturman                         │
└─────────────────────────────────────┘
```

---

## 9. 기술적 고려사항

### 9.1 로그 파일 감시

- `FileSystemWatcher` 사용하여 실시간 감시
- 로그 파일 롤오버 처리 (새 세션마다 새 파일)
- 파일 잠금 처리 (게임이 파일 사용 중)

### 9.2 성능 최적화

- 로그 파싱은 별도 스레드에서 처리
- 필요한 이벤트만 필터링
- 메모리 효율적인 스트림 읽기

### 9.3 데이터 동기화

- tarkov.dev API 데이터와 로그 ID 매핑 테이블 유지
- 게임 업데이트 시 ID 변경 가능성 대비
- 매핑 실패 시 graceful degradation

---

## 10. 향후 확장 가능성

1. **통계 대시보드**: 레이드 성공률, 킬/데스 비율, 수익 분석
2. **알림 시스템**: Windows 토스트 알림으로 중요 이벤트 표시
3. **히스토리 로깅**: 퀘스트 완료 이력, 아이템 거래 기록
4. **다중 프로필 지원**: 여러 캐릭터 추적
5. **오버레이 UI**: 게임 내 오버레이로 정보 표시

---

## 부록: 정규식 패턴

```csharp
// 로그 라인 파싱
Regex LogLinePattern = new Regex(
    @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<time>\d{2}:\d{2}:\d{2}\.\d{3})\|" +
    @"(?<version>[\d.]+)\|(?<level>\w+)\|(?<category>[\w-]+)\|(?<message>.+)$"
);

// 맵 이름 추출
Regex MapPattern = new Regex(@"Locations:(\w+)");

// 레이드 ID 추출
Regex RaidIdPattern = new Regex(@"RaidId:([a-f0-9]+)");

// JSON 추출
Regex JsonPattern = new Regex(@"\{[\s\S]*\}");
```
