# Tarkov Market Map Migration PRD
## Version 1.0 | 2025-12

---

## 1. Overview

### 1.1 Purpose
Tarkov Market에서 추출한 새로운 맵 SVG 파일들을 기존 좌표 시스템과 호환되도록 마이그레이션하기 위한 Product Requirements Document.

### 1.2 Background
Tarkov Market (tarkov-market.com)에서 업데이트된 맵 SVG 파일 11개를 추출했습니다. 이 맵들은 기존 tarkov.dev 맵과 다른 viewBox 좌표 체계를 사용하므로, 기존 마커 렌더링 시스템과 호환되도록 좌표 변환 설정을 업데이트해야 합니다.

### 1.3 Goals
1. 새 Tarkov Market 맵에서 기존과 동일한 위치에 퀘스트 마커가 표시되도록 함
2. 플레이어 위치 추적 기능이 새 맵에서도 정상 작동하도록 함
3. 신규 맵 Labyrinth 지원 추가

---

## 2. Current State Analysis (현재 상태 분석)

### 2.1 Extracted Maps from Tarkov Market

| Map | File Size | viewBox | Status |
|-----|-----------|---------|--------|
| GroundZero.svg | 73KB | 0 0 2800 3100 | New |
| Factory.svg | 67KB | 0 0 3600 3600 | New |
| Customs.svg | 190KB | 0 0 4400 3200 | New |
| Woods.svg | 171KB | 0 0 4800 4800 | New |
| Shoreline.svg | 254KB | 0 0 3700 3100 | New |
| Interchange.svg | 231KB | 0 0 4000 3900 | New |
| Reserve.svg | 123KB | 0 0 3200 3000 | New |
| Lighthouse.svg | 240KB | 0 0 3100 3700 | New |
| StreetsOfTarkov.svg | 88KB | 0 0 3260 3500 | New |
| Labs.svg | 49KB | 0 0 5500 4200 | New |
| **Labyrinth.svg** | 96KB | 0 0 3300 3200 | **NEW MAP** |

### 2.2 Backup Location
기존 맵들은 `TarkovHelper/Assets/Maps/Backup/`에 백업됨.

### 2.3 ViewBox Comparison

| Map | Old viewBox | New viewBox | Scale Factor |
|-----|-------------|-------------|--------------|
| Customs | 0 0 1062 535 | 0 0 4400 3200 | ~4.1x, ~6x |
| Factory | 0 0 131 141 | 0 0 3600 3600 | ~27x |
| GroundZero | 0 0 349 488 | 0 0 2800 3100 | ~8x, ~6.4x |
| Interchange | 0 0 977 977 | 0 0 4000 3900 | ~4.1x |
| Lighthouse | 0 0 1059 1723 | 0 0 3100 3700 | ~2.9x, ~2.1x |
| Reserve | 0 0 827 761 | 0 0 3200 3000 | ~3.9x |
| Shoreline | 0 0 1560 1032 | 0 0 3700 3100 | ~2.4x, ~3x |
| StreetsOfTarkov | 0 0 605 832 | 0 0 3260 3500 | ~5.4x, ~4.2x |
| Woods | 0 0 1402 1421 | 0 0 4800 4800 | ~3.4x |
| Labs | 0 0 720 586 | 0 0 5500 4200 | ~7.6x, ~7.2x |

---

## 3. Coordinate Transformation System

### 3.1 Current Implementation

**파이프라인:**
```
Game Coordinates (x, y, z)
    ↓
pos() → (lat=z, lng=x)
    ↓
applyRotation(coordinateRotation)
    ↓
CRS Transform: pixel = scale * coord + margin
    ↓
Normalize to SVG viewBox coordinates
```

**핵심 파일:**
- `Services/MapTracker/MapCoordinateTransformer.cs`
- `Services/MapTracker/MapTrackerService.cs`
- `Data/map_tracker_settings.json`

### 3.2 MapConfig Structure

```json
{
  "key": "Woods",
  "displayName": "Woods",
  "imagePath": "Assets/Maps/Woods_tarkovdev.svg",
  "imageWidth": 1402,
  "imageHeight": 1421,
  "transform": [0.1855, 113.1, 0.1855, 167.8],
  "coordinateRotation": 180,
  "svgBounds": [[650, -945], [-695, 470]],
  "markerScale": 1
}
```

### 3.3 Transform Array Meaning
```
transform[0] = scaleX  (게임 좌표 → 픽셀 X 스케일)
transform[1] = marginX (픽셀 X 오프셋)
transform[2] = scaleY  (게임 좌표 → 픽셀 Y 스케일, 내부에서 -1 곱함)
transform[3] = marginY (픽셀 Y 오프셋)
```

### 3.4 SvgBounds Meaning
```
svgBounds[0] = [lng1, lat1]  // 게임 좌표 기준 첫 번째 코너
svgBounds[1] = [lng2, lat2]  // 게임 좌표 기준 두 번째 코너
```

---

## 4. Migration Strategy

### 4.1 Option A: Recalculate Transform Values (Selected)

새 viewBox에 맞게 Transform 값을 재계산합니다.

**변환 공식:**
```
newScaleX = oldScaleX * (newViewBoxWidth / oldViewBoxWidth)
newMarginX = oldMarginX * (newViewBoxWidth / oldViewBoxWidth)
newScaleY = oldScaleY * (newViewBoxHeight / oldViewBoxHeight)
newMarginY = oldMarginY * (newViewBoxHeight / oldViewBoxHeight)
```

**장점:**
- 기존 코드 변경 최소화
- tarkov.dev API 좌표와 호환 유지
- SvgBounds는 게임 좌표 기준이므로 변경 불필요

**단점:**
- 정확한 캘리브레이션 필요

### 4.2 Option B: Modify SVG viewBox (Not Selected)

새 SVG의 viewBox를 기존과 동일하게 변환.

**미선택 사유:**
- SVG 내부 모든 좌표 변환 필요 (복잡)
- 이미지 품질 저하 가능성

---

## 5. Implementation Plan

### Phase 1: Transform 계산 스크립트 작성

#### 5.1.1 Task: Python 계산 스크립트 생성

**파일:** `scripts/calculate_new_transforms.py`

**기능:**
1. 기존 map_tracker_settings.json 로드
2. 새/기존 viewBox 비교
3. 새 Transform 값 계산
4. 결과 JSON 출력

**입력:**
- 기존 settings: `Data/map_tracker_settings.json`
- 기존 viewBox: `Assets/Maps/Backup/*.svg`
- 새 viewBox: `Assets/Maps/*.svg`

**출력:**
- 새 settings 제안: `Data/map_tracker_settings_new.json`

#### 5.1.2 Expected Transform Calculations

예시 (Woods):
```
Old viewBox: 0 0 1402 1421
New viewBox: 0 0 4800 4800

scaleRatioX = 4800 / 1402 = 3.424
scaleRatioY = 4800 / 1421 = 3.377

newTransform[0] = 0.1855 * 3.424 = 0.635
newTransform[1] = 113.1 * 3.424 = 387.3
newTransform[2] = 0.1855 * 3.377 = 0.627
newTransform[3] = 167.8 * 3.377 = 566.7

newImageWidth = 4800
newImageHeight = 4800
```

---

### Phase 2: Settings 파일 업데이트

#### 5.2.1 Task: map_tracker_settings.json 수정

**변경 내용:**
1. 각 맵의 `imagePath`를 새 맵 경로로 변경 (접미사 `_tarkovdev` 제거)
2. `imageWidth`, `imageHeight`를 새 viewBox 크기로 업데이트
3. `transform` 배열 업데이트
4. `markerScale` 조정 (필요시)

#### 5.2.2 신규 맵 Labyrinth 설정 추가

```json
{
  "key": "Labyrinth",
  "displayName": "The Labyrinth",
  "imagePath": "Assets/Maps/Labyrinth.svg",
  "imageWidth": 3300,
  "imageHeight": 3200,
  "transform": [2.115, 85.5, 2.115, 128.0],
  "coordinateRotation": 270,
  "svgBounds": [[-52, -37], [53, 76]],
  "markerScale": 2,
  "aliases": ["labyrinth", "LABYRINTH", "the-labyrinth"]
}
```

---

### Phase 3: 검증 및 테스트

#### 5.3.1 Task: 마커 위치 검증

**테스트 케이스:**

| Map | Test Quest | API Coordinates | Expected Visual Position |
|-----|------------|-----------------|--------------------------|
| Woods | Jaeger's Camp | x=-256, z=9.7 | 맵 중앙-오른쪽 |
| Customs | Delivery from the Past | (TBD) | Factory key 위치 |
| Interchange | Database Part 1 | (TBD) | OLI 내부 |
| Factory | Delivery from the Past | (TBD) | 서류가방 위치 |

#### 5.3.2 Task: 플레이어 위치 추적 테스트

1. 스크린샷 좌표 파싱 정상 동작 확인
2. 플레이어 마커가 올바른 위치에 표시되는지 확인
3. 이동 경로(trail) 정상 표시 확인

---

### Phase 4: 미세 조정

#### 5.4.1 Task: 시각적 캘리브레이션

계산된 Transform 값으로 마커가 정확히 표시되지 않는 경우:
1. 알려진 좌표 포인트 2개 이상 선정
2. 실제 표시 위치와 기대 위치 차이 측정
3. Transform margin 값 미세 조정

#### 5.4.2 Task: MarkerScale 조정

새 맵의 스케일이 크게 달라진 경우 마커 크기 조정:
- Factory: markerScale 조정 필요 (27x 스케일 증가)
- Labs: markerScale 조정 필요 (7x 스케일 증가)

---

## 6. File Changes Summary

### 6.1 Modified Files

| File | Change Type | Description |
|------|-------------|-------------|
| `Data/map_tracker_settings.json` | Modify | Transform, imageWidth/Height, imagePath 업데이트 |
| `Assets/Maps/*.svg` | Replace | 11개 새 맵 파일 (이미 완료) |

### 6.2 New Files

| File | Description |
|------|-------------|
| `scripts/calculate_new_transforms.py` | Transform 계산 스크립트 |
| `Assets/Maps/Backup/*` | 기존 맵 백업 (이미 완료) |

### 6.3 Unchanged Files

| File | Reason |
|------|--------|
| `MapCoordinateTransformer.cs` | 좌표 변환 로직 변경 불필요 |
| `MapTrackerService.cs` | 설정 로딩 로직 변경 불필요 |
| `MapTrackerPage.xaml.cs` | UI 로직 변경 불필요 |

---

## 7. tarkov.dev Transform Reference

tarkov.dev에서 사용하는 공식 Transform 값 (참고용):

| Map | Transform | Bounds | Rotation |
|-----|-----------|--------|----------|
| Streets of Tarkov | [0.38, 0, 0.38, 0] | [[323, -317], [-280, 554]] | 180 |
| Ground Zero | [0.524, 167.3, 0.524, 65.1] | [[249, -124], [-99, 364]] | 180 |
| Customs | [0.239, 168.65, 0.239, 136.35] | [[698, -307], [-372, 237]] | 180 |
| Factory | [1.629, 119.9, 1.629, 139.3] | [[79, -64.5], [-66.5, 67.4]] | 90 |
| Interchange | [0.265, 150.6, 0.265, 134.6] | [[532.75, -442.75], [-364, 453.5]] | 180 |
| The Lab | [0.575, 281.2, 0.575, 193.7] | [[-80, -477], [-287, -193]] | 270 |
| The Labyrinth | [2.115, 85.5, 2.115, 128.0] | [[-52, -37], [53, 76]] | 270 |
| Lighthouse | [0.2, 0, 0.2, 0] | [[515, -998], [-545, 725]] | 180 |
| Reserve | [0.395, 122.0, 0.395, 137.65] | [[289, -293], [-303, 244]] | 180 |
| Shoreline | [0.16, 83.2, 0.16, 111.1] | [[508, -415], [-1060, 618]] | 180 |
| Woods | [0.1855, 113.1, 0.1855, 167.8] | [[650, -945], [-762, 470]] | 180 |

---

## 8. Risk Assessment

### 8.1 Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Transform 계산 오류 | Medium | High | 여러 테스트 좌표로 검증 |
| 일부 맵 좌표 불일치 | Medium | Medium | 개별 맵 미세 조정 |
| Labyrinth 좌표 데이터 부족 | High | Low | tarkov.dev API 데이터 활용 |

### 8.2 Rollback Plan

문제 발생 시:
1. `Assets/Maps/Backup/`에서 기존 맵 복원
2. `map_tracker_settings.json` 이전 버전 복원
3. Git commit 이전으로 롤백

---

## 9. Testing Checklist

### 9.1 Functional Tests

- [ ] 각 맵 로드 정상 확인 (11개)
- [ ] 퀘스트 마커 위치 정확도 검증
- [ ] 플레이어 위치 추적 정상 동작
- [ ] 탈출구 마커 위치 정확도
- [ ] 맵 줌/팬 기능 정상 동작
- [ ] 맵 레벨 전환 정상 동작 (해당 맵)

### 9.2 Visual Tests

- [ ] 마커 크기 적절함
- [ ] 마커가 맵 경계 내에 표시됨
- [ ] 퀘스트 드로어 목표 표시 정상

### 9.3 Integration Tests

- [ ] Quests 탭과 Map Tracker 동기화 정상
- [ ] 설정 저장/로드 정상
- [ ] 앱 재시작 후 설정 유지

---

## 10. Success Criteria

1. 모든 11개 맵에서 퀘스트 마커가 정확한 위치에 표시됨
2. 플레이어 위치 추적이 새 맵에서 정상 동작함
3. 신규 맵 Labyrinth가 정상적으로 지원됨
4. 기존 기능 (체크박스 동기화, 필터링 등) 정상 유지

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-12-09 | Claude | Initial document creation |
