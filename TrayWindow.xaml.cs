using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI
{
    public sealed partial class TrayWindow : Window
    {
        private readonly MainWindow _mainWindow;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
 
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
 
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int left;
            public int right;
            public int top;
            public int bottom;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        private const int SW_RESTORE = 9;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        public TrayWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            _mainWindow = mainWindow;

            // Get HWND and AppWindow
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
 
            // Extend frame into client area for transparency (removes black background corners under rounded edges)
            MARGINS margins = new MARGINS { left = -1, right = -1, top = -1, bottom = -1 };
            DwmExtendFrameIntoClientArea(hWnd, ref margins);

            // Configure titlebar transparency
            appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

            // Configure OverlappedPresenter to be borderless and top-most
            // Configure OverlappedPresenter to keep border (for Mica/composition backdrop) but hide title bar
            var overlappedPresenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (overlappedPresenter != null)
            {
                overlappedPresenter.IsResizable = false;
                overlappedPresenter.IsAlwaysOnTop = true;
                overlappedPresenter.SetBorderAndTitleBar(true, false); // hasBorder: true enables Mica
            }

            // DWM custom attributes: Set border color to blend with window background (removing the white line)
            // Color value format is BGR (Blue-Green-Red): 0x111010 corresponds to #101011
            int borderColor = 0x111010;
            DwmSetWindowAttribute(hWnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));


            // Force rounded corners (3 = DWMWCP_ROUND)
            int cornerPreference = 3;
            DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

            // Set version text
            TrayVersionText.Text = $"v{AppUpdateService.CurrentGuiVersion}";
            MenuVersionText.Text = $"Zapret Mirrly GUI v{AppUpdateService.CurrentGuiVersion}";

            // Sync connection status
            UpdateUiState();
            ZapretService.OnStatusChanged += ZapretService_OnStatusChanged;
            TgWsProxyService.OnStatusChanged += TgWsProxyService_OnStatusChanged;
 
            this.Activated += TrayWindow_Activated;
            this.Closed += TrayWindow_Closed;
        }
 
        private void ZapretService_OnStatusChanged(bool isRunning)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateUiState();
            });
        }

        private void TgWsProxyService_OnStatusChanged(bool isRunning)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateUiState();
            });
        }

        private void TrayWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                // Auto-hide when focus is lost
                AppWindow.Hide();
            }
        }

        private void TrayWindow_Closed(object sender, WindowEventArgs args)
        {
            ZapretService.OnStatusChanged -= ZapretService_OnStatusChanged;
            TgWsProxyService.OnStatusChanged -= TgWsProxyService_OnStatusChanged;
        }

        public void ShowAndPosition(bool isMenuMode)
        {
            // Toggle panel visibilities based on mode
            if (isMenuMode)
            {
                VpnPanel.Visibility = Visibility.Collapsed;
                MenuPanel.Visibility = Visibility.Visible;
 
                if (TgWsProxyService.IsRunning)
                {
                    MenuConnectTelegramButton.IsEnabled = true;
                    MenuConnectTelegramButton.Opacity = 1.0;
                }
                else
                {
                    MenuConnectTelegramButton.IsEnabled = false;
                    MenuConnectTelegramButton.Opacity = 0.5;
                }
            }
            else
            {
                VpnPanel.Visibility = Visibility.Visible;
                MenuPanel.Visibility = Visibility.Collapsed;
            }
 
            // Calculate size and placement on screen
            var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
            int screenWidth = displayArea.WorkArea.Width;
            int screenHeight = displayArea.WorkArea.Height;
 
            bool updateVisible = UpdateBadgeBorder.Visibility == Visibility.Visible;
            int windowWidth = isMenuMode ? 180 : 220;
            int windowHeight = isMenuMode ? (MenuUpdateButton.Visibility == Visibility.Visible ? 232 : 204) : (updateVisible ? 316 : 280);
 
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = dpi / 96.0;

            int physicalWidth = (int)Math.Round(windowWidth * scale);
            int physicalHeight = (int)Math.Round(windowHeight * scale);

            int posX, posY;
 
            if (isMenuMode)
            {
                // Position directly at cursor position for context menu feel
                POINT pt;
                GetCursorPos(out pt);
                posX = pt.x;
                posY = pt.y;
 
                // Bounds checks to prevent menu opening off-screen
                if (posX + physicalWidth > displayArea.WorkArea.X + screenWidth)
                    posX = displayArea.WorkArea.X + screenWidth - physicalWidth - (int)(8 * scale);
                if (posY + physicalHeight > displayArea.WorkArea.Y + screenHeight)
                    posY = displayArea.WorkArea.Y + screenHeight - physicalHeight - (int)(8 * scale);
            }
            else
            {
                // Position at bottom-right corner just above the taskbar
                posX = displayArea.WorkArea.X + screenWidth - physicalWidth - (int)(10 * scale);
                posY = displayArea.WorkArea.Y + screenHeight - physicalHeight - (int)(10 * scale);
            }
 
            // Move, resize, show and focus
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(posX, posY, physicalWidth, physicalHeight));
            AppWindow.Show();
 
            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
 
            UpdateUiState();
            UpdateTrayUpdateStatus();
        }

        private void UpdateUiState()
        {
            bool isZapretRunning = ZapretService.IsRunning;
            bool isTgRunning = TgWsProxyService.IsRunning;
 
            if (isZapretRunning)
            {
                PowerButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(30, 48, 204, 90));
                PowerButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90));
                PowerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90));
            }
            else
            {
                PowerButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(15, 255, 255, 255));
                PowerButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255));
                PowerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 255, 255));
            }

            if (isTgRunning)
            {
                TgPowerButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(30, 48, 204, 90));
                TgPowerButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90));
                TgPowerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90));
            }
            else
            {
                TgPowerButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(15, 255, 255, 255));
                TgPowerButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 255, 255));
                TgPowerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 255, 255));
            }
            bool anyRunning = isZapretRunning || isTgRunning;
            if (anyRunning)
            {
                ToggleAllTextBlock.Text = "Остановить всё";
                MenuToggleAllIcon.Glyph = "\uE10A"; // Stop glyph
                MenuToggleAllIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 69, 58)); // Red
                MenuToggleAllText.Text = "Остановить всё";
            }
            else
            {
                ToggleAllTextBlock.Text = "Запустить всё";
                MenuToggleAllIcon.Glyph = "\uE7E8"; // Start/Power glyph
                MenuToggleAllIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90)); // Green
                MenuToggleAllText.Text = "Запустить всё";
            }
 
            var activePreset = SettingsManager.Instance.LastSelectedPreset;
            ActivePresetNameText.Text = string.IsNullOrEmpty(activePreset) ? "general.bat" : activePreset;
        }

        public void UpdateTrayUpdateStatus()
        {
            var result = AppUpdateService.LastCheckResult;
            bool hasUpdate = result != null && result.UpdateAvailable;

            if (hasUpdate)
            {
                UpdateBadgeText.Text = $"Обновление {result!.LatestVersion}";
                UpdateBadgeBorder.Visibility = Visibility.Visible;
                UpdatePulseStoryboard.Begin();

                MenuUpdateButton.Visibility = Visibility.Visible;
                MenuUpdateText.Text = $"Обновить до {result.LatestVersion}";
            }
            else
            {
                UpdateBadgeBorder.Visibility = Visibility.Collapsed;
                UpdatePulseStoryboard.Stop();

                MenuUpdateButton.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateBadgeButton_Click(object sender, RoutedEventArgs e)
        {
            AppWindow.Hide();
            _mainWindow.RestoreWindowPublic();
            var result = AppUpdateService.LastCheckResult;
            if (result != null && result.UpdateAvailable)
            {
                _mainWindow.ShowUpdateModal(result);
            }
        }

        private void MenuUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            AppWindow.Hide();
            _mainWindow.RestoreWindowPublic();
            var result = AppUpdateService.LastCheckResult;
            if (result != null && result.UpdateAvailable)
            {
                _mainWindow.ShowUpdateModal(result);
            }
        }

        private void PowerButton_Click(object sender, RoutedEventArgs e)
        {
            if (ZapretService.IsRunning)
            {
                ZapretService.StopBypass();
            }
            else
            {
                var activePreset = SettingsManager.Instance.LastSelectedPreset;
                if (string.IsNullOrEmpty(activePreset)) activePreset = "general.bat";

                var root = ZapretService.FindZapretRoot();
                var gameFilterFile = System.IO.Path.Combine(root, "utils", "game_filter.enabled");
                string gameFilterMode = "disabled";
                try
                {
                    if (System.IO.File.Exists(gameFilterFile))
                        gameFilterMode = System.IO.File.ReadAllText(gameFilterFile).Trim().ToLower();
                }
                catch { }

                ZapretService.StartBypass(activePreset, gameFilterMode);
            }
            AppWindow.Hide(); // Hide menu or widget immediately after toggle action
        }

        private void TgPowerButton_Click(object sender, RoutedEventArgs e)
        {
            if (TgWsProxyService.IsRunning)
            {
                TgWsProxyService.StopProxy();
            }
            else
            {
                TgWsProxyService.StartProxy();
            }
            AppWindow.Hide(); // Hide menu or widget immediately after toggle action
        }

        private void HideWindowButton_Click(object sender, RoutedEventArgs e)
        {
            AppWindow.Hide();
        }

        private void OpenFullAppButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.RestoreWindowPublic();
            AppWindow.Hide();
        }

        private void ExitAppButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.ExitAppPublic();
            AppWindow.Hide();
        }

        private void ToggleAllButton_Click(object sender, RoutedEventArgs e)
        {
            bool isZapretRunning = ZapretService.IsRunning;
            bool isTgRunning = TgWsProxyService.IsRunning;
            bool anyRunning = isZapretRunning || isTgRunning;
 
            if (anyRunning)
            {
                if (isZapretRunning) ZapretService.StopBypass();
                if (isTgRunning) TgWsProxyService.StopProxy();
            }
            else
            {
                var activePreset = SettingsManager.Instance.LastSelectedPreset;
                if (string.IsNullOrEmpty(activePreset)) activePreset = "general.bat";
 
                var root = ZapretService.FindZapretRoot();
                var gameFilterFile = System.IO.Path.Combine(root, "utils", "game_filter.enabled");
                string gameFilterMode = "disabled";
                try
                {
                    if (System.IO.File.Exists(gameFilterFile))
                        gameFilterMode = System.IO.File.ReadAllText(gameFilterFile).Trim().ToLower();
                }
                catch { }
 
                ZapretService.StartBypass(activePreset, gameFilterMode);
                TgWsProxyService.StartProxy();
            }
            AppWindow.Hide();
        }

        private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.RestoreWindowPublic();
            _mainWindow.NavigateToPublic("settings");
            AppWindow.Hide();
        }

        private void ConnectTelegramButton_Click(object sender, RoutedEventArgs e)
        {
            var port = SettingsManager.Instance.TgWsProxyPort;
            var secret = SettingsManager.Instance.TgWsProxySecret;
            var url = $"https://t.me/proxy?server=127.0.0.1&port={port}&secret=ee{secret}";
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
            AppWindow.Hide();
        }
    }
}
