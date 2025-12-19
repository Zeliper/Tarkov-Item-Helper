# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Environment

**This is a Windows development environment.** Use PowerShell commands instead of Bash/Unix commands.

## Build & Run Commands

```powershell
# Build entire solution
dotnet build TarkovHelper.sln

# Build specific project
dotnet build TarkovHelper/TarkovHelper.csproj

# Build Release
dotnet build TarkovHelper/TarkovHelper.csproj -c Release

# Run main application
dotnet run --project TarkovHelper/TarkovHelper.csproj

# Run with data fetch
dotnet run --project TarkovHelper/TarkovHelper.csproj -- --fetch
```

## Solution Structure

| Project | Description |
|---------|-------------|
| **TarkovHelper** | Main WPF application for tracking quests, hideouts, items |
| **TarkovDBEditor** | Database editor tool for managing tarkov_data.db (see `TarkovDBEditor/CLAUDE.md` for details) |
| **CheckDb** | Utility project (.NET 10) |

## Architecture Overview

### Pattern: Singleton Services with Event-Driven Data Flow

```
UI Pages (WPF)
    ↓ Subscribe to events
Services (Singleton instances)
    ↓ Persist/load data
Databases (SQLite)
```

### Key Services

**Data Services:**
- `QuestDbService` - Loads quest data from tarkov_data.db
- `HideoutDbService` - Hideout module data
- `ItemDbService` - Item data management
- `UserDataDbService` - User persistence (progress, settings, inventory)

**Sync Services:**
- `LogSyncService` - Monitors EFT game logs, detects quest events (started/completed/failed)
- `EftRaidEventService` - Parses raid state (Idle/Matching/InRaid/Ended) from EFT logs
- `DatabaseUpdateService` - Auto-updates tarkov_data.db from GitHub

**Map Services:**
- `MapTrackerService` - Coordinate tracking for map visualization
- `MapCoordinateTransformer` - Game-to-screen coordinate conversion
- `OverlayMiniMapService` - Manages overlay minimap window

**Other:**
- `LocalizationService` - Multi-language support (EN, KO, JA)
- `QuestProgressService` - Quest progress state management
- `SettingsService` - User preferences persistence

### Database Architecture

**tarkov_data.db** (Asset database - auto-updated from GitHub):
- Quest, hideout, item, trader data from tarkov.dev API
- Read-only during runtime
- Located: `TarkovHelper/Assets/tarkov_data.db`

**user_data.db** (User persistence):
- Quest/hideout progress, item inventory, settings
- Located: `%LocalAppData%/TarkovHelper/Config/user_data.db`

### UI Structure

- **Pages:** QuestListPage, HideoutPage, ItemsPage, CollectorPage, MapPage
- **Overlay:** OverlayMiniMapWindow (topmost window for in-game use)
- **Global keyboard hooks** for overlay control (requires admin rights)

## Data Flow Patterns

### Game Log Monitoring
```
EFT debug.log → LogSyncService (FileSystemWatcher)
  → EftRaidEventService (regex parsing)
  → QuestEventDetected event
  → QuestProgressService.Update()
  → UserDataDbService persistence
```

### Database Updates
```
DatabaseUpdateService (5-min timer)
  → Check GitHub version
  → Download if newer
  → DatabaseUpdated event
  → Services reload data
  → UI refreshes
```

## Key Patterns

### Dual-Key Quest Mapping
Quests tracked by both `Id` (tarkov.dev) and `NormalizedName` (wiki-legacy) for migration compatibility.

### Event-Driven Updates
Services emit events (ProgressChanged, DatabaseUpdated, DataRefreshed) that UI pages subscribe to for reactive updates.

## External APIs

- **tarkov.dev**: Quest, hideout, item data (embedded in tarkov_data.db)
- **GitHub**: Auto-updates for both app and database
- **EFT Game Logs**: Real-time quest/raid event monitoring

## Framework & Dependencies

- **.NET 8.0 WPF** (Windows desktop)
- **Microsoft.Data.Sqlite** - SQLite database access
- **SharpVectors.Wpf** - SVG map rendering
- **Westermo.GraphX.Controls** - Graph visualization (quest dependencies)
- **AutoUpdater.NET** - Application self-updates

## Admin Rights

App requires administrator privileges (via app.manifest) for:
- Global keyboard hooks (overlay hotkeys work in-game)
- Game log file monitoring

## Localization

Multi-language support via `LocalizationService` partial classes:
- `LocalizationService.Core.cs` - Core strings
- `LocalizationService.Map.cs` - Map-specific
- `LocalizationService.Quest.cs` - Quest-specific

Supported languages: English (EN), Korean (KO), Japanese (JA)
