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

    public void RestartVisibleThumbnailTimerIfNeeded()
    {
        RestartVisibleThumbnailTimer(HasPendingThumbnailWork());
    }
}
