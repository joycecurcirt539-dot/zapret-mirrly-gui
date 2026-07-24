using System;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class GuidePage : Page
{
    public GuidePage()
    {
        InitializeComponent();
        Loaded += GuidePage_Loaded;
    }

    private bool _isFirstLoad = true;

    private void GuidePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            Services.AnimationHelper.AnimateElementEntrance(GuideHeaderPanel, 0, -30, 1.0, 200, 0);
            Services.AnimationHelper.AnimateElementEntrance(GuideCol1, -40, 40, 0.95, 260, 40);
            Services.AnimationHelper.AnimateElementEntrance(GuideCol2, 0, 50, 0.95, 260, 85);
            Services.AnimationHelper.AnimateElementEntrance(GuideCol3, 40, 40, 0.95, 260, 130);
        }
    }

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

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
}
