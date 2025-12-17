# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build
dotnet build

# Run WPF GUI application
dotnet run
```

## Architecture Overview

This is a WPF (.NET 8) application for Escape from Tarkov quest tracking. All data is stored in SQLite databases and supports trilingual display (English/Korean/Japanese).

### Data Flow

```
Master Data (read-only):
  Assets/tarkov_data.db (SQLite)
    ├── Items table (item names, icons, translations)
    ├── Quests table (quest data)
    ├── QuestRequiredItems table (quest item requirements)
    ├── QuestObjectives table (quest objectives with locations)
    └── MapMarkers table (map marker data)
        ↓
  QuestDbService, ItemDbService, MapMarkerDbService (load from DB)
        ↓
  UI Pages (QuestListPage, ItemsPage, HideoutPage, MapTrackerPage)

User Data (read-write):
  [AppData]/TarkovHelper/user_data.db (SQLite)
    ├── QuestProgress table (completed/failed quests)
    ├── ObjectiveProgress table (quest objective completion)
    ├── HideoutProgress table (module levels)
    ├── ItemInventory table (owned FIR/non-FIR counts)
    └── UserSettings table (app settings, language, map tracker settings)
        ↓
  QuestProgressService, HideoutProgressService, ItemInventoryService
  SettingsService, LocalizationService, MapTrackerService
```

### Key Components

**Models/**
- `TarkovTask.cs` - Quest model with EN/KO/JA names, prerequisites, required items/skills
- `TarkovItem.cs` - Item model with EN/KO/JA names, wiki links, icons
- `TarkovTrader.cs` - Trader model with EN/KO/JA names, images
- `HideoutModule.cs` - Hideout station/module with levels, requirements, EN/KO/JA names

**Services/**
- `QuestDbService.cs` - Loads quests from tarkov_data.db (Quests, QuestRequiredItems tables)
- `ItemDbService.cs` - Loads items from tarkov_data.db (Items table) for name/icon lookups
- `QuestProgressService.cs` - Quest progress tracking, persists to user_data.db
- `HideoutProgressService.cs` - Hideout construction progress, persists to user_data.db
- `ItemInventoryService.cs` - User's item inventory (FIR/non-FIR counts), persists to user_data.db
- `UserDataDbService.cs` - SQLite operations for user_data.db (progress, inventory, settings)
- `SettingsService.cs` - Application settings (player level, scav rep, etc.), persists to user_data.db
- `LocalizationService.cs` - UI localization (EN/KO/JA), persists language to user_data.db
- `NormalizedNameGenerator.cs` - Utility for generating normalized names for matching
- `QuestGraphService.cs` - Quest dependency graph traversal (prerequisites, follow-ups, optimal path)
- `ItemRequirementService.cs` - Item requirement aggregation across quests
- `MapMarkerDbService.cs` - Map marker data from tarkov_data.db
- `MapTrackerService.cs` - Map position tracking, settings persisted to user_data.db

**Services/Logging/**
- `LogLevel.cs` - Log level enumeration (Trace, Debug, Info, Warning, Error, Critical, None)
- `ILogger.cs` - Logger interface
- `LoggingService.cs` - Main logging service (singleton, session management)
- `Log.cs` - Logger factory (`Log.For<T>()`)
- `FileLogWriter.cs` - Async file writing with buffering
- `LogCleanupService.cs` - Old log cleanup service

**Pages/**
- `QuestListPage.xaml` - Quest list view with filtering and detail panel
- `HideoutPage.xaml` - Hideout module management with level controls and requirements
- `ItemsPage.xaml` - Aggregated item requirements from quests and hideout
- `CollectorPage.xaml` - Collector quest item tracking
- `MapTrackerPage.xaml` - Map position tracking with quest markers

**Entry Point**
- `Program.cs` - Custom Main with `[STAThread]` for WPF

### Important Patterns

- **DB-first architecture**: All data comes from SQLite databases
  - Master data: `Assets/tarkov_data.db` (bundled with app, read-only)
  - User data: `[AppData]/TarkovHelper/user_data.db` (created on first run, read-write)
- **ItemDbService for item lookups**: Use `ItemDbService.Instance.GetItemLookup()` to get item names/icons by ID or NormalizedName
- **QuestDbService for quest data**: Use `QuestDbService.Instance` for quest information
- **Settings stored in DB**: All settings are stored in `UserSettings` table as key-value pairs
  - App settings: `app.playerLevel`, `app.scavRep`, `app.baseFontSize`, etc.
  - Language: `app.language`
  - Map tracker: `mapTracker.settings` (JSON serialized)
  - Logging: `logging.level`, `logging.maxDays`, `logging.maxSizeMB`
- **Auto migration**: When user updates app, old JSON settings are automatically migrated to DB and deleted
- Quest prerequisites are stored as ID lists; use `QuestGraphService.GetAllPrerequisites()` for recursive chain resolution
- Item objectives track `FoundInRaid` boolean to distinguish FIR requirements from regular item submissions

### Items Tab - Quest Required Items

The Items tab shows items required for quests. The data flow:

```
QuestRequiredItems table (tarkov_data.db)
    ├── ItemId (FK to Items.Id, tarkov.dev API ID)
    ├── ItemName (original name from wiki)
    └── QuestId, Count, RequiresFIR, etc.
        ↓
QuestDbService.LoadQuestRequiredItemsAsync()
    - If ItemId is NULL → skip (item not matched in TarkovDBEditor)
    - If ItemId exists → add to quest.RequiredItems with ItemId as lookup key
        ↓
ItemsPage / CollectorPage
    - Lookup item by ItemId in Items table
    - Get name, icon, translations from Items table
```

**IMPORTANT**:
- `QuestRequiredItems.ItemId` must match `Items.Id` (both are tarkov.dev API IDs)
- Items with `NULL` ItemId are NOT displayed (need to be matched in TarkovDBEditor first)
- Item matching is done in TarkovDBEditor's RefreshDataService, not in TarkovHelper

## Logging System

### Overview

The application uses a custom logging system with file-based log storage and configurable log levels.

### Log Storage Location

```
[실행 폴더]/Logs/
├── 2025-12-17-001/           # 날짜-인스턴스번호
│   ├── trace.log             # Trace 레벨 로그
│   ├── debug.log             # Debug 레벨 로그
│   ├── info.log              # Info 레벨 로그
│   ├── warning.log           # Warning 레벨 로그
│   ├── error.log             # Error 레벨 로그
│   ├── critical.log          # Critical 레벨 로그
│   └── all.log               # 모든 레벨 통합 로그
├── 2025-12-17-002/           # 같은 날 두 번째 실행
└── ...
```

### Log Levels

| Level | Value | Description | Usage |
|-------|-------|-------------|-------|
| Trace | 0 | Very detailed debugging | Method entry/exit, variable values |
| Debug | 1 | Debugging information | DB queries, state changes |
| Info | 2 | General information | App start, page navigation |
| Warning | 3 | Potential issues | Slow responses, retries |
| Error | 4 | Errors occurred | Exceptions, failures |
| Critical | 5 | Fatal errors | App crashes, data corruption |
| None | 6 | Disable logging | - |

### Build-specific Defaults

| Build Mode | File Logging Level | Console Output |
|------------|-------------------|----------------|
| **Debug** | Trace (all logs) | Enabled |
| **Release** | Warning (Warning+) | Disabled |

### Usage

```csharp
using TarkovHelper.Services.Logging;

public class MyService
{
    private static readonly ILogger _log = Log.For<MyService>();

    public void DoSomething()
    {
        _log.Debug("Starting operation...");

        try
        {
            // ... work
            _log.Info("Operation completed successfully");
        }
        catch (Exception ex)
        {
            _log.Error("Operation failed", ex);
        }
    }
}
```

### Log Settings (user_data.db)

| Setting Key | Description | Default |
|-------------|-------------|---------|
| `logging.level` | Log level (0-6) | 3 (Warning) in Release |
| `logging.maxDays` | Log retention days | 7 |
| `logging.maxSizeMB` | Max log folder size (MB) | 100 |

### Log Format

```
[2025-12-17 14:30:45.123] [INFO ] [MainWindow] Application started
[2025-12-17 14:30:45.456] [DEBUG] [QuestDbService] Loaded 245 quests from database
[2025-12-17 14:30:46.789] [ERROR] [MapTrackerService] Failed to connect: Connection refused
    Exception: System.Net.Sockets.SocketException
    at MapTrackerService.Connect() in Services\MapTrackerService.cs:line 123
```

## Database Schema

### Master Data (tarkov_data.db)

**Items table:**
- `Id` - tarkov.dev API ID (primary key)
- `Name`, `NameKo`, `NameJa` - Translations
- `NormalizedName` - For matching
- `IconLink`, `WikiLink` - URLs

**Quests table:**
- `Id` - tarkov.dev API ID (primary key)
- `Name`, `NameKo`, `NameJa` - Translations
- `NormalizedName` - For matching
- `Trader`, `Location` - Quest info
- `RequiredLevel`, `ReqKappa` - Requirements

**QuestRequiredItems table:**
- `QuestId` - FK to Quests
- `ItemId` - FK to Items (can be NULL if not matched)
- `ItemName` - Original name from wiki
- `Count`, `RequiresFIR` - Requirements

### User Data (user_data.db)

**UserSettings table:**
- `Key` - Setting key (e.g., "app.playerLevel")
- `Value` - Setting value as string

**QuestProgress table:**
- `Id` - Quest ID
- `NormalizedName` - For backwards compatibility
- `Status` - "Done", "Failed", "Active"
- `UpdatedAt` - Timestamp

**HideoutProgress table:**
- `StationId` - Hideout station ID
- `Level` - Current level (0 = not built)
- `UpdatedAt` - Timestamp

**ItemInventory table:**
- `ItemNormalizedName` - Item identifier
- `FirQuantity`, `NonFirQuantity` - Owned counts
- `UpdatedAt` - Timestamp

## Claude Code Configuration

### Available SubAgents

Specialized agents for specific tasks (located in `.claude/agents/`):

| Agent | Purpose | Use When |
|-------|---------|----------|
| `db-schema-analyzer` | SQLite schema, queries, migrations | Database work |
| `wpf-xaml-specialist` | XAML binding, UI layout, events | UI modifications |
| `service-architect` | Service design, DI patterns | Service refactoring |
| `map-feature-specialist` | Map tracking, coordinates, markers | Map 탭 기능 작업 |
| `prd-manager` | PRD 관리, 작업 계획, 에이전트 조율 | 기능 계획 및 관리 |

### Agent Self-Learning

모든 에이전트는 작업 완료 후 다음을 수행합니다:
1. **발견한 패턴 기록**: 프로젝트 특화 정보를 에이전트 파일의 "Agent Learning Log"에 추가
2. **이슈 기록**: 발견한 문제점이나 주의사항 기록
3. **업데이트 리포트**: 에이전트 파일 수정 시 변경 내용 요약

이를 통해 에이전트가 프로젝트에 특화된 지식을 축적합니다.

### PRD Management

기능 개발 계획은 `PRDs/` 폴더에서 관리됩니다:

```
PRDs/
├── README.md              # PRD 관리 가이드
├── active/                # 진행 중인 PRD
├── archive/               # 완료된 PRD (월별 정리)
└── templates/             # PRD 템플릿
```

PRD 워크플로우:
1. `templates/feature-template.prd` 복사하여 `active/` 에 새 PRD 생성
2. 작업 진행하며 Task 체크 및 Progress Log 기록
3. 완료 시 `archive/YYYY-MM/` 로 이동

### Reference Project: TarkovDBEditor

Map 기능 작업 시 `../TarkovDBEditor/` 프로젝트를 참조해야 합니다:
- `Models/MapConfig.cs` - 맵 좌표 변환 설정
- `Services/MapMarkerService.cs` - 맵 마커 CRUD
- `Views/MapEditorWindow.xaml` - 맵 편집 UI
- `Resources/Data/map_configs.json` - 맵 설정 파일

### Available Skills

Helper skills for common tasks (located in `.claude/skills/`):

| Skill | Purpose |
|-------|---------|
| `db-query-helper` | Write SQLite queries for tarkov_data.db and user_data.db |
| `logging-config` | Configure logging system settings |

### Slash Commands

Custom commands (located in `.claude/commands/`):

| Command | Description |
|---------|-------------|
| `/build-and-run` | Build and run the WPF application |
| `/db-check` | Check database schema and statistics |

### Usage Examples

```
# Database schema questions → db-schema-analyzer
"How should I add a new table for tracking achievements?"

# UI work → wpf-xaml-specialist
"Help me add a filter dropdown to the ItemsPage"

# Service design → service-architect
"How should I structure a new notification service?"

# Quick commands
/build-and-run    # Build and launch the app
/db-check         # View database stats
```