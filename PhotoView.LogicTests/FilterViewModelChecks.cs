using PhotoView.ViewModels;

namespace PhotoView.LogicTests;

internal static class FilterViewModelChecks
{
    public static void Run()
    {
        IsFilterActive_IsFalseByDefault();
        RatingMode_HasRating_SetsDefaultThreshold();
        DualFormatFilter_ClearsOtherFormatFilters();
        DualFormatInverseFilter_ClearsOtherFormatFilters();
        StandardFormatFilter_ClearsDualFormatFilters();
        Reset_ClearsAllFilterFlags();
    }

    private static void IsFilterActive_IsFalseByDefault()
    {
        var viewModel = new FilterViewModel();

        TestAssert.False(viewModel.IsFilterActive, "Filter should be inactive by default.");
    }

    private static void RatingMode_HasRating_SetsDefaultThreshold()
    {
        var viewModel = new FilterViewModel
        {
            RatingCondition = RatingCondition.Equals,
            RatingStars = 5
        };

        viewModel.RatingMode = RatingFilterMode.HasRating;

        TestAssert.Equal(RatingCondition.GreaterOrEqual, viewModel.RatingCondition, "HasRating should reset condition.");
        TestAssert.Equal(1, viewModel.RatingStars, "HasRating should reset stars to 1.");
        TestAssert.True(viewModel.IsFilterActive, "HasRating should activate filter.");
    }

    private static void DualFormatFilter_ClearsOtherFormatFilters()
    {
        var viewModel = new FilterViewModel
        {
            IsImageFilter = true,
            IsImageSingleOnlyFilter = true,
            IsRawFilter = true,
            IsRawSingleOnlyFilter = true
        };

        viewModel.IsDualFormatFilter = true;

        TestAssert.True(viewModel.IsDualFormatFilter, "Dual format filter should be enabled.");
        TestAssert.False(viewModel.IsDualFormatInverseFilter, "Dual format inverse filter should be cleared.");
        TestAssert.False(viewModel.IsImageFilter, "Dual format filter should clear image filter.");
        TestAssert.False(viewModel.IsImageSingleOnlyFilter, "Dual format filter should clear image solo filter.");
        TestAssert.False(viewModel.IsRawFilter, "Dual format filter should clear raw filter.");
        TestAssert.False(viewModel.IsRawSingleOnlyFilter, "Dual format filter should clear raw solo filter.");
    }

    private static void DualFormatInverseFilter_ClearsOtherFormatFilters()
    {
        var viewModel = new FilterViewModel
        {
            IsImageFilter = true,
            IsImageSingleOnlyFilter = true,
            IsRawFilter = true,
            IsRawSingleOnlyFilter = true
        };

        viewModel.IsDualFormatInverseFilter = true;

        TestAssert.True(viewModel.IsDualFormatInverseFilter, "Single format filter should be enabled.");
        TestAssert.False(viewModel.IsDualFormatFilter, "Single format filter should clear dual format filter.");
        TestAssert.False(viewModel.IsImageFilter, "Single format filter should clear image filter.");
        TestAssert.False(viewModel.IsImageSingleOnlyFilter, "Single format filter should clear image solo filter.");
        TestAssert.False(viewModel.IsRawFilter, "Single format filter should clear raw filter.");
        TestAssert.False(viewModel.IsRawSingleOnlyFilter, "Single format filter should clear raw solo filter.");
    }

    private static void StandardFormatFilter_ClearsDualFormatFilters()
    {
        var viewModel = new FilterViewModel
        {
            IsDualFormatInverseFilter = true
        };

        viewModel.IsImageFilter = true;

        TestAssert.True(viewModel.IsImageFilter, "Image filter should stay enabled.");
        TestAssert.False(viewModel.IsDualFormatFilter, "Image filter should clear dual format filter.");
        TestAssert.False(viewModel.IsDualFormatInverseFilter, "Image filter should clear single format filter.");
    }

    private static void Reset_ClearsAllFilterFlags()
    {
        var viewModel = new FilterViewModel
        {
            IsImageFilter = true,
            IsRawFilter = true,
            RatingMode = RatingFilterMode.NoRating,
            IsPendingDeleteFilter = true,
            IsBurstFilter = true
        };

        viewModel.Reset();

        TestAssert.False(viewModel.IsImageFilter, "Reset should clear image filter.");
        TestAssert.False(viewModel.IsRawFilter, "Reset should clear raw filter.");
        TestAssert.Equal(RatingFilterMode.All, viewModel.RatingMode, "Reset should restore rating mode.");
        TestAssert.False(viewModel.IsPendingDeleteFilter, "Reset should clear pending delete filter.");
        TestAssert.False(viewModel.IsBurstFilter, "Reset should clear burst filter.");
        TestAssert.False(viewModel.IsFilterActive, "Reset should deactivate filter.");
    }
}
