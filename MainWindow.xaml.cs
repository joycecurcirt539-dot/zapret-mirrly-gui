using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZapretMirrlyGUI.Pages;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI;

public sealed partial class MainWindow : Window
{
    private bool _isPaneExpandedByLogo;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData);

    [System.Runtime.InteropServices.DllImport("comctl32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, EntryPoint = "SetWindowSubclass")]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc callback, uint uIdSubclass, IntPtr dwRefData);

    [System.Runtime.InteropServices.DllImport("comctl32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, EntryPoint = "DefSubclassProc")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private SubclassProc? _subclassProc;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    private const uint WDA_NONE = 0x00000000; // Allow screen capture

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.Title = "Zapret Mirrly GUI";

        // Set custom App Window icon
        var assetsPath = AssetsExtractor.GetAssetsPath();
        var iconPath = System.IO.Path.Combine(assetsPath, "AppIcon.ico");
        if (!System.IO.File.Exists(iconPath))
        {
            iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        }
        
        if (System.IO.File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Set SidebarLogo image source
        var logoPath = System.IO.Path.Combine(assetsPath, "SidebarLogoNav.png");
        if (!System.IO.File.Exists(logoPath))
        {
            logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SidebarLogoNav.png");
        }
        if (System.IO.File.Exists(logoPath))
        {
            SidebarLogo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logoPath));
        }

        // Extend content into title bar to blend title bar with Mica background and remove borders
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Set window size to 1500x750
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 750));

        // Subclass window to set minimum size
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _subclassProc = new SubclassProc(WindowSubclassCallback);
        SetWindowSubclass(hWnd, _subclassProc, 1, IntPtr.Zero);

        // Explicitly allow screen capture (elevated process may default to WDA_EXCLUDEFROMCAPTURE)
        SetWindowDisplayAffinity(hWnd, WDA_NONE);

        // Initialize tray icon
        InitializeTrayIcon(hWnd);

        // Register window closing handler
        AppWindow.Closing += AppWindow_Closing;

        // Set startup page
        RootNavigationView.SelectedItem = DashboardItem;
        NavigateTo("dashboard");
    }

    private IntPtr WindowSubclassCallback(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == 0x0024) // WM_GETMINMAXINFO
        {
            var mmi = (MINMAXINFO)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(MINMAXINFO))!;
            mmi.ptMinTrackSize.x = 1500; // Min width
            mmi.ptMinTrackSize.y = 750;  // Min height
            System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero; // Handled
        }

        if (uMsg == WM_SIZE)
        {
            if (wParam.ToInt32() == SIZE_MINIMIZED)
            {
                if (SettingsManager.Instance.MinimizeToTrayOnMinimize)
                {
                    AppWindow.Hide();
                    return IntPtr.Zero; // Handled
                }
            }
        }

        if (uMsg == TRAY_CALLBACK_MSG)
        {
            int subMsg = lParam.ToInt32();
            if (subMsg == WM_LBUTTONUP || subMsg == WM_LBUTTONDBLCLK)
            {
                ShowTrayWindow(false);
                return IntPtr.Zero;
            }
            if (subMsg == WM_RBUTTONUP)
            {
                ShowTrayWindow(true);
                return IntPtr.Zero;
            }
        }

        if (uMsg == WM_COMMAND)
        {
            int commandId = wParam.ToInt32();
            if (commandId == 1001) // Open
            {
                RestoreWindow();
                return IntPtr.Zero;
            }
            if (commandId == 1002) // Exit
            {
                ExitAppPublic();
                return IntPtr.Zero;
            }
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void RootNavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag &&
            string.Equals(tag, "paneToggle", StringComparison.OrdinalIgnoreCase))
        {
            _isPaneExpandedByLogo = !_isPaneExpandedByLogo;
            RootNavigationView.IsPaneOpen = _isPaneExpandedByLogo;
        }
    }

    private void RootNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        NavigateTo(tag);
    }

    private void NavigateTo(string tag)
    {
        if (string.Equals(tag, "paneToggle", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetPage = tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "settings" => typeof(SettingsPage),
            "lists" => typeof(ListsPage),
            "logs" => typeof(LogsPage),
            "diagnostics" => typeof(DiagnosticsPage),
            "support" => typeof(SupportPage),
            "guide" => typeof(GuidePage),
            _ => typeof(DashboardPage)
        };

        if (RootFrame.CurrentSourcePageType != targetPage)
        {
            RootFrame.Navigate(targetPage);
        }
    }

    // --- Win32 System Tray & Window control API ---
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    private const int SW_RESTORE = 9;

    private const uint WM_SIZE = 0x0005;
    private const int SIZE_MINIMIZED = 1;
    private const int WM_APP = 0x8000;
    private const int TRAY_CALLBACK_MSG = WM_APP + 100;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_COMMAND = 0x0111;

    private NOTIFYICONDATA _nid;
    private bool _forceClose = false;
    private bool _isShowingCloseDialog = false;
    private TrayWindow? _trayWindow;

    private void InitializeTrayIcon(IntPtr hWnd)
    {
        var assetsPath = AssetsExtractor.GetAssetsPath();
        var iconPath = System.IO.Path.Combine(assetsPath, "AppIcon.ico");
        if (!System.IO.File.Exists(iconPath))
        {
            iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        }
        
        IntPtr hIcon = IntPtr.Zero;
        if (System.IO.File.Exists(iconPath))
        {
            hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
        }

        _nid = new NOTIFYICONDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hWnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = TRAY_CALLBACK_MSG,
            hIcon = hIcon,
            szTip = "Zapret Mirrly GUI"
        };

        Shell_NotifyIcon(NIM_ADD, ref _nid);
    }

    private void RemoveTrayIcon()
    {
        if (_nid.hWnd != IntPtr.Zero)
        {
            Shell_NotifyIcon(NIM_DELETE, ref _nid);
        }
    }

    private void RestoreWindow()
    {
        AppWindow.Show();
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        ShowWindow(hWnd, SW_RESTORE);
        SetForegroundWindow(hWnd);
    }

    private void ShowTrayContextMenu(IntPtr hWnd)
    {
        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        AppendMenu(hMenu, 0, (IntPtr)1001, "Открыть Zapret Mirrly GUI");
        AppendMenu(hMenu, 0, (IntPtr)1002, "Выход");

        POINT pt;
        GetCursorPos(out pt);

        SetForegroundWindow(hWnd);
        TrackPopupMenu(hMenu, 0, pt.x, pt.y, 0, hWnd, IntPtr.Zero);
        DestroyMenu(hMenu);
    }

    private async void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_forceClose)
        {
            RemoveTrayIcon();
            if (ZapretService.IsRunning)
            {
                ZapretService.StopBypass();
            }
            if (_trayWindow != null)
            {
                try { _trayWindow.Close(); } catch {}
            }
            System.Environment.Exit(0);
            return;
        }

        args.Cancel = true;

        bool isBypassActive = ZapretService.IsRunning;
        bool isDiagActive = ZapretService.IsDiagnosticsRunning;

        if (isBypassActive || isDiagActive)
        {
            if (_isShowingCloseDialog) return;
            _isShowingCloseDialog = true;

            var dialog = new ContentDialog
            {
                Title = "Внимание",
                XamlRoot = this.Content.XamlRoot
            };

            var panel = new StackPanel { Spacing = 12 };
            
            var descText = new TextBlock
            {
                Text = "В данный момент запущен активный обход DPI или тестирование. Закрытие программы остановит работу обхода.",
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(descText);

            var optTray = new RadioButton
            {
                Content = "Свернуть в системный трей (обход продолжит работать)",
                IsChecked = true
            };
            var optClose = new RadioButton
            {
                Content = "Полностью закрыть приложение (обход будет остановлен)"
            };
            panel.Children.Add(optTray);
            panel.Children.Add(optClose);

            var rememberCheckBox = new CheckBox
            {
                Content = "Запомнить выбор и больше не спрашивать",
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(rememberCheckBox);

            dialog.Content = panel;
            dialog.PrimaryButtonText = "ОК";
            dialog.CloseButtonText = "Отмена";

            var result = await dialog.ShowAsync();
            _isShowingCloseDialog = false;

            if (result == ContentDialogResult.Primary)
            {
                bool saveChoice = rememberCheckBox.IsChecked == true;
                if (optTray.IsChecked == true)
                {
                    if (saveChoice)
                    {
                        SettingsManager.Instance.MinimizeToTrayOnClose = true;
                        SettingsManager.Save();
                    }
                    AppWindow.Hide();
                }
                else
                {
                    if (saveChoice)
                    {
                        SettingsManager.Instance.MinimizeToTrayOnClose = false;
                        SettingsManager.Instance.AskBeforeClosing = false;
                        SettingsManager.Save();
                    }
                    _forceClose = true;
                    Close();
                }
            }
        }
        else
        {
            if (SettingsManager.Instance.MinimizeToTrayOnClose)
            {
                AppWindow.Hide();
            }
            else
            {
                _forceClose = true;
                Close();
            }
        }
    }

    private void ShowTrayWindow(bool isMenuMode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_trayWindow == null)
            {
                _trayWindow = new TrayWindow(this);
                _trayWindow.Closed += (s, e) => _trayWindow = null;
            }
            _trayWindow.ShowAndPosition(isMenuMode);
        });
    }

    public void RestoreWindowPublic()
    {
        RestoreWindow();
    }

    public void ExitAppPublic()
    {
        RemoveTrayIcon();
        if (ZapretService.IsRunning)
        {
            ZapretService.StopBypass();
        }
        if (_trayWindow != null)
        {
            try { _trayWindow.Close(); } catch {}
        }
        System.Environment.Exit(0);
    }
}
