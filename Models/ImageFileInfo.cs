using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.Storage;

namespace PhotoView.Models;

public class ImageFileInfo : INotifyPropertyChanged
{
    private BitmapImage? _thumbnail;
    private int _loadVersion;
    private CancellationTokenSource? _thumbnailLoadCts;
    private bool _isSelected;
    private readonly object _thumbnailLoadLock = new();
    private ThumbnailSize? _loadedThumbnailSize;

    private static readonly uint[] SystemThumbnailSizes = { 96, 160, 256, 512, 1024 };
    private static readonly Random _random = new();

    public ImageFileInfo(ImageProperties properties,
        StorageFile imageFile,
        string name,
        string type)
    {
        ImageProperties = properties;
        ImageName = name;
        ImageFileType = type;
        ImageFile = imageFile;
        var rating = (int)properties.Rating;
        ImageRating = rating == 0 ? _random.Next(1, 5) : rating;
    }

    public int Width => (int)ImageProperties.Width;

    public int Height => (int)ImageProperties.Height;

    public StorageFile ImageFile { get; }

    public ImageProperties ImageProperties { get; }

    private async Task<BitmapImage> GetThumbnailAsync(ThumbnailSize size, CancellationToken cancellationToken)
    {
        try
        {
            var requestedSize = (uint)size;
            var optimalSize = GetOptimalThumbnailSize(requestedSize);

            StorageItemThumbnail thumbnail = await ImageFile.GetThumbnailAsync(
                ThumbnailMode.PicturesView,
                optimalSize,
                ThumbnailOptions.UseCurrentScale).AsTask(cancellationToken);

            if (thumbnail != null && thumbnail.Size > 0)
            {
                var bitmapImage = await CreateBitmapOnUIThreadAsync(thumbnail, cancellationToken);
                thumbnail.Dispose();
                return bitmapImage;
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

    private static async Task<BitmapImage> CreateBitmapOnUIThreadAsync(IRandomAccessStream stream, CancellationToken cancellationToken)
    {
        BitmapImage? bitmap = null;

        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        if (dispatcherQueue == null)
        {
            bitmap = new BitmapImage();
            bitmap.SetSource(stream);
            return bitmap;
        }

        var tcs = new TaskCompletionSource<bool>();

        dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());
            
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled();
                    return;
                }

                bitmap = new BitmapImage();
                bitmap.SetSource(stream);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

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

    public async Task EnsureThumbnailAsync(ThumbnailSize size)
    {
        lock (_thumbnailLoadLock)
        {
            if (Thumbnail != null && _loadedThumbnailSize == size)
                return;
        }

        CancellationTokenSource localCts;
        int localVersion;

        lock (_thumbnailLoadLock)
        {
            _thumbnailLoadCts?.Cancel();
            _thumbnailLoadCts = new CancellationTokenSource();
            localCts = _thumbnailLoadCts;
            localVersion = ++_loadVersion;
        }

        var cancellationToken = localCts.Token;

        try
        {
            var result = await GetThumbnailAsync(size, cancellationToken);

            lock (_thumbnailLoadLock)
            {
                if (localVersion != _loadVersion || cancellationToken.IsCancellationRequested)
                    return;

                Thumbnail = result;
                _loadedThumbnailSize = size;
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
        }
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(propertyName);
        }
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

    public string ImageDimensions => $"{ImageProperties.Width} x {ImageProperties.Height}";

    public string ImageTitle
    {
        get => string.IsNullOrEmpty(ImageProperties.Title) ? ImageName : ImageProperties.Title;
        set
        {
            if (ImageProperties.Title != value)
            {
                ImageProperties.Title = value;
                _ = ImageProperties.SavePropertiesAsync();
                OnPropertyChanged();
            }
        }
    }

    public int ImageRating
    {
        get => (int)ImageProperties.Rating;
        set
        {
            if (ImageProperties.Rating != value)
            {
                ImageProperties.Rating = (uint)value;
                _ = ImageProperties.SavePropertiesAsync();
                OnPropertyChanged();
            }
        }
    }

    public int AutoWidth => Height == 0 ? 200 : (int)(ImageProperties.Width * 200 / Height);

    public double AspectRatio => Height == 0 ? 1.0 : (double)Width / Height;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
