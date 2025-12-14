# PRD: 통합 아이템 허브 (Integrated Item Hub)

## 개요

### 목적
아이템 하나를 선택하면 퀘스트와 은신처에서 필요한 전체 수요를 한눈에 파악할 수 있는 통합 뷰 제공

### 현재 문제점
- Items 탭: 퀘스트 필요 아이템만 표시
- Hideout 탭: 은신처 필요 재료만 표시
- 사용자가 아이템 총 수요를 파악하려면 두 탭을 오가야 함
- 우선순위 판단이 어려움 (퀘스트 vs 은신처)

### 목표
1. 아이템별 전체 수요 통합 표시
2. 퀘스트/은신처 출처 구분
3. 보유량 대비 부족량 명확히 표시
4. 클릭 시 해당 퀘스트/은신처로 즉시 이동

---

## 기능 요구사항

### FR-1: 통합 아이템 카드

```
┌─────────────────────────────────────────────────────┐
│ 📦 Salewa First Aid Kit                      [👁]  │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│                                                     │
│ 📊 수량 현황                                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ 보유: 3 (FIR: 2, 일반: 1)                       │ │
│ │ 필요: 6 (퀘스트: 3, 은신처: 3)                  │ │
│ │ 부족: 3                                         │ │
│ │ ████████████░░░░░░░░░░░░ 50%                   │ │
│ └─────────────────────────────────────────────────┘ │
│                                                     │
│ 📋 퀘스트 필요 (3개)                                │
│ ┌─────────────────────────────────────────────────┐ │
│ │ ○ Therapist - Shortage                          │ │
│ │   2개 필요 (FIR) | 보유: 2/2 ✓           [이동] │ │
│ ├─────────────────────────────────────────────────┤ │
│ │ ○ Therapist - Car Repair                        │ │
│ │   1개 필요 | 보유: 1/1 ✓                 [이동] │ │
│ └─────────────────────────────────────────────────┘ │
│                                                     │
│ 🏠 은신처 필요 (3개)                                │
│ ┌─────────────────────────────────────────────────┐ │
│ │ ○ Medstation Lv2                                │ │
│ │   3개 필요 | 보유: 0/3 ✗                 [이동] │ │
│ └─────────────────────────────────────────────────┘ │
│                                                     │
│ 💡 획득 팁                                          │
│ • Crack House (Customs), EMERCOM (Interchange)     │
│ • Therapist Lv2 구매 가능                          │
└─────────────────────────────────────────────────────┘
```

### FR-2: 필터 및 정렬

| 필터 | 설명 |
|------|------|
| 전체 | 모든 아이템 |
| 부족 아이템만 | 보유량 < 필요량 |
| 퀘스트 전용 | 퀘스트에만 필요한 아이템 |
| 은신처 전용 | 은신처에만 필요한 아이템 |
| 둘 다 필요 | 퀘스트 + 은신처 모두 필요 |

| 정렬 | 설명 |
|------|------|
| 부족량 순 | 가장 부족한 아이템 먼저 |
| 이름순 | 알파벳/가나다 순 |
| 총 필요량 순 | 가장 많이 필요한 아이템 먼저 |
| 진행률 순 | 거의 완료된 아이템 먼저 |

### FR-3: 네비게이션

- **[이동] 버튼 클릭**: 해당 퀘스트/은신처 탭으로 전환 + 항목 선택
- **아이템 아이콘 클릭**: Wiki 링크 열기
- **더블클릭**: 인벤토리 수량 편집 모드

### FR-4: 실시간 동기화

- 인벤토리 변경 시 즉시 반영
- 퀘스트 완료 시 해당 항목 제거
- 은신처 레벨업 시 해당 항목 제거

---

## 기술 요구사항

### TR-1: 데이터 모델

```csharp
public class IntegratedItemRequirement
{
    public string ItemId { get; set; }
    public string ItemName { get; set; }
    public string ItemNameKo { get; set; }
    public string IconLink { get; set; }

    // 보유 현황
    public int OwnedFir { get; set; }
    public int OwnedNonFir { get; set; }
    public int TotalOwned => OwnedFir + OwnedNonFir;

    // 필요 현황
    public int QuestRequired { get; set; }
    public int QuestRequiredFir { get; set; }
    public int HideoutRequired { get; set; }
    public int TotalRequired => QuestRequired + HideoutRequired;

    // 계산
    public int Shortage => Math.Max(0, TotalRequired - TotalOwned);
    public double Progress => TotalRequired > 0 ? (double)TotalOwned / TotalRequired : 1.0;

    // 상세 출처
    public List<QuestItemSource> QuestSources { get; set; }
    public List<HideoutItemSource> HideoutSources { get; set; }
}

public class QuestItemSource
{
    public string QuestId { get; set; }
    public string QuestName { get; set; }
    public string TraderName { get; set; }
    public int RequiredCount { get; set; }
    public bool RequiresFir { get; set; }
    public bool IsFulfilled { get; set; }
}

public class HideoutItemSource
{
    public string StationId { get; set; }
    public string StationName { get; set; }
    public int Level { get; set; }
    public int RequiredCount { get; set; }
    public bool IsFulfilled { get; set; }
}
```

### TR-2: 서비스 레이어

```csharp
public class IntegratedItemService
{
    // 의존성
    private readonly ItemDbService _itemDb;
    private readonly QuestDbService _questDb;
    private readonly HideoutDbService _hideoutDb;
    private readonly ItemInventoryService _inventory;
    private readonly QuestProgressService _questProgress;
    private readonly HideoutProgressService _hideoutProgress;

    // 메서드
    public List<IntegratedItemRequirement> GetAllRequirements();
    public IntegratedItemRequirement GetItemRequirement(string itemId);
    public List<IntegratedItemRequirement> GetShortageItems();
    public List<IntegratedItemRequirement> GetItemsForQuest(string questId);
    public List<IntegratedItemRequirement> GetItemsForHideout(string stationId, int level);
}
```

### TR-3: UI 구현

- **위치**: 기존 Items 탭 확장 또는 새로운 "통합" 서브탭
- **XAML 구조**:
  - ListView + DataTemplate (IntegratedItemCard)
  - Expander로 퀘스트/은신처 섹션 접기
  - ProgressBar로 진행률 시각화

### TR-4: 이벤트 연동

```csharp
// 구독할 이벤트
ItemInventoryService.InventoryChanged += RefreshItems;
QuestProgressService.ProgressChanged += RefreshItems;
HideoutProgressService.ProgressChanged += RefreshItems;
```

---

## UI/UX 요구사항

### UX-1: 색상 코드

| 상태 | 색상 | 설명 |
|------|------|------|
| 충족 | #4CAF50 (녹색) | 보유량 >= 필요량 |
| 부분 충족 | #FFC107 (노랑) | 0 < 보유량 < 필요량 |
| 미충족 | #F44336 (빨강) | 보유량 = 0 |
| FIR 필요 | #2196F3 (파랑) | FIR 태그 강조 |

### UX-2: 아이콘

| 용도 | 아이콘 |
|------|--------|
| 퀘스트 | 📋 |
| 은신처 | 🏠 |
| FIR | ✓ (체크 in 원) |
| 부족 | ⚠️ |
| 충족 | ✓ |

### UX-3: 반응형 동작

- 목록 스크롤 시 부드러운 가상화
- 필터 변경 시 애니메이션 전환
- 로딩 중 스켈레톤 UI

---

## 구현 계획

### Phase 1: 데이터 레이어 (1-2일)
1. `IntegratedItemRequirement` 모델 생성
2. `IntegratedItemService` 구현
3. 기존 서비스 연동

### Phase 2: UI 구현 (2-3일)
1. IntegratedItemCard DataTemplate
2. 필터/정렬 컨트롤
3. 네비게이션 로직

### Phase 3: 통합 및 테스트 (1일)
1. 기존 Items 탭과 통합
2. 이벤트 동기화 테스트
3. 성능 최적화

---

## 성공 지표

- 사용자가 아이템 수요를 파악하는 시간 50% 단축
- 탭 전환 횟수 감소
- 아이템 우선순위 결정 용이성 향상
