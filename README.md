# TarkovHelper

[![en](https://img.shields.io/badge/lang-English-blue.svg)](README.md)
[![ko](https://img.shields.io/badge/lang-한국어-red.svg)](README_KR.md)
[![ja](https://img.shields.io/badge/lang-日本語-green.svg)](README_JA.md)

<a href="https://buymeacoffee.com/zeliperstap" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" height="30"></a>

A Windows desktop application for tracking Escape from Tarkov quest and hideout progress.

## Key Features

Tarkov 1.0 Fully accepted

### Quest Management
- View and search all quests
- Track quest completion/in-progress status
- Display prerequisite and follow-up quests
- Automatically mark prerequisite quests as complete when starting a quest
- Link to quest wiki pages

### Hideout Management
- Track construction levels for each hideout station
- Display required items for each level upgrade
- Provide trader, skill, and dependent station requirements

### Required Items Tracking
- Aggregate items needed for in-progress quests
- Aggregate items needed for hideout construction
- Separate tracking for regular items and FIR (Found in Raid) items
- Calculate owned quantity and remaining required quantity
- Display item wiki links and icons

### Game Log Monitoring
- Automatically detect quest completion from game logs
- Support for both BSG Launcher and Steam versions
- Auto-detect game installation folder

### Multi-language Support
- English / Korean / Japanese support
- Real-time language switching

## Screenshots

<!-- Screenshots coming soon -->
![Quest List](screenshots/quests.png)
![Hideout](screenshots/hideout.png)
![Required Items](screenshots/items.png)

## Installation

### Requirements
- Windows OS
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

### Download Release
Download the latest version from the [Releases](../../releases) page.

### Build from Source
```bash
# Clone repository
git clone https://github.com/Zeliper/Tarkov-Item-Helper.git
cd Tarkov-Item-Helper

# Build and run
dotnet build -c Release
dotnet run -c Release
```

## Usage

### Data Update
When you first run the app, it automatically fetches the latest quest, item, and hideout data from the [tarkov.dev](https://tarkov.dev) API.

To manually update data:
```bash
dotnet run -- --fetch
```

### Quest Tracking
1. Check quest list in the **Quests** tab
2. Mark completion status with checkboxes
3. Search quests using the search bar
4. Click on a quest to view details and prerequisite/follow-up quests

### Hideout Tracking
1. Check station list in the **Hideout** tab
2. Set the current level for each station
3. View required items for the next level upgrade

### Required Items
1. Check all required items in the **Required Items** tab
2. Track progress by entering owned quantities
3. FIR items are managed separately

### Game Log Integration
The app auto-detects your game installation folder to provide notifications when quests are completed.

## Tech Stack

- **Framework**: .NET 8.0, WPF
- **Language**: C# 13
- **API**: [tarkov.dev GraphQL API](https://tarkov.dev)

## Data Storage Location

All data is stored in the `Data/` folder:
- `tasks.json` - Quest data
- `items.json` - Item data
- `hideouts.json` - Hideout data
- `progress.json` - User progress
- `settings.json` - Language settings

## License

MIT License

## Credits

- Game data: [tarkov.dev](https://tarkov.dev)
- Escape from Tarkov is a trademark of Battlestate Games.
