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
}
