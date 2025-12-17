using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TarkovHelper.Services.Logging
{
    /// <summary>
    /// 오래된 로그 파일 정리 서비스
    /// </summary>
    public class LogCleanupService
    {
        private static readonly ILogger _log = Log.For<LogCleanupService>();

        private readonly string _logDirectory;
        private readonly int _maxDays;
        private readonly long _maxSizeBytes;

        /// <summary>
        /// 로그 정리 서비스 생성
        /// </summary>
        /// <param name="logDirectory">로그 디렉토리 경로</param>
        /// <param name="maxDays">최대 보관 일수 (기본 7일)</param>
        /// <param name="maxSizeMB">최대 폴더 크기 MB (기본 100MB)</param>
        public LogCleanupService(string logDirectory, int maxDays = 7, int maxSizeMB = 100)
        {
            _logDirectory = logDirectory;
            _maxDays = maxDays;
            _maxSizeBytes = (long)maxSizeMB * 1024 * 1024;
        }

        /// <summary>
        /// 설정에서 값을 로드하여 정리 서비스 생성
        /// </summary>
        public static LogCleanupService CreateFromSettings()
        {
            var logDirectory = LoggingService.Instance.LogDirectory;
            var maxDays = 7;
            var maxSizeMB = 100;

            try
            {
                var maxDaysStr = SettingsService.Instance.GetValue("logging.maxDays", "7");
                if (int.TryParse(maxDaysStr, out var days) && days > 0)
                {
                    maxDays = days;
                }

                var maxSizeStr = SettingsService.Instance.GetValue("logging.maxSizeMB", "100");
                if (int.TryParse(maxSizeStr, out var size) && size > 0)
                {
                    maxSizeMB = size;
                }
            }
            catch (Exception ex)
            {
                _log.Warning($"Failed to load cleanup settings: {ex.Message}");
            }

            return new LogCleanupService(logDirectory, maxDays, maxSizeMB);
        }

        /// <summary>
        /// 비동기 정리 실행
        /// </summary>
        public async Task CleanupAsync()
        {
            await Task.Run(Cleanup);
        }

        /// <summary>
        /// 로그 정리 실행
        /// </summary>
        public void Cleanup()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    return;
                }

                _log.Info($"Starting log cleanup. MaxDays: {_maxDays}, MaxSize: {_maxSizeBytes / (1024 * 1024)}MB");

                // 1. 날짜 기준 정리
                CleanupByDate();

                // 2. 크기 기준 정리
                CleanupBySize();

                _log.Info("Log cleanup completed");
            }
            catch (Exception ex)
            {
                _log.Error($"Log cleanup failed: {ex.Message}", ex);
            }
        }

        private void CleanupByDate()
        {
            var cutoffDate = DateTime.Now.AddDays(-_maxDays);

            var directories = Directory.GetDirectories(_logDirectory)
                .Select(d => new DirectoryInfo(d))
                .Where(d => TryParseFolderDate(d.Name, out var date) && date < cutoffDate)
                .ToList();

            foreach (var dir in directories)
            {
                try
                {
                    dir.Delete(true);
                    _log.Debug($"Deleted old log folder: {dir.Name}");
                }
                catch (Exception ex)
                {
                    _log.Warning($"Failed to delete log folder {dir.Name}: {ex.Message}");
                }
            }

            if (directories.Count > 0)
            {
                _log.Info($"Cleaned up {directories.Count} old log folders");
            }
        }

        private void CleanupBySize()
        {
            var directories = Directory.GetDirectories(_logDirectory)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.CreationTime)
                .ToList();

            long totalSize = 0;
            var foldersToDelete = new System.Collections.Generic.List<DirectoryInfo>();

            foreach (var dir in directories)
            {
                var dirSize = GetDirectorySize(dir);
                totalSize += dirSize;

                if (totalSize > _maxSizeBytes)
                {
                    foldersToDelete.Add(dir);
                }
            }

            foreach (var dir in foldersToDelete)
            {
                try
                {
                    dir.Delete(true);
                    _log.Debug($"Deleted log folder (size limit): {dir.Name}");
                }
                catch (Exception ex)
                {
                    _log.Warning($"Failed to delete log folder {dir.Name}: {ex.Message}");
                }
            }

            if (foldersToDelete.Count > 0)
            {
                _log.Info($"Cleaned up {foldersToDelete.Count} log folders due to size limit");
            }
        }

        private static bool TryParseFolderDate(string folderName, out DateTime date)
        {
            date = DateTime.MinValue;

            // 형식: yyyy-MM-dd-NNN
            var parts = folderName.Split('-');
            if (parts.Length >= 3)
            {
                var dateStr = $"{parts[0]}-{parts[1]}-{parts[2]}";
                return DateTime.TryParse(dateStr, out date);
            }

            return false;
        }

        private static long GetDirectorySize(DirectoryInfo dir)
        {
            try
            {
                return dir.GetFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }
    }
}
