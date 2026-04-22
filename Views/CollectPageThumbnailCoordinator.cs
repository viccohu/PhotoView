using Microsoft.UI.Xaml.Controls;
using PhotoView.Helpers;
using PhotoView.Models;
using System;
using System.Collections.Generic;

namespace PhotoView.Views;

internal sealed class CollectPageThumbnailCoordinator : ThumbnailCoordinatorBase
{
    public CollectPageThumbnailCoordinator(TimeSpan timerInterval)
        : base(timerInterval)
    {
    }

    public HashSet<ImageFileInfo> PendingVisibleThumbnailLoads { get; } = new();

    public ItemsStackPanel? ThumbnailItemsPanel { get; set; }

    public ScrollViewer? ThumbnailScrollViewer { get; set; }

    public void ClearPendingThumbnailState()
    {
        PendingVisibleThumbnailLoads.Clear();
        ClearRealizedImageItems();
    }

    public void QueueVisibleThumbnailCandidates(
        ItemCollection items,
        int? firstVisibleIndex,
        int? lastVisibleIndex,
        int prefetchItemCount,
        IList<ImageFileInfo> orderedImages,
        int fallbackTake)
    {
        ThumbnailQueueHelper.QueueVisibleOrRealizedFallback(
            items,
            firstVisibleIndex,
            lastVisibleIndex,
            prefetchItemCount,
            PendingVisibleThumbnailLoads,
            RealizedImageItems,
            orderedImages,
            fallbackTake);
    }

    public override void MarkItemRecycled(ImageFileInfo imageInfo)
    {
        PendingVisibleThumbnailLoads.Remove(imageInfo);
        base.MarkItemRecycled(imageInfo);
    }

    public void ResetState()
    {
        StopVisibleThumbnailTimer();
        ClearPendingThumbnailState();
        ThumbnailItemsPanel = null;
        ThumbnailScrollViewer = null;
    }

    public void RestartVisibleThumbnailTimerIfNeeded()
    {
        RestartVisibleThumbnailTimer(PendingVisibleThumbnailLoads.Count > 0);
    }
}
