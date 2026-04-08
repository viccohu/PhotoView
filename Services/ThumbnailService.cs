using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace PhotoView.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly ISettingsService _settingsService;
    private SemaphoreSlim _decodeGate;

    public ThumbnailService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        _settingsService.PerformanceModeChanged += OnPerformanceModeChanged;
        var concurrencyCount = GetConcurrencyCount();
        _decodeGate = new SemaphoreSlim(concurrencyCount, concurrencyCount);
        System.Diagnostics.Debug.WriteLine($"[ThumbnailService] 初始化, 并发数={concurrencyCount}, PerformanceMode={_settingsService.PerformanceMode}");
    }

    private int GetConcurrencyCount()
    {
        return _settingsService.PerformanceMode == PerformanceMode.Smart
            ? Math.Max(4, Environment.ProcessorCount / 2)
            : 4;
    }

    private void OnPerformanceModeChanged(object? sender, PerformanceMode mode)
    {
        var newCount = GetConcurrencyCount();
        _decodeGate = new SemaphoreSlim(newCount, newCount);
        System.Diagnostics.Debug.WriteLine($"[ThumbnailService] PerformanceMode 变更, 新并发数={newCount}, mode={mode}");
    }

    public async Task<ImageSource?> GetThumbnailAsync(StorageFile file, ThumbnailSize size, CancellationToken cancellationToken)
    {
        await _decodeGate.WaitAsync(cancellationToken);
        try
        {
            var result = await DecodeThumbnailAsync(file, (uint)size, cancellationToken);
            return result?.ImageSource;
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    public async Task<ImageSource?> GetThumbnailByLongSideAsync(StorageFile file, uint longSidePixels, CancellationToken cancellationToken)
    {
        await _decodeGate.WaitAsync(cancellationToken);
        try
        {
            var result = await DecodeThumbnailAsync(file, longSidePixels, cancellationToken);
            return result?.ImageSource;
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    public async Task<DecodeResult?> GetThumbnailWithSizeAsync(StorageFile file, uint longSidePixels, CancellationToken cancellationToken)
    {
        await _decodeGate.WaitAsync(cancellationToken);
        try
        {
            return await DecodeThumbnailAsync(file, longSidePixels, cancellationToken);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    public void Invalidate(StorageFile file)
    {
    }

    public void Clear()
    {
    }

    private static async Task<DecodeResult?> DecodeThumbnailAsync(StorageFile file, uint longSidePixels, CancellationToken cancellationToken)
    {
        using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        uint scaledWidth, scaledHeight;
        if (decoder.PixelWidth >= decoder.PixelHeight)
        {
            scaledWidth = Math.Max(1u, longSidePixels);
            var aspectRatio = decoder.PixelWidth == 0 ? 1d : (double)decoder.PixelHeight / decoder.PixelWidth;
            scaledHeight = Math.Max(1u, (uint)Math.Round(scaledWidth * aspectRatio));
        }
        else
        {
            scaledHeight = Math.Max(1u, longSidePixels);
            var aspectRatio = decoder.PixelHeight == 0 ? 1d : (double)decoder.PixelWidth / decoder.PixelHeight;
            scaledWidth = Math.Max(1u, (uint)Math.Round(scaledHeight * aspectRatio));
        }

        System.Diagnostics.Debug.WriteLine($"[ThumbnailService] DecodeThumbnailAsync: 文件={file.Name}, 解码尺寸={scaledWidth}x{scaledHeight}");

        var transform = new BitmapTransform
        {
            ScaledWidth = scaledWidth,
            ScaledHeight = scaledHeight,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var imageSource = await CreateSoftwareBitmapSourceAsync(softwareBitmap, cancellationToken);

        return imageSource != null ? new DecodeResult(scaledWidth, scaledHeight, imageSource) : null;
    }

    private static async Task<ImageSource?> CreateSoftwareBitmapSourceAsync(SoftwareBitmap softwareBitmap, CancellationToken cancellationToken)
    {
        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
        {
            var bitmapSource = new SoftwareBitmapSource();
            await bitmapSource.SetBitmapAsync(softwareBitmap).AsTask(cancellationToken);
            return bitmapSource;
        }

        var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    var bitmapSource = new SoftwareBitmapSource();
                    await bitmapSource.SetBitmapAsync(softwareBitmap).AsTask(cancellationToken);
                    tcs.TrySetResult(bitmapSource);
                }
                catch (OperationCanceledException ex)
                {
                    tcs.TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            throw new OperationCanceledException("Failed to enqueue thumbnail creation.", cancellationToken);
        }

        return await tcs.Task;
    }
}
