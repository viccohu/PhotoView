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

    public async Task<DecodeResult?> GetThumbnailWithSizeAsync(StorageFile file, uint longSidePixels, bool forceFullDecode, CancellationToken cancellationToken)
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

    private static bool IsRawFile(string extension)
    {
        var rawExtensions = new[] {
            ".cr2", ".cr3", ".crw",
            ".nef", ".nrw",
            ".arw", ".srf", ".sr2",
            ".raf",
            ".orf",
            ".rw2",
            ".pef",
            ".dng",
            ".3fr", ".iiq", ".eip",
            ".srw",
            ".raw"
        };
        return rawExtensions.Contains(extension.ToLowerInvariant());
    }

    private static async Task<DecodeResult?> TryGetRawEmbeddedPreviewAsync(
        StorageFile file,
        uint targetLongSide,
        CancellationToken cancellationToken)
    {
        try
        {
            using var thumbnail = await file.GetThumbnailAsync(
                Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                1920,
                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale).AsTask(cancellationToken);
            
            if (thumbnail == null || thumbnail.Size == 0)
                return null;
            
            cancellationToken.ThrowIfCancellationRequested();
            
            var dispatcherQueue = App.MainWindow.DispatcherQueue;
            if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
            {
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(thumbnail);
                var previewLongSide = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
                
                if (previewLongSide < targetLongSide)
                    return null;
                
                return new DecodeResult((uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, bitmap);
            }
            
            var tcs = new TaskCompletionSource<DecodeResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(thumbnail);
                        var previewLongSide = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
                        
                        if (previewLongSide < targetLongSide)
                        {
                            tcs.TrySetResult(null);
                        }
                        else
                        {
                            tcs.TrySetResult(new DecodeResult((uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, bitmap));
                        }
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
                throw new OperationCanceledException("Failed to enqueue RAW preview creation.", cancellationToken);
            }
            
            return await tcs.Task;
        }
        catch
        {
            return null;
        }
    }

    private async Task<DecodeResult?> DecodeThumbnailAsync(StorageFile file, uint longSidePixels, CancellationToken cancellationToken)
    {
        var extension = file.FileType.ToLowerInvariant();
        
        if (IsRawFile(extension) && !_settingsService.AlwaysDecodeRaw)
        {
            var previewResult = await TryGetRawEmbeddedPreviewAsync(file, longSidePixels, cancellationToken);
            if (previewResult != null)
            {
                return previewResult;
            }
        }
        
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
