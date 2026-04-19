namespace PhotoView.Models;

public sealed class BurstPhotoGroup
{
    private static readonly string[] AccentPalette =
    {
        "#2E7D32",
        "#0078D4",
        "#C239B3",
        "#D83B01",
        "#8764B8",
        "#008575",
        "#E3008C",
        "#498205"
    };

    private bool _isExpanded;

    public BurstPhotoGroup(string groupKey, IEnumerable<ImageFileInfo> images)
    {
        GroupKey = groupKey;
        Images = images.ToList();
        PrimaryImage = Images.First();
        CoverImage = SelectCover(Images);
        AccentColor = SelectAccentColor(groupKey);

        foreach (var image in Images)
        {
            image.SetBurstInfo(this, ReferenceEquals(image, CoverImage));
        }
    }

    public string GroupKey { get; }

    public List<ImageFileInfo> Images { get; }

    public ImageFileInfo PrimaryImage { get; }

    public ImageFileInfo CoverImage { get; private set; }

    public string AccentColor { get; }

    public bool IsExpanded => _isExpanded;

    public ImageFileInfo GetCoverImage(IEnumerable<ImageFileInfo>? visibleMembers = null)
    {
        var candidates = visibleMembers?.ToList();
        if (candidates is { Count: > 0 })
        {
            return SelectCover(candidates);
        }

        return CoverImage;
    }

    public bool RecalculateCover()
    {
        var newCover = SelectCover(Images);
        if (ReferenceEquals(newCover, CoverImage))
        {
            foreach (var image in Images)
            {
                image.RefreshBurstProperties();
            }
            return false;
        }

        CoverImage = newCover;
        foreach (var image in Images)
        {
            image.SetBurstInfo(this, ReferenceEquals(image, CoverImage));
        }

        return true;
    }

    public void SetExpanded(bool isExpanded)
    {
        if (_isExpanded == isExpanded)
            return;

        _isExpanded = isExpanded;
        foreach (var image in Images)
        {
            image.RefreshBurstProperties();
        }
    }

    private ImageFileInfo SelectCover(IReadOnlyCollection<ImageFileInfo> candidates)
    {
        return candidates
            .OrderByDescending(image => ImageFileInfo.RatingToStars(image.Rating))
            .ThenBy(image => Images.IndexOf(image) < 0 ? int.MaxValue : Images.IndexOf(image))
            .First();
    }

    private static string SelectAccentColor(string groupKey)
    {
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(groupKey ?? string.Empty);
        var index = (hash & int.MaxValue) % AccentPalette.Length;
        return AccentPalette[index];
    }
}
