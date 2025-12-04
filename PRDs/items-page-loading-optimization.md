# Items 페이지 로딩 최적화 PRD

## 문제 정의

Data Cache를 제거하고 다시 데이터를 다운로드한 뒤 Items 탭으로 이동하면 "Loading items data..."가 약 2분간 표시되는 성능 문제.

## 근본 원인 분석

### 병목 지점: `ItemsPage.xaml.cs:446-457` (수정 전)

```csharp
foreach (var vm in _allItemViewModels)
{
    vm.IconSource = await _imageCache.GetItemIconAsync(vm.IconLink);  // 순차 대기!
}
```

- 300+개 아이템 이미지를 **순차적으로** 다운로드
- 각 다운로드 0.3~1초 × 300개 = **약 2분**

## 해결 계획 및 진행 상황

### 우선순위 1: UI 먼저 표시, 이미지는 백그라운드 로딩 ✅ 완료

**변경 내용:**
1. `AggregatedItemViewModel.IconSource`에 `INotifyPropertyChanged` 구현 추가
2. `LoadItemsAsync()`에서 이미지 로딩 코드 분리
3. `ItemsPage_Loaded`에서 Loading Overlay 숨긴 후 백그라운드 이미지 로딩 시작

**효과:** 아이템 리스트가 즉시 표시되고, 이미지는 점진적으로 로딩됨

### 우선순위 2: SemaphoreSlim으로 병렬 다운로드 ✅ 완료

**변경 내용:**
- `LoadItemImagesAsync()` 메서드 추가
- `SemaphoreSlim(10)`으로 동시에 10개 이미지 병렬 다운로드
- `Task.WhenAll()`로 모든 이미지 동시 처리

**효과:** 2분 → 약 10~20초 (네트워크 상태에 따라 다름)

### 우선순위 3: 가시 영역 우선 로딩 (Lazy Loading) ✅ 완료

**변경 내용:**
1. `LoadImagesInBackgroundAsync()` 2단계 로딩 구현
   - Phase 1: 현재 보이는 아이템 (~20개) 우선 로딩
   - Phase 2: 나머지 아이템 백그라운드 로딩
2. `GetVisibleItems()` - ScrollViewer에서 현재 보이는 아이템 범위 계산
3. `LstItems_ScrollChanged` - 스크롤 시 새로 보이는 아이템 이미지 로딩 (100ms 디바운스)

**효과:** 초기 화면이 1~2초 내에 이미지와 함께 표시됨

## 변경된 코드

### `ItemsPage.xaml`

```xml
<!-- ScrollChanged 이벤트 추가 -->
<ListBox x:Name="LstItems"
         ScrollViewer.ScrollChanged="LstItems_ScrollChanged"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         ... />
```

### `ItemsPage.xaml.cs`

```csharp
// 1. IconSource에 PropertyChanged 추가
private BitmapImage? _iconSource;
public BitmapImage? IconSource
{
    get => _iconSource;
    set
    {
        if (_iconSource != value)
        {
            _iconSource = value;
            OnPropertyChanged(nameof(IconSource));
        }
    }
}

// 2. 2단계 이미지 로딩
private async Task LoadImagesInBackgroundAsync()
{
    // Phase 1: 보이는 아이템 먼저
    await LoadVisibleItemImagesAsync();

    // Phase 2: 나머지 백그라운드 로딩
    await LoadRemainingItemImagesAsync();
}

// 3. 보이는 아이템 계산
private List<AggregatedItemViewModel> GetVisibleItems()
{
    var scrollViewer = GetScrollViewer(LstItems);
    // ScrollViewer 위치 기반으로 보이는 아이템 범위 계산
    ...
}

// 4. 스크롤 이벤트 (100ms 디바운스)
private void LstItems_ScrollChanged(object sender, ScrollChangedEventArgs e)
{
    // 스크롤 멈추면 보이는 아이템 이미지 로딩
    ...
}
```

## 진행 상황

| 단계 | 상태 | 날짜 | 비고 |
|------|------|------|------|
| 문제 분석 | ✅ 완료 | 2025-12-04 | 순차적 이미지 로딩이 주요 병목 |
| 우선순위 1 | ✅ 완료 | 2025-12-04 | UI 먼저 표시, 이미지 백그라운드 로딩 |
| 우선순위 2 | ✅ 완료 | 2025-12-04 | SemaphoreSlim(10) 병렬 다운로드 |
| 우선순위 3 | ✅ 완료 | 2025-12-04 | Lazy Loading (가시 영역 우선) |
| 빌드 검증 | ✅ 완료 | 2025-12-04 | 오류 0개 |

## 변경 파일

### `TarkovHelper/Pages/ItemsPage.xaml`
- Line 312: `ScrollViewer.ScrollChanged` 이벤트 추가

### `TarkovHelper/Pages/ItemsPage.xaml.cs`
- Line 3: `System.Threading` using 추가
- Line 32-44: `IconSource` 속성에 PropertyChanged 추가
- Line 346-359: 백그라운드 이미지 로딩 호출
- Line 484-497: `LoadImagesInBackgroundAsync()` - 2단계 로딩
- Line 499-516: `LoadVisibleItemImagesAsync()` - 가시 영역 로딩
- Line 518-534: `LoadRemainingItemImagesAsync()` - 나머지 로딩
- Line 536-571: `LoadItemImagesAsync()` - 병렬 다운로드
- Line 573-608: `GetVisibleItems()` - 보이는 아이템 계산
- Line 610-626: `GetScrollViewer()` - ScrollViewer 헬퍼
- Line 628-652: `LstItems_ScrollChanged()` - 스크롤 이벤트

## 테스트 방법

1. 설정 → "Clear Data Cache" 실행
2. 앱 재시작하여 데이터 다시 다운로드
3. Items 탭으로 이동
4. **예상 결과:**
   - 아이템 리스트가 즉시 표시됨
   - 보이는 아이템 이미지가 1~2초 내에 로딩됨
   - 스크롤 시 새 아이템 이미지가 빠르게 로딩됨
   - 전체 로딩 시간: 약 10~20초

## 성능 개선 요약

| 항목 | 이전 | 이후 |
|------|------|------|
| UI 표시 | ~2분 대기 | **즉시** |
| 보이는 이미지 | ~2분 | **1~2초** |
| 전체 이미지 | ~2분 (순차) | ~10-20초 (병렬) |
| 스크롤 반응 | 느림 | **즉시** (100ms 디바운스) |

## 롤백 계획

문제 발생 시 `git revert` 또는 아래 변경사항 수동 되돌리기:
1. `IconSource`를 단순 auto-property로 복원
2. XAML에서 `ScrollViewer.ScrollChanged` 제거
3. 새 메서드들 삭제하고 원래 순차 로딩 복원

## 추후 개선 가능 사항

1. **이미지 프리로딩**: 앱 시작 시 자주 사용하는 아이템 이미지 미리 로딩
2. **WebP 포맷**: 이미지 크기 축소로 다운로드 속도 향상 (서버 측 지원 필요)
3. **Placeholder 이미지**: 로딩 중 스켈레톤 UI 표시
