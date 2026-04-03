using Microsoft.UI.Xaml.Controls;

namespace PhotoView.Contracts.Services;

public interface INavigationViewService
{
    IList<object>? MenuItems
    {
        get;
    }

    object? SettingsItem
    {
        get;
    }

    NavigationViewPaneDisplayMode PaneDisplayMode
    {
        get;
        set;
    }

    void Initialize(NavigationView navigationView);

    void UnregisterEvents();

    NavigationViewItem? GetSelectedItem(Type pageType);

    string? GetSelectedItemKey();

    string? GetNameForItem(NavigationViewItemBase item);
}
