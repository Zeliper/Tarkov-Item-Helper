using System.IO;
using System.Reflection;
using TarkovHelper.Debug;

namespace TarkovHelper.Services;

/// <summary>
/// Handles data migration between versions.
/// When upgrading from 1.x.x to 2.x.x, clears all data and cache.
/// </summary>
public static class MigrationService
{
    // Store .version file outside Data folder to prevent deletion during migration
    private static readonly string VersionFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".version");

    /// <summary>
    /// Run migration if needed. Should be called at application startup.
    /// </summary>
    public static void RunMigrationIfNeeded()
    {
        var currentVersion = GetCurrentVersion();
        var lastVersion = GetLastRunVersion();

        // First run or fresh install
        if (lastVersion == null)
        {
            SaveCurrentVersion(currentVersion);
            return;
        }

        // Check if upgrading from 1.x.x to 2.x.x
        if (RequiresMigration(lastVersion, currentVersion))
        {
            PerformMigration();
        }

        SaveCurrentVersion(currentVersion);
    }

    /// <summary>
    /// Get the current application version
    /// </summary>
    private static Version GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version ?? new Version(2, 0, 0);
    }

    /// <summary>
    /// Get the last run version from the version file
    /// </summary>
    private static Version? GetLastRunVersion()
    {
        try
        {
            if (!File.Exists(VersionFilePath))
            {
                // Check if Data folder exists but no version file
                // This means it's an upgrade from a version before migration was implemented
                if (Directory.Exists(AppEnv.DataPath) &&
                    Directory.GetFiles(AppEnv.DataPath).Length > 0)
                {
                    // Assume it was 1.x.x
                    return new Version(1, 0, 0);
                }
                return null;
            }

            var versionString = File.ReadAllText(VersionFilePath).Trim();
            return Version.TryParse(versionString, out var version) ? version : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Save the current version to the version file
    /// </summary>
    private static void SaveCurrentVersion(Version version)
    {
        try
        {
            Directory.CreateDirectory(AppEnv.DataPath);
            File.WriteAllText(VersionFilePath, version.ToString());
        }
        catch
        {
            // Ignore errors
        }
    }

    /// <summary>
    /// Check if migration is required based on version change
    /// </summary>
    private static bool RequiresMigration(Version lastVersion, Version currentVersion)
    {
        // Upgrading from 1.x.x to 2.x.x requires data cleanup
        return lastVersion.Major == 1 && currentVersion.Major >= 2;
    }

    /// <summary>
    /// Perform the migration by clearing data and cache
    /// </summary>
    private static void PerformMigration()
    {
        Console.WriteLine("Migrating from v1.x.x to v2.x.x - clearing data and cache...");

        // Clear Data folder
        ClearDirectory(AppEnv.DataPath);

        // Clear Cache folder
        ClearDirectory(AppEnv.CachePath);

        Console.WriteLine("Migration complete.");
    }

    /// <summary>
    /// Clear all contents of a directory
    /// </summary>
    private static void ClearDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to clear directory {path}: {ex.Message}");
        }
    }
}
