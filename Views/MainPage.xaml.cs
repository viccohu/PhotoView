using System.Xml.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoView.Models;
using PhotoView.ViewModels;
using Windows.Foundation;
using static System.Net.Mime.MediaTypeNames;
using Image = Microsoft.UI.Xaml.Controls.Image;

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
    }
    private void ShowMenu(bool isTransient, UIElement element)
    {
        FlyoutShowOptions myOption = new FlyoutShowOptions();
        myOption.ShowMode = isTransient ? FlyoutShowMode.Transient : FlyoutShowMode.Standard;
        CommandBarFlyout1.ShowAt(element, myOption);
    }


    private void imageButton_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        //var element = templateRoot.FindName("ItemImage") as Image;        
        ShowMenu(false, sender);
    }

    private void ResizeButton1_Click(object sender, RoutedEventArgs e)
    {

    }
    private void OnElementClicked(object sender, RoutedEventArgs e)
    {

    }
}
