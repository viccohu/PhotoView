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
        // 标准图像格式
        ".jpg", ".jpeg", ".tif", ".tiff", ".png", ".heic", ".heif",
        // RAW 格式 - Canon
        ".crw", ".cr2", ".cr3",
        // RAW 格式 - Nikon
        ".nef", ".nrw",
        // RAW 格式 - Sony
        ".arw", ".srf", ".sr2",
        // RAW 格式 - Fujifilm
        ".raf",
        // RAW 格式 - Panasonic
        ".rw2",
        // RAW 格式 - Olympus
        ".orf",
        // RAW 格式 - Pentax
        ".pef",
        // RAW 格式 - Leica
        ".rwl",
        // RAW 格式 - Samsung
        ".srw",
        // RAW 格式 - Epson
        ".erf",
        // RAW 格式 - Kodak
        ".dcr",
        // RAW 格式 - Minolta
        ".mrw",
        // RAW 格式 - Hasselblad
        ".3fr", ".fff",
        // RAW 格式 - Leaf
        ".mos",
        // RAW 格式 - Adobe
        ".dng",
        // 通用 RAW 格式
        ".raw"
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
            
            System.Diagnostics.Debug.WriteLine($"[ExifService] 开始处理文件: {file.Name}, 格式: {file.FileType}");

            // 1. 首先尝试使用 ImageProperties API 获取 DateTaken
            try
            {
                var imageProperties = await file.Properties.GetImagePropertiesAsync();
                if (imageProperties.DateTaken != DateTimeOffset.MinValue && imageProperties.DateTaken != default)
                {
                    exifData.DateTaken = imageProperties.DateTaken.DateTime;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken (ImageProperties): {imageProperties.DateTaken}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken (ImageProperties): 未找到");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExifService] ImageProperties 获取失败: {ex.Message}");
            }

            // 2. 使用 BitmapDecoder 获取其他属性
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

                // 如果 ImageProperties 没有获取到 DateTaken，尝试 BitmapProperties
                if (!exifData.DateTaken.HasValue)
                {
                    if (properties.TryGetValue("System.Photo.DateTaken", out var dateProp) && dateProp.Value is DateTime dateTaken)
                    {
                        exifData.DateTaken = dateTaken;
                        System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken (BitmapProperties): {dateTaken}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken (BitmapProperties): 未找到");
                    }
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

                // ISOSpeed - 尝试多种数据类型
                if (properties.TryGetValue("System.Photo.ISOSpeed", out var isoProp))
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed 原始值: {isoProp.Value}, 类型: {isoProp.Value?.GetType().Name}");
                    
                    if (isoProp.Value is ushort isoSpeedUshort)
                    {
                        exifData.ISOSpeed = isoSpeedUshort;
                        System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed (ushort): {isoSpeedUshort}");
                    }
                    else if (isoProp.Value is uint isoSpeedUint)
                    {
                        exifData.ISOSpeed = isoSpeedUint;
                        System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed (uint): {isoSpeedUint}");
                    }
                    else if (isoProp.Value is double isoSpeedDouble)
                    {
                        exifData.ISOSpeed = (uint)isoSpeedDouble;
                        System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed (double): {isoSpeedDouble}");
                    }
                    else if (isoProp.Value != null)
                    {
                        // 尝试直接转换
                        try
                        {
                            exifData.ISOSpeed = Convert.ToUInt32(isoProp.Value);
                            System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed (converted): {exifData.ISOSpeed}");
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed 转换失败: {ex.Message}");
                        }
                    }
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
