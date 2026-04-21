using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using PhotoView.Models;

namespace PhotoView.ViewModels;

public enum RatingFilterMode
{
    All,
    HasRating,
    NoRating
}

public enum RatingCondition
{
    Equals,
    GreaterOrEqual,
    LessOrEqual
}

public partial class FilterViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isImageFilter;

    [ObservableProperty]
    private bool _isRawFilter;

    [ObservableProperty]
    private RatingFilterMode _ratingMode = RatingFilterMode.All;

    [ObservableProperty]
    private RatingCondition _ratingCondition = RatingCondition.GreaterOrEqual;

    [ObservableProperty]
    private int _ratingStars = 1;

    [ObservableProperty]
    private bool _isPendingDeleteFilter;

    [ObservableProperty]
    private bool _isBurstFilter;

    public event EventHandler? FilterChanged;

    public bool IsFilterActive
    {
        get
        {
            return IsImageFilter || IsRawFilter ||
                   RatingMode != RatingFilterMode.All || IsPendingDeleteFilter || IsBurstFilter;
        }
    }

    public FilterViewModel()
    {
    }

    public static bool IsRawExtension(string extension)
    {
        return ImageFormatRegistry.IsRaw(extension);
    }

    partial void OnIsImageFilterChanged(bool value)
    {
        OnFilterChanged();
    }

    partial void OnIsRawFilterChanged(bool value)
    {
        OnFilterChanged();
    }

    partial void OnRatingModeChanged(RatingFilterMode value)
    {
        if (value == RatingFilterMode.HasRating)
        {
            RatingCondition = RatingCondition.GreaterOrEqual;
            RatingStars = 1;
        }
        OnFilterChanged();
    }

    partial void OnRatingConditionChanged(RatingCondition value)
    {
        OnFilterChanged();
    }

    partial void OnRatingStarsChanged(int value)
    {
        OnFilterChanged();
    }

    partial void OnIsPendingDeleteFilterChanged(bool value)
    {
        OnFilterChanged();
    }

    partial void OnIsBurstFilterChanged(bool value)
    {
        OnFilterChanged();
    }

    public void Reset()
    {
        IsImageFilter = false;
        IsRawFilter = false;
        RatingMode = RatingFilterMode.All;
        RatingCondition = RatingCondition.GreaterOrEqual;
        RatingStars = 1;
        IsPendingDeleteFilter = false;
        IsBurstFilter = false;
    }

    private void OnFilterChanged()
    {
        FilterChanged?.Invoke(this, EventArgs.Empty);
    }
}
