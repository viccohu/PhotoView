using Microsoft.UI.Xaml.Controls;
using PhotoView.Contracts.Services;
using PhotoView.Models;

namespace PhotoView.Services;

public class SettingsService : ISettingsService
{
    private readonly ILocalSettingsService _localSettingsService;
    private NavigationViewPaneDisplayMode _navigationViewMode = NavigationViewPaneDisplayMode.Top;
    private int _batchSize = 30;
    private PerformanceMode _performanceMode = PerformanceMode.Smart;
    private ThumbnailSize _thumbnailSize = ThumbnailSize.Medium;
    private bool _rememberLastFolder = false;
    private bool _deleteToRecycleBin = true;
    private string _lastFolderPath = string.Empty;
    private string _exportLastFolderPath = string.Empty;
    private bool _exportImageEnabled = true;
    private int _exportImageMinRating = 1;
    private string _exportImageFolderName = string.Empty;
    private bool _exportRawEnabled = false;
    private int _exportRawMinRating = 1;
    private string _exportRawFolderName = "RAW";
    private double _decodeScaleFactor = 2.0;
    private bool _alwaysDecodeRaw = false;
    private bool _mainPageAutoCollapseSidebar = false;
    private bool _collectPageLoadDrawerCollapsed = false;
    private bool _preferPsdAsPrimaryPreview = false;
    private bool _autoExpandBurstOnDirectionalNavigation = true;
    private bool _collapseBurstGroups = true;
    private bool _autoSyncGroupRatings = true;

    public event EventHandler<NavigationViewPaneDisplayMode>? NavigationViewModeChanged;
    public event EventHandler<int>? BatchSizeChanged;
    public event EventHandler<PerformanceMode>? PerformanceModeChanged;
    public event EventHandler<ThumbnailSize>? ThumbnailSizeChanged;
    public event EventHandler<bool>? RememberLastFolderChanged;
    public event EventHandler<bool>? DeleteToRecycleBinChanged;
    public event EventHandler<bool>? AlwaysDecodeRawChanged;
    public event EventHandler<bool>? MainPageAutoCollapseSidebarChanged;
    public event EventHandler<bool>? CollectPageLoadDrawerCollapsedChanged;
    public event EventHandler<bool>? PreferPsdAsPrimaryPreviewChanged;
    public event EventHandler<bool>? AutoExpandBurstOnDirectionalNavigationChanged;
    public event EventHandler<bool>? CollapseBurstGroupsChanged;
    public event EventHandler<bool>? AutoSyncGroupRatingsChanged;

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

    public int BatchSize
    {
        get => _batchSize;
        set
        {
            if (_batchSize != value)
            {
                _batchSize = value;
                BatchSizeChanged?.Invoke(this, value);
            }
        }
    }

    public PerformanceMode PerformanceMode
    {
        get => _performanceMode;
        set
        {
            if (_performanceMode != value)
            {
                _performanceMode = value;
                PerformanceModeChanged?.Invoke(this, value);
            }
        }
    }

    public ThumbnailSize ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            if (_thumbnailSize != value)
            {
                _thumbnailSize = value;
                ThumbnailSizeChanged?.Invoke(this, value);
            }
        }
    }

    public bool RememberLastFolder
    {
        get => _rememberLastFolder;
        set
        {
            if (_rememberLastFolder != value)
            {
                _rememberLastFolder = value;
                RememberLastFolderChanged?.Invoke(this, value);
            }
        }
    }

    public bool DeleteToRecycleBin
    {
        get => _deleteToRecycleBin;
        set
        {
            if (_deleteToRecycleBin != value)
            {
                _deleteToRecycleBin = value;
                DeleteToRecycleBinChanged?.Invoke(this, value);
            }
        }
    }

    public string LastFolderPath
    {
        get => _lastFolderPath;
        set
        {
            if (_lastFolderPath != value)
            {
                _lastFolderPath = value;
            }
        }
    }

    public string ExportLastFolderPath
    {
        get => _exportLastFolderPath;
        set
        {
            if (_exportLastFolderPath != value)
            {
                _exportLastFolderPath = value;
            }
        }
    }

    public bool ExportImageEnabled
    {
        get => _exportImageEnabled;
        set
        {
            if (_exportImageEnabled != value)
            {
                _exportImageEnabled = value;
            }
        }
    }

    public int ExportImageMinRating
    {
        get => _exportImageMinRating;
        set
        {
            if (_exportImageMinRating != value)
            {
                _exportImageMinRating = value;
            }
        }
    }

    public string ExportImageFolderName
    {
        get => _exportImageFolderName;
        set
        {
            if (_exportImageFolderName != value)
            {
                _exportImageFolderName = value;
            }
        }
    }

    public bool ExportRawEnabled
    {
        get => _exportRawEnabled;
        set
        {
            if (_exportRawEnabled != value)
            {
                _exportRawEnabled = value;
            }
        }
    }

    public int ExportRawMinRating
    {
        get => _exportRawMinRating;
        set
        {
            if (_exportRawMinRating != value)
            {
                _exportRawMinRating = value;
            }
        }
    }

    public string ExportRawFolderName
    {
        get => _exportRawFolderName;
        set
        {
            if (_exportRawFolderName != value)
            {
                _exportRawFolderName = value;
            }
        }
    }

    public double DecodeScaleFactor
    {
        get => _decodeScaleFactor;
        set
        {
            if (Math.Abs(_decodeScaleFactor - value) > 0.01)
            {
                _decodeScaleFactor = value;
            }
        }
    }

    public bool AlwaysDecodeRaw
    {
        get => _alwaysDecodeRaw;
        set
        {
            if (_alwaysDecodeRaw != value)
            {
                _alwaysDecodeRaw = value;
                AlwaysDecodeRawChanged?.Invoke(this, value);
            }
        }
    }

    public bool MainPageAutoCollapseSidebar
    {
        get => _mainPageAutoCollapseSidebar;
        set
        {
            if (_mainPageAutoCollapseSidebar != value)
            {
                _mainPageAutoCollapseSidebar = value;
                MainPageAutoCollapseSidebarChanged?.Invoke(this, value);
            }
        }
    }

    public bool CollectPageLoadDrawerCollapsed
    {
        get => _collectPageLoadDrawerCollapsed;
        set
        {
            if (_collectPageLoadDrawerCollapsed != value)
            {
                _collectPageLoadDrawerCollapsed = value;
                CollectPageLoadDrawerCollapsedChanged?.Invoke(this, value);
            }
        }
    }

    public bool PreferPsdAsPrimaryPreview
    {
        get => _preferPsdAsPrimaryPreview;
        set
        {
            if (_preferPsdAsPrimaryPreview != value)
            {
                _preferPsdAsPrimaryPreview = value;
                PreferPsdAsPrimaryPreviewChanged?.Invoke(this, value);
            }
        }
    }

    public bool AutoExpandBurstOnDirectionalNavigation
    {
        get => _autoExpandBurstOnDirectionalNavigation;
        set
        {
            if (_autoExpandBurstOnDirectionalNavigation != value)
            {
                _autoExpandBurstOnDirectionalNavigation = value;
                AutoExpandBurstOnDirectionalNavigationChanged?.Invoke(this, value);
            }
        }
    }

    public bool CollapseBurstGroups
    {
        get => _collapseBurstGroups;
        set
        {
            if (_collapseBurstGroups != value)
            {
                _collapseBurstGroups = value;
                CollapseBurstGroupsChanged?.Invoke(this, value);
            }
        }
    }

    public bool AutoSyncGroupRatings
    {
        get => _autoSyncGroupRatings;
        set
        {
            if (_autoSyncGroupRatings != value)
            {
                _autoSyncGroupRatings = value;
                AutoSyncGroupRatingsChanged?.Invoke(this, value);
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

    public async Task SaveBatchSizeAsync(int batchSize)
    {
        await _localSettingsService.SaveSettingAsync("BatchSize", batchSize).ConfigureAwait(false);
    }

    public async Task<int> LoadBatchSizeAsync()
    {
        var batchSize = await _localSettingsService.ReadSettingAsync<int?>("BatchSize").ConfigureAwait(false);
        if (batchSize.HasValue && batchSize.Value > 0)
        {
            _batchSize = batchSize.Value;
            return batchSize.Value;
        }
        return 30;
    }

    public async Task SavePerformanceModeAsync(PerformanceMode mode)
    {
        await _localSettingsService.SaveSettingAsync("PerformanceMode", mode.ToString());
    }

    public async Task<PerformanceMode> LoadPerformanceModeAsync()
    {
        var mode = await _localSettingsService.ReadSettingAsync<string>("PerformanceMode");
        if (mode != null && Enum.TryParse<PerformanceMode>(mode, out var result))
        {
            _performanceMode = result;
            return result;
        }
        return PerformanceMode.Smart;
    }

    public async Task SaveThumbnailSizeAsync(ThumbnailSize size)
    {
        await _localSettingsService.SaveSettingAsync("ThumbnailSize", size.ToString());
    }

    public async Task<ThumbnailSize> LoadThumbnailSizeAsync()
    {
        var size = await _localSettingsService.ReadSettingAsync<string>("ThumbnailSize");
        if (size != null && Enum.TryParse<ThumbnailSize>(size, out var result))
        {
            _thumbnailSize = result;
            return result;
        }
        return ThumbnailSize.Medium;
    }

    public async Task SaveRememberLastFolderAsync(bool remember)
    {
        await _localSettingsService.SaveSettingAsync("RememberLastFolder", remember);
    }

    public async Task<bool> LoadRememberLastFolderAsync()
    {
        var remember = await _localSettingsService.ReadSettingAsync<bool?>("RememberLastFolder");
        if (remember.HasValue)
        {
            _rememberLastFolder = remember.Value;
            return remember.Value;
        }
        return _rememberLastFolder;
    }

    public async Task SaveDeleteToRecycleBinAsync(bool deleteToRecycleBin)
    {
        await _localSettingsService.SaveSettingAsync("DeleteToRecycleBin", deleteToRecycleBin);
    }

    public async Task<bool> LoadDeleteToRecycleBinAsync()
    {
        var deleteToRecycleBin = await _localSettingsService.ReadSettingAsync<bool?>("DeleteToRecycleBin");
        if (deleteToRecycleBin.HasValue)
        {
            _deleteToRecycleBin = deleteToRecycleBin.Value;
            return deleteToRecycleBin.Value;
        }
        return _deleteToRecycleBin;
    }

    public async Task SaveLastFolderPathAsync(string path)
    {
        await _localSettingsService.SaveSettingAsync("LastFolderPath", path);
    }

    public async Task<string> LoadLastFolderPathAsync()
    {
        var path = await _localSettingsService.ReadSettingAsync<string>("LastFolderPath");
        if (path != null)
        {
            _lastFolderPath = path;
            return path;
        }
        return string.Empty;
    }

    public async Task SaveFolderAccessHistoryAsync(FolderAccessHistory history)
    {
        await _localSettingsService.SaveSettingAsync("FolderAccessHistory", history);
    }

    public async Task<FolderAccessHistory> LoadFolderAccessHistoryAsync()
    {
        var history = await _localSettingsService.ReadSettingAsync<FolderAccessHistory>("FolderAccessHistory");
        return history ?? new FolderAccessHistory();
    }

    public async Task SaveExportLastFolderPathAsync(string path)
    {
        await _localSettingsService.SaveSettingAsync("ExportLastFolderPath", path);
    }

    public async Task<string> LoadExportLastFolderPathAsync()
    {
        var path = await _localSettingsService.ReadSettingAsync<string>("ExportLastFolderPath");
        if (path != null)
        {
            _exportLastFolderPath = path;
            return path;
        }
        return string.Empty;
    }

    public async Task SaveExportImageEnabledAsync(bool enabled)
    {
        await _localSettingsService.SaveSettingAsync("ExportImageEnabled", enabled);
    }

    public async Task<bool> LoadExportImageEnabledAsync()
    {
        var enabled = await _localSettingsService.ReadSettingAsync<bool?>("ExportImageEnabled");
        if (enabled.HasValue)
        {
            _exportImageEnabled = enabled.Value;
            return enabled.Value;
        }
        return _exportImageEnabled;
    }

    public async Task SaveExportImageMinRatingAsync(int rating)
    {
        await _localSettingsService.SaveSettingAsync("ExportImageMinRating", rating);
    }

    public async Task<int> LoadExportImageMinRatingAsync()
    {
        var rating = await _localSettingsService.ReadSettingAsync<int?>("ExportImageMinRating");
        if (rating.HasValue)
        {
            _exportImageMinRating = rating.Value;
            return rating.Value;
        }
        return _exportImageMinRating;
    }

    public async Task SaveExportImageFolderNameAsync(string name)
    {
        await _localSettingsService.SaveSettingAsync("ExportImageFolderName", name);
    }

    public async Task<string> LoadExportImageFolderNameAsync()
    {
        var name = await _localSettingsService.ReadSettingAsync<string>("ExportImageFolderName");
        if (name != null)
        {
            _exportImageFolderName = name;
            return name;
        }
        return _exportImageFolderName;
    }

    public async Task SaveExportRawEnabledAsync(bool enabled)
    {
        await _localSettingsService.SaveSettingAsync("ExportRawEnabled", enabled);
    }

    public async Task<bool> LoadExportRawEnabledAsync()
    {
        var enabled = await _localSettingsService.ReadSettingAsync<bool?>("ExportRawEnabled");
        if (enabled.HasValue)
        {
            _exportRawEnabled = enabled.Value;
            return enabled.Value;
        }
        return _exportRawEnabled;
    }

    public async Task SaveExportRawMinRatingAsync(int rating)
    {
        await _localSettingsService.SaveSettingAsync("ExportRawMinRating", rating);
    }

    public async Task<int> LoadExportRawMinRatingAsync()
    {
        var rating = await _localSettingsService.ReadSettingAsync<int?>("ExportRawMinRating");
        if (rating.HasValue)
        {
            _exportRawMinRating = rating.Value;
            return rating.Value;
        }
        return _exportRawMinRating;
    }

    public async Task SaveExportRawFolderNameAsync(string name)
    {
        await _localSettingsService.SaveSettingAsync("ExportRawFolderName", name);
    }

    public async Task<string> LoadExportRawFolderNameAsync()
    {
        var name = await _localSettingsService.ReadSettingAsync<string>("ExportRawFolderName");
        if (name != null)
        {
            _exportRawFolderName = name;
            return name;
        }
        return _exportRawFolderName;
    }

    public async Task SaveDecodeScaleFactorAsync(double factor)
    {
        await _localSettingsService.SaveSettingAsync("DecodeScaleFactor", factor);
    }

    public async Task<double> LoadDecodeScaleFactorAsync()
    {
        var factor = await _localSettingsService.ReadSettingAsync<double?>("DecodeScaleFactor");
        if (factor.HasValue)
        {
            _decodeScaleFactor = factor.Value;
            return factor.Value;
        }
        return _decodeScaleFactor;
    }

    public async Task SaveAlwaysDecodeRawAsync(bool alwaysDecode)
    {
        await _localSettingsService.SaveSettingAsync("AlwaysDecodeRaw", alwaysDecode);
    }

    public async Task<bool> LoadAlwaysDecodeRawAsync()
    {
        var alwaysDecode = await _localSettingsService.ReadSettingAsync<bool?>("AlwaysDecodeRaw");
        if (alwaysDecode.HasValue)
        {
            _alwaysDecodeRaw = alwaysDecode.Value;
            return alwaysDecode.Value;
        }
        return _alwaysDecodeRaw;
    }

    public async Task SaveMainPageAutoCollapseSidebarAsync(bool enabled)
    {
        await _localSettingsService.SaveSettingAsync("MainPageAutoCollapseSidebar", enabled);
    }

    public async Task<bool> LoadMainPageAutoCollapseSidebarAsync()
    {
        var enabled = await _localSettingsService.ReadSettingAsync<bool?>("MainPageAutoCollapseSidebar");
        if (enabled.HasValue)
        {
            _mainPageAutoCollapseSidebar = enabled.Value;
            return enabled.Value;
        }

        return _mainPageAutoCollapseSidebar;
    }

    public async Task SaveCollectPageLoadDrawerCollapsedAsync(bool collapsed)
    {
        await _localSettingsService.SaveSettingAsync("CollectPageLoadDrawerCollapsed", collapsed);
    }

    public async Task<bool> LoadCollectPageLoadDrawerCollapsedAsync()
    {
        var collapsed = await _localSettingsService.ReadSettingAsync<bool?>("CollectPageLoadDrawerCollapsed");
        if (collapsed.HasValue)
        {
            _collectPageLoadDrawerCollapsed = collapsed.Value;
            return collapsed.Value;
        }

        return _collectPageLoadDrawerCollapsed;
    }

    public async Task SavePreferPsdAsPrimaryPreviewAsync(bool enabled)
    {
        await _localSettingsService.SaveSettingAsync("PreferPsdAsPrimaryPreview", enabled);
    }

    public async Task<bool> LoadPreferPsdAsPrimaryPreviewAsync()
    {
        var enabled = await _localSettingsService.ReadSettingAsync<bool?>("PreferPsdAsPrimaryPreview");
        if (enabled.HasValue)
        {
            _preferPsdAsPrimaryPreview = enabled.Value;
            return enabled.Value;
        }

        return _preferPsdAsPrimaryPreview;
    }

    public async Task SaveAutoExpandBurstOnDirectionalNavigationAsync(bool enabled)
    {
        await _localSettingsService.SaveSettingAsync("AutoExpandBurstOnDirectionalNavigation", enabled);
    }

    public async Task<bool> LoadAutoExpandBurstOnDirectionalNavigationAsync()
    {
        var enabled = await _localSettingsService.ReadSettingAsync<bool?>("AutoExpandBurstOnDirectionalNavigation");
        if (enabled.HasValue)
        {
            _autoExpandBurstOnDirectionalNavigation = enabled.Value;
            return enabled.Value;
        }

        return _autoExpandBurstOnDirectionalNavigation;
    }

    public async Task SaveCollapseBurstGroupsAsync(bool enabled)
    {
        await _localSettingsService.SaveSettingAsync("CollapseBurstGroups", enabled);
    }

    public async Task<bool> LoadCollapseBurstGroupsAsync()
    {
        var enabled = await _localSettingsService.ReadSettingAsync<bool?>("CollapseBurstGroups");
        if (enabled.HasValue)
        {
            _collapseBurstGroups = enabled.Value;
            return enabled.Value;
        }

        return _collapseBurstGroups;
    }

    public async Task SaveAutoSyncGroupRatingsAsync(bool enabled)
    {
        await _localSettingsService.SaveSettingAsync("AutoSyncGroupRatings", enabled);
    }

    public async Task<bool> LoadAutoSyncGroupRatingsAsync()
    {
        var enabled = await _localSettingsService.ReadSettingAsync<bool?>("AutoSyncGroupRatings");
        if (enabled.HasValue)
        {
            _autoSyncGroupRatings = enabled.Value;
            return enabled.Value;
        }

        return _autoSyncGroupRatings;
    }

    public void SuspendAlwaysDecodeRawPersistence(string reason)
    {
        // Reserved hook for viewer lifetime; the setting is only persisted by explicit save calls.
    }

    public Task ResumeAlwaysDecodeRawPersistenceAsync(string reason)
    {
        return Task.CompletedTask;
    }

    public async Task InitializeAsync()
    {
        await LoadNavigationViewModeAsync();
        await LoadBatchSizeAsync();
        await LoadPerformanceModeAsync();
        await LoadThumbnailSizeAsync();
        await LoadRememberLastFolderAsync();
        await LoadDeleteToRecycleBinAsync();
        await LoadLastFolderPathAsync();
        await LoadExportLastFolderPathAsync();
        await LoadExportImageEnabledAsync();
        await LoadExportImageMinRatingAsync();
        await LoadExportImageFolderNameAsync();
        await LoadExportRawEnabledAsync();
        await LoadExportRawMinRatingAsync();
        await LoadExportRawFolderNameAsync();
        await LoadDecodeScaleFactorAsync();
        await LoadAlwaysDecodeRawAsync();
        await LoadMainPageAutoCollapseSidebarAsync();
        await LoadCollectPageLoadDrawerCollapsedAsync();
        await LoadPreferPsdAsPrimaryPreviewAsync();
        await LoadAutoExpandBurstOnDirectionalNavigationAsync();
        await LoadCollapseBurstGroupsAsync();
        await LoadAutoSyncGroupRatingsAsync();
    }
}
