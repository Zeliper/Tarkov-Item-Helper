using TarkovHelper.Debug;
using TarkovHelper.Services;

namespace TarkovHelper;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // CLI mode: --fetch-tasks
        if (args.Length > 0 && args[0] == "--fetch-tasks")
        {
            RunFetchTasksAsync().GetAwaiter().GetResult();
            return;
        }

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

    private static async Task RunFetchTasksAsync()
    {
        Console.WriteLine("Fetching tasks from tarkov.dev API...");
        var service = TarkovDataService.Instance;

        var (tasks, missingTasks) = await service.RefreshTasksDataAsync(message =>
        {
            Console.WriteLine($"  {message}");
        });

        Console.WriteLine();
        Console.WriteLine($"=== Results ===");
        Console.WriteLine($"Total matched tasks: {tasks.Count}");
        Console.WriteLine($"Missing tasks: {missingTasks.Count}");
        Console.WriteLine($"Kappa required: {tasks.Count(t => t.ReqKappa)}");
        Console.WriteLine($"With Korean translation: {tasks.Count(t => !string.IsNullOrEmpty(t.NameKo))}");
        Console.WriteLine($"With Japanese translation: {tasks.Count(t => !string.IsNullOrEmpty(t.NameJa))}");
        Console.WriteLine();
        Console.WriteLine("Sample tasks:");
        foreach (var task in tasks.Take(5))
        {
            Console.WriteLine($"  - {task.Name}");
            if (!string.IsNullOrEmpty(task.NameKo))
                Console.WriteLine($"    KO: {task.NameKo}");
            if (!string.IsNullOrEmpty(task.NameJa))
                Console.WriteLine($"    JA: {task.NameJa}");
            Console.WriteLine($"    Kappa: {task.ReqKappa}");
        }

        if (missingTasks.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"=== Missing Tasks (first 10) ===");
            foreach (var missing in missingTasks.Take(10))
            {
                Console.WriteLine($"  - {missing.Name}");
                Console.WriteLine($"    Reason: {missing.Reason}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Saved to {AppEnv.DataPath}/tasks.json");
        if (missingTasks.Count > 0)
            Console.WriteLine($"Missing tasks saved to {AppEnv.DataPath}/tasks_missing.json");
    }
}
