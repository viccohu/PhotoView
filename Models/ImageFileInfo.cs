using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Helpers;
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
    private readonly object _thumbnailLoadLock = new();
    private ThumbnailSize? _loadedThumbnailSize;

    private static readonly uint[] SystemThumbnailSizes = { 96, 160, 256, 512, 1024 };
    private static readonly Random _random = new();

    public ImageFileInfo(
        int width,
        int height,
        string title,
        int rating,
        StorageFile imageFile,
        string name,
        string type)
    {
        Width = width;
        Height = height;
        ImageTitle = title;
        ImageRating = rating == 0 ? _random.Next(1, 5) : rating;
        ImageFile = imageFile ?? throw new ArgumentNullException(nameof(imageFile));
        ImageName = name;
        ImageFileType = type;
        _isSelected = false;
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
                ThumbnailOptions.UseCurrentScale).AsTask(cancellationToken);

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

        dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
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
        if (AppLifetime.IsShuttingDown)
            return;

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
                    dispatcher.TryEnqueue(() =>
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
                    });
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

    public string ImageDimensions => $"{Width} x {Height}";

    public string ImageTitle { get; }

    public int ImageRating { get; }

    public int AutoWidth => Height == 0 ? 200 : (int)((Width * 200.0) / Height);

    public double AspectRatio => Height == 0 ? 1.0 : (double)Width / Height;

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
            dispatcherQueue.TryEnqueue(() =>
            {
                if (AppLifetime.IsShuttingDown)
                    return;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }
    }
}
