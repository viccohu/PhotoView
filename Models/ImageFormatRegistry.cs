namespace PhotoView.Models;

public static class ImageFormatRegistry
{
    private static readonly HashSet<string> CommonImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp"
    };

    private static readonly HashSet<string> PhotoshopExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".psd", ".psb"
    };

    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2",
        ".raf", ".orf", ".rw2", ".pef", ".dng", ".srw", ".raw", ".iiq",
        ".3fr", ".fff", ".mef", ".mos", ".x3f", ".erf", ".dcr", ".kdc",
        ".mrw", ".rwl", ".eip"
    };

    private static readonly Dictionary<string, int> CommonImagePriority = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = 10,
        [".jpeg"] = 11,
        [".png"] = 12,
        [".webp"] = 13,
        [".tiff"] = 14,
        [".tif"] = 15,
        [".bmp"] = 16,
        [".gif"] = 17
    };

    private static readonly Dictionary<string, int> RawPriority = new(StringComparer.OrdinalIgnoreCase)
    {
        [".dng"] = 100,
        [".cr2"] = 101,
        [".cr3"] = 102,
        [".crw"] = 103,
        [".nef"] = 104,
        [".nrw"] = 105,
        [".arw"] = 106,
        [".srf"] = 107,
        [".sr2"] = 108,
        [".raf"] = 109,
        [".orf"] = 110,
        [".rw2"] = 111,
        [".pef"] = 112,
        [".srw"] = 113,
        [".raw"] = 114,
        [".iiq"] = 115,
        [".3fr"] = 116,
        [".fff"] = 117,
        [".mef"] = 118,
        [".mos"] = 119,
        [".x3f"] = 120,
        [".erf"] = 121,
        [".dcr"] = 122,
        [".kdc"] = 123,
        [".mrw"] = 124,
        [".rwl"] = 125,
        [".eip"] = 126
    };

    public static IReadOnlyCollection<string> SupportedExtensions =>
        CommonImageExtensions
            .Concat(PhotoshopExtensions)
            .Concat(RawExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsSupported(string? extension)
    {
        var ext = NormalizeExtension(extension);
        return CommonImageExtensions.Contains(ext) || PhotoshopExtensions.Contains(ext) || RawExtensions.Contains(ext);
    }

    public static bool IsRaw(string? extension)
    {
        return RawExtensions.Contains(NormalizeExtension(extension));
    }

    public static bool IsPhotoshop(string? extension)
    {
        return PhotoshopExtensions.Contains(NormalizeExtension(extension));
    }

    public static int GetFormatPriority(string? extension, bool preferPsdAsPrimaryPreview)
    {
        var ext = NormalizeExtension(extension);

        if (preferPsdAsPrimaryPreview && PhotoshopExtensions.Contains(ext))
            return ext.Equals(".psd", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

        if (CommonImagePriority.TryGetValue(ext, out var commonPriority))
            return commonPriority;

        if (PhotoshopExtensions.Contains(ext))
            return ext.Equals(".psd", StringComparison.OrdinalIgnoreCase) ? 50 : 51;

        if (RawPriority.TryGetValue(ext, out var rawPriority))
            return rawPriority;

        return int.MaxValue;
    }

    public static string GetFormatDisplayName(string? extension)
    {
        var ext = NormalizeExtension(extension);
        return ext switch
        {
            ".jpg" or ".jpeg" => "JPG",
            ".png" => "PNG",
            ".gif" => "GIF",
            ".bmp" => "BMP",
            ".tiff" or ".tif" => "TIF",
            ".webp" => "WebP",
            ".psd" => "PSD",
            ".psb" => "PSB",
            _ when IsRaw(ext) => "RAW",
            _ => ext.ToUpperInvariant().TrimStart('.')
        };
    }

    public static string GetFormatColor(string? extension)
    {
        var ext = NormalizeExtension(extension);

        if (CommonImageExtensions.Contains(ext))
            return "#00c8ff";
        if (RawExtensions.Contains(ext))
            return "#ffb300";
        if (PhotoshopExtensions.Contains(ext))
            return "#31d2f7";

        return "#808080";
    }

    public static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        var ext = extension.Trim().ToLowerInvariant();
        return ext.StartsWith('.') ? ext : $".{ext}";
    }
}
