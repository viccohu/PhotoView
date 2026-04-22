using Microsoft.UI.Xaml.Controls;
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

    public void Clear()
    {
        PendingVisibleThumbnailLoads.Clear();
        ClearRealizedImageItems();
        ThumbnailItemsPanel = null;
        ThumbnailScrollViewer = null;
    }

    public void RestartVisibleThumbnailTimerIfNeeded()
    {
        RestartVisibleThumbnailTimer(PendingVisibleThumbnailLoads.Count > 0);
    }
}
