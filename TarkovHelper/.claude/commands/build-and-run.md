---
description: Build and run the WPF application
---

Build and run the TarkovHelper WPF application.

## Steps

1. **Build the project**
   ```bash
   dotnet build
   ```

2. **If build succeeds, run the application**
   ```bash
   dotnet run
   ```

## What to Check

- XAML compilation errors
- Missing resource references
- Binding errors in Output window
- Runtime exceptions on startup

## Common Issues

- **XAML parse error**: Check for typos in XAML files
- **Missing assembly**: Run `dotnet restore` first
- **Database not found**: Ensure `Assets/tarkov_data.db` exists
