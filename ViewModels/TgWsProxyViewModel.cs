using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZapretMirrlyGUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ZapretMirrlyGUI.ViewModels;

public partial class TgWsProxyViewModel : ObservableObject
{
    private readonly DispatcherTimer _orbTimer;
    private readonly DispatcherTimer _autoMetricsTimer;
    private double _orbOpacity = 1.0;
    private bool _orbFadeDir = true;
    private bool _isMeasuring = false;

    [ObservableProperty]
    private string _proxyPortText = "1080";

    [ObservableProperty]
    private string _proxySecretText = "";

    [ObservableProperty]
    private string _launchLogText = "";

    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private string _statusTitle = "Прокси-сервер не запущен";

    [ObservableProperty]
    private string _serviceNote = "Работает параллельно с основным обходом";

    [ObservableProperty]
    private string _actionButtonText = "Запустить прокси";

    [ObservableProperty]
    private bool _isConnectTelegramEnabled = false;

    [ObservableProperty]
    private double _statusOrbInnerOpacity = 1.0;

    [ObservableProperty]
    private SolidColorBrush _statusOrbBg = new(Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2F));

    [ObservableProperty]
    private SolidColorBrush _statusOrbInnerBg = new(Windows.UI.Color.FromArgb(0xFF, 0x4B, 0x4B, 0x54));

    [ObservableProperty]
    private SolidColorBrush _statusIconFg = new(Windows.UI.Color.FromArgb(0xFF, 0xA1, 0xA1, 0xA6));

    [ObservableProperty]
    private string _statusIndicatorIconGlyph = "\xE73E";

    [ObservableProperty]
    private string _proxyPortDisplayText = "127.0.0.1:1080";

    [ObservableProperty]
    private string _dc2PingText = "-- мс";

    [ObservableProperty]
    private SolidColorBrush _dc2PingBrush = new(Windows.UI.Color.FromArgb(255, 161, 161, 170));

    [ObservableProperty]
    private string _dc4PingText = "-- мс";

    [ObservableProperty]
    private SolidColorBrush _dc4PingBrush = new(Windows.UI.Color.FromArgb(255, 161, 161, 170));

    public TgWsProxyViewModel()
    {
        _orbTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _orbTimer.Tick += OrbTimer_Tick;

        _autoMetricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoMetricsTimer.Tick += (s, e) => _ = MeasureTgMetricsAsync();

        ProxyPortText = SettingsManager.Instance.TgWsProxyPort.ToString();
        ProxySecretText = SettingsManager.Instance.TgWsProxySecret;

        var sb = new StringBuilder();
        foreach (var log in TgWsProxyService.GetLogHistory())
        {
            sb.AppendLine(log);
        }
        LaunchLogText = sb.ToString();
    }

    public void OnNavigatedTo()
    {
        TgWsProxyService.OnStatusChanged += OnStatusChangedHandler;
        TgWsProxyService.OnLogReceived += OnLogReceivedHandler;

        UpdateStatusUI(TgWsProxyService.IsRunning);
        _ = MeasureTgMetricsAsync();
        _autoMetricsTimer.Start();
    }

    public void OnNavigatedFrom()
    {
        TgWsProxyService.OnStatusChanged -= OnStatusChangedHandler;
        TgWsProxyService.OnLogReceived -= OnLogReceivedHandler;

        _orbTimer.Stop();
        _autoMetricsTimer.Stop();
    }

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    private void OnStatusChangedHandler(bool isRunning)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => UpdateStatusUI(isRunning));
        }
        else
        {
            UpdateStatusUI(isRunning);
        }
    }

    private void OnLogReceivedHandler(string logLine)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => LaunchLogText += logLine + "\n");
        }
        else
        {
            LaunchLogText += logLine + "\n";
        }
    }

    public void UpdateStatusUI(bool isRunning)
    {
        IsRunning = isRunning;

        if (isRunning)
        {
            StatusOrbBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x3F, 0x24));
            StatusOrbInnerBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x10, 0x7C, 0x10));
            StatusIconFg = new SolidColorBrush(Microsoft.UI.Colors.White);
            StatusIndicatorIconGlyph = "\xE73E";
            StatusTitle = "Прокси-сервер запущен";
            ServiceNote = $"Локальный прокси слушает порт {SettingsManager.Instance.TgWsProxyPort}";

            ActionButtonText = "Остановить прокси";
            IsConnectTelegramEnabled = true;

            _orbTimer.Start();
        }
        else
        {
            StatusOrbBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2F));
            StatusOrbInnerBg = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x4B, 0x4B, 0x54));
            StatusIconFg = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0xA1, 0xA1, 0xA6));
            StatusIndicatorIconGlyph = "\xE73E";
            StatusTitle = "Прокси-сервер не запущен";
            ServiceNote = "Работает параллельно с основным обходом";

            ActionButtonText = "Запустить прокси";
            IsConnectTelegramEnabled = false;

            _orbTimer.Stop();
            StatusOrbInnerOpacity = 1.0;
        }
    }

    private void OrbTimer_Tick(object? sender, object e)
    {
        if (_orbFadeDir)
        {
            _orbOpacity -= 0.05;
            if (_orbOpacity <= 0.4) _orbFadeDir = false;
        }
        else
        {
            _orbOpacity += 0.05;
            if (_orbOpacity >= 1.0) _orbFadeDir = true;
        }
        StatusOrbInnerOpacity = _orbOpacity;
    }

    [RelayCommand]
    private void ToggleProxy()
    {
        if (TgWsProxyService.IsRunning)
        {
            TgWsProxyService.StopProxy();
        }
        else
        {
            if (SaveCurrentSettings(null))
            {
                TgWsProxyService.StartProxy();
            }
        }
    }

    [RelayCommand]
    private void ConnectTelegram()
    {
        var port = SettingsManager.Instance.TgWsProxyPort;
        var host = SettingsManager.Instance.TgWsProxyHost;
        var secret = SettingsManager.Instance.TgWsProxySecret;

        string link = !string.IsNullOrWhiteSpace(secret)
            ? $"tg://proxy?server={host}&port={port}&secret={secret}"
            : $"tg://socks?server={host}&port={port}";

        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LaunchLogText += $"[GUI ERROR] Не удалось открыть ссылку в Telegram: {ex.Message}\n";
        }
    }

    [RelayCommand]
    private void RegenerateSecret()
    {
        var randomBytes = new byte[16];
        RandomNumberGenerator.Fill(randomBytes);
        ProxySecretText = Convert.ToHexString(randomBytes).ToLower();
    }

    [RelayCommand]
    private void CopyLog()
    {
        var package = new DataPackage();
        package.SetText(LaunchLogText);
        Clipboard.SetContent(package);
    }

    [RelayCommand]
    private void ClearLog()
    {
        LaunchLogText = string.Empty;
    }

    public bool SaveCurrentSettings(XamlRoot? xamlRoot)
    {
        if (!int.TryParse(ProxyPortText, out var port) || port <= 0 || port > 65535)
        {
            if (xamlRoot != null)
            {
                var dialog = new ContentDialog
                {
                    Title = "Ошибка",
                    Content = "Пожалуйста, введите корректный порт (1 - 65535).",
                    CloseButtonText = "ОК",
                    XamlRoot = xamlRoot
                };
                _ = dialog.ShowAsync();
            }
            return false;
        }

        SettingsManager.Instance.TgWsProxyPort = port;
        SettingsManager.Instance.TgWsProxySecret = ProxySecretText.Trim();
        SettingsManager.Save();

        if (TgWsProxyService.IsRunning)
        {
            LaunchLogText += "[GUI] Настройки сохранены. Перезапустите прокси для применения настроек.\n";
        }

        return true;
    }

    private async Task MeasureTgMetricsAsync()
    {
        if (_isMeasuring) return;
        _isMeasuring = true;

        try
        {
            int port = SettingsManager.Instance.TgWsProxyPort;
            ProxyPortDisplayText = $"127.0.0.1:{port}";

            var dc2Task = NetworkLatencyService.MeasureTgProxyDcLatencyAsync("DC2", port, 1000);
            var dc4Task = NetworkLatencyService.MeasureTgProxyDcLatencyAsync("DC4", port, 1000);

            await Task.WhenAll(dc2Task, dc4Task);

            var res2 = dc2Task.Result;
            var res4 = dc4Task.Result;

            Dc2PingText = res2.FormattedText;
            Dc2PingBrush = res2.IsSuccess
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 161, 161, 170));

            Dc4PingText = res4.FormattedText;
            Dc4PingBrush = res4.IsSuccess
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 16, 185, 129))
                : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 161, 161, 170));
        }
        finally
        {
            _isMeasuring = false;
        }
    }
}
