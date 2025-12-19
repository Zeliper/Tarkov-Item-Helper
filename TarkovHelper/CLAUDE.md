# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

For solution-wide commands and architecture overview, see the parent `../CLAUDE.md`.

## Build Commands (This Project)

```powershell
# Build this project
dotnet build TarkovHelper.csproj

# Run application
dotnet run --project TarkovHelper.csproj

# Build Release
dotnet build TarkovHelper.csproj -c Release
```

## Project-Specific Architecture

### Entry Points

- `Program.cs` - Application entry point, runs migration, creates MainWindow
- `App.xaml.cs` - WPF Application class, handles startup/exit, auto-updater, global exception handling
- `MainWindow.xaml.cs` - Main UI shell with tab navigation

### Debug Mode

Debug features controlled by `Debug/AppEnv.cs`:
- `AppEnv.IsDebugMode` - Automatically true in DEBUG build
- In debug mode, `ToolboxWindow` appears with test functions
- Test functions registered via `[TestMenu]` attribute in `Debug/TestMenu.cs`

### Directory Structure

```
TarkovHelper/
├── Assets/           # Static resources (DB, icons, maps, fonts)
├── Debug/            # Debug toolbox and test utilities
├── Models/           # Data models (TarkovTask, HideoutModule, etc.)
├── Pages/            # WPF Pages (QuestList, Hideout, Items, Collector, Map)
│   ├── Map/          # Map page components and view models
│   └── Components/   # Reusable UI components
├── Services/         # Business logic services (singleton pattern)
│   ├── Logging/      # Application logging system
│   ├── Map/          # Map-related services
│   └── Settings/     # Settings management
└── Windows/          # Dialog windows and overlays
```

### Service Initialization Order

Services initialize lazily via `Instance` singleton pattern. Key initialization flow on startup:

1. `MigrationService.RunMigrationIfNeeded()` - Data migration check
2. `SettingsService.Instance` - Load user settings
3. `QuestProgressService.InitializeFromDbAsync()` - Load quest progress
4. `HideoutDbService.LoadStationsAsync()` - Load hideout data
5. `QuestGraphService.Initialize(tasks)` - Build quest dependency graph
6. `DatabaseUpdateService.StartBackgroundUpdates()` - Start DB update timer
7. `UpdateService.StartAutoCheck()` - Start app update check

### Key Service Patterns

**Database Services** (`*DbService`):
- Load data from `tarkov_data.db` via SQLite
- Emit `DataRefreshed` event when data changes
- Subscribe to `DatabaseUpdateService.DatabaseUpdated` for auto-reload

**Progress Services** (`*ProgressService`):
- Track user completion state
- Persist to `user_data.db` via `UserDataDbService`
- Emit `ProgressChanged` events for UI updates

**Map Services** (`Services/Map/`):
- `MapCoordinateTransformer` - Convert game coordinates to screen coordinates
- `MapTrackerService` - Track player position from game screenshots
- `ScreenshotWatcherService` - Watch for new screenshots

### UI Update Pattern

```csharp
// Service emits event
ProgressChanged?.Invoke(this, questName);

// UI subscribes and updates on dispatcher
service.ProgressChanged += (s, name) => {
    Dispatcher.Invoke(() => RefreshDisplay());
};
```

### Logging

Use `Services/Logging/Log.cs`:
```csharp
private static readonly ILogger _log = Log.For<ClassName>();
_log.Info("message");
_log.Debug("message");
_log.Warning("message");
_log.Error("message", exception);
```

Logs written to `%LocalAppData%/TarkovHelper/Logs/`.

### Data Paths

- `AppEnv.DataPath` - `{AppDir}/Data/` - Cache data
- `AppEnv.CachePath` - `{AppDir}/Cache/` - Wiki pages, images
- `AppEnv.ConfigPath` - `{AppDir}/Config/` - Legacy config location
- User data now stored in `%LocalAppData%/TarkovHelper/Config/user_data.db`

### Cross-Tab Navigation

MainWindow provides navigation methods for other pages to use:
```csharp
var mainWindow = Application.Current.MainWindow as MainWindow;
mainWindow?.NavigateToQuest(questNormalizedName);
mainWindow?.NavigateToItem(itemId);
mainWindow?.NavigateToHideout(stationId);
```
