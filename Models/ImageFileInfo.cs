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
    private const uint FastPreviewLongSidePixels = 160;
    
    private ImageSource? _thumbnail;
    private ImageSource? _fastPreviewThumbnail;
    private int _fastPreviewLoadVersion;
    private int _targetThumbnailLoadVersion;
    private CancellationTokenSource? _fastPreviewLoadCts;
    private CancellationTokenSource? _targetThumbnailLoadCts;
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
    private ThumbnailSize? _loadedTargetThumbnailSize;
    private ThumbnailSize? _requestedFastPreviewSize;
    private ThumbnailSize? _requestedTargetThumbnailSize;
    private ThumbnailLoadStage _thumbnailStage = ThumbnailLoadStage.None;
    private ImageGroup? _group;
    private bool _isPrimary;
    private BurstPhotoGroup? _burstGroup;
    private bool _isBurstPrimary;
    private bool _isBurstDisplayCover;
    private bool _isBurstChildVisible;
    private uint _rating;
    private bool _isRatingLoading = true;
    private bool _isRatingLoaded;
    private bool _isRatingLoadRequested;
    private int _ratingEditVersion;
    private RatingSource _ratingSource = RatingSource.Unknown;
    private ObservableCollection<FormatTag> _formatTags = new();
    private DateTime? _dateTaken;

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

    private async Task<ImageSource?> RequestFastPreviewAsync(ThumbnailSize size, CancellationToken cancellationToken)
    {
        var thumbnailService = App.GetService<Contracts.Services.IThumbnailService>();
        var result = await thumbnailService.GetFastPreviewAsync(
            ImageFile,
            FastPreviewLongSidePixels,
            cancellationToken);
        return result?.ImageSource;
    }

    private async Task<ImageSource?> RequestTargetThumbnailAsync(ThumbnailSize size, CancellationToken cancellationToken)
    {
        var requestedSize = (uint)size;
        var optimalSize = GetOptimalThumbnailSize(requestedSize);
        var thumbnailService = App.GetService<Contracts.Services.IThumbnailService>();
        var result = await thumbnailService.GetTargetThumbnailAsync(
            ImageFile,
            optimalSize,
            cancellationToken);
        return result?.ImageSource;
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
            // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Thumbnail cache hit for {ImageName}, size={optimalSize}");
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
                // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] System thumbnail image used for {ImageName}, size={optimalSize}");
                return bitmap;
            }
            else if (thumbnail != null && thumbnail.Type != ThumbnailType.Image)
            {
                // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Ignore system thumbnail {thumbnail.Type} for {ImageName}");
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
            // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Fallback thumbnail decode start for {ImageName}, size={optimalSize}");
            var imageSource = await thumbnailService.GetThumbnailByLongSideAsync(ImageFile, optimalSize, cancellationToken);
            if (imageSource != null)
            {
                // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Fallback thumbnail decode used for {ImageName}, size={optimalSize}");
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
                // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Retried system thumbnail image used for {ImageName}, size={optimalSize}");
                return bitmap;
            }
            else if (thumbnail != null && thumbnail.Type != ThumbnailType.Image)
            {
                // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Ignore retried system thumbnail {thumbnail.Type} for {ImageName}");
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

    public bool HasFastPreview
    {
        get
        {
            lock (_thumbnailLoadLock)
            {
                return _thumbnailStage >= ThumbnailLoadStage.FastPreview;
            }
        }
    }

    public bool HasTargetThumbnail
    {
        get
        {
            lock (_thumbnailLoadLock)
            {
                return _thumbnailStage == ThumbnailLoadStage.Target;
            }
        }
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

    public async Task EnsureFastPreviewAsync(ThumbnailSize size)
    {
        if (AppLifetime.IsShuttingDown)
            return;

        lock (_thumbnailLoadLock)
        {
            if (Thumbnail != null && _thumbnailStage >= ThumbnailLoadStage.FastPreview)
                return;

            if (_fastPreviewLoadCts != null &&
                !_fastPreviewLoadCts.IsCancellationRequested &&
                _requestedFastPreviewSize == size)
            {
                return;
            }
        }

        CancellationTokenSource localCts;
        int localVersion;

        lock (_thumbnailLoadLock)
        {
            _fastPreviewLoadCts?.Cancel();
            _fastPreviewLoadCts = new CancellationTokenSource();
            localCts = _fastPreviewLoadCts;
            localVersion = ++_fastPreviewLoadVersion;
            _requestedFastPreviewSize = size;
            IsThumbnailLoading = Thumbnail == null;
            IsThumbnailFailed = false;
        }

        var cancellationToken = localCts.Token;

        try
        {
            await _globalThumbnailLoadSemaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await RequestFastPreviewAsync(size, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                CommitThumbnailResult(localVersion, size, result, ThumbnailLoadStage.FastPreview, cancellationToken);
            }
            finally
            {
                _globalThumbnailLoadSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage.FastPreview, localVersion, size);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] EnsureFastPreviewAsync failed for {ImageName}: {ex.Message}");
            ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage.FastPreview, localVersion, size);
        }
    }

    public async Task EnsureThumbnailAsync(ThumbnailSize size)
    {
        if (AppLifetime.IsShuttingDown)
            return;

        lock (_thumbnailLoadLock)
        {
            if (Thumbnail != null &&
                _thumbnailStage == ThumbnailLoadStage.Target &&
                _loadedTargetThumbnailSize == size)
            {
                return;
            }

            if (_targetThumbnailLoadCts != null &&
                !_targetThumbnailLoadCts.IsCancellationRequested &&
                _requestedTargetThumbnailSize == size)
            {
                return;
            }
        }

        CancellationTokenSource localCts;
        int localVersion;

        lock (_thumbnailLoadLock)
        {
            _targetThumbnailLoadCts?.Cancel();
            _targetThumbnailLoadCts = new CancellationTokenSource();
            localCts = _targetThumbnailLoadCts;
            localVersion = ++_targetThumbnailLoadVersion;
            _requestedTargetThumbnailSize = size;
            IsThumbnailLoading = true;
            IsThumbnailFailed = false;
        }

        var cancellationToken = localCts.Token;

        try
        {
            await _globalThumbnailLoadSemaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await RequestTargetThumbnailAsync(size, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                CommitThumbnailResult(localVersion, size, result, ThumbnailLoadStage.Target, cancellationToken);
            }
            finally
            {
                _globalThumbnailLoadSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage.Target, localVersion, size);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] EnsureThumbnailAsync failed for {ImageName}: {ex.Message}");
            ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage.Target, localVersion, size);
        }
    }

    private void CommitThumbnailResult(
        int loadVersion,
        ThumbnailSize size,
        ImageSource? result,
        ThumbnailLoadStage resultStage,
        CancellationToken cancellationToken)
    {
        var dispatcher = App.MainWindow.DispatcherQueue;
        if (dispatcher.HasThreadAccess)
        {
            CommitThumbnailResultOnUIThread(loadVersion, size, result, resultStage, cancellationToken);
            return;
        }

        if (!dispatcher.TryEnqueue(() =>
            CommitThumbnailResultOnUIThread(loadVersion, size, result, resultStage, cancellationToken)))
        {
            ClearThumbnailLoadingIfCurrent(resultStage, loadVersion, size);
        }
    }

    private void CommitThumbnailResultOnUIThread(
        int loadVersion,
        ThumbnailSize size,
        ImageSource? result,
        ThumbnailLoadStage resultStage,
        CancellationToken cancellationToken)
    {
        if (AppLifetime.IsShuttingDown)
            return;

        lock (_thumbnailLoadLock)
        {
            if (cancellationToken.IsCancellationRequested || !IsCurrentThumbnailRequest(resultStage, loadVersion, size))
                return;

            if (result != null && resultStage >= _thumbnailStage)
            {
                var previousStage = _thumbnailStage;
                if (resultStage == ThumbnailLoadStage.FastPreview)
                {
                    _fastPreviewThumbnail = result;
                }

                Thumbnail = result;
                _thumbnailStage = resultStage;
                if (resultStage == ThumbnailLoadStage.Target)
                {
                    _loadedTargetThumbnailSize = size;
                }

                if (previousStage != _thumbnailStage)
                {
                    OnPropertyChanged(nameof(HasFastPreview));
                    OnPropertyChanged(nameof(HasTargetThumbnail));
                }
            }

            if (resultStage == ThumbnailLoadStage.FastPreview)
            {
                _requestedFastPreviewSize = null;
            }

            if (resultStage == ThumbnailLoadStage.Target)
            {
                _requestedTargetThumbnailSize = null;
                IsThumbnailLoading = false;
                IsThumbnailFailed = result == null && Thumbnail == null;
            }
            else if (_requestedTargetThumbnailSize == null)
            {
                IsThumbnailLoading = false;
                IsThumbnailFailed = result == null && Thumbnail == null;
            }
        }
    }

    private bool IsCurrentThumbnailRequest(ThumbnailLoadStage stage, int loadVersion, ThumbnailSize size)
    {
        return stage switch
        {
            ThumbnailLoadStage.FastPreview => loadVersion == _fastPreviewLoadVersion && _requestedFastPreviewSize == size,
            ThumbnailLoadStage.Target => loadVersion == _targetThumbnailLoadVersion && _requestedTargetThumbnailSize == size,
            _ => false
        };
    }

    private void ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage stage, int loadVersion, ThumbnailSize size)
    {
        lock (_thumbnailLoadLock)
        {
            if (!IsCurrentThumbnailRequest(stage, loadVersion, size))
                return;

            if (stage == ThumbnailLoadStage.Target || _requestedTargetThumbnailSize == null)
            {
                IsThumbnailLoading = false;
            }
        }
    }

    public void CancelThumbnailLoad()
    {
        lock (_thumbnailLoadLock)
        {
            _fastPreviewLoadCts?.Cancel();
            _targetThumbnailLoadCts?.Cancel();
            _fastPreviewLoadVersion++;
            _targetThumbnailLoadVersion++;
            _requestedFastPreviewSize = null;
            _requestedTargetThumbnailSize = null;
            IsThumbnailLoading = false;
            IsThumbnailFailed = false;
        }
    }

    public void CancelTargetThumbnailLoad()
    {
        lock (_thumbnailLoadLock)
        {
            _targetThumbnailLoadCts?.Cancel();
            _targetThumbnailLoadVersion++;
            _requestedTargetThumbnailSize = null;
            IsThumbnailLoading = false;
            IsThumbnailFailed = false;
        }
    }

    public void ClearThumbnail()
    {
        lock (_thumbnailLoadLock)
        {
            _fastPreviewLoadCts?.Cancel();
            _targetThumbnailLoadCts?.Cancel();
            _fastPreviewLoadVersion++;
            _targetThumbnailLoadVersion++;
            Thumbnail = null;
            _fastPreviewThumbnail = null;
            _thumbnailStage = ThumbnailLoadStage.None;
            _loadedTargetThumbnailSize = null;
            _requestedFastPreviewSize = null;
            _requestedTargetThumbnailSize = null;
            IsThumbnailLoading = false;
            IsThumbnailFailed = false;
            OnPropertyChanged(nameof(HasFastPreview));
            OnPropertyChanged(nameof(HasTargetThumbnail));
        }
    }

    public void DowngradeToFastPreview()
    {
        lock (_thumbnailLoadLock)
        {
            _targetThumbnailLoadCts?.Cancel();
            _targetThumbnailLoadVersion++;
            _loadedTargetThumbnailSize = null;
            _requestedTargetThumbnailSize = null;

            if (_thumbnailStage == ThumbnailLoadStage.Target && _fastPreviewThumbnail != null)
            {
                Thumbnail = _fastPreviewThumbnail;
                _thumbnailStage = ThumbnailLoadStage.FastPreview;
                IsThumbnailLoading = false;
                IsThumbnailFailed = false;
                OnPropertyChanged(nameof(HasTargetThumbnail));
            }
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
            lock (_thumbnailLoadLock)
            {
                _ratingEditVersion++;
                SetRatingCore(value);
                IsRatingLoaded = true;
                IsRatingLoading = false;
                _isRatingLoadRequested = false;
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

    public bool IsRatingLoaded
    {
        get => _isRatingLoaded;
        private set => SetProperty(ref _isRatingLoaded, value);
    }

    public void SetRatingFromProperties(uint rating, RatingSource source)
    {
        lock (_thumbnailLoadLock)
        {
            if (_isRatingLoaded)
                return;

            _isRatingLoadRequested = true;
            SetRatingCore(rating);
            RatingSource = source;
            IsRatingLoaded = true;
            IsRatingLoading = false;
        }
    }

    public DateTime? DateTaken
    {
        get => _dateTaken;
        private set => SetProperty(ref _dateTaken, value);
    }

    public void SetDateTakenFromProperties(DateTime? dateTaken)
    {
        DateTaken = dateTaken;
    }

    public async Task EnsureRatingAsync(RatingService ratingService)
    {
        var editVersion = BeginRatingPreload();
        if (editVersion < 0)
            return;

        await LoadRatingAsync(ratingService, editVersion);
    }

    public async Task LoadRatingAsync(RatingService ratingService)
    {
        var editVersion = BeginRatingPreload();
        if (editVersion < 0)
            return;

        await LoadRatingAsync(ratingService, editVersion);
    }

    private async Task LoadRatingAsync(RatingService ratingService, int editVersion)
    {
        try
        {
            var (rating, source) = await ratingService.GetRatingAsync(ImageFile);
            ApplyLoadedRating(rating, source, editVersion);
        }
        catch
        {
            CancelRatingPreload(editVersion);
            throw;
        }
    }

    internal int BeginRatingPreload()
    {
        if (AppLifetime.IsShuttingDown || IsRatingLoaded)
            return -1;

        lock (_thumbnailLoadLock)
        {
            if (_isRatingLoadRequested || _isRatingLoaded)
                return -1;

            _isRatingLoadRequested = true;
            IsRatingLoading = true;
            return _ratingEditVersion;
        }
    }

    internal void ApplyLoadedRating(uint rating, RatingSource source, int expectedEditVersion)
    {
        lock (_thumbnailLoadLock)
        {
            if (_ratingEditVersion == expectedEditVersion)
            {
                SetRatingCore(rating);
                RatingSource = source;
                IsRatingLoaded = true;
            }

            IsRatingLoading = false;
            _isRatingLoadRequested = false;
        }
    }

    internal void CancelRatingPreload(int expectedEditVersion)
    {
        lock (_thumbnailLoadLock)
        {
            if (_ratingEditVersion != expectedEditVersion)
                return;

            IsRatingLoading = false;
            _isRatingLoadRequested = false;
        }
    }

    private void SetRatingCore(uint rating)
    {
        if (SetProperty(ref _rating, rating))
        {
            OnPropertyChanged(nameof(RatingValue));
        }
    }

    public async Task SetRatingAsync(RatingService ratingService, uint newRating)
    {
        var source = ratingService.IsWinRTRatingSupported(ImageFile.FileType)
            ? RatingSource.WinRT
            : RatingSource.Cache;

        lock (_thumbnailLoadLock)
        {
            _ratingEditVersion++;
            SetRatingCore(newRating);
            RatingSource = source;
            IsRatingLoaded = true;
            IsRatingLoading = false;
            _isRatingLoadRequested = false;
        }

        try
        {
            await ratingService.SetRatingAsync(ImageFile, newRating);
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

    public BurstPhotoGroup? BurstGroup => _burstGroup;

    public bool IsBurstPrimary => IsBurstCover;

    public bool IsBurstCover => (_isBurstPrimary || _isBurstDisplayCover) && BurstCount > 1;

    public bool IsBurstExpanded => _burstGroup?.IsExpanded == true;

    public int BurstCount => _burstGroup?.Images.Count ?? 0;

    public string BurstBadgeText => IsBurstExpanded ? $"收起 {BurstCount}" : $"连拍 {BurstCount}";

    public bool IsCollapsedBurstCover => IsBurstCover && !IsBurstExpanded;

    public bool CanEditGridRating => !IsCollapsedBurstCover;

    public bool IsBurstMemberVisualActive => BurstCount > 1 && IsBurstExpanded;

    public string BurstAccentColor => _burstGroup?.AccentColor ?? "#808080";

    public bool IsBurstChildVisible
    {
        get => _isBurstChildVisible;
        private set => SetProperty(ref _isBurstChildVisible, value);
    }

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

    public void ClearBurstInfo()
    {
        _burstGroup = null;
        _isBurstPrimary = false;
        _isBurstDisplayCover = false;
        IsBurstChildVisible = false;
        RefreshBurstProperties();
    }

    public void SetBurstInfo(BurstPhotoGroup group, bool isPrimary)
    {
        _burstGroup = group;
        _isBurstPrimary = isPrimary;
        _isBurstDisplayCover = false;
        IsBurstChildVisible = !isPrimary && group.IsExpanded;
        RefreshBurstProperties();
    }

    public void SetBurstDisplayCover(bool isDisplayCover)
    {
        if (SetProperty(ref _isBurstDisplayCover, isDisplayCover, nameof(IsBurstCover)))
        {
            RefreshBurstProperties();
        }
    }

    public void SetBurstChildVisible(bool isVisible)
    {
        IsBurstChildVisible = isVisible;
    }

    public void RefreshBurstProperties()
    {
        OnPropertyChanged(nameof(BurstGroup));
        OnPropertyChanged(nameof(IsBurstPrimary));
        OnPropertyChanged(nameof(IsBurstCover));
        OnPropertyChanged(nameof(IsBurstExpanded));
        OnPropertyChanged(nameof(BurstCount));
        OnPropertyChanged(nameof(BurstBadgeText));
        OnPropertyChanged(nameof(IsCollapsedBurstCover));
        OnPropertyChanged(nameof(CanEditGridRating));
        OnPropertyChanged(nameof(IsBurstMemberVisualActive));
        OnPropertyChanged(nameof(BurstAccentColor));
        OnPropertyChanged(nameof(IsBurstChildVisible));
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
        return ImageFormatRegistry.NormalizeExtension(ext);
    }

    private bool IsRawFormat(string ext)
    {
        return ImageFormatRegistry.IsRaw(ext);
    }

    private string GetFormatDisplayName(string ext)
    {
        return ImageFormatRegistry.GetFormatDisplayName(ext);
    }

    private string GetFormatColor(string ext)
    {
        return ImageFormatRegistry.GetFormatColor(ext);
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
                // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Skip PropertyChanged enqueue for {propertyName} on {ImageName}");
                return;
            }
        }
    }
}
