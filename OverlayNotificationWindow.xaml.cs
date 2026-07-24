using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI;

public sealed partial class OverlayNotificationWindow : Window
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int left;
        public int right;
        public int top;
        public int bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, uint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static OverlayNotificationWindow? _instance;
    private DispatcherTimer? _hideTimer;
    private bool _isHiding = false;
    private SUBCLASSPROC? _subclassCallback;

    public OverlayNotificationWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);

        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        // Subclass window to override WM_GETMINMAXINFO and allow ultra-compact width (< 136px)
        _subclassCallback = new SUBCLASSPROC(SubclassCallback);
        SetWindowSubclass(hWnd, _subclassCallback, 101, IntPtr.Zero);

        // Remove titlebar buttons
        appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        // Extend frame into client area for native DWM Acrylic transparency
        MARGINS margins = new MARGINS { left = -1, right = -1, top = -1, bottom = -1 };
        DwmExtendFrameIntoClientArea(hWnd, ref margins);

        // Dark mode and dark border
        int borderColor = 0x00141416;
        DwmSetWindowAttribute(hWnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        int darkMode = 1;
        DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        int cornerPref = 3; // DWMWCP_ROUND
        DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(true, false);
        }

        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(3500)
        };
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            StartHideAnimation();
        };

        HideStoryboard.Completed += (s, e) =>
        {
            try
            {
                appWindow.Hide();
            }
            catch { }
            _isHiding = false;
        };
    }

    private IntPtr SubclassCallback(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == 0x0024) // WM_GETMINMAXINFO
        {
            var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
            mmi.ptMinTrackSize.x = 40; // Allow width down to 40px!
            mmi.ptMinTrackSize.y = 40; // Allow height down to 40px!
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero; // Handled
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    public static void ShowToast(bool dpiRunning, bool tgRunning, string subtitle = "")
    {
        ShowHotKeyToast(dpiRunning, tgRunning);
    }

    public static async void ShowHotKeyToast(bool dpiRunning, bool tgRunning)
    {
        try
        {
            if (_instance == null)
            {
                _instance = new OverlayNotificationWindow();
            }

            // Display initial state
            _instance.DisplayStatus(dpiRunning, tgRunning, dpiRunning ? "..." : "--", tgRunning ? "..." : "--");

            // Measure real ping latency asynchronously
            string dpiPingText = "--";
            if (dpiRunning)
            {
                var dpiProbe = await NetworkLatencyService.MeasureHttpSniLatencyAsync("YouTube", "https://www.youtube.com/generate_204", 1200);
                dpiPingText = dpiProbe.IsSuccess ? dpiProbe.FormattedText : "--";
            }

            string tgPingText = "--";
            if (tgRunning)
            {
                int tgPort = SettingsManager.Instance.TgWsProxyPort;
                var tgProbe = await NetworkLatencyService.MeasureTgProxyDcLatencyAsync("DC2", tgPort, 1000);
                tgPingText = tgProbe.IsSuccess ? tgProbe.FormattedText : "--";
            }

            _instance.DisplayStatus(dpiRunning, tgRunning, dpiPingText, tgPingText);
        }
        catch (Exception ex)
        {
            App.LogCrash("OverlayNotificationWindow.ShowHotKeyToast", ex);
        }
    }

    public static void ShowConnectingToast(string message = "Переподключение...")
    {
        try
        {
            if (_instance == null)
            {
                _instance = new OverlayNotificationWindow();
            }

            _instance.DisplayConnecting();
        }
        catch (Exception ex)
        {
            App.LogCrash("OverlayNotificationWindow.ShowConnectingToast", ex);
        }
    }

    public static void ShowNetworkToast(bool dpiSuccess, bool tgSuccess, string dpiPingText = "", string tgPingText = "")
    {
        try
        {
            if (_instance == null)
            {
                _instance = new OverlayNotificationWindow();
            }

            _instance.DisplayStatus(dpiSuccess, tgSuccess, dpiPingText, tgPingText);
        }
        catch (Exception ex)
        {
            App.LogCrash("OverlayNotificationWindow.ShowNetworkToast", ex);
        }
    }

    private void DisplayConnecting()
    {
        StatusPanel.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Visible;
        ConnectingRing.IsActive = true;

        SetWindowPositionAndShow(46, 46);
    }

    private void DisplayStatus(bool dpiActive, bool tgActive, string dpiPingText, string tgPingText)
    {
        var greenBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 245, 212));
        var redBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 69, 58));

        StatusPanel.Visibility = Visibility.Visible;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        ConnectingRing.IsActive = false;

        // DPI Status & Ping
        DpiIconPath.Fill = dpiActive ? greenBrush : redBrush;
        DpiPingText.Text = dpiActive ? (string.IsNullOrEmpty(dpiPingText) ? "--" : dpiPingText) : "--";
        DpiPingText.Foreground = dpiActive ? greenBrush : redBrush;

        // TG Status & Ping
        TgIconPath.Fill = tgActive ? greenBrush : redBrush;
        TgPingText.Text = tgActive ? (string.IsNullOrEmpty(tgPingText) ? "--" : tgPingText) : "--";
        TgPingText.Foreground = tgActive ? greenBrush : redBrush;

        // Calculate dynamic tight width based on text lengths
        int width = 78;
        if (DpiPingText.Text.Length > 5 || TgPingText.Text.Length > 5)
        {
            width = 86;
        }

        SetWindowPositionAndShow(width, 44);
    }

    private void SetWindowPositionAndShow(int width, int height)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new SizeInt32(width, height));

        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int posX = Math.Max(10, screenWidth - width - 24);
        int posY = 32;
        appWindow.Move(new PointInt32(posX, posY));

        _hideTimer?.Stop();
        _isHiding = false;

        Activate();

        ShowStoryboard.Begin();
        _hideTimer?.Start();
    }

    private void StartHideAnimation()
    {
        if (_isHiding) return;
        _isHiding = true;
        HideStoryboard.Begin();
    }
}
