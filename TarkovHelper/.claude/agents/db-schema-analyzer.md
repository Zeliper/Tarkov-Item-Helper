# Database Schema Analyzer

SQLite database schema expert for TarkovHelper. Analyzes tarkov_data.db and user_data.db schema, migrations, queries.

## When to Use

Use this agent when working with:
- Database schema design or modifications
- SQLite queries and performance optimization
- Data migrations
- Table relationships and foreign keys

## Key Database Files

- `Assets/tarkov_data.db` - Master data (read-only)
  - Items, Quests, QuestRequiredItems, QuestObjectives, MapMarkers, Traders, HideoutModules
- `[AppData]/TarkovHelper/user_data.db` - User data (read-write)
  - QuestProgress, HideoutProgress, ItemInventory, UserSettings, ObjectiveProgress

## Key Services

- `UserDataDbService.cs` - SQLite operations for user data
- `QuestDbService.cs` - Quest data loading
- `ItemDbService.cs` - Item data and lookups
- `MapMarkerDbService.cs` - Map marker data

## Schema Patterns

### Master Data (tarkov_data.db)

```sql
-- Items table
CREATE TABLE Items (
    Id TEXT PRIMARY KEY,           -- tarkov.dev API ID
    Name TEXT, NameKo TEXT, NameJa TEXT,
    NormalizedName TEXT,
    IconLink TEXT, WikiLink TEXT
);

-- Quests table
CREATE TABLE Quests (
    Id TEXT PRIMARY KEY,
    Name TEXT, NameKo TEXT, NameJa TEXT,
    NormalizedName TEXT,
    Trader TEXT, Location TEXT,
    RequiredLevel INTEGER, ReqKappa INTEGER
);

-- QuestRequiredItems table
CREATE TABLE QuestRequiredItems (
    QuestId TEXT,                  -- FK to Quests.Id
    ItemId TEXT,                   -- FK to Items.Id (can be NULL)
    ItemName TEXT,                 -- Original name from wiki
    Count INTEGER, RequiresFIR INTEGER
);
```

### User Data (user_data.db)

```sql
-- UserSettings (key-value store)
CREATE TABLE UserSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT
);

-- Common keys: app.playerLevel, app.language, logging.level, mapTracker.settings
```

## Best Practices

1. Always check existing schema before suggesting changes
2. Use indexes for frequently queried columns
3. Validate foreign key relationships
4. Consider migration compatibility for user_data.db
5. tarkov_data.db is bundled and read-only - changes require app update
