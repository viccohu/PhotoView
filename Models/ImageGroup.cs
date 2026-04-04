using PhotoView.Models;

namespace PhotoView.Models;

public class ImageGroup
{
    private static readonly Dictionary<string, int> FormatPriority = new()
    {
        { ".jpg", 1 },
        { ".jpeg", 2 },
        { ".png", 3 },
        { ".webp", 4 },
        { ".tiff", 5 },
        { ".bmp", 6 },
        { ".gif", 7 },
        { ".dng", 8 },
        { ".cr2", 9 },
        { ".cr3", 10 },
        { ".crw", 11 },
        { ".nef", 12 },
        { ".nrw", 13 },
        { ".arw", 14 },
        { ".sr2", 15 },
        { ".raf", 16 },
        { ".orf", 17 },
        { ".rw2", 18 },
        { ".pef", 19 },
        { ".srw", 20 },
        { ".raw", 21 },
        { ".iiq", 22 },
        { ".3fr", 23 },
        { ".mef", 24 },
        { ".mos", 25 },
        { ".x3f", 26 },
        { ".erf", 27 },
        { ".dcr", 28 },
        { ".kdc", 29 }
    };

    public string GroupName { get; }
    public List<ImageFileInfo> Images { get; }
    public ImageFileInfo PrimaryImage { get; }

    public ImageGroup(string groupName, IEnumerable<ImageFileInfo> images)
    {
        GroupName = groupName;
        Images = images.ToList();
        PrimaryImage = SelectPrimaryImage();
        
        foreach (var image in Images)
        {
            image.SetGroupInfo(this, image == PrimaryImage);
        }
    }

    private ImageFileInfo SelectPrimaryImage()
    {
        return Images
            .OrderBy(img => GetFormatPriority(img.FileType))
            .First();
    }

    private static int GetFormatPriority(string extension)
    {
        var ext = extension.ToLowerInvariant();
        if (!ext.StartsWith("."))
        {
            ext = "." + ext;
        }
        return FormatPriority.TryGetValue(ext, out var priority) ? priority : int.MaxValue;
    }

    public static string GetGroupName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return name ?? fileName;
    }
}