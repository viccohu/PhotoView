using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
}
