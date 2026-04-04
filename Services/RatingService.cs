using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace PhotoView.Services;

public enum RatingSource
{
    Unknown,
    WinRT,
    Cache
}

public class RatingService
{
    private static readonly HashSet<string> WinRTSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".tif", ".tiff", ".png", ".heic", ".heif", ".dng"
    };

    private readonly RatingCacheService _cacheService;
    private readonly SemaphoreSlim _concurrencyLimiter = new(4);

    public RatingService(RatingCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    public async Task InitializeAsync()
    {
        await _cacheService.InitializeAsync();
    }

    public bool IsWinRTRatingSupported(string fileExtension)
    {
        return WinRTSupportedExtensions.Contains(fileExtension);
    }

    public async Task<(uint Rating, RatingSource Source)> GetRatingAsync(StorageFile file)
    {
        await _concurrencyLimiter.WaitAsync();
        try
        {
            if (IsWinRTRatingSupported(file.FileType))
            {
                try
                {
                    var properties = await file.Properties.GetImagePropertiesAsync();
                    return (properties.Rating, RatingSource.WinRT);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RatingService] WinRT读取失败: {file.Name}, 错误: {ex.Message}");
                }
            }

            var cachedRating = _cacheService.GetRating(file.Path);
            return (cachedRating ?? 0, RatingSource.Cache);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    public async Task SetRatingAsync(StorageFile file, uint rating)
    {
        await _concurrencyLimiter.WaitAsync();
        try
        {
            if (IsWinRTRatingSupported(file.FileType))
            {
                try
                {
                    var properties = await file.Properties.GetImagePropertiesAsync();
                    properties.Rating = rating;
                    await properties.SavePropertiesAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RatingService] WinRT写入失败: {file.Name}, 错误: {ex.Message}");
                }
            }

            await _cacheService.SetRatingAsync(file.Path, rating);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }
}
