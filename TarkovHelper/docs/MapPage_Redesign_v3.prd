# PRD: MapTrackerPage UI/UX 전면 재설계 v3

## 문서 정보
| 항목 | 내용 |
|------|------|
| 버전 | 3.0 |
| 작성일 | 2024-12-15 |
| 상태 | Draft |
| 작성자 | Claude (AI Assistant) |
| 이전 버전 | MapPage_Redesign_v2.md |

---

## 1. 개요

### 1.1 배경
현재 MapTrackerPage는 v2 개선(Settings Panel, Clustering, Enhanced Tooltip)을 거쳤으나, 여전히 다음 문제가 존재:
- 상단바 과밀로 핵심 조작(Start/Stop)이 묻힘
- 레이어 필터 상태가 한눈에 보이지 않음
- 마커 선택 후 상세 정보 확인 흐름이 약함
- 색상 의존도가 높아 색각 이상 사용자 접근성 저하

### 1.2 목표
1. **정보 밀도 최적화**: 맵 영역 최대화, 컨트롤 영역 최소화
2. **레이어 상태 가시성**: 현재 켜진 레이어를 항상 확인 가능
3. **선택→상세 흐름 강화**: 마커 클릭 시 풍부한 컨텍스트 제공
4. **접근성 개선**: 색상+모양+패턴 조합으로 마커 구분
5. **숙련자 효율**: 단축키로 모든 핵심 조작 가능

### 1.3 범위
- **포함**: MapTrackerPage 레이아웃, 컴포넌트, 상호작용, 단축키
- **제외**: 다른 페이지(QuestListPage, ItemsPage 등), 백엔드 로직

---

## 2. 현재 상태 분석

### 2.1 현재 레이아웃 구조
```
┌─────────────────────────────────────────────────────────────┐
│ [Map▾] [Zoom+/-] [Reset] [Fit] [Start] [Auto] [⚙]         │ ← 상단 툴바 (과밀)
├──────┬──────────────────────────────────────────────────────┤
│Quest │                                                      │
│Drawer│              MAP CANVAS                              │
│(40px)│         + Settings Panel (오버레이)                  │
│      │         + Tooltip (오버레이)                         │
└──────┴──────────────────────────────────────────────────────┘
```

### 2.2 문제점 상세

| ID | 문제 | 심각도 | 영향 |
|----|------|--------|------|
| P1 | 상단바에 12+개 컨트롤 밀집 | High | 신규 사용자 학습 곡선 증가 |
| P2 | Settings Panel 열어야 레이어 상태 확인 | High | 조작 단계 증가 (2클릭→1클릭) |
| P3 | 마커 클릭 시 상세 정보 부족 | Medium | 관련 퀘스트/조건 확인 불가 |
| P4 | 클러스터 클릭 시 내부 확인 어려움 | Medium | 원하는 마커 선택 실패 |
| P5 | 색상만으로 마커 타입 구분 | Medium | 색각 이상 사용자 8% 접근성 저하 |
| P6 | Quest Drawer 40px 공간 낭비 | Low | 맵 영역 감소 |
| P7 | 트래킹 상태 시각적 구분 약함 | Low | ON/OFF 혼동 가능 |

---

## 3. 제안 레이아웃

### 3.1 선정안: "Floating Toolbar + Context Sidebar"

```
┌─────────────────────────────────────────────────────────────────┐
│  [Map▾] ──────────── MAP TITLE ──────────────── [?] [⚙] [─]   │  Header (48px)
├────────┬────────────────────────────────────────────────┬───────┤
│ LAYER  │                                                │CONTEXT│
│ CHIPS  │              MAP CANVAS                        │ PANEL │
│ (80px) │                                                │(280px)│
│        │         ┌────────────────────┐                 │       │
│ [👹ON] │         │ 🎯 + − 🔄 ⟲ ◎    │ Floating Bar    │[Search]│
│ [🚪ON] │         └────────────────────┘                 │[Filter]│
│ [🚇OFF]│                                                │[Detail]│
│ [📍ON] │                                                │       │
│        │                                                │  [◀]  │
├────────┴────────────────────────────────────────────────┴───────┤
│ [▶ Start] [Auto] │ 🧭 X:-123 Z:456 │ Floor: 1F ▾ │ 👁 42/67   │  Status Bar (36px)
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 영역 정의

| 영역 | 크기 | 고정/가변 | 역할 |
|------|------|-----------|------|
| Header | 48px 높이 | 고정 | 맵 선택, 앱 컨트롤 |
| Layer Chips | 80px 너비 | 고정 | 레이어 ON/OFF 상태 표시 및 토글 |
| Map Canvas | 나머지 | 가변 | 지도 표시, Floating Bar 포함 |
| Context Panel | 280px 너비 | 접기 가능 (0px) | 검색, 필터, 선택 마커 상세 |
| Status Bar | 36px 높이 | 고정 | 트래킹 컨트롤, 좌표, 층 선택 |

### 3.3 대안

#### 대안 A: Bottom Dock
- 모든 컨트롤을 하단 도킹 패널로 통합
- 장점: 수직 공간 최대화
- 단점: 맵 하단 가림, 마커 클릭 충돌 가능

#### 대안 B: Split View + Mini-map
- 우측에 고정 상세 패널 + 미니맵
- 장점: 전체 조망 항상 가능
- 단점: 가로 공간 많이 사용

---

## 4. 상세 설계

### 4.1 Header (48px)

#### 4.1.1 구성 요소

| 요소 | 타입 | 기능 | 기존 매핑 |
|------|------|------|-----------|
| Map Selector | ComboBox | 맵 선택 | `CmbMapSelector` |
| Map Title | TextBlock | 현재 맵 이름 (읽기 전용) | 신규 |
| Help Button | Button | 단축키/사용법 모달 | 신규 |
| Settings Button | Button | Settings Panel 토글 | `BtnToggleSettings` |
| Window Controls | Buttons | 최소화/닫기 | 기존 윈도우 크롬 |

#### 4.1.2 XAML 구조
```xml
<Grid x:Name="HeaderGrid" Height="48" Background="{StaticResource BackgroundDarkBrush}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>    <!-- Map Selector: 150px -->
        <ColumnDefinition Width="*"/>       <!-- Title: flexible -->
        <ColumnDefinition Width="Auto"/>    <!-- Buttons: ~100px -->
    </Grid.ColumnDefinitions>

    <ComboBox x:Name="CmbMapSelector" Grid.Column="0" Width="150" Margin="12,0"/>

    <TextBlock x:Name="TxtMapTitle" Grid.Column="1"
               Text="Woods" FontSize="16" FontWeight="SemiBold"
               HorizontalAlignment="Center" VerticalAlignment="Center"/>

    <StackPanel Grid.Column="2" Orientation="Horizontal" Margin="0,0,8,0">
        <Button x:Name="BtnHelp" Content="?" Width="32" ToolTip="Help (F1)"/>
        <Button x:Name="BtnSettings" Content="⚙" Width="32" ToolTip="Settings (,)"/>
    </StackPanel>
</Grid>
```

---

### 4.2 Layer Chips (좌측 80px)

#### 4.2.1 목적
- 레이어 ON/OFF 상태를 **항상** 화면에 표시
- 원클릭으로 레이어 토글
- 색각 이상 사용자도 상태 구분 가능

#### 4.2.2 Chip 목록

| ID | 이름 | 아이콘 | 색상 | 단축키 |
|----|------|--------|------|--------|
| L1 | Boss | 💀 | #E53935 (빨강) | 1 |
| L2 | PMC Extract | ▲ | #43A047 (초록) | 2 |
| L3 | Scav Extract | △ | #81C784 (연초록) | 3 |
| L4 | Shared Extract | ◆ | #26A69A (청록) | 4 |
| L5 | Transit | ■ | #1E88E5 (파랑) | 5 |
| L6 | Spawn | ● | #FDD835 (노랑) | 6 |
| L7 | Lever/Keys | 🔧 | #9E9E9E (회색) | 7 |

#### 4.2.3 상태 시각화

| 상태 | 배경 | 테두리 | 아이콘 투명도 | 라벨 |
|------|------|--------|---------------|------|
| ON | 색상 20% | 색상 100%, 2px 실선 | 100% | "ON" |
| OFF | 투명 | 색상 50%, 1px 점선 | 50% | "OFF" |
| Hover | 색상 30% | 색상 100%, 2px 실선 | 100% | - |

#### 4.2.4 XAML 구조
```xml
<ItemsControl x:Name="LayerChipsPanel" Width="80" Background="{StaticResource BackgroundBrush}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <ToggleButton Style="{StaticResource LayerChipStyle}"
                          IsChecked="{Binding IsEnabled, Mode=TwoWay}"
                          Command="{Binding ToggleCommand}"
                          ToolTip="{Binding TooltipText}">
                <StackPanel HorizontalAlignment="Center" Margin="4,8">
                    <TextBlock Text="{Binding Icon}" FontSize="20" HorizontalAlignment="Center"/>
                    <TextBlock Text="{Binding ShortName}" FontSize="10" HorizontalAlignment="Center"/>
                    <TextBlock Text="{Binding StatusText}" FontSize="8" Opacity="0.7" HorizontalAlignment="Center"/>
                </StackPanel>
            </ToggleButton>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

#### 4.2.5 색각 이상 대응
- **모양 차별화**: 각 타입별 고유 모양 (삼각형, 사각형, 원형 등)
- **패턴 차별화**: ON=실선, OFF=점선
- **텍스트 라벨**: "ON"/"OFF" 명시
- **크기 차별화**: Boss/Extract는 다른 마커보다 10% 크게

---

### 4.3 Floating Bar (맵 위 오버레이)

#### 4.3.1 목적
- 자주 사용하는 뷰 컨트롤을 맵 위에 배치
- 맵 영역을 최대화하면서도 접근성 유지

#### 4.3.2 버튼 구성

| 아이콘 | 기능 | 기존 매핑 | 단축키 |
|--------|------|-----------|--------|
| 🎯 | Pan to Player | 신규 | P |
| + | Zoom In | `BtnZoomIn` | + / = |
| − | Zoom Out | `BtnZoomOut` | - |
| 🔄 | Reset View | `BtnResetView` | R |
| ⟲ | Fit Map | `BtnFitMap` | F |
| ◎ | Lock View (토글) | 신규 | L |

#### 4.3.3 위치 및 동작
- **기본 위치**: 맵 캔버스 하단 중앙, 하단에서 60px 위
- **드래그 이동**: Alt+드래그로 위치 조정 가능 (위치 저장됨)
- **자동 숨김**: 없음 (항상 표시)
- **투명도**: 기본 80%, 호버 시 100%

#### 4.3.4 XAML 구조
```xml
<Border x:Name="FloatingToolbar"
        HorizontalAlignment="Center" VerticalAlignment="Bottom"
        Margin="0,0,0,60"
        Background="#CC1E1E1E" CornerRadius="8" Padding="8,4"
        MouseLeftButtonDown="FloatingToolbar_MouseDown">
    <StackPanel Orientation="Horizontal">
        <Button Content="🎯" ToolTip="Pan to Player (P)" Click="BtnPanToPlayer_Click"/>
        <Separator Style="{StaticResource VerticalSeparator}"/>
        <Button Content="+" ToolTip="Zoom In (+)" Click="BtnZoomIn_Click"/>
        <Button Content="−" ToolTip="Zoom Out (-)" Click="BtnZoomOut_Click"/>
        <Separator Style="{StaticResource VerticalSeparator}"/>
        <Button Content="🔄" ToolTip="Reset View (R)" Click="BtnResetView_Click"/>
        <Button Content="⟲" ToolTip="Fit Map (F)" Click="BtnFitMap_Click"/>
        <Separator Style="{StaticResource VerticalSeparator}"/>
        <ToggleButton x:Name="BtnLockView" Content="◎" ToolTip="Lock View (L)"/>
    </StackPanel>
</Border>
```

---

### 4.4 Context Panel (우측 280px)

#### 4.4.1 목적
- 마커 미선택 시: 검색 및 상세 필터
- 마커 선택 시: 상세 정보 및 액션
- 검색 중: 결과 목록 표시

#### 4.4.2 모드 정의

| 모드 | 트리거 | 내용 |
|------|--------|------|
| Default | 마커 미선택 | 검색바, 레이어 상세, 디스플레이 설정 |
| Selected | 마커/클러스터 클릭 | 마커 상세, 조건, 관련 퀘스트, 액션 버튼 |
| Search | 검색어 입력 | 매칭 마커 리스트, 클릭 시 해당 위치로 이동 |

#### 4.4.3 Default 모드 레이아웃
```
┌─────────────────────────────┐
│ 🔍 Search markers...       │  검색바
├─────────────────────────────┤
│ ▼ LAYERS                   │  레이어 상세 (체크박스 + 카운트)
│   ☑ Boss Spawns (3)        │
│   ☑ PMC Extract (7)        │
│   ☑ Scav Extract (4)       │
│   ...                      │
├─────────────────────────────┤
│ ▼ DISPLAY                  │  디스플레이 설정
│   Marker Size: ────●────   │
│   Labels: ☑ Show           │
│   Clustering: ☑ Enabled    │
├─────────────────────────────┤
│ ▼ PRESETS                  │  레이어 프리셋
│   [PvP Mode] [Quest Mode]  │
│   [All ON]  [All OFF]      │
└─────────────────────────────┘
```

#### 4.4.4 Selected 모드 레이아웃
```
┌─────────────────────────────┐
│ ← Back              ZB-014 │  헤더 (뒤로가기 + 이름)
├─────────────────────────────┤
│        🚪                   │  아이콘 (크게)
│   PMC Extraction           │  타입
│                            │
│ Coordinates:               │  좌표
│ X: -123.4  Z: 456.7  [Copy]│
│                            │
│ Floor: Underground         │  층 정보
├─────────────────────────────┤
│ CONDITIONS                 │  조건 (있는 경우)
│ ⚡ Power must be ON        │
│ 💰 7000₽ required          │
├─────────────────────────────┤
│ RELATED QUESTS (2)         │  관련 퀘스트
│ • Delivery from the Past   │
│ • The Cult - Part 1        │
├─────────────────────────────┤
│ [Go to Floor] [Pin] [Hide] │  액션 버튼
└─────────────────────────────┘
```

#### 4.4.5 Search 모드 레이아웃
```
┌─────────────────────────────┐
│ 🔍 "ZB-01"            [✕]  │  검색바 (입력 중)
├─────────────────────────────┤
│ RESULTS (3)                │  결과 헤더
├─────────────────────────────┤
│ 🚪 ZB-011 (PMC Extract)    │  결과 아이템
│    Underground • X:-45     │
├─────────────────────────────┤
│ 🚪 ZB-012 (PMC Extract)    │
│    1F • X:-120             │
├─────────────────────────────┤
│ 🚪 ZB-014 (PMC Extract)    │
│    Underground • X:-123    │
└─────────────────────────────┘
```

#### 4.4.6 접기/펴기
- **접기 버튼**: 패널 하단 좌측 `◀` 버튼
- **접힌 상태**: 너비 0px, 토글 버튼만 표시 (24px)
- **펼친 상태**: 너비 280px
- **애니메이션**: 200ms ease-out
- **저장**: 상태를 UserSettings에 저장

---

### 4.5 Status Bar (하단 36px)

#### 4.5.1 목적
- 플레이어 트래킹 핵심 컨트롤
- 실시간 좌표 표시
- 층 선택
- 마커 카운트

#### 4.5.2 구성 요소

| 섹션 | 너비 | 내용 | 기존 매핑 |
|------|------|------|-----------|
| Tracking | 180px | Start/Stop + Auto 토글 | `BtnStartTracking`, `ChkAutoFloor` |
| Coordinates | flexible | X: -123.4 Z: 456.7 | `PlayerCoordsText` |
| Floor | 100px | Dropdown (1F, 2F, B1...) | Floor selector in Settings |
| Count | 80px | 👁 42/67 (보이는/전체) | `MarkerCountText` |
| Toggle | 40px | Context Panel 접기 버튼 | 신규 |

#### 4.5.3 Start 버튼 상태

| 상태 | 텍스트 | 배경색 | 아이콘 | 애니메이션 |
|------|--------|--------|--------|------------|
| Idle | "Start" | #333333 | ▶ | 없음 |
| Active | "Stop" | #2E7D32 | ■ | 테두리 펄스 |
| Error | "Retry" | #C62828 | ⚠ | 없음 |
| Connecting | "..." | #F57C00 | ◌ | 로딩 스피너 |

#### 4.5.4 XAML 구조
```xml
<Grid x:Name="StatusBar" Height="36" Background="{StaticResource BackgroundDarkBrush}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="180"/>  <!-- Tracking -->
        <ColumnDefinition Width="Auto"/> <!-- Separator -->
        <ColumnDefinition Width="*"/>    <!-- Coordinates -->
        <ColumnDefinition Width="Auto"/> <!-- Separator -->
        <ColumnDefinition Width="100"/>  <!-- Floor -->
        <ColumnDefinition Width="Auto"/> <!-- Separator -->
        <ColumnDefinition Width="80"/>   <!-- Count -->
        <ColumnDefinition Width="40"/>   <!-- Toggle -->
    </Grid.ColumnDefinitions>

    <!-- Tracking Controls -->
    <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="8,0">
        <Button x:Name="BtnStartTracking" Content="▶ Start" Width="80"/>
        <ToggleButton x:Name="BtnAutoFollow" Content="Auto" Width="60" Margin="8,0,0,0"/>
    </StackPanel>

    <!-- Coordinates -->
    <StackPanel Grid.Column="2" Orientation="Horizontal" HorizontalAlignment="Center">
        <TextBlock Text="🧭" Margin="0,0,8,0"/>
        <TextBlock x:Name="TxtPlayerCoords" Text="X: -123.4  Z: 456.7"/>
    </StackPanel>

    <!-- Floor -->
    <ComboBox x:Name="CmbFloor" Grid.Column="4" Width="90"/>

    <!-- Count -->
    <StackPanel Grid.Column="6" Orientation="Horizontal" HorizontalAlignment="Center">
        <TextBlock Text="👁"/>
        <TextBlock x:Name="TxtMarkerCount" Text="42/67" Margin="4,0,0,0"/>
    </StackPanel>

    <!-- Toggle -->
    <Button x:Name="BtnToggleContextPanel" Grid.Column="7" Content="◀"/>
</Grid>
```

---

### 4.6 마커 시스템 개선

#### 4.6.1 마커 디자인 (색각 이상 대응)

| 타입 | 색상 | 모양 | 패턴 | 크기 |
|------|------|------|------|------|
| Boss | #E53935 | 💀 해골 | 실선 | 1.2x |
| Raider | #FF7043 | 💀 해골 (작게) | 점선 | 1.0x |
| PMC Extract | #43A047 | ▲ 삼각형 | 실선 | 1.1x |
| Scav Extract | #81C784 | △ 빈 삼각형 | 점선 | 1.0x |
| Shared Extract | #26A69A | ◆ 다이아 | 실선 | 1.1x |
| Transit | #1E88E5 | ■ 사각형 | 실선 | 1.0x |
| PMC Spawn | #FFEE58 | ● 원형 | 실선 | 0.9x |
| Scav Spawn | #FFF59D | ○ 빈 원형 | 점선 | 0.9x |
| Lever | #9E9E9E | ⚙ 기어 | 실선 | 0.8x |
| Keys | #FF9800 | 🔑 열쇠 | 실선 | 0.8x |

#### 4.6.2 동적 클러스터링

```csharp
// 줌 레벨 기반 동적 클러스터 크기
private double GetDynamicClusterSize() => _zoomLevel switch
{
    < 0.3 => 100.0,  // 매우 축소 시 큰 클러스터
    < 0.5 => 80.0,
    < 0.7 => 60.0,
    < 1.0 => 40.0,
    _ => 25.0        // 확대 시 작은 클러스터
};
```

#### 4.6.3 클러스터 Spiderfy

클러스터 클릭 시 내부 마커를 방사형으로 펼침:

```
클릭 전:          클릭 후 (Spiderfy):
                       ●
   [5]              ╱  │  ╲
                   ●   ●   ●
                       │
                       ●
```

- **애니메이션**: 300ms ease-out
- **취소**: 빈 공간 클릭 또는 Esc
- **최대 표시**: 8개 (초과 시 "더 보기" 표시)

#### 4.6.4 LOD (Level of Detail)

| 줌 레벨 | 마커 표시 | 라벨 | 조건 아이콘 |
|---------|-----------|------|-------------|
| < 30% | 클러스터만 | 숨김 | 숨김 |
| 30-60% | 아이콘 (작게) | 짧은 이름 | 숨김 |
| 60-100% | 아이콘 (기본) | 전체 이름 | 표시 |
| > 100% | 아이콘 (크게) | 전체 이름 + 좌표 | 표시 |

---

### 4.7 단축키 설계

#### 4.7.1 전역 단축키

| 단축키 | 액션 | 카테고리 |
|--------|------|----------|
| `Space` | Start/Stop Tracking 토글 | 트래킹 |
| `A` | Auto Follow 토글 | 트래킹 |
| `P` | Pan to Player | 뷰 |
| `F` | Fit Map | 뷰 |
| `R` | Reset View | 뷰 |
| `L` | Lock View 토글 | 뷰 |
| `+` / `=` | Zoom In | 뷰 |
| `-` | Zoom Out | 뷰 |
| `1` - `7` | Layer 1-7 토글 | 레이어 |
| `Ctrl+1` - `Ctrl+7` | Layer Solo (해당 레이어만) | 레이어 |
| `0` | All Layers ON | 레이어 |
| `9` | All Layers OFF | 레이어 |
| `Tab` | Context Panel 토글 | UI |
| `/` 또는 `Ctrl+F` | 검색 포커스 | UI |
| `Esc` | 선택 해제 / 검색 취소 / 패널 닫기 | UI |
| `F1` | Help 모달 | UI |
| `,` | Settings Panel 토글 | UI |

#### 4.7.2 선택 상태 단축키

| 단축키 | 액션 | 조건 |
|--------|------|------|
| `←` / `→` | 이전/다음 마커 선택 | 마커 선택됨 |
| `Enter` | 선택 마커로 줌 인 | 마커 선택됨 |
| `C` | 좌표 복사 | 마커 선택됨 |
| `G` | Go to Floor | 마커 선택됨, 다른 층일 때 |
| `H` | Hide Marker | 마커 선택됨 |
| `Delete` | 핀 삭제 | 사용자 핀 선택됨 |

#### 4.7.3 마우스 조작

| 조작 | 액션 |
|------|------|
| 좌클릭 (마커) | 마커 선택 |
| 좌클릭 (클러스터) | Spiderfy 펼침 |
| 좌클릭 (빈 공간) | 선택 해제 |
| 우클릭 (마커) | 컨텍스트 메뉴 |
| 우클릭 (빈 공간) | 커스텀 핀 추가 |
| 중클릭 드래그 | 팬 |
| 스크롤 | 줌 |
| Ctrl+스크롤 | 빠른 줌 (3배속) |
| 더블클릭 (마커) | 줌 인 + 선택 |

---

### 4.8 상태 관리

#### 4.8.1 앱 상태

| 상태 | 조건 | UI 변화 |
|------|------|---------|
| Loading | 맵 SVG 로딩 중 | 중앙 스피너 + "Loading {MapName}..." |
| Ready | 로딩 완료 | 정상 표시 |
| Empty | 필터 결과 마커 0개 | 중앙 메시지 + [Reset Filters] 버튼 |
| Error | 로드 실패 | 에러 아이콘 + 메시지 + [Retry] 버튼 |

#### 4.8.2 트래킹 상태

| 상태 | 조건 | UI 변화 |
|------|------|---------|
| Idle | 트래킹 미시작 | Start 버튼 회색 |
| Connecting | 연결 시도 중 | Start 버튼 주황, 스피너 |
| Active | 정상 수신 중 | Stop 버튼 초록, 펄스 애니메이션 |
| Lost | 3초간 수신 없음 | Stop 버튼 유지, 좌표 영역 주황 경고 |
| Error | 연결 실패 | Retry 버튼 빨강 |

#### 4.8.3 선택 상태

| 상태 | 조건 | UI 변화 |
|------|------|---------|
| None | 마커 미선택 | Context Panel Default 모드 |
| Single | 단일 마커 선택 | Context Panel Selected 모드, 마커 하이라이트 |
| Cluster | 클러스터 선택 | Spiderfy 펼침, 내부 마커 선택 가능 |
| Search | 검색 중 | Context Panel Search 모드, 매칭 마커 하이라이트 |

---

## 5. 데이터 모델

### 5.1 LayerChip 모델

```csharp
public class LayerChipViewModel : INotifyPropertyChanged
{
    public string Id { get; set; }           // "boss", "pmc_extract", ...
    public string Icon { get; set; }         // "💀", "▲", ...
    public string ShortName { get; set; }    // "Boss", "PMC", ...
    public string FullName { get; set; }     // "Boss Spawns", "PMC Extractions", ...
    public Color AccentColor { get; set; }   // #E53935, #43A047, ...
    public bool IsEnabled { get; set; }      // ON/OFF
    public int Count { get; set; }           // 현재 맵의 해당 타입 마커 수
    public string Shortcut { get; set; }     // "1", "2", ...

    public string StatusText => IsEnabled ? "ON" : "OFF";
    public string TooltipText => $"{FullName} ({Count})\nShortcut: {Shortcut}";
}
```

### 5.2 MarkerDetail 모델

```csharp
public class MarkerDetailViewModel
{
    public MapMarker Marker { get; set; }
    public string LocalizedName { get; set; }
    public string TypeName { get; set; }
    public string FloorName { get; set; }
    public List<string> Conditions { get; set; }      // 탈출 조건
    public List<QuestInfo> RelatedQuests { get; set; } // 관련 퀘스트
    public bool CanGoToFloor { get; set; }            // 층 이동 가능 여부
}
```

### 5.3 설정 저장

```csharp
// UserSettings 키
public static class MapSettingsKeys
{
    public const string ContextPanelCollapsed = "map.contextPanel.collapsed";
    public const string FloatingBarPosition = "map.floatingBar.position";  // "x,y"
    public const string LayerPreset = "map.layers.preset";                  // JSON
    public const string MarkerScale = "map.display.markerScale";
    public const string ShowLabels = "map.display.showLabels";
    public const string ClusteringEnabled = "map.display.clustering";
    public const string LastSelectedMap = "map.lastSelectedMap";
}
```

---

## 6. 마이그레이션 가이드

### 6.1 기존 컴포넌트 매핑

| 기존 | 신규 위치 | 변경 사항 |
|------|-----------|-----------|
| `CmbMapSelector` | Header | 위치만 이동 |
| `BtnToggleSettings` | Header | 위치만 이동 |
| `BtnZoomIn/Out` | Floating Bar | 위치 이동 |
| `BtnResetView` | Floating Bar | 위치 이동 |
| `BtnFitMap` | Floating Bar | 위치 이동 |
| `BtnStartTracking` | Status Bar | 위치 이동, 상태 스타일 강화 |
| `ChkAutoFloor` | Status Bar (Auto 버튼) | ToggleButton으로 변경 |
| `ChkShowBoss` 등 | Layer Chips + Context Panel | Chip + 상세 체크박스로 분리 |
| Floor 선택 (Settings) | Status Bar | ComboBox로 통합 |
| `MarkerCountText` | Status Bar | 위치 이동 |
| Quest Drawer | **제거** | Context Panel로 통합 |
| Settings Panel | 유지 (고급 설정용) | 기본 설정은 Context Panel로 |

### 6.2 삭제 대상

| 컴포넌트 | 이유 |
|----------|------|
| `QuestDrawerColumn` (40px) | 미사용, Context Panel로 통합 |
| `BtnToggleSidebar` | 기능 중복 |

---

## 7. 구현 계획

### 7.1 Phase 1: 레이아웃 재구성 (2-3일)

- [ ] Header 단순화 (맵 선택기, 제목, 버튼)
- [ ] Layer Chips 컴포넌트 구현
- [ ] Status Bar 구현 (트래킹 컨트롤 이동)
- [ ] Floating Bar 구현 (줌/리셋 버튼 이동)
- [ ] Quest Drawer 제거

### 7.2 Phase 2: Context Panel (2-3일)

- [ ] Context Panel 프레임 (접기/펴기)
- [ ] Default 모드 (검색, 레이어 상세, 디스플레이)
- [ ] Selected 모드 (마커 상세, 조건, 퀘스트)
- [ ] Search 모드 (검색 결과 리스트)
- [ ] 마커 클릭 → Selected 모드 전환

### 7.3 Phase 3: 마커 시스템 (2-3일)

- [ ] 마커 아이콘 리디자인 (모양+패턴)
- [ ] 동적 클러스터링 구현
- [ ] Spiderfy 애니메이션
- [ ] LOD 시스템

### 7.4 Phase 4: 상호작용 (1-2일)

- [ ] 단축키 전체 등록
- [ ] 마우스 조작 개선
- [ ] 상태 피드백 강화 (Start 버튼 애니메이션)
- [ ] 에러/로딩/Empty 상태 UI

### 7.5 Phase 5: 마무리 (1-2일)

- [ ] 설정 저장/복원
- [ ] 레이어 프리셋
- [ ] 접근성 테스트 (색각 이상)
- [ ] 성능 최적화

---

## 8. 성공 지표

| 지표 | 현재 | 목표 | 측정 방법 |
|------|------|------|-----------|
| 레이어 토글 클릭 수 | 2 (패널 열기 + 체크) | 1 (Chip 클릭) | 로그 분석 |
| 마커 상세 확인 시간 | N/A (없음) | < 1초 | 사용자 테스트 |
| 색각 이상 사용자 마커 구분율 | ~60% (색상 의존) | > 95% | 접근성 테스트 |
| 숙련자 조작 속도 | 마우스 의존 | 키보드만으로 90% 조작 | 사용자 테스트 |

---

## 9. 리스크 및 대응

| 리스크 | 확률 | 영향 | 대응 |
|--------|------|------|------|
| 레이아웃 변경으로 기존 사용자 혼란 | 중 | 중 | 첫 실행 시 가이드 툴팁 표시 |
| Spiderfy 애니메이션 성능 이슈 | 낮 | 낮 | 마커 8개 제한, 애니메이션 끄기 옵션 |
| Context Panel이 작은 모니터에서 비좁음 | 중 | 낮 | 최소 너비 240px, 접기 가능 |
| 단축키 충돌 (다른 앱과) | 낮 | 낮 | 커스터마이징 옵션 추후 추가 |

---

## 10. 참고 자료

- 이전 버전: `docs/MapPage_Redesign_v2.md`
- 현재 구현: `Pages/MapTrackerPage.xaml`, `Pages/MapTrackerPage.xaml.cs`
- 마커 모델: `Models/MapMarker.cs`
- 설정 서비스: `Services/SettingsService.cs`
