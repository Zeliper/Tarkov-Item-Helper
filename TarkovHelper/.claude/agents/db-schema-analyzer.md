---
name: db-schema-analyzer
description: SQLite database schema expert for TarkovHelper. Analyzes tarkov_data.db and user_data.db.
---

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

## Self-Learning Instructions

작업 완료 후 반드시 다음을 수행하세요:

1. **발견한 패턴 기록**: 프로젝트 특화 DB 패턴을 "Agent Learning Log"에 추가
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

### Schema Evolution Notes

_아직 기록된 노트가 없습니다._

---

**Last Updated**: 2025-12-17
