using Microsoft.UI.Xaml.Controls;
using PhotoView.Contracts.Services;

namespace PhotoView.Services;

public class SettingsService : ISettingsService
{
    private readonly ILocalSettingsService _localSettingsService;
    private NavigationViewPaneDisplayMode _navigationViewMode = NavigationViewPaneDisplayMode.Top;

    public event EventHandler<NavigationViewPaneDisplayMode>? NavigationViewModeChanged;

    public NavigationViewPaneDisplayMode NavigationViewMode
    {
        get => _navigationViewMode;
        set
        {
            if (_navigationViewMode != value)
            {
                _navigationViewMode = value;
                NavigationViewModeChanged?.Invoke(this, value);
            }
        }
    }

    public SettingsService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task SaveNavigationViewModeAsync(NavigationViewPaneDisplayMode mode)
    {
        await _localSettingsService.SaveSettingAsync("NavigationViewMode", mode.ToString()).ConfigureAwait(false);
    }

    public async Task<NavigationViewPaneDisplayMode> LoadNavigationViewModeAsync()
    {
        var mode = await _localSettingsService.ReadSettingAsync<string>("NavigationViewMode").ConfigureAwait(false);
        if (Enum.TryParse<NavigationViewPaneDisplayMode>(mode, out var result))
        {
            _navigationViewMode = result;
            return result;
        }
        return NavigationViewPaneDisplayMode.Top;
    }
}