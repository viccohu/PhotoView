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
    private readonly SemaphoreSlim _decodeGate = new(4, 4);

    public async Task<ImageSource?> GetThumbnailAsync(StorageFile file, ThumbnailSize size, CancellationToken cancellationToken)
    {
        await _decodeGate.WaitAsync(cancellationToken);
        try
        {
            return await DecodeThumbnailAsync(file, size, cancellationToken);
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

    private static async Task<ImageSource?> DecodeThumbnailAsync(StorageFile file, ThumbnailSize size, CancellationToken cancellationToken)
    {
        using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var scaledHeight = Math.Max(1u, (uint)size);
        var aspectRatio = decoder.PixelHeight == 0 ? 1d : (double)decoder.PixelWidth / decoder.PixelHeight;
        var scaledWidth = Math.Max(1u, (uint)Math.Round(scaledHeight * aspectRatio));
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
        return await CreateSoftwareBitmapSourceAsync(softwareBitmap, cancellationToken);
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
