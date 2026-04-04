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

    ThumbnailSize ThumbnailSize {
        get;
        set;
    }

    bool RememberLastFolder {
        get;
        set;
    }

    bool DeleteToRecycleBin {
        get;
        set;
    }

    string LastFolderPath {
        get;
        set;
    }

    event EventHandler<NavigationViewPaneDisplayMode> NavigationViewModeChanged;
    event EventHandler<int> BatchSizeChanged;
    event EventHandler<PerformanceMode>? PerformanceModeChanged;
    event EventHandler<ThumbnailSize>? ThumbnailSizeChanged;
    event EventHandler<bool>? RememberLastFolderChanged;
    event EventHandler<bool>? DeleteToRecycleBinChanged;

    Task SaveNavigationViewModeAsync(NavigationViewPaneDisplayMode mode);
    Task<NavigationViewPaneDisplayMode> LoadNavigationViewModeAsync();
    Task SaveBatchSizeAsync(int batchSize);
    Task<int> LoadBatchSizeAsync();
    Task SavePerformanceModeAsync(PerformanceMode mode);
    Task<PerformanceMode> LoadPerformanceModeAsync();
    Task SaveThumbnailSizeAsync(ThumbnailSize size);
    Task<ThumbnailSize> LoadThumbnailSizeAsync();
    Task SaveRememberLastFolderAsync(bool remember);
    Task<bool> LoadRememberLastFolderAsync();
    Task SaveDeleteToRecycleBinAsync(bool deleteToRecycleBin);
    Task<bool> LoadDeleteToRecycleBinAsync();
    Task SaveLastFolderPathAsync(string path);
    Task<string> LoadLastFolderPathAsync();

    Task InitializeAsync();
}