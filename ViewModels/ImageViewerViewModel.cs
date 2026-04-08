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
    private string _imageName = string.Empty;

    [ObservableProperty]
    private string _resolution = string.Empty;

    [ObservableProperty]
    private string _fileSize = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _captureDate;

    [ObservableProperty]
    private TimeSpan? _captureTime;

    [ObservableProperty]
    private bool _hasDeviceInfo;

    [ObservableProperty]
    private ObservableCollection<string> _deviceInfo = new();

    [ObservableProperty]
    private ObservableCollection<FilePathItem> _filePaths = new();

    [ObservableProperty]
    private uint _rating;

    [ObservableProperty]
    private string _ratingSource = string.Empty;

    [ObservableProperty]
    private string _dpi = string.Empty;

    [ObservableProperty]
    private string _bitDepth = string.Empty;

    [ObservableProperty]
    private string _lensModel = string.Empty;

    [ObservableProperty]
    private string _focalLength = string.Empty;

    [ObservableProperty]
    private string _exposureTime = string.Empty;

    [ObservableProperty]
    private string _fNumber = string.Empty;

    [ObservableProperty]
    private string _iso = string.Empty;

    [ObservableProperty]
    private string _exposureProgram = string.Empty;

    [ObservableProperty]
    private string _exposureBias = string.Empty;

    [ObservableProperty]
    private string _flash = string.Empty;

    [ObservableProperty]
    private string _fileFormat = string.Empty;

    [ObservableProperty]
    private string _fileFormatColor = "Gray";

    public ImageViewerViewModel(IExifService exifService, RatingService ratingService)
    {
        _exifService = exifService;
        _ratingService = ratingService;
    }

    public async Task LoadFileInfoAsync(ImageFileInfo imageFileInfo)
    {
        if (imageFileInfo == null)
            return;

        _currentImage = imageFileInfo;
        ImageName = imageFileInfo.ImageName;
        Resolution = $"{imageFileInfo.Width} x {imageFileInfo.Height}";
        Rating = imageFileInfo.Rating;
        RatingSource = imageFileInfo.RatingSource.ToString();

        SetFileFormatInfo(imageFileInfo.ImageName);

        if (imageFileInfo.ImageFile != null)
        {
            await LoadFileDetailsAsync(imageFileInfo.ImageFile);
        }

        LoadFilePaths(imageFileInfo);
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

    private async Task LoadFileDetailsAsync(StorageFile file)
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
            var exifData = await _exifService.GetExifDataAsync(file);

            if (exifData.DateTaken.HasValue)
            {
                CaptureDate = exifData.DateTaken.Value.Date;
                CaptureTime = exifData.DateTaken.Value.TimeOfDay;
            }

            if (exifData.DpiX.HasValue && exifData.DpiY.HasValue)
            {
                Dpi = $"{exifData.DpiX.Value:F0} x {exifData.DpiY.Value:F0}";
            }

            if (exifData.BitDepth.HasValue)
            {
                BitDepth = $"{exifData.BitDepth.Value} bit";
            }

            UpdateDeviceInfo(exifData);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFileDetailsAsync - EXIF读取错误: {ex}");
        }
    }

    private void UpdateDeviceInfo(ExifData exifData)
    {
        DeviceInfo.Clear();

        var deviceInfoList = new System.Collections.Generic.List<string>();

        if (!string.IsNullOrEmpty(exifData.CameraManufacturer))
        {
            deviceInfoList.Add(exifData.CameraManufacturer);
        }

        if (!string.IsNullOrEmpty(exifData.CameraModel))
        {
            deviceInfoList.Add(exifData.CameraModel);
        }

        foreach (var info in deviceInfoList)
        {
            DeviceInfo.Add(info);
        }

        HasDeviceInfo = deviceInfoList.Count > 0;

        if (!string.IsNullOrEmpty(exifData.LensModel))
        {
            LensModel = exifData.LensModel;
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
        }

        if (exifData.ExposureTime.HasValue)
        {
            ExposureTime = exifData.GetFormattedExposureTime();
        }

        if (exifData.FNumber.HasValue)
        {
            FNumber = exifData.GetFormattedFNumber();
        }

        if (exifData.ISOSpeed.HasValue)
        {
            Iso = $"ISO {exifData.ISOSpeed.Value}";
        }

        if (exifData.ExposureProgram.HasValue)
        {
            ExposureProgram = exifData.GetFormattedExposureProgram();
        }

        if (exifData.ExposureBias.HasValue)
        {
            ExposureBias = $"{(exifData.ExposureBias.Value >= 0 ? "+" : "")}{exifData.ExposureBias.Value:F1} EV";
        }

        if (exifData.Flash.HasValue)
        {
            Flash = exifData.GetFormattedFlash();
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
        ImageName = string.Empty;
        Resolution = string.Empty;
        FileSize = string.Empty;
        CaptureDate = null;
        CaptureTime = null;
        HasDeviceInfo = false;
        DeviceInfo.Clear();
        FilePaths.Clear();
        Rating = 0;
        RatingSource = string.Empty;
        Dpi = string.Empty;
        BitDepth = string.Empty;
        LensModel = string.Empty;
        FocalLength = string.Empty;
        ExposureTime = string.Empty;
        FNumber = string.Empty;
        Iso = string.Empty;
        ExposureProgram = string.Empty;
        ExposureBias = string.Empty;
        Flash = string.Empty;
        FileFormat = string.Empty;
        FileFormatColor = "Gray";
    }
}
