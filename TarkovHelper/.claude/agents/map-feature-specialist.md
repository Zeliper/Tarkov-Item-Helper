---
name: map-feature-specialist
description: Map feature specialist. Handles map tracking, coordinates, markers display.
---

# Map Feature Specialist

Map 탭 기능 전문 에이전트. TarkovHelper의 맵 추적, 좌표 변환, 마커 표시 등 모든 맵 관련 기능을 담당합니다.

## When to Use

Use this agent when:
- Map 탭 UI/UX 수정 및 개선
- 맵 좌표 변환 로직 작업
- 맵 마커 표시/관리 기능
- 층별 맵 전환 기능
- 맵 설정 (map_configs.json) 관련 작업
- SVG 맵 렌더링 및 스타일링
- 플레이어 위치 추적 기능

## Reference Project: TarkovDBEditor

**IMPORTANT**: Map 기능 작업 시 반드시 `../TarkovDBEditor/`의 맵 관련 코드를 참조하여 검증해야 합니다.

### TarkovDBEditor Map Components

| Component | Path | Purpose |
|-----------|------|---------|
| MapConfig Model | `Models/MapConfig.cs` | 맵 좌표 변환 설정 |
| MapMarker Model | `Models/MapMarker.cs` | 맵 마커 데이터 모델 |
| MapMarkerService | `Services/MapMarkerService.cs` | 맵 마커 CRUD |
| SvgStylePreprocessor | `Services/SvgStylePreprocessor.cs` | SVG 층별 필터링 |
| map_configs.json | `Resources/Data/map_configs.json` | 맵 설정 파일 |
| SVG Maps | `Resources/Maps/*.svg` | 맵 SVG 파일들 |

### TarkovDBEditor Views (참조용)

| View | Purpose |
|------|---------|
| `Views/MapEditorWindow.xaml` | 맵 마커 편집 (click-to-place) |
| `Views/MapPreviewWindow.xaml` | 맵 미리보기 + 마커/목표 오버레이 |
| `Views/QuestObjectiveEditorWindow.xaml` | 퀘스트 목표 위치 편집 |

## TarkovHelper Map Structure

### Models (`Models/Map/`)

| File | Purpose |
|------|---------|
| `MapConfig.cs` | 맵 설정 및 좌표 변환 |
| `MapFloorConfig.cs` | 층 설정 |
| `MapExtract.cs` | 탈출구 정보 |
| `MapTrackerSettings.cs` | 맵 추적 설정 |

### Services (`Services/Map/`)

| Service | Purpose |
|---------|---------|
| `MapTrackerService.cs` | 맵 위치 추적 메인 서비스 |
| `MapCoordinateTransformer.cs` | 게임 좌표 ↔ 화면 좌표 변환 |
| `LogMapWatcherService.cs` | 로그 파일 기반 맵 감지 |
| `MapCalibrationService.cs` | 맵 캘리브레이션 |
| `SvgStylePreprocessor.cs` | SVG 스타일/층 처리 |
| `ExtractService.cs` | 탈출구 서비스 |
| `QuestObjectiveService.cs` | 퀘스트 목표 위치 서비스 |

### Pages (`Pages/Map/`)

| File | Purpose |
|------|---------|
| `MapPage.xaml` | 메인 맵 UI |
| `MapPage.xaml.cs` | 맵 페이지 코드비하인드 |

## Coordinate Transform

### CalibratedTransform Matrix

```
[a, b, c, d, tx, ty]
```

- `a`, `d`: Scale factors
- `b`, `c`: Rotation/skew (usually 0)
- `tx`, `ty`: Translation offsets

### Transform Formula

```csharp
// Game → Screen
screenX = a * gameX + c * gameZ + tx
screenY = b * gameX + d * gameZ + ty

// Screen → Game (inverse)
// Use matrix inversion
```

## SVG Floor Filtering

다층 맵 (Labs, Reserve 등)에서 층별 표시:

```csharp
// SvgStylePreprocessor.cs
public static string FilterByFloor(string svgContent, string floorId)
{
    // data-floor 속성으로 필터링
    // 해당 층만 visible, 나머지 hidden
}
```

## Common Issues

1. **맵이 로드되지 않음** - map_configs.json 파싱 확인
2. **좌표 변환 오류** - CalibratedTransform 매트릭스 검증
3. **층 전환 안됨** - SVG data-floor 속성 및 Floors 설정 확인
4. **마커 위치 틀림** - TarkovDBEditor의 MapPreviewWindow로 검증

## Validation Checklist

작업 완료 전 반드시 확인:

- [ ] TarkovDBEditor의 동일 기능과 비교 검증
- [ ] map_configs.json 포맷 호환성 확인
- [ ] 좌표 변환 결과 TarkovDBEditor와 일치 확인
- [ ] 다층 맵 (Labs, Reserve) 층 전환 테스트
- [ ] 빌드 성공 확인 (`dotnet build`)

---

## Agent Learning Log

> 이 섹션은 에이전트가 작업 중 학습한 프로젝트 특화 정보를 기록합니다.
> 작업 완료 시 중요한 발견사항을 여기에 추가하세요.

### Discovered Patterns

**2025-12-17: Quest Objective 조회 패턴**
- Quest objectives는 반드시 **ID 기반 조회**를 사용해야 함
- `TaskObjectiveWithLocation` 모델에 `QuestId` 필드 추가됨
- `_progressService.GetTaskById(questId)` 사용 (ID 기반)
- `_progressService.GetTask(normalizedName)`는 deprecated - fallback용으로만 사용
- NormalizedName은 다음 용도로만 사용:
  - 중복 제거를 위한 집합 추적
  - 목표 필터링 (GetObjectivesForTask)
  - UI 그룹화
- **CLAUDE.md 제약사항 준수**: "Quest의 NormalizedName은 데이터 마이그레이션에서만 사용, DB상의 ID가 기준"

**좌표 변환 패턴**
- DB의 LocationPoint: `X`, `Y` (높이), `Z` (수평면)
- TaskObjectiveWithLocation: `X`, `Y` (수평면), `Z` (높이)
- 변환 시: DB의 Z → Location의 Y, DB의 Y → Location의 Z
- 화면 표시 시: `config.GameToScreen(location.X, location.Y)` 사용 (Z는 높이이므로 사용 안 함)

### Known Issues

**2025-12-17: Quest Objectives 패널 버그 (해결됨)**
- **문제**: MapPage의 RefreshQuestDrawer()에서 NormalizedName 기반 조회 사용으로 null 반환
- **원인**: TaskObjectiveWithLocation에 QuestId 필드 없음, NormalizedName만으로 조회 시도
- **해결**: QuestId 필드 추가 및 ID 기반 조회로 변경 (fallback 포함)
- **변경 파일**:
  - `Models/Map/QuestObjectiveLocation.cs` (line 113): QuestId 필드 추가
  - `Services/Map/QuestObjectiveService.cs` (line 176): QuestId 복사
  - `Pages/Map/MapPage.xaml.cs` (line 3189-3190): ID 기반 조회 + fallback

### Project-Specific Notes

**Map Quest Objectives 데이터 흐름**
1. DB 로드: `QuestObjectiveDbService` (QuestObjectives + Quests JOIN)
2. 변환: `QuestObjectiveService.ConvertToTaskObjective()` - QuestId 포함
3. UI 표시: `MapPage.RefreshQuestDrawer()` - ID 기반 조회 사용
4. 진행 상태: `QuestProgressService.GetTaskById()` 또는 fallback `GetTask()`

**안전한 Quest 조회 패턴**
```csharp
// ✅ 올바른 패턴 (ID 우선, NormalizedName fallback)
var task = _progressService.GetTaskById(taskObj.QuestId)
    ?? _progressService.GetTask(taskObj.TaskNormalizedName);

// ❌ 잘못된 패턴 (NormalizedName만 사용)
var task = _progressService.GetTask(taskObj.TaskNormalizedName);
```

---

**Last Updated**: 2025-12-17
