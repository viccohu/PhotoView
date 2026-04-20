using PhotoView.Models;

namespace PhotoView.Helpers;

internal static class ImageMetadataDateHelper
{
    private static readonly DateTime EarliestValidDateTaken = new(1900, 1, 1);

    public static DateTime? NormalizeDateTaken(DateTime? dateTaken, string? extension)
    {
        if (!dateTaken.HasValue || ImageFormatRegistry.IsPhotoshop(extension))
            return null;

        return IsPlausibleDateTaken(dateTaken.Value) ? dateTaken.Value : null;
    }

    public static DateTime? NormalizeDateTaken(DateTime dateTaken, string? extension)
    {
        if (ImageFormatRegistry.IsPhotoshop(extension))
            return null;

        return IsPlausibleDateTaken(dateTaken) ? dateTaken : null;
    }

    public static DateTime? NormalizeDateTaken(DateTimeOffset dateTaken, string? extension)
    {
        if (dateTaken == default ||
            dateTaken == DateTimeOffset.MinValue ||
            ImageFormatRegistry.IsPhotoshop(extension))
        {
            return null;
        }

        var localDateTaken = dateTaken.LocalDateTime;
        return IsPlausibleDateTaken(localDateTaken) ? localDateTaken : null;
    }

    private static bool IsPlausibleDateTaken(DateTime dateTaken)
    {
        return dateTaken >= EarliestValidDateTaken;
    }
}
