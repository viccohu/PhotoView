using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using PhotoView.Contracts.Services;

namespace PhotoView.Behaviors;

public class NavigationViewHeaderBehavior
{
    public DataTemplate? DefaultHeaderTemplate
    {
        get; set;
    }

    public object? DefaultHeader
    {
        get; set;
    }

    public static NavigationViewHeaderMode GetHeaderMode(Page item) => NavigationViewHeaderMode.Always;

    public static void SetHeaderMode(Page item, NavigationViewHeaderMode value) { }

    public static object? GetHeaderContext(Page item) => null;

    public static void SetHeaderContext(Page item, object value) { }

    public static DataTemplate? GetHeaderTemplate(Page item) => null;

    public static void SetHeaderTemplate(Page item, DataTemplate value) { }
}
