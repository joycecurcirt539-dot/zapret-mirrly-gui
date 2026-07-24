using System;
using System.IO;
using System.Threading.Tasks;
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

        // 1. Setup Global Crash Handler for Unhandled Exceptions
        UnhandledException += (s, e) =>
        {
            LogCrash("UnhandledException (WinUI)", e.Exception, e.Message);
            e.Handled = true; // Prevent app hard crashing if possible
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash("AppDomain.UnhandledException", ex, ex.Message);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            LogCrash("TaskScheduler.UnobservedTaskException", e.Exception, e.Exception.Message);
            e.SetObserved();
        };

        try
        {
            AssetsExtractor.ExtractEverythingIfNeeded();
        }
        catch (Exception ex)
        {
            LogCrash("AssetsExtractor", ex, ex.Message);
        }
    }

    public static void LogCrash(string source, Exception? ex, string customMessage = "")
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            string logsDir = Path.Combine(baseDir, "crash_reports");
            Directory.CreateDirectory(logsDir);

            string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string crashFile = Path.Combine(logsDir, $"crash_{timeStamp}.txt");
            string mainCrashLog = Path.Combine(baseDir, "crash.txt");

            string details = $"==========================================" + Environment.NewLine +
                             $"[CRASH REPORT] {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}" + Environment.NewLine +
                             $"Source: {source}" + Environment.NewLine +
                             $"Message: {customMessage}" + Environment.NewLine +
                             $"Exception: {ex?.GetType().FullName}: {ex?.Message}" + Environment.NewLine +
                             $"StackTrace:" + Environment.NewLine +
                             $"{ex?.StackTrace}" + Environment.NewLine +
                             $"==========================================" + Environment.NewLine;

            File.WriteAllText(crashFile, details);
            File.AppendAllText(mainCrashLog, details);
            System.Diagnostics.Debug.WriteLine($"[CRASH ERROR] {source}: {ex?.Message}");
        }
        catch
        {
            // Fallback
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
