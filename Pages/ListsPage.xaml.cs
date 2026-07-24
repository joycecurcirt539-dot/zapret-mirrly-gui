using System;
using Microsoft.UI.Xaml.Controls;
using ZapretMirrlyGUI.ViewModels;

namespace ZapretMirrlyGUI.Pages;

public class StringToGeometryConverter : Microsoft.UI.Xaml.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string pathData && !string.IsNullOrWhiteSpace(pathData))
        {
            try
            {
                var xaml = $"<Geometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">{pathData}</Geometry>";
                return Microsoft.UI.Xaml.Markup.XamlReader.Load(xaml);
            }
            catch { }
        }
        return null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed partial class ListsPage : Page
{
    public ListsViewModel ViewModel { get; } = new();

    public ListsPage()
    {
        InitializeComponent();
        Loaded += ListsPage_Loaded;
    }

    private bool _isFirstLoad = true;

    private void ListsPage_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            // Animate master list sidebar emerging directly from main sidebar
            Services.AnimationHelper.AnimateElementEntrance(ListsSidebarGrid, -120, 0, 1.0, 260, 0);
            // Animate right editor workspace area
            Services.AnimationHelper.AnimateElementEntrance(ListsEditorGrid, 50, 0, 0.95, 280, 50);
        }
    }
}
