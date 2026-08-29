using System;
using System.IO;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class GuidePage : Page
{
    public GuidePage()
    {
        InitializeComponent();
        Loaded += GuidePage_Loaded;

        // Load TG Proxy Android Logo
        var assetsPath = AssetsExtractor.GetAssetsPath();
        var logoPath = Path.Combine(assetsPath, "tg-proxy-logo.png");
        if (!File.Exists(logoPath))
        {
            logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tg-proxy-logo.png");
        }
        if (File.Exists(logoPath))
        {
            TgProxyLogoImage.Source = new BitmapImage(new Uri(logoPath));
        }
    }

    private bool _isFirstLoad = true;

    private void GuidePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            AnimationHelper.AnimateElementEntrance(GuideHeaderPanel, 0, -30, 1.0, 200, 0);
            AnimationHelper.AnimateElementEntrance(GuideCol1, -40, 40, 0.95, 260, 40);
            AnimationHelper.AnimateElementEntrance(GuideCol2, 0, 50, 0.95, 260, 85);
            AnimationHelper.AnimateElementEntrance(GuideCol3, 40, 40, 0.95, 260, 130);

            // Tab 2 Elements Animation
            AnimationHelper.AnimateElementEntrance(TgProxyHeaderPanel, 0, -30, 1.0, 200, 0);
            AnimationHelper.AnimateElementEntrance(TgProxyCol1, -40, 40, 0.95, 260, 40);
            AnimationHelper.AnimateElementEntrance(TgProxyCol2, 40, 40, 0.95, 260, 90);
        }
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is int tabIndex && tabIndex >= 0 && tabIndex < GuideTabView.TabItems.Count)
        {
            GuideTabView.SelectedIndex = tabIndex;
        }
    }

    public void SelectTab(int tabIndex)
    {
        if (tabIndex >= 0 && tabIndex < GuideTabView.TabItems.Count)
        {
            GuideTabView.SelectedIndex = tabIndex;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Tab 1: Safety & General Help Handlers
    // ─────────────────────────────────────────────────────────────
    private void OpenOriginalZapretGithub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/bol-van/zapret");
    }

    private void OpenFlowsealGithub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/Flowseal");
    }

    private void OpenGoodbyeDpiGithub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/ValdikSS/GoodbyeDPI");
    }

    private void OpenWinDivertGithub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/basil00/Divert");
    }

    private void OpenMirrlyGithub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/joycecurcirt539-dot/Zapret-Mirrly-GUI");
    }

    // ─────────────────────────────────────────────────────────────
    // Tab 2: Mirrly TG Proxy (Android) Handlers
    // ─────────────────────────────────────────────────────────────
    private void OpenTgProxyReleases_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/joycecurcirt539-dot/Mirrly-TG-Proxy/releases");
    }

    private void OpenTgProxyGithub_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/joycecurcirt539-dot/Mirrly-TG-Proxy");
    }

    private void OpenTgProxyTelegram_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://t.me/WhyOkyHb");
    }

    private void CopyDeployWorkerScript_Click(object sender, RoutedEventArgs e)
    {
        var psCommand = "irm https://raw.githubusercontent.com/joycecurcirt539-dot/Mirrly-TG-Proxy/main/tools/deploy-worker/deploy.ps1 | iex";
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(psCommand);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

        OverlayNotificationWindow.ShowToast(true, true, "Команда деплоя воркера скопирована в буфер!");
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
}
