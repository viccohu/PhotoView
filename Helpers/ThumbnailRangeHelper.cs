namespace PhotoView.Helpers;

internal static class ThumbnailRangeHelper
{
    public static bool TryClampVisibleRange(
        int firstVisibleIndex,
        int lastVisibleIndex,
        int itemCount,
        out int clampedFirstIndex,
        out int clampedLastIndex)
    {
        if (itemCount <= 0 || firstVisibleIndex < 0 || lastVisibleIndex < firstVisibleIndex)
        {
            clampedFirstIndex = -1;
            clampedLastIndex = -1;
            return false;
        }

        clampedFirstIndex = Math.Clamp(firstVisibleIndex, 0, itemCount - 1);
        clampedLastIndex = Math.Clamp(lastVisibleIndex, clampedFirstIndex, itemCount - 1);
        return true;
    }

    public static bool TryGetPrefetchWindow(
        int firstVisibleIndex,
        int lastVisibleIndex,
        int itemCount,
        int prefetchItemCount,
        out int firstIndex,
        out int lastIndex)
    {
        if (!TryClampVisibleRange(firstVisibleIndex, lastVisibleIndex, itemCount, out var clampedFirstIndex, out var clampedLastIndex))
        {
            firstIndex = -1;
            lastIndex = -1;
            return false;
        }

        firstIndex = Math.Max(0, clampedFirstIndex - prefetchItemCount);
        lastIndex = Math.Min(itemCount - 1, clampedLastIndex + prefetchItemCount);
        return true;
    }

    public static bool IsIndexInRange(int index, int firstIndex, int lastIndex)
    {
        return index >= firstIndex && index <= lastIndex;
    }
}
