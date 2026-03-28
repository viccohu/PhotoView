using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoView.ViewModels;

namespace PhotoView.Views;

public sealed partial class CollectPage : Page
{
    public CollectViewModel ViewModel
    {
        get;
    }

    public CollectPage()
    {
        ViewModel = App.GetService<CollectViewModel>();
        InitializeComponent();
    }

    private void Image_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            FlyoutBase.ShowAttachedFlyout(element);
        }
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
    }
}
