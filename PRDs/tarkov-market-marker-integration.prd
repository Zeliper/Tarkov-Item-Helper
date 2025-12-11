# Tarkov Market Marker Integration PRD
## Version 1.0 | 2025-12-11

---

## 1. Overview

### 1.1 Purpose
Tarkov Market API에서 가져온 마커 데이터를 현재 맵에 적용하고, 기존 wiki 기반 quests 탭과 완전히 연동하는 통합 시스템 구축.

### 1.2 Background
현재 시스템:
- `TarkovMarketMarkerService`: Tarkov Market API에서 3,815+ 마커 데이터 관리
- `QuestListPage`: tarkov.dev API + wiki 데이터 기반 퀘스트 목록
- `MapTrackerPage`: 맵 표시 및 마커 렌더링

문제점:
- 마커 데이터가 퀘스트 탭과 완전히 동기화되지 않음
- 마커 위치 정확성 검증 메커니즘 없음
- 향후 오프라인 지원을 위한 로컬 DB 구조 부재

### 1.3 Goals
1. **마커 통합**: Tarkov Market 마커를 맵에 정확히 표시
2. **퀘스트 연동**: QuestListPage와 MapTrackerPage 간 양방향 동기화
3. **위치 검증**: Tarkov Market 웹사이트와 교차 검증 시스템
4. **SQLite 준비**: 향후 로컬 DB 동기화를 위한 아키텍처 설계

---

## 2. System Architecture

### 2.1 Current Data Flow
```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  tarkov.dev     │────▶│  TarkovDataSvc   │────▶│  QuestListPage  │
│  GraphQL API    │     │  (tasks.json)    │     │  (퀘스트 목록)   │
└─────────────────┘     └──────────────────┘     └─────────────────┘

┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  Tarkov Market  │────▶│  TMMarkerService │────▶│  MapTrackerPage │
│  REST API       │     │  (markers.json)  │     │  (맵 마커)       │
└─────────────────┘     └──────────────────┘     └─────────────────┘
```

### 2.2 Target Architecture
```
┌─────────────────────────────────────────────────────────────────┐
│                     Unified Data Layer                          │
├─────────────────┬──────────────────┬───────────────────────────┤
│  tarkov.dev     │  Tarkov Market   │  SQLite (Future)          │
│  (Tasks/Items)  │  (Markers/Quests)│  (Offline Cache)          │
└────────┬────────┴────────┬─────────┴────────────┬──────────────┘
         │                 │                       │
         ▼                 ▼                       ▼
┌─────────────────────────────────────────────────────────────────┐
│              MarkerQuestBridgeService (NEW)                     │
│  - bsgId 매칭                                                    │
│  - 좌표 변환                                                      │
│  - 퀘스트-마커 매핑                                               │
└────────────────────────────┬────────────────────────────────────┘
                             │
         ┌───────────────────┼───────────────────┐
         ▼                   ▼                   ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────────────┐
│  QuestListPage  │ │  MapTrackerPage │ │  MarkerVerificationSvc  │
│  (퀘스트 선택)   │◀─▶│  (마커 표시)    │ │  (위치 검증)             │
└─────────────────┘ └─────────────────┘ └─────────────────────────┘
```

---

## 3. Implementation Details

### 3.1 Phase 1: MarkerQuestBridgeService (핵심 연동)

#### 3.1.1 새 서비스 클래스
**파일**: `Services/MapTracker/MarkerQuestBridgeService.cs`

```csharp
public class MarkerQuestBridgeService
{
    // Tarkov Market questUid → bsgId → tarkov.dev task 매핑
    private Dictionary<string, TarkovTask> _questToTaskMap;

    // tarkov.dev taskId → Tarkov Market markers 매핑
    private Dictionary<string, List<TarkovMarketMarker>> _taskToMarkersMap;

    // Methods
    public List<TarkovMarketMarker> GetMarkersForTask(TarkovTask task);
    public TarkovTask? GetTaskForMarker(TarkovMarketMarker marker);
    public void SyncQuestProgress(string taskId, bool completed);
    public MarkerClickResult HandleMarkerClick(TarkovMarketMarker marker);
}
```

#### 3.1.2 매핑 키 체인
```
QuestListPage 퀘스트 선택
    ↓
TarkovTask.Ids[0] (bsgId)
    ↓
TarkovMarketQuest.bsgId 매칭
    ↓
TarkovMarketQuest.uid (questUid)
    ↓
TarkovMarketMarker.questUid 필터링
    ↓
MapTrackerPage 마커 하이라이트
```

#### 3.1.3 양방향 동기화 이벤트
```csharp
// QuestListPage → MapTrackerPage
public event Action<TarkovTask, List<TarkovMarketMarker>>? QuestSelected;

// MapTrackerPage → QuestListPage
public event Action<TarkovMarketMarker, TarkovTask?>? MarkerClicked;

// 진행상황 동기화
public event Action<string, QuestStatus>? QuestStatusChanged;
```

### 3.2 Phase 2: UI 통합

#### 3.2.1 QuestListPage 수정
- 퀘스트 선택 시 `MarkerQuestBridgeService.QuestSelected` 이벤트 발행
- 맵 아이콘 버튼 추가 (퀘스트 위치가 있는 경우)
- Detail Panel에 "맵에서 보기" 버튼 추가

#### 3.2.2 MapTrackerPage 수정
- `QuestSelected` 이벤트 구독하여 마커 하이라이트
- 마커 클릭 시 퀘스트 정보 팝업 표시
- Quest Drawer와 마커 연동 (기존 기능 유지)

#### 3.2.3 새 UI 컴포넌트
```
┌─────────────────────────────────────────────┐
│ Quest Marker Info Popup                     │
├─────────────────────────────────────────────┤
│ [Quest Icon] Quest Name                     │
│ Trader: Prapor                              │
│ ─────────────────────────────────────────── │
│ Objective: Find the document                │
│ ─────────────────────────────────────────── │
│ [Image from TM imgs]                        │
│ ─────────────────────────────────────────── │
│ [Mark Complete] [Show in Quests]            │
└─────────────────────────────────────────────┘
```

### 3.3 Phase 3: 마커 위치 검증 시스템

#### 3.3.1 검증 도구 스택
```
Python 3.11+
├── playwright (웹 자동화)
├── httpx (API 호출)
├── pandas (데이터 분석)
└── sqlite3 (검증 결과 저장)
```

#### 3.3.2 검증 스크립트
**파일**: `scripts/verify_marker_positions.py`

```python
from playwright.sync_api import sync_playwright
import httpx
import json

class MarkerVerifier:
    """Tarkov Market 웹사이트에서 마커 위치 교차 검증"""

    def __init__(self):
        self.base_url = "https://tarkov-market.com/maps"
        self.api_markers = {}  # API에서 가져온 마커
        self.web_markers = {}  # 웹에서 추출한 마커

    async def fetch_api_markers(self, map_name: str):
        """API에서 마커 데이터 가져오기"""
        # 기존 디코딩 로직 사용

    async def extract_web_markers(self, map_name: str):
        """웹 페이지에서 마커 위치 추출"""
        with sync_playwright() as p:
            browser = p.chromium.launch(headless=True)
            page = browser.new_page()
            page.goto(f"{self.base_url}/{map_name}")

            # SVG 마커 요소에서 좌표 추출
            markers = page.evaluate('''() => {
                const markers = document.querySelectorAll('[data-marker-id]');
                return Array.from(markers).map(m => ({
                    id: m.dataset.markerId,
                    x: parseFloat(m.getAttribute('cx') || m.style.left),
                    y: parseFloat(m.getAttribute('cy') || m.style.top)
                }));
            }''')

            browser.close()
            return markers

    def compare_positions(self, tolerance: float = 5.0):
        """API와 웹 좌표 비교"""
        discrepancies = []
        for marker_id, api_pos in self.api_markers.items():
            if marker_id in self.web_markers:
                web_pos = self.web_markers[marker_id]
                distance = sqrt((api_pos.x - web_pos.x)**2 +
                               (api_pos.y - web_pos.y)**2)
                if distance > tolerance:
                    discrepancies.append({
                        'id': marker_id,
                        'api': api_pos,
                        'web': web_pos,
                        'distance': distance
                    })
        return discrepancies
```

#### 3.3.3 MCP 통합 검증
```python
# MCP Puppeteer 활용 실시간 검증
async def verify_via_mcp(marker_id: str, expected_coords: tuple):
    """MCP를 통한 실시간 마커 위치 검증"""
    # 1. puppeteer_navigate로 맵 페이지 이동
    # 2. puppeteer_evaluate로 마커 요소 찾기
    # 3. 좌표 비교 및 스크린샷 저장
    # 4. 불일치 시 경고 로그 생성
```

### 3.4 Phase 4: SQLite 동기화 준비

#### 3.4.1 DB 스키마 설계
**파일**: `Data/tarkov_markers.db`

```sql
-- 마커 테이블
CREATE TABLE markers (
    uid TEXT PRIMARY KEY,
    map TEXT NOT NULL,
    category TEXT NOT NULL,
    sub_category TEXT,
    name TEXT NOT NULL,
    name_ko TEXT,
    description TEXT,
    geometry_x REAL NOT NULL,
    geometry_y REAL NOT NULL,
    level INTEGER,
    quest_uid TEXT,
    updated_at DATETIME,
    synced_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    is_verified BOOLEAN DEFAULT FALSE,

    FOREIGN KEY (quest_uid) REFERENCES quests(uid)
);

-- 퀘스트 테이블
CREATE TABLE quests (
    uid TEXT PRIMARY KEY,
    bsg_id TEXT UNIQUE NOT NULL,
    name TEXT NOT NULL,
    name_ko TEXT,
    trader TEXT,
    type TEXT,
    wiki_url TEXT,
    required_for_kappa BOOLEAN DEFAULT FALSE,
    updated_at DATETIME,
    synced_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 검증 결과 테이블
CREATE TABLE verification_results (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    marker_uid TEXT NOT NULL,
    verified_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    api_x REAL,
    api_y REAL,
    web_x REAL,
    web_y REAL,
    distance REAL,
    is_match BOOLEAN,
    screenshot_path TEXT,

    FOREIGN KEY (marker_uid) REFERENCES markers(uid)
);

-- 동기화 메타데이터
CREATE TABLE sync_metadata (
    key TEXT PRIMARY KEY,
    value TEXT,
    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
);

-- 인덱스
CREATE INDEX idx_markers_map ON markers(map);
CREATE INDEX idx_markers_quest ON markers(quest_uid);
CREATE INDEX idx_quests_bsg ON quests(bsg_id);
```

#### 3.4.2 동기화 서비스
**파일**: `Services/MarkerSyncService.cs`

```csharp
public class MarkerSyncService
{
    private readonly string _dbPath = "Data/tarkov_markers.db";

    // 초기 동기화 (API → SQLite)
    public async Task InitialSyncAsync();

    // 증분 동기화 (변경분만)
    public async Task IncrementalSyncAsync();

    // 로컬 캐시 조회 (오프라인 지원)
    public List<TarkovMarketMarker> GetMarkersFromCache(string mapKey);

    // 검증 결과 저장
    public void SaveVerificationResult(VerificationResult result);
}
```

---

## 4. Verification Workflow

### 4.1 자동 검증 파이프라인
```
┌─────────────────────────────────────────────────────────────┐
│                   Verification Pipeline                     │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  1. Fetch API Markers                                       │
│     └── TarkovMarketService.FetchMarkersAsync()             │
│                     ↓                                       │
│  2. Extract Web Markers (Playwright)                        │
│     └── verify_marker_positions.py                          │
│                     ↓                                       │
│  3. Compare Positions                                       │
│     └── tolerance: 5px (SVG viewBox coords)                 │
│                     ↓                                       │
│  4. Generate Report                                         │
│     ├── verified_markers.json (일치)                        │
│     ├── discrepancies.json (불일치)                         │
│     └── screenshots/ (불일치 마커 스크린샷)                   │
│                     ↓                                       │
│  5. Store in SQLite                                         │
│     └── verification_results 테이블                          │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### 4.2 수동 검증 도구
```
# CLI 명령어
python scripts/verify_marker_positions.py --map woods --verbose
python scripts/verify_marker_positions.py --marker-id ae6c753c-...
python scripts/verify_marker_positions.py --all --report html
```

### 4.3 MCP 실시간 검증
```
1. MapTrackerPage에서 마커 표시
2. "Verify" 버튼 클릭
3. MCP Puppeteer로 Tarkov Market 웹 열기
4. 해당 마커 위치로 스크롤/줌
5. 좌표 비교 및 결과 표시
6. 스크린샷 비교 (선택적)
```

---

## 5. Quest Tab Integration

### 5.1 기존 기능 유지
| 기능 | 유지 방법 |
|------|----------|
| 퀘스트 필터링 (트레이더/맵/상태) | 기존 코드 그대로 유지 |
| 퀘스트 상세 정보 | Detail Panel 유지 |
| 진행 상황 추적 | QuestProgressService 사용 |
| 아이템 요구사항 | RequiredItemViewModel 유지 |
| Kappa 배지 | IsKappaRequired 플래그 유지 |

### 5.2 새 연동 기능
| 기능 | 구현 |
|------|------|
| 맵에서 보기 | MarkerQuestBridgeService 활용 |
| 마커 하이라이트 | QuestSelected 이벤트 |
| 목표 완료 동기화 | SetObjectiveCompletedById |
| 위치 이미지 표시 | TarkovMarketMarker.imgs 사용 |

### 5.3 데이터 흐름
```
QuestListPage                     MapTrackerPage
┌─────────────────┐              ┌─────────────────┐
│ Quest Selected  │─────────────▶│ Highlight       │
│ (TarkovTask)    │  QuestSelected│ Markers        │
└─────────────────┘              └─────────────────┘
                                          │
                                          │ MarkerClicked
                                          ▼
┌─────────────────┐              ┌─────────────────┐
│ Show Detail     │◀─────────────│ Marker Info     │
│ (QuestDetails)  │              │ (Popup)         │
└─────────────────┘              └─────────────────┘
```

---

## 6. File Structure

### 6.1 New Files
```
TarkovHelper/
├── Services/
│   ├── MapTracker/
│   │   ├── MarkerQuestBridgeService.cs    [NEW]
│   │   └── MarkerSyncService.cs           [NEW]
│   └── MarkerVerificationService.cs       [NEW]
├── Models/
│   └── MarkerVerificationResult.cs        [NEW]
└── Data/
    └── tarkov_markers.db                  [NEW - SQLite]

scripts/
├── verify_marker_positions.py             [NEW]
├── marker_verification_report.py          [NEW]
└── sync_markers_to_sqlite.py              [NEW]
```

### 6.2 Modified Files
```
TarkovHelper/
├── Pages/
│   ├── QuestListPage.xaml.cs              [MODIFY - 연동 이벤트]
│   └── MapTrackerPage.xaml.cs             [MODIFY - 연동 이벤트]
└── Services/
    └── QuestProgressService.cs            [MODIFY - 동기화]
```

---

## 7. Testing Plan

### 7.1 Unit Tests
- [ ] MarkerQuestBridgeService 매핑 테스트
- [ ] bsgId 매칭 정확도
- [ ] 좌표 변환 검증

### 7.2 Integration Tests
- [ ] QuestListPage → MapTrackerPage 이벤트 흐름
- [ ] 마커 클릭 → 퀘스트 Detail 표시
- [ ] 진행상황 양방향 동기화

### 7.3 E2E Tests (Playwright)
- [ ] 전체 맵 마커 위치 검증
- [ ] Woods 453개 마커 검증
- [ ] Customs 582개 마커 검증
- [ ] 불일치 마커 리포트 생성

---

## 8. Implementation Timeline

### Phase 1: Core Bridge Service (Priority: High)
- MarkerQuestBridgeService 구현
- bsgId 매핑 로직
- 기본 이벤트 시스템

### Phase 2: UI Integration (Priority: High)
- QuestListPage 수정
- MapTrackerPage 수정
- Marker Info Popup

### Phase 3: Verification System (Priority: Medium)
- Python 검증 스크립트
- MCP Puppeteer 통합
- 리포트 생성

### Phase 4: SQLite Sync (Priority: Low)
- DB 스키마 생성
- MarkerSyncService 구현
- 오프라인 캐시 지원

---

## 9. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| API 난독화 방식 변경 | 데이터 수집 실패 | 디코딩 로직 모듈화, 빠른 대응 |
| 좌표 시스템 불일치 | 마커 위치 오류 | 다중 검증 포인트, IDW 보정 |
| 웹사이트 구조 변경 | 검증 스크립트 실패 | Selector 유연성, 정기 점검 |
| SQLite 동기화 충돌 | 데이터 불일치 | 타임스탬프 기반 우선순위 |

---

## 10. Success Metrics

| Metric | Target |
|--------|--------|
| 마커 매핑 정확도 | 95%+ |
| 위치 검증 통과율 | 90%+ |
| 퀘스트-마커 연동 응답시간 | < 100ms |
| SQLite 동기화 시간 | < 5s (전체 맵) |

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-12-11 | Claude | Initial document creation |
