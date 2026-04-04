using Microsoft.UI.Xaml.Controls;
using PhotoView.Models;

namespace PhotoView.Contracts.Services;

public interface ISettingsService
{
    NavigationViewPaneDisplayMode NavigationViewMode {
        get;
        set;
    }

    int BatchSize {
        get;
        set;
    }

    PerformanceMode PerformanceMode {
        get;
        set;
    }

    event EventHandler<NavigationViewPaneDisplayMode> NavigationViewModeChanged;
    event EventHandler<int> BatchSizeChanged;
    event EventHandler<PerformanceMode>? PerformanceModeChanged;

    Task SaveNavigationViewModeAsync(NavigationViewPaneDisplayMode mode);
    Task<NavigationViewPaneDisplayMode> LoadNavigationViewModeAsync();
    Task SaveBatchSizeAsync(int batchSize);
    Task<int> LoadBatchSizeAsync();
    Task SavePerformanceModeAsync(PerformanceMode mode);
    Task<PerformanceMode> LoadPerformanceModeAsync();
}