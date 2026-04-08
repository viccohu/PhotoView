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
        System.Diagnostics.Debug.WriteLine($"[RatingService] GetRatingAsync: 开始, 文件={file.Name}, 扩展名={file.FileType}");
        await _concurrencyLimiter.WaitAsync();
        try
        {
            if (IsWinRTRatingSupported(file.FileType))
            {
                System.Diagnostics.Debug.WriteLine($"[RatingService] GetRatingAsync: 支持WinRT评级, 尝试读取");
                try
                {
                    var properties = await file.Properties.GetImagePropertiesAsync();
                    System.Diagnostics.Debug.WriteLine($"[RatingService] GetRatingAsync: WinRT读取成功, rating={properties.Rating}");
                    return (properties.Rating, RatingSource.WinRT);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RatingService] WinRT读取失败: {file.Name}, 错误: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[RatingService] GetRatingAsync: 不支持WinRT评级");
            }

            var cachedRating = _cacheService.GetRating(file.Path);
            System.Diagnostics.Debug.WriteLine($"[RatingService] GetRatingAsync: 从缓存读取, rating={( cachedRating.HasValue ? cachedRating.Value : 0 )}");
            return (cachedRating ?? 0, RatingSource.Cache);
        }
        finally
        {
            _concurrencyLimiter.Release();
            System.Diagnostics.Debug.WriteLine($"[RatingService] GetRatingAsync: 完成");
        }
    }

    public async Task SetRatingAsync(StorageFile file, uint rating)
    {
        System.Diagnostics.Debug.WriteLine($"[RatingService] SetRatingAsync: 开始, 文件={file.Path}, 扩展名={file.FileType}, rating={rating}");
        await _concurrencyLimiter.WaitAsync();
        try
        {
            if (IsWinRTRatingSupported(file.FileType))
            {
                System.Diagnostics.Debug.WriteLine($"[RatingService] SetRatingAsync: 支持WinRT评级, 尝试写入");
                try
                {
                    var properties = await file.Properties.GetImagePropertiesAsync();
                    System.Diagnostics.Debug.WriteLine($"[RatingService] SetRatingAsync: 读取旧rating={properties.Rating}, 准备写入新rating={rating}");
                    properties.Rating = rating;
                    await properties.SavePropertiesAsync();
                    System.Diagnostics.Debug.WriteLine($"[RatingService] SetRatingAsync: WinRT写入成功");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[RatingService] WinRT写入失败: {file.Name}, 错误: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[RatingService] SetRatingAsync: 不支持WinRT评级, 仅写入缓存");
            }

            System.Diagnostics.Debug.WriteLine($"[RatingService] SetRatingAsync: 准备写入缓存");
            await _cacheService.SetRatingAsync(file.Path, rating);
            System.Diagnostics.Debug.WriteLine($"[RatingService] SetRatingAsync: 缓存写入成功");
        }
        finally
        {
            _concurrencyLimiter.Release();
            System.Diagnostics.Debug.WriteLine($"[RatingService] SetRatingAsync: 完成");
        }
    }
}
