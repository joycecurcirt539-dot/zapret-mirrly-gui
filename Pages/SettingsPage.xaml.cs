using System;
using System.IO;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class SettingsPage : Page
{
    private string _originalPreset = "general.bat";
    private string _originalGameFilter = "disabled";
    private string _originalIPSet = "none";
    private bool _originalAutoUpdate = false;
    private bool _originalRunAtStartup = false;
    private bool _originalMinimizeToTrayOnClose = false;
    private bool _originalMinimizeToTrayOnMinimize = false;
    private bool _originalAutoCheckGuiUpdates = true;
    private bool _originalAutoUpdateDatabase = false;
    private string _originalTgWsProxyHost = "127.0.0.1";
    private int _originalTgWsProxyPoolSize = 4;
    private string _originalTgWsProxyWorkerDomains = "";
    private bool _originalTgWsProxyCfProxy = true;
    private bool _originalTgWsProxyForceTestDc = false;
    private string _originalTgWsProxyFakeTlsDomain = "";
    private string _originalCustomZapretRoot = "";
    private string _originalCustomListsPath = "";
    private string _originalCustomBackupPath = "";
    private bool _originalUseAutoPaths = true;
    private string _originalAppBackdropType = "Mica";
    private string _originalTrayBackdropType = "Acrylic";
    private string _originalAppTheme = "Standard";
    private bool _isLoading = false;

    public SettingsPage()
    {
        InitializeComponent();

        // Initialize default selected item in settings menu
        foreach (var item in SettingsNav.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == "launch")
            {
                SettingsNav.SelectedItem = nvi;
                LaunchParamsPanel.Visibility = Visibility.Visible;
                break;
            }
        }

        // Load settings directly during initialization to prevent layout flickering during render
        LoadAllSettingsFromDisk();
    }

    private void LoadAllSettingsFromDisk()
    {
        _isLoading = true;
        try
        {
            _originalPreset = GetActivePresetFromRegistry();

            // 2. Game Filter
            _originalGameFilter = LoadGameFilterSettings();
            if (_originalGameFilter == "all")
                GameAllRadio.IsChecked = true;
            else if (_originalGameFilter == "tcp")
                GameTcpRadio.IsChecked = true;
            else if (_originalGameFilter == "udp")
                GameUdpRadio.IsChecked = true;
            else
                GameDisabledRadio.IsChecked = true;

            // 3. IPSet settings
            _originalIPSet = LoadIPSetSettings();
            if (_originalIPSet == "loaded")
                IpsetLoadedRadio.IsChecked = true;
            else if (_originalIPSet == "any")
                IpsetAnyRadio.IsChecked = true;
            else
                IpsetNoneRadio.IsChecked = true;

            // 4. Automation: Updates
            _originalAutoUpdate = LoadUpdateSettings();
            AutoUpdateToggle.IsOn = _originalAutoUpdate;

            // 5. Automation: Run at startup
            _originalRunAtStartup = IsRunAtStartupEnabled();
            RunAtStartupToggle.IsOn = _originalRunAtStartup;

            // 6. Minimize to Tray settings
            _originalMinimizeToTrayOnClose = SettingsManager.Instance.MinimizeToTrayOnClose;
            MinimizeToTrayOnCloseToggle.IsOn = _originalMinimizeToTrayOnClose;

            _originalMinimizeToTrayOnMinimize = SettingsManager.Instance.MinimizeToTrayOnMinimize;
            MinimizeToTrayOnMinimizeToggle.IsOn = _originalMinimizeToTrayOnMinimize;

            // 7. GUI wrapper auto check updates settings
            _originalAutoCheckGuiUpdates = SettingsManager.Instance.AutoCheckGuiUpdates;
            AutoCheckGuiUpdatesToggle.IsOn = _originalAutoCheckGuiUpdates;

            // 7.5. Database auto update settings
            _originalAutoUpdateDatabase = SettingsManager.Instance.AutoUpdateDatabase;
            AutoUpdateDatabaseToggle.IsOn = _originalAutoUpdateDatabase;

            // Set current version text labels and initial badge states dynamically based on local check (no stubs!)
            GuiUpdateStatusText.Text = $"Установленная версия: v{AppUpdateService.CurrentGuiVersion} (проверка не проводилась)";
            SetBadgeNotChecked(GuiVersionBadge, GuiVersionBadgeText);

            AppUpdateStatusText.Text = $"Установленная версия: 1.9.9a (проверка не проводилась)";
            SetBadgeNotChecked(CoreVersionBadge, CoreVersionBadgeText);

            // Check local IPSet file existence and size
            var ipsetPath = Path.Combine(ZapretService.FindListsDirectory(), "ipset-all.txt");
            if (File.Exists(ipsetPath))
            {
                var fileInfo = new FileInfo(ipsetPath);
                if (fileInfo.Length > 20)
                {
                    IPSetUpdateStatusText.Text = $"Файл ipset-all.txt готов к работе ({fileInfo.Length / 1024} КБ).";
                    SetBadgeUpToDate(IpsetVersionBadge, IpsetVersionBadgeText);
                }
                else
                {
                    IPSetUpdateStatusText.Text = "Файл ipset-all.txt пуст или содержит заглушку.";
                    SetBadgeNotChecked(IpsetVersionBadge, IpsetVersionBadgeText);
                    IpsetVersionBadgeText.Text = "ПУСТОЙ";
                }
            }
            else
            {
                IPSetUpdateStatusText.Text = "Файл ipset-all.txt отсутствует в папке lists.";
                SetBadgeError(IpsetVersionBadge, IpsetVersionBadgeText);
                IpsetVersionBadgeText.Text = "ОТСУТСТВУЕТ";
            }

            // Check local hosts file status
            var hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            if (File.Exists(hostsFile))
            {
                try
                {
                    var hostsContent = File.ReadAllText(hostsFile);
                    if (hostsContent.Contains("# zapret-discord-youtube start", StringComparison.OrdinalIgnoreCase))
                    {
                        HostsUpdateStatusText.Text = "Записи обхода DPI присутствуют в hosts.";
                        SetBadgeUpToDate(HostsVersionBadge, HostsVersionBadgeText);
                    }
                    else
                    {
                        HostsUpdateStatusText.Text = "Файл hosts не содержит записей обхода.";
                        SetBadgeNotChecked(HostsVersionBadge, HostsVersionBadgeText);
                        HostsVersionBadgeText.Text = "ОТСУТСТВУЕТ";
                    }
                }
                catch
                {
                    HostsUpdateStatusText.Text = "Файл hosts заблокирован или недоступен для чтения.";
                    SetBadgeError(HostsVersionBadge, HostsVersionBadgeText);
                }
            }
            else
            {
                HostsUpdateStatusText.Text = "Системный файл hosts не найден.";
                SetBadgeError(HostsVersionBadge, HostsVersionBadgeText);
                HostsVersionBadgeText.Text = "НЕ НАЙДЕН";
            }

            // 8. TgWsProxy advanced settings
            _originalTgWsProxyHost = SettingsManager.Instance.TgWsProxyHost;
            SettingsProxyHostTextBox.Text = _originalTgWsProxyHost;

            _originalTgWsProxyFakeTlsDomain = SettingsManager.Instance.TgWsProxyFakeTlsDomain;
            SettingsProxyFakeTlsDomainTextBox.Text = _originalTgWsProxyFakeTlsDomain;

            _originalTgWsProxyPoolSize = SettingsManager.Instance.TgWsProxyPoolSize;
            SettingsProxyPoolSizeTextBox.Text = _originalTgWsProxyPoolSize.ToString();

            _originalTgWsProxyWorkerDomains = SettingsManager.Instance.TgWsProxyWorkerDomains;
            SettingsProxyWorkerDomainsTextBox.Text = _originalTgWsProxyWorkerDomains;

            _originalTgWsProxyCfProxy = SettingsManager.Instance.TgWsProxyCfProxy;
            SettingsCfProxyToggle.IsOn = _originalTgWsProxyCfProxy;

            _originalTgWsProxyForceTestDc = SettingsManager.Instance.TgWsProxyForceTestDc;
            SettingsProxyForceTestDcToggle.IsOn = _originalTgWsProxyForceTestDc;

            // Initialize visual routing map
            UpdateVisualRoutingMap(_originalTgWsProxyCfProxy);
            if (VisualProxyPortText != null)
            {
                VisualProxyPortText.Text = $"Порт: {SettingsManager.Instance.TgWsProxyPort}";
            }

            // Populate Network Interfaces ComboBox
            var interfaces = new List<string> { "default" };
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up && 
                        (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet || 
                         ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ||
                         ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel))
                    {
                        interfaces.Add(ni.Name);
                    }
                }
            }
            catch { }
            BindInterfaceComboBox.ItemsSource = interfaces;

            var bindInterface = SettingsManager.Instance.BindInterface;
            int bindIdx = interfaces.FindIndex(i => i.Equals(bindInterface, StringComparison.OrdinalIgnoreCase));
            BindInterfaceComboBox.SelectedIndex = bindIdx != -1 ? bindIdx : 0;

            // Load AutoHostlist
            AutoHostlistToggle.IsOn = SettingsManager.Instance.AutoHostlist;

            // Load IpProtocolMode
            var ipMode = SettingsManager.Instance.IpProtocolMode;
            if (ipMode == "ipv4")
                IpProtoV4Radio.IsChecked = true;
            else if (ipMode == "ipv6")
                IpProtoV6Radio.IsChecked = true;
            else
                IpProtoBothRadio.IsChecked = true;

            _originalUseAutoPaths = SettingsManager.Instance.UseAutoPaths;
            AutoPathsToggle.IsOn = _originalUseAutoPaths;

            _originalCustomZapretRoot = SettingsManager.Instance.CustomZapretRoot;
            CustomZapretRootTextBox.Text = _originalCustomZapretRoot;

            _originalCustomListsPath = SettingsManager.Instance.CustomListsPath;
            CustomListsPathTextBox.Text = _originalCustomListsPath;

            _originalCustomBackupPath = SettingsManager.Instance.CustomBackupPath;
            CustomBackupPathTextBox.Text = _originalCustomBackupPath;

            // Apply initial buttons IsEnabled states based on UseAutoPaths
            BrowseZapretRootBtn.IsEnabled = !_originalUseAutoPaths;
            BrowseListsPathBtn.IsEnabled = !_originalUseAutoPaths;

            // Load visual window backdrops
            _originalAppBackdropType = SettingsManager.Instance.AppBackdropType;
            if (AppBackdropComboBox != null)
            {
                foreach (ComboBoxItem item in AppBackdropComboBox.Items)
                {
                    if (item.Tag as string == _originalAppBackdropType)
                    {
                        AppBackdropComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            _originalTrayBackdropType = SettingsManager.Instance.TrayBackdropType;
            if (TrayBackdropToggle != null)
            {
                TrayBackdropToggle.IsOn = _originalTrayBackdropType == "Acrylic";
            }

            // Load theme setting
            _originalAppTheme = SettingsManager.Instance.AppTheme;
            if (_originalAppTheme == "Standard")
            {
                _originalAppTheme = "Dark";
            }
            if (AppThemeComboBox != null)
            {
                foreach (ComboBoxItem item in AppThemeComboBox.Items)
                {
                    if (item.Tag as string == _originalAppTheme)
                    {
                        AppThemeComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            // Update visual filters map
            UpdateVisualFilteringMap();

            // Update visual folders map
            UpdateVisualFoldersMap();
        }
        finally
        {
            _isLoading = false;

            // Revert backdrop preview back to saved settings if loading/cancelling
            if (App.Current is App myApp && myApp.MainWindowInstance != null)
            {
                myApp.MainWindowInstance.ApplyBackdropSettings();
            }
        }
    }

    private string GetActivePresetFromRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\zapret");
            if (key != null)
            {
                var val = key.GetValue("zapret-discord-youtube") as string;
                if (!string.IsNullOrEmpty(val))
                {
                    return val + ".bat";
                }
            }
        }
        catch { }
        return "general.bat";
    }

    private string LoadGameFilterSettings()
    {
        var root = ZapretService.FindZapretRoot();
        var file = Path.Combine(root, "utils", "game_filter.enabled");
        if (!File.Exists(file)) return "disabled";
        try
        {
            return File.ReadAllText(file).Trim().ToLower();
        }
        catch
        {
            return "disabled";
        }
    }

    private string LoadIPSetSettings()
    {
        var file = Path.Combine(ZapretService.FindListsDirectory(), "ipset-all.txt");
        if (!File.Exists(file)) return "any";
        try
        {
            var lines = File.ReadAllLines(file);
            if (lines.Length == 0) return "any";
            var firstLine = lines[0].Trim();
            return firstLine == "203.0.113.113/32" ? "none" : "loaded";
        }
        catch
        {
            return "any";
        }
    }

    private bool LoadUpdateSettings()
    {
        var root = ZapretService.FindZapretRoot();
        var file = Path.Combine(root, "utils", "check_updates.enabled");
        return File.Exists(file);
    }

    private bool IsRunAtStartupEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (key != null)
            {
                return key.GetValue("ZapretMirrlyGUI") != null;
            }
        }
        catch { }
        return false;
    }

    private void SetRunAtStartupInRegistry(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                {
                    var path = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        key.SetValue("ZapretMirrlyGUI", $"\"{path}\"");
                    }
                }
                else
                {
                    key.DeleteValue("ZapretMirrlyGUI", false);
                }
            }
        }
        catch { }
    }

    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            LaunchParamsPanel.Visibility = tag == "launch" ? Visibility.Visible : Visibility.Collapsed;
            FiltersPanel.Visibility = tag == "filters" ? Visibility.Visible : Visibility.Collapsed;
            FilesFoldersPanel.Visibility = tag == "folders" ? Visibility.Visible : Visibility.Collapsed;
            UpdatesPanel.Visibility = tag == "updates" ? Visibility.Visible : Visibility.Collapsed;
            TgWsProxyPanel.Visibility = tag == "tgwsproxy" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OpenRootFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var root = ZapretService.FindZapretRoot();
        if (Directory.Exists(root))
        {
            Process.Start("explorer.exe", $"\"{root}\"");
        }
    }

    private void OpenListsFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var listsDir = ZapretService.FindListsDirectory();
        if (Directory.Exists(listsDir))
        {
            Process.Start("explorer.exe", $"\"{listsDir}\"");
        }
    }

    private void AutoPathsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (AutoPathsToggle == null || BrowseZapretRootBtn == null || BrowseListsPathBtn == null || CustomZapretRootTextBox == null || CustomListsPathTextBox == null) return;

        bool auto = AutoPathsToggle.IsOn;
        BrowseZapretRootBtn.IsEnabled = !auto;
        BrowseListsPathBtn.IsEnabled = !auto;

        if (auto)
        {
            CustomZapretRootTextBox.Text = "";
            CustomListsPathTextBox.Text = "";
        }

        UpdateVisualFoldersMap();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public class Win32Window : System.Windows.Forms.IWin32Window
    {
        private readonly IntPtr _hwnd;
        public Win32Window(IntPtr hwnd) => _hwnd = hwnd;
        public IntPtr Handle => _hwnd;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        return await Task.Run(() =>
        {
            try
            {
                var hwnd = GetActiveWindow();
                string? selectedPath = null;

                var t = new System.Threading.Thread(() =>
                {
                    using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                    {
                        dialog.Description = title;
                        dialog.UseDescriptionForTitle = true;

                        var result = dialog.ShowDialog(hwnd != IntPtr.Zero ? new Win32Window(hwnd) : null);
                        if (result == System.Windows.Forms.DialogResult.OK)
                        {
                            selectedPath = dialog.SelectedPath;
                        }
                    }
                });
                t.SetApartmentState(System.Threading.ApartmentState.STA);
                t.Start();
                t.Join();

                return selectedPath;
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Ошибка выбора папки",
                        Content = $"Не удалось запустить выбор папки:\n{ex.Message}",
                        CloseButtonText = "ОК",
                        XamlRoot = this.XamlRoot
                    };
                    _ = dialog.ShowAsync();
                });
                return null;
            }
        });
    }

    private async void BrowseZapretRootBtn_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync("Выберите папку с утилитой zapret");
        if (folder != null)
        {
            CustomZapretRootTextBox.Text = folder;
            UpdateVisualFoldersMap();
        }
    }

    private async void BrowseListsPathBtn_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync("Выберите папку со списками доменов");
        if (folder != null)
        {
            CustomListsPathTextBox.Text = folder;
            UpdateVisualFoldersMap();
        }
    }

    private async void BrowseBackupPathBtn_Click(object sender, RoutedEventArgs e)
    {
        var folder = await PickFolderAsync("Выберите папку для сохранения бэкапов");
        if (folder != null)
        {
            CustomBackupPathTextBox.Text = folder;
            UpdateVisualFoldersMap();
        }
    }

    private async void ExportBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        var backupDir = CustomBackupPathTextBox.Text.Trim();
        if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir))
        {
            var warningDialog = new ContentDialog
            {
                Title = "Путь не указан",
                Content = "⚠️ Пожалуйста, сначала укажите корректную папку для сохранения резервных копий в настройках!",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = warningDialog.ShowAsync();
            return;
        }

        try
        {
            var zipFileName = $"zapret_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            var targetZipPath = Path.Combine(backupDir, zipFileName);

            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            // Copy lists
            var listsDir = ZapretService.FindListsDirectory();
            if (Directory.Exists(listsDir))
            {
                var tempLists = Path.Combine(tempDir, "lists");
                Directory.CreateDirectory(tempLists);
                foreach (var f in Directory.GetFiles(listsDir))
                {
                    File.Copy(f, Path.Combine(tempLists, Path.GetFileName(f)), true);
                }
            }

            // Copy settings.json if exists
            var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretMirrlyGUI");
            var settingsJson = Path.Combine(localAppData, "settings.json");
            if (File.Exists(settingsJson))
            {
                File.Copy(settingsJson, Path.Combine(tempDir, "settings.json"), true);
            }

            // Create ZIP
            var tempZip = Path.GetTempFileName();
            File.Delete(tempZip);
            System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, tempZip);

            // Copy to target destination
            if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
            File.Move(tempZip, targetZipPath);

            // Clean up temp dir
            try { Directory.Delete(tempDir, true); } catch { }

            var dialog = new ContentDialog
            {
                Title = "Успех",
                Content = $"Резервная копия успешно создана и сохранена в:\n{targetZipPath}",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Ошибка",
                Content = $"Не удалось создать резервную копию:\n{ex.Message}",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }
    }

    private async void ImportBackupBtn_Click(object sender, RoutedEventArgs e)
    {
        string? selectedFilePath = null;
        try
        {
            var hwnd = GetActiveWindow();
            var t = new System.Threading.Thread(() =>
            {
                using (var dialog = new System.Windows.Forms.OpenFileDialog())
                {
                    dialog.Title = "Выберите файл резервной копии";
                    dialog.Filter = "ZIP-архивы (*.zip)|*.zip|Все файлы (*.*)|*.*";
                    dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                    var result = dialog.ShowDialog(hwnd != IntPtr.Zero ? new Win32Window(hwnd) : null);
                    if (result == System.Windows.Forms.DialogResult.OK)
                    {
                        selectedFilePath = dialog.FileName;
                    }
                }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            t.Join();
        }
        catch (Exception ex)
        {
            var errDialog = new ContentDialog
            {
                Title = "Ошибка",
                Content = $"Не удалось запустить выбор файлов:\n{ex.Message}",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = errDialog.ShowAsync();
            return;
        }

        if (string.IsNullOrEmpty(selectedFilePath)) return;

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);

            System.IO.Compression.ZipFile.ExtractToDirectory(selectedFilePath, tempDir);

            // 1. Restore settings.json
            var backupSettings = Path.Combine(tempDir, "settings.json");
            var localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZapretMirrlyGUI");
            var settingsJson = Path.Combine(localAppData, "settings.json");
            
            if (File.Exists(backupSettings))
            {
                Directory.CreateDirectory(localAppData);
                File.Copy(backupSettings, settingsJson, true);
            }

            // 2. Restore lists
            var backupLists = Path.Combine(tempDir, "lists");
            if (Directory.Exists(backupLists))
            {
                // Force load settings so we know what lists folder to copy to
                SettingsManager.Load();
                var targetLists = ZapretService.FindListsDirectory();
                Directory.CreateDirectory(targetLists);
                foreach (var f in Directory.GetFiles(backupLists))
                {
                    File.Copy(f, Path.Combine(targetLists, Path.GetFileName(f)), true);
                }
            }

            // Clean up temp dir
            try { Directory.Delete(tempDir, true); } catch { }

            // Reload Settings Page values
            SettingsManager.Load();
            LoadAllSettingsFromDisk();

            var dialog = new ContentDialog
            {
                Title = "Успех",
                Content = "Данные успешно восстановлены из резервной копии. Настройки обновлены.",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Ошибка",
                Content = $"Не удалось восстановить данные из резервной копии:\n{ex.Message}",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }
    }



    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(SettingsProxyPoolSizeTextBox.Text, out var poolSize) || poolSize < 0 || poolSize > 100)
        {
            var dialog = new ContentDialog
            {
                Title = "Ошибка",
                Content = "Пожалуйста, введите корректный размер пула веб-сокетов (0 - 100).",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
            return;
        }

        var root = ZapretService.FindZapretRoot();

        try
        {
            // 2. Save Game Filter
            var gameFilterFile = Path.Combine(root, "utils", "game_filter.enabled");
            if (GameDisabledRadio.IsChecked == true)
            {
                if (File.Exists(gameFilterFile)) File.Delete(gameFilterFile);
                _originalGameFilter = "disabled";
            }
            else
            {
                var val = GameAllRadio.IsChecked == true ? "all" : (GameTcpRadio.IsChecked == true ? "tcp" : "udp");
                Directory.CreateDirectory(Path.GetDirectoryName(gameFilterFile)!);
                File.WriteAllText(gameFilterFile, val);
                _originalGameFilter = val;
            }

            // 3. Save IPSet settings
            var ipsetFile = Path.Combine(ZapretService.FindListsDirectory(), "ipset-all.txt");
            var backupFile = ipsetFile + ".backup";
            Directory.CreateDirectory(Path.GetDirectoryName(ipsetFile)!);

            void BackupIfNeeded()
            {
                if (File.Exists(ipsetFile))
                {
                    var lines = File.ReadAllLines(ipsetFile);
                    if (lines.Length > 0 && lines[0].Trim() != "203.0.113.113/32")
                    {
                        if (File.Exists(backupFile)) File.Delete(backupFile);
                        File.Copy(ipsetFile, backupFile);
                    }
                }
            }

            if (IpsetLoadedRadio.IsChecked == true)
            {
                if (File.Exists(backupFile))
                {
                    File.Copy(backupFile, ipsetFile, overwrite: true);
                }
                else if (!File.Exists(ipsetFile))
                {
                    File.WriteAllText(ipsetFile, "");
                }
                _originalIPSet = "loaded";
            }
            else if (IpsetNoneRadio.IsChecked == true)
            {
                BackupIfNeeded();
                File.WriteAllText(ipsetFile, "203.0.113.113/32\r\n");
                _originalIPSet = "none";
            }
            else if (IpsetAnyRadio.IsChecked == true)
            {
                BackupIfNeeded();
                File.WriteAllText(ipsetFile, "");
                _originalIPSet = "any";
            }

            // 4. Save Updates check settings
            var updateCheckFile = Path.Combine(root, "utils", "check_updates.enabled");
            if (AutoUpdateToggle.IsOn)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(updateCheckFile)!);
                File.WriteAllText(updateCheckFile, "enabled");
                _originalAutoUpdate = true;
            }
            else
            {
                if (File.Exists(updateCheckFile)) File.Delete(updateCheckFile);
                _originalAutoUpdate = false;
            }

            // 5. Save Startup key
            SetRunAtStartupInRegistry(RunAtStartupToggle.IsOn);
            _originalRunAtStartup = RunAtStartupToggle.IsOn;

            // 6. Save Minimize to Tray settings
            SettingsManager.Instance.MinimizeToTrayOnClose = MinimizeToTrayOnCloseToggle.IsOn;
            _originalMinimizeToTrayOnClose = MinimizeToTrayOnCloseToggle.IsOn;

            SettingsManager.Instance.MinimizeToTrayOnMinimize = MinimizeToTrayOnMinimizeToggle.IsOn;
            _originalMinimizeToTrayOnMinimize = MinimizeToTrayOnMinimizeToggle.IsOn;

            // 7. Save GUI wrapper auto check updates settings
            SettingsManager.Instance.AutoCheckGuiUpdates = AutoCheckGuiUpdatesToggle.IsOn;
            _originalAutoCheckGuiUpdates = AutoCheckGuiUpdatesToggle.IsOn;

            // 7.5. Save Database auto update settings
            SettingsManager.Instance.AutoUpdateDatabase = AutoUpdateDatabaseToggle.IsOn;
            _originalAutoUpdateDatabase = AutoUpdateDatabaseToggle.IsOn;

            // 8. Save TgWsProxy advanced settings
            var proxyHost = SettingsProxyHostTextBox.Text.Trim();
            if (string.IsNullOrEmpty(proxyHost)) proxyHost = "127.0.0.1";
            SettingsManager.Instance.TgWsProxyHost = proxyHost;
            _originalTgWsProxyHost = proxyHost;

            var fakeTlsDomain = SettingsProxyFakeTlsDomainTextBox.Text.Trim();
            SettingsManager.Instance.TgWsProxyFakeTlsDomain = fakeTlsDomain;
            _originalTgWsProxyFakeTlsDomain = fakeTlsDomain;

            SettingsManager.Instance.TgWsProxyPoolSize = poolSize;
            _originalTgWsProxyPoolSize = poolSize;

            var workers = SettingsProxyWorkerDomainsTextBox.Text.Trim();
            SettingsManager.Instance.TgWsProxyWorkerDomains = workers;
            _originalTgWsProxyWorkerDomains = workers;

            SettingsManager.Instance.TgWsProxyCfProxy = SettingsCfProxyToggle.IsOn;
            _originalTgWsProxyCfProxy = SettingsCfProxyToggle.IsOn;

            SettingsManager.Instance.TgWsProxyForceTestDc = SettingsProxyForceTestDcToggle.IsOn;
            _originalTgWsProxyForceTestDc = SettingsProxyForceTestDcToggle.IsOn;

            // Restart proxy server dynamically if running to apply changes immediately
            if (TgWsProxyService.IsRunning)
            {
                TgWsProxyService.StartProxy();
            }

            // Save Traffic Filter settings
            SettingsManager.Instance.AutoHostlist = AutoHostlistToggle.IsOn;
            SettingsManager.Instance.BindInterface = BindInterfaceComboBox.SelectedItem as string ?? "default";
            
            if (IpProtoV4Radio.IsChecked == true)
                SettingsManager.Instance.IpProtocolMode = "ipv4";
            else if (IpProtoV6Radio.IsChecked == true)
                SettingsManager.Instance.IpProtocolMode = "ipv6";
            else
                SettingsManager.Instance.IpProtocolMode = "both";

            // Save custom paths settings
            SettingsManager.Instance.UseAutoPaths = AutoPathsToggle.IsOn;
            _originalUseAutoPaths = AutoPathsToggle.IsOn;

            SettingsManager.Instance.CustomZapretRoot = CustomZapretRootTextBox.Text.Trim();
            _originalCustomZapretRoot = CustomZapretRootTextBox.Text.Trim();

            SettingsManager.Instance.CustomListsPath = CustomListsPathTextBox.Text.Trim();
            _originalCustomListsPath = CustomListsPathTextBox.Text.Trim();

            SettingsManager.Instance.CustomBackupPath = CustomBackupPathTextBox.Text.Trim();
            _originalCustomBackupPath = CustomBackupPathTextBox.Text.Trim();

            // Save Window Backdrop effects
            var appBackdropItem = AppBackdropComboBox.SelectedItem as ComboBoxItem;
            var appBackdrop = appBackdropItem?.Tag as string ?? "Mica";
            SettingsManager.Instance.AppBackdropType = appBackdrop;
            _originalAppBackdropType = appBackdrop;

            var trayBackdrop = TrayBackdropToggle.IsOn ? "Acrylic" : "None";
            SettingsManager.Instance.TrayBackdropType = trayBackdrop;
            _originalTrayBackdropType = trayBackdrop;

            // Save theme
            var themeItem = AppThemeComboBox.SelectedItem as ComboBoxItem;
            var appTheme = themeItem?.Tag as string ?? "Standard";
            SettingsManager.Instance.AppTheme = appTheme;
            _originalAppTheme = appTheme;

            // Apply immediately to MainWindow and Tray
            if (App.Current is App myApp && myApp.MainWindowInstance != null)
            {
                myApp.MainWindowInstance.ApplyBackdropSettings();
                myApp.MainWindowInstance.ApplyThemeSettings();
            }

            SettingsManager.Save();

            // Success Visual Feedback
            var infoQueue = new ContentDialog
            {
                Title = "Успех",
                Content = "Параметры успешно сохранены.",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = infoQueue.ShowAsync();
        }
        catch (Exception ex)
        {
            var errorQueue = new ContentDialog
            {
                Title = "Ошибка",
                Content = $"Не удалось сохранить настройки: {ex.Message}",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = errorQueue.ShowAsync();
        }
    }

    private void CancelSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAllSettingsFromDisk();
    }

    private string? _downloadedHostsContent = null;

    private async void CheckAppUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckAppUpdatesBtn.IsEnabled = false;
        AppUpdateStatusText.Text = "Проверка обновлений на GitHub...";
        SetBadgeChecking(CoreVersionBadge, CoreVersionBadgeText);
        
        var result = await ZapretService.CheckForUpdatesAsync();
        
        if (result.UpdateAvailable)
        {
            AppUpdateStatusText.Text = $"Установлено: 1.9.9a | Доступно: {result.LatestVersion} (проверено в {DateTime.Now:HH:mm:ss})";
            DownloadLatestReleaseBtn.NavigateUri = new Uri(result.DownloadUrl);
            DownloadLatestReleaseBtn.Visibility = Visibility.Visible;
            SetBadgeUpdateAvailable(CoreVersionBadge, CoreVersionBadgeText, result.LatestVersion);
        }
        else
        {
            AppUpdateStatusText.Text = $"Установлено: 1.9.9a | Актуально (проверено в {DateTime.Now:HH:mm:ss})";
            DownloadLatestReleaseBtn.Visibility = Visibility.Collapsed;
            SetBadgeUpToDate(CoreVersionBadge, CoreVersionBadgeText);
        }
        CheckAppUpdatesBtn.IsEnabled = true;
    }

    private async void CheckGuiUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckGuiUpdatesBtn.IsEnabled = false;
        GuiUpdateStatusText.Text = "Проверка обновлений на GitHub...";
        SetBadgeChecking(GuiVersionBadge, GuiVersionBadgeText);

        var result = await AppUpdateService.CheckForGuiUpdatesAsync();

        CheckGuiUpdatesBtn.IsEnabled = true;

        if (result.UpdateAvailable)
        {
            GuiUpdateStatusText.Text = $"Установлено: v{AppUpdateService.CurrentGuiVersion} | Доступно: {result.LatestVersion} (проверено в {DateTime.Now:HH:mm:ss})";
            SetBadgeUpdateAvailable(GuiVersionBadge, GuiVersionBadgeText, result.LatestVersion);
        }
        else
        {
            GuiUpdateStatusText.Text = $"Установлено: v{AppUpdateService.CurrentGuiVersion} | Актуально (проверено в {DateTime.Now:HH:mm:ss})";
            SetBadgeUpToDate(GuiVersionBadge, GuiVersionBadgeText);
        }

        var mainWindow = (App.Current as App)?.MainWindowInstance;
        if (mainWindow != null)
        {
            mainWindow.UpdateSidebarStatus(result);
            if (result.UpdateAvailable)
            {
                mainWindow.ShowUpdateModal(result);
            }
        }
    }

    private async void UpdateIPSetListBtn_Click(object sender, RoutedEventArgs e)
    {
        UpdateIPSetListBtn.IsEnabled = false;
        IPSetUpdateStatusText.Text = "Обновление базы IP-адресов...";
        SetBadgeChecking(IpsetVersionBadge, IpsetVersionBadgeText);
        
        bool success = await ZapretService.UpdateIpsetListAsync();
        
        if (success)
        {
            var ipsetPath = Path.Combine(ZapretService.FindListsDirectory(), "ipset-all.txt");
            var sizeText = "";
            if (File.Exists(ipsetPath))
            {
                sizeText = $" ({new FileInfo(ipsetPath).Length / 1024} КБ)";
            }
            IPSetUpdateStatusText.Text = $"База успешно обновлена в {DateTime.Now:HH:mm:ss}{sizeText}.";
            SetBadgeUpToDate(IpsetVersionBadge, IpsetVersionBadgeText);
        }
        else
        {
            IPSetUpdateStatusText.Text = "Не удалось обновить базу IP-адресов.";
            SetBadgeError(IpsetVersionBadge, IpsetVersionBadgeText);
        }
        UpdateIPSetListBtn.IsEnabled = true;
    }

    private async void CheckHostsBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckHostsBtn.IsEnabled = false;
        HostsUpdateStatusText.Text = "Проверка файла hosts...";
        SetBadgeChecking(HostsVersionBadge, HostsVersionBadgeText);
        
        var result = await ZapretService.CheckHostsStatusAsync();
        
        HostsUpdateStatusText.Text = $"{result.CurrentStatus} (проверено в {DateTime.Now:HH:mm:ss})";
        _downloadedHostsContent = result.DownloadedContent;

        if (result.NeedsUpdate)
        {
            ApplyHostsUpdateBtn.Visibility = Visibility.Visible;
            SetBadgeUpdateAvailable(HostsVersionBadge, HostsVersionBadgeText, "ОБНОВИТЬ");
        }
        else
        {
            ApplyHostsUpdateBtn.Visibility = Visibility.Collapsed;
            SetBadgeUpToDate(HostsVersionBadge, HostsVersionBadgeText);
        }
        CheckHostsBtn.IsEnabled = true;
    }

    private async void ApplyHostsUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplyHostsUpdateBtn.IsEnabled = false;
        HostsUpdateStatusText.Text = "Применение изменений к файлу hosts...";
        SetBadgeChecking(HostsVersionBadge, HostsVersionBadgeText);
        
        bool success = await ZapretService.ApplyHostsUpdateAsync(_downloadedHostsContent);
        
        if (success)
        {
            HostsUpdateStatusText.Text = $"Файл hosts успешно обновлен в {DateTime.Now:HH:mm:ss}!";
            ApplyHostsUpdateBtn.Visibility = Visibility.Collapsed;
            SetBadgeUpToDate(HostsVersionBadge, HostsVersionBadgeText);
        }
        else
        {
            HostsUpdateStatusText.Text = "Ошибка записи (требуется Администратор).";
            ApplyHostsUpdateBtn.IsEnabled = true;
            SetBadgeError(HostsVersionBadge, HostsVersionBadgeText);
        }
    }

    private void SetBadgeChecking(Border badge, TextBlock badgeText)
    {
        badge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255));
        badge.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(100, 255, 255, 255));
        badge.BorderThickness = new Thickness(1);
        badgeText.Text = "ПРОВЕРКА...";
        badgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 200, 200));
    }

    private void SetBadgeUpToDate(Border badge, TextBlock badgeText)
    {
        badge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 16, 185, 129)); // Green
        badge.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
        badge.BorderThickness = new Thickness(1);
        badgeText.Text = "АКТУАЛЬНО";
        badgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129));
    }

    private void SetBadgeUpdateAvailable(Border badge, TextBlock badgeText, string latestVer)
    {
        badge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 255, 159, 28)); // Amber
        badge.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 159, 28));
        badge.BorderThickness = new Thickness(1);
        badgeText.Text = latestVer == "ОБНОВИТЬ" ? "ОБНОВИТЬ" : $"ДОСТУПНО: {latestVer}";
        badgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 159, 28));
    }

    private void SetBadgeError(Border badge, TextBlock badgeText)
    {
        badge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(50, 239, 68, 68)); // Red
        badge.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
        badge.BorderThickness = new Thickness(1);
        badgeText.Text = "ОШИБКА";
        badgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68));
    }

    private void SetBadgeNotChecked(Border badge, TextBlock badgeText)
    {
        badge.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(20, 255, 255, 255));
        badge.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255));
        badge.BorderThickness = new Thickness(1);
        badgeText.Text = "НЕ ПРОВЕРЕНО";
        badgeText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 138, 138, 143));
    }



    private void UpdateVisualRoutingMap(bool cfEnabled)
    {
        if (VisualExitNodeIcon == null || VisualExitNodeTitle == null || VisualExitNodeDesc == null) return;
        if (cfEnabled)
        {
            VisualExitNodeIcon.Glyph = "\uE909"; // Cloud
            VisualExitNodeTitle.Text = "Cloudflare Workers";
            VisualExitNodeDesc.Text = "Туннель WebSocket";
        }
        else
        {
            VisualExitNodeIcon.Glyph = "\uE73E"; // Globe/Connected
            VisualExitNodeTitle.Text = "Прямое подключение";
            VisualExitNodeDesc.Text = "Маскировка Fake TLS";
        }
    }

    private void SettingsCfProxyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        UpdateVisualRoutingMap(SettingsCfProxyToggle.IsOn);
    }

    private void UpdateVisualFilteringMap()
    {
        if (VisualFilterInterfaceText == null || VisualFilterPortsText == null || 
            VisualFilterListText == null || VisualFilterLearnText == null ||
            VisualFilterPortsIcon == null || VisualFilterListIcon == null || VisualFilterLearnIcon == null) return;

        // 1. Adapter & IP
        var bind = BindInterfaceComboBox.SelectedItem as string ?? "default";
        var ipMode = IpProtoBothRadio.IsChecked == true ? "Both" :
                     (IpProtoV4Radio.IsChecked == true ? "IPv4" : "IPv6");
        VisualFilterInterfaceText.Text = $"{bind} / {ipMode}";

        // 2. Ports
        if (GameDisabledRadio.IsChecked == true)
        {
            VisualFilterPortsIcon.Glyph = "\uE9A1"; // Ethernet
            VisualFilterPortsText.Text = "Все порты";
        }
        else if (GameAllRadio.IsChecked == true)
        {
            VisualFilterPortsIcon.Glyph = "\uE7FC"; // Game controller
            VisualFilterPortsText.Text = "TCP/UDP (1024+)";
        }
        else if (GameTcpRadio.IsChecked == true)
        {
            VisualFilterPortsIcon.Glyph = "\uF13C"; // Plug
            VisualFilterPortsText.Text = "TCP (1024+)";
        }
        else
        {
            VisualFilterPortsIcon.Glyph = "\uF13D"; // Audio/Speaker (WebRTC UDP)
            VisualFilterPortsText.Text = "UDP (1024+)";
        }

        // 3. Lists matching
        if (IpsetLoadedRadio.IsChecked == true)
        {
            VisualFilterListIcon.Glyph = "\uE8F1"; // Database
            VisualFilterListText.Text = "ipset-all.txt";
        }
        else if (IpsetNoneRadio.IsChecked == true)
        {
            VisualFilterListIcon.Glyph = "\uE909"; // Globe/Cloud
            VisualFilterListText.Text = "Весь интернет";
        }
        else
        {
            VisualFilterListIcon.Glyph = "\uEF58"; // Filter list
            VisualFilterListText.Text = "Режим 'Any'";
        }

        // 4. Auto learning
        if (AutoHostlistToggle.IsOn)
        {
            VisualFilterLearnIcon.Glyph = "\uEA80"; // Brain/Sparkle
            VisualFilterLearnIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129)); // Green
            VisualFilterLearnText.Text = "Обучение ВКЛ";
        }
        else
        {
            VisualFilterLearnIcon.Glyph = "\uE730"; // Blocked/Muted
            VisualFilterLearnIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 138, 138, 143)); // Grey
            VisualFilterLearnText.Text = "Отключено";
        }
    }

    private void GameRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        UpdateVisualFilteringMap();
    }

    private void IpsetRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        UpdateVisualFilteringMap();
    }

    private void IpProtoRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        UpdateVisualFilteringMap();
    }

    private void AutoHostlistToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        UpdateVisualFilteringMap();
    }

    private void BindInterfaceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        UpdateVisualFilteringMap();
    }

    private void UpdateVisualFoldersMap()
    {
        if (VisualZapretRootText == null || VisualListsPathText == null || VisualBackupPathText == null) return;

        if (AutoPathsToggle.IsOn)
        {
            VisualZapretRootText.Text = "Автоопределение";
            VisualListsPathText.Text = "zapret\\lists (По умолчанию)";
        }
        else
        {
            var root = CustomZapretRootTextBox.Text;
            VisualZapretRootText.Text = string.IsNullOrWhiteSpace(root) ? "Автоопределение" : root;

            var lists = CustomListsPathTextBox.Text;
            VisualListsPathText.Text = string.IsNullOrWhiteSpace(lists) ? "zapret\\lists (По умолчанию)" : lists;
        }

        var backup = CustomBackupPathTextBox.Text;
        VisualBackupPathText.Text = string.IsNullOrWhiteSpace(backup) ? "Папка не выбрана" : backup;
    }

    private void AppBackdropComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        var selectedItem = AppBackdropComboBox.SelectedItem as ComboBoxItem;
        var tag = selectedItem?.Tag as string;
        if (tag != null && App.Current is App myApp && myApp.MainWindowInstance != null)
        {
            // Instant live preview
            SettingsManager.Instance.AppBackdropType = tag;
            myApp.MainWindowInstance.ApplyBackdropSettings();
            myApp.MainWindowInstance.ApplyThemeSettings();
        }
    }

    private void TrayBackdropToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // Settings are stored in memory and applied to tray window dynamically upon opening or clicking Apply
    }

    private void AppThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (!(App.Current is App myApp) || myApp.MainWindowInstance == null) return;

        var item = AppThemeComboBox.SelectedItem as ComboBoxItem;
        var tag = item?.Tag as string ?? "Standard";

        // Instant live preview — temporarily apply the theme without saving
        SettingsManager.Instance.AppTheme = tag;
        myApp.MainWindowInstance.ApplyThemeSettings();
    }
}

