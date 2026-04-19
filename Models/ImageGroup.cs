using PhotoView.Models;

namespace PhotoView.Models;

public class ImageGroup
{
    public string GroupName { get; }
    public List<ImageFileInfo> Images { get; }
    public ImageFileInfo PrimaryImage { get; private set; }

    public ImageGroup(string groupName, IEnumerable<ImageFileInfo> images, bool preferPsdAsPrimaryPreview = false)
    {
        GroupName = groupName;
        Images = images.ToList();
        ReapplyPrimary(preferPsdAsPrimaryPreview);
    }

    public void ReapplyPrimary(bool preferPsdAsPrimaryPreview)
    {
        PrimaryImage = Images
            .OrderBy(img => GetFormatPriority(img.FileType, preferPsdAsPrimaryPreview))
            .First();

        foreach (var image in Images)
        {
            image.SetGroupInfo(this, image == PrimaryImage);
        }
    }

    public static int GetFormatPriority(string extension, bool preferPsdAsPrimaryPreview = false)
    {
        return ImageFormatRegistry.GetFormatPriority(extension, preferPsdAsPrimaryPreview);
    }

    public static string GetGroupName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return name ?? fileName;
    }
}
