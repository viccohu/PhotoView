using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PhotoView.Helpers;

internal static class StorageFilePropertyReader
{
    private static readonly object GateLock = new();
    private static readonly Dictionary<string, FileGate> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<BasicProperties> GetBasicPropertiesAsync(
        StorageFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        var key = GetFileKey(file);
        var gate = AcquireGate(key);
        var entered = false;

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            entered = true;

            var properties = await file.GetBasicPropertiesAsync().AsTask();
            cancellationToken.ThrowIfCancellationRequested();
            return properties;
        }
        finally
        {
            if (entered)
            {
                gate.Semaphore.Release();
            }

            ReleaseGate(key, gate);
        }
    }

    public static async Task<ImageProperties> GetImagePropertiesAsync(
        StorageFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        cancellationToken.ThrowIfCancellationRequested();

        var key = GetFileKey(file);
        var gate = AcquireGate(key);
        var entered = false;

        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken);
            entered = true;

            var properties = await file.Properties.GetImagePropertiesAsync().AsTask();
            cancellationToken.ThrowIfCancellationRequested();
            return properties;
        }
        finally
        {
            if (entered)
            {
                gate.Semaphore.Release();
            }

            ReleaseGate(key, gate);
        }
    }

    private static string GetFileKey(StorageFile file)
    {
        return string.IsNullOrWhiteSpace(file.Path) ? file.Name : file.Path;
    }

    private static FileGate AcquireGate(string key)
    {
        lock (GateLock)
        {
            if (!Gates.TryGetValue(key, out var gate))
            {
                gate = new FileGate();
                Gates[key] = gate;
            }

            gate.ReferenceCount++;
            return gate;
        }
    }

    private static void ReleaseGate(string key, FileGate gate)
    {
        lock (GateLock)
        {
            gate.ReferenceCount--;
            if (gate.ReferenceCount == 0)
            {
                Gates.Remove(key);
            }
        }
    }

    private sealed class FileGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }
}
