using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TarkovHelper.Debug;
using TarkovHelper.Services;

namespace TarkovHelper;

public partial class MainWindow : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly WikiDataService _wikiService = WikiDataService.Instance;
    private bool _isLoading;

    // Windows API for dark title bar
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();
        _loc.LanguageChanged += OnLanguageChanged;

        // Apply dark title bar
        SourceInitialized += (s, e) => EnableDarkTitleBar();
    }

    private void EnableDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var useDarkMode = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
    }

    private void OnLanguageChanged(object? sender, AppLanguage e)
    {
        UpdateAllLocalizedText();
    }

    private void UpdateAllLocalizedText()
    {
        TxtWelcome.Text = _loc.Welcome;
    }

    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (CmbLanguage.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            _loc.CurrentLanguage = lang switch
            {
                "KO" => AppLanguage.KO,
                "JA" => AppLanguage.JA,
                _ => AppLanguage.EN
            };
        }
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoading = true;

        // Apply saved language setting to UI
        CmbLanguage.SelectedIndex = _loc.CurrentLanguage switch
        {
            AppLanguage.KO => 1,
            AppLanguage.JA => 2,
            _ => 0
        };

        UpdateAllLocalizedText();

        _isLoading = false;

        // Check if data needs to be refreshed (tasks.json doesn't exist)
        await CheckAndRefreshDataAsync();
    }

    /// <summary>
    /// Check if task data exists, if not run RefreshData automatically
    /// </summary>
    private async Task CheckAndRefreshDataAsync()
    {
        var tasksFilePath = Path.Combine(AppEnv.DataPath, "tasks.json");

        if (!File.Exists(tasksFilePath))
        {
            // Show loading message
            TxtWelcome.Text = "Loading data for first time...";

            try
            {
                var tarkovService = TarkovDataService.Instance;
                var result = await tarkovService.RefreshAllDataAsync(message =>
                {
                    // Update UI with progress (run on UI thread)
                    Dispatcher.Invoke(() =>
                    {
                        TxtWelcome.Text = message;
                    });
                });

                if (result.Success)
                {
                    TxtWelcome.Text = $"Data loaded: {result.TotalTasksMerged} tasks";
                }
                else
                {
                    TxtWelcome.Text = $"Failed to load data: {result.ErrorMessage}";
                    MessageBox.Show(
                        $"Failed to refresh data:\n{result.ErrorMessage}",
                        "Data Refresh Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                TxtWelcome.Text = "Failed to load data";
                MessageBox.Show(
                    $"Error refreshing data:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
