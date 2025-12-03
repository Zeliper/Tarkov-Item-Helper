# Cross-Tab Navigation PRD (탭 간 링크 네비게이션)

## Overview
Items나 Quests 탭의 우측 상세 패널에서 아이템 이름 또는 퀘스트 이름을 클릭하면 해당 탭으로 이동하여 상세 정보를 표시하는 기능. 사용자가 연관 데이터 간 빠르게 이동할 수 있도록 하여 UX를 개선한다.

## Reference
- 관련 파일: `MainWindow.xaml.cs` - 탭 전환 로직
- 관련 페이지: `QuestListPage.xaml`, `ItemsPage.xaml`
- 탭 컨트롤: `TabQuests`, `TabItems`, `TabHideout` RadioButtons

---

## Features

### 1. Items Page → Quests Tab Navigation (아이템 → 퀘스트 이동)

#### 1.1 Trigger Location
- Items 탭 우측 상세 패널의 "Required for Quests" 섹션
- `QuestRequirementsList` 내 각 퀘스트 항목

#### 1.2 Current UI Structure
```xml
<ItemsControl x:Name="QuestRequirementsList">
    <DataTemplate>
        <StackPanel>
            <TextBlock Text="{Binding QuestName}" />  <!-- 클릭 대상 -->
            <TextBlock Text="{Binding TraderName}" />
        </StackPanel>
    </DataTemplate>
</ItemsControl>
```

#### 1.3 Expected Behavior
1. 사용자가 퀘스트 이름(QuestName)을 클릭
2. Quests 탭으로 자동 전환
3. 해당 퀘스트가 리스트에서 선택되고 상세 패널에 표시
4. 필요 시 검색/필터 초기화하여 퀘스트가 보이도록 처리

#### 1.4 UI Changes
- 퀘스트 이름에 하이퍼링크 스타일 적용 (underline on hover, cursor: hand)
- 클릭 가능함을 시각적으로 표시

### 2. Quests Page → Items Tab Navigation (퀘스트 → 아이템 이동)

#### 2.1 Trigger Location
- Quests 탭 우측 상세 패널의 "Required Items" 섹션
- `RequiredItemsList` 내 각 아이템 항목

#### 2.2 Current UI Structure
```xml
<ItemsControl x:Name="RequiredItemsList">
    <DataTemplate>
        <StackPanel Orientation="Horizontal">
            <Image Source="{Binding IconSource}" />
            <TextBlock Text="{Binding DisplayText}" />  <!-- 클릭 대상 -->
        </StackPanel>
    </DataTemplate>
</ItemsControl>
```

#### 2.3 Expected Behavior
1. 사용자가 아이템 이름(DisplayText) 또는 아이콘을 클릭
2. Items 탭으로 자동 전환
3. 해당 아이템이 리스트에서 선택되고 상세 패널에 표시
4. 필요 시 검색/필터 초기화하여 아이템이 보이도록 처리

#### 2.4 UI Changes
- 아이템 이름에 하이퍼링크 스타일 적용
- 아이템 행 전체 또는 이름 영역에 hover 효과

### 3. Visual Feedback (시각적 피드백)

#### 3.1 Link Style
```
기본 상태:
  - 텍스트 색상: TextPrimaryBrush
  - Cursor: Arrow

Hover 상태:
  - 텍스트 색상: AccentBrush
  - TextDecoration: Underline
  - Cursor: Hand
```

#### 3.2 Navigation Animation (선택사항)
- 탭 전환 시 부드러운 전환 효과
- 선택된 항목 하이라이트 애니메이션

---

## UI Layout Changes

### Items Page - Quest Requirements Section
```
+------------------------------------------------------------------+
| Required for Quests                                                |
+------------------------------------------------------------------+
|  +------------------------------------------------------------+  |
|  | [Quest Name - 클릭 가능 링크]              x5      [FIR]    |  |
|  | Prapor                                                      |  |
|  +------------------------------------------------------------+  |
|  | [Delivery from the Past]                   x1               |  |
|  | Prapor                         ↑ hover시 밑줄 + 색상 변경   |  |
|  +------------------------------------------------------------+  |
+------------------------------------------------------------------+
```

### Quests Page - Required Items Section
```
+------------------------------------------------------------------+
| Required Items                                                     |
+------------------------------------------------------------------+
|  +------------------------------------------------------------+  |
|  | [Icon] [Flash Drive - 클릭 가능 링크] x4    [FIR]          |  |
|  |                      ↑ hover시 밑줄 + 색상 변경             |  |
|  +------------------------------------------------------------+  |
|  | [Icon] [Bolts]                              x10             |  |
|  +------------------------------------------------------------+  |
+------------------------------------------------------------------+
```

---

## Technical Implementation

### 1. Navigation Service / Method

#### 1.1 MainWindow Navigation Methods
```csharp
// MainWindow.xaml.cs에 추가
public void NavigateToQuest(string questId)
{
    // 1. Quests 탭 선택
    TabQuests.IsChecked = true;

    // 2. QuestListPage에 선택 요청
    _questListPage?.SelectQuest(questId);
}

public void NavigateToItem(string itemNormalizedName)
{
    // 1. Items 탭 선택
    TabItems.IsChecked = true;

    // 2. ItemsPage에 선택 요청
    _itemsPage?.SelectItem(itemNormalizedName);
}
```

#### 1.2 QuestListPage Selection Method
```csharp
// QuestListPage.xaml.cs에 추가
public void SelectQuest(string questId)
{
    // 1. 필터 초기화 (해당 퀘스트가 보이도록)
    ResetFiltersForNavigation();

    // 2. 퀘스트 찾기
    var quest = _allQuests.FirstOrDefault(q => q.NormalizedName == questId);
    if (quest == null) return;

    // 3. 리스트에서 선택
    LstQuests.SelectedItem = quest;

    // 4. 스크롤하여 보이게
    LstQuests.ScrollIntoView(quest);

    // 5. 상세 패널 업데이트
    ShowQuestDetail(quest);
}

private void ResetFiltersForNavigation()
{
    // Status 필터를 "All"로 변경
    CmbStatus.SelectedIndex = 1; // All
    TxtSearch.Text = "";
    // 필요 시 다른 필터도 초기화
}
```

#### 1.3 ItemsPage Selection Method
```csharp
// ItemsPage.xaml.cs에 추가
public void SelectItem(string itemNormalizedName)
{
    // 1. 필터 초기화
    ResetFiltersForNavigation();

    // 2. 아이템 찾기
    var item = _aggregatedItems.FirstOrDefault(i =>
        i.NormalizedName == itemNormalizedName);
    if (item == null) return;

    // 3. 리스트에서 선택
    LstItems.SelectedItem = item;

    // 4. 스크롤하여 보이게
    LstItems.ScrollIntoView(item);

    // 5. 상세 패널 업데이트
    ShowItemDetail(item);
}
```

### 2. ViewModel Extensions

#### 2.1 Quest Requirement ViewModel
```csharp
// ItemsPage.xaml.cs - QuestRequirementViewModel에 추가
public class QuestRequirementViewModel
{
    // 기존 속성들...
    public string QuestName { get; set; }
    public string TraderName { get; set; }

    // 네비게이션용 추가
    public string QuestNormalizedName { get; set; }  // 추가: 퀘스트 식별자
}
```

#### 2.2 Required Item ViewModel
```csharp
// QuestListPage.xaml.cs - RequiredItemViewModel에 추가
public class RequiredItemViewModel
{
    // 기존 속성들...
    public string DisplayText { get; set; }
    public ImageSource? IconSource { get; set; }

    // 네비게이션용 추가
    public string ItemNormalizedName { get; set; }  // 추가: 아이템 식별자
}
```

### 3. XAML Click Handler Binding

#### 3.1 Items Page - Quest Name Click
```xml
<TextBlock Text="{Binding QuestName}"
           Foreground="{StaticResource TextPrimaryBrush}"
           Cursor="Hand"
           MouseLeftButtonDown="QuestName_Click"
           Tag="{Binding QuestNormalizedName}">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
                    <Setter Property="TextDecorations" Value="Underline"/>
                </Trigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

#### 3.2 Quests Page - Item Name Click
```xml
<TextBlock Text="{Binding DisplayText}"
           Foreground="{StaticResource TextPrimaryBrush}"
           Cursor="Hand"
           MouseLeftButtonDown="ItemName_Click"
           Tag="{Binding ItemNormalizedName}">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Foreground" Value="{StaticResource AccentBrush}"/>
                    <Setter Property="TextDecorations" Value="Underline"/>
                </Trigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

### 4. Event Handlers

#### 4.1 Items Page Event Handler
```csharp
// ItemsPage.xaml.cs
private void QuestName_Click(object sender, MouseButtonEventArgs e)
{
    if (sender is TextBlock textBlock && textBlock.Tag is string questId)
    {
        // MainWindow를 통해 네비게이션
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.NavigateToQuest(questId);
    }
}
```

#### 4.2 Quests Page Event Handler
```csharp
// QuestListPage.xaml.cs
private void ItemName_Click(object sender, MouseButtonEventArgs e)
{
    if (sender is TextBlock textBlock && textBlock.Tag is string itemName)
    {
        var mainWindow = Window.GetWindow(this) as MainWindow;
        mainWindow?.NavigateToItem(itemName);
    }
}
```

---

## Implementation Priority

### Phase 1: Core Navigation (필수)
1. MainWindow에 NavigateToQuest, NavigateToItem 메서드 추가
2. QuestListPage에 SelectQuest 메서드 추가
3. ItemsPage에 SelectItem 메서드 추가
4. ViewModel에 식별자 속성 추가

### Phase 2: UI & Event Binding (필수)
1. Items 페이지 XAML에 클릭 핸들러 및 스타일 추가
2. Quests 페이지 XAML에 클릭 핸들러 및 스타일 추가
3. 이벤트 핸들러 구현

### Phase 3: Polish (선택)
1. 네비게이션 시 스크롤 애니메이션
2. 선택 항목 하이라이트 효과
3. 탭 전환 애니메이션

---

## Edge Cases

### 1. 필터로 인해 항목이 숨겨진 경우
- 해결: 네비게이션 시 필터를 "All"로 초기화
- Status, Source 등 모든 필터를 기본값으로 리셋

### 2. 검색어로 인해 항목이 숨겨진 경우
- 해결: 검색어 초기화 (TxtSearch.Text = "")

### 3. 아이템이 리스트에 없는 경우 (퀘스트 완료 등)
- 해결: "Hide Fulfilled" 필터 해제
- 아이템을 찾지 못하면 사용자에게 알림 (선택사항)

### 4. 동일 이름의 복수 항목
- 퀘스트: NormalizedName으로 고유 식별 (중복 없음)
- 아이템: NormalizedName으로 고유 식별

---

## Dependencies

### Existing (사용됨)
- `MainWindow` - 탭 전환 로직 (TabQuests.IsChecked)
- `QuestListPage` - 퀘스트 리스트 및 상세 표시
- `ItemsPage` - 아이템 리스트 및 상세 표시
- `TarkovTask.NormalizedName` - 퀘스트 식별자
- `TarkovItem.NormalizedName` - 아이템 식별자

### New (구현 필요)
- `MainWindow.NavigateToQuest()` - 퀘스트 네비게이션 메서드
- `MainWindow.NavigateToItem()` - 아이템 네비게이션 메서드
- `QuestListPage.SelectQuest()` - 퀘스트 선택 메서드
- `ItemsPage.SelectItem()` - 아이템 선택 메서드

---

## Notes

### Accessibility
- 키보드 네비게이션 지원 고려 (Enter 키로 이동)
- 스크린 리더를 위한 적절한 역할(role) 지정

### Performance
- 필터 초기화 및 리스트 검색은 동기적으로 빠르게 수행
- 대량 데이터에서도 ScrollIntoView가 즉시 동작하도록

### Future Extensions
- Hideout 페이지에서도 아이템 클릭 시 Items 탭 이동
- Prerequisites 섹션의 퀘스트 이름도 클릭 가능하게
- 뒤로가기(Back) 네비게이션 히스토리 지원
