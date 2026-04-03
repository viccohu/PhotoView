using Microsoft.UI.Xaml.Controls;

namespace PhotoView.Contracts.Services;

public interface ISettingsService
{
    NavigationViewPaneDisplayMode NavigationViewMode {
        get;
        set;
    }

    event EventHandler<NavigationViewPaneDisplayMode> NavigationViewModeChanged;

    void SaveNavigationViewMode(NavigationViewPaneDisplayMode mode);
    NavigationViewPaneDisplayMode LoadNavigationViewMode();
}