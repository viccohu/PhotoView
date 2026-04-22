using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Helpers;
using PhotoView.Models;
using System;
using System.Collections.Specialized;
using System.Linq;
using Windows.System;

namespace PhotoView.Views;

public sealed partial class CollectPage
{
    private void PreviewThumbnailGridView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_isDisposed || _isUnloaded)
            return;

        AttachThumbnailScrollViewer();
        if (_thumbnailScrollViewer == null || _thumbnailScrollViewer.ScrollableWidth <= 0)
            return;

        var delta = e.GetCurrentPoint(PreviewThumbnailGridView).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        var targetOffset = Math.Clamp(
            _thumbnailScrollViewer.HorizontalOffset - delta,
            0,
            _thumbnailScrollViewer.ScrollableWidth);
        _thumbnailScrollViewer.ChangeView(targetOffset, null, null, true);
        e.Handled = true;
        QueueVisibleThumbnailLoad("thumbnail-wheel");
    }

    private void PreviewThumbnailGridView_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled)
            return;

        if (_isDisposed || _isUnloaded || !TryGetDirectionalNavigationDelta(e.Key, out var direction))
            return;

        e.Handled = true;
        if (ShouldProcessDirectionalNavigation(e.KeyStatus.WasKeyDown, direction))
        {
            MoveSelection(direction);
        }
    }

    private void PreviewThumbnailGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not ImageFileInfo imageInfo)
            return;

        if (args.InRecycleQueue)
        {
            imageInfo.CancelTargetThumbnailLoad();
            _pendingVisibleThumbnailLoads.Remove(imageInfo);
            _realizedImageItems.Remove(imageInfo);
            DebugThumbnailLoad($"Recycle cancel target image={GetDebugName(imageInfo)}");
            return;
        }

        if (args.Phase == 0)
        {
            args.RegisterUpdateCallback(1u, PreviewThumbnailGridView_ContainerContentChanging);
        }
        else if (args.Phase == 1)
        {
            _realizedImageItems.Add(imageInfo);
            QueueVisibleThumbnailLoad("container-phase1");
        }
    }

    private void VisibleThumbnailLoadTimer_Tick(object? sender, object e)
    {
        _visibleThumbnailLoadTimer.Stop();
        if (_isUnloaded)
            return;

        ThumbnailQueueHelper.DrainPendingItems(
            _pendingVisibleThumbnailLoads,
            ViewModel.Images,
            VisibleThumbnailStartBudgetPerTick,
            imageInfo => _realizedImageItems.Contains(imageInfo),
            imageInfo => _ = imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize));

        RestartVisibleThumbnailTimerIfNeeded();
    }

    private void QueueVisibleThumbnailLoad(string reason)
    {
        if (_isUnloaded)
            return;

        AttachThumbnailItemsPanel();

        var hasVisibleRange = _thumbnailItemsPanel != null &&
            _thumbnailItemsPanel.FirstVisibleIndex >= 0 &&
            _thumbnailItemsPanel.LastVisibleIndex >= _thumbnailItemsPanel.FirstVisibleIndex;

        ThumbnailQueueHelper.QueueVisibleOrRealizedFallback(
            PreviewThumbnailGridView.Items,
            hasVisibleRange ? _thumbnailItemsPanel!.FirstVisibleIndex : null,
            hasVisibleRange ? _thumbnailItemsPanel!.LastVisibleIndex : null,
            VisibleThumbnailPrefetchItemCount,
            _pendingVisibleThumbnailLoads,
            _realizedImageItems,
            ViewModel.Images,
            VisibleThumbnailStartBudgetPerTick + VisibleThumbnailPrefetchItemCount);

        RestartVisibleThumbnailTimerIfNeeded();
    }

    private void RestartVisibleThumbnailTimerIfNeeded()
    {
        if (_pendingVisibleThumbnailLoads.Count == 0)
            return;

        _visibleThumbnailLoadTimer.Stop();
        _visibleThumbnailLoadTimer.Start();
    }

    private void AttachThumbnailItemsPanel()
    {
        if (_thumbnailItemsPanel != null)
            return;

        _thumbnailItemsPanel = FindDescendant<ItemsStackPanel>(PreviewThumbnailGridView);
    }

    private void AttachThumbnailScrollViewer()
    {
        if (_thumbnailScrollViewer != null)
            return;

        _thumbnailScrollViewer = FindDescendant<ScrollViewer>(PreviewThumbnailGridView);
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        if (parent is T typedParent)
            return typedParent;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            var result = FindDescendant<T>(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private void Images_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _pendingVisibleThumbnailLoads.Clear();
            _realizedImageItems.Clear();
            DebugThumbnailLoad("Images reset; cleared visible thumbnail queues");
        }
        else
        {
            DebugThumbnailLoad($"Images changed action={e.Action} new={e.NewItems?.Count ?? 0} old={e.OldItems?.Count ?? 0}");
        }

        QueueVisibleThumbnailLoad("images-changed");
    }
}
