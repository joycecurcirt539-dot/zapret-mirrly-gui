using System;
using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class TgWsProxyPage : Page
{
    private readonly DispatcherTimer _orbTimer;
    private double _orbOpacity = 1.0;
    private bool _orbFadeDir = true;

    public TgWsProxyPage()
    {
        this.InitializeComponent();

        _orbTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _orbTimer.Tick += OrbTimer_Tick;

        // Initialize UI settings from SettingsManager
        ProxyPortTextBox.Text = SettingsManager.Instance.TgWsProxyPort.ToString();
        ProxySecretTextBox.Text = SettingsManager.Instance.TgWsProxySecret;

        // Load log history
        var sb = new StringBuilder();
        foreach (var log in TgWsProxyService.GetLogHistory())
        {
            sb.AppendLine(log);
        }
        LaunchLogTextBlock.Text = sb.ToString();
        LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        TgWsProxyService.OnStatusChanged += OnStatusChangedHandler;
        TgWsProxyService.OnLogReceived += OnLogReceivedHandler;

        UpdateStatusUI(TgWsProxyService.IsRunning);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        TgWsProxyService.OnStatusChanged -= OnStatusChangedHandler;
        TgWsProxyService.OnLogReceived -= OnLogReceivedHandler;

        _orbTimer.Stop();
    }

    private void OnStatusChangedHandler(bool isRunning)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateStatusUI(isRunning);
        });
    }

    private void OnLogReceivedHandler(string logLine)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LaunchLogTextBlock.Text += logLine + "\n";
            LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
        });
    }

    private void UpdateStatusUI(bool isRunning)
    {
        if (isRunning)
        {
            // Active Green theme
            StatusOrbBg.Color = Windows.UI.Color.FromArgb(0xFF, 0x1F, 0x3F, 0x24); // Dark Green
            StatusOrbInnerBg.Color = Windows.UI.Color.FromArgb(0xFF, 0x10, 0x7C, 0x10); // Bright Green
            StatusIconFg.Color = Microsoft.UI.Colors.White;
            StatusIndicatorIcon.Glyph = "\xE73E"; // Globe/Connected
            StatusText.Text = "Прокси-сервер запущен";
            ServiceStatusText.Text = $"Локальный прокси слушает порт {SettingsManager.Instance.TgWsProxyPort}";

            ActionBypassText.Text = "Остановить прокси";
            ConnectTelegramButton.IsEnabled = true;

            _orbTimer.Start();
        }
        else
        {
            // Inactive Grey theme
            StatusOrbBg.Color = Windows.UI.Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2F); // Dark Grey
            StatusOrbInnerBg.Color = Windows.UI.Color.FromArgb(0xFF, 0x4B, 0x4B, 0x54); // Medium Grey
            StatusIconFg.Color = Windows.UI.Color.FromArgb(0xFF, 0xA1, 0xA1, 0xA6);
            StatusIndicatorIcon.Glyph = "\xE73E";
            StatusText.Text = "Прокси-сервер не запущен";
            ServiceStatusText.Text = "Работает параллельно с основным обходом";

            ActionBypassText.Text = "Запустить прокси";
            ConnectTelegramButton.IsEnabled = false;

            _orbTimer.Stop();
            StatusOrbInner.Opacity = 1.0;
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
        StatusOrbInner.Opacity = _orbOpacity;
    }

    private void ActionBypassButton_Click(object sender, RoutedEventArgs e)
    {
        if (TgWsProxyService.IsRunning)
        {
            TgWsProxyService.StopProxy();
        }
        else
        {
            // Save settings first
            SaveCurrentSettings();
            TgWsProxyService.StartProxy();
        }
    }

    private void ConnectTelegramButton_Click(object sender, RoutedEventArgs e)
    {
        var port = SettingsManager.Instance.TgWsProxyPort;
        var host = SettingsManager.Instance.TgWsProxyHost;
        var secret = SettingsManager.Instance.TgWsProxySecret;

        string link;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            link = $"tg://proxy?server={host}&port={port}&secret={secret}";
        }
        else
        {
            link = $"tg://socks?server={host}&port={port}";
        }

        try
        {
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LaunchLogTextBlock.Text += $"[GUI ERROR] Не удалось открыть ссылку в Telegram: {ex.Message}\n";
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveCurrentSettings())
        {
            var dialog = new ContentDialog
            {
                Title = "Успех",
                Content = "Настройки успешно сохранены.",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }
    }

    private bool SaveCurrentSettings()
    {
        if (!int.TryParse(ProxyPortTextBox.Text, out var port) || port <= 0 || port > 65535)
        {
            var dialog = new ContentDialog
            {
                Title = "Ошибка",
                Content = "Пожалуйста, введите корректный порт (1 - 65535).",
                CloseButtonText = "ОК",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
            return false;
        }

        SettingsManager.Instance.TgWsProxyPort = port;
        SettingsManager.Instance.TgWsProxySecret = ProxySecretTextBox.Text.Trim();

        SettingsManager.Save();

        // If the service is currently running, notify that a restart is required
        if (TgWsProxyService.IsRunning)
        {
            LaunchLogTextBlock.Text += "[GUI] Настройки сохранены. Перезапустите прокси для применения настроек.\n";
        }

        return true;
    }

    private void CopyLogButton_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(LaunchLogTextBlock.Text);
        Clipboard.SetContent(package);
    }
    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        LaunchLogTextBlock.Text = string.Empty;
    }

    private void RegenerateSecretButton_Click(object sender, RoutedEventArgs e)
    {
        var randomBytes = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);
        ProxySecretTextBox.Text = Convert.ToHexString(randomBytes).ToLower();
    }
}
