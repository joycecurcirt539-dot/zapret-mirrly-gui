using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _runAtStartup;

    [ObservableProperty]
    private bool _autoUpdate;

    [ObservableProperty]
    private bool _autoCheckGuiUpdates;

    [ObservableProperty]
    private bool _autoUpdateDatabase;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose;

    [ObservableProperty]
    private bool _minimizeToTrayOnMinimize;

    [ObservableProperty]
    private string _tgWsProxyHost = "127.0.0.1";

    [ObservableProperty]
    private string _tgWsProxyFakeTlsDomain = "";

    [ObservableProperty]
    private string _tgWsProxyPoolSizeText = "4";

    [ObservableProperty]
    private string _tgWsProxyWorkerDomains = "";

    [ObservableProperty]
    private bool _tgWsProxyCfProxy = true;

    [ObservableProperty]
    private bool _tgWsProxyForceTestDc = false;

    [ObservableProperty]
    private string _appBackdropType = "Mica";

    [ObservableProperty]
    private string _trayBackdropType = "Acrylic";

    [ObservableProperty]
    private string _appTheme = "Standard";

    [ObservableProperty]
    private string _customZapretRoot = "";

    [ObservableProperty]
    private string _customListsPath = "";

    [ObservableProperty]
    private string _customBackupPath = "";

    [ObservableProperty]
    private bool _useAutoPaths = true;

    [ObservableProperty]
    private string _guiUpdateStatusText = "Проверка не проводилась";

    [ObservableProperty]
    private string _appUpdateStatusText = "Проверка не проводилась";

    [ObservableProperty]
    private string _ipSetUpdateStatusText = "Файл проверяется...";

    [ObservableProperty]
    private string _hostsUpdateStatusText = "Файл проверяется...";

    [ObservableProperty]
    private string _serviceStatusText = "Статус службы неизвестен";

    [ObservableProperty]
    private bool _isServiceInstalled = false;

    public SettingsViewModel()
    {
        LoadAllSettingsFromDisk();
    }

    public void LoadAllSettingsFromDisk()
    {
        RunAtStartup = IsRunAtStartupEnabled();
        AutoUpdate = false;
        AutoCheckGuiUpdates = SettingsManager.Instance.AutoCheckGuiUpdates;
        AutoUpdateDatabase = SettingsManager.Instance.AutoUpdateDatabase;
        MinimizeToTrayOnClose = SettingsManager.Instance.MinimizeToTrayOnClose;
        MinimizeToTrayOnMinimize = SettingsManager.Instance.MinimizeToTrayOnMinimize;

        TgWsProxyHost = SettingsManager.Instance.TgWsProxyHost;
        TgWsProxyFakeTlsDomain = SettingsManager.Instance.TgWsProxyFakeTlsDomain;
        TgWsProxyPoolSizeText = SettingsManager.Instance.TgWsProxyPoolSize.ToString();
        TgWsProxyWorkerDomains = SettingsManager.Instance.TgWsProxyWorkerDomains;
        TgWsProxyCfProxy = SettingsManager.Instance.TgWsProxyCfProxy;
        TgWsProxyForceTestDc = SettingsManager.Instance.TgWsProxyForceTestDc;

        AppBackdropType = SettingsManager.Instance.AppBackdropType;
        TrayBackdropType = SettingsManager.Instance.TrayBackdropType;
        AppTheme = SettingsManager.Instance.AppTheme;

        CustomZapretRoot = SettingsManager.Instance.CustomZapretRoot;
        CustomListsPath = SettingsManager.Instance.CustomListsPath;
        CustomBackupPath = SettingsManager.Instance.CustomBackupPath;
        UseAutoPaths = SettingsManager.Instance.UseAutoPaths;

        GuiUpdateStatusText = $"Установленная версия: v{AppUpdateService.CurrentGuiVersion}";
        AppUpdateStatusText = "Установленная версия: 1.9.9d";

        UpdateServiceStatus();
        CheckLocalFileStatuses();
    }

    private static bool IsRunAtStartupEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue("ZapretMirrlyGUI") != null;
        }
        catch
        {
            return false;
        }
    }

    public void UpdateServiceStatus()
    {
        IsServiceInstalled = ZapretService.IsServiceInstalled();
        string serviceStatus = ZapretService.GetServiceStatus();
        ServiceStatusText = IsServiceInstalled ? $"Служба установлена. Статус: {serviceStatus}" : "Служба Windows не установлена.";
    }

    private void CheckLocalFileStatuses()
    {
        var ipsetPath = Path.Combine(ZapretService.FindListsDirectory(), "ipset-all.txt");
        if (File.Exists(ipsetPath))
        {
            var fileInfo = new FileInfo(ipsetPath);
            IpSetUpdateStatusText = fileInfo.Length > 20
                ? $"Файл ipset-all.txt готов к работе ({fileInfo.Length / 1024} КБ)."
                : "Файл ipset-all.txt пуст.";
        }
        else
        {
            IpSetUpdateStatusText = "Файл ipset-all.txt отсутствует.";
        }

        var hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
        if (File.Exists(hostsFile))
        {
            try
            {
                var hostsContent = File.ReadAllText(hostsFile);
                HostsUpdateStatusText = hostsContent.Contains("# zapret-discord-youtube start", StringComparison.OrdinalIgnoreCase)
                    ? "Записи обхода DPI присутствуют в hosts."
                    : "Файл hosts не содержит записей обхода.";
            }
            catch
            {
                HostsUpdateStatusText = "Файл hosts недоступен для чтения.";
            }
        }
        else
        {
            HostsUpdateStatusText = "Файл hosts не найден.";
        }
    }

    [RelayCommand]
    private void SaveAllSettings()
    {
        SettingsManager.Instance.AutoCheckGuiUpdates = AutoCheckGuiUpdates;
        SettingsManager.Instance.AutoUpdateDatabase = AutoUpdateDatabase;
        SettingsManager.Instance.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
        SettingsManager.Instance.MinimizeToTrayOnMinimize = MinimizeToTrayOnMinimize;

        SettingsManager.Instance.TgWsProxyHost = TgWsProxyHost;
        SettingsManager.Instance.TgWsProxyFakeTlsDomain = TgWsProxyFakeTlsDomain;
        if (int.TryParse(TgWsProxyPoolSizeText, out var poolSize))
        {
            SettingsManager.Instance.TgWsProxyPoolSize = Math.Max(1, Math.Min(32, poolSize));
        }
        SettingsManager.Instance.TgWsProxyWorkerDomains = TgWsProxyWorkerDomains;
        SettingsManager.Instance.TgWsProxyCfProxy = TgWsProxyCfProxy;
        SettingsManager.Instance.TgWsProxyForceTestDc = TgWsProxyForceTestDc;

        SettingsManager.Instance.AppBackdropType = AppBackdropType;
        SettingsManager.Instance.TrayBackdropType = TrayBackdropType;
        SettingsManager.Instance.AppTheme = AppTheme;

        SettingsManager.Instance.CustomZapretRoot = CustomZapretRoot;
        SettingsManager.Instance.CustomListsPath = CustomListsPath;
        SettingsManager.Instance.CustomBackupPath = CustomBackupPath;
        SettingsManager.Instance.UseAutoPaths = UseAutoPaths;

        SettingsManager.Save();
    }
}
