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
