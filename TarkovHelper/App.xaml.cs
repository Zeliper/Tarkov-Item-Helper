using System.IO;
using System.Reflection;
using System.Windows;
using AutoUpdaterDotNET;
using TarkovHelper.Services;
using TarkovHelper.Services.Logging;

namespace TarkovHelper
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly ILogger _log = Log.For<App>();

        private const string UpdateXmlUrl = "https://raw.githubusercontent.com/Zeliper/Tarkov-Item-Helper/main/update.xml";

        private static string DataDirectory => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Data"
        );

        private static string VersionFilePath => Path.Combine(DataDirectory, "app_version.txt");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 전역 예외 처리 - 에러 로그 파일에 기록
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled Exception:\n{ex}\n\nStack trace:\n{ex?.StackTrace}");
            };

            DispatcherUnhandledException += (s, args) =>
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dispatcher Exception:\n{args.Exception}\n\nStack trace:\n{args.Exception.StackTrace}");
                args.Handled = false; // 앱 종료 허용
            };

            // 버전 변경 시 캐시 데이터 초기화 (사용자 진행 상황은 유지)
            CheckAndRefreshDataOnVersionChange();

            // 폰트 크기 적용
            ApplyBaseFontSize(SettingsService.Instance.BaseFontSize);
            SettingsService.Instance.BaseFontSizeChanged += (_, size) => ApplyBaseFontSize(size);

            // Note: AutoUpdater is now managed by UpdateService in MainWindow
            // It will show update dialog only when user clicks "Update to vX.X.X" button
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _log.Info("Application shutting down...");

            try
            {
                // Stop background database updates
                DatabaseUpdateService.Instance.Dispose();
                _log.Debug("DatabaseUpdateService disposed");
            }
            catch (Exception ex)
            {
                _log.Error("Error disposing DatabaseUpdateService", ex);
            }

            try
            {
                // Dispose overlay service (closes overlay window and unsubscribes events)
                OverlayMiniMapService.Instance.Dispose();
                _log.Debug("OverlayMiniMapService disposed");
            }
            catch (Exception ex)
            {
                _log.Error("Error disposing OverlayMiniMapService", ex);
            }

            try
            {
                // Dispose keyboard hook (releases Windows hook)
                GlobalKeyboardHookService.Instance.IsEnabled = false;
                GlobalKeyboardHookService.Instance.Dispose();
                _log.Debug("GlobalKeyboardHookService disposed");
            }
            catch (Exception ex)
            {
                _log.Error("Error disposing GlobalKeyboardHookService", ex);
            }

            try
            {
                // Flush and dispose logging service
                LoggingService.Instance.Dispose();
            }
            catch
            {
                // Ignore logging errors during shutdown
            }

            base.OnExit(e);
        }

        /// <summary>
        /// Apply base font size to application resources
        /// </summary>
        public void ApplyBaseFontSize(double baseFontSize)
        {
            Resources["BaseFontSize"] = baseFontSize;
            Resources["FontSizeTiny"] = baseFontSize - 6;
            Resources["FontSizeXSmall"] = baseFontSize - 4;
            Resources["FontSizeSmall"] = baseFontSize - 2;
            Resources["FontSizeMedium"] = baseFontSize;
            Resources["FontSizeLarge"] = baseFontSize + 2;
            Resources["FontSizeXLarge"] = baseFontSize + 4;
            Resources["FontSizeTitle"] = baseFontSize + 6;
            Resources["FontSizeHeader"] = baseFontSize + 8;
        }

        /// <summary>
        /// 앱 버전이 변경되었으면 API 데이터 캐시를 삭제하여 새로 받도록 함
        /// progress.json, settings.json 등 사용자 데이터는 유지
        /// </summary>
        private void CheckAndRefreshDataOnVersionChange()
        {
            try
            {
                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
                var savedVersion = GetSavedVersion();

                if (savedVersion != currentVersion)
                {
                    // 버전이 다르면 캐시 데이터만 삭제 (사용자 데이터는 유지)
                    DeleteCacheDataFiles();
                    SaveCurrentVersion(currentVersion);
                }
            }
            catch
            {
                // 버전 체크 실패 시 무시하고 진행
            }
        }

        private string? GetSavedVersion()
        {
            if (!File.Exists(VersionFilePath))
                return null;

            return File.ReadAllText(VersionFilePath).Trim();
        }

        private void SaveCurrentVersion(string version)
        {
            if (!Directory.Exists(DataDirectory))
                Directory.CreateDirectory(DataDirectory);

            File.WriteAllText(VersionFilePath, version);
        }

        /// <summary>
        /// API에서 받은 캐시 데이터만 삭제
        /// tasks.json, items.json, hideout.json 등 캐시 데이터 삭제
        /// app_settings.json, quest_progress.json, hideout_progress.json 등 사용자 데이터는 유지
        /// </summary>
        private void DeleteCacheDataFiles()
        {
            var ignoreFiles = new[]
            {
                // 사용자 설정 파일
                Path.Combine(DataDirectory, "app_settings.json"),
                // 퀘스트/하이드아웃 진행 상황
                Path.Combine(DataDirectory, "quest_progress.json"),
                Path.Combine(DataDirectory, "hideout_progress.json"),
                // 레거시 파일 (하위 호환성)
                Path.Combine(DataDirectory, "progress.json"),
                Path.Combine(DataDirectory, "settings.json"),
                Path.Combine(DataDirectory, "game_settings.json")
            };

            foreach (var file in Directory.GetFiles(DataDirectory))
            {
                if (File.Exists(file) && !ignoreFiles.Contains(file))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // 삭제 실패 시 무시
                    }
                }
            }
        }
    }
}
