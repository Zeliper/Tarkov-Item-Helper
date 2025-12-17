# Database Query Helper

Write and analyze SQLite queries for TarkovHelper databases.

## Purpose

Help write, debug, and optimize SQLite queries for:
- `Assets/tarkov_data.db` - Master data (read-only)
- `[AppData]/TarkovHelper/user_data.db` - User data (read-write)

## Common Queries

### Schema Inspection

```bash
# View all tables
sqlite3 Assets/tarkov_data.db ".tables"

# View specific table schema
sqlite3 Assets/tarkov_data.db ".schema Items"
sqlite3 Assets/tarkov_data.db ".schema Quests"
sqlite3 Assets/tarkov_data.db ".schema QuestRequiredItems"
```

### Item Queries

```sql
-- Find item by name
SELECT * FROM Items WHERE Name LIKE '%Salewa%';

-- Find item by normalized name
SELECT * FROM Items WHERE NormalizedName = 'salewa-first-aid-kit';

-- Get item with Korean name
SELECT Id, Name, NameKo FROM Items WHERE Id = 'item-id';
```

### Quest Queries

```sql
-- Get quests by trader
SELECT * FROM Quests WHERE Trader = 'Prapor';

-- Get quest requirements
SELECT q.Name, qri.ItemName, qri.Count, qri.RequiresFIR
FROM Quests q
JOIN QuestRequiredItems qri ON q.Id = qri.QuestId
WHERE q.Id = 'quest-id';

-- Find quests requiring specific item
SELECT q.Name, qri.Count, qri.RequiresFIR
FROM Quests q
JOIN QuestRequiredItems qri ON q.Id = qri.QuestId
JOIN Items i ON qri.ItemId = i.Id
WHERE i.NormalizedName = 'item-normalized-name';
```

### User Data Queries

```sql
-- Get all settings
SELECT * FROM UserSettings;

-- Get specific setting
SELECT Value FROM UserSettings WHERE Key = 'app.playerLevel';

-- Get quest progress
SELECT * FROM QuestProgress WHERE Status = 'Done';

-- Get item inventory
SELECT * FROM ItemInventory WHERE FirQuantity > 0;
```

## Data Relationships

```
tarkov_data.db:
  Items ←── QuestRequiredItems ──→ Quests
    ↑
    └── QuestObjectives

user_data.db:
  UserSettings (key-value)
  QuestProgress (by quest ID)
  HideoutProgress (by station ID)
  ItemInventory (by normalized name)
  ObjectiveProgress (by objective ID)
```

## Important Notes

1. **tarkov_data.db is read-only** - Changes require app update
2. **user_data.db is read-write** - User progress and settings
3. **ItemId can be NULL** in QuestRequiredItems - Items not matched in TarkovDBEditor
4. **All IDs are tarkov.dev API IDs** - String format like "5c0d5e4486f77478390952fe"
