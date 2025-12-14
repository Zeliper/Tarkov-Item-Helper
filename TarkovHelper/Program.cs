using TarkovHelper.Debug;
using TarkovHelper.Services;

namespace TarkovHelper;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Run migration before anything else
        MigrationService.RunMigrationIfNeeded();

        var app = new App();
        app.InitializeComponent();

        var mainWindow = new MainWindow();

        // Debug 모드에서 Toolbox 창 표시
        if (AppEnv.IsDebugMode)
        {
            TestMenu.MainWindow = mainWindow;
            var toolbox = new ToolboxWindow
            {

            };
            mainWindow.Loaded += (s, e) => {
                toolbox.Owner = mainWindow;
                toolbox.Show();
            };
        }

        app.Run(mainWindow);
    }
}
