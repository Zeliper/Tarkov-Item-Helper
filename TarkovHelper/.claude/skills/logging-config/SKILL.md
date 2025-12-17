# Logging Configuration

Configure and manage TarkovHelper's logging system.

## Log Files Location

```
[실행 폴더]/Logs/
├── 2025-12-17-001/           # Date-InstanceNumber format
│   ├── trace.log             # Trace level logs
│   ├── debug.log             # Debug level logs
│   ├── info.log              # Info level logs
│   ├── warning.log           # Warning level logs
│   ├── error.log             # Error level logs
│   ├── critical.log          # Critical level logs
│   └── all.log               # All levels combined
├── 2025-12-17-002/           # Second run same day
└── ...
```

## Log Levels

| Level | Value | Description | Example Usage |
|-------|-------|-------------|---------------|
| Trace | 0 | Very detailed | Method entry/exit, variable values |
| Debug | 1 | Debugging info | DB queries, state changes |
| Info | 2 | General info | App start, page navigation |
| Warning | 3 | Potential issues | Slow responses, retries |
| Error | 4 | Errors | Exceptions, failures |
| Critical | 5 | Fatal errors | App crashes, data corruption |
| None | 6 | Disable | No logging |

## Build-Specific Defaults

| Build Mode | File Log Level | Console Output |
|------------|----------------|----------------|
| **Debug** | Trace (0) | Enabled |
| **Release** | Warning (3) | Disabled |

## Configuration (user_data.db)

Settings stored in `UserSettings` table:

| Key | Description | Default |
|-----|-------------|---------|
| `logging.level` | Log level (0-6) | 3 (Warning) in Release |
| `logging.maxDays` | Log retention days | 7 |
| `logging.maxSizeMB` | Max log folder size (MB) | 100 |

### Change Log Level via SQL

```sql
-- Set to Debug level
UPDATE UserSettings SET Value = '1' WHERE Key = 'logging.level';

-- Set to Info level
UPDATE UserSettings SET Value = '2' WHERE Key = 'logging.level';

-- Set retention to 14 days
UPDATE UserSettings SET Value = '14' WHERE Key = 'logging.maxDays';
```

## Usage in Code

```csharp
using TarkovHelper.Services.Logging;

public class MyService
{
    private static readonly ILogger _log = Log.For<MyService>();

    public void DoSomething()
    {
        _log.Trace("Entering DoSomething");
        _log.Debug("Processing with value: {0}", value);

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

## Log Format

```
[2025-12-17 14:30:45.123] [INFO ] [MainWindow] Application started
[2025-12-17 14:30:45.456] [DEBUG] [QuestDbService] Loaded 245 quests from database
[2025-12-17 14:30:46.789] [ERROR] [MapTrackerService] Failed to connect: Connection refused
    Exception: System.Net.Sockets.SocketException
    at MapTrackerService.Connect() in Services\MapTrackerService.cs:line 123
```

## Key Services

- `LoggingService.cs` - Main logging service (singleton, session management)
- `Log.cs` - Logger factory (`Log.For<T>()`)
- `FileLogWriter.cs` - Async file writing with buffering
- `LogCleanupService.cs` - Old log cleanup based on maxDays/maxSizeMB

## Troubleshooting

1. **Logs not appearing** - Check `logging.level` setting
2. **Too many logs** - Increase log level (e.g., 3 for Warning+)
3. **Disk space issues** - Reduce `logging.maxDays` or `logging.maxSizeMB`
4. **Missing exception details** - Use `_log.Error("message", exception)` format
