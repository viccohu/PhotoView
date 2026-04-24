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

    string ExportLastFolderPath {
        get;
        set;
    }

    bool ExportImageEnabled {
        get;
        set;
    }

    int ExportImageMinRating {
        get;
        set;
    }

    string ExportImageFolderName {
        get;
        set;
    }

    bool ExportRawEnabled {
        get;
        set;
    }

    int ExportRawMinRating {
        get;
        set;
    }

    string ExportRawFolderName {
        get;
        set;
    }

    double DecodeScaleFactor {
        get;
        set;
    }

    bool AlwaysDecodeRaw {
        get;
        set;
    }

    bool MainPageAutoCollapseSidebar {
        get;
        set;
    }

    bool CollectPageLoadDrawerCollapsed {
        get;
        set;
    }

    bool PreferPsdAsPrimaryPreview {
        get;
        set;
    }

    bool AutoExpandBurstOnDirectionalNavigation {
        get;
        set;
    }

    bool CollapseBurstGroups {
        get;
        set;
    }

    bool AutoSyncGroupRatings {
        get;
        set;
    }

    event EventHandler<NavigationViewPaneDisplayMode> NavigationViewModeChanged;
    event EventHandler<int> BatchSizeChanged;
    event EventHandler<PerformanceMode>? PerformanceModeChanged;
    event EventHandler<ThumbnailSize>? ThumbnailSizeChanged;
    event EventHandler<bool>? RememberLastFolderChanged;
    event EventHandler<bool>? DeleteToRecycleBinChanged;
    event EventHandler<bool>? AlwaysDecodeRawChanged;
    event EventHandler<bool>? MainPageAutoCollapseSidebarChanged;
    event EventHandler<bool>? CollectPageLoadDrawerCollapsedChanged;
    event EventHandler<bool>? PreferPsdAsPrimaryPreviewChanged;
    event EventHandler<bool>? AutoExpandBurstOnDirectionalNavigationChanged;
    event EventHandler<bool>? CollapseBurstGroupsChanged;
    event EventHandler<bool>? AutoSyncGroupRatingsChanged;

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
    Task SaveFolderAccessHistoryAsync(FolderAccessHistory history);
    Task<FolderAccessHistory> LoadFolderAccessHistoryAsync();
    Task SaveExportLastFolderPathAsync(string path);
    Task<string> LoadExportLastFolderPathAsync();
    Task SaveExportImageEnabledAsync(bool enabled);
    Task<bool> LoadExportImageEnabledAsync();
    Task SaveExportImageMinRatingAsync(int rating);
    Task<int> LoadExportImageMinRatingAsync();
    Task SaveExportImageFolderNameAsync(string name);
    Task<string> LoadExportImageFolderNameAsync();
    Task SaveExportRawEnabledAsync(bool enabled);
    Task<bool> LoadExportRawEnabledAsync();
    Task SaveExportRawMinRatingAsync(int rating);
    Task<int> LoadExportRawMinRatingAsync();
    Task SaveExportRawFolderNameAsync(string name);
    Task<string> LoadExportRawFolderNameAsync();
    Task SaveDecodeScaleFactorAsync(double factor);
    Task<double> LoadDecodeScaleFactorAsync();
    Task SaveAlwaysDecodeRawAsync(bool alwaysDecode);
    Task<bool> LoadAlwaysDecodeRawAsync();
    Task SaveMainPageAutoCollapseSidebarAsync(bool enabled);
    Task<bool> LoadMainPageAutoCollapseSidebarAsync();
    Task SaveCollectPageLoadDrawerCollapsedAsync(bool collapsed);
    Task<bool> LoadCollectPageLoadDrawerCollapsedAsync();
    Task SavePreferPsdAsPrimaryPreviewAsync(bool enabled);
    Task<bool> LoadPreferPsdAsPrimaryPreviewAsync();
    Task SaveAutoExpandBurstOnDirectionalNavigationAsync(bool enabled);
    Task<bool> LoadAutoExpandBurstOnDirectionalNavigationAsync();
    Task SaveCollapseBurstGroupsAsync(bool enabled);
    Task<bool> LoadCollapseBurstGroupsAsync();
    Task SaveAutoSyncGroupRatingsAsync(bool enabled);
    Task<bool> LoadAutoSyncGroupRatingsAsync();
    void SuspendAlwaysDecodeRawPersistence(string reason);
    Task ResumeAlwaysDecodeRawPersistenceAsync(string reason);

    Task InitializeAsync();
}
