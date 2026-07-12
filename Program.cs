using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using System.Threading;

namespace ZapretMirrlyGUI;

public static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        bool skipAdminCheck = System.Linq.Enumerable.Contains(args, "--no-admin-check");
        if (!IsAdministrator() && !skipAdminCheck)
        {
            try
            {
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = string.Join(" ", args)
                };
                System.Diagnostics.Process.Start(processInfo);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled UAC prompt or failed to elevate
            }
            return;
        }

        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Local\ZapretMirrlyGUI.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return;
        }

        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        WinRT.ComWrappersSupport.InitializeComWrappers();

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            var app = new App();
        });
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
