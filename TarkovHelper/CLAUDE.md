# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Build
dotnet build

# Run WPF GUI application
dotnet run

# Run CLI mode to fetch fresh data from API
dotnet run -- --fetch

# Fetch task data with wiki reqkappa matching
dotnet run -- --fetch-tasks

# Fetch master data (items, skills, traders) from tarkov.dev API
dotnet run -- --fetch-master-data

# Quest graph analysis (shows stats if no quest name)
dotnet run -- --quest-graph
dotnet run -- --quest-graph "delivery-from-the-past"

# Item requirements analysis (shows stats if no item name)
dotnet run -- --item-requirements
dotnet run -- --item-requirements "flash drive"

# Kappa container quest path and required items
dotnet run -- --kappa-path
```

## Architecture Overview

This is a WPF (.NET 8) application for Escape from Tarkov quest tracking. It fetches quest and item data from the tarkov.dev GraphQL API and supports bilingual display (English/Korean).

### Data Flow

```
tarkov.dev GraphQL API
        ↓
TarkovApiService (fetches EN + KO data in parallel)
        ↓
TaskDatasetManager (merges, saves/loads JSON)
        ↓
Data/tasks.json, Data/items.json
        ↓
MainWindow (UI display)
```

### Key Components

**Models/**
- `TarkovTask.cs` - Quest model with EN/KO/JA names, prerequisites, required items/skills
- `TarkovItem.cs` - Item model with EN/KO/JA names, wiki links, icons
- `TarkovSkill.cs` - Skill model with EN/KO/JA names
- `TarkovTrader.cs` - Trader model with EN/KO/JA names, images
- `HideoutModule.cs` - Hideout station/module with levels, requirements, EN/KO/JA names
- `WikiQuest.cs` - Quest data parsed from wiki

**Services/**
- `TarkovDataService.cs` - Main data service, coordinates task data fetching
- `TarkovDevApiService.cs` - Master data (items, skills, traders, hideout) fetching from tarkov.dev API
- `HideoutProgressService.cs` - Hideout construction progress tracking and persistence
- `WikiDataService.cs` - Wiki page fetching and parsing
- `WikiQuestParser.cs` - Parses quest relationships, items, level/skill requirements from wiki
- `NormalizedNameGenerator.cs` - Utility for generating normalized names for matching
- `LocalizationService.cs` - UI localization support
- `QuestGraphService.cs` - Quest dependency graph traversal (prerequisites, follow-ups, optimal path)
- `ItemRequirementService.cs` - Item requirement aggregation across quests

**Pages/**
- `QuestListPage.xaml` - Quest list view with filtering and detail panel
- `HideoutPage.xaml` - Hideout module management with level controls and requirements
- `ItemsPage.xaml` - Aggregated item requirements from quests and hideout

**Entry Point**
- `Program.cs` - Custom Main with `[STAThread]` for WPF; supports CLI modes

### Important Patterns

- Quest prerequisites are stored as ID lists; use `TaskDatasetManager.GetAllPrerequisites()` for recursive chain resolution
- Item objectives track `FoundInRaid` boolean to distinguish FIR requirements from regular item submissions
- Data is cached locally; delete `Data/*.json` files to force API refresh on next launch

## Data Sources & Trust Hierarchy

### IMPORTANT: tarkov.dev API vs Wiki Data

**tarkov.dev API is used for:**
- Task/Item translations (English, Korean, Japanese)
- Task IDs for reference
- Item icons and basic info

**Wiki (.wiki files) is the source of truth for:**
- `reqKappa` (Kappa container requirement) - ALWAYS parse from wiki, never trust API's `kappaRequired` field
- `previous` / `leadsTo` - Quest relationships from Infobox
- `requiredLevel` - "Must be level X" requirements
- `requiredSkills` - Skill level requirements (e.g., Charisma 10)
- `requiredItems` - Items needed with FIR status and amounts
- The API's kappa data is outdated; wiki is authoritative

### Task Name Matching Rules

When matching tarkov.dev tasks to wiki files:

1. **Remove `[PVP ZONE]` suffix** - API includes this, wiki doesn't
   - Example: `Easy Money - Part 1 [PVP ZONE]` → `Easy Money - Part 1`

2. **Normalize special characters:**
   - Apostrophes: `'` (U+2019) → `'` (standard)
   - Question marks: `?` → `_` (filename safe)
   - Other invalid chars: `:`, `*`, `"`, `<`, `>`, `|` → `_`

3. **Handle hyphen/space variations:**
   - `Half-Empty` may be saved as `Half Empty.wiki`

4. **HTML entity encoding:**
   - `You've Got Mail` may be saved as `You&#39;ve Got Mail.wiki`

### Output Files

- `Data/tasks.json` - Matched tasks with wiki-parsed reqKappa
- `Data/tasks_missing.json` - Tasks that couldn't be matched to wiki (new quests, need wiki download)
- `Data/items.json` - All items from tarkov.dev API with EN/KO/JA translations (4807+ items)
- `Data/skills.json` - All skills from tarkov.dev API with EN/KO/JA translations (49 skills)
- `Data/traders.json` - All traders from tarkov.dev API with EN/KO/JA translations
- `Data/hideout.json` - Hideout stations with levels and requirements from tarkov.dev API
- `Data/quest_progress.json` - User's quest progress (completed/failed quests)
- `Data/hideout_progress.json` - User's hideout construction progress (module levels)
