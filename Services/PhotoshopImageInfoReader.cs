using ImageMagick;
using PhotoView.Models;
using System.Threading;
using Windows.Storage;

namespace PhotoView.Services;

public static class PhotoshopImageInfoReader
{
    public static async Task<(int Width, int Height)?> TryReadSizeAsync(
        StorageFile file,
        CancellationToken cancellationToken)
    {
        if (!ImageFormatRegistry.IsPhotoshop(file.FileType) || string.IsNullOrWhiteSpace(file.Path))
            return null;

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new MagickImageInfo(file.Path);
                if (info.Width <= 0 || info.Height <= 0)
                    return ((int Width, int Height)?)null;

                return ((int Width, int Height)?)((
                    (int)Math.Min(int.MaxValue, info.Width),
                    (int)Math.Min(int.MaxValue, info.Height)));
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PhotoshopImageInfoReader] failed for {file.Name}: {ex.Message}");
            return null;
        }
    }
}
