using Microsoft.UI.Xaml.Controls;
using PhotoView.Helpers;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using System.Threading;

namespace PhotoView.Views;

internal sealed class MainPageThumbnailCoordinator : ThumbnailCoordinatorBase
{
    public MainPageThumbnailCoordinator(TimeSpan timerInterval)
        : base(timerInterval)
    {
    }

    public HashSet<ImageFileInfo> PendingFastPreviewLoads { get; } = new();

    public HashSet<ImageFileInfo> PendingTargetThumbnailLoads { get; } = new();

    public List<ImageFileInfo> PendingWarmPreviewLoads { get; } = new();

    public HashSet<ImageFileInfo> QueuedWarmPreviewLoads { get; } = new();

    public HashSet<ImageFileInfo> ImmediateVisibleThumbnailLoads { get; } = new();

    public HashSet<ImageFileInfo> TargetThumbnailRetainedItems { get; } = new();

    public int ImmediateVisibleThumbnailStartCount;

    public int ThumbnailQueueVersion;

    public int ActiveWarmPreviewLoads;

    public CancellationTokenSource WarmPreviewCts = new();

    public bool HasPendingThumbnailWork()
    {
        return PendingFastPreviewLoads.Count > 0 ||
               PendingTargetThumbnailLoads.Count > 0 ||
               PendingWarmPreviewLoads.Count > 0;
    }

    public void ResetImmediateVisibleLoadState()
    {
        ImmediateVisibleThumbnailLoads.Clear();
        ImmediateVisibleThumbnailStartCount = 0;
    }

    public void EnqueueFastPreviewIfNeeded(ImageFileInfo imageInfo)
    {
        if (!imageInfo.HasFastPreview)
        {
            PendingFastPreviewLoads.Add(imageInfo);
        }
    }

    public void EnqueueTargetThumbnailIfNeeded(ImageFileInfo imageInfo)
    {
        if (!imageInfo.HasTargetThumbnail)
        {
            PendingTargetThumbnailLoads.Add(imageInfo);
        }
    }

    public void QueueFastPreviewCandidates(
        ItemCollection items,
        int? firstIndex,
        int? lastIndex,
        int prefetchItemCount,
        IList<ImageFileInfo> orderedImages,
        int fallbackTake)
    {
        ThumbnailQueueHelper.QueueVisibleOrRealizedFallback(
            items,
            firstIndex,
            lastIndex,
            prefetchItemCount,
            PendingFastPreviewLoads,
            RealizedImageItems,
            orderedImages,
            fallbackTake,
            imageInfo => !imageInfo.HasFastPreview);
    }

    public void QueueTargetThumbnailCandidates(
        ItemCollection items,
        int? firstIndex,
        int? lastIndex,
        int prefetchItemCount,
        IList<ImageFileInfo> orderedImages,
        int fallbackTake)
    {
        ThumbnailQueueHelper.QueueVisibleOrRealizedFallback(
            items,
            firstIndex,
            lastIndex,
            prefetchItemCount,
            PendingTargetThumbnailLoads,
            RealizedImageItems,
            orderedImages,
            fallbackTake,
            imageInfo => !imageInfo.HasTargetThumbnail);
    }

    public void RequeueTargetThumbnailCandidates(IEnumerable<ImageFileInfo> imageInfos)
    {
        foreach (var imageInfo in imageInfos)
        {
            PendingTargetThumbnailLoads.Add(imageInfo);
        }
    }

    public override void MarkItemRecycled(ImageFileInfo imageInfo)
    {
        PendingFastPreviewLoads.Remove(imageInfo);
        PendingTargetThumbnailLoads.Remove(imageInfo);
        ImmediateVisibleThumbnailLoads.Remove(imageInfo);
        base.MarkItemRecycled(imageInfo);
    }

    public void ClearQueues()
    {
        WarmPreviewCts.Cancel();
        WarmPreviewCts = new CancellationTokenSource();
        Interlocked.Increment(ref ThumbnailQueueVersion);
        PendingFastPreviewLoads.Clear();
        PendingTargetThumbnailLoads.Clear();
        PendingWarmPreviewLoads.Clear();
        QueuedWarmPreviewLoads.Clear();
        TargetThumbnailRetainedItems.Clear();
        ResetImmediateVisibleLoadState();
    }

    public void ResetState()
    {
        StopVisibleThumbnailTimer();
        ClearQueues();
        ClearRealizedImageItems();
    }

    public void RestartVisibleThumbnailTimerIfNeeded()
    {
        RestartVisibleThumbnailTimer(HasPendingThumbnailWork());
    }
}
