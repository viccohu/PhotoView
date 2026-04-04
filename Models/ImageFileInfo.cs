using System;
using System.Collections.Generic;
using System.ComponentModel;
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

            var thumbnail = await ImageFile.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                optimalSize,
                ThumbnailOptions.None).AsTask(cancellationToken);

            if (thumbnail != null && thumbnail.Size > 0)
            {
                using (thumbnail)
                {
                    return await CreateBitmapOnUIThreadAsync(thumbnail, optimalSize, cancellationToken);
                }
            }

            return new BitmapImage();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetThumbnailAsync error: {ex}");
            return new BitmapImage();
        }
    }

    private static async Task<BitmapImage> CreateBitmapOnUIThreadAsync(
        IRandomAccessStream stream, 
        uint decodePixelWidth,
        CancellationToken cancellationToken)
    {
        if (AppLifetime.IsShuttingDown)
            return new BitmapImage();

        BitmapImage? bitmap = null;
        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        var tcs = new TaskCompletionSource<bool>();

        if (dispatcherQueue.HasThreadAccess)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
                {
                    return new BitmapImage();
                }

                bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = (int)decodePixelWidth;
                bitmap.SetSource(stream);
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateBitmapOnUIThreadAsync error: {ex}");
                return new BitmapImage();
            }
        }

        if (!dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
                {
                    tcs.TrySetCanceled();
                    return;
                }

                bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = (int)decodePixelWidth;
                bitmap.SetSource(stream);

                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }))
        {
            throw new OperationCanceledException("Failed to enqueue thumbnail creation.", cancellationToken);
        }

        await tcs.Task;
        return bitmap!;
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

                var dispatcher = App.MainWindow.DispatcherQueue;
                if (dispatcher.HasThreadAccess)
                {
                    lock (_thumbnailLoadLock)
                    {
                        if (localVersion != _loadVersion || cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
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
                            if (localVersion != _loadVersion || cancellationToken.IsCancellationRequested)
                                return;

                            Thumbnail = result;
                            _loadedThumbnailSize = size;
                        }
                    }))
                    {
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EnsureThumbnailAsync error: {ex}");
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
        var designHeight = size switch
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
        const double borderPadding = 12d;
        
        // 内容区域高度 = 外层高度 - Padding
        var contentHeight = designHeight - borderPadding;
        // 内容区域宽度 = 内容高度 * 宽高比
        var contentWidth = contentHeight * aspectRatio;
        // 外层宽度 = 内容宽度 + Padding
        DisplayWidth = Math.Max(1d, contentWidth + borderPadding);
        DisplayHeight = designHeight;
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
                return;
            }
        }
    }
}
