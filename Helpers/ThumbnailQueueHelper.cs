using PhotoView.Models;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace PhotoView.Helpers;

internal static class ThumbnailQueueHelper
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

    public static ImageFileInfo[] GetOrderedExistingItems(
        IEnumerable<ImageFileInfo> items,
        IList<ImageFileInfo> orderedImages)
    {
        return items
            .Select(imageInfo => new
            {
                ImageInfo = imageInfo,
                Index = orderedImages.IndexOf(imageInfo)
            })
            .Where(candidate => candidate.Index >= 0)
            .OrderBy(candidate => candidate.Index)
            .Select(candidate => candidate.ImageInfo)
            .ToArray();
    }

    public static IEnumerable<ImageFileInfo> GetRealizedFallbackItems(
        IEnumerable<ImageFileInfo> realizedItems,
        IList<ImageFileInfo> orderedImages,
        int take)
    {
        return realizedItems
            .Select(imageInfo => new
            {
                ImageInfo = imageInfo,
                Index = orderedImages.IndexOf(imageInfo)
            })
            .Where(candidate => candidate.Index >= 0)
            .OrderBy(candidate => candidate.Index)
            .Take(take)
            .Select(candidate => candidate.ImageInfo);
    }

    public static void AddItemsInRange(
        ItemCollection items,
        int firstIndex,
        int lastIndex,
        ISet<ImageFileInfo> pendingItems,
        System.Predicate<ImageFileInfo>? shouldInclude = null)
    {
        for (var index = firstIndex; index <= lastIndex; index++)
        {
            if (items[index] is not ImageFileInfo imageInfo)
            {
                continue;
            }

            if (shouldInclude == null || shouldInclude(imageInfo))
            {
                pendingItems.Add(imageInfo);
            }
        }
    }

    public static void QueueVisibleOrRealizedFallback(
        ItemCollection items,
        int? firstVisibleIndex,
        int? lastVisibleIndex,
        int prefetchItemCount,
        ISet<ImageFileInfo> pendingItems,
        IEnumerable<ImageFileInfo> realizedItems,
        IList<ImageFileInfo> orderedImages,
        int fallbackTake,
        System.Predicate<ImageFileInfo>? shouldInclude = null)
    {
        if (firstVisibleIndex.HasValue && lastVisibleIndex.HasValue)
        {
            var firstIndex = Math.Max(0, firstVisibleIndex.Value - prefetchItemCount);
            var lastIndex = Math.Min(items.Count - 1, lastVisibleIndex.Value + prefetchItemCount);
            AddItemsInRange(items, firstIndex, lastIndex, pendingItems, shouldInclude);
            return;
        }

        foreach (var imageInfo in GetRealizedFallbackItems(realizedItems, orderedImages, fallbackTake))
        {
            if (shouldInclude == null || shouldInclude(imageInfo))
            {
                pendingItems.Add(imageInfo);
            }
        }
    }

    public static bool DrainPendingItems(
        ISet<ImageFileInfo> pendingItems,
        IList<ImageFileInfo> orderedImages,
        int startBudget,
        System.Predicate<ImageFileInfo> canStart,
        System.Action<ImageFileInfo> start,
        System.Action<ImageFileInfo>? requeue = null)
    {
        var candidates = GetOrderedExistingItems(pendingItems, orderedImages);
        pendingItems.Clear();

        var started = 0;
        foreach (var imageInfo in candidates)
        {
            if (!canStart(imageInfo))
            {
                continue;
            }

            if (started >= startBudget)
            {
                if (requeue != null)
                {
                    requeue(imageInfo);
                }
                else
                {
                    pendingItems.Add(imageInfo);
                }

                continue;
            }

            start(imageInfo);
            started++;
        }

        return pendingItems.Count > 0;
    }

    public static void RestartTimerIfNeeded(DispatcherTimer timer, bool hasPendingWork)
    {
        if (!hasPendingWork)
        {
            return;
        }

        timer.Stop();
        timer.Start();
    }
}
