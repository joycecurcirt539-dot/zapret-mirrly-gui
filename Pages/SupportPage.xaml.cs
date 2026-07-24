using System.IO;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
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

    private bool _isFirstLoad = true;

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Start QR animations
        QrSpinStoryboard.Begin();
        QrGlowStoryboard.Begin();

        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            // Trigger smooth page entrance animations once on initial load
            AnimationHelper.AnimateElementEntrance(SupportHeaderPanel, 0, -20, 1.0, 220, 0);
            AnimationHelper.AnimateElementEntrance(SupportQrHeroCard, -80, 0, 0.88, 300, 40, new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });
            AnimationHelper.AnimateElementEntrance(SupportRightColumn, 60, 20, 0.96, 280, 80);
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
