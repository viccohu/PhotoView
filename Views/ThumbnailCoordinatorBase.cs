using Microsoft.UI.Xaml;
using PhotoView.Helpers;
using PhotoView.Models;
using System;
using System.Collections.Generic;

namespace PhotoView.Views;

internal abstract class ThumbnailCoordinatorBase
{
    protected ThumbnailCoordinatorBase(TimeSpan timerInterval)
    {
        VisibleThumbnailLoadTimer = new DispatcherTimer
        {
            Interval = timerInterval
        };
    }

    public DispatcherTimer VisibleThumbnailLoadTimer { get; }

    public HashSet<ImageFileInfo> RealizedImageItems { get; } = new();

    public void StopVisibleThumbnailTimer()
    {
        VisibleThumbnailLoadTimer.Stop();
    }

    public void ClearRealizedImageItems()
    {
        RealizedImageItems.Clear();
    }

    public void MarkItemRealized(ImageFileInfo imageInfo)
    {
        RealizedImageItems.Add(imageInfo);
    }

    public virtual void MarkItemRecycled(ImageFileInfo imageInfo)
    {
        RealizedImageItems.Remove(imageInfo);
    }

    protected void RestartVisibleThumbnailTimer(bool hasPendingWork)
    {
        ThumbnailQueueHelper.RestartTimerIfNeeded(VisibleThumbnailLoadTimer, hasPendingWork);
    }
}
