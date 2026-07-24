using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZapretMirrlyGUI.ViewModels;

namespace ZapretMirrlyGUI.Pages;

public sealed partial class LogsPage : Page
{
    public LogsViewModel ViewModel { get; } = new();

    public LogsPage()
    {
        InitializeComponent();
        Loaded += LogsPage_Loaded;
        Unloaded += LogsPage_Unloaded;

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.LogText))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LogScrollViewer.ChangeView(null, LogScrollViewer.ScrollableHeight, null);
                });
            }
        };
    }

    private bool _isFirstLoad = true;

    private void LogsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnLoaded();

        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            Services.AnimationHelper.AnimateElementEntrance(LogsHeaderGrid, 0, -25, 1.0, 220, 0);
            Services.AnimationHelper.AnimateElementEntrance(LogsToolDeckCard, 0, -35, 0.96, 230, 0);
            Services.AnimationHelper.AnimateElementEntrance(LogsViewportCard, 0, 50, 0.97, 290, 70);
        }
    }

    private void LogsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();
    }
}
