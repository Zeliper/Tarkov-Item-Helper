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

_아직 기록된 패턴이 없습니다._

### Known Issues

_아직 기록된 이슈가 없습니다._

### Project-Specific Notes

_아직 기록된 노트가 없습니다._

---

**Last Updated**: 2025-12-17
