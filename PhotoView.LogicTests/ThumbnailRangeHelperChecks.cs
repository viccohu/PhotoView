using PhotoView.Helpers;

namespace PhotoView.LogicTests;

internal static class ThumbnailRangeHelperChecks
{
    public static void Run()
    {
        TryClampVisibleRange_RejectsInvalidRange();
        TryClampVisibleRange_ClampsToItemBounds();
        TryGetPrefetchWindow_ExpandsWithinBounds();
        IsIndexInRange_UsesInclusiveBounds();
    }

    private static void TryClampVisibleRange_RejectsInvalidRange()
    {
        var success = ThumbnailRangeHelper.TryClampVisibleRange(-1, 3, 10, out var firstIndex, out var lastIndex);

        TestAssert.False(success, "Negative visible start should be rejected.");
        TestAssert.Equal(-1, firstIndex, "Rejected clamp should return -1 for first index.");
        TestAssert.Equal(-1, lastIndex, "Rejected clamp should return -1 for last index.");
    }

    private static void TryClampVisibleRange_ClampsToItemBounds()
    {
        var success = ThumbnailRangeHelper.TryClampVisibleRange(4, 20, 8, out var firstIndex, out var lastIndex);

        TestAssert.True(success, "Valid visible range should clamp successfully.");
        TestAssert.Equal(4, firstIndex, "First index should stay within range.");
        TestAssert.Equal(7, lastIndex, "Last index should clamp to final item.");
    }

    private static void TryGetPrefetchWindow_ExpandsWithinBounds()
    {
        var success = ThumbnailRangeHelper.TryGetPrefetchWindow(2, 4, 6, 3, out var firstIndex, out var lastIndex);

        TestAssert.True(success, "Prefetch window should be created for valid visible range.");
        TestAssert.Equal(0, firstIndex, "Prefetch start should clamp at zero.");
        TestAssert.Equal(5, lastIndex, "Prefetch end should clamp at the final item.");
    }

    private static void IsIndexInRange_UsesInclusiveBounds()
    {
        TestAssert.True(ThumbnailRangeHelper.IsIndexInRange(3, 3, 5), "Lower bound should be inclusive.");
        TestAssert.True(ThumbnailRangeHelper.IsIndexInRange(5, 3, 5), "Upper bound should be inclusive.");
        TestAssert.False(ThumbnailRangeHelper.IsIndexInRange(6, 3, 5), "Index outside upper bound should be excluded.");
    }
}
