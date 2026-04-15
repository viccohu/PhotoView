namespace PhotoView.Models;

public class FolderAccessHistory
{
    public List<string> PinnedPaths { get; set; } = new();

    public List<string> RecentPaths { get; set; } = new();
}
