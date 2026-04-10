using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Helpers;
using PhotoView.Services;
using Windows.Graphics.Display;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.Storage;

namespace PhotoView.Models;

public class ImageFileInfo : INotifyPropertyChanged
{
    private static readonly SemaphoreSlim _globalThumbnailLoadSemaphore = new(8);
    
    private BitmapImage? _thumbnail;
    private int _loadVersion;
    private CancellationTokenSource? _thumbnailLoadCts;
    private bool _isSelected;
    private bool _isPendingDelete;
    private double _displayWidth;
    private double _displayHeight;
    private readonly object _thumbnailLoadLock = new();
    private ThumbnailSize? _loadedThumbnailSize;
    private ThumbnailSize? _requestedThumbnailSize;
    private ImageGroup? _group;
    private bool _isPrimary;
    private uint _rating;
    private bool _isRatingLoading = true;
    private RatingSource _ratingSource = RatingSource.Unknown;
    private ObservableCollection<FormatTag> _formatTags = new();

    private static readonly uint[] SystemThumbnailSizes = { 96, 160, 256, 512, 1024 };

    private static double DpiScale => DisplayInformation.GetForCurrentView().RawPixelsPerViewPixel;

    public ImageFileInfo(
        int width,
        int height,
        string title,
        StorageFile imageFile,
        string name,
        string type)
    {
        Width = width;
        Height = height;
        ImageTitle = title;
        ImageFile = imageFile ?? throw new ArgumentNullException(nameof(imageFile));
        ImageName = name;
        ImageFileType = type;
        _isSelected = false;
        UpdateDisplaySize(ThumbnailSize.Medium);
        UpdateFormatTags();
    }

    public int Width { get; }

    public int Height { get; }

    public StorageFile ImageFile { get; }

    private async Task<BitmapImage> GetThumbnailAsync(ThumbnailSize size, CancellationToken cancellationToken)
    {
        try
        {
            var requestedSize = (uint)size;
            var optimalSize = GetOptimalThumbnailSize(requestedSize);
            var dispatcher = App.MainWindow.DispatcherQueue;

            if (dispatcher.HasThreadAccess)
            {
                return await GetThumbnailOnUIThreadAsync(optimalSize, cancellationToken);
            }
            else
            {
                var tcs = new TaskCompletionSource<BitmapImage>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        var bmp = await GetThumbnailOnUIThreadAsync(optimalSize, cancellationToken);
                        tcs.TrySetResult(bmp);
                    }
                    catch (OperationCanceledException)
                    {
                        tcs.TrySetResult(new BitmapImage());
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }))
                {
                    System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Skip thumbnail enqueue for {ImageName}");
                    return new BitmapImage();
                }

                return await tcs.Task;
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Thumbnail request canceled for {ImageName}");
            throw;
        }
        catch (Exception ex)
        {
            return new BitmapImage();
        }
    }

    private async Task<BitmapImage> GetThumbnailOnUIThreadAsync(
        uint optimalSize,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
            return new BitmapImage();

        // 优先尝试获取系统缩略图（最快）
        try
        {
            using var thumbnail = await ImageFile.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                optimalSize,
                ThumbnailOptions.None);

            if (thumbnail != null && thumbnail.Size > 0)
            {
                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = (int)optimalSize;
                await bitmap.SetSourceAsync(thumbnail);
                return bitmap;
            }
        }
        catch (Exception ex)
        {
        }

        // 系统缩略图失败，回退到完整解码
        try
        {
            var thumbnailService = App.GetService<Contracts.Services.IThumbnailService>();
            var imageSource = await thumbnailService.GetThumbnailByLongSideAsync(ImageFile, optimalSize, cancellationToken);
            
            if (imageSource is BitmapImage bitmapImage)
            {
                return bitmapImage;
            }
            else if (imageSource != null)
            {
                var bitmap = new BitmapImage();
                using var stream = new InMemoryRandomAccessStream();
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
        }
        catch (Exception ex)
        {
        }

        return new BitmapImage();
    }

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsPendingDelete
    {
        get => _isPendingDelete;
        set => SetProperty(ref _isPendingDelete, value);
    }

    public double DisplayWidth
    {
        get => _displayWidth;
        private set => SetProperty(ref _displayWidth, value);
    }

    public double DisplayHeight
    {
        get => _displayHeight;
        private set => SetProperty(ref _displayHeight, value);
    }

    public async Task EnsureThumbnailAsync(ThumbnailSize size)
    {
        if (AppLifetime.IsShuttingDown)
            return;

        lock (_thumbnailLoadLock)
        {
            if (Thumbnail != null && _loadedThumbnailSize == size)
                return;

            if (_thumbnailLoadCts != null &&
                !_thumbnailLoadCts.IsCancellationRequested &&
                _requestedThumbnailSize == size)
            {
                return;
            }
        }

        CancellationTokenSource localCts;
        int localVersion;

        lock (_thumbnailLoadLock)
        {
            _thumbnailLoadCts?.Cancel();
            _thumbnailLoadCts = new CancellationTokenSource();
            localCts = _thumbnailLoadCts;
            localVersion = ++_loadVersion;
            _requestedThumbnailSize = size;
        }

        var cancellationToken = localCts.Token;

        try
        {
            await _globalThumbnailLoadSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                if (AppLifetime.IsShuttingDown)
                    return;

                var result = await GetThumbnailAsync(size, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                var dispatcher = App.MainWindow.DispatcherQueue;
                if (dispatcher.HasThreadAccess)
                {
                    lock (_thumbnailLoadLock)
                    {
                        if (localVersion != _loadVersion ||
                            cancellationToken.IsCancellationRequested ||
                            AppLifetime.IsShuttingDown ||
                            _requestedThumbnailSize != size)
                            return;

                        Thumbnail = result;
                        _loadedThumbnailSize = size;
                    }
                }
                else
                {
                    if (!dispatcher.TryEnqueue(() =>
                    {
                        if (AppLifetime.IsShuttingDown)
                            return;

                        lock (_thumbnailLoadLock)
                        {
                            if (localVersion != _loadVersion ||
                                cancellationToken.IsCancellationRequested ||
                                _requestedThumbnailSize != size ||
                                AppLifetime.IsShuttingDown)
                                return;

                            Thumbnail = result;
                            _loadedThumbnailSize = size;
                        }
                    }))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Skip thumbnail commit enqueue for {ImageName}");
                        return;
                    }
                }
            }
            finally
            {
                _globalThumbnailLoadSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] EnsureThumbnailAsync canceled for {ImageName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] EnsureThumbnailAsync failed for {ImageName}: {ex.Message}");
        }
    }

    public void CancelThumbnailLoad()
    {
        lock (_thumbnailLoadLock)
        {
            _thumbnailLoadCts?.Cancel();
            _loadVersion++;
            _requestedThumbnailSize = null;
        }
    }

    public void ClearThumbnail()
    {
        lock (_thumbnailLoadLock)
        {
            _thumbnailLoadCts?.Cancel();
            _loadVersion++;
            Thumbnail = null;
            _loadedThumbnailSize = null;
            _requestedThumbnailSize = null;
        }
    }

    public void UpdateDisplaySize(ThumbnailSize size)
    {
        var designSize = size switch
        {
            ThumbnailSize.Small => 120d,
            ThumbnailSize.Medium => 256d,
            ThumbnailSize.Large => 512d,
            _ => 256d
        };

        var aspectRatio = AspectRatio;
        if (double.IsNaN(aspectRatio) || double.IsInfinity(aspectRatio) || aspectRatio <= 0)
        {
            aspectRatio = 1d;
        }

        // Border Padding = 6 (左右各6，上下各6)
        // 缩略图准确计算宽高
        const double borderPadding = 0d;
        
        double contentWidth, contentHeight;
        
        // 根据图片方向决定是固定高度还是固定宽度
        if (Width >= Height)
        {
            // 横构图：固定高度，计算宽度
            contentHeight = designSize - borderPadding;
            contentWidth = contentHeight * aspectRatio;
        }
        else
        {
            // 竖构图：固定宽度，计算高度
            contentWidth = designSize - borderPadding;
            contentHeight = contentWidth / aspectRatio;
        }
        
        // 外层尺寸 = 内容尺寸 + Padding
        DisplayWidth = Math.Max(1d, contentWidth + borderPadding);
        DisplayHeight = Math.Max(1d, contentHeight + borderPadding);
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        return false;
    }

    private static uint GetOptimalThumbnailSize(uint requestedSize)
    {
        foreach (var size in SystemThumbnailSizes)
        {
            if (size >= requestedSize)
            {
                return size;
            }
        }
        return SystemThumbnailSizes[^1];
    }

    public string ImageName { get; }

    public string ImageFileType { get; }

    public string FileType => ImageFileType;

    public string ImageDimensions => $"{Width} x {Height}";

    public string ImageTitle { get; }

    public uint Rating
    {
        get => _rating;
        set
        {
            if (SetProperty(ref _rating, value))
            {
                OnPropertyChanged(nameof(RatingValue));
            }
        }
    }

    public RatingSource RatingSource
    {
        get => _ratingSource;
        set => SetProperty(ref _ratingSource, value);
    }

    public double RatingValue
    {
        get
        {
            if (_rating == 0)
                return -1;
            return RatingToStars(_rating);
        }
        set
        {
            uint newRating;

            if (value < 0 || value == 0)
            {
                newRating = 0;
            }
            else
            {
                int stars = (int)Math.Round(value, MidpointRounding.AwayFromZero);
                stars = Math.Clamp(stars, 1, 5);
                newRating = StarsToRating(stars);
            }

            if (newRating != _rating)
            {
                Rating = newRating;
            }
        }
    }

    public static int RatingToStars(uint rating)
    {
        if (rating == 0) return 0;
        if (rating >= 1 && rating <= 12) return 1;
        if (rating >= 13 && rating <= 37) return 2;
        if (rating >= 38 && rating <= 62) return 3;
        if (rating >= 63 && rating <= 87) return 4;
        if (rating >= 88 && rating <= 99) return 5;
        return 0;
    }

    public static uint StarsToRating(int stars)
    {
        return stars switch
        {
            0 => 0,
            1 => 1,
            2 => 25,
            3 => 50,
            4 => 75,
            5 => 99,
            _ => 0
        };
    }

    public bool IsRatingLoading
    {
        get => _isRatingLoading;
        set => SetProperty(ref _isRatingLoading, value);
    }

    public async Task LoadRatingAsync(RatingService ratingService)
    {
        IsRatingLoading = true;
        try
        {
            var (rating, source) = await ratingService.GetRatingAsync(ImageFile);
            Rating = rating;
            RatingSource = source;
        }
        finally
        {
            IsRatingLoading = false;
        }
    }

    public async Task SetRatingAsync(RatingService ratingService, uint newRating)
    {
        try
        {
            await ratingService.SetRatingAsync(ImageFile, newRating);
            Rating = newRating;
            RatingSource = ratingService.IsWinRTRatingSupported(ImageFile.FileType) ? RatingSource.WinRT : RatingSource.Cache;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SetRatingAsync] 错误: {ex.Message}");
        }
    }

    public int AutoWidth => Height == 0 ? 200 : (int)((Width * 200.0) / Height);

    public double AspectRatio => Height == 0 ? 1.5 : (double)Width / Height;

    public ImageGroup? Group => _group;

    public bool IsPrimary => _isPrimary;

    public List<ImageFileInfo>? AlternateFormats =>
        _group?.Images.Where(i => i != this).ToList();

    public bool HasAlternateFormats =>
        AlternateFormats != null && AlternateFormats.Count > 0;

    public string AlternateFormatsText
    {
        get
        {
            if (!HasAlternateFormats || AlternateFormats == null)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"Current: {FileType.ToUpperInvariant()}");
            sb.AppendLine();
            sb.AppendLine("Other formats:");
            foreach (var format in AlternateFormats)
            {
                sb.AppendLine($"• {format.FileType.ToUpperInvariant()}");
            }
            return sb.ToString().TrimEnd();
        }
    }

    public void SetGroupInfo(ImageGroup group, bool isPrimary)
    {
        _group = group;
        _isPrimary = isPrimary;
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(IsPrimary));
        OnPropertyChanged(nameof(AlternateFormats));
        OnPropertyChanged(nameof(HasAlternateFormats));
        OnPropertyChanged(nameof(AlternateFormatsText));
        UpdateFormatTags();
    }

    public void RefreshGroupProperties()
    {
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(IsPrimary));
        OnPropertyChanged(nameof(AlternateFormats));
        OnPropertyChanged(nameof(HasAlternateFormats));
        OnPropertyChanged(nameof(AlternateFormatsText));
        OnPropertyChanged(nameof(IsPendingDelete));
    }

    public ObservableCollection<FormatTag> FormatTags
    {
        get => _formatTags;
        private set => SetProperty(ref _formatTags, value);
    }

    public void UpdateFormatTags()
    {
        FormatTags.Clear();
        
        if (Group == null)
        {
            var ext = ImageFileType.ToLowerInvariant();
            FormatTags.Add(new FormatTag
            {
                Format = GetFormatDisplayName(ext),
                Color = GetFormatColor(ext),
                IsLast = true
            });
        }
        else
        {
            var formats = new HashSet<string>();
            foreach (var image in Group.Images)
            {
                var ext = image.ImageFileType.ToLowerInvariant();
                formats.Add(ext);
            }
            
            var sortedFormats = formats
                .OrderByDescending(f => IsRawFormat(f))
                .ThenBy(f => f)
                .ToList();
            
            for (int i = 0; i < sortedFormats.Count; i++)
            {
                var ext = sortedFormats[i];
                FormatTags.Add(new FormatTag
                {
                    Format = GetFormatDisplayName(ext),
                    Color = GetFormatColor(ext),
                    IsLast = i == sortedFormats.Count - 1
                });
            }
        }
    }

    private bool IsRawFormat(string ext)
    {
        return ext switch
        {
            ".cr2" or ".cr3" or ".crw" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" or ".dng" or ".orf" or ".pef" or ".raf" or ".rw2" or ".raw" or ".3fr" or ".fff" or ".mos" or ".erf" or ".dcr" or ".mrw" or ".rwl" or ".srw" => true,
            _ => false
        };
    }

    private string GetFormatDisplayName(string ext)
    {
        return ext switch
        {
            ".jpg" or ".jpeg" => "JPG",
            ".png" => "PNG",
            ".gif" => "GIF",
            ".bmp" => "BMP",
            ".tiff" or ".tif" => "TIF",
            ".webp" => "WebP",
            ".cr2" or ".cr3" or ".crw" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" or ".dng" or ".orf" or ".pef" or ".raf" or ".rw2" or ".raw" or ".3fr" or ".fff" or ".mos" or ".erf" or ".dcr" or ".mrw" or ".rwl" or ".srw" => "RAW",
            _ => ext.ToUpper().TrimStart('.')
        };
    }

    private string GetFormatColor(string ext)
    {
        var compressedFormats = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        var rawFormats = new[] { ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".orf", ".pef", ".raf", ".rw2", ".raw", ".3fr", ".fff", ".mos", ".erf", ".dcr", ".mrw", ".rwl", ".srw" };
        var losslessFormats = new[] { ".tiff", ".tif", ".bmp" };

        if (compressedFormats.Contains(ext))
            return "#00c8ff";
        if (rawFormats.Contains(ext))
            return "#ffb300";
        if (losslessFormats.Contains(ext))
            return "#2bff00";

        return "#808080";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (AppLifetime.IsShuttingDown)
            return;

        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        if (dispatcherQueue.HasThreadAccess)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        else
        {
            if (!dispatcherQueue.TryEnqueue(() =>
            {
                if (AppLifetime.IsShuttingDown)
                    return;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }))
            {
                System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Skip PropertyChanged enqueue for {propertyName} on {ImageName}");
                return;
            }
        }
    }
}
