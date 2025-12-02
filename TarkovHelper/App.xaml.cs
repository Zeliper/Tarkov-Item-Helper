using System.IO;
using System.Reflection;
using System.Windows;
using AutoUpdaterDotNET;

namespace TarkovHelper
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string UpdateXmlUrl = "https://raw.githubusercontent.com/Zeliper/Tarkov-Item-Helper/main/update.xml";

        private static string DataDirectory => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Data"
        );

        private static string VersionFilePath => Path.Combine(DataDirectory, "app_version.txt");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 버전 변경 시 캐시 데이터 초기화 (사용자 진행 상황은 유지)
            CheckAndRefreshDataOnVersionChange();

            // AutoUpdater 설정
            AutoUpdater.InstalledVersion = Assembly.GetExecutingAssembly().GetName().Version;
            AutoUpdater.ShowSkipButton = true;
            AutoUpdater.ShowRemindLaterButton = true;
            AutoUpdater.LetUserSelectRemindLater = true;
            AutoUpdater.RemindLaterTimeSpan = RemindLaterFormat.Days;
            AutoUpdater.RemindLaterAt = 1;

            // 업데이트 체크 시작
            AutoUpdater.Start(UpdateXmlUrl);
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
        /// tasks.json, items.json, hideouts.json 삭제
        /// progress.json, settings.json, game_settings.json 등 사용자 데이터는 유지
        /// </summary>
        private void DeleteCacheDataFiles()
        {
            var ignoreFiles = new[]
            {
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
