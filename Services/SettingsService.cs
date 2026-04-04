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

    public event EventHandler<NavigationViewPaneDisplayMode>? NavigationViewModeChanged;
    public event EventHandler<int>? BatchSizeChanged;
    public event EventHandler<PerformanceMode>? PerformanceModeChanged;
    public event EventHandler<ThumbnailSize>? ThumbnailSizeChanged;

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

    public async Task InitializeAsync()
    {
        await LoadNavigationViewModeAsync();
        await LoadBatchSizeAsync();
        await LoadPerformanceModeAsync();
        await LoadThumbnailSizeAsync();
    }
}