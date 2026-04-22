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

    public void DrainFastPreviewLoads(
        IList<ImageFileInfo> orderedImages,
        int startBudget,
        Action<ImageFileInfo> startLoad)
    {
        ThumbnailQueueHelper.DrainPendingItems(
            PendingFastPreviewLoads,
            orderedImages,
            startBudget,
            imageInfo => !imageInfo.HasFastPreview,
            startLoad);
    }

    public void ProcessTargetThumbnailLoads(
        IList<ImageFileInfo> orderedImages,
        int startBudget,
        bool canStartLoads,
        Predicate<ImageFileInfo> shouldStart,
        Action<ImageFileInfo> startLoad)
    {
        var targetCandidates = ThumbnailQueueHelper.GetOrderedExistingItems(
            PendingTargetThumbnailLoads,
            orderedImages);
        PendingTargetThumbnailLoads.Clear();
        RequeueTargetThumbnailCandidates(targetCandidates);

        if (!canStartLoads)
        {
            return;
        }

        ThumbnailQueueHelper.DrainPendingItems(
            PendingTargetThumbnailLoads,
            orderedImages,
            startBudget,
            shouldStart,
            startLoad);
    }

    public override void MarkItemRecycled(ImageFileInfo imageInfo)
    {
        PendingFastPreviewLoads.Remove(imageInfo);
        PendingTargetThumbnailLoads.Remove(imageInfo);
        ImmediateVisibleThumbnailLoads.Remove(imageInfo);
        base.MarkItemRecycled(imageInfo);
    }

    public bool TryStartImmediateVisibleFastPreview(ImageFileInfo imageInfo, int startBudget)
    {
        if (ImmediateVisibleThumbnailStartCount >= startBudget)
        {
            return false;
        }

        if (!ImmediateVisibleThumbnailLoads.Add(imageInfo))
        {
            return false;
        }

        PendingFastPreviewLoads.Remove(imageInfo);
        ImmediateVisibleThumbnailStartCount++;
        return true;
    }

    public bool TryRetainTargetThumbnail(ImageFileInfo imageInfo)
    {
        return TargetThumbnailRetainedItems.Add(imageInfo);
    }

    public bool HasRetainedTargetThumbnails()
    {
        return TargetThumbnailRetainedItems.Count > 0;
    }

    public int RetainedTargetThumbnailCount()
    {
        return TargetThumbnailRetainedItems.Count;
    }

    public ImageFileInfo[] GetRetainedTargetThumbnailSnapshot()
    {
        return TargetThumbnailRetainedItems.ToArray();
    }

    public bool DropRetainedTargetThumbnail(ImageFileInfo imageInfo)
    {
        return TargetThumbnailRetainedItems.Remove(imageInfo);
    }

    public bool TryDowngradeRetainedTargetThumbnail(ImageFileInfo imageInfo)
    {
        imageInfo.DowngradeToFastPreview();
        return DropRetainedTargetThumbnail(imageInfo);
    }

    public void QueueWarmPreviewIfNeeded(ImageFileInfo imageInfo, bool prioritize)
    {
        if (imageInfo.HasFastPreview)
        {
            return;
        }

        if (QueuedWarmPreviewLoads.Contains(imageInfo))
        {
            if (prioritize && PendingWarmPreviewLoads.Remove(imageInfo))
            {
                PendingWarmPreviewLoads.Insert(0, imageInfo);
            }

            return;
        }

        QueuedWarmPreviewLoads.Add(imageInfo);
        if (prioritize)
        {
            PendingWarmPreviewLoads.Insert(0, imageInfo);
        }
        else
        {
            PendingWarmPreviewLoads.Add(imageInfo);
        }
    }

    public bool HasPendingWarmPreviewLoads()
    {
        return PendingWarmPreviewLoads.Count > 0;
    }

    public bool TryTakeNextWarmPreviewLoad(int maxActiveLoads, out ImageFileInfo imageInfo)
    {
        imageInfo = null!;
        if (ActiveWarmPreviewLoads >= maxActiveLoads || PendingWarmPreviewLoads.Count == 0)
        {
            return false;
        }

        imageInfo = PendingWarmPreviewLoads[0];
        PendingWarmPreviewLoads.RemoveAt(0);
        QueuedWarmPreviewLoads.Remove(imageInfo);
        Interlocked.Increment(ref ActiveWarmPreviewLoads);
        return true;
    }

    public void CompleteWarmPreviewLoad()
    {
        Interlocked.Decrement(ref ActiveWarmPreviewLoads);
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
