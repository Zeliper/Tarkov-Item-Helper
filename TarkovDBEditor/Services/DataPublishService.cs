using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TarkovDBEditor.Services;

/// <summary>
/// Service for publishing DB updates from TarkovDBEditor to TarkovHelper Assets folder.
/// Compares files using MD5 hash and copies changed files.
/// </summary>
public class DataPublishService : IDisposable
{
    private readonly string _sourceBasePath;
    private readonly string _targetBasePath;

    public DataPublishService()
    {
        // TarkovDBEditor Release build output path
        _sourceBasePath = AppDomain.CurrentDomain.BaseDirectory;

        // TarkovHelper Assets path (relative to TarkovDBEditor)
        _targetBasePath = Path.GetFullPath(Path.Combine(_sourceBasePath, "..", "..", "..", "..", "TarkovHelper", "Assets"));
    }

    public string SourceBasePath => _sourceBasePath;
    public string TargetBasePath => _targetBasePath;

    /// <summary>
    /// Result of a comparison operation
    /// </summary>
    public class ComparisonResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        // Database
        public bool DbExists { get; set; }
        public bool DbChanged { get; set; }
        public string? SourceDbHash { get; set; }
        public string? TargetDbHash { get; set; }
        public long SourceDbSize { get; set; }
        public long TargetDbSize { get; set; }

        // Version
        public string? CurrentVersion { get; set; }
        public string? NewVersion { get; set; }

        // Map configs
        public bool MapConfigsChanged { get; set; }
        public string? SourceMapConfigsHash { get; set; }
        public string? TargetMapConfigsHash { get; set; }

        // Map SVGs
        public List<FileChangeInfo> MapSvgChanges { get; set; } = new();
        public int MapSvgAdded { get; set; }
        public int MapSvgUpdated { get; set; }
        public int MapSvgUnchanged { get; set; }

        // Map marker icons
        public List<FileChangeInfo> MarkerIconChanges { get; set; } = new();
        public int MarkerIconAdded { get; set; }
        public int MarkerIconUpdated { get; set; }
        public int MarkerIconUnchanged { get; set; }

        // Item icons
        public List<FileChangeInfo> ItemIconChanges { get; set; } = new();
        public int ItemIconAdded { get; set; }
        public int ItemIconUpdated { get; set; }
        public int ItemIconUnchanged { get; set; }

        // Hideout icons
        public List<FileChangeInfo> HideoutIconChanges { get; set; } = new();
        public int HideoutIconAdded { get; set; }
        public int HideoutIconUpdated { get; set; }
        public int HideoutIconUnchanged { get; set; }

        public bool HasAnyChanges => DbChanged || MapConfigsChanged ||
            MapSvgAdded > 0 || MapSvgUpdated > 0 ||
            MarkerIconAdded > 0 || MarkerIconUpdated > 0 ||
            ItemIconAdded > 0 || ItemIconUpdated > 0 ||
            HideoutIconAdded > 0 || HideoutIconUpdated > 0;

        public int TotalChanges =>
            (DbChanged ? 1 : 0) +
            (MapConfigsChanged ? 1 : 0) +
            MapSvgAdded + MapSvgUpdated +
            MarkerIconAdded + MarkerIconUpdated +
            ItemIconAdded + ItemIconUpdated +
            HideoutIconAdded + HideoutIconUpdated;
    }

    public class FileChangeInfo
    {
        public string FileName { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string TargetPath { get; set; } = "";
        public ChangeType Type { get; set; }
        public long SourceSize { get; set; }
        public long TargetSize { get; set; }
    }

    public enum ChangeType
    {
        Added,
        Updated,
        Unchanged
    }

    public class PublishResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }

        public int FilesCopied { get; set; }
        public int IconsCopied { get; set; }
        public string? NewVersion { get; set; }
        public List<string> CopiedFiles { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Compare all files between source (TarkovDBEditor Release) and target (TarkovHelper Assets)
    /// </summary>
    public async Task<ComparisonResult> CompareAsync(Action<string>? progress = null)
    {
        var result = new ComparisonResult { Success = true };

        try
        {
            // Verify paths exist
            if (!Directory.Exists(_sourceBasePath))
            {
                result.Success = false;
                result.ErrorMessage = $"Source path not found: {_sourceBasePath}";
                return result;
            }

            if (!Directory.Exists(_targetBasePath))
            {
                result.Success = false;
                result.ErrorMessage = $"Target path not found: {_targetBasePath}\n\nPlease ensure TarkovHelper project exists.";
                return result;
            }

            // 1. Compare Database
            progress?.Invoke("Comparing database...");
            await CompareDatabase(result);

            // 2. Read current version
            progress?.Invoke("Reading version info...");
            await ReadVersionInfo(result);

            // 3. Compare map configs
            progress?.Invoke("Comparing map configs...");
            await CompareMapConfigs(result);

            // 4. Compare map SVGs
            progress?.Invoke("Comparing map SVG files...");
            await CompareMapSvgs(result);

            // 5. Compare marker icons
            progress?.Invoke("Comparing marker icons...");
            await CompareMarkerIcons(result);

            // 6. Compare item icons
            progress?.Invoke("Comparing item icons...");
            await CompareItemIcons(result);

            // 7. Compare hideout icons
            progress?.Invoke("Comparing hideout icons...");
            await CompareHideoutIcons(result);

            progress?.Invoke("Comparison complete.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task CompareDatabase(ComparisonResult result)
    {
        var sourceDbPath = Path.Combine(_sourceBasePath, "tarkov_data.db");
        var targetDbPath = Path.Combine(_targetBasePath, "tarkov_data.db");

        result.DbExists = File.Exists(sourceDbPath);

        if (!result.DbExists)
        {
            return;
        }

        result.SourceDbHash = await ComputeFileHashAsync(sourceDbPath);
        result.SourceDbSize = new FileInfo(sourceDbPath).Length;

        if (File.Exists(targetDbPath))
        {
            result.TargetDbHash = await ComputeFileHashAsync(targetDbPath);
            result.TargetDbSize = new FileInfo(targetDbPath).Length;
            result.DbChanged = result.SourceDbHash != result.TargetDbHash;
        }
        else
        {
            result.DbChanged = true; // New file
        }
    }

    private async Task ReadVersionInfo(ComparisonResult result)
    {
        var versionPath = Path.Combine(_targetBasePath, "db_version.txt");

        if (File.Exists(versionPath))
        {
            result.CurrentVersion = (await File.ReadAllTextAsync(versionPath)).Trim();
        }
        else
        {
            result.CurrentVersion = "0.0.0";
        }

        // Suggest new version (increment patch)
        if (Version.TryParse(result.CurrentVersion, out var currentVer))
        {
            result.NewVersion = $"{currentVer.Major}.{currentVer.Minor}.{currentVer.Build + 1}";
        }
        else
        {
            result.NewVersion = "1.0.0";
        }
    }

    private async Task CompareMapConfigs(ComparisonResult result)
    {
        var sourceConfigPath = Path.Combine(_sourceBasePath, "Resources", "Data", "map_configs.json");
        var targetConfigPath = Path.Combine(_targetBasePath, "DB", "Data", "map_configs.json");

        if (!File.Exists(sourceConfigPath))
        {
            return;
        }

        result.SourceMapConfigsHash = await ComputeFileHashAsync(sourceConfigPath);

        if (File.Exists(targetConfigPath))
        {
            result.TargetMapConfigsHash = await ComputeFileHashAsync(targetConfigPath);
            result.MapConfigsChanged = result.SourceMapConfigsHash != result.TargetMapConfigsHash;
        }
        else
        {
            result.MapConfigsChanged = true;
        }
    }

    private async Task CompareMapSvgs(ComparisonResult result)
    {
        var sourceDir = Path.Combine(_sourceBasePath, "Resources", "Maps");
        var targetDir = Path.Combine(_targetBasePath, "DB", "Maps");

        await CompareFiles(sourceDir, targetDir, "*.svg",
            result.MapSvgChanges,
            added => result.MapSvgAdded = added,
            updated => result.MapSvgUpdated = updated,
            unchanged => result.MapSvgUnchanged = unchanged);
    }

    private async Task CompareMarkerIcons(ComparisonResult result)
    {
        var sourceDir = Path.Combine(_sourceBasePath, "Resources", "Icons");
        var targetDir = Path.Combine(_targetBasePath, "DB", "Icons");

        await CompareFiles(sourceDir, targetDir, "*.webp",
            result.MarkerIconChanges,
            added => result.MarkerIconAdded = added,
            updated => result.MarkerIconUpdated = updated,
            unchanged => result.MarkerIconUnchanged = unchanged);
    }

    private async Task CompareItemIcons(ComparisonResult result)
    {
        var sourceDir = Path.Combine(_sourceBasePath, "wiki_data", "icons");
        var targetDir = Path.Combine(_targetBasePath, "icons");

        await CompareFiles(sourceDir, targetDir, "*.png",
            result.ItemIconChanges,
            added => result.ItemIconAdded = added,
            updated => result.ItemIconUpdated = updated,
            unchanged => result.ItemIconUnchanged = unchanged);
    }

    private async Task CompareHideoutIcons(ComparisonResult result)
    {
        var sourceDir = Path.Combine(_sourceBasePath, "icons", "hideout");
        var targetDir = Path.Combine(_targetBasePath, "icons", "hideout");

        await CompareFiles(sourceDir, targetDir, "*.png",
            result.HideoutIconChanges,
            added => result.HideoutIconAdded = added,
            updated => result.HideoutIconUpdated = updated,
            unchanged => result.HideoutIconUnchanged = unchanged);
    }

    private async Task CompareFiles(string sourceDir, string targetDir, string pattern,
        List<FileChangeInfo> changes,
        Action<int> setAdded, Action<int> setUpdated, Action<int> setUnchanged)
    {
        int added = 0, updated = 0, unchanged = 0;

        if (!Directory.Exists(sourceDir))
        {
            setAdded(0);
            setUpdated(0);
            setUnchanged(0);
            return;
        }

        var sourceFiles = Directory.GetFiles(sourceDir, pattern, SearchOption.TopDirectoryOnly);

        foreach (var sourceFile in sourceFiles)
        {
            var fileName = Path.GetFileName(sourceFile);
            var targetFile = Path.Combine(targetDir, fileName);

            var info = new FileChangeInfo
            {
                FileName = fileName,
                SourcePath = sourceFile,
                TargetPath = targetFile,
                SourceSize = new FileInfo(sourceFile).Length
            };

            if (!File.Exists(targetFile))
            {
                info.Type = ChangeType.Added;
                added++;
                changes.Add(info);
            }
            else
            {
                info.TargetSize = new FileInfo(targetFile).Length;

                var sourceHash = await ComputeFileHashAsync(sourceFile);
                var targetHash = await ComputeFileHashAsync(targetFile);

                if (sourceHash != targetHash)
                {
                    info.Type = ChangeType.Updated;
                    updated++;
                    changes.Add(info);
                }
                else
                {
                    info.Type = ChangeType.Unchanged;
                    unchanged++;
                    // Don't add unchanged files to reduce list size
                }
            }
        }

        setAdded(added);
        setUpdated(updated);
        setUnchanged(unchanged);
    }

    /// <summary>
    /// Publish all changed files to TarkovHelper Assets folder
    /// </summary>
    public async Task<PublishResult> PublishAsync(ComparisonResult comparison, string newVersion, Action<string>? progress = null)
    {
        var result = new PublishResult { Success = true, NewVersion = newVersion };

        try
        {
            // 1. Copy database (using stream to handle files open by other processes)
            if (comparison.DbChanged)
            {
                progress?.Invoke("Copying database...");
                var sourceDbPath = Path.Combine(_sourceBasePath, "tarkov_data.db");
                var targetDbPath = Path.Combine(_targetBasePath, "tarkov_data.db");

                await CopyFileWithShareAsync(sourceDbPath, targetDbPath);
                result.CopiedFiles.Add("tarkov_data.db");
                result.FilesCopied++;
            }

            // 2. Copy map configs
            if (comparison.MapConfigsChanged)
            {
                progress?.Invoke("Copying map configs...");
                var sourceConfigPath = Path.Combine(_sourceBasePath, "Resources", "Data", "map_configs.json");
                var targetConfigPath = Path.Combine(_targetBasePath, "DB", "Data", "map_configs.json");

                Directory.CreateDirectory(Path.GetDirectoryName(targetConfigPath)!);
                File.Copy(sourceConfigPath, targetConfigPath, overwrite: true);
                result.CopiedFiles.Add("DB/Data/map_configs.json");
                result.FilesCopied++;
            }

            // 3. Copy map SVGs
            progress?.Invoke("Copying map SVGs...");
            var svgTargetDir = Path.Combine(_targetBasePath, "DB", "Maps");
            Directory.CreateDirectory(svgTargetDir);
            foreach (var change in comparison.MapSvgChanges.Where(c => c.Type != ChangeType.Unchanged))
            {
                File.Copy(change.SourcePath, change.TargetPath, overwrite: true);
                result.CopiedFiles.Add($"DB/Maps/{change.FileName}");
                result.FilesCopied++;
            }

            // 4. Copy marker icons
            progress?.Invoke("Copying marker icons...");
            var markerTargetDir = Path.Combine(_targetBasePath, "DB", "Icons");
            Directory.CreateDirectory(markerTargetDir);
            foreach (var change in comparison.MarkerIconChanges.Where(c => c.Type != ChangeType.Unchanged))
            {
                File.Copy(change.SourcePath, change.TargetPath, overwrite: true);
                result.IconsCopied++;
            }

            // 5. Copy item icons
            progress?.Invoke($"Copying item icons ({comparison.ItemIconChanges.Count} changes)...");
            var itemIconTargetDir = Path.Combine(_targetBasePath, "icons");
            Directory.CreateDirectory(itemIconTargetDir);
            int iconCount = 0;
            foreach (var change in comparison.ItemIconChanges.Where(c => c.Type != ChangeType.Unchanged))
            {
                File.Copy(change.SourcePath, change.TargetPath, overwrite: true);
                result.IconsCopied++;
                iconCount++;

                if (iconCount % 100 == 0)
                {
                    progress?.Invoke($"Copying item icons ({iconCount}/{comparison.ItemIconChanges.Count})...");
                }
            }

            // 6. Copy hideout icons
            progress?.Invoke("Copying hideout icons...");
            var hideoutTargetDir = Path.Combine(_targetBasePath, "icons", "hideout");
            Directory.CreateDirectory(hideoutTargetDir);
            foreach (var change in comparison.HideoutIconChanges.Where(c => c.Type != ChangeType.Unchanged))
            {
                File.Copy(change.SourcePath, change.TargetPath, overwrite: true);
                result.IconsCopied++;
            }

            // 7. Update version file
            progress?.Invoke("Updating version...");
            var versionPath = Path.Combine(_targetBasePath, "db_version.txt");
            await File.WriteAllTextAsync(versionPath, newVersion);
            result.CopiedFiles.Add("db_version.txt");

            progress?.Invoke("Publish complete.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var md5 = MD5.Create();
        // Use FileShare.ReadWrite to allow reading files that are open by other processes (like SQLite DB)
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hash = await md5.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Copy a file using FileShare.ReadWrite to handle files that are open by other processes (like SQLite DB)
    /// </summary>
    private async Task CopyFileWithShareAsync(string sourcePath, string targetPath)
    {
        const int bufferSize = 81920; // 80KB buffer

        await using var sourceStream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var targetStream = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await sourceStream.CopyToAsync(targetStream);
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
