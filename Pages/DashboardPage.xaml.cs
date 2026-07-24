using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZapretMirrlyGUI.ViewModels;
using ZapretMirrlyGUI.Services;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; } = new();

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += DashboardPage_Loaded;
        Unloaded += DashboardPage_Unloaded;

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.LaunchLogText))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null, true);
                });
            }
        };
    }

    private bool _isFirstLoad = true;

    private void DashboardPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        if (_isFirstLoad)
        {
            _isFirstLoad = false;

            // Trigger entrance animations once on initial load
            AnimationHelper.AnimateElementEntrance(TopMetricsPanel, 0, -25, 1.0, 220, 0);
            AnimationHelper.AnimateElementEntrance(DashboardTopHeaderGrid, 0, -15, 1.0, 240, 20);
            AnimationHelper.AnimateElementEntrance(MonitorCard, -50, 0, 0.96, 260, 40);
            AnimationHelper.AnimateOrbLiquidPulse(StatusOrbOuter, 340, 80);
            AnimationHelper.AnimateElementEntrance(LaunchLogCard, 0, 40, 0.95, 280, 100);
            AnimationHelper.AnimateElementEntrance(StrategyCard, 60, 0, 0.96, 260, 120);
            AnimationHelper.AnimateElementEntrance(ArgumentsCard, 60, 0, 0.96, 280, 160);
        }
    }

    private void DashboardPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();
    }
}
