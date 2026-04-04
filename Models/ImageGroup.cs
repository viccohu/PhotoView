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
        { ".nef", 11 },
        { ".arw", 12 },
        { ".raf", 13 },
        { ".orf", 14 },
        { ".rw2", 15 },
        { ".pef", 16 },
        { ".srw", 17 },
        { ".raw", 18 }
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