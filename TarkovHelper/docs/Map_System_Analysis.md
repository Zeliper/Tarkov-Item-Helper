# TarkovHelper Map 시스템 분석: Legacy_Map vs MapTrackerPage

## 1. 개요

TarkovHelper에는 두 가지 맵 시스템이 있습니다:
- **Legacy_Map**: 이전에 동작하던 시스템 (Pages/Legacy_Map/, Services/Legacy_Map/, Models/Legacy_Map/)
- **MapTrackerPage**: 현재 Map 탭 (맵 리스트가 로드되지 않는 문제)

---

## 2. 아키텍처 비교

### Legacy_Map 구조

```
LegacyMapPage.xaml.cs
    │
    ├── LegacyMapTrackerService (싱글톤)
    │       │
    │       ├── LoadSettings()
    │       │       └── map_configs.json → MapTrackerSettings.Maps
    │       │
    │       ├── MapCoordinateTransformer
    │       │       └── UpdateMaps(settings.Maps)
    │       │
    │       └── ScreenshotWatcherService
    │
    └── PopulateMapComboBox()
            └── _trackerService.GetAllMapKeys() → ComboBoxItem (Content + Tag)
```

### 현재 MapTrackerPage 구조

```
MapTrackerPage.xaml.cs
    │
    ├── LoadMapConfigs() (생성자에서 직접 호출)
    │       └── map_configs.json → _mapConfigs
    │
    ├── MapTrackerService (싱글톤, Legacy와 다른 서비스)
    │
    └── MapSelector.Items.Add(MapConfig 객체)
            └── ItemTemplate: {Binding DisplayName}
```

---

## 3. 주요 차이점

### 3.1 맵 설정 로딩 방식

| 항목 | Legacy_Map | MapTrackerPage |
|------|-----------|----------------|
| 로딩 시점 | 서비스 생성자에서 | 페이지 생성자에서 |
| 로딩 메서드 | `LegacyMapTrackerService.LoadSettings()` | `LoadMapConfigs()` |
| JSON 옵션 | `JsonNamingPolicy.CamelCase` | `PropertyNameCaseInsensitive = true` |
| 저장 위치 | `MapTrackerSettings.Maps` | `_mapConfigs` |

### 3.2 MapConfig 클래스

#### TarkovHelper.Models.MapConfig (현재)
```csharp
public class MapConfig
{
    public string Key { get; set; }
    public string DisplayName { get; set; }
    public string SvgFileName { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public List<string>? Aliases { get; set; }
    public double[]? CalibratedTransform { get; set; }
    public List<MapFloorConfig>? Floors { get; set; }
    public double[]? PlayerMarkerTransform { get; set; }
    public double[]? SvgBounds { get; set; }

    // 좌표 변환 메서드
    public (double, double) GameToScreen(double gameX, double gameZ);
    public (double, double) ScreenToGame(double screenX, double screenY);
    public (double, double) GameToScreenForPlayer(double gameX, double gameZ);
    public (double, double) ScreenToGameForPlayer(double screenX, double screenY);
}
```

#### TarkovHelper.Models.Legacy_Map.MapConfig
```csharp
public sealed class MapConfig
{
    public string Key { get; set; }
    public string DisplayName { get; set; }
    public string ImagePath { get; set; }
    [JsonPropertyName("svgFileName")]
    public string? SvgFileName { get; set; }

    // 좌표 범위 (구 방식)
    public double WorldMinX { get; set; }
    public double WorldMaxX { get; set; }
    public double WorldMinY { get; set; }
    public double WorldMaxY { get; set; }

    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public bool InvertY { get; set; } = true;
    public bool InvertX { get; set; } = false;
    public double OffsetX { get; set; } = 0;
    public double OffsetY { get; set; } = 0;

    public List<string>? Aliases { get; set; }
    public double[]? Transform { get; set; }
    public int CoordinateRotation { get; set; } = 180;
    public double[]? SvgBounds { get; set; }
    public double[]? PlayerMarkerTransform { get; set; }
    public double MarkerScale { get; set; } = 1.0;
    public List<CalibrationPoint>? CalibrationPoints { get; set; }
    public double[]? CalibratedTransform { get; set; }
    public List<MapFloorConfig>? Floors { get; set; }
}
```

### 3.3 콤보박스 채우기 방식

#### Legacy_Map (PopulateMapComboBox)
```csharp
private void PopulateMapComboBox()
{
    CmbMapSelect.Items.Clear();
    foreach (var mapKey in _trackerService.GetAllMapKeys())
    {
        var config = _trackerService.GetMapConfig(mapKey);
        CmbMapSelect.Items.Add(new ComboBoxItem
        {
            Content = config?.DisplayName ?? mapKey,  // 문자열 표시
            Tag = mapKey  // 키 저장
        });
    }
    if (CmbMapSelect.Items.Count > 0)
        CmbMapSelect.SelectedIndex = 0;
}
```

#### MapTrackerPage (LoadMapConfigs)
```csharp
private void LoadMapConfigs()
{
    // ...
    if (_mapConfigs != null)
    {
        foreach (var map in _mapConfigs.Maps)
        {
            MapSelector.Items.Add(map);  // MapConfig 객체 직접 추가
        }
    }
}
```

XAML의 ItemTemplate:
```xml
<ComboBox.ItemTemplate>
    <DataTemplate>
        <TextBlock Text="{Binding DisplayName}"/>  <!-- DisplayName 바인딩 -->
    </DataTemplate>
</ComboBox.ItemTemplate>
```

---

## 4. 문제 원인 분석

### 가능한 원인 1: JSON 파싱 실패
- `PropertyNameCaseInsensitive = true`가 제대로 동작하지 않을 수 있음
- Legacy는 `JsonNamingPolicy.CamelCase` 사용

### 가능한 원인 2: 파일 경로 문제
- 생성자에서 호출 시 `AppDomain.CurrentDomain.BaseDirectory`가 다를 수 있음

### 가능한 원인 3: 타이밍 문제
- 생성자에서 호출 vs Loaded 이벤트에서 호출

---

## 5. 해결 방안

### 방안 A: Legacy_Map 방식 적용 (권장)

MapTrackerPage를 Legacy_Map과 동일한 패턴으로 수정:

```csharp
// 1. MapTrackerPage.xaml.cs - 맵 콤보박스 채우기 수정
private void PopulateMapComboBox()
{
    MapSelector.Items.Clear();

    if (_mapConfigs?.Maps == null) return;

    foreach (var map in _mapConfigs.Maps)
    {
        MapSelector.Items.Add(new ComboBoxItem
        {
            Content = map.DisplayName,
            Tag = map.Key
        });
    }

    if (MapSelector.Items.Count > 0)
        MapSelector.SelectedIndex = 0;
}

// 2. MapSelector_SelectionChanged 수정
private void MapSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (MapSelector.SelectedItem is ComboBoxItem item &&
        item.Tag is string mapKey &&
        _mapConfigs != null)
    {
        _currentMapConfig = _mapConfigs.Maps.FirstOrDefault(m => m.Key == mapKey);
        if (_currentMapConfig != null)
        {
            UpdateFloorSelector(_currentMapConfig);
            LoadMap(_currentMapConfig);
            // ...
        }
    }
}
```

```xml
<!-- 3. MapTrackerPage.xaml - ItemTemplate 제거 -->
<ComboBox x:Name="MapSelector" Width="140" Height="28" Margin="0,0,8,0"
          Background="{StaticResource BgL3}" Foreground="{StaticResource TextPrimary}"
          BorderBrush="{StaticResource BorderSubtle}"
          SelectionChanged="MapSelector_SelectionChanged">
    <!-- ItemTemplate 제거하면 ComboBoxItem.Content가 직접 표시됨 -->
</ComboBox>
```

### 방안 B: JSON 파싱 옵션 수정

```csharp
private void LoadMapConfigs()
{
    // Legacy와 동일한 옵션 사용
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    _mapConfigs = JsonSerializer.Deserialize<MapConfigList>(json, options);
}
```

### 방안 C: Loaded 이벤트로 이동

```csharp
public MapTrackerPage()
{
    InitializeComponent();
    // LoadMapConfigs() 제거
}

private async void MapTrackerPage_Loaded(object sender, RoutedEventArgs e)
{
    LoadMapConfigs();  // Loaded에서 호출

    if (MapSelector.Items.Count > 0)
    {
        MapSelector.SelectedIndex = 0;
    }

    // 나머지 초기화...
}
```

---

## 6. 디버그 로그 확인

현재 추가된 디버그 로그 (MapTrackerPage.xaml.cs):

```
[MapTrackerPage] Loading map configs from: {경로}
[MapTrackerPage] File exists: {true/false}
[MapTrackerPage] JSON length: {bytes} bytes
[MapTrackerPage] Parsed map count: {개수}
[MapTrackerPage] Adding map: {Key} - {DisplayName}
[MapTrackerPage] MapSelector.Items.Count: {개수}
```

**확인 방법:**
1. Visual Studio → View → Output (Ctrl+Alt+O)
2. "Show output from" → **Debug**
3. 앱 실행 후 Map 탭 이동
4. `[MapTrackerPage]` 로그 확인

---

## 7. 파일 목록

### Legacy_Map
```
Pages/Legacy_Map/
├── LegacyMapPage.xaml
└── LegacyMapPage.xaml.cs

Services/Legacy_Map/
├── LegacyMapTrackerService.cs
├── MapCoordinateTransformer.cs
├── MapCalibrationService.cs
├── ScreenshotWatcherService.cs
├── SvgStylePreprocessor.cs
└── ...

Models/Legacy_Map/
├── MapConfig.cs
├── MapFloorConfig.cs
├── MapTrackerSettings.cs
├── EftPosition.cs
├── ScreenPosition.cs
└── ...
```

### 현재 MapTrackerPage
```
Pages/
└── MapTrackerPage.xaml(.cs)

Services/
├── MapTrackerService.cs
├── SvgStylePreprocessor.cs
└── ...

Models/
├── MapConfig.cs
├── MapMarker.cs
├── QuestObjective.cs
└── ...
```

---

## 8. 권장 조치

1. **즉시**: 디버그 로그로 파싱 상태 확인
2. **단기**: Legacy_Map 방식으로 콤보박스 채우기 수정 (방안 A)
3. **장기**: Legacy_Map 서비스들을 MapTrackerPage로 통합하거나 완전히 분리

---

## 9. TarkovDBEditor와의 비교

TarkovDBEditor의 QuestObjectiveEditorWindow도 비슷한 구조를 사용:

```csharp
// TarkovDBEditor/Views/QuestObjectiveEditorWindow.xaml.cs
private void LoadMapConfigs()
{
    var configPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "Resources", "Data", "map_configs.json");

    // ...
    foreach (var map in _mapConfigs.Maps)
    {
        MapSelector.Items.Add(map.DisplayName);  // DisplayName 문자열만 추가
    }
}
```

TarkovDBEditor는 `DisplayName` 문자열만 추가하고, 선택 시 `FirstOrDefault`로 MapConfig를 찾습니다.
