# Player Level Filter PRD (플레이어 레벨 필터)

## Status: IMPLEMENTED

## Overview
플레이어 레벨을 입력받아 해당 레벨에서 진행 가능한 퀘스트만 Active로 표시하는 기능 구현. 우측 상단에 +/- 버튼이 있는 레벨 입력란을 추가하여 사용자가 쉽게 레벨을 조정할 수 있도록 한다.

## Reference
- 기존 서비스: `QuestProgressService.cs`, `QuestGraphService.cs`
- 관련 모델: `TarkovTask.cs` (`RequiredLevel` 필드)
- UI: `MainWindow.xaml`, `QuestListPage.xaml`

---

## Features

### 1. Player Level Input (플레이어 레벨 입력)

#### 1.1 UI Location
- 위치: 우측 상단 타이틀 바, 언어 선택기 왼쪽
- 기존 버튼들(☕ Support, 🔄 Refresh, ↩ Reset, ⚙)과 같은 영역

#### 1.2 UI Components
```
+-----------------------------------+
| [-] [레벨 숫자] [+]  |  🔄  ↩  ⚙  EN ▼ |
+-----------------------------------+
```

- **Decrement Button (-)**: 레벨 1 감소
- **Level Display**: 현재 레벨 표시 (숫자)
- **Increment Button (+)**: 레벨 1 증가

#### 1.3 Level Range
- 최소: 1
- 최대: 79 (타르코프 최대 레벨)
- 기본값: 15 (첫 실행 시)

#### 1.4 Behavior
- +/- 버튼 클릭 시 레벨 1씩 증감
- 레벨 숫자 직접 클릭 시 입력 모드로 전환 가능 (선택사항)
- 최소/최대 레벨에서는 해당 방향 버튼 비활성화
- 레벨 변경 시 즉시 퀘스트 목록 업데이트

### 2. Quest Level Filtering (퀘스트 레벨 필터링)

#### 2.1 Level Requirement Check
각 퀘스트의 `RequiredLevel` 필드를 현재 플레이어 레벨과 비교:

```csharp
// 레벨 요구사항 충족 여부
bool IsLevelRequirementMet(TarkovTask task, int playerLevel)
{
    if (!task.RequiredLevel.HasValue)
        return true;  // 레벨 요구사항 없음

    return playerLevel >= task.RequiredLevel.Value;
}
```

#### 2.2 Quest Status with Level
기존 QuestStatus에 레벨 조건 추가:

| 기존 상태 | 레벨 충족 | 최종 상태 |
|-----------|-----------|-----------|
| Locked | - | Locked (변경 없음) |
| Active | O | Active |
| Active | X | Level Locked (신규) |
| Done | - | Done (변경 없음) |
| Failed | - | Failed (변경 없음) |

#### 2.3 Level Locked Status
- 선행 퀘스트는 모두 완료했으나 레벨이 부족한 퀘스트
- UI에서 "Lv.XX Required" 형태로 표시
- 기존 Locked와 다른 시각적 구분 (색상 또는 아이콘)

### 3. UI Integration

#### 3.1 Quest List Display
레벨 필터 적용 시 퀘스트 목록 변화:

```
기본 뷰 (Active만 표시):
- 레벨 충족 + Active → 표시
- 레벨 미충족 + Active → "Level Locked" 배지와 함께 표시 또는 숨김 (설정 가능)

전체 뷰:
- 모든 퀘스트에 레벨 요구사항 표시 (해당되는 경우)
```

#### 3.2 Quest Detail Panel
선택된 퀘스트 상세 정보에 레벨 요구사항 표시:

```
+----------------------------+
| Quest Name                 |
+----------------------------+
| Prerequisites              |
| - Level: 15 ⚠ (현재: 12)   |  <- 미충족 시 경고
| - Quest A (Done)           |
| - Quest B (Active)         |
+----------------------------+
```

#### 3.3 Filter Options
퀘스트 필터 영역에 레벨 관련 옵션 추가:

- [x] Show Level Locked quests (레벨 부족 퀘스트 표시)
- 기본값: 체크 (표시)

### 4. Data Persistence

#### 4.1 Settings Storage
`appsettings.json` 또는 별도 설정 파일에 저장:

```json
{
  "playerLevel": 15,
  "showLevelLockedQuests": true
}
```

#### 4.2 Auto-save
- 레벨 변경 시 자동 저장
- 앱 재시작 시 마지막 설정 복원

---

## Bug Fix: Quest Sync Started Status Issue

### Issue Description
퀘스트 동기화 시 "Started" (진행중, message.type == 10) 상태의 퀘스트가 다른 퀘스트의 선행 퀘스트로 판단되어 자동으로 완료 처리되는 버그.

### Current Behavior (버그)
```
로그 이벤트 순서:
1. Quest A Started (type 10)
2. Quest B Started (type 10) - Quest A가 Quest B의 선행 퀘스트인 경우

현재 동작:
- Quest B Started 처리 시
- Quest A가 선행 퀘스트로 감지됨
- Quest A 상태가 Done이 아니므로 자동 완료 처리됨 ← 버그!
- Quest A는 아직 진행중(Started)인데 완료 처리됨
```

### Root Cause
`LogSyncService.cs`의 `SyncFromLogsAsync` 메서드에서:

```csharp
// 현재 코드 (406-432번 라인 근처)
case QuestEventType.Started:
    // ...
    foreach (var prereq in prereqs)
    {
        var currentStatus = progressService.GetStatus(prereq);
        if (currentStatus != QuestStatus.Done)  // 문제: Active 상태도 완료 처리됨
        {
            // 완료 처리
        }
    }
```

### Expected Behavior (수정 후)
- Started 이벤트가 있는 퀘스트는 자동 완료 대상에서 제외
- 명시적으로 Completed 이벤트(type 12)가 있는 퀘스트만 완료 처리

### Solution

#### Option 1: Track Started Quests (권장)
시작된 퀘스트 목록을 별도로 추적하여 자동완료에서 제외:

```csharp
// 시작된 퀘스트 추적
var startedQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

// 이벤트 처리 전 Started 이벤트 먼저 수집
foreach (var evt in events.Where(e => e.EventType == QuestEventType.Started))
{
    var task = FindTaskByQuestId(tasksByQuestId, evt.QuestId);
    if (task?.NormalizedName != null)
        startedQuests.Add(task.NormalizedName);
}

// Started 처리 시 선행 퀘스트 자동완료
case QuestEventType.Started:
    foreach (var prereq in prereqs)
    {
        if (prereq.NormalizedName == null) continue;
        if (processedQuests.Contains(prereq.NormalizedName)) continue;
        if (startedQuests.Contains(prereq.NormalizedName)) continue;  // 추가: Started 제외

        var currentStatus = progressService.GetStatus(prereq);
        if (currentStatus != QuestStatus.Done)
        {
            // 완료 처리
        }
    }
```

#### Option 2: Check Current Active Status
현재 UI 상태가 Active인 퀘스트도 제외:

```csharp
var currentStatus = progressService.GetStatus(prereq);
if (currentStatus != QuestStatus.Done && currentStatus != QuestStatus.Active)
{
    // Locked 상태인 경우만 완료 처리
}
```

### Test Cases
1. Quest A Started → Quest B Started (A는 B의 선행)
   - 예상: A는 Active 유지, B도 Active
   - 버그: A가 Done으로 변경됨

2. Quest A Completed → Quest B Started (A는 B의 선행)
   - 예상: A는 Done, B는 Active
   - 정상 동작

3. Quest B Started (A는 B의 선행, A는 로그에 없음)
   - 예상: A는 Done (암묵적 완료), B는 Active
   - 정상 동작 유지

---

## UI Layout (Updated Header)

```
+------------------------------------------------------------------+
| TARKOV HELPER           [-][15][+] ☕ 🔄 ↩ ⚙  [EN▼]              |
+------------------------------------------------------------------+
|  Quests  |  Hideout  |  Items                                     |
+------------------------------------------------------------------+
```

### Level Control Style
```
+-------------------+
|  Lv.  [-] 15 [+]  |
+-------------------+
```

- "Lv." 라벨 표시 (선택사항)
- 레벨 숫자는 굵게 표시
- +/- 버튼은 작고 컴팩트하게

---

## Implementation Priority

### Phase 1: Bug Fix ✅ DONE
1. ✅ `LogSyncService.cs` Started 퀘스트 추적 로직 추가
2. ✅ 선행 퀘스트 자동완료 시 Started 제외 조건 추가
3. ✅ 테스트 케이스 검증

**구현 내용:**
- `SyncFromLogsAsync()` 메서드에 `startedQuests` HashSet 추가
- Started 이벤트가 있는 퀘스트는 선행 퀘스트 자동완료에서 제외

### Phase 2: Player Level UI ✅ DONE
1. ✅ `MainWindow.xaml` 레벨 입력 UI 컴포넌트 추가
2. ✅ 레벨 증감 이벤트 핸들러 구현
3. ✅ 설정 저장/로드 로직

**구현 내용:**
- 우측 상단에 `Lv. [-] 15 [+]` 형태의 레벨 컨트롤 추가
- `SettingsService.cs`에 `PlayerLevel`, `ShowLevelLockedQuests` 속성 추가
- `app_settings.json`에 레벨 설정 자동 저장/로드

### Phase 3: Level Filtering ✅ DONE
1. ✅ `QuestProgressService`에 레벨 필터 로직 추가
2. ✅ 퀘스트 목록 UI 업데이트 (Level Locked 표시)
3. ✅ 퀘스트 상세 패널에 레벨 요구사항 표시

**구현 내용:**
- `QuestStatus` enum에 `LevelLocked` 상태 추가
- `QuestProgressService.GetStatus()`에서 레벨 체크 로직 추가
- `QuestProgressService.IsLevelRequirementMet()` 메서드 추가
- `QuestListPage`에 LevelLocked 상태에 대한 UI 처리 (주황색 배지)
- 상태 필터에 "Level Locked" 옵션 추가
- 통계 표시에 현재 레벨 및 Level Locked 퀘스트 수 표시
- 상세 패널에 현재 레벨과 요구 레벨 비교 표시 (레벨 미충족 시 주황색)

### Phase 4: Polish ⏳ OPTIONAL
1. ⬜ UI 스타일 정리
2. ⬜ 레벨 부족 퀘스트 표시/숨김 옵션 (ShowLevelLockedQuests 설정 준비됨)
3. ✅ 레벨 요구사항 시각적 강조

---

## Dependencies

### Existing
- `QuestProgressService` - 퀘스트 진행 상태 관리
- `QuestGraphService` - 퀘스트 선행 관계 조회
- `TarkovTask.RequiredLevel` - 레벨 요구사항 데이터

### New
- `PlayerSettingsService` (선택) - 플레이어 레벨 등 설정 관리
  - 또는 기존 설정 시스템에 통합

---

## Notes

### Level Data Source
- `TarkovTask.RequiredLevel` 필드는 Wiki 파싱을 통해 채워짐
- 일부 퀘스트는 레벨 요구사항이 없을 수 있음 (null)
- 레벨 요구사항이 없는 퀘스트는 레벨 필터 영향 없음

### Localization
- "Lv." 또는 "Level" 표기는 언어 설정에 따라 변경
  - EN: "Lv."
  - KO: "레벨" 또는 "Lv."
  - JA: "Lv."

---

## Changed Files (Implementation Summary)

### Models
- `Models/QuestStatus.cs` - Added `LevelLocked` enum value

### Services
- `Services/LogSyncService.cs` - Bug fix: Track started quests to exclude from auto-completion
- `Services/QuestProgressService.cs` - Added `IsLevelRequirementMet()`, updated `GetStatus()` with level check, updated `GetStatistics()` to include LevelLocked count
- `Services/SettingsService.cs` - Added `PlayerLevel`, `ShowLevelLockedQuests` properties and constants

### UI
- `MainWindow.xaml` - Added player level control (Lv. [-][15][+])
- `MainWindow.xaml.cs` - Added level change handlers and UI update logic
- `Pages/QuestListPage.xaml` - Added "Level Locked" status filter option
- `Pages/QuestListPage.xaml.cs` - Added LevelLocked status UI handling, updated statistics display
