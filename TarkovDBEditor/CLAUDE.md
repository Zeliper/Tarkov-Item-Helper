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

**Services/**
- `DatabaseService.cs` - SQLite operations with dynamic schema management. Uses a `_schema_meta` table to store column metadata as JSON, enabling runtime schema inspection and modification.
- `TarkovDevDataService.cs` - Fetches item data from tarkov.dev GraphQL API, enriches wiki items with API data
- `TarkovWikiDataService.cs` - Parses Fandom wiki for item categories and builds category trees

**ViewModels/**
- `MainViewModel.cs` - Main MVVM view model with table/row selection, CRUD commands

**Models/**
- `TableSchema.cs` - Schema model with `ColumnSchema` including type, PK, FK, auto-increment flags
- `DataRow.cs` - Dictionary-based row model for dynamic column access

**Views/**
- `CreateTableDialog.xaml` - Table creation with column definition
- `AddColumnDialog.xaml` - Add columns to existing tables
- `EditRowDialog.xaml` - Row editing with FK dropdown support

### Database Schema Storage

The `DatabaseService` stores schema metadata in a special `_schema_meta` table:
```sql
CREATE TABLE _schema_meta (
    TableName TEXT PRIMARY KEY,
    DisplayName TEXT,
    SchemaJson TEXT NOT NULL,  -- JSON array of ColumnSchema
    CreatedAt TEXT,
    UpdatedAt TEXT
)
```

This allows the application to track column types, display names, and foreign key relationships beyond what SQLite's native schema provides.

### Supported Column Types

```csharp
enum ColumnType { Text, Integer, Real, Boolean, DateTime, Json }
```

Boolean stored as INTEGER (0/1), DateTime and Json stored as TEXT.

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
- **.NET 8.0 WPF** - Windows desktop UI
