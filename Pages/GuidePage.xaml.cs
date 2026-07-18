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
