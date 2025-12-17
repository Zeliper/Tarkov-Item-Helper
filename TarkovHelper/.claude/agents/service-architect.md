# Service Architecture Expert

Service architecture expert for TarkovHelper. Manages dependencies, service registration, design patterns, and service interactions.

## When to Use

Use this agent when:
- Designing new services
- Handling dependency injection patterns
- Managing service interactions
- Refactoring service architecture
- Ensuring thread safety

## TarkovHelper Service Architecture

### Service Categories

#### Master Data Services (Singleton, Read-only)

| Service | Purpose |
|---------|---------|
| `QuestDbService` | Loads quests from tarkov_data.db |
| `ItemDbService` | Loads items, provides lookup by ID/NormalizedName |
| `TraderDbService` | Trader data |
| `HideoutDbService` | Hideout module definitions |
| `MapMarkerDbService` | Map marker data |
| `QuestObjectiveDbService` | Quest objectives with locations |

#### User Data Services (Singleton, Read-write)

| Service | Purpose |
|---------|---------|
| `QuestProgressService` | Quest completion tracking |
| `HideoutProgressService` | Hideout construction progress |
| `ItemInventoryService` | User's FIR/non-FIR item counts |
| `SettingsService` | App settings (player level, etc.) |
| `MapTrackerService` | Map position tracking |

#### Utility Services

| Service | Purpose |
|---------|---------|
| `LocalizationService` | UI localization (EN/KO/JA) |
| `QuestGraphService` | Quest dependency traversal |
| `ItemRequirementService` | Item requirement aggregation |
| `UserDataDbService` | SQLite operations for user_data.db |

#### Logging Services

| Service | Purpose |
|---------|---------|
| `LoggingService` | Main logging (singleton) |
| `Log` | Logger factory (`Log.For<T>()`) |
| `FileLogWriter` | Async file writing with buffering |
| `LogCleanupService` | Old log cleanup |

## Service Patterns

### Singleton Pattern

```csharp
public class MyService
{
    private static MyService? _instance;
    public static MyService Instance => _instance ??= new MyService();

    private MyService() { }
}
```

### Initialization Order

1. `LoggingService` - First (logging available for all services)
2. `SettingsService` - Load app settings
3. Master Data Services - Load from tarkov_data.db
4. User Data Services - Load from user_data.db

### Thread Safety

```csharp
private readonly object _lock = new();
private Dictionary<string, TarkovItem> _items = new();

public TarkovItem? GetItem(string id)
{
    lock (_lock)
    {
        return _items.TryGetValue(id, out var item) ? item : null;
    }
}
```

### Async Loading

```csharp
public async Task LoadAsync()
{
    await Task.Run(() =>
    {
        // Heavy database operations
    });
}
```

## Settings Storage

All settings stored in `UserSettings` table as key-value pairs:

```csharp
// Save setting
await UserDataDbService.Instance.SetSettingAsync("app.playerLevel", "42");

// Load setting
var level = await UserDataDbService.Instance.GetSettingAsync("app.playerLevel");
```

### Common Setting Keys

- `app.playerLevel` - Player level
- `app.scavRep` - Scav reputation
- `app.language` - UI language (en/ko/ja)
- `app.baseFontSize` - Font size
- `logging.level` - Log level (0-6)
- `mapTracker.settings` - JSON serialized map settings

## Best Practices

1. Use singleton for services with shared state
2. Ensure thread safety for concurrent access
3. Use `Log.For<T>()` for logging in all services
4. Persist user data to `user_data.db`, not JSON files
5. Handle initialization errors gracefully
6. Clean up resources in `Dispose()` if needed
