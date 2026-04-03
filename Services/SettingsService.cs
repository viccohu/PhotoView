using Microsoft.UI.Xaml.Controls;
using PhotoView.Contracts.Services;

namespace PhotoView.Services;

public class SettingsService : ISettingsService
{
    private readonly ILocalSettingsService _localSettingsService;

    public event EventHandler<NavigationViewPaneDisplayMode> NavigationViewModeChanged;

    public NavigationViewPaneDisplayMode NavigationViewMode
    {
        get => LoadNavigationViewMode();
        set
        {
            SaveNavigationViewMode(value);
            NavigationViewModeChanged?.Invoke(this, value);
        }
    }

    public SettingsService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public void SaveNavigationViewMode(NavigationViewPaneDisplayMode mode)
    {
        _localSettingsService.SaveSettingAsync("NavigationViewMode", mode.ToString()).Wait();
    }

    public NavigationViewPaneDisplayMode LoadNavigationViewMode()
    {
        var mode = _localSettingsService.ReadSettingAsync<string>("NavigationViewMode").Result;
        if (Enum.TryParse<NavigationViewPaneDisplayMode>(mode, out var result))
        {
            return result;
        }
        return NavigationViewPaneDisplayMode.Top; // 默认值
    }
}