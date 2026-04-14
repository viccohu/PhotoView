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
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Helpers;
using PhotoView.Services;
using Windows.Graphics.Display;
using Windows.Storage.FileProperties;
using Windows.Storage;

namespace PhotoView.Models;

public class ImageFileInfo : INotifyPropertyChanged
{
    private static readonly SemaphoreSlim _globalThumbnailLoadSemaphore = new(8);
    
    private ImageSource? _thumbnail;
    private int _loadVersion;
    private CancellationTokenSource? _thumbnailLoadCts;
    private bool _isSelected;
    private bool _isPendingDelete;
    private bool _isThumbnailLoading;
    private bool _isThumbnailFailed;
    private int _width;
    private int _height;
    private string _imageTitle = string.Empty;
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
        _width = width;
        _height = height;
        _imageTitle = title;
        ImageFile = imageFile ?? throw new ArgumentNullException(nameof(imageFile));
        ImageName = name;
        ImageFileType = type;
        _isSelected = false;
        UpdateDisplaySize(ThumbnailSize.Medium);
        UpdateFormatTags();
    }

    public int Width
    {
        get => _width;
        private set
        {
            if (SetProperty(ref _width, value))
            {
                OnPropertyChanged(nameof(ImageDimensions));
                OnPropertyChanged(nameof(AutoWidth));
                OnPropertyChanged(nameof(AspectRatio));
            }
        }
    }

    public int Height
    {
        get => _height;
        private set
        {
            if (SetProperty(ref _height, value))
            {
                OnPropertyChanged(nameof(ImageDimensions));
                OnPropertyChanged(nameof(AutoWidth));
                OnPropertyChanged(nameof(AspectRatio));
            }
        }
    }

    public StorageFile ImageFile { get; }

    private async Task<ImageSource?> GetThumbnailAsync(ThumbnailSize size, CancellationToken cancellationToken)
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
                var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!dispatcher.TryEnqueue(async () =>
                {
                    try
                    {
                        var thumbnail = await GetThumbnailOnUIThreadAsync(optimalSize, cancellationToken);
                        tcs.TrySetResult(thumbnail);
                    }
                    catch (OperationCanceledException)
                    {
                        tcs.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }))
                {
                    System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Skip thumbnail enqueue for {ImageName}");
                    return null;
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
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Thumbnail request failed for {ImageName}: {ex.Message}");
            return null;
        }
    }

    private async Task<ImageSource?> GetThumbnailOnUIThreadAsync(
        uint optimalSize,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
            return null;

        var thumbnailService = App.GetService<Contracts.Services.IThumbnailService>();
        var cachedThumbnail = await thumbnailService.TryGetCachedThumbnailAsync(
            ImageFile,
            optimalSize,
            forceFullDecode: false,
            cancellationToken);
        if (cachedThumbnail?.ImageSource != null)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Thumbnail cache hit for {ImageName}, size={optimalSize}");
            return cachedThumbnail.ImageSource;
        }

        // 优先尝试获取系统缩略图（最快）
        try
        {
            var thumbnail = await ImageFile.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                optimalSize,
                ThumbnailOptions.None).AsTask(cancellationToken);

            if (thumbnail != null && thumbnail.Size > 0 && thumbnail.Type == ThumbnailType.Image)
            {
                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = (int)optimalSize;
                await bitmap.SetSourceAsync(thumbnail).AsTask(cancellationToken);
                await thumbnailService.StoreCachedThumbnailAsync(
                    ImageFile,
                    optimalSize,
                    forceFullDecode: false,
                    CreateDecodeResult(bitmap),
                    cancellationToken);
                System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] System thumbnail image used for {ImageName}, size={optimalSize}");
                return bitmap;
            }
            else if (thumbnail != null && thumbnail.Type != ThumbnailType.Image)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Ignore system thumbnail {thumbnail.Type} for {ImageName}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] System thumbnail failed for {ImageName}: {ex.Message}");
        }

        // System can return an icon before it has generated the real thumbnail. Prefer immediate
        // local fallback for first-screen responsiveness, then retry the system API only as a last resort.
        try
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Fallback thumbnail decode start for {ImageName}, size={optimalSize}");
            var imageSource = await thumbnailService.GetThumbnailByLongSideAsync(ImageFile, optimalSize, cancellationToken);
            if (imageSource != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Fallback thumbnail decode used for {ImageName}, size={optimalSize}");
                return imageSource;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Fallback thumbnail decode failed for {ImageName}: {ex.Message}");
        }

        await Task.Delay(150, cancellationToken);
        try
        {
            var thumbnail = await ImageFile.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                optimalSize,
                ThumbnailOptions.None).AsTask(cancellationToken);

            if (thumbnail != null && thumbnail.Size > 0 && thumbnail.Type == ThumbnailType.Image)
            {
                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = (int)optimalSize;
                await bitmap.SetSourceAsync(thumbnail).AsTask(cancellationToken);
                await thumbnailService.StoreCachedThumbnailAsync(
                    ImageFile,
                    optimalSize,
                    forceFullDecode: false,
                    CreateDecodeResult(bitmap),
                    cancellationToken);
                System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Retried system thumbnail image used for {ImageName}, size={optimalSize}");
                return bitmap;
            }
            else if (thumbnail != null && thumbnail.Type != ThumbnailType.Image)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Ignore retried system thumbnail {thumbnail.Type} for {ImageName}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Retried system thumbnail failed for {ImageName}: {ex.Message}");
        }

        // System retry and local fallback both failed.
        return null;
    }

    private static DecodeResult CreateDecodeResult(BitmapImage bitmap)
    {
        return new DecodeResult(
            (uint)Math.Max(0, bitmap.PixelWidth),
            (uint)Math.Max(0, bitmap.PixelHeight),
            bitmap);
    }

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public bool IsThumbnailLoading
    {
        get => _isThumbnailLoading;
        private set => SetProperty(ref _isThumbnailLoading, value);
    }

    public bool IsThumbnailFailed
    {
        get => _isThumbnailFailed;
        private set => SetProperty(ref _isThumbnailFailed, value);
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
            IsThumbnailLoading = true;
            IsThumbnailFailed = false;
        }

        var cancellationToken = localCts.Token;

        try
        {
            await _globalThumbnailLoadSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                if (AppLifetime.IsShuttingDown)
                {
                    ClearThumbnailLoadingIfCurrent(localVersion, size);
                    return;
                }

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
                        IsThumbnailLoading = false;
                        IsThumbnailFailed = result == null;
                        System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Thumbnail commit for {ImageName}, size={size}, success={result != null}");
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
                            IsThumbnailLoading = false;
                            IsThumbnailFailed = result == null;
                            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Thumbnail commit for {ImageName}, size={size}, success={result != null}");
                        }
                    }))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Skip thumbnail commit enqueue for {ImageName}");
                        ClearThumbnailLoadingIfCurrent(localVersion, size);
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
            ClearThumbnailLoadingIfCurrent(localVersion, size);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] EnsureThumbnailAsync failed for {ImageName}: {ex.Message}");
            ClearThumbnailLoadingIfCurrent(localVersion, size);
        }
    }

    private void ClearThumbnailLoadingIfCurrent(int loadVersion, ThumbnailSize size)
    {
        lock (_thumbnailLoadLock)
        {
            if (loadVersion == _loadVersion && _requestedThumbnailSize == size)
            {
                IsThumbnailLoading = false;
            }
        }
    }

    public void CancelThumbnailLoad()
    {
        lock (_thumbnailLoadLock)
        {
            _thumbnailLoadCts?.Cancel();
            _loadVersion++;
            _requestedThumbnailSize = null;
            IsThumbnailLoading = false;
            IsThumbnailFailed = false;
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
            IsThumbnailLoading = false;
            IsThumbnailFailed = false;
        }
    }

    public void UpdateDisplaySize(ThumbnailSize size)
    {
        var designSize = size switch
        {
            ThumbnailSize.Small => 160d,
            ThumbnailSize.Medium => 256d,
            ThumbnailSize.Large => 512d,
            _ => 256d
        };

        DisplayWidth = designSize;
        DisplayHeight = designSize;
    }

    public void UpdateMetadata(int width, int height, string title)
    {
        Width = width;
        Height = height;
        ImageTitle = title;
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

    public string ImageTitle
    {
        get => _imageTitle;
        private set => SetProperty(ref _imageTitle, value);
    }

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
            var ext = NormalizeFormatExtension(ImageFileType);
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
                var ext = NormalizeFormatExtension(image.ImageFileType);
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

    private static string NormalizeFormatExtension(string ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
            return string.Empty;

        ext = ext.Trim().ToLowerInvariant();
        return ext.StartsWith('.') ? ext : $".{ext}";
    }

    private bool IsRawFormat(string ext)
    {
        ext = NormalizeFormatExtension(ext);
        return ext switch
        {
            ".cr2" or ".cr3" or ".crw" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" or ".dng" or ".orf" or ".pef" or ".raf" or ".rw2" or ".raw" or ".3fr" or ".fff" or ".mos" or ".erf" or ".dcr" or ".mrw" or ".rwl" or ".srw" => true,
            _ => false
        };
    }

    private string GetFormatDisplayName(string ext)
    {
        ext = NormalizeFormatExtension(ext);
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
        ext = NormalizeFormatExtension(ext);
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
