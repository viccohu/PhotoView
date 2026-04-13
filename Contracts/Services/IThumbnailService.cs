using Microsoft.UI.Xaml.Media;
using PhotoView.Models;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;

namespace PhotoView.Contracts.Services;

public interface IThumbnailService
{
    Task<ImageSource?> GetThumbnailAsync(StorageFile file, ThumbnailSize size, CancellationToken cancellationToken);
    Task<ImageSource?> GetThumbnailByLongSideAsync(StorageFile file, uint longSidePixels, CancellationToken cancellationToken);
    Task<DecodeResult?> GetThumbnailWithSizeAsync(StorageFile file, uint longSidePixels, CancellationToken cancellationToken);
    Task<DecodeResult?> GetThumbnailWithSizeAsync(StorageFile file, uint longSidePixels, bool forceFullDecode, CancellationToken cancellationToken);

    // Cache hooks are intentionally no-op for now; size changes reload visible items.
    void Invalidate(StorageFile file);

    // Cache hooks are intentionally no-op for now; size changes reload visible items.
    void Clear();
}
