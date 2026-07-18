using Microsoft.UI.Xaml;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI;

public partial class App : Application
{
    private Window? _window;

    public MainWindow? MainWindowInstance => _window as MainWindow;

    public App()
    {
        InitializeComponent();
        
        try
        {
            AssetsExtractor.ExtractEverythingIfNeeded();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to extract embedded assets: {ex.Message}");
        }

        UnhandledException += (s, e) =>
        {
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "crash.txt"),
                    (e.Exception?.ToString() ?? "Unknown exception") + "\nMessage: " + e.Message);
            }
            catch {}
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
