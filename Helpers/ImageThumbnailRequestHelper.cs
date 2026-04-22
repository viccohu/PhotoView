using Microsoft.UI.Xaml.Media;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace PhotoView.Helpers;

internal static class ImageThumbnailRequestHelper
{
    private const uint FastPreviewLongSidePixels = 160;
    private static readonly uint[] SystemThumbnailSizes = { 96, 160, 256, 512, 1024 };

    public static async Task<ImageSource?> RequestFastPreviewAsync(StorageFile imageFile, CancellationToken cancellationToken)
    {
        var thumbnailService = App.GetService<IThumbnailService>();
        var result = await thumbnailService.GetFastPreviewAsync(
            imageFile,
            FastPreviewLongSidePixels,
            cancellationToken);
        return result?.ImageSource;
    }

    public static async Task<ImageSource?> RequestTargetThumbnailAsync(
        StorageFile imageFile,
        ThumbnailSize size,
        CancellationToken cancellationToken)
    {
        var thumbnailService = App.GetService<IThumbnailService>();
        var result = await thumbnailService.GetTargetThumbnailAsync(
            imageFile,
            GetOptimalThumbnailSize((uint)size),
            cancellationToken);
        return result?.ImageSource;
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
}
