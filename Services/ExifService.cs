using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using PhotoView.Contracts.Services;
using PhotoView.Models;

namespace PhotoView.Services;

public class ExifService : IExifService
{
    private static readonly HashSet<string> WinRTSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".tif", ".tiff", ".png", ".heic", ".heif", ".dng"
    };

    private static readonly string[] ExifPropertyNames = {
        // 基本属性
        "System.Photo.DateTaken",
        "System.Photo.CameraManufacturer",
        "System.Photo.CameraModel",
        "System.Photo.Orientation",
        "System.Image.HorizontalSize",
        "System.Image.VerticalSize",
        
        // 拍摄参数
        "System.Photo.ExposureTime",
        "System.Photo.FNumber",
        "System.Photo.ISOSpeed",
        "System.Photo.FocalLength",
        "System.Photo.FocalLengthInFilm",
        "System.Photo.ExposureProgram",
        "System.Photo.ExposureBias",
        "System.Photo.MaxAperture",
        
        // 其他信息
        "System.Photo.Flash",
        "System.Photo.MeteringMode",
        "System.Photo.WhiteBalance",
        "System.Photo.LensModel"
    };

    private readonly RatingCacheService _cacheService;
    private readonly SemaphoreSlim _concurrencyLimiter = new(4);

    public ExifService(RatingCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    private static ushort? GetBitDepthFromPixelFormat(BitmapPixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            BitmapPixelFormat.Bgra8 => 32,
            BitmapPixelFormat.Rgba8 => 32,
            BitmapPixelFormat.Rgba16 => 64,
            BitmapPixelFormat.Gray8 => 8,
            BitmapPixelFormat.Gray16 => 16,
            BitmapPixelFormat.Nv12 => 12,
            BitmapPixelFormat.Yuy2 => 16,
            _ => null
        };
    }

    public async Task InitializeAsync()
    {
        await _cacheService.InitializeAsync();
    }

    public async Task<ExifData> GetExifDataAsync(StorageFile file, CancellationToken cancellationToken = default)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            var exifData = new ExifData();

            using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            exifData.Width = decoder.PixelWidth;
            exifData.Height = decoder.PixelHeight;

            try
            {
                exifData.DpiX = decoder.DpiX;
                exifData.DpiY = decoder.DpiY;
                exifData.PixelFormat = decoder.BitmapPixelFormat.ToString();
                exifData.BitDepth = GetBitDepthFromPixelFormat(decoder.BitmapPixelFormat);
            }
            catch
            {
            }

            try
            {
                var properties = await decoder.BitmapProperties.GetPropertiesAsync(ExifPropertyNames);

                if (properties.TryGetValue("System.Photo.DateTaken", out var dateProp) && dateProp.Value is DateTime dateTaken)
                {
                    exifData.DateTaken = dateTaken;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken: {dateTaken}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken: 未找到");
                }

                if (properties.TryGetValue("System.Photo.CameraManufacturer", out var manuProp) && manuProp.Value is string manufacturer)
                {
                    exifData.CameraManufacturer = manufacturer;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] CameraManufacturer: {manufacturer}");
                }

                if (properties.TryGetValue("System.Photo.CameraModel", out var modelProp) && modelProp.Value is string model)
                {
                    exifData.CameraModel = model;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] CameraModel: {model}");
                }

                if (properties.TryGetValue("System.Photo.Orientation", out var orientProp) && orientProp.Value is ushort orientation)
                {
                    exifData.Orientation = orientation;
                }

                if (properties.TryGetValue("System.Photo.ExposureTime", out var expProp) && expProp.Value is double exposureTime)
                {
                    exifData.ExposureTime = exposureTime;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] ExposureTime: {exposureTime}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] ExposureTime: 未找到");
                }

                if (properties.TryGetValue("System.Photo.FNumber", out var fProp) && fProp.Value is double fNumber)
                {
                    exifData.FNumber = fNumber;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] FNumber: {fNumber}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] FNumber: 未找到");
                }

                if (properties.TryGetValue("System.Photo.ISOSpeed", out var isoProp) && isoProp.Value is uint isoSpeed)
                {
                    exifData.ISOSpeed = isoSpeed;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed: {isoSpeed}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed: 未找到");
                }

                if (properties.TryGetValue("System.Photo.FocalLength", out var flProp) && flProp.Value is double focalLength)
                {
                    exifData.FocalLength = focalLength;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] FocalLength: {focalLength}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] FocalLength: 未找到");
                }

                if (properties.TryGetValue("System.Photo.FocalLengthInFilm", out var flfProp) && flfProp.Value is double focalLengthInFilm)
                {
                    exifData.FocalLengthInFilm = focalLengthInFilm;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] FocalLengthInFilm: {focalLengthInFilm}");
                }

                if (properties.TryGetValue("System.Photo.ExposureProgram", out var epProp) && epProp.Value is ushort exposureProgram)
                {
                    exifData.ExposureProgram = exposureProgram;
                }

                if (properties.TryGetValue("System.Photo.ExposureBias", out var ebProp) && ebProp.Value is double exposureBias)
                {
                    exifData.ExposureBias = exposureBias;
                }

                if (properties.TryGetValue("System.Photo.MaxAperture", out var maProp) && maProp.Value is double maxAperture)
                {
                    exifData.MaxAperture = maxAperture;
                }

                if (properties.TryGetValue("System.Photo.Flash", out var flashProp) && flashProp.Value is ushort flash)
                {
                    exifData.Flash = flash;
                }

                if (properties.TryGetValue("System.Photo.MeteringMode", out var mmProp) && mmProp.Value is ushort meteringMode)
                {
                    exifData.MeteringMode = meteringMode;
                }

                if (properties.TryGetValue("System.Photo.WhiteBalance", out var wbProp) && wbProp.Value is ushort whiteBalance)
                {
                    exifData.WhiteBalance = whiteBalance;
                }

                if (properties.TryGetValue("System.Photo.LensModel", out var lmProp) && lmProp.Value is string lensModel)
                {
                    exifData.LensModel = lensModel;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] LensModel: {lensModel}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] LensModel: 未找到");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExifService] 读取EXIF属性失败: {file.Name}, 错误: {ex.Message}");
            }

            return exifData;
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    public async Task<(uint Rating, RatingSource Source)> GetRatingAsync(StorageFile file)
    {
        await _concurrencyLimiter.WaitAsync();
        try
        {
            if (WinRTSupportedExtensions.Contains(file.FileType))
            {
                try
                {
                    var properties = await file.Properties.GetImagePropertiesAsync();
                    return (properties.Rating, RatingSource.WinRT);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] WinRT读取评级失败: {file.Name}, 错误: {ex.Message}");
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
            if (WinRTSupportedExtensions.Contains(file.FileType))
            {
                try
                {
                    var properties = await file.Properties.GetImagePropertiesAsync();
                    properties.Rating = rating;
                    await properties.SavePropertiesAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] WinRT写入评级失败: {file.Name}, 错误: {ex.Message}");
                }
            }

            await _cacheService.SetRatingAsync(file.Path, rating);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    public async Task<ExifData> GetFullExifDataAsync(StorageFile file, CancellationToken cancellationToken = default)
    {
        var exifData = await GetExifDataAsync(file, cancellationToken);
        var (rating, source) = await GetRatingAsync(file);
        exifData.Rating = rating;
        exifData.RatingSource = source;
        return exifData;
    }
}
