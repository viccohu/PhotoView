namespace PhotoView.Models;

public class RatingCacheEntry
{
    public string FilePath { get; set; } = string.Empty;
    public uint Rating { get; set; }
    public DateTime LastModified { get; set; }
}
