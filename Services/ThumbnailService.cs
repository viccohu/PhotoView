using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Contracts.Services;
using PhotoView.Helpers;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PhotoView.Services;

public class ThumbnailService : IThumbnailService
{
    private const int MaxCachedThumbnails = 2000;
    private const uint MaxCachedThumbnailLongSidePixels = 4096;
    private const long MaxCachedThumbnailPixels = 160_000_000;
    private const uint FastPreviewFallbackLongSidePixels = 160;

    private readonly ISettingsService _settingsService;
    private readonly object _thumbnailCacheLock = new();
    private readonly Dictionary<ThumbnailCacheKey, LinkedListNode<ThumbnailCacheEntry>> _thumbnailCache = new();
    private readonly LinkedList<ThumbnailCacheEntry> _thumbnailCacheLru = new();
    private long _cachedThumbnailPixels;
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
            ? Math.Clamp(Environment.ProcessorCount / 2, 4, 8)
            : 4;
    }

    private void OnPerformanceModeChanged(object? sender, PerformanceMode mode)
    {
        var newCount = GetConcurrencyCount();
        _decodeGate = new SemaphoreSlim(newCount, newCount);
    }

    public async Task<DecodeResult?> GetFastPreviewAsync(
        StorageFile file,
        uint longSidePixels,
        CancellationToken cancellationToken)
    {
        var previewLongSidePixels = Math.Max(1u, Math.Min(longSidePixels, FastPreviewFallbackLongSidePixels));
        var key = IsCacheEligible(previewLongSidePixels)
            ? await TryCreateCacheKeyAsync(file, previewLongSidePixels, forceFullDecodeRaw: false, cancellationToken)
            : (ThumbnailCacheKey?)null;
        if (key.HasValue && TryGetCachedThumbnail(key.Value, out var cachedResult))
            return cachedResult;

        var result = await TryGetShellThumbnailAsync(
            file,
            previewLongSidePixels,
            ThumbnailOptions.ReturnOnlyIfCached | ThumbnailOptions.UseCurrentScale,
            cancellationToken);

        result ??= await TryGetEmbeddedPreviewAsync(file, previewLongSidePixels, cancellationToken);

        result ??= await TryGetShellThumbnailAsync(
            file,
            previewLongSidePixels,
            ThumbnailOptions.UseCurrentScale,
            cancellationToken);

        if (key.HasValue)
        {
            StoreCachedThumbnail(key.Value, result);
        }

        return result;
    }

    public async Task<DecodeResult?> GetTargetThumbnailAsync(
        StorageFile file,
        uint longSidePixels,
        CancellationToken cancellationToken)
    {
        var key = IsCacheEligible(longSidePixels)
            ? await TryCreateCacheKeyAsync(file, longSidePixels, forceFullDecodeRaw: false, cancellationToken)
            : (ThumbnailCacheKey?)null;
        if (key.HasValue && TryGetCachedThumbnail(key.Value, out var cachedResult))
            return cachedResult;

        var result = await TryGetShellThumbnailAsync(
            file,
            longSidePixels,
            ThumbnailOptions.UseCurrentScale,
            cancellationToken);

        if (result == null)
        {
            var gate = _decodeGate;
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (key.HasValue && TryGetCachedThumbnail(key.Value, out cachedResult))
                    return cachedResult;

                result = await DecodeThumbnailAsync(file, longSidePixels, forceFullDecodeRaw: false, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        if (key.HasValue)
        {
            StoreCachedThumbnail(key.Value, result);
        }

        return result;
    }

    public async Task WarmFastPreviewAsync(
        StorageFile file,
        uint longSidePixels,
        CancellationToken cancellationToken)
    {
        _ = await GetFastPreviewAsync(file, longSidePixels, cancellationToken);
    }

    public async Task<ImageSource?> GetThumbnailAsync(StorageFile file, ThumbnailSize size, CancellationToken cancellationToken)
    {
        var longSidePixels = (uint)size;
        var key = IsCacheEligible(longSidePixels)
            ? await TryCreateCacheKeyAsync(file, longSidePixels, forceFullDecodeRaw: false, cancellationToken)
            : (ThumbnailCacheKey?)null;
        if (key.HasValue && TryGetCachedThumbnail(key.Value, out var cachedResult))
            return cachedResult.ImageSource;

        var gate = _decodeGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (key.HasValue && TryGetCachedThumbnail(key.Value, out cachedResult))
                return cachedResult.ImageSource;

            var result = await DecodeThumbnailAsync(file, longSidePixels, forceFullDecodeRaw: false, cancellationToken);
            if (key.HasValue)
            {
                StoreCachedThumbnail(key.Value, result);
            }
            return result?.ImageSource;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ImageSource?> GetThumbnailByLongSideAsync(StorageFile file, uint longSidePixels, CancellationToken cancellationToken)
    {
        var key = IsCacheEligible(longSidePixels)
            ? await TryCreateCacheKeyAsync(file, longSidePixels, forceFullDecodeRaw: false, cancellationToken)
            : (ThumbnailCacheKey?)null;
        if (key.HasValue && TryGetCachedThumbnail(key.Value, out var cachedResult))
            return cachedResult.ImageSource;

        var gate = _decodeGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (key.HasValue && TryGetCachedThumbnail(key.Value, out cachedResult))
                return cachedResult.ImageSource;

            var result = await DecodeThumbnailAsync(file, longSidePixels, forceFullDecodeRaw: false, cancellationToken);
            if (key.HasValue)
            {
                StoreCachedThumbnail(key.Value, result);
            }
            return result?.ImageSource;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DecodeResult?> GetThumbnailWithSizeAsync(StorageFile file, uint longSidePixels, CancellationToken cancellationToken)
    {
        var key = IsCacheEligible(longSidePixels)
            ? await TryCreateCacheKeyAsync(file, longSidePixels, forceFullDecodeRaw: false, cancellationToken)
            : (ThumbnailCacheKey?)null;
        if (key.HasValue && TryGetCachedThumbnail(key.Value, out var cachedResult))
            return cachedResult;

        var gate = _decodeGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (key.HasValue && TryGetCachedThumbnail(key.Value, out cachedResult))
                return cachedResult;

            var result = await DecodeThumbnailAsync(file, longSidePixels, forceFullDecodeRaw: false, cancellationToken);
            if (key.HasValue)
            {
                StoreCachedThumbnail(key.Value, result);
            }
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DecodeResult?> GetThumbnailWithSizeAsync(StorageFile file, uint longSidePixels, bool forceFullDecode, CancellationToken cancellationToken)
    {
        var key = IsCacheEligible(longSidePixels)
            ? await TryCreateCacheKeyAsync(file, longSidePixels, forceFullDecode, cancellationToken)
            : (ThumbnailCacheKey?)null;
        if (key.HasValue && TryGetCachedThumbnail(key.Value, out var cachedResult))
            return cachedResult;

        var gate = _decodeGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (key.HasValue && TryGetCachedThumbnail(key.Value, out cachedResult))
                return cachedResult;

            var result = await DecodeThumbnailAsync(file, longSidePixels, forceFullDecode, cancellationToken);
            if (key.HasValue)
            {
                StoreCachedThumbnail(key.Value, result);
            }
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<DecodeResult?> TryGetCachedThumbnailAsync(
        StorageFile file,
        uint longSidePixels,
        bool forceFullDecode,
        CancellationToken cancellationToken)
    {
        if (!IsCacheEligible(longSidePixels))
            return null;

        var key = await TryCreateCacheKeyAsync(file, longSidePixels, forceFullDecode, cancellationToken);
        return key.HasValue && TryGetCachedThumbnail(key.Value, out var result) ? result : null;
    }

    public async Task StoreCachedThumbnailAsync(
        StorageFile file,
        uint longSidePixels,
        bool forceFullDecode,
        DecodeResult result,
        CancellationToken cancellationToken)
    {
        if (!IsCacheEligible(longSidePixels))
            return;

        var key = await TryCreateCacheKeyAsync(file, longSidePixels, forceFullDecode, cancellationToken);
        if (key.HasValue)
        {
            StoreCachedThumbnail(key.Value, result);
        }
    }

    public void Invalidate(StorageFile file)
    {
        var path = NormalizePath(GetFilePath(file));
        lock (_thumbnailCacheLock)
        {
            var keysToRemove = _thumbnailCache.Keys
                .Where(key => key.Path == path)
                .ToArray();

            foreach (var key in keysToRemove)
            {
                RemoveCacheEntry(key);
            }
        }
    }

    public void Clear()
    {
        lock (_thumbnailCacheLock)
        {
            _thumbnailCache.Clear();
            _thumbnailCacheLru.Clear();
            _cachedThumbnailPixels = 0;
        }
    }

    private static string GetFilePath(StorageFile file)
    {
        return string.IsNullOrWhiteSpace(file.Path) ? file.Name : file.Path;
    }

    private static string NormalizePath(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static async Task<ThumbnailCacheKey> CreateCacheKeyAsync(
        StorageFile file,
        uint longSidePixels,
        bool forceFullDecodeRaw,
        CancellationToken cancellationToken)
    {
        var properties = await StorageFilePropertyReader.GetBasicPropertiesAsync(file, cancellationToken);
        return new ThumbnailCacheKey(
            NormalizePath(GetFilePath(file)),
            file.FileType.ToUpperInvariant(),
            longSidePixels,
            forceFullDecodeRaw,
            properties.Size,
            properties.DateModified);
    }

    private static async Task<ThumbnailCacheKey?> TryCreateCacheKeyAsync(
        StorageFile file,
        uint longSidePixels,
        bool forceFullDecodeRaw,
        CancellationToken cancellationToken)
    {
        try
        {
            return await CreateCacheKeyAsync(file, longSidePixels, forceFullDecodeRaw, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThumbnailService] Skip cache key for {file.Name}: {ex.Message}");
            return null;
        }
    }

    private static bool IsCacheEligible(uint longSidePixels)
    {
        return longSidePixels <= MaxCachedThumbnailLongSidePixels;
    }

    private bool TryGetCachedThumbnail(ThumbnailCacheKey key, out DecodeResult result)
    {
        lock (_thumbnailCacheLock)
        {
            if (_thumbnailCache.TryGetValue(key, out var node))
            {
                _thumbnailCacheLru.Remove(node);
                _thumbnailCacheLru.AddLast(node);
                var entry = node.Value;
                result = new DecodeResult(entry.Width, entry.Height, entry.ImageSource);
                // System.Diagnostics.Debug.WriteLine($"[ThumbnailService] Cache hit: {key.Path}, size={key.LongSidePixels}");
                return true;
            }
        }

        result = null!;
        // System.Diagnostics.Debug.WriteLine($"[ThumbnailService] Cache miss: {key.Path}, size={key.LongSidePixels}");
        return false;
    }

    private void StoreCachedThumbnail(ThumbnailCacheKey key, DecodeResult? result)
    {
        if (result?.ImageSource == null)
            return;

        if (key.LongSidePixels > MaxCachedThumbnailLongSidePixels)
            return;

        lock (_thumbnailCacheLock)
        {
            RemoveCacheEntry(key);

            var pixelCount = EstimatePixelCount(key, result);
            var entry = new ThumbnailCacheEntry(key, result.Width, result.Height, pixelCount, result.ImageSource);
            var node = new LinkedListNode<ThumbnailCacheEntry>(entry);
            _thumbnailCacheLru.AddLast(node);
            _thumbnailCache[key] = node;
            _cachedThumbnailPixels += pixelCount;

            while (_thumbnailCache.Count > MaxCachedThumbnails ||
                   _cachedThumbnailPixels > MaxCachedThumbnailPixels)
            {
                var oldest = _thumbnailCacheLru.First;
                if (oldest == null)
                    break;

                // System.Diagnostics.Debug.WriteLine($"[ThumbnailService] Cache evict: {oldest.Value.Key.Path}, size={oldest.Value.Key.LongSidePixels}");
                _thumbnailCache.Remove(oldest.Value.Key);
                _cachedThumbnailPixels = Math.Max(0, _cachedThumbnailPixels - oldest.Value.PixelCount);
                _thumbnailCacheLru.RemoveFirst();
            }
        }
    }

    private void RemoveCacheEntry(ThumbnailCacheKey key)
    {
        if (!_thumbnailCache.TryGetValue(key, out var node))
            return;

        _thumbnailCacheLru.Remove(node);
        _thumbnailCache.Remove(key);
        _cachedThumbnailPixels = Math.Max(0, _cachedThumbnailPixels - node.Value.PixelCount);
    }

    private static long EstimatePixelCount(ThumbnailCacheKey key, DecodeResult result)
    {
        var width = result.Width > 0 ? result.Width : key.LongSidePixels;
        var height = result.Height > 0 ? result.Height : key.LongSidePixels;
        return Math.Max(1L, (long)width * height);
    }

    private readonly record struct ThumbnailCacheKey(
        string Path,
        string FileType,
        uint LongSidePixels,
        bool ForceFullDecodeRaw,
        ulong Size,
        DateTimeOffset DateModified);

    private sealed record ThumbnailCacheEntry(
        ThumbnailCacheKey Key,
        uint Width,
        uint Height,
        long PixelCount,
        ImageSource ImageSource);

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
            var thumbnail = await file.GetThumbnailAsync(
                Windows.Storage.FileProperties.ThumbnailMode.SingleItem,
                1920,
                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale).AsTask(cancellationToken);
            
            if (thumbnail == null || thumbnail.Size == 0)
                return null;

            if (thumbnail.Type != ThumbnailType.Image)
            {
                // System.Diagnostics.Debug.WriteLine($"[ThumbnailService] Ignore system thumbnail {thumbnail.Type} for {file.Name}, continue decode fallback");
                return null;
            }
            
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
                        if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
                        {
                            tcs.TrySetResult(null);
                            return;
                        }

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
                        System.Diagnostics.Debug.WriteLine($"[ThumbnailService] RAW preview canceled for {file.Name}");
                        tcs.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                }))
            {
                // System.Diagnostics.Debug.WriteLine($"[ThumbnailService] Skip RAW preview enqueue for {file.Name}");
                return null;
            }
            
            return await tcs.Task;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<DecodeResult?> TryGetShellThumbnailAsync(
        StorageFile file,
        uint targetLongSide,
        ThumbnailOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                targetLongSide,
                options).AsTask(cancellationToken);

            if (thumbnail == null || thumbnail.Size == 0)
                return null;

            using (thumbnail)
            {
                if (thumbnail.Type != ThumbnailType.Image)
                    return null;

                return await CreateBitmapImageResultAsync(thumbnail, targetLongSide, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThumbnailService] Shell thumbnail failed for {file.Name}: {ex.Message}");
            return null;
        }
    }

    private static async Task<DecodeResult?> TryGetEmbeddedPreviewAsync(
        StorageFile file,
        uint targetLongSide,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            using var previewStream = await decoder.GetPreviewAsync().AsTask(cancellationToken);
            if (previewStream == null || previewStream.Size == 0)
                return null;

            return await CreateBitmapImageResultAsync(previewStream, targetLongSide, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ThumbnailService] Embedded preview failed for {file.Name}: {ex.Message}");
            return null;
        }
    }

    private static async Task<DecodeResult?> CreateBitmapImageResultAsync(
        Windows.Storage.Streams.IRandomAccessStream stream,
        uint targetLongSide,
        CancellationToken cancellationToken)
    {
        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
        {
            return await CreateBitmapImageResultOnUIThreadAsync(stream, targetLongSide, cancellationToken);
        }

        var tcs = new TaskCompletionSource<DecodeResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
                    {
                        tcs.TrySetResult(null);
                        return;
                    }

                    var result = await CreateBitmapImageResultOnUIThreadAsync(stream, targetLongSide, cancellationToken);
                    tcs.TrySetResult(result);
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
            return null;
        }

        return await tcs.Task;
    }

    private static async Task<DecodeResult?> CreateBitmapImageResultOnUIThreadAsync(
        Windows.Storage.Streams.IRandomAccessStream stream,
        uint targetLongSide,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
            return null;

        try
        {
            stream.Seek(0);
        }
        catch
        {
        }

        var bitmap = new BitmapImage
        {
            DecodePixelWidth = (int)Math.Min(int.MaxValue, targetLongSide)
        };

        await bitmap.SetSourceAsync(stream).AsTask(cancellationToken);
        return new DecodeResult(
            (uint)Math.Max(0, bitmap.PixelWidth),
            (uint)Math.Max(0, bitmap.PixelHeight),
            bitmap);
    }

    private async Task<DecodeResult?> DecodeThumbnailAsync(
        StorageFile file,
        uint longSidePixels,
        bool forceFullDecodeRaw,
        CancellationToken cancellationToken)
    {
        var extension = file.FileType.ToLowerInvariant();
        
        if (IsRawFile(extension) && !forceFullDecodeRaw)
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

        var sourceLongSide = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        var decodeLongSidePixels = sourceLongSide > 0
            ? Math.Min(longSidePixels, sourceLongSide)
            : longSidePixels;

        uint scaledWidth, scaledHeight;
        if (decoder.PixelWidth >= decoder.PixelHeight)
        {
            scaledWidth = Math.Max(1u, decodeLongSidePixels);
            var aspectRatio = decoder.PixelWidth == 0 ? 1d : (double)decoder.PixelHeight / decoder.PixelWidth;
            scaledHeight = Math.Max(1u, (uint)Math.Round(scaledWidth * aspectRatio));
        }
        else
        {
            scaledHeight = Math.Max(1u, decodeLongSidePixels);
            var aspectRatio = decoder.PixelHeight == 0 ? 1d : (double)decoder.PixelWidth / decoder.PixelHeight;
            scaledWidth = Math.Max(1u, (uint)Math.Round(scaledHeight * aspectRatio));
        }

        var transform = new BitmapTransform
        {
            ScaledWidth = scaledWidth,
            ScaledHeight = scaledHeight,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
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
            if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
                return null;

            var bitmapSource = new SoftwareBitmapSource();
            await bitmapSource.SetBitmapAsync(softwareBitmap).AsTask(cancellationToken);
            return bitmapSource;
        }

        var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
                    {
                        tcs.TrySetResult(null);
                        return;
                    }

                    var bitmapSource = new SoftwareBitmapSource();
                    await bitmapSource.SetBitmapAsync(softwareBitmap).AsTask(cancellationToken);
                    tcs.TrySetResult(bitmapSource);
                }
                catch (OperationCanceledException ex)
                {
                    System.Diagnostics.Debug.WriteLine("[ThumbnailService] SoftwareBitmapSource creation canceled");
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            // System.Diagnostics.Debug.WriteLine("[ThumbnailService] Skip thumbnail creation enqueue");
            return null;
        }

        return await tcs.Task;
    }
}
