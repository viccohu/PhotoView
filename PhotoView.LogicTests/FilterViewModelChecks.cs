using PhotoView.ViewModels;

namespace PhotoView.LogicTests;

internal static class FilterViewModelChecks
{
    public static void Run()
    {
        IsFilterActive_IsFalseByDefault();
        RatingMode_HasRating_SetsDefaultThreshold();
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
