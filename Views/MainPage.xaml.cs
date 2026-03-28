using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoView.Models;
using PhotoView.ViewModels;

namespace PhotoView.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel
    {
        get;
    }

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();
        FolderTreeView.DataContext = ViewModel;
    }

    private async void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            await ViewModel.LoadChildrenAsync(node);
        }
    }

    private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNode node)
        {
            await ViewModel.LoadImagesAsync(node);
        }
    }

    private async void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            await ViewModel.LoadImagesAsync(node);
        }
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
