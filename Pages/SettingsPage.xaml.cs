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
    private string _originalTgWsProxyHost = "127.0.0.1";
    private int _originalTgWsProxyPoolSize = 4;
    private string _originalTgWsProxyWorkerDomains = "";
    private bool _originalTgWsProxyCfProxy = true;
    private bool _originalTgWsProxyForceTestDc = false;
    private string _originalTgWsProxyFakeTlsDomain = "";

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var item in SettingsNav.MenuItems)
        {
            if (item is NavigationViewItem nvi && nvi.Tag as string == "launch")
            {
                SettingsNav.SelectedItem = nvi;
                LaunchParamsPanel.Visibility = Visibility.Visible;
                break;
            }
        }

        LoadAllSettingsFromDisk();
    }

    private void LoadAllSettingsFromDisk()
    {
        // 1. Presets List
        var presets = ZapretService.GetPresets();
        PresetsComboBox.ItemsSource = presets;
        
        _originalPreset = GetActivePresetFromRegistry();
        int presetIdx = presets.FindIndex(p => p.Equals(_originalPreset, StringComparison.OrdinalIgnoreCase));
        PresetsComboBox.SelectedIndex = presetIdx != -1 ? presetIdx : (presets.Count > 0 ? 0 : -1);

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
        var root = ZapretService.FindZapretRoot();
        var file = Path.Combine(root, "lists", "ipset-all.txt");
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
            AutomationPanel.Visibility = tag == "automation" ? Visibility.Visible : Visibility.Collapsed;
            UpdatesPanel.Visibility = tag == "updates" ? Visibility.Visible : Visibility.Collapsed;
            TgWsProxyPanel.Visibility = tag == "tgwsproxy" ? Visibility.Visible : Visibility.Collapsed;

            if (tag == "automation")
                UpdateServiceStatusInSettings();
        }
    }

    private void PresetsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // State tracking
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
        var root = ZapretService.FindZapretRoot();
        var listsDir = Path.Combine(root, "lists");
        if (Directory.Exists(listsDir))
        {
            Process.Start("explorer.exe", $"\"{listsDir}\"");
        }
    }

    private void ServiceInstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (PresetsComboBox.SelectedItem is string preset)
        {
            var mode = GameDisabledRadio.IsChecked == true ? "disabled" :
                       (GameAllRadio.IsChecked == true ? "all" :
                       (GameTcpRadio.IsChecked == true ? "tcp" : "udp"));
            
            ZapretService.InstallService(preset, mode);
        }
    }

    private void ServiceRemoveBtn_Click(object sender, RoutedEventArgs e)
    {
        ZapretService.RemoveService();
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
            // 1. Write Preset Registry (if service installed)
            if (PresetsComboBox.SelectedItem is string preset)
            {
                if (ZapretService.IsServiceInstalled())
                {
                    try
                    {
                        using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\zapret", true);
                        if (key != null)
                        {
                            var strategyName = Path.GetFileNameWithoutExtension(preset);
                            key.SetValue("zapret-discord-youtube", strategyName);
                        }
                    }
                    catch { }
                }
                _originalPreset = preset;
            }

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
            var ipsetFile = Path.Combine(root, "lists", "ipset-all.txt");
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
        AppUpdateStatusText.Text = "Проверка обновлений...";
        
        var result = await ZapretService.CheckForUpdatesAsync();
        
        AppUpdateStatusText.Text = result.StatusText;
        if (result.UpdateAvailable)
        {
            DownloadLatestReleaseBtn.NavigateUri = new Uri(result.DownloadUrl);
            DownloadLatestReleaseBtn.Visibility = Visibility.Visible;
        }
        else
        {
            DownloadLatestReleaseBtn.Visibility = Visibility.Collapsed;
        }
        CheckAppUpdatesBtn.IsEnabled = true;
    }

    private async void CheckGuiUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckGuiUpdatesBtn.IsEnabled = false;
        GuiUpdateStatusText.Text = "Проверка обновлений...";

        var result = await AppUpdateService.CheckForGuiUpdatesAsync();

        GuiUpdateStatusText.Text = result.StatusText;
        CheckGuiUpdatesBtn.IsEnabled = true;

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
        
        bool success = await ZapretService.UpdateIpsetListAsync();
        
        if (success)
        {
            IPSetUpdateStatusText.Text = "База IP-адресов успешно обновлена и синхронизирована.";
        }
        else
        {
            IPSetUpdateStatusText.Text = "Не удалось обновить базу IP-адресов (ошибка загрузки).";
        }
        UpdateIPSetListBtn.IsEnabled = true;
    }

    private async void CheckHostsBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckHostsBtn.IsEnabled = false;
        HostsUpdateStatusText.Text = "Проверка файла hosts...";
        
        var result = await ZapretService.CheckHostsStatusAsync();
        
        HostsUpdateStatusText.Text = result.CurrentStatus;
        _downloadedHostsContent = result.DownloadedContent;

        if (result.NeedsUpdate)
        {
            ApplyHostsUpdateBtn.Visibility = Visibility.Visible;
        }
        else
        {
            ApplyHostsUpdateBtn.Visibility = Visibility.Collapsed;
        }
        CheckHostsBtn.IsEnabled = true;
    }

    private async void ApplyHostsUpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        ApplyHostsUpdateBtn.IsEnabled = false;
        HostsUpdateStatusText.Text = "Применение изменений к файлу hosts...";
        
        bool success = await ZapretService.ApplyHostsUpdateAsync(_downloadedHostsContent);
        
        if (success)
        {
            HostsUpdateStatusText.Text = "Файл hosts успешно обновлен!";
            ApplyHostsUpdateBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            HostsUpdateStatusText.Text = "Ошибка записи. Возможно, приложению не хватает прав Администратора.";
            ApplyHostsUpdateBtn.IsEnabled = true;
        }
    }

    // ── Windows Service (Automation panel) ─────────────────────────────────────

    private void UpdateServiceStatusInSettings()
    {
        bool installed = ZapretService.IsServiceInstalled();
        string status = ZapretService.GetServiceStatus();

        if (!installed)
        {
            ServiceStatusInSettings.Text = "Служба не установлена — обход запускается только вручную";
            SettingsInstallServiceButton.IsEnabled = true;
            SettingsUninstallServiceButton.IsEnabled = false;
        }
        else
        {
            var humanStatus = status switch
            {
                "RUNNING"       => "работает",
                "STOPPED"       => "остановлена",
                "START_PENDING" => "запускается...",
                "STOP_PENDING"  => "останавливается...",
                _               => status
            };
            ServiceStatusInSettings.Text = $"Служба установлена — состояние: {humanStatus}";
            SettingsInstallServiceButton.IsEnabled = true;
            SettingsUninstallServiceButton.IsEnabled = true;
        }
    }

    private async void SettingsInstallServiceButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsInstallServiceButton.IsEnabled = false;
        SettingsUninstallServiceButton.IsEnabled = false;
        InstallServiceProgressRing.IsActive = true;
        InstallServiceProgressRing.Visibility = Visibility.Visible;

        try
        {
            var activePreset = SettingsManager.Instance.LastSelectedPreset;
            if (string.IsNullOrEmpty(activePreset)) activePreset = "general.bat";

            var root = ZapretService.FindZapretRoot();
            var gameFilterFile = System.IO.Path.Combine(root, "utils", "game_filter.enabled");
            string gameFilterMode = "disabled";
            try { if (System.IO.File.Exists(gameFilterFile)) gameFilterMode = System.IO.File.ReadAllText(gameFilterFile).Trim().ToLower(); } catch { }

            await Task.Run(() =>
            {
                if (ZapretService.IsRunning) ZapretService.StopBypass();
                ZapretService.InstallService(activePreset, gameFilterMode);
            });
        }
        finally
        {
            InstallServiceProgressRing.IsActive = false;
            InstallServiceProgressRing.Visibility = Visibility.Collapsed;
            UpdateServiceStatusInSettings();
        }
    }

    private async void SettingsUninstallServiceButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsInstallServiceButton.IsEnabled = false;
        SettingsUninstallServiceButton.IsEnabled = false;
        UninstallServiceProgressRing.IsActive = true;
        UninstallServiceProgressRing.Visibility = Visibility.Visible;

        try
        {
            await Task.Run(() =>
            {
                ZapretService.RemoveService();
            });
        }
        finally
        {
            UninstallServiceProgressRing.IsActive = false;
            UninstallServiceProgressRing.Visibility = Visibility.Collapsed;
            UpdateServiceStatusInSettings();
        }
    }
}

