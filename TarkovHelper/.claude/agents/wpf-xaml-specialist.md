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
