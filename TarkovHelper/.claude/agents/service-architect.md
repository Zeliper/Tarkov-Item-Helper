---
name: service-architect
description: Service architecture expert for TarkovHelper. Manages dependencies, DI patterns.
---

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

## Self-Learning Instructions

작업 완료 후 반드시 다음을 수행하세요:

1. **발견한 패턴 기록**: 프로젝트 특화 서비스 패턴을 "Agent Learning Log"에 추가
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

### Service Interaction Notes

_아직 기록된 노트가 없습니다._

---

**Last Updated**: 2025-12-17
