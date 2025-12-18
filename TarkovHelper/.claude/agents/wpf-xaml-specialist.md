---
name: wpf-xaml-specialist
description: WPF/XAML and C# UI expert for TarkovHelper. Handles XAML binding, view models, events.
---

# WPF/XAML Specialist

WPF/XAML and C# UI expert for TarkovHelper. Handles XAML binding, view models, event handling, layout optimization.

## When to Use

Use this agent when:
- Building or modifying UI pages
- Working with data binding
- Handling UI events
- Optimizing layout performance
- Debugging XAML binding errors

## TarkovHelper UI Structure

### Main Pages

| Page | Purpose |
|------|---------|
| `Pages/QuestListPage.xaml` | Quest list with filtering and detail panel |
| `Pages/HideoutPage.xaml` | Hideout module management with level controls |
| `Pages/ItemsPage.xaml` | Aggregated item requirements from quests/hideout |
| `Pages/CollectorPage.xaml` | Collector quest item tracking |
| `Pages/MapTrackerPage.xaml` | Map position tracking with quest markers |

### Entry Points

- `MainWindow.xaml` / `MainWindow.xaml.cs` - Main window with navigation
- `Program.cs` - Custom Main with `[STAThread]` for WPF

## Key Patterns

### Data Binding

```xml
<!-- Property binding with update trigger -->
<TextBox Text="{Binding PlayerLevel, UpdateSourceTrigger=PropertyChanged}" />

<!-- Collection binding -->
<ItemsControl ItemsSource="{Binding Quests}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <TextBlock Text="{Binding Name}" />
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

### INotifyPropertyChanged

```csharp
public class ViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private int _playerLevel;
    public int PlayerLevel
    {
        get => _playerLevel;
        set
        {
            _playerLevel = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayerLevel)));
        }
    }
}
```

### ObservableCollection

```csharp
public ObservableCollection<TarkovTask> Quests { get; } = new();

// Adding/removing items automatically updates UI
Quests.Add(newQuest);
Quests.Remove(oldQuest);
```

## Build & Test

```bash
# Build (checks XAML compilation)
dotnet build

# Run WPF GUI
dotnet run
```

## Common Issues

1. **Binding not updating** - Check `INotifyPropertyChanged` implementation
2. **List not updating** - Use `ObservableCollection<T>` instead of `List<T>`
3. **Memory leaks** - Unsubscribe event handlers in cleanup
4. **UI freezing** - Use `async/await` for long operations

## Localization

All UI text supports EN/KO/JA via `LocalizationService`:
- Use `NameKo`, `NameJa` properties for translations
- Language setting stored in `UserSettings.app.language`

## Self-Learning Instructions

작업 완료 후 반드시 다음을 수행하세요:

1. **발견한 패턴 기록**: 프로젝트 특화 UI 패턴을 "Agent Learning Log"에 추가
2. **이슈 기록**: 발견한 문제점이나 주의사항 기록
3. **업데이트 리포트**: 에이전트 파일 수정 시 변경 내용 요약 리포트

---

## Agent Learning Log

> 이 섹션은 에이전트가 작업 중 학습한 프로젝트 특화 정보를 기록합니다.
> 작업 완료 시 중요한 발견사항을 여기에 추가하세요.

### Discovered Patterns

_아직 기록된 패턴이 없습니다._

### Known Issues

_아직 기록된 이슈가 없습니다._

### UI/UX Notes

_아직 기록된 노트가 없습니다._

---

**Last Updated**: 2025-12-17
