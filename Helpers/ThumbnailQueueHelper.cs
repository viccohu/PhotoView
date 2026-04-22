using PhotoView.Models;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml.Controls;

namespace PhotoView.Helpers;

internal static class ThumbnailQueueHelper
{
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
}
