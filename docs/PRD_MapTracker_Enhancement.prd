# Map Tracker Enhancement PRD
## Version 1.4 | 2024-12

---

## 1. Overview

### 1.1 Purpose
Map Tracker 기능의 기존 구현 강화 및 발견된 버그 수정을 위한 Product Requirements Document.

### 1.2 Background
현재 Map Tracker는 게임 내 퀘스트 목표를 맵에 시각적으로 표시하고, 플레이어 위치를 추적하는 기능을 제공합니다. 그러나 다중 지역 퀘스트 처리 및 동일 지역 내 다중 목표 체크박스 관련 버그가 발견되었습니다.

---

## 2. Current Implementation Summary (현재 구현 상태)

### 2.1 Core Features

#### A. Map Display System
- **다중 맵 지원**: Customs, Woods, Shoreline, Streets, Labs, Factory, Ground Zero, Interchange, Lighthouse
- **SVG/래스터 이미지 지원**: PNG/JPG 및 SVG (CSS 전처리 포함)
- **좌표 변환**: tarkov.dev Transform 배열 및 CoordinateRotation 사용
- **관련 파일**:
  - `Services/MapTracker/MapTrackerService.cs`
  - `Services/MapTracker/MapCoordinateTransformer.cs`
  - `Models/MapTracker/MapConfig.cs`

#### B. Quest Objective Tracking
- **활성 퀘스트 목표 마커**: 맵에 색상 코딩된 마커 표시
- **마커 스타일**: 기본 원형, 녹색 원형, 퀘스트명 포함/미포함 변형
- **목표 유형별 색상**:
  - Green (#4CAF50): visit
  - Orange (#FF9800): mark
  - Purple (#9C27B0): plantItem
  - Blue (#2196F3): extract
  - Yellow (#FFEB3B): findItem
- **관련 파일**:
  - `Services/MapTracker/QuestObjectiveService.cs`
  - `Models/MapTracker/QuestObjectiveLocation.cs`
  - `Pages/MapTrackerPage.xaml.cs`

#### C. Objective Completion System
- **체크박스 기반 완료 추적**: 사이드 드로어에서 목표 완료 체크
- **ObjectiveId 기반 추적**: 각 목표를 고유 ID로 개별 추적 (v1.1에서 개선)
- **이중 키 동기화**: Map Tracker와 Quests 탭 간 양방향 동기화
- **시각적 피드백**: 완료 시 체크마크 오버레이 및 반투명 처리
- **관련 파일**:
  - `Services/QuestProgressService.cs`
  - `Pages/MapTrackerPage.xaml.cs`
  - `Pages/QuestListPage.xaml.cs`

#### D. Player Position Tracking
- **스크린샷 모니터링**: EFT Screenshots 폴더 감시
- **이동 경로 표시**: 폴리라인으로 플레이어 이동 시각화
- **방향 지시자**: 플레이어 바라보는 방향 화살표
- **관련 파일**:
  - `Services/MapTracker/ScreenshotWatcherService.cs`
  - `Services/MapTracker/ScreenshotCoordinateParser.cs`

#### E. Extract Points
- **PMC/Scav 탈출구 마커**: 팩션별 색상 구분
- **그룹화된 탈출구 처리**: 동일 위치 탈출구 그룹핑
- **관련 파일**:
  - `Services/MapTracker/ExtractService.cs`
  - `Models/MapTracker/MapExtract.cs`

---

## 3. Bug Reports (발견된 버그)

### 3.1 BUG-001: Multi-Location Quest Display Issue
**심각도**: High
**상태**: ✅ Fixed (v1.1)

#### 문제 설명
여러 지역에 걸쳐진 퀘스트의 목표가 해당 맵에 표시되지 않는 문제.

#### 근본 원인
1. GraphQL 쿼리에서 `TaskObjectiveQuestItem`, `TaskObjectiveItem` 타입의 zone 정보를 가져오지 않음
2. "Delivery From the Past" 같은 `findQuestItem`, `plantQuestItem` 타입 퀘스트 누락

#### 해결 방법
`QuestObjectiveService.cs`의 GraphQL 쿼리에 누락된 타입 추가:
```csharp
objectives {{
    ...
    ... on TaskObjectiveQuestItem {{ {zoneFragment} }}
    ... on TaskObjectiveItem {{ {zoneFragment} }}
}}
```

#### 추가 개선
`IsLocationOnCurrentMap()` 메서드 추가로 현재 맵에 해당하는 위치만 필터링하여 마커 표시.

---

### 3.2 BUG-002: Checkbox Batch Check Bug
**심각도**: High
**상태**: ✅ Fixed (v1.1)

#### 문제 설명
WiFi 카메라 설치, 마커 설치 등 한 지역에서 동일한 유형의 여러 임무를 수행해야 할 때, 하나의 체크박스를 체크하면 같은 퀘스트의 모든 목표가 함께 체크되는 버그.

#### 근본 원인
1. `GetObjectiveIndex()` 메서드가 설명 텍스트 기반 매칭 사용
2. 동일한 설명을 가진 목표들(예: "Wi-Fi 카메라 설치" x 3)이 같은 인덱스 반환
3. `ObjectiveIndex` 기반 완료 추적이 이들을 구분하지 못함

#### 해결 방법
ObjectiveId 기반 추적으로 전환:
```csharp
// Map Tracker에서 ObjectiveId로 완료 상태 추적
_progressService.SetObjectiveCompletedById(objective.ObjectiveId, completed,
    objective.QuestNormalizedName, objectiveIndex);
```

---

## 4. Implemented Enhancements (v1.1)

### 4.1 ENH-001: Checkbox Synchronization
**상태**: ✅ Implemented

#### 구현 내용
- Map Tracker와 Quests 탭 간 체크박스 상태 양방향 동기화
- `QuestProgressService`에 이중 키 저장 방식 구현
- ObjectiveId 키와 Index 키를 동시에 저장하여 양쪽 탭에서 동기화

#### 관련 코드
- `QuestProgressService.SetObjectiveCompleted()` - Index 키와 함께 ObjectiveId 키 저장
- `QuestProgressService.SetObjectiveCompletedById()` - ObjectiveId 키와 함께 Index 키 저장
- `QuestObjectiveService.GetObjectiveIdByIndex()` - Index로 ObjectiveId 조회 헬퍼

### 4.2 ENH-002: Multi-Location Marker Display
**상태**: ✅ Implemented

#### 구현 내용
- 현재 맵에 해당하는 위치만 필터링하여 마커 표시
- `IsLocationOnCurrentMap()` 메서드로 맵 이름/alias 매칭

### 4.3 ENH-003: Visual Distinction for Same-Quest Objectives
**상태**: ❌ Removed (v1.1)

#### 결정 사항
- 위치 번호 배지(1/2, 2/3 등) 기능 제거
- 사용자 피드백에 따라 단순화된 UI 유지

---

## 5. Technical Specifications

### 5.1 Affected Files (v1.1 Changes)

| File | Changes |
|------|---------|
| `Services/MapTracker/QuestObjectiveService.cs` | GraphQL 쿼리 확장, GetObjectiveIdByIndex() 추가 |
| `Services/QuestProgressService.cs` | 이중 키 동기화 로직 추가 |
| `Pages/MapTrackerPage.xaml.cs` | ObjectiveId 기반 체크박스 처리, IsLocationOnCurrentMap() 추가 |
| `Pages/QuestListPage.xaml.cs` | ObjectiveId 동기화 연동 |

### 5.2 Data Flow (v1.1)

```
Map Tracker 체크박스:
CheckBox Click → ObjectiveId 추출 → SetObjectiveCompletedById(objectiveId, completed, questName, index)
                                                ↓
                                    저장: "id:{objectiveId}" = true
                                          "{questName}:{index}" = true  (동기화용)
                                                ↓
                                    Quests 탭에서도 완료 상태 반영

Quests 탭 체크박스:
CheckBox Click → Index 추출 → ObjectiveId 조회 → SetObjectiveCompleted(questName, index, completed, objectiveId)
                                                ↓
                                    저장: "{questName}:{index}" = true
                                          "id:{objectiveId}" = true  (동기화용)
                                                ↓
                                    Map Tracker에서도 완료 상태 반영
```

---

## 6. Testing Requirements

### 6.1 Test Cases

| TC | Description | Expected Result | Status |
|----|-------------|-----------------|--------|
| TC-001 | 다중 맵 퀘스트(Delivery From the Past) 표시 | Customs, Factory 모두에서 마커 표시 | ✅ Pass |
| TC-002 | 동일 퀘스트 내 첫 번째 목표만 체크 | 해당 목표만 완료 표시 | ✅ Pass |
| TC-003 | Map Tracker에서 체크 → Quests 탭 확인 | 동일하게 체크됨 | ✅ Pass |
| TC-004 | Quests 탭에서 체크 → Map Tracker 확인 | 동일하게 체크됨 | ✅ Pass |
| TC-005 | 앱 재시작 후 완료 상태 유지 | 완료 상태 유지됨 | ✅ Pass |

---

## 7. Implemented Enhancements (v1.2)

### 7.1 ENH-004: Other Map Objectives Display
**상태**: ✅ Implemented

#### 구현 내용
- 퀘스트 드로어에서 현재 맵이 아닌 다른 맵의 목표도 비활성 상태로 표시
- 다른 맵 목표는 0.5 투명도로 표시되고 체크박스 비활성화
- 맵 이름 배지로 어느 맵에서 완료해야 하는지 표시

#### 관련 코드
- `QuestObjectiveViewModel` - `IsOnCurrentMap`, `OtherMapName`, `OtherMapBadgeVisibility`, `IsEnabled` 프로퍼티 추가
- `RefreshQuestDrawer()` - 퀘스트의 모든 목표(다른 맵 포함)를 수집하여 표시

### 7.2 ENH-005: Map Progress Display
**상태**: ✅ Implemented

#### 구현 내용
- 퀘스트 드로어 상단에 현재 맵의 퀘스트 진행률 표시
- 완료된 목표/전체 목표 카운트 표시 (예: "3/5")
- 시각적 진행률 바 (녹색)

#### 관련 코드
- `UpdateMapProgress()` - 진행률 계산 및 UI 업데이트
- XAML에 `TxtMapProgressCount`, `MapProgressBar` 추가

### 7.3 ENH-006: Quest Filtering Options
**상태**: ✅ Implemented

#### 구현 내용
- **상태 필터**: All / Incomplete / Completed
- **타입 필터**: All Types / Visit / Mark / Plant / Extract / Find
- **현재 맵만 보기**: 체크박스로 다른 맵 목표 숨기기

#### 관련 코드
- `_drawerStatusFilter`, `_drawerTypeFilter`, `_drawerCurrentMapOnly` 필드
- `ApplyDrawerFilters()` - 필터 적용 로직
- XAML에 `CmbStatusFilter`, `CmbTypeFilter`, `ChkCurrentMapOnly` 추가

---

## 8. Implemented Enhancements (v1.3)

### 8.1 ENH-007: UI Enhancement
**상태**: ✅ Implemented

#### 구현 내용
- 상단 컨트롤 바 구조 개선
- 상태 표시 바 레이아웃 개선 (라벨과 값 분리)
- 퀘스트 드로어 필터 UI 개선 (콤보박스 크기 최적화)
- 설정 패널 전체 레이블 구조화
- 맵 없음 안내 텍스트 영문화

#### 관련 코드
- `MapTrackerPage.xaml` - 모든 UI 요소에 x:Name 추가
- `TxtLastUpdate` → `TxtLastUpdateLabel` + `TxtLastUpdateTime` 분리

### 8.2 ENH-008: Full Localization Support
**상태**: ✅ Implemented

#### 구현 내용
Map 탭 전체에 한국어/영어/일본어 다국어 지원 추가:

**상단 컨트롤 바:**
- 페이지 타이틀 (맵 위치 트래커)
- 맵 라벨, 퀘스트 마커, 탈출구
- 버튼: 경로 지우기, 전체 화면, 설정, 추적 시작/중지

**상태 표시 바:**
- 상태: 대기 중 / 추적 중
- 위치, 마지막 업데이트 라벨

**퀘스트 드로어:**
- 퀘스트 목표 타이틀
- 맵 진행률 라벨
- 필터 옵션: 전체/미완료/완료, 타입별 (방문/마킹/설치/탈출/찾기)
- 이 맵만 체크박스

**설정 패널:**
- 섹션 헤더: 스크린샷 폴더, 마커 설정, 탈출구 설정
- 버튼: 자동 감지, 찾아보기
- 체크박스: 완료된 목표 숨기기, PMC/Scav 탈출구
- 슬라이더 라벨: 퀘스트 스타일, 퀘스트명, 퀘스트 마커, 플레이어 마커, 이름 크기

**퀘스트 스타일 옵션:**
- 아이콘만, 녹색 원, 아이콘+이름, 원+이름

**맵 없음 안내:**
- 맵 이미지 없음 메시지 및 힌트

#### 관련 코드
- `LocalizationService.cs` - Map Tracker 섹션 추가 (40+ 문자열)
- `MapTrackerPage.xaml.cs` - `UpdateLocalizedText()` 메서드 구현
- `MapTrackerPage.xaml` - 모든 텍스트 요소에 x:Name 추가

---

## 9. Implemented Enhancements (v1.4)

### 9.1 ENH-009: Quest Grouping Option
**상태**: ✅ Implemented

#### 구현 내용
- 퀘스트 드로어에서 목표를 퀘스트별로 그룹화하는 옵션 추가
- 그룹 헤더에 퀘스트명과 진행률(완료/전체) 표시
- 완료된 퀘스트 그룹은 취소선과 투명도로 표시

#### 관련 코드
- `MapTrackerPage.xaml` - `ChkGroupByQuest` 체크박스 추가
- `MapTrackerPage.xaml.cs` - `ApplyQuestGrouping()` 메서드, `QuestGroupHeader` 클래스
- `QuestDrawerTemplateSelector` - DataTemplate 선택을 위한 템플릿 셀렉터

### 9.2 ENH-010: Dark/Light Theme Support
**상태**: ✅ Implemented

#### 구현 내용
- 다크/라이트 테마 선택 옵션 추가 (설정 패널)
- 런타임 테마 전환 지원
- 설정 파일에 테마 저장 및 복원

#### 관련 코드
- `Services/ThemeService.cs` - 테마 관리 서비스 (싱글톤)
- `MapTrackerPage.xaml` - 테마 콤보박스 추가
- `MapTrackerSettings.cs` - `Theme` 속성 추가
- `LocalizationService.cs` - 테마 관련 로컬라이제이션 문자열

### 9.3 ENH-011: Marker Color Customization
**상태**: ✅ Implemented

#### 구현 내용
- 퀘스트 목표 타입별 마커 색상 커스터마이징
- Visit, Mark, Plant, Extract, Find 각 타입별 색상 설정
- 색상 선택 대화상자 (Windows ColorDialog)
- 기본값 복원 버튼

#### 관련 코드
- `MapTrackerSettings.cs` - `MarkerColors` Dictionary, `GetMarkerColor()`/`SetMarkerColor()` 메서드
- `MapTrackerPage.xaml` - 색상 박스 UI 추가
- `MapTrackerPage.xaml.cs` - `MarkerColor_Click()`, `BtnResetColors_Click()`, `UpdateMarkerColorUI()` 메서드
- `CreateQuestMarker()` - 커스텀 색상 적용 로직

---

## 10. Future Considerations

### 10.1 Potential Enhancements
1. 퀘스트 검색 기능 (드로어 내)
2. 맵 간 퀘스트 이동 시 자동 맵 전환 제안

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2024-12 | Claude | Initial document creation |
| 1.1 | 2024-12 | Claude | BUG-001, BUG-002 수정 완료, ENH-001/002 구현, ENH-003 제거 |
| 1.2 | 2024-12 | Claude | ENH-004/005/006 구현 (다른 맵 목표 표시, 진행률, 필터링) |
| 1.3 | 2024-12 | Claude | ENH-007/008 구현 (UI 개선, 전체 Localization 지원) |
| 1.4 | 2024-12 | Claude | ENH-009/010/011 구현 (퀘스트 그룹화, 테마 지원, 마커 색상 커스터마이징) |
