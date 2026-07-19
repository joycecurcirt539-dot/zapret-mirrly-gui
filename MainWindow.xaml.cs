using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
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
        ApplyThemeSettings();
        ApplyBackdropSettings();

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

        // Set up hide update storyboard completed event
        HideUpdateOverlayStoryboard.Completed += (s, e) =>
        {
            UpdateNotificationOverlay.Visibility = Visibility.Collapsed;
        };

        // Hook update sidebar item tap
        UpdateStatusItem.Tapped += (s, e) =>
        {
            UpdateStatusItem_Tapped();
        };

        // Check for updates asynchronously
        DispatcherQueue.TryEnqueue(async () =>
        {
            await CheckForGUIUpdatesOnStartupAsync();
            if (SettingsManager.Instance.AutoUpdateDatabase)
            {
                await Task.Run(async () =>
                {
                    try { await ZapretService.UpdateIpsetListAsync(); } catch {}
                });
            }
        });
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

    private void NavigateTo(string tag, object? parameter = null)
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
            "tgwsproxy" => typeof(TgWsProxyPage),
            "support" => typeof(SupportPage),
            "guide" => typeof(GuidePage),
            _ => typeof(DashboardPage)
        };

        if (RootFrame.CurrentSourcePageType != targetPage)
        {
            RootFrame.Navigate(targetPage, parameter);
        }
        else if (parameter != null && RootFrame.Content is Page page)
        {
            if (page is DiagnosticsPage diagPage && parameter.ToString() == "start")
            {
                diagPage.TriggerAutoStart();
            }
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
            if (TgWsProxyService.IsRunning)
            {
                TgWsProxyService.StopProxy();
            }
            if (_trayWindow != null)
            {
                try { _trayWindow.Close(); } catch {}
            }
            System.Environment.Exit(0);
            return;
        }

        args.Cancel = true;

        bool isBypassActive = ZapretService.IsRunning || TgWsProxyService.IsRunning;
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

    public void NavigateToPublic(string tag, object? parameter = null)
    {
        NavigateTo(tag, parameter);
    }

    public void ExitAppPublic()
    {
        RemoveTrayIcon();
        if (ZapretService.IsRunning)
        {
            ZapretService.StopBypass();
        }
        if (TgWsProxyService.IsRunning)
        {
            TgWsProxyService.StopProxy();
        }
        if (_trayWindow != null)
        {
            try { _trayWindow.Close(); } catch {}
        }
        System.Environment.Exit(0);
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // GUI wrapper update checker and notification handlers
    // ────────────────────────────────────────────────────────────────────────────────
    private string _currentDownloadUrl = "";
    private string _latestVersionTag = "";

    private async System.Threading.Tasks.Task CheckForGUIUpdatesOnStartupAsync()
    {
        var result = await AppUpdateService.CheckForGuiUpdatesAsync();
        
        UpdateSidebarStatus(result);

        // Sync tray update indicator if tray window exists
        _trayWindow?.UpdateTrayUpdateStatus();

        if (!SettingsManager.Instance.AutoCheckGuiUpdates)
            return;

        if (result.UpdateAvailable)
        {
            // Check if skipped version matches, and if less than 7 days have passed since skipped
            if (SettingsManager.Instance.SkippedGuiVersion == result.LatestVersion)
            {
                var timePassed = DateTime.UtcNow - SettingsManager.Instance.SkippedGuiVersionTime;
                if (timePassed.TotalDays < 7)
                {
                    // Within 7 days window, skip showing the popup on startup
                    return;
                }
            }

            ShowUpdateModal(result);
        }
    }

    public void UpdateSidebarStatus(GuiUpdateResult result)
    {
        if (result == null) return;

        if (result.UpdateAvailable)
        {
            // Update icon and colors
            UpdateStatusIcon.Glyph = "\uE896"; // Update arrow glyph
            UpdateStatusIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 159, 28)); // Gold/orange

            UpdateStatusItem.Content = "Доступно обновление";
            // UpdateInfoBadge.Visibility = Visibility.Visible;

            // ToolTip updates
            UpdateTooltipIcon.Glyph = "\uE896";
            UpdateTooltipIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 159, 28));
            UpdateTooltipTitle.Text = "Доступна новая версия!";
            UpdateTooltipDesc.Text = $"Версия {result.LatestVersion} ({(result.IsPrerelease ? "Эксперимент" : "Релиз")}) готова к установке. Нажмите на этот пункт меню, чтобы просмотреть подробности.";
        }
        else
        {
            // No updates
            UpdateStatusIcon.Glyph = "\uE930"; // Checkmark glyph
            UpdateStatusIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 138, 138, 143)); // Gray

            UpdateStatusItem.Content = "Версия актуальна";
            // UpdateInfoBadge.Visibility = Visibility.Collapsed;

            // ToolTip updates
            UpdateTooltipIcon.Glyph = "\uE930";
            UpdateTooltipIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 245, 212)); // Green/teal
            UpdateTooltipTitle.Text = "Приложение обновлено";
            UpdateTooltipDesc.Text = $"Установлена последняя версия Zapret Mirrly GUI (v{AppUpdateService.CurrentGuiVersion}).";
        }
    }

    private void UpdateStatusItem_Tapped()
    {
        var result = AppUpdateService.LastCheckResult;
        if (result != null && result.UpdateAvailable)
        {
            ShowUpdateModal(result);
        }
        else
        {
            ShowCurrentVersionModal();
        }
    }

    public void ShowUpdateModal(GuiUpdateResult update)
    {
        ShowVersionModal(
            versionTag: update.LatestVersion,
            title: "Доступно обновление!",
            changelog: update.Changelog ?? "",
            isPrerelease: update.IsPrerelease,
            isNewUpdate: true,
            downloadUrl: update.DownloadUrl
        );
    }

    public void ShowCurrentVersionModal()
    {
        var currentChangelog = @"### Zapret Mirrly GUI v1.1.2 — Что нового в этом обновлении:

• **Полноценная система тем**:
  - **Чёрный графит**: глубокий стиль с высокой контрастностью и прозрачными плашками элементов.
  - **Светлая тема**: чистый, контрастный дизайн в стиле Apple Light Glass.
  - **Тёмная тема**: классический графитовый стиль.

• **Оптический стеклянный эффект (Acrylic & Mica)**:
  - Прямая интеграция с Desktop Window Manager (DWM) и `DWMWA_USE_IMMERSIVE_DARK_MODE`.
  - Чистое аппаратное размытие без лагов и визуальных артефактов.

• **Универсальный диагностический модуль**:
  - Новый движок тестирования сетевой подсистемы (`test zapret.ps1`) с полной совместимостью любых версий Zapret.

• **Автоматическое обучение списка (Auto Hostlist)**:
  - Динамическое автопополнение `autohostlist.txt` заблокированными ресурсами во время веб-серфинга.

• **Фильтрация по IPv4 / IPv6**:
  - Селектор протоколов перехвата сетевого трафика.

• **Улучшения трея и интерфейса**:
  - Обновленный стеклянный виджет трея по ЛКМ и контекстное меню по ПКМ.
  - Синхронизация прозрачности и цвета текстов с выбранной темой.
  - Раздел настроек с вкладками «Поддержать автора», «Справка» и «Обновления».";

        ShowVersionModal(
            versionTag: AppUpdateService.CurrentGuiVersion,
            title: $"Что нового в версии v{AppUpdateService.CurrentGuiVersion}!",
            changelog: currentChangelog,
            isPrerelease: false,
            isNewUpdate: false,
            downloadUrl: null
        );
    }

    public void ShowVersionModal(string versionTag, string title, string changelog, bool isPrerelease, bool isNewUpdate, string? downloadUrl)
    {
        _currentDownloadUrl = downloadUrl;
        _latestVersionTag = versionTag;

        if (UpdateModalTitleText != null) UpdateModalTitleText.Text = title;
        if (UpdateTagNameText != null) UpdateTagNameText.Text = $"Версия {versionTag}";
        PopulateRichTextBlockWithMarkdown(UpdateChangelogText, changelog);

        if (isPrerelease)
        {
            if (UpdateStableBadge != null) UpdateStableBadge.Visibility = Visibility.Collapsed;
            if (UpdatePrereleaseBadge != null) UpdatePrereleaseBadge.Visibility = Visibility.Visible;
        }
        else
        {
            if (UpdateStableBadge != null) UpdateStableBadge.Visibility = Visibility.Visible;
            if (UpdatePrereleaseBadge != null) UpdatePrereleaseBadge.Visibility = Visibility.Collapsed;
        }

        if (isNewUpdate)
        {
            if (InstallUpdateButton != null) InstallUpdateButton.Visibility = Visibility.Visible;
            if (SkipVersionButton != null) SkipVersionButton.Visibility = Visibility.Visible;
            if (CloseUpdateOverlayButtonText != null) CloseUpdateOverlayButtonText.Text = "Позже";
        }
        else
        {
            if (InstallUpdateButton != null) InstallUpdateButton.Visibility = Visibility.Collapsed;
            if (SkipVersionButton != null) SkipVersionButton.Visibility = Visibility.Collapsed;
            if (CloseUpdateOverlayButtonText != null) CloseUpdateOverlayButtonText.Text = "Отлично";
        }

        // Dynamic Highlights Parsing (Anti-Clickbait)
        string changelogLower = (changelog ?? "").ToLower();
        bool hasFeatures = changelogLower.Contains("добавлен") || changelogLower.Contains("новое") || changelogLower.Contains("добавил") || changelogLower.Contains("feature") || changelogLower.Contains("added") || changelogLower.Contains("реализован") || changelogLower.Contains("поддерж") || changelogLower.Contains("тем");
        bool hasSpeed = changelogLower.Contains("скорост") || changelogLower.Contains("быстро") || changelogLower.Contains("ускор") || changelogLower.Contains("оптимизац") || changelogLower.Contains("производител") || changelogLower.Contains("speed") || changelogLower.Contains("performance") || changelogLower.Contains("fast") || changelogLower.Contains("улучшен сетев") || changelogLower.Contains("размытие");
        bool hasFixes = changelogLower.Contains("исправлен") || changelogLower.Contains("ошибк") || changelogLower.Contains("баг") || changelogLower.Contains("конфликт") || changelogLower.Contains("вылет") || changelogLower.Contains("краш") || changelogLower.Contains("fix") || changelogLower.Contains("bug") || changelogLower.Contains("error") || changelogLower.Contains("зависа");

        // Fallback: if nothing matched, set stability to true
        if (!hasFeatures && !hasSpeed && !hasFixes)
        {
            hasFixes = true;
        }

        // 1. Features
        if (hasFeatures)
        {
            FeatureHighlightRow.Opacity = 1.0;
            FeatureHighlightIconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 44, 41, 37)); // active gold
            FeatureHighlightTitle.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 221, 213)); // #E2DDD5
            FeatureHighlightDesc.Text = "Добавлены новые возможности";
            FeatureHighlightBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 61, 58, 53)); // #3D3A35
            FeatureHighlightBadgeText.Text = "НОВОЕ";
            FeatureHighlightBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 215, 0)); // Gold
        }
        else
        {
            FeatureHighlightRow.Opacity = 0.35;
            FeatureHighlightIconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 23, 21)); // inactive
            FeatureHighlightTitle.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 155, 146)); // muted #A09B92
            FeatureHighlightDesc.Text = "В этом релизе без новых функций";
            FeatureHighlightBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 27, 26, 25)); // dark grey
            FeatureHighlightBadgeText.Text = "НЕТ";
            FeatureHighlightBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 115, 110)); // dark muted grey
        }

        // 2. Speed
        if (hasSpeed)
        {
            SpeedHighlightRow.Opacity = 1.0;
            SpeedHighlightIconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 40, 42)); // active cyan
            SpeedHighlightTitle.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 221, 213));
            SpeedHighlightDesc.Text = "Оптимизация и быстрый обход";
            SpeedHighlightBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 29, 42, 43)); // #1D2A2B
            SpeedHighlightBadgeText.Text = "УСКОРЕНО";
            SpeedHighlightBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 245, 212)); // Cyan
        }
        else
        {
            SpeedHighlightRow.Opacity = 0.35;
            SpeedHighlightIconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 21, 22, 23));
            SpeedHighlightTitle.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 155, 146));
            SpeedHighlightDesc.Text = "Показатели скорости без изменений";
            SpeedHighlightBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 27, 26, 25));
            SpeedHighlightBadgeText.Text = "НЕТ";
            SpeedHighlightBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 115, 110));
        }

        // 3. Stability
        if (hasFixes)
        {
            StabilityHighlightRow.Opacity = 1.0;
            StabilityHighlightIconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 43, 36, 37)); // active orange/amber
            StabilityHighlightTitle.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 226, 221, 213));
            StabilityHighlightDesc.Text = "Исправлены системные ошибки";
            StabilityHighlightBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 52, 35, 35)); // #342323
            StabilityHighlightBadgeText.Text = "АКТИВНО";
            StabilityHighlightBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 159, 28)); // Amber
        }
        else
        {
            StabilityHighlightRow.Opacity = 0.35;
            StabilityHighlightIconBorder.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 22, 22));
            StabilityHighlightTitle.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 160, 155, 146));
            StabilityHighlightDesc.Text = "Критических багов не обнаружено";
            StabilityHighlightBadge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 27, 26, 25));
            StabilityHighlightBadgeText.Text = "НЕТ";
            StabilityHighlightBadgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 115, 110));
        }

        // Hide the subtitle header as requested ("не пиши крупный релиз")
        SidebarSubtitleText.Visibility = Visibility.Collapsed;

        UpdateNotificationOverlay.Visibility = Visibility.Visible;
        ShowUpdateOverlayStoryboard.Begin();
        ScanlineStoryboard.Begin(); // Start the scanline sweep loop
    }

    private void HideUpdateModal()
    {
        HideUpdateOverlayStoryboard.Begin();
        ScanlineStoryboard.Stop(); // Stop scanline sweep loop
    }

    private void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentDownloadUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _currentDownloadUrl,
                    UseShellExecute = true
                });
            }
            catch {}
        }
        HideUpdateModal();
    }

    private void SkipVersion_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_latestVersionTag))
        {
            SettingsManager.Instance.SkippedGuiVersion = _latestVersionTag;
            SettingsManager.Instance.SkippedGuiVersionTime = DateTime.UtcNow;
            SettingsManager.Save();
        }
        HideUpdateModal();
    }

    private void CloseUpdateOverlay_Click(object sender, RoutedEventArgs e)
    {
        HideUpdateModal();
    }
    private void PopulateRichTextBlockWithMarkdown(RichTextBlock rtb, string markdown)
    {
        rtb.Blocks.Clear();
        if (string.IsNullOrEmpty(markdown)) return;

        var lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Paragraph? currentParagraph = null;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // 0. Blockquotes (e.g. > Quote text)
            if (trimmedLine.StartsWith(">"))
            {
                string quoteText = trimmedLine.TrimStart('>').Trim();
                var quoteParagraph = new Paragraph { Margin = new Thickness(16, 8, 0, 8) };
                
                var quoteBar = new Run 
                { 
                    Text = "┃  ", 
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold, 
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 154, 131)) // #A79A83
                };
                quoteParagraph.Inlines.Add(quoteBar);

                var quoteContent = new Italic { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(180, 226, 221, 213)) };
                ParseInlineMarkdown(quoteContent.Inlines, quoteText);
                
                quoteParagraph.Inlines.Add(quoteContent);
                rtb.Blocks.Add(quoteParagraph);
                currentParagraph = null;
                continue;
            }

            // 0b. Sub-bullet list chevrons (e.g. - > Subitem or * > Subitem)
            if (trimmedLine.StartsWith("- >") || trimmedLine.StartsWith("* >") || trimmedLine.StartsWith("-&gt;") || trimmedLine.StartsWith("*&gt;"))
            {
                string itemText = trimmedLine.Substring(trimmedLine.Contains("&gt;") ? trimmedLine.IndexOf("&gt;") + 4 : 3).Trim();
                var listParagraph = new Paragraph { Margin = new Thickness(32, 4, 0, 4) }; // indented further
                
                // Add right chevron
                var chevronRun = new Run 
                { 
                    Text = "›  ", 
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold, 
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 154, 131)) // #A79A83
                };
                listParagraph.Inlines.Add(chevronRun);

                var contentItalic = new Italic { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(220, 226, 221, 213)) };
                ParseInlineMarkdown(contentItalic.Inlines, itemText);
                listParagraph.Inlines.Add(contentItalic);

                rtb.Blocks.Add(listParagraph);
                currentParagraph = null;
                continue;
            }

            // 1. Headers (e.g. ### Header or ## Header)
            if (trimmedLine.StartsWith("#"))
            {
                int depth = 0;
                while (depth < trimmedLine.Length && trimmedLine[depth] == '#') depth++;
                string headerText = trimmedLine.Substring(depth).Trim();

                var headerParagraph = new Paragraph { Margin = new Thickness(0, depth == 1 ? 22 : 14, 0, 8) };
                var run = new Run 
                { 
                    Text = headerText, 
                    FontSize = depth == 1 ? 17 : (depth == 2 ? 15 : 13.5), 
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 246, 242, 234)) // #F6F2EA warm white
                };
                headerParagraph.Inlines.Add(run);
                rtb.Blocks.Add(headerParagraph);
                currentParagraph = null; // Reset paragraph context
                continue;
            }

            // 2. Horizontal Rule (e.g. ---)
            if (trimmedLine == "---" || trimmedLine == "***")
            {
                var ruleParagraph = new Paragraph { Margin = new Thickness(0, 16, 0, 16) };
                var inlineContainer = new InlineUIContainer();
                var lineBorder = new Border 
                { 
                    Height = 1, 
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(32, 167, 154, 131)), // subtle gold-tinted rule
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Width = rtb.ActualWidth > 0 ? rtb.ActualWidth : 580 // fallback
                };
                inlineContainer.Child = lineBorder;
                ruleParagraph.Inlines.Add(inlineContainer);
                rtb.Blocks.Add(ruleParagraph);
                currentParagraph = null;
                continue;
            }

            // 3. Bullet list item (e.g. * Item or - Item)
            if (trimmedLine.StartsWith("* ") || trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("• "))
            {
                var itemText = trimmedLine.Substring(2).Trim();
                var listParagraph = new Paragraph { Margin = new Thickness(16, 4, 0, 4) };
                
                // Add bullet dot
                var bulletRun = new Run { Text = "•  ", FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 154, 131)) }; // #A79A83
                listParagraph.Inlines.Add(bulletRun);

                ParseInlineMarkdown(listParagraph.Inlines, itemText);
                rtb.Blocks.Add(listParagraph);
                currentParagraph = null;
                continue;
            }

            // 4. Numbered list item (e.g. 1. Item)
            if (trimmedLine.Length > 2 && char.IsDigit(trimmedLine[0]))
            {
                int dotIndex = trimmedLine.IndexOf('.');
                if (dotIndex > 0 && dotIndex < trimmedLine.Length - 1 && trimmedLine[dotIndex + 1] == ' ' && IsAllDigits(trimmedLine.Substring(0, dotIndex)))
                {
                    var numberPrefix = trimmedLine.Substring(0, dotIndex + 2);
                    var itemText = trimmedLine.Substring(dotIndex + 2).Trim();
                    var listParagraph = new Paragraph { Margin = new Thickness(16, 4, 0, 4) };
                    
                    var numRun = new Run { Text = numberPrefix + " ", FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 167, 154, 131)) }; // #A79A83
                    listParagraph.Inlines.Add(numRun);

                    ParseInlineMarkdown(listParagraph.Inlines, itemText);
                    rtb.Blocks.Add(listParagraph);
                    currentParagraph = null;
                    continue;
                }
            }

            // 5. Empty line
            if (string.IsNullOrWhiteSpace(line))
            {
                currentParagraph = null;
                continue;
            }

            // 6. Normal text paragraph
            if (currentParagraph == null)
            {
                currentParagraph = new Paragraph { Margin = new Thickness(0, 6, 0, 6) };
                rtb.Blocks.Add(currentParagraph);
            }
            else
            {
                currentParagraph.Inlines.Add(new LineBreak());
            }

            ParseInlineMarkdown(currentParagraph.Inlines, line);
        }
    }

    private void ParseInlineMarkdown(Microsoft.UI.Xaml.Documents.InlineCollection inlines, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        int i = 0;
        while (i < text.Length)
        {
            // Inline code `code`
            if (text[i] == '`')
            {
                int end = text.IndexOf('`', i + 1);
                if (end != -1)
                {
                    var codeText = text.Substring(i + 1, end - (i + 1));
                    var codeRun = new Run 
                    { 
                        Text = codeText,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 12.5,
                        Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 192, 180, 159)) // light gold-bronze #C0B49F
                    };
                    inlines.Add(codeRun);
                    i = end + 1;
                    continue;
                }
            }

            // Hyperlink [Link Text](url)
            if (text[i] == '[')
            {
                int endText = text.IndexOf("](", i + 1);
                if (endText != -1)
                {
                    int endUrl = text.IndexOf(')', endText + 2);
                    if (endUrl != -1)
                    {
                        var linkText = text.Substring(i + 1, endText - (i + 1));
                        var urlStr = text.Substring(endText + 2, endUrl - (endText + 2));
                        
                        try
                        {
                            var hyperlink = new Hyperlink { NavigateUri = new Uri(urlStr) };
                            hyperlink.Inlines.Add(new Run { Text = linkText });
                            hyperlink.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 245, 212)); // cyan #00F5D4
                            inlines.Add(hyperlink);
                            i = endUrl + 1;
                            continue;
                        }
                        catch
                        {
                            // Fallback if URL parsing fails
                        }
                    }
                }
            }

            // Bold **
            if (text.Substring(i).StartsWith("**"))
            {
                int end = text.IndexOf("**", i + 2);
                if (end != -1)
                {
                    var boldText = text.Substring(i + 2, end - (i + 2));
                    var bold = new Bold();
                    ParseInlineMarkdown(bold.Inlines, boldText);
                    inlines.Add(bold);
                    i = end + 2;
                    continue;
                }
            }
            
            // Bold __
            if (text.Substring(i).StartsWith("__"))
            {
                int end = text.IndexOf("__", i + 2);
                if (end != -1)
                {
                    var boldText = text.Substring(i + 2, end - (i + 2));
                    var bold = new Bold();
                    ParseInlineMarkdown(bold.Inlines, boldText);
                    inlines.Add(bold);
                    i = end + 2;
                    continue;
                }
            }

            // Italic *
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] != '*')
            {
                int end = text.IndexOf('*', i + 1);
                if (end != -1)
                {
                    var italicText = text.Substring(i + 1, end - (i + 1));
                    var italic = new Italic();
                    ParseInlineMarkdown(italic.Inlines, italicText);
                    inlines.Add(italic);
                    i = end + 1;
                    continue;
                }
            }

            // Italic _
            if (text[i] == '_' && i + 1 < text.Length && text[i + 1] != '_')
            {
                int end = text.IndexOf('_', i + 1);
                if (end != -1)
                {
                    var italicText = text.Substring(i + 1, end - (i + 1));
                    var italic = new Italic();
                    ParseInlineMarkdown(italic.Inlines, italicText);
                    inlines.Add(italic);
                    i = end + 1;
                    continue;
                }
            }

            // Normal text chunk - search starting from k = i + 1 (crucial to prevent infinite loops!)
            int nextSpecial = -1;
            for (int k = i + 1; k < text.Length; k++)
            {
                if (text[k] == '*' || text[k] == '_' || text[k] == '`' || text[k] == '[')
                {
                    nextSpecial = k;
                    break;
                }
            }

            if (nextSpecial == -1)
            {
                inlines.Add(new Run { Text = text.Substring(i) });
                break;
            }
            else
            {
                if (nextSpecial > i)
                {
                    inlines.Add(new Run { Text = text.Substring(i, nextSpecial - i) });
                }
                i = nextSpecial;
            }
        }
    }

    private bool IsAllDigits(string str)
    {
        if (string.IsNullOrEmpty(str)) return false;
        foreach (char c in str)
        {
            if (!char.IsDigit(c)) return false;
        }
        return true;
    }

    public void ApplyBackdropSettings()
    {
        var type = SettingsManager.Instance.AppBackdropType;
        var theme = SettingsManager.Instance.AppTheme;

        if (type == "Mica")
        {
            // Light theme uses Base (lighter Mica), Standard/Dark use BaseAlt (deeper Mica)
            var kind = (theme == "Light")
                ? Microsoft.UI.Composition.SystemBackdrops.MicaKind.Base
                : Microsoft.UI.Composition.SystemBackdrops.MicaKind.BaseAlt;
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop { Kind = kind };
            if (RootGrid != null)
            {
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
        else if (type == "Acrylic")
        {
            this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            if (RootGrid != null)
            {
                RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }
        else
        {
            this.SystemBackdrop = null;
            if (RootGrid != null)
            {
                if (theme == "Light")
                {
                    RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 243, 243, 245));
                }
                else if (theme == "Amoled")
                {
                    RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                }
                else
                {
                    RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 19));
                }
            }
        }

        if (_trayWindow != null)
        {
            try { _trayWindow.ApplyBackdropSettings(); } catch {}
        }
    }

    private void ApplyThemeResources()
    {
        var theme = SettingsManager.Instance.AppTheme;
        var backdrop = SettingsManager.Instance.AppBackdropType;

        void ApplyToDictionary(ResourceDictionary dict)
        {
            if (theme == "Amoled")
            {
                // Black Graphite (AMOLED) resource overrides - 100% pitch black everywhere
                dict["WindowBackgroundBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                dict["TrayWindowBackground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(30, 0, 0, 0));
                dict["TrayWindowOpaqueBackground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                
                // Plaques/cards are completely transparent (absence of background)
                dict["CardBackgroundBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                dict["CardBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 24, 25));
                dict["WidgetCardBackgroundBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                dict["WidgetCardBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 21));
                dict["ConsoleBackgroundBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                dict["ConsoleBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 16, 17));

                // If backdrop is enabled (Mica/Acrylic), navigation backgrounds must be transparent to let it show through.
                // If backdrop is disabled, navigation backgrounds must be solid black.
                var navBackground = (backdrop == "Mica" || backdrop == "Acrylic")
                    ? Microsoft.UI.Colors.Transparent
                    : Windows.UI.Color.FromArgb(255, 0, 0, 0);

                var navBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(navBackground);
                dict["NavigationViewContentBackground"] = navBrush;
                dict["NavigationViewContentGridBackground"] = navBrush;
                dict["NavigationViewPaneBackground"] = navBrush;
                dict["NavigationViewExpandedPaneBackground"] = navBrush;
                dict["NavigationViewDefaultPaneBackground"] = navBrush;
                dict["NavigationViewListBackground"] = navBrush;
                dict["ApplicationPageBackgroundThemeBrush"] = navBrush;
                dict["TabViewBackground"] = navBrush;
                dict["TabViewContentBackground"] = navBrush;
                dict["TabViewHeaderBackground"] = navBrush;
                dict["TabViewItemHeaderBackground"] = navBrush;
                dict["TabViewItemHeaderBackgroundSelected"] = navBrush;
                dict["TabViewItemHeaderBackgroundPointerOver"] = navBrush;
                dict["TabViewItemHeaderBackgroundPressed"] = navBrush;
            }
            else
            {
                // Restore default Dark resources
                dict["WindowBackgroundBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 18, 18, 19));
                dict["TrayWindowBackground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(128, 16, 16, 17));
                dict["TrayWindowOpaqueBackground"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 24, 24, 26));
                dict["CardBackgroundBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(18, 255, 255, 255));
                dict["CardBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 47, 47, 47));
                dict["WidgetCardBackgroundBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 28, 28, 29));
                dict["WidgetCardBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 37, 37, 38));
                dict["ConsoleBackgroundBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 12, 12, 12));
                dict["ConsoleBorderBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 31, 31, 31));

                var transBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                dict["NavigationViewContentBackground"] = transBrush;
                dict["NavigationViewContentGridBackground"] = transBrush;
                dict["NavigationViewPaneBackground"] = transBrush;
                dict["NavigationViewExpandedPaneBackground"] = transBrush;
                dict["NavigationViewDefaultPaneBackground"] = transBrush;
                dict["NavigationViewListBackground"] = transBrush;
                dict["ApplicationPageBackgroundThemeBrush"] = transBrush;
                dict["TabViewBackground"] = transBrush;
                dict["TabViewContentBackground"] = transBrush;
                dict["TabViewHeaderBackground"] = transBrush;
                dict["TabViewItemHeaderBackground"] = transBrush;
                dict["TabViewItemHeaderBackgroundSelected"] = transBrush;
                dict["TabViewItemHeaderBackgroundPointerOver"] = transBrush;
                dict["TabViewItemHeaderBackgroundPressed"] = transBrush;
            }
        }

        // Apply overrides to both theme dictionaries for reliable precedence
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue("Dark", out object darkObj) && darkObj is ResourceDictionary darkDict)
        {
            ApplyToDictionary(darkDict);
        }
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue("Default", out object defaultObj) && defaultObj is ResourceDictionary defaultDict)
        {
            ApplyToDictionary(defaultDict);
        }
    }

    public void ApplyThemeSettings()
    {
        var theme = SettingsManager.Instance.AppTheme;
        ElementTheme elementTheme = (theme == "Light") ? ElementTheme.Light : ElementTheme.Dark;

        // Apply dynamic resources override
        ApplyThemeResources();

        if (this.Content is FrameworkElement rootElement)
        {
            // Toggle theme temporarily to force deep resource tree re-evaluation
            rootElement.RequestedTheme = (elementTheme == ElementTheme.Dark) ? ElementTheme.Light : ElementTheme.Dark;
            rootElement.RequestedTheme = elementTheme;
        }

        // Re-apply backdrop with theme-aware Mica variant
        ApplyBackdropSettings();

        // Sync tray theme
        if (_trayWindow != null)
        {
            try { _trayWindow.ApplyThemeSettings(); } catch {}
        }
    }
}
