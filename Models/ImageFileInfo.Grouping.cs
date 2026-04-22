using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace PhotoView.Models;

public partial class ImageFileInfo
{
    private ImageGroup? _group;
    private bool _isPrimary;
    private BurstPhotoGroup? _burstGroup;
    private bool _isBurstPrimary;
    private bool _isBurstDisplayCover;
    private bool _isBurstChildVisible;
    private ObservableCollection<FormatTag> _formatTags = new();

    public ImageGroup? Group => _group;

    public bool IsPrimary => _isPrimary;

    public BurstPhotoGroup? BurstGroup => _burstGroup;

    public bool IsBurstPrimary => IsBurstCover;

    public bool IsBurstCover => (_isBurstPrimary || _isBurstDisplayCover) && BurstCount > 1;

    public bool IsBurstExpanded => _burstGroup?.IsExpanded == true;

    public int BurstCount => _burstGroup?.Images.Count ?? 0;

    public bool IsBurstMember => BurstCount > 1;

    public string BurstOrdinalText
    {
        get
        {
            if (_burstGroup == null || _burstGroup.Images.Count < 2)
            {
                return string.Empty;
            }

            var index = _burstGroup.Images.IndexOf(this);
            return index < 0 ? string.Empty : $"{index + 1}/{_burstGroup.Images.Count}";
        }
    }

    public string BurstBadgeText => IsBurstExpanded ? "收起 " + BurstCount : "连拍 " + BurstCount;

    public bool IsCollapsedBurstCover => IsBurstCover && !IsBurstExpanded && !IsBurstChildVisible;

    public bool CanEditGridRating => !IsCollapsedBurstCover;

    public bool IsBurstMemberVisualActive => BurstCount > 1 && (IsBurstExpanded || IsBurstChildVisible);

    public bool IsBurstFoldButtonVisible => IsBurstCover && !IsBurstChildVisible;

    public bool IsBurstInlineBadgeVisible => IsBurstMember && IsBurstChildVisible && !IsBurstExpanded;

    public string BurstAccentColor => _burstGroup?.AccentColor ?? "#808080";

    public bool IsBurstChildVisible
    {
        get => _isBurstChildVisible;
        private set
        {
            if (SetProperty(ref _isBurstChildVisible, value))
            {
                OnPropertyChanged(nameof(IsCollapsedBurstCover));
                OnPropertyChanged(nameof(CanEditGridRating));
                OnPropertyChanged(nameof(IsBurstMemberVisualActive));
                OnPropertyChanged(nameof(IsBurstFoldButtonVisible));
                OnPropertyChanged(nameof(IsBurstInlineBadgeVisible));
            }
        }
    }

    public List<ImageFileInfo>? AlternateFormats => _group?.Images.Where(i => i != this).ToList();

    public bool HasAlternateFormats => AlternateFormats != null && AlternateFormats.Count > 0;

    public string AlternateFormatsText
    {
        get
        {
            if (!HasAlternateFormats || AlternateFormats == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Current: {FileType.ToUpperInvariant()}");
            sb.AppendLine();
            sb.AppendLine("Other formats:");
            foreach (var format in AlternateFormats)
            {
                sb.AppendLine($"- {format.FileType.ToUpperInvariant()}");
            }

            return sb.ToString().TrimEnd();
        }
    }

    public void SetGroupInfo(ImageGroup group, bool isPrimary)
    {
        _group = group;
        _isPrimary = isPrimary;
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(IsPrimary));
        OnPropertyChanged(nameof(AlternateFormats));
        OnPropertyChanged(nameof(HasAlternateFormats));
        OnPropertyChanged(nameof(AlternateFormatsText));
        UpdateFormatTags();
    }

    public void ClearBurstInfo()
    {
        _burstGroup = null;
        _isBurstPrimary = false;
        _isBurstDisplayCover = false;
        IsBurstChildVisible = false;
        RefreshBurstProperties();
    }

    public void SetBurstInfo(BurstPhotoGroup group, bool isPrimary)
    {
        _burstGroup = group;
        _isBurstPrimary = isPrimary;
        _isBurstDisplayCover = false;
        IsBurstChildVisible = !isPrimary && group.IsExpanded;
        RefreshBurstProperties();
    }

    public void SetBurstDisplayCover(bool isDisplayCover)
    {
        if (SetProperty(ref _isBurstDisplayCover, isDisplayCover, nameof(IsBurstCover)))
        {
            RefreshBurstProperties();
        }
    }

    public void SetBurstChildVisible(bool isVisible)
    {
        IsBurstChildVisible = isVisible;
    }

    public void RefreshBurstProperties()
    {
        OnPropertyChanged(nameof(BurstGroup));
        OnPropertyChanged(nameof(IsBurstPrimary));
        OnPropertyChanged(nameof(IsBurstCover));
        OnPropertyChanged(nameof(IsBurstExpanded));
        OnPropertyChanged(nameof(BurstCount));
        OnPropertyChanged(nameof(BurstBadgeText));
        OnPropertyChanged(nameof(IsBurstMember));
        OnPropertyChanged(nameof(BurstOrdinalText));
        OnPropertyChanged(nameof(IsCollapsedBurstCover));
        OnPropertyChanged(nameof(CanEditGridRating));
        OnPropertyChanged(nameof(IsBurstMemberVisualActive));
        OnPropertyChanged(nameof(IsBurstFoldButtonVisible));
        OnPropertyChanged(nameof(IsBurstInlineBadgeVisible));
        OnPropertyChanged(nameof(BurstAccentColor));
        OnPropertyChanged(nameof(IsBurstChildVisible));
    }

    public void RefreshGroupProperties()
    {
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(IsPrimary));
        OnPropertyChanged(nameof(AlternateFormats));
        OnPropertyChanged(nameof(HasAlternateFormats));
        OnPropertyChanged(nameof(AlternateFormatsText));
        OnPropertyChanged(nameof(IsPendingDelete));
    }

    public ObservableCollection<FormatTag> FormatTags
    {
        get => _formatTags;
        private set => SetProperty(ref _formatTags, value);
    }

    public void UpdateFormatTags()
    {
        FormatTags.Clear();

        if (Group == null)
        {
            var ext = NormalizeFormatExtension(ImageFileType);
            FormatTags.Add(new FormatTag
            {
                Format = GetFormatDisplayName(ext),
                Color = GetFormatColor(ext),
                IsLast = true
            });
            return;
        }

        var sortedFormats = Group.Images
            .Select(image => NormalizeFormatExtension(image.ImageFileType))
            .Distinct()
            .OrderByDescending(ImageFormatRegistry.IsRaw)
            .ThenBy(ext => ext)
            .ToList();

        for (var i = 0; i < sortedFormats.Count; i++)
        {
            var ext = sortedFormats[i];
            FormatTags.Add(new FormatTag
            {
                Format = GetFormatDisplayName(ext),
                Color = GetFormatColor(ext),
                IsLast = i == sortedFormats.Count - 1
            });
        }
    }

    private static string NormalizeFormatExtension(string ext)
    {
        return ImageFormatRegistry.NormalizeExtension(ext);
    }

    private string GetFormatDisplayName(string ext)
    {
        return ImageFormatRegistry.GetFormatDisplayName(ext);
    }

    private string GetFormatColor(string ext)
    {
        return ImageFormatRegistry.GetFormatColor(ext);
    }
}
