# Collector Quest PRD (Kappa 컨테이너 퀘스트 처리)

## Overview
Collector 퀘스트는 Fence가 제공하는 최종 퀘스트로, Kappa 컨테이너를 보상으로 획득할 수 있다. 이 퀘스트는 특별한 선행 조건을 가지며, `reqKappa == true`인 모든 퀘스트를 완료해야 시작할 수 있다.

## Reference
- Wiki: https://escapefromtarkov.fandom.com/wiki/Collector
- 기존 서비스: `QuestGraphService.cs`, `QuestProgressService.cs`, `TarkovDataService.cs`

---

## Features

### 1. Collector 퀘스트 특수 처리

#### 1.1 Dynamic Previous 생성
Collector 퀘스트의 `Previous` 필드는 정적으로 정의되지 않고, 런타임에 `reqKappa == true`인 모든 퀘스트들로 채워져야 한다.

**핵심 로직:**
```csharp
// Collector 퀘스트 식별
var collectorQuest = tasks.FirstOrDefault(t =>
    t.NormalizedName == "collector" ||
    t.Name.Equals("Collector", StringComparison.OrdinalIgnoreCase));

if (collectorQuest != null)
{
    // reqKappa가 true인 모든 퀘스트를 선행 퀘스트로 설정
    var kappaRequiredQuests = tasks
        .Where(t => t.ReqKappa && t.NormalizedName != "collector")
        .Select(t => t.NormalizedName)
        .ToList();

    collectorQuest.Previous = kappaRequiredQuests;
}
```

#### 1.2 Collector 퀘스트 정보

| 필드 | 값 |
|------|-----|
| Name | Collector |
| NameKo | 수집가 |
| NameJa | コレクター |
| Trader | Fence |
| RequiredLevel | 71 (최소) |
| ReqKappa | false (이 퀘스트 자체는 Kappa 요구사항이 아님) |
| Previous | [모든 reqKappa == true 퀘스트] |

### 2. 구현 위치

#### 2.1 TarkovDataService.cs 수정
`FetchTaskDataAsync` 또는 `LoadTasksAsync` 메서드에서 퀘스트 데이터 로드 후 Collector 퀘스트의 Previous를 동적으로 설정한다.

```csharp
/// <summary>
/// Collector 퀘스트의 선행 퀘스트를 동적으로 설정
/// reqKappa == true인 모든 퀘스트를 Previous에 추가
/// </summary>
private void SetupCollectorQuestPrerequisites(List<TarkovTask> tasks)
{
    var collectorQuest = tasks.FirstOrDefault(t =>
        t.NormalizedName?.Equals("collector", StringComparison.OrdinalIgnoreCase) == true);

    if (collectorQuest == null)
        return;

    // reqKappa가 true인 모든 퀘스트 수집 (Collector 자신 제외)
    var kappaRequiredQuests = tasks
        .Where(t => t.ReqKappa &&
                    !t.NormalizedName?.Equals("collector", StringComparison.OrdinalIgnoreCase) == true)
        .Select(t => t.NormalizedName!)
        .Where(name => !string.IsNullOrEmpty(name))
        .ToList();

    // Previous 필드 설정 (기존 값과 병합)
    collectorQuest.Previous ??= new List<string>();
    foreach (var questName in kappaRequiredQuests)
    {
        if (!collectorQuest.Previous.Contains(questName))
        {
            collectorQuest.Previous.Add(questName);
        }
    }
}
```

#### 2.2 호출 시점
```csharp
// FetchTaskDataAsync 메서드 내
var tasks = await LoadTasksFromApiAsync();
SetupCollectorQuestPrerequisites(tasks);  // 추가
await SaveTasksAsync(tasks);

// 또는 LoadTasksAsync 메서드 내 (캐시된 데이터 로드 시)
var tasks = LoadTasksFromCache();
SetupCollectorQuestPrerequisites(tasks);  // 매번 호출
return tasks;
```

### 3. QuestGraphService 연동

#### 3.1 GetAllPrerequisites 호출 시
Collector 퀘스트의 선행 퀘스트 조회 시 모든 reqKappa 퀘스트가 반환되어야 한다.

```csharp
// 예시: Collector 퀘스트의 선행 퀘스트 조회
var prerequisites = questGraphService.GetAllPrerequisites("collector");
// 결과: [모든 reqKappa == true 퀘스트의 normalizedName]
```

#### 3.2 퀘스트 완료 시 역방향 업데이트
reqKappa 퀘스트가 완료될 때, Collector 퀘스트의 진행 상황도 업데이트해야 한다.

```csharp
// Collector 퀘스트 진행률 계산
public int GetCollectorProgress()
{
    var kappaQuests = tasks.Where(t => t.ReqKappa).ToList();
    var completedCount = kappaQuests.Count(t => IsQuestCompleted(t.NormalizedName));
    return completedCount * 100 / kappaQuests.Count;
}
```

### 4. UI 표시

#### 4.1 Collector 퀘스트 상세 화면
```
+------------------------------------------------------------------+
| Collector (수집가)                                    Fence       |
+------------------------------------------------------------------+
| Required Level: 71                                                |
|                                                                   |
| Prerequisites: (45/52 completed)                                  |
| ████████████████████████████████░░░░░░░░░  87%                   |
|                                                                   |
| [Show All Required Quests]                                        |
|                                                                   |
| Rewards:                                                          |
| - Secure container Kappa                                          |
| - Armband (DEADSKUL)                                              |
+------------------------------------------------------------------+
```

#### 4.2 reqKappa 퀘스트 목록 표시
Collector 퀘스트 상세 화면에서 "Show All Required Quests" 버튼 클릭 시:

```
+------------------------------------------------------------------+
| Kappa Required Quests (45/52)                                     |
+------------------------------------------------------------------+
| ✓ Debut (데뷔)                                         Prapor    |
| ✓ Checking (확인)                                      Prapor    |
| ✓ Shootout Picnic (총격 피크닉)                        Prapor    |
| ○ The Huntsman Path - Trophy (사냥꾼의 길 - 트로피)    Jaeger    |
| ○ Mentor (멘토)                                        Peacekeeper|
| ...                                                               |
+------------------------------------------------------------------+
```

### 5. 데이터 검증

#### 5.1 reqKappa 데이터 무결성 검사
```csharp
/// <summary>
/// reqKappa 데이터 검증
/// - Collector 퀘스트가 존재하는지
/// - reqKappa가 true인 퀘스트가 충분한지 (최소 40개 이상)
/// </summary>
public void ValidateKappaData(List<TarkovTask> tasks)
{
    var collector = tasks.FirstOrDefault(t =>
        t.NormalizedName == "collector");

    if (collector == null)
    {
        Console.WriteLine("Warning: Collector quest not found");
        return;
    }

    var kappaCount = tasks.Count(t => t.ReqKappa);
    Console.WriteLine($"Found {kappaCount} reqKappa quests");

    if (kappaCount < 40)
    {
        Console.WriteLine("Warning: Expected at least 40 reqKappa quests");
    }
}
```

---

## Implementation Steps

### Phase 1: Core Logic
1. `TarkovDataService.cs`에 `SetupCollectorQuestPrerequisites` 메서드 추가
2. 퀘스트 데이터 로드 시점에서 메서드 호출
3. 단위 테스트 작성

### Phase 2: Graph Service 연동
1. `QuestGraphService.GetAllPrerequisites`에서 Collector 퀘스트 테스트
2. 역방향 조회 (어떤 퀘스트가 Kappa 요구사항인지) 지원

### Phase 3: UI 연동
1. Collector 퀘스트 상세 화면에서 진행률 표시
2. reqKappa 퀘스트 목록 팝업/다이얼로그 구현
3. 퀘스트리스트에서 각 퀘스트에 Kappa 여부 뱃지 표시

---

## Edge Cases

### 1. 신규 시즌/와이프 대응
- 게임 업데이트 시 reqKappa 목록이 변경될 수 있음
- Wiki 데이터 갱신 시 자동으로 Previous 목록도 업데이트됨

### 2. Wiki-only 퀘스트
- tarkov.dev API에 없고 Wiki에만 있는 퀘스트도 reqKappa일 수 있음
- 이러한 퀘스트도 Collector의 Previous에 포함되어야 함

### 3. Collector 퀘스트 자체
- Collector 퀘스트의 `ReqKappa`는 false여야 함 (자기 자신을 참조하지 않음)
- Collector 퀘스트 완료 후에는 더 이상 선행 퀘스트 표시 불필요

---

## Testing

### Test Cases
1. Collector 퀘스트의 Previous에 모든 reqKappa 퀘스트가 포함되는지
2. reqKappa가 false인 퀘스트는 Previous에서 제외되는지
3. Collector 자신은 Previous에 포함되지 않는지
4. 진행률 계산이 정확한지 (완료된 reqKappa 퀘스트 수 / 전체 reqKappa 퀘스트 수)
5. 신규 reqKappa 퀘스트 추가 시 자동으로 반영되는지
