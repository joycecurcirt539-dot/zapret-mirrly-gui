using System.IO;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class SupportPage : Page
{
    public SupportPage()
    {
        InitializeComponent();
        
        var assetsPath = AssetsExtractor.GetAssetsPath();
        var qrPath = Path.Combine(assetsPath, "dalink-qr-code.png");
        if (!File.Exists(qrPath))
        {
            qrPath = Path.Combine(AppContext.BaseDirectory, "Assets", "dalink-qr-code.png");
        }
        if (File.Exists(qrPath))
        {
            QrCodeImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(qrPath));
        }
    }

    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
}
