using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace TarkovHelper.Debug;

public partial class ToolboxWindow : Window
{
    public ToolboxWindow()
    {
        InitializeComponent();
        LoadTestMenuButtons();
    }

    private void LoadTestMenuButtons()
    {
        var methods = typeof(TestMenu)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<TestMenuAttribute>() != null)
            .OrderBy(m => m.Name);

        foreach (var method in methods)
        {
            var attr = method.GetCustomAttribute<TestMenuAttribute>()!;
            var displayName = attr.DisplayName ?? method.Name;

            var button = new Button
            {
                Content = displayName,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(8, 6, 8, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left
            };

            var methodInfo = method; // Capture for closure
            button.Click += (s, e) => InvokeTestMethod(methodInfo, displayName);

            ButtonPanel.Children.Add(button);
        }

        if (ButtonPanel.Children.Count == 0)
        {
            ButtonPanel.Children.Add(new TextBlock
            {
                Text = "No test functions found.\nAdd [TestMenu] attribute to methods in TestMenu.cs",
                Foreground = System.Windows.Media.Brushes.Gray,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private async void InvokeTestMethod(MethodInfo method, string displayName)
    {
        try
        {
            var result = method.Invoke(null, null);
            if (result is Task task)
            {
                await task;
            }
        }
        catch (Exception ex)
        {
            var innerEx = ex.InnerException ?? ex;
            MessageBox.Show(
                $"Error executing '{displayName}':\n\n{innerEx.Message}",
                "Test Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
