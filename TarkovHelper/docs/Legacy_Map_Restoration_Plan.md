# Legacy Map Restoration Plan

v3.5.0의 Map 기능을 Legacy_Map으로 복원하고 DB 기반으로 변경하는 계획입니다.

---

## 진행 상태

| Phase | 상태 | 설명 |
|-------|------|------|
| Phase 1 | ✅ 완료 | Models/Legacy_Map 복원 (9개 파일) |
| Phase 2 | ✅ 완료 | Services/Legacy_Map 복원 (11개 서비스) |
| Phase 3 | ✅ 완료 | DB 기반 어댑터 서비스 생성 (3개 서비스) |
| Phase 4 | ✅ 완료 | UI 복원 (LegacyMapPage) |
| Phase 5 | ✅ 완료 | 빌드 확인 완료 |

---

## 개요

v3.5.0에서 제거된 Map 기능을 `Legacy_Map` 네임스페이스로 복원하고,
tarkov.dev API 대신 로컬 SQLite DB를 사용하도록 변경합니다.

---

## 파일 목록

### 복원된 Models (9개) ✅

| 원본 경로 | 새 경로 | 상태 |
|-----------|---------|------|
| `Models/MapTracker/CalibrationPoint.cs` | `Models/Legacy_Map/CalibrationPoint.cs` | ✅ 완료 |
| `Models/MapTracker/EftPosition.cs` | `Models/Legacy_Map/EftPosition.cs` | ✅ 완료 |
| `Models/MapTracker/MapConfig.cs` | `Models/Legacy_Map/MapConfig.cs` | ✅ 완료 |
| `Models/MapTracker/MapExtract.cs` | `Models/Legacy_Map/MapExtract.cs` | ✅ 완료 |
| `Models/MapTracker/MapFloorConfig.cs` | `Models/Legacy_Map/MapFloorConfig.cs` | ✅ 완료 |
| `Models/MapTracker/MapTrackerSettings.cs` | `Models/Legacy_Map/MapTrackerSettings.cs` | ✅ 완료 |
| `Models/MapTracker/OldMapReferenceData.cs` | `Models/Legacy_Map/OldMapReferenceData.cs` | ✅ 완료 |
| `Models/MapTracker/QuestObjectiveLocation.cs` | `Models/Legacy_Map/QuestObjectiveLocation.cs` | ✅ 완료 |
| `Models/MapTracker/ScreenPosition.cs` | `Models/Legacy_Map/ScreenPosition.cs` | ✅ 완료 |

### 복원된 Services (14개) ✅

| 파일명 | 설명 | 상태 |
|--------|------|------|
| `IMapCoordinateTransformer.cs` | 좌표 변환 인터페이스 | ✅ 완료 |
| `IScreenshotCoordinateParser.cs` | 스크린샷 파서 인터페이스 | ✅ 완료 |
| `MapCalibrationService.cs` | 맵 보정 서비스 (IDW 보정) | ✅ 완료 |
| `ScreenshotCoordinateParser.cs` | 스크린샷 파일명 파싱 | ✅ 완료 |
| `ScreenshotWatcherService.cs` | 스크린샷 폴더 감시 | ✅ 완료 |
| `OldMapTransformService.cs` | 구 지도 좌표 변환 | ✅ 완료 |
| `MapComparisonService.cs` | 구/신 지도 비교 | ✅ 완료 |
| `AutoCalibrationService.cs` | 자동 보정 서비스 | ✅ 완료 |
| `MapCoordinateTransformer.cs` | 좌표 변환 서비스 | ✅ 완료 |
| `LogMapWatcherService.cs` | 게임 로그 맵 감지 | ✅ 완료 |
| `SvgStylePreprocessor.cs` | SVG CSS→인라인 변환 | ✅ 완료 |
| `LegacyExtractService.cs` | DB 탈출구 어댑터 | ✅ 완료 (신규) |
| `LegacyQuestObjectiveService.cs` | DB 퀘스트 목표 어댑터 | ✅ 완료 (신규) |
| `LegacyMapTrackerService.cs` | 메인 통합 서비스 | ✅ 완료 (신규) |

### 복원된 Pages (2개) ✅

| 원본 경로 | 새 경로 | 상태 |
|-----------|---------|------|
| `Pages/MapTrackerPage.xaml` | `Pages/Legacy_Map/LegacyMapPage.xaml` | ✅ 완료 |
| `Pages/MapTrackerPage.xaml.cs` | `Pages/Legacy_Map/LegacyMapPage.xaml.cs` | ✅ 완료 |

---

## API → DB 변경 상세

### 1. ExtractService → LegacyExtractService ✅

기존 `MapMarkerDbService`를 래핑하여 `Legacy_Map.MapExtract` 모델로 변환합니다.

```csharp
// LegacyExtractService.cs
public List<MapExtract> GetExtractsForMap(string mapKey)
{
    var markers = _markerService.GetExtractionsForMap(mapKey);
    return markers.Select(ConvertToMapExtract).ToList();
}
```

### 2. QuestObjectiveService → LegacyQuestObjectiveService ✅

기존 `QuestObjectiveDbService`를 래핑하여 `Legacy_Map.TaskObjectiveWithLocation` 모델로 변환합니다.

```csharp
// LegacyQuestObjectiveService.cs
public List<TaskObjectiveWithLocation> GetObjectivesForMap(string mapKey, LegacyMapConfig mapConfig)
{
    var baseConfig = ConvertToBaseMapConfig(mapConfig);
    var objectives = _objectiveService.GetObjectivesForMap(mapKey, baseConfig);
    return objectives.Select(o => ConvertToTaskObjective(o, mapConfig)).ToList();
}
```

### 3. MapTrackerSettings 변경 ✅

- 맵 설정: `Assets/DB/Data/map_configs.json`에서 로드
- 사용자 설정: `LegacyMapTrackerService`에서 관리

---

## DB 테이블 매핑

### MapMarkers → MapExtract

| DB Column | Model Property | 변환 |
|-----------|----------------|------|
| `Id` | `Id` | 직접 |
| `Name` | `Name` | 직접 |
| `NameKo` | `NameKo` | 직접 |
| `MarkerType` | `Faction` | PmcExtraction→Pmc, ScavExtraction→Scav, SharedExtraction→Shared |
| `MapKey` | `MapName` | 직접 |
| `X` | `X` | 직접 |
| `Y` | `Y` | 직접 |
| `Z` | `Z` | 직접 |
| `FloorId` | (층 정보) | Top/Bottom 계산 필요 |

### QuestObjectives → QuestObjectiveLocation

| DB Column | Model Property | 변환 |
|-----------|----------------|------|
| `Id` | `ObjectiveId` | 직접 |
| `QuestId` | `TaskId` | 직접 |
| `ObjectiveType` | `Type` | 문자열 → enum |
| `Description` | `Description` | 직접 |
| `MapName` | `MapName` | 직접 |
| `LocationPoints` | `Positions` | JSON 파싱 |
| `OptionalPoints` | `OptionalPositions` | JSON 파싱 |

---

## 작업 순서

### Phase 1: 기본 복원 (Models + Core Services) ✅
1. ✅ Models/Legacy_Map 폴더에 모델 클래스 복원
2. ✅ namespace 변경: `TarkovHelper.Models.MapTracker` → `TarkovHelper.Models.Legacy_Map`

### Phase 2: Services 복원 (API 미사용) ✅
1. ✅ 좌표 변환 서비스들 복원 (MapCoordinateTransformer 등)
2. ✅ 스크린샷 감시 서비스들 복원
3. ✅ namespace 변경

### Phase 3: DB 기반 서비스 생성 ✅
1. ✅ `LegacyExtractService.cs` 신규 생성 - MapMarkerDbService 어댑터
2. ✅ `LegacyQuestObjectiveService.cs` 신규 생성 - QuestObjectiveDbService 어댑터
3. ✅ `LegacyMapTrackerService.cs` 메인 서비스 생성

### Phase 4: UI 복원 ✅
1. ✅ LegacyMapPage.xaml/cs 복원
2. ✅ MainWindow에서 Map 탭이 LegacyMapPage를 사용하도록 변경
3. ✅ 기존 MapTrackerPage를 LegacyMapPage로 대체

### Phase 5: 빌드 확인 ✅
1. ✅ 빌드 성공 확인
2. ⏳ 각 맵에서 좌표 변환 테스트 (런타임 테스트 필요)
3. ⏳ 탈출구 마커 표시 테스트 (런타임 테스트 필요)
4. ⏳ 퀘스트 목표 마커 테스트 (런타임 테스트 필요)
5. ⏳ 층 전환 테스트 (런타임 테스트 필요)

---

## 주의사항

1. **네임스페이스 충돌 방지**:
   - `TarkovHelper.Models.MapConfig`와 `TarkovHelper.Models.Legacy_Map.MapConfig` 공존
   - 명시적 using alias 사용 (예: `using LegacyMapConfig = ...`)

2. **map_configs.json 활용**: `Assets/DB/Data/map_configs.json`에 좌표 변환 설정 존재

3. **SVG 파일**: `Assets/DB/Maps/` 폴더에 SVG 맵 이미지 존재

4. **기존 서비스 재사용**:
   - `MapMarkerDbService` - 탈출구 데이터
   - `QuestObjectiveDbService` - 퀘스트 목표 데이터

---

## 파일 구조

```
TarkovHelper/
├── Models/
│   └── Legacy_Map/
│       ├── CalibrationPoint.cs
│       ├── EftPosition.cs
│       ├── MapConfig.cs
│       ├── MapExtract.cs
│       ├── MapFloorConfig.cs
│       ├── MapTrackerSettings.cs
│       ├── OldMapReferenceData.cs
│       ├── QuestObjectiveLocation.cs
│       └── ScreenPosition.cs
├── Services/
│   └── Legacy_Map/
│       ├── IMapCoordinateTransformer.cs
│       ├── IScreenshotCoordinateParser.cs
│       ├── AutoCalibrationService.cs
│       ├── LegacyExtractService.cs          (신규)
│       ├── LegacyMapTrackerService.cs       (신규)
│       ├── LegacyQuestObjectiveService.cs   (신규)
│       ├── LogMapWatcherService.cs
│       ├── MapCalibrationService.cs
│       ├── MapComparisonService.cs
│       ├── MapCoordinateTransformer.cs
│       ├── OldMapTransformService.cs
│       ├── ScreenshotCoordinateParser.cs
│       ├── ScreenshotWatcherService.cs
│       └── SvgStylePreprocessor.cs
└── Pages/
    └── Legacy_Map/                          (다음 단계)
        ├── LegacyMapPage.xaml
        └── LegacyMapPage.xaml.cs
```
