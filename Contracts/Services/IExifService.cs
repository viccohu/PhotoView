using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using PhotoView.Models;
using PhotoView.Services;

namespace PhotoView.Contracts.Services;

public interface IExifService
{
    Task<ExifData> GetExifDataAsync(StorageFile file, CancellationToken cancellationToken = default);
    Task<(uint Rating, RatingSource Source)> GetRatingAsync(StorageFile file);
    Task SetRatingAsync(StorageFile file, uint rating);
    Task<ExifData> GetFullExifDataAsync(StorageFile file, CancellationToken cancellationToken = default);
}
