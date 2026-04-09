using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using PhotoView.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace PhotoView.ViewModels;

public partial class ImageViewerViewModel : ObservableRecipient
{
    private readonly IExifService _exifService;
    private readonly RatingService _ratingService;
    private ImageFileInfo? _currentImage;

    [ObservableProperty]
    private string _imageName = "None";

    [ObservableProperty]
    private string _resolution = "None";

    [ObservableProperty]
    private string _fileSize = "None";

    [ObservableProperty]
    private DateTimeOffset? _captureDate;

    [ObservableProperty]
    private TimeSpan? _captureTime;

    [ObservableProperty]
    private string _formattedDateTime = "None";

    [ObservableProperty]
    private string _captureYear = "None";

    [ObservableProperty]
    private string _captureMonth = "None";

    [ObservableProperty]
    private string _captureDay = "None";

    [ObservableProperty]
    private string _captureTimeOfDay = "None";

    [ObservableProperty]
    private bool _hasDeviceInfo;

    [ObservableProperty]
    private ObservableCollection<string> _deviceInfo = new();

    [ObservableProperty]
    private ObservableCollection<FilePathItem> _filePaths = new();

    [ObservableProperty]
    private uint _rating;

    [ObservableProperty]
    private string _ratingSource = "None";

    [ObservableProperty]
    private string _dpi = "None";

    [ObservableProperty]
    private string _bitDepth = "None";

    [ObservableProperty]
    private string _lensModel = "None";

    [ObservableProperty]
    private string _focalLength = "None";

    [ObservableProperty]
    private string _exposureTime = "None";

    [ObservableProperty]
    private string _fNumber = "None";

    [ObservableProperty]
    private string _iso = "None";

    [ObservableProperty]
    private string _exposureProgram = "None";

    [ObservableProperty]
    private string _exposureBias = "None";

    [ObservableProperty]
    private string _flash = "None";

    [ObservableProperty]
    private string _fileFormat = "None";

    [ObservableProperty]
    private string _fileFormatColor = "Gray";

    [ObservableProperty]
    private bool _isLoadingExif = false;

    public ImageViewerViewModel(IExifService exifService, RatingService ratingService)
    {
        _exifService = exifService;
        _ratingService = ratingService;
    }

    public void SetBasicInfo(ImageFileInfo imageFileInfo)
    {
        if (imageFileInfo == null)
            return;

        _currentImage = imageFileInfo;
        ImageName = imageFileInfo.ImageName;
        Resolution = $"{imageFileInfo.Width} x {imageFileInfo.Height}";
        Rating = imageFileInfo.Rating;
        RatingSource = imageFileInfo.RatingSource.ToString();

        SetFileFormatInfo(imageFileInfo.ImageName);
        LoadFilePaths(imageFileInfo);

        IsLoadingExif = true;
    }

    public async Task LoadFileInfoAsync(ImageFileInfo imageFileInfo)
    {
        SetBasicInfo(imageFileInfo);

        if (imageFileInfo.ImageFile != null)
        {
            await LoadFileDetailsAsync(imageFileInfo.ImageFile);
        }
    }

    private void SetFileFormatInfo(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        
        FileFormat = ext switch
        {
            ".jpg" or ".jpeg" => "JPG",
            ".png" => "PNG",
            ".gif" => "GIF",
            ".bmp" => "BMP",
            ".tiff" or ".tif" => "TIFF",
            ".webp" => "WebP",
            ".cr2" or ".cr3" or ".crw" => "RAW",
            ".nef" or ".nrw" => "RAW",
            ".arw" or ".srf" or ".sr2" => "RAW",
            ".dng" => "RAW",
            ".orf" or ".pef" or ".raf" or ".rw2" => "RAW",
            _ => ext.ToUpper().TrimStart('.')
        };

        FileFormatColor = ext switch
        {
            ".jpg" or ".jpeg" => "Blue",
            ".png" => "Green",
            ".cr2" or ".cr3" or ".crw" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" or ".dng" or ".orf" or ".pef" or ".raf" or ".rw2" => "Orange",
            _ => "Gray"
        };
    }

    public async Task LoadFileDetailsAsync(StorageFile file)
    {
        try
        {
            var basicProps = await file.GetBasicPropertiesAsync();
            FileSize = FormatFileSize(basicProps.Size);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFileDetailsAsync - 文件大小读取错误: {ex}");
        }

        try
        {
            Debug.WriteLine($"[ImageViewerViewModel] 开始读取EXIF: {file.Name}");
            var exifData = await _exifService.GetExifDataAsync(file);
            Debug.WriteLine($"[ImageViewerViewModel] EXIF读取完成");

            if (exifData.DateTaken.HasValue)
            {
                CaptureDate = exifData.DateTaken.Value.Date;
                CaptureTime = exifData.DateTaken.Value.TimeOfDay;
                FormattedDateTime = $"{exifData.DateTaken.Value:yyyy年 M月 d日 HH:mm}";
                CaptureYear = $"{exifData.DateTaken.Value:yyyy年}";
                CaptureMonth = $"{exifData.DateTaken.Value:M月}";
                CaptureDay = $"{exifData.DateTaken.Value:d日}";
                CaptureTimeOfDay = $"{exifData.DateTaken.Value:HH:mm}";
                Debug.WriteLine($"[ImageViewerViewModel] 日期时间: {FormattedDateTime}");
            }
            else
            {
                FormattedDateTime = "None";
                CaptureYear = "None";
                CaptureMonth = "None";
                CaptureDay = "None";
                CaptureTimeOfDay = "None";
                Debug.WriteLine($"[ImageViewerViewModel] 日期时间: 未找到");
            }

            if (exifData.DpiX.HasValue && exifData.DpiY.HasValue)
            {
                Dpi = $"{exifData.DpiX.Value:F0} x {exifData.DpiY.Value:F0}";
            }
            else
            {
                Dpi = "None";
            }

            if (exifData.BitDepth.HasValue)
            {
                BitDepth = $"{exifData.BitDepth.Value} bit";
            }
            else
            {
                BitDepth = "None";
            }

            UpdateDeviceInfo(exifData);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFileDetailsAsync - EXIF读取错误: {ex}");
        }
        finally
        {
            IsLoadingExif = false;
            Debug.WriteLine($"[ImageViewerViewModel] IsLoadingExif = false");
        }
    }

    private void UpdateDeviceInfo(ExifData exifData)
    {
        DeviceInfo.Clear();

        var deviceInfoList = new System.Collections.Generic.List<string>();

        if (!string.IsNullOrEmpty(exifData.CameraManufacturer))
        {
            deviceInfoList.Add(exifData.CameraManufacturer);
            Debug.WriteLine($"[ImageViewerViewModel] CameraManufacturer: {exifData.CameraManufacturer}");
        }

        if (!string.IsNullOrEmpty(exifData.CameraModel))
        {
            deviceInfoList.Add(exifData.CameraModel);
            Debug.WriteLine($"[ImageViewerViewModel] CameraModel: {exifData.CameraModel}");
        }

        foreach (var info in deviceInfoList)
        {
            DeviceInfo.Add(info);
        }

        HasDeviceInfo = deviceInfoList.Count > 0;

        if (!string.IsNullOrEmpty(exifData.LensModel))
        {
            LensModel = exifData.LensModel;
            Debug.WriteLine($"[ImageViewerViewModel] LensModel: {exifData.LensModel}");
        }
        else
        {
            LensModel = "None";
            Debug.WriteLine($"[ImageViewerViewModel] LensModel: 未找到");
        }

        if (exifData.FocalLength.HasValue)
        {
            var focalLength = exifData.GetFormattedFocalLength();
            if (exifData.FocalLengthInFilm.HasValue)
            {
                FocalLength = $"{focalLength} / {exifData.FocalLengthInFilm.Value:F0}mm";
            }
            else
            {
                FocalLength = focalLength;
            }
            Debug.WriteLine($"[ImageViewerViewModel] FocalLength: {FocalLength}");
        }
        else
        {
            FocalLength = "None";
            Debug.WriteLine($"[ImageViewerViewModel] FocalLength: 未找到");
        }

        if (exifData.ExposureTime.HasValue)
        {
            ExposureTime = exifData.GetFormattedExposureTime();
            Debug.WriteLine($"[ImageViewerViewModel] ExposureTime: {ExposureTime}");
        }
        else
        {
            ExposureTime = "None";
            Debug.WriteLine($"[ImageViewerViewModel] ExposureTime: 未找到");
        }

        if (exifData.FNumber.HasValue)
        {
            FNumber = exifData.GetFormattedFNumber();
            Debug.WriteLine($"[ImageViewerViewModel] FNumber: {FNumber}");
        }
        else
        {
            FNumber = "None";
            Debug.WriteLine($"[ImageViewerViewModel] FNumber: 未找到");
        }

        if (exifData.ISOSpeed.HasValue)
        {
            Iso = $"ISO {exifData.ISOSpeed.Value}";
            Debug.WriteLine($"[ImageViewerViewModel] ISO: {Iso}");
        }
        else
        {
            Iso = "None";
            Debug.WriteLine($"[ImageViewerViewModel] ISO: 未找到");
        }

        if (exifData.ExposureProgram.HasValue)
        {
            ExposureProgram = exifData.GetFormattedExposureProgram();
        }
        else
        {
            ExposureProgram = "None";
        }

        if (exifData.ExposureBias.HasValue)
        {
            ExposureBias = $"{(exifData.ExposureBias.Value >= 0 ? "+" : "")}{exifData.ExposureBias.Value:F1} EV";
        }
        else
        {
            ExposureBias = "None";
        }

        if (exifData.Flash.HasValue)
        {
            Flash = exifData.GetFormattedFlash();
        }
        else
        {
            Flash = "None";
        }
    }

    private void LoadFilePaths(ImageFileInfo imageFileInfo)
    {
        FilePaths.Clear();

        if (imageFileInfo == null)
            return;

        var allFiles = new System.Collections.Generic.List<ImageFileInfo>();

        if (imageFileInfo.Group != null)
        {
            allFiles.AddRange(imageFileInfo.Group.Images);
        }
        else
        {
            allFiles.Add(imageFileInfo);
        }

        foreach (var file in allFiles)
        {
            if (file.ImageFile == null)
                continue;

            var ext = System.IO.Path.GetExtension(file.ImageName).ToLowerInvariant();
            var fileFormat = ext switch
            {
                ".jpg" or ".jpeg" => "JPG",
                ".png" => "PNG",
                ".gif" => "GIF",
                ".bmp" => "BMP",
                ".tiff" or ".tif" => "TIFF",
                ".webp" => "WebP",
                ".cr2" or ".cr3" or ".crw" => "RAW",
                ".nef" or ".nrw" => "RAW",
                ".arw" or ".srf" or ".sr2" => "RAW",
                ".dng" => "RAW",
                ".orf" or ".pef" or ".raf" or ".rw2" => "RAW",
                _ => ext.ToUpper().TrimStart('.')
            };

            var fileFormatColor = ext switch
            {
                ".jpg" or ".jpeg" => "Blue",
                ".png" => "Green",
                ".cr2" or ".cr3" or ".crw" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" or ".dng" or ".orf" or ".pef" or ".raf" or ".rw2" => "Orange",
                _ => "Gray"
            };

            var filePathItem = new FilePathItem
            {
                Path = file.ImageFile.Path,
                FileName = file.ImageName,
                FileFormat = fileFormat,
                FileFormatColor = fileFormatColor,
                OpenInExplorerCommand = new AsyncRelayCommand(() => OpenInExplorerAsync(file.ImageFile.Path)),
                CopyPathCommand = new AsyncRelayCommand(() => CopyPathToClipboardAsync(file.ImageFile.Path))
            };

            FilePaths.Add(filePathItem);
        }
    }

    // 评级更新事件
    public event EventHandler<(ImageFileInfo Image, uint Rating)>? RatingUpdated;

    [RelayCommand]
    private async Task SetRatingAsync(uint rating)
    {
        System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: 开始执行");
        System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: _currentImage={( _currentImage == null ? "null" : _currentImage.ImageName )}, newRating={rating}");
        
        if (_currentImage == null)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: _currentImage 为 null，退出");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: 更新 ViewModel.Rating 从 {Rating} 到 {rating}");
        Rating = rating;
        System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: 更新 _currentImage.Rating 从 {_currentImage.Rating} 到 {rating}");
        _currentImage.Rating = rating;
        
        if (_currentImage.ImageFile != null)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: 准备调用 RatingService.SetRatingAsync, 文件={_currentImage.ImageFile.Path}");
            await _ratingService.SetRatingAsync(_currentImage.ImageFile, rating);
            System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: RatingService.SetRatingAsync 完成");
            
            System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: 更新 _currentImage.RatingSource 为 WinRT");
            _currentImage.RatingSource = Services.RatingSource.WinRT;
            System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: 更新 ViewModel.RatingSource 为 WinRT");
            RatingSource = Services.RatingSource.WinRT.ToString();
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: _currentImage.ImageFile 为 null，跳过 RatingService 调用");
        }
        
        // 触发评级更新事件，通知其他组件（如 MainViewModel）
        RatingUpdated?.Invoke(this, (_currentImage, rating));
        System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: 已触发 RatingUpdated 事件");
        
        System.Diagnostics.Debug.WriteLine($"[ImageViewerViewModel] SetRatingAsync: 执行完成");
    }

    public async Task OpenInExplorerAsync(string path)
    {
        try
        {
            var folderPath = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folderPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenInExplorerAsync error: {ex}");
        }
    }

    public async Task CopyPathToClipboardAsync(string path)
    {
        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(path);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CopyPathToClipboardAsync error: {ex}");
        }
    }

    public async Task CopyToClipboardAsync(string text)
    {
        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CopyToClipboardAsync error: {ex}");
        }
    }

    private static string FormatFileSize(ulong size)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double len = size;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.0} {sizes[order]}";
    }

    public void Clear()
    {
        _currentImage = null;
        ImageName = "None";
        Resolution = "None";
        FileSize = "None";
        CaptureDate = null;
        CaptureTime = null;
        FormattedDateTime = "None";
        CaptureYear = "None";
        CaptureMonth = "None";
        CaptureDay = "None";
        CaptureTimeOfDay = "None";
        HasDeviceInfo = false;
        DeviceInfo.Clear();
        FilePaths.Clear();
        Rating = 0;
        RatingSource = "None";
        Dpi = "None";
        BitDepth = "None";
        LensModel = "None";
        FocalLength = "None";
        ExposureTime = "None";
        FNumber = "None";
        Iso = "None";
        ExposureProgram = "None";
        ExposureBias = "None";
        Flash = "None";
        FileFormat = "None";
        FileFormatColor = "Gray";
    }
}
