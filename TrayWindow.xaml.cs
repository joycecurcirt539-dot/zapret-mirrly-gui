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

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int SW_RESTORE = 9;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        public TrayWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            // Get HWND and AppWindow
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

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
            UpdateUiState(ZapretService.IsRunning);
            ZapretService.OnStatusChanged += ZapretService_OnStatusChanged;

            this.Activated += TrayWindow_Activated;
            this.Closed += TrayWindow_Closed;
        }

        private void ZapretService_OnStatusChanged(bool isRunning)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateUiState(isRunning);
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
        }

        public void ShowAndPosition(bool isMenuMode)
        {
            // Toggle panel visibilities based on mode
            if (isMenuMode)
            {
                VpnPanel.Visibility = Visibility.Collapsed;
                MenuPanel.Visibility = Visibility.Visible;

                // Sync status texts in MenuPanel
                if (ZapretService.IsRunning)
                {
                    MenuPowerIcon.Glyph = "\uE10A"; // Stop glyph
                    MenuPowerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 69, 58)); // Red
                    MenuPowerText.Text = "Остановить обход";
                }
                else
                {
                    MenuPowerIcon.Glyph = "\uE7E8"; // Start/Power glyph
                    MenuPowerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90)); // Green
                    MenuPowerText.Text = "Запустить обход";
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

            // Dynamically size the VPN panel — taller if update badge is visible
            bool updateVisible = UpdateBadgeBorder.Visibility == Visibility.Visible;
            int windowWidth = isMenuMode ? 180 : 220;
            int windowHeight = isMenuMode ? (MenuUpdateButton.Visibility == Visibility.Visible ? 140 : 118) : (updateVisible ? 282 : 246);

            int posX, posY;

            if (isMenuMode)
            {
                // Position directly at cursor position for context menu feel
                POINT pt;
                GetCursorPos(out pt);
                posX = pt.x;
                posY = pt.y;

                // Bounds checks to prevent menu opening off-screen
                if (posX + windowWidth > displayArea.WorkArea.X + screenWidth)
                    posX = displayArea.WorkArea.X + screenWidth - windowWidth - 8;
                if (posY + windowHeight > displayArea.WorkArea.Y + screenHeight)
                    posY = displayArea.WorkArea.Y + screenHeight - windowHeight - 8;
            }
            else
            {
                // Position at bottom-right corner just above the taskbar
                posX = displayArea.WorkArea.X + screenWidth - windowWidth - 10;
                posY = displayArea.WorkArea.Y + screenHeight - windowHeight - 10;
            }

            // Move, resize, show and focus
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(posX, posY, windowWidth, windowHeight));
            AppWindow.Show();

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);

            UpdateUiState(ZapretService.IsRunning);
            UpdateTrayUpdateStatus();
        }

        private void UpdateUiState(bool isRunning)
        {
            if (isRunning)
            {
                PowerButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(26, 48, 204, 90));
                PowerButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90));
                PowerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90));
                StatusTextBlock.Text = "ПОДКЛЮЧЕНО";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 204, 90));
                StatusSubTextBlock.Text = "DPI обход активен";
            }
            else
            {
                PowerButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(26, 255, 69, 58));
                PowerButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 69, 58));
                PowerIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 69, 58));
                StatusTextBlock.Text = "ОТКЛЮЧЕНО";
                StatusTextBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 69, 58));
                StatusSubTextBlock.Text = "Трафик идет напрямую";
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
    }
}
