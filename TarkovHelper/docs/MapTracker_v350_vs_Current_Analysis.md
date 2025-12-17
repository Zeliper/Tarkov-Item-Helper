# Map Tracker v3.5.0 vs Current Version Analysis

## Executive Summary

v3.5.0 버전에는 풍부한 Map 탭 기능이 있었으나, 현재 버전에서는 대부분의 기능이 제거되고 기본적인 스크린샷 감시 기능만 남아있습니다.

---

## Architecture Comparison

### v3.5.0 Structure
```
TarkovHelper/
├── Models/MapTracker/
│   ├── CalibrationPoint.cs        # 맵 보정점 모델
│   ├── EftPosition.cs             # 게임 좌표 모델
│   ├── MapConfig.cs               # 맵 설정 (변환 매트릭스, 층 정보)
│   ├── MapExtract.cs              # 탈출구 모델
│   ├── MapFloorConfig.cs          # 층 설정 모델
│   ├── MapTrackerSettings.cs      # 전체 설정 모델
│   ├── OldMapReferenceData.cs     # 이전 맵 참조 데이터
│   ├── QuestObjectiveLocation.cs  # 퀘스트 목표 위치
│   └── ScreenPosition.cs          # 화면 좌표 모델
│
└── Services/MapTracker/
    ├── AutoCalibrationService.cs     # 자동 보정
    ├── ExtractService.cs             # 탈출구 데이터 (tarkov.dev API)
    ├── IMapCoordinateTransformer.cs  # 좌표 변환 인터페이스
    ├── IScreenshotCoordinateParser.cs# 파싱 인터페이스
    ├── LogMapWatcherService.cs       # 로그 맵 감시 (자동 맵 전환)
    ├── MapCalibrationService.cs      # 맵 보정 서비스
    ├── MapComparisonService.cs       # 맵 비교 서비스
    ├── MapCoordinateTransformer.cs   # 좌표 변환 로직
    ├── MapTrackerService.cs          # 메인 서비스
    ├── OldMapTransformService.cs     # 이전 맵 변환
    ├── QuestObjectiveService.cs      # 퀘스트 목표 (tarkov.dev API)
    ├── ScreenshotCoordinateParser.cs # 스크린샷 좌표 파싱
    ├── ScreenshotWatcherService.cs   # 스크린샷 폴더 감시
    └── SvgStylePreprocessor.cs       # SVG 스타일 처리
```

### Current Version Structure
```
TarkovHelper/
├── Models/
│   └── EftPosition.cs             # 기본 좌표 모델만 존재
│
└── Services/
    └── MapTrackerService.cs       # 단일 서비스 (스크린샷 감시만)
```

---

## Feature Comparison

| Feature | v3.5.0 | Current | Status |
|---------|--------|---------|--------|
| **스크린샷 감시** | ✅ 고급 | ✅ 기본 | 간소화됨 |
| **좌표 변환** | ✅ CalibratedTransform + IDW | ❌ 없음 | **제거됨** |
| **맵 보정** | ✅ 자동/수동 보정 | ❌ 없음 | **제거됨** |
| **층 전환** | ✅ 다층 맵 지원 | ❌ 없음 | **제거됨** |
| **탈출구 마커** | ✅ tarkov.dev API | ❌ 없음 | **제거됨** |
| **퀘스트 목표 마커** | ✅ tarkov.dev API | ❌ 없음 | **제거됨** |
| **자동 맵 전환** | ✅ 게임 로그 감시 | ❌ 없음 | **제거됨** |
| **SVG 맵 렌더링** | ✅ SharpVectors + 층별 | ⚠️ 기본만 | 간소화됨 |
| **설정 저장** | ✅ JSON 파일 | ⚠️ 제한적 | 간소화됨 |
| **퀘스트 드로어** | ✅ 필터링, 그룹화 | ⚠️ 기본만 | 간소화됨 |
| **마커 클러스터링** | ✅ 겹치는 마커 그룹화 | ❌ 없음 | **제거됨** |
| **Trail (이동 경로)** | ✅ 고급 | ⚠️ 기본 | 간소화됨 |

---

## Detailed Feature Analysis

### 1. Coordinate Transformation (좌표 변환)

#### v3.5.0
```csharp
// MapCoordinateTransformer.cs
private bool TryTransformGameCoordinate(string mapKey, double gameX, double? gameZ, double? angle, out ScreenPosition? screenPosition)
{
    // 보정된 변환이 있으면 IDW 보정을 적용한 변환 사용
    if (config.CalibratedTransform != null && config.CalibratedTransform.Length >= 6)
    {
        var calibrationService = MapCalibrationService.Instance;
        (finalX, finalY) = calibrationService.ApplyCalibratedTransformWithIDW(
            config.CalibratedTransform,
            config.CalibrationPoints,
            gameX,
            gameZ ?? 0);
    }
    // ...
}
```

**기능:**
- 아핀 변환 (Affine Transform) 지원
- IDW (Inverse Distance Weighting) 보정
- 보정점 기반 정확도 향상
- 회전 적용 (CoordinateRotation)
- SVG viewBox 좌표로 정규화

#### Current Version
```csharp
// MapTrackerService.cs
public bool TryParsePosition(string fileName, out EftPosition? position)
{
    // 단순히 파일명에서 좌표만 파싱
    // 좌표 변환 없음
}
```

**기능:**
- 파일명에서 좌표 파싱만 수행
- 좌표 변환 로직 없음
- 맵에 마커 표시 불가

---

### 2. Extract Service (탈출구 서비스)

#### v3.5.0
```csharp
// ExtractService.cs
public async Task<List<MapExtract>> FetchExtractsAsync(Action<string>? progressCallback = null)
{
    progressCallback?.Invoke("Fetching English extract data...");
    var mapsEn = await FetchMapsWithExtractsAsync("en");

    progressCallback?.Invoke("Fetching Korean extract data...");
    var mapsKo = await FetchMapsWithExtractsAsync("ko");
    // ...
}
```

**기능:**
- tarkov.dev GraphQL API에서 탈출구 데이터 가져옴
- 다국어 지원 (영어, 한국어)
- PMC/Scav 탈출구 구분
- 좌표 정보 포함
- 캐싱 지원

#### Current Version
- **완전히 제거됨**

---

### 3. Quest Objective Service (퀘스트 목표 서비스)

#### v3.5.0
```csharp
// QuestObjectiveService.cs
public async Task<List<TaskObjectiveWithLocation>> FetchObjectivesAsync()
{
    // tarkov.dev API에서 퀘스트 목표 위치 데이터 가져옴
    // zones, possibleLocations 정보 포함
}
```

**기능:**
- tarkov.dev API에서 퀘스트 목표 위치 가져옴
- 맵별, 퀘스트별 인덱싱
- 다국어 지원
- 위치 좌표 포함

#### Current Version
- **완전히 제거됨**

---

### 4. Log Map Watcher (로그 맵 감시)

#### v3.5.0
```csharp
// LogMapWatcherService.cs
private static readonly Dictionary<string, string> MapNameMapping = new(StringComparer.OrdinalIgnoreCase)
{
    { "bigmap", "Customs" },
    { "factory4_day", "Factory" },
    { "woods", "Woods" },
    // ...
};

private void ProcessLogFileChanges(string filePath)
{
    // 로그 파일에서 맵 변경 감지
    // 자동으로 현재 맵 전환
}
```

**기능:**
- EFT 로그 파일 (application.log) 실시간 감시
- 게임 내 맵 변경 자동 감지
- 자동 맵 전환
- 맵 이름 매핑 (내부 이름 → 표시 이름)

#### Current Version
- **완전히 제거됨**

---

### 5. Floor Switching (층 전환)

#### v3.5.0
```json
// map_configs.json
{
  "key": "Customs",
  "floors": [
    {"layerId": "basement", "displayName": "Basement", "order": -1},
    {"layerId": "main", "displayName": "Ground Floor", "order": 0, "isDefault": true},
    {"layerId": "level2", "displayName": "Level 2", "order": 1},
    {"layerId": "level3", "displayName": "Level 3", "order": 2}
  ]
}
```

```csharp
// MapTrackerPage.xaml.cs
private void CmbFloorSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    // 층 전환 시 SVG 레이어 표시/숨김
}
```

**기능:**
- 다층 맵 (Customs, Shoreline, Reserve 등) 지원
- 층별 SVG 레이어 표시/숨김
- 층 선택 콤보박스
- 높이 기반 자동 층 감지

#### Current Version
- **완전히 제거됨**

---

### 6. Map Calibration (맵 보정)

#### v3.5.0
```csharp
// MapCalibrationService.cs
public (double x, double y) ApplyCalibratedTransformWithIDW(
    double[] transform,
    List<CalibrationPoint>? calibrationPoints,
    double gameX,
    double gameZ)
{
    // 1. 아핀 변환 적용
    // 2. IDW 보정으로 정확도 향상
}
```

```json
// map_configs.json
{
  "calibratedTransform": [-3.394478979463914, -0.00672470985271966, 0.028968077414678935, 3.3624781094363794, 2073.690129251411, 3161.542755683878],
  "playerMarkerTransform": [-2, 0, 0, 2, 2200, 2841.5]
}
```

**기능:**
- 수동 보정점 설정
- 자동 보정 (기존 데이터 활용)
- IDW (역거리 가중치) 보간
- 변환 매트릭스 저장/로드

#### Current Version
- **완전히 제거됨**

---

### 7. Settings Management (설정 관리)

#### v3.5.0
```csharp
// MapTrackerSettings.cs
public sealed class MapTrackerSettings
{
    public string ScreenshotFolderPath { get; set; }
    public string FileNamePattern { get; set; }
    public List<MapConfig> Maps { get; set; }
    public int MarkerSize { get; set; }
    public int PlayerMarkerSize { get; set; }
    public bool ShowPmcExtracts { get; set; }
    public bool ShowScavExtracts { get; set; }
    public QuestMarkerStyle QuestMarkerStyle { get; set; }
    public Dictionary<string, string> MarkerColors { get; set; }
    // ... 더 많은 설정
}
```

**기능:**
- JSON 파일 (map_tracker_settings.json) 저장
- 다양한 UI 설정 저장
- 맵별 보정 데이터 저장
- 마커 스타일/색상 커스터마이징

#### Current Version
- 대부분의 설정 제거됨
- 기본적인 폴더 경로 설정만 존재

---

## UI Feature Comparison

### v3.5.0 MapTrackerPage 기능
1. **상단 컨트롤 바**
   - 맵 선택 콤보박스
   - 층 선택 콤보박스 (다층 맵)
   - 퀘스트 마커 토글
   - 탈출구 마커 토글
   - Trail 초기화 버튼
   - 전체화면 버튼
   - 설정 버튼
   - 추적 시작/중지 버튼

2. **상태 표시 바**
   - 추적 상태 표시 (색상 인디케이터)
   - 현재 좌표 표시
   - 마지막 업데이트 시간

3. **퀘스트 드로어 (왼쪽 패널)**
   - 퀘스트 목표 목록
   - 진행률 표시
   - 상태 필터 (전체/미완료/완료)
   - 타입 필터 (방문/마크/설치/탈출/획득)
   - 현재 맵만 보기 옵션
   - 퀘스트별 그룹화 옵션
   - 체크박스로 완료 표시

4. **맵 영역**
   - SVG 맵 렌더링
   - 플레이어 마커 (방향 화살표 포함)
   - 퀘스트 목표 마커 (타입별 아이콘)
   - 탈출구 마커
   - Trail (이동 경로)
   - 줌/드래그 지원
   - 층별 레이어 표시

5. **설정 패널 (오른쪽)**
   - 스크린샷 폴더 설정
   - 마커 크기 조정
   - 퀘스트 마커 스타일
   - 탈출구 표시 설정
   - 마커 색상 커스터마이징

### Current Version MapTrackerPage
- 기본적인 맵 표시
- 스크린샷 감시
- 좌표 파싱
- 대부분의 고급 기능 제거됨

---

## Data Flow Comparison

### v3.5.0
```
Screenshot Created
       │
       ▼
ScreenshotWatcherService (파일 감시)
       │
       ▼
ScreenshotCoordinateParser (좌표 파싱)
       │
       ▼
MapCoordinateTransformer (좌표 변환)
       │  ├─ Affine Transform
       │  └─ IDW Correction
       │
       ▼
ScreenPosition (화면 좌표)
       │
       ▼
MapTrackerPage UI
       │  ├─ Player Marker
       │  ├─ Quest Objectives
       │  ├─ Extract Markers
       │  └─ Trail Path
       │
       ▼
SVG Map Rendering
```

### Current Version
```
Screenshot Created
       │
       ▼
MapTrackerService (파일 감시 + 파싱)
       │
       ▼
EftPosition (게임 좌표만)
       │
       ▼
MapTrackerPage UI
       │
       ▼
??? (좌표 변환 없음)
```

---

## Missing Critical Components

### 1. map_configs.json 사용
v3.5.0에서는 `Assets/DB/Data/map_configs.json`에 각 맵의 변환 매트릭스가 정의되어 있었습니다:
- `calibratedTransform`: 보정된 아핀 변환 매트릭스
- `playerMarkerTransform`: 플레이어 마커용 변환
- `svgBounds`: SVG 경계
- `floors`: 층 정보

현재 버전에서는 이 설정을 읽고 적용하는 코드가 없습니다.

### 2. tarkov.dev API 연동
v3.5.0에서는 tarkov.dev GraphQL API에서 실시간 데이터를 가져왔습니다:
- 탈출구 위치 및 정보
- 퀘스트 목표 위치
- 다국어 번역

현재 버전에서는 이 연동이 완전히 제거되었습니다.

### 3. 좌표 변환 시스템
게임 좌표 (X, Y, Z) → 화면 좌표 변환이 없어서 마커가 올바른 위치에 표시되지 않습니다.

---

## Recommendations

1. **v3.5.0 코드 복원**: MapTracker 관련 서비스들을 v3.5.0에서 복원
2. **점진적 마이그레이션**: 핵심 기능부터 단계적으로 복원
3. **데이터 파일 확인**: map_configs.json, 캐시 파일 등이 올바르게 로드되는지 확인
4. **테스트**: 각 맵에서 좌표 변환이 정확한지 테스트

---

## Conclusion

v3.5.0에서 현재 버전으로 오면서 Map 탭의 핵심 기능들이 대부분 제거되었습니다. 이로 인해:

1. **좌표 변환 불가**: 스크린샷에서 파싱한 좌표를 맵에 표시할 수 없음
2. **탈출구 표시 불가**: tarkov.dev API 연동 제거
3. **퀘스트 목표 표시 불가**: 위치 기반 퀘스트 추적 불가
4. **자동 맵 전환 불가**: 로그 감시 기능 제거
5. **층 전환 불가**: 다층 맵에서 층별 보기 불가

v3.5.0의 기능을 복원하려면 `Services/MapTracker/` 폴더와 `Models/MapTracker/` 폴더의 파일들을 다시 추가해야 합니다.
