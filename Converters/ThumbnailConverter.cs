using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Models;
using System;
using Windows.Storage;

namespace PhotoView.Converters;

public class ThumbnailConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is StorageFile file)
        {
            var size = ThumbnailSize.Medium;
            if (parameter is string sizeString && Enum.TryParse<ThumbnailSize>(sizeString, out var parsedSize))
            {
                size = parsedSize;
            }

            var bitmapImage = new BitmapImage();
            _ = LoadThumbnailAsync(file, bitmapImage, size);
            return bitmapImage;
        }

        return null!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }

    private static async System.Threading.Tasks.Task LoadThumbnailAsync(StorageFile file, BitmapImage bitmapImage, ThumbnailSize size)
    {
        try
        {
            var requestedSize = (uint)size;
            var thumbnail = await file.GetThumbnailAsync(
                Windows.Storage.FileProperties.ThumbnailMode.PicturesView,
                requestedSize,
                Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

            if (thumbnail != null && thumbnail.Size > 0)
            {
                bitmapImage.SetSource(thumbnail);
                thumbnail.Dispose();
            }
        }
        catch
        {
        }
    }
}
