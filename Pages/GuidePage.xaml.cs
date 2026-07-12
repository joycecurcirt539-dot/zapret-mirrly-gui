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
        OpenUrl("https://github.com/bolvan/zapret");
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
