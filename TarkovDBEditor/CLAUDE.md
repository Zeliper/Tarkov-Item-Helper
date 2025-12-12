# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build this project
dotnet build

# Run the WPF GUI application
dotnet run

# Build the entire solution (includes TarkovHelper main app)
dotnet build ../TarkovHelper.sln
```

## Project Overview

TarkovDBEditor is a SQLite database management tool built with WPF (.NET 8). It provides a GUI for managing custom databases with dynamic schema support, and includes specialized services for fetching/exporting Tarkov wiki and tarkov.dev API data.

This project is part of the TarkovHelper solution. See `../TarkovHelper/CLAUDE.md` for the main application documentation.

## Project Characteristics & Common Issues

### Type Ambiguity (IMPORTANT)

This project uses **both WPF and WindowsForms** (`UseWindowsForms=true` in csproj). This causes type name conflicts that require explicit using aliases in code-behind files:

```csharp
// Required aliases for WPF code-behind files (especially Map-related windows)
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
```

**Always add these aliases when creating new WPF windows that use graphics/input handling.**

### SVG Rendering

Uses **SharpVectors.Wpf** (1.8.5) for SVG map rendering. SVG files are in `Resources/Maps/`.

```xml
<!-- XAML namespace for SVG -->
xmlns:svgc="http://sharpvectors.codeplex.com/svgc/"
<svgc:SvgViewbox Source="..." />
```

## Architecture

### Pattern: MVVM with Singleton Services

```
MainWindow.xaml (.cs)     - View layer, handles dialogs and DataGrid
        ↓
MainViewModel.cs          - ViewModel with commands and data binding
        ↓
DatabaseService.cs        - Singleton SQLite service with schema metadata
```

### Key Components

**Services/** (Singleton pattern)
- `DatabaseService.cs` - SQLite operations with dynamic schema management. Uses `_schema_meta` table for column metadata as JSON.
- `MapMarkerService.cs` - CRUD operations for map markers (spawns, extractions, etc.)
- `TarkovDevDataService.cs` - Fetches item/quest/hideout data from tarkov.dev GraphQL API
- `TarkovWikiDataService.cs` - Parses Fandom wiki for item categories
- `WikiQuestService.cs` - Quest data parsing and caching
- `WikiCacheService.cs` - Page content caching with revision checking
- `RefreshDataService.cs` - Syncs wiki/API data to local DB
- `HideoutDataService.cs` - Fetches hideout data from tarkov.dev and saves to DB with icon download (Base64 encoded ID filenames)
- `SvgStylePreprocessor.cs` - SVG floor layer filtering for multi-floor maps

**ViewModels/**
- `MainViewModel.cs` - Main MVVM view model with table/row selection, CRUD commands
- `QuestRequirementsViewModel.cs` - Quest validation and objective editing
- `RelayCommand.cs` - ICommand implementation

**Models/**
- `TableSchema.cs` - Schema model with `ColumnSchema` (type, PK, FK, auto-increment)
- `DataRow.cs` - Dictionary-based row model for dynamic column access
- `MapConfig.cs` - Map coordinate transform config (game coords ↔ screen coords)
- `MapMarker.cs` - Map marker model (spawns, extractions, POIs)
- `QuestObjectiveItem.cs` - Quest objective with location points

**Views/** (Windows & Dialogs)
- `MainWindow.xaml` - Main DB editor with menu and data grid
- `MapEditorWindow.xaml` - Map marker editor with click-to-place
- `MapPreviewWindow.xaml` - Read-only map view with markers + objectives overlay
- `QuestRequirementsView.xaml` - Quest objective validator/editor
- `CreateTableDialog.xaml` - Table creation dialog
- `AddColumnDialog.xaml` - Column addition dialog
- `EditRowDialog.xaml` - Row editing with FK dropdowns

### Database Tables

All tables are auto-created by their respective services. The database file (`.db`) is managed via File > Open/New Database.

#### System Tables

```sql
-- Schema metadata (auto-created by DatabaseService)
-- Stores column definitions for dynamic table management in the UI
CREATE TABLE _schema_meta (
    TableName TEXT PRIMARY KEY,
    DisplayName TEXT,
    SchemaJson TEXT NOT NULL,  -- JSON array of ColumnSchema
    CreatedAt TEXT,            -- ISO 8601 format
    UpdatedAt TEXT             -- ISO 8601 format
)
```

#### Data Tables (created by RefreshDataService)

```sql
-- Items from tarkov.dev API
CREATE TABLE Items (
    Id TEXT PRIMARY KEY,       -- tarkov.dev ID
    BsgId TEXT,                -- BSG internal ID
    Name TEXT NOT NULL,
    NameEN TEXT,
    NameKO TEXT,
    NameJA TEXT,
    ShortNameEN TEXT,
    ShortNameKO TEXT,
    ShortNameJA TEXT,
    WikiPageLink TEXT,
    IconUrl TEXT,
    Category TEXT,
    Categories TEXT,           -- JSON array
    UpdatedAt TEXT
)

-- Quests from wiki + tarkov.dev
CREATE TABLE Quests (
    Id TEXT PRIMARY KEY,       -- tarkov.dev ID
    BsgId TEXT,
    Name TEXT NOT NULL,
    NameEN TEXT,
    NameKO TEXT,
    NameJA TEXT,
    WikiPageLink TEXT,
    Trader TEXT,               -- Prapor, Therapist, etc.
    Location TEXT,             -- Map name (Customs, Factory, etc.)
    MinLevel INTEGER,
    MinLevelApproved INTEGER NOT NULL DEFAULT 0,
    MinLevelApprovedAt TEXT,
    MinScavKarma INTEGER,
    MinScavKarmaApproved INTEGER NOT NULL DEFAULT 0,
    MinScavKarmaApprovedAt TEXT,
    UpdatedAt TEXT
)

-- Quest pre-requisites (which quests must be completed first)
CREATE TABLE QuestRequirements (
    Id TEXT PRIMARY KEY,       -- Generated: "{QuestId}_{RequiredQuestId}"
    QuestId TEXT NOT NULL,
    RequiredQuestId TEXT NOT NULL,
    RequirementType TEXT NOT NULL DEFAULT 'Complete',  -- Complete, Start, etc.
    DelayMinutes INTEGER,      -- Time delay after completing required quest
    GroupId INTEGER NOT NULL DEFAULT 0,  -- For OR groups (0 = AND, same GroupId = OR)
    ContentHash TEXT,          -- For change detection
    IsApproved INTEGER NOT NULL DEFAULT 0,
    ApprovedAt TEXT,
    UpdatedAt TEXT,
    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
    FOREIGN KEY (RequiredQuestId) REFERENCES Quests(Id) ON DELETE CASCADE
)
-- Indexes: idx_questreq_questid, idx_questreq_requiredid

-- Quest objectives (tasks within a quest)
CREATE TABLE QuestObjectives (
    Id TEXT PRIMARY KEY,       -- Generated: NanoId
    QuestId TEXT NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    ObjectiveType TEXT NOT NULL DEFAULT 'Custom',  -- Kill, Collect, HandOver, Visit, Mark, Stash, Survive, Build, Task
    Description TEXT NOT NULL,
    TargetType TEXT,           -- For Kill objectives: Scav, PMC, Boss, etc.
    TargetCount INTEGER,
    ItemId TEXT,               -- FK to Items table
    ItemName TEXT,
    RequiresFIR INTEGER NOT NULL DEFAULT 0,  -- Found In Raid
    MapName TEXT,              -- Specific map for this objective
    LocationName TEXT,         -- Named location within map
    LocationPoints TEXT,       -- JSON array of LocationPoint [{X, Y, Z, FloorId}]
    Conditions TEXT,           -- Additional conditions text
    ContentHash TEXT,
    IsApproved INTEGER NOT NULL DEFAULT 0,
    ApprovedAt TEXT,
    UpdatedAt TEXT,
    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
    FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE SET NULL
)
-- Indexes: idx_questobj_questid, idx_questobj_itemid, idx_questobj_map

-- Alternative quests (quest A OR quest B as pre-requisite)
CREATE TABLE OptionalQuests (
    Id TEXT PRIMARY KEY,       -- Generated: "{QuestId}_{AlternativeQuestId}"
    QuestId TEXT NOT NULL,
    AlternativeQuestId TEXT NOT NULL,
    ContentHash TEXT,
    IsApproved INTEGER NOT NULL DEFAULT 0,
    ApprovedAt TEXT,
    UpdatedAt TEXT,
    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
    FOREIGN KEY (AlternativeQuestId) REFERENCES Quests(Id) ON DELETE CASCADE
)
-- Indexes: idx_optquest_questid, idx_optquest_altid

-- Quest required items (from Wiki's "Related Quest Items" table)
CREATE TABLE QuestRequiredItems (
    Id TEXT PRIMARY KEY,       -- Hash-based ID
    QuestId TEXT NOT NULL,
    ItemId TEXT,               -- FK to Items table (if matched)
    ItemName TEXT NOT NULL,    -- Wiki item name
    Count INTEGER NOT NULL DEFAULT 1,  -- Required quantity
    RequiresFIR INTEGER NOT NULL DEFAULT 0,  -- Found in Raid required
    RequirementType TEXT NOT NULL DEFAULT 'Required',  -- Handover, Required, Optional
    SortOrder INTEGER NOT NULL DEFAULT 0,
    DogtagMinLevel INTEGER,    -- Minimum dogtag level (for dogtag items only)
    ContentHash TEXT,          -- For change detection
    IsApproved INTEGER NOT NULL DEFAULT 0,
    ApprovedAt TEXT,
    UpdatedAt TEXT,
    FOREIGN KEY (QuestId) REFERENCES Quests(Id) ON DELETE CASCADE,
    FOREIGN KEY (ItemId) REFERENCES Items(Id) ON DELETE SET NULL
)
-- Indexes: idx_questreqitem_questid, idx_questreqitem_itemid
```

#### Hideout Tables (created by HideoutDataService)

```sql
-- Hideout stations/modules from tarkov.dev API
CREATE TABLE HideoutStations (
    Id TEXT PRIMARY KEY,       -- tarkov.dev ID
    Name TEXT NOT NULL,        -- English name
    NameKO TEXT,               -- Korean name
    NameJA TEXT,               -- Japanese name
    NormalizedName TEXT,       -- URL-friendly name
    ImageLink TEXT,            -- Icon URL from tarkov.dev
    MaxLevel INTEGER NOT NULL DEFAULT 0,
    UpdatedAt TEXT
)

-- Hideout levels for each station
CREATE TABLE HideoutLevels (
    Id TEXT PRIMARY KEY,       -- Generated: "{StationId}_{Level}"
    StationId TEXT NOT NULL,
    Level INTEGER NOT NULL,
    ConstructionTime INTEGER NOT NULL DEFAULT 0,  -- Seconds
    UpdatedAt TEXT,
    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
)
-- Index: idx_hideoutlevels_stationid

-- Item requirements for hideout levels
CREATE TABLE HideoutItemRequirements (
    Id TEXT PRIMARY KEY,       -- Generated: "{StationId}_{Level}_{ItemId}"
    StationId TEXT NOT NULL,
    Level INTEGER NOT NULL,
    ItemId TEXT NOT NULL,      -- tarkov.dev item ID
    ItemName TEXT NOT NULL,
    ItemNameKO TEXT,
    ItemNameJA TEXT,
    IconLink TEXT,
    Count INTEGER NOT NULL DEFAULT 1,
    FoundInRaid INTEGER NOT NULL DEFAULT 0,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    UpdatedAt TEXT,
    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
)
-- Index: idx_hideoutitemreq_stationid

-- Station requirements (prerequisite hideout modules)
CREATE TABLE HideoutStationRequirements (
    Id TEXT PRIMARY KEY,       -- Generated: "{StationId}_{Level}_{RequiredStationId}"
    StationId TEXT NOT NULL,
    Level INTEGER NOT NULL,
    RequiredStationId TEXT NOT NULL,
    RequiredStationName TEXT NOT NULL,
    RequiredStationNameKO TEXT,
    RequiredStationNameJA TEXT,
    RequiredLevel INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    UpdatedAt TEXT,
    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE,
    FOREIGN KEY (RequiredStationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
)
-- Index: idx_hideoutstationreq_stationid

-- Trader loyalty requirements
CREATE TABLE HideoutTraderRequirements (
    Id TEXT PRIMARY KEY,       -- Generated: "{StationId}_{Level}_{TraderId}"
    StationId TEXT NOT NULL,
    Level INTEGER NOT NULL,
    TraderId TEXT NOT NULL,
    TraderName TEXT NOT NULL,
    TraderNameKO TEXT,
    TraderNameJA TEXT,
    RequiredLevel INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    UpdatedAt TEXT,
    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
)
-- Index: idx_hideouttraderreq_stationid

-- Skill requirements
CREATE TABLE HideoutSkillRequirements (
    Id TEXT PRIMARY KEY,       -- Generated: "{StationId}_{Level}_{SkillName}"
    StationId TEXT NOT NULL,
    Level INTEGER NOT NULL,
    SkillName TEXT NOT NULL,
    SkillNameKO TEXT,
    SkillNameJA TEXT,
    RequiredLevel INTEGER NOT NULL,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    UpdatedAt TEXT,
    FOREIGN KEY (StationId) REFERENCES HideoutStations(Id) ON DELETE CASCADE
)
-- Index: idx_hideoutskillreq_stationid
```

#### Map Tables (created by MapMarkerService)

```sql
-- Map markers (spawns, extractions, POIs)
CREATE TABLE MapMarkers (
    Id TEXT PRIMARY KEY,       -- Generated GUID
    Name TEXT NOT NULL,
    NameKo TEXT,
    MarkerType TEXT NOT NULL,  -- Enum string: PmcSpawn, ScavSpawn, PmcExtraction, ScavExtraction, SharedExtraction, Transit, BossSpawn, RaiderSpawn, Lever, Keys
    MapKey TEXT NOT NULL,      -- Map config key (Customs, Factory, etc.)
    X REAL NOT NULL DEFAULT 0, -- Game X coordinate
    Y REAL NOT NULL DEFAULT 0, -- Game Y coordinate (height, usually 0)
    Z REAL NOT NULL DEFAULT 0, -- Game Z coordinate
    FloorId TEXT,              -- For multi-floor maps (main, basement, level2, etc.)
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
)
-- Indexes: idx_mapmarkers_mapkey, idx_mapmarkers_type
```

### JSON Data Formats

#### LocationPoints (QuestObjectives.LocationPoints)
```json
[
  {"X": 123.5, "Y": 0, "Z": -45.2, "FloorId": "main"},
  {"X": 125.0, "Y": 0, "Z": -43.8, "FloorId": "main"}
]
```
- 1 point: Single marker
- 2 points: Line between points
- 3+ points: Polygon area

#### ColumnSchema (in _schema_meta.SchemaJson)
```json
[
  {
    "Name": "Id",
    "DisplayName": "ID",
    "Type": "Text",
    "IsPrimaryKey": true,
    "IsRequired": false,
    "IsAutoIncrement": false,
    "ForeignKeyTable": null,
    "ForeignKeyColumn": null,
    "SortOrder": 0
  }
]
```

### Supported Column Types

```csharp
enum ColumnType { Text, Integer, Real, Boolean, DateTime, Json }
```

- **Boolean**: Stored as INTEGER (0/1)
- **DateTime**: Stored as TEXT in ISO 8601 format
- **Json**: Stored as TEXT

### Database Access Patterns

1. **Singleton Services**: All DB access goes through singleton service instances
   - `DatabaseService.Instance` - General DB operations
   - `MapMarkerService.Instance` - Map marker CRUD
   - `RefreshDataService` - Wiki/API data sync
   - `HideoutDataService` - Hideout data from tarkov.dev

2. **Connection String**: `Data Source={DatabaseService.Instance.DatabasePath}`

3. **Async Pattern**: All DB operations are async using `Microsoft.Data.Sqlite`
   ```csharp
   var connectionString = $"Data Source={DatabaseService.Instance.DatabasePath}";
   await using var connection = new SqliteConnection(connectionString);
   await connection.OpenAsync();
   // ... execute queries
   ```

4. **UPSERT Pattern**: Use `ON CONFLICT(Id) DO UPDATE` for insert-or-update operations

## Resource Files

### Maps (`Resources/Maps/`)
SVG map files for all Tarkov locations:
- Customs, Factory, GroundZero, Interchange, Labs, Labyrinth
- Lighthouse, Reserve, Shoreline, StreetsOfTarkov, Woods

### Map Configuration (`Resources/Data/map_configs.json`)
Coordinate transform matrices and floor definitions for each map:
```json
{
  "Key": "Customs",
  "DisplayName": "Customs",
  "SvgFileName": "Customs.svg",
  "ImageWidth": 4096,
  "ImageHeight": 2867,
  "CalibratedTransform": [a, b, c, d, tx, ty],
  "Floors": [{ "LayerId": "main", "DisplayName": "Main", "Order": 0, "IsDefault": true }]
}
```

### Icons (`Resources/Icons/`)
Marker icons in WebP format:
- PMC Extraction.webp, SCAV Extraction.webp, Transit.webp
- PMC Spawn.webp, SCAV Spawn.webp, BOSS Spawn.webp, Raider Spawn.webp
- Lever.webp, Keys.webp

## Menu Structure

- **File**: New/Open Database, Exit
- **Table**: New Table, Delete Table, Add Column
- **Data**: Add Row, Delete Row, Refresh
- **Debug**: Export Wiki Items, Export Wiki Quests, Refresh Data, Map Editor
- **Tools**: Quest Requirements Validator, Map Preview

## Data Export Feature

The "Export Wiki Item Categories" menu option:
1. Fetches all item categories from Fandom wiki
2. Builds a category tree structure
3. Enriches items with tarkov.dev API data (IDs, translations)
4. Outputs to `wiki_data/` directory:
   - `wiki_items.json` - Enriched item list
   - `wiki_category_tree.json` - Full category hierarchy
   - `wiki_category_structure.json` - Processed structure
   - `dev_missing.json` - Wiki items not in tarkov.dev
   - `dev_only.json` - tarkov.dev items not in wiki

## Dependencies

- **Microsoft.Data.Sqlite** (8.0.11) - SQLite database access
- **SharpVectors.Wpf** (1.8.5) - SVG rendering for maps
- **.NET 8.0 WPF + WindowsForms** - Windows desktop UI
