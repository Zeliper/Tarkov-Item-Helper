using System.Windows;
using TarkovDBEditor.Services;

namespace TarkovDBEditor;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DatabaseService.Instance.Initialize();
    }
}
