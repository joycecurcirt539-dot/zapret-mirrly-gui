using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZapretMirrlyGUI.ViewModels;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class TgWsProxyPage : Page
{
    public TgWsProxyViewModel ViewModel { get; } = new();

    public TgWsProxyPage()
    {
        InitializeComponent();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.LaunchLogText))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
                });
            }
        };

        Loaded += TgWsProxyPage_Loaded;
    }

    private bool _isFirstLoad = true;

    private void TgWsProxyPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            Services.AnimationHelper.AnimateElementEntrance(TopTgMetricsPanel, 0, -30, 1.0, 200, 0);
            Services.AnimationHelper.AnimateElementEntrance(TgLeftMonitorPanel, -40, 0, 0.98, 240, 30);
            Services.AnimationHelper.AnimateOrbLiquidPulse(StatusOrbOuter, 340, 60);
            Services.AnimationHelper.AnimateElementEntrance(ConnectTelegramButton, 0, 0, 0.90, 260, 80, new Microsoft.UI.Xaml.Media.Animation.BackEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut, Amplitude = 0.5 });
            Services.AnimationHelper.AnimateElementEntrance(TgLogCard, 0, 40, 0.95, 270, 120);
            Services.AnimationHelper.AnimateElementEntrance(TgRightScrollViewer, 50, 0, 0.96, 260, 140);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.OnNavigatedTo();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.OnNavigatedFrom();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SaveCurrentSettings(this.XamlRoot))
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

    private void CopyPsCommandButton_Click(object sender, RoutedEventArgs e)
    {
        var psCommand = "Set-NetIPInterface -Forwarding Enabled; reg add HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters /v IPEnableRouter /t REG_DWORD /d 1 /f; netsh interface portproxy reset; netsh interface portproxy add v4tov4 listenport=1443 listenaddress=0.0.0.0 connectport=1080 connectaddress=127.0.0.1; New-NetFirewallRule -DisplayName \"TgWsProxy Tailscale 1443\" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 1443; net stop tailscaled; net start tailscaled";
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(psCommand);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

        OverlayNotificationWindow.ShowToast(true, true, "Команда PowerShell скопирована в буфер!");
    }

    private void ShowMobileGuideButton_Click(object sender, RoutedEventArgs e)
    {
        if (App.Current is App myApp && myApp.MainWindowInstance != null)
        {
            myApp.MainWindowInstance.NavigateToPublic("guide", 1);
        }
    }
}
