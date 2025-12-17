---
description: Check database schema and statistics
---

Check the TarkovHelper database schema and basic statistics.

## Master Data (tarkov_data.db)

```bash
# List all tables
sqlite3 Assets/tarkov_data.db ".tables"

# Table row counts
sqlite3 Assets/tarkov_data.db "SELECT 'Items' as tbl, COUNT(*) as cnt FROM Items UNION ALL SELECT 'Quests', COUNT(*) FROM Quests UNION ALL SELECT 'QuestRequiredItems', COUNT(*) FROM QuestRequiredItems UNION ALL SELECT 'QuestObjectives', COUNT(*) FROM QuestObjectives UNION ALL SELECT 'MapMarkers', COUNT(*) FROM MapMarkers UNION ALL SELECT 'Traders', COUNT(*) FROM Traders UNION ALL SELECT 'HideoutModules', COUNT(*) FROM HideoutModules;"
```

## User Data (user_data.db)

The user database is located at `[AppData]/TarkovHelper/user_data.db`.

```bash
# Check if user database exists and list tables
sqlite3 "%APPDATA%/TarkovHelper/user_data.db" ".tables" 2>nul || echo "User DB not created yet (app needs to run first)"

# Check settings
sqlite3 "%APPDATA%/TarkovHelper/user_data.db" "SELECT * FROM UserSettings;" 2>nul
```

## Schema Check

```bash
# View specific table schema
sqlite3 Assets/tarkov_data.db ".schema Items"
sqlite3 Assets/tarkov_data.db ".schema Quests"
sqlite3 Assets/tarkov_data.db ".schema QuestRequiredItems"
```
