using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Helpers;
using PhotoView.Models;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;

namespace PhotoView.Views;

public sealed partial class MainPage
{
    private void VisibleThumbnailLoadTimer_Tick(object? sender, object e)
    {
        _thumbnailCoordinator.VisibleThumbnailLoadTimer.Stop();

        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        AttachImageGridScrollViewer();

        if (_isProgrammaticScrollActive)
        {
            QueueVisibleThumbnailLoad("programmatic-scroll");
            return;
        }

        var size = ViewModel.ThumbnailSize;
        ThumbnailQueueHelper.DrainPendingItems(
            _thumbnailCoordinator.PendingFastPreviewLoads,
            ViewModel.Images,
            FastPreviewStartBudgetPerTick,
            imageInfo => !imageInfo.HasFastPreview,
            imageInfo => _ = imageInfo.EnsureFastPreviewAsync(size));

        var targetCandidates = ThumbnailQueueHelper.GetOrderedExistingItems(
            _thumbnailCoordinator.PendingTargetThumbnailLoads,
            ViewModel.Images);
        _thumbnailCoordinator.PendingTargetThumbnailLoads.Clear();

        if (!_isUserScrollInProgress)
        {
            foreach (var imageInfo in targetCandidates)
            {
                _thumbnailCoordinator.PendingTargetThumbnailLoads.Add(imageInfo);
            }

            ThumbnailQueueHelper.DrainPendingItems(
                _thumbnailCoordinator.PendingTargetThumbnailLoads,
                ViewModel.Images,
                TargetThumbnailStartBudgetPerTick,
                imageInfo => IsItemContainerRealized(imageInfo) && IsItemInCurrentTargetRange(imageInfo),
                imageInfo => _ = LoadTargetThumbnailAsync(
                    imageInfo,
                    size,
                    Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion)));
        }
        else
        {
            foreach (var imageInfo in targetCandidates)
            {
                _thumbnailCoordinator.PendingTargetThumbnailLoads.Add(imageInfo);
            }
        }

        StartWarmPreviewLoads(_isUserScrollInProgress ? WarmPreviewScrollBudgetPerTick : WarmPreviewIdleBudgetPerTick);

        RestartVisibleThumbnailTimerIfNeeded();
    }

    private void TryStartImmediateFastPreviewLoad(ImageFileInfo imageInfo, string reason)
    {
        if (_isUnloaded ||
            AppLifetime.IsShuttingDown ||
            _isProgrammaticScrollActive ||
            _thumbnailCoordinator.ImmediateVisibleThumbnailStartCount >= FastPreviewStartBudgetPerTick)
        {
            return;
        }

        if (!IsItemContainerRealized(imageInfo) || !IsItemInCurrentVisibleRange(imageInfo))
            return;

        if (!_thumbnailCoordinator.ImmediateVisibleThumbnailLoads.Add(imageInfo))
            return;

        _thumbnailCoordinator.PendingFastPreviewLoads.Remove(imageInfo);
        _thumbnailCoordinator.ImmediateVisibleThumbnailStartCount++;
        _ = imageInfo.EnsureFastPreviewAsync(ViewModel.ThumbnailSize);
    }

    private async Task LoadTargetThumbnailAsync(
        ImageFileInfo imageInfo,
        ThumbnailSize size,
        int queueVersion)
    {
        try
        {
            DebugTargetThumbnail($"load start item={imageInfo.ImageName} queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion)} hasTarget={imageInfo.HasTargetThumbnail} userScroll={_isUserScrollInProgress}");
            await imageInfo.EnsureFastPreviewAsync(size);
            if (queueVersion != Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion))
            {
                DebugTargetThumbnail($"load stop after fast item={imageInfo.ImageName} stale queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion)}");
                return;
            }

            await imageInfo.EnsureThumbnailAsync(size);
            if (queueVersion != Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion))
            {
                DebugTargetThumbnail($"load stop after target item={imageInfo.ImageName} stale queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion)}");
                return;
            }

            if (!_isUnloaded &&
                !AppLifetime.IsShuttingDown &&
                queueVersion == Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion) &&
                IsItemInCurrentTargetRange(imageInfo))
            {
                if (_thumbnailCoordinator.TargetThumbnailRetainedItems.Add(imageInfo))
                {
                    DebugTargetThumbnail($"retain item={imageInfo.ImageName} count={_thumbnailCoordinator.TargetThumbnailRetainedItems.Count}");
                }
            }
            else
            {
                DebugTargetThumbnail($"load complete not retained item={imageInfo.ImageName} inRange={IsItemInCurrentTargetRange(imageInfo)} unloaded={_isUnloaded}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] LoadTargetThumbnailAsync failed for {imageInfo.ImageName}: {ex.Message}");
        }
    }

    private bool IsItemInCurrentVisibleRange(ImageFileInfo imageInfo)
    {
        AttachImageItemsWrapGrid();
        if (_imageItemsWrapGrid == null ||
            _imageItemsWrapGrid.FirstVisibleIndex < 0 ||
            _imageItemsWrapGrid.LastVisibleIndex < _imageItemsWrapGrid.FirstVisibleIndex)
        {
            return true;
        }

        var index = ViewModel.Images.IndexOf(imageInfo);
        return index >= _imageItemsWrapGrid.FirstVisibleIndex &&
               index <= _imageItemsWrapGrid.LastVisibleIndex;
    }

    private void ResetImmediateVisibleThumbnailLoadState()
    {
        _thumbnailCoordinator.ResetImmediateVisibleLoadState();
    }

    private bool HasPendingThumbnailWork()
    {
        return _thumbnailCoordinator.HasPendingThumbnailWork();
    }

    private void ClearThumbnailQueues()
    {
        _thumbnailCoordinator.ClearQueues();
    }

    private void ImageGridScrollViewer_ViewChanging(object? sender, ScrollViewerViewChangingEventArgs e)
    {
        if (_isProgrammaticScrollActive)
            return;

        _isUserScrollInProgress = true;
        HandleFolderDrawerScrollIntent(e.NextView.VerticalOffset, "view-changing");
        QueueVisibleThumbnailLoad("user-scroll");
    }

    private void ImageGridView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_currentViewer != null)
            return;

        var delta = e.GetCurrentPoint(ImageGridView).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        if (IsCtrlPressed())
        {
            ChangeThumbnailSizeByWheel(delta);
            e.Handled = true;
            return;
        }

        if (_isProgrammaticScrollActive)
            return;

        AttachImageGridScrollViewer();
        var verticalOffset = _imageGridScrollViewer?.VerticalOffset ?? _lastImageGridVerticalOffset;
        _lastImageGridVerticalOffset = verticalOffset;

        if (delta < 0)
        {
            AutoCollapseImageBrowsingChrome("wheel-down");
        }
        else if (verticalOffset <= ImageGridTopScrollTolerance)
        {
            AutoExpandImageBrowsingChrome("wheel-up-at-top");
        }
    }

    private static bool IsCtrlPressed()
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private void ChangeThumbnailSizeByWheel(int delta)
    {
        var nextSize = delta > 0
            ? GetLargerThumbnailSize(ViewModel.ThumbnailSize)
            : GetSmallerThumbnailSize(ViewModel.ThumbnailSize);

        if (nextSize == ViewModel.ThumbnailSize)
            return;

        ViewModel.ThumbnailSize = nextSize;
    }

    private static ThumbnailSize GetLargerThumbnailSize(ThumbnailSize size)
    {
        return size switch
        {
            ThumbnailSize.Small => ThumbnailSize.Medium,
            ThumbnailSize.Medium => ThumbnailSize.Large,
            _ => ThumbnailSize.Large
        };
    }

    private static ThumbnailSize GetSmallerThumbnailSize(ThumbnailSize size)
    {
        return size switch
        {
            ThumbnailSize.Large => ThumbnailSize.Medium,
            ThumbnailSize.Medium => ThumbnailSize.Small,
            _ => ThumbnailSize.Small
        };
    }

    private void HandleFolderDrawerScrollIntent(double nextVerticalOffset, string reason)
    {
        var previousOffset = _lastImageGridVerticalOffset;
        var delta = nextVerticalOffset - previousOffset;
        _lastImageGridVerticalOffset = nextVerticalOffset;

        if (delta > ImageGridTopScrollTolerance)
        {
            AutoCollapseImageBrowsingChrome(reason);
        }
        else if (delta < -ImageGridTopScrollTolerance &&
                 previousOffset <= ImageGridTopScrollTolerance &&
                 nextVerticalOffset <= ImageGridTopScrollTolerance)
        {
            AutoExpandImageBrowsingChrome($"{reason}-top");
        }
    }

    private void ImageGridScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
            return;

        _isUserScrollInProgress = false;
        _lastImageGridVerticalOffset = _imageGridScrollViewer?.VerticalOffset ?? _lastImageGridVerticalOffset;
        QueueVisibleThumbnailLoad("view-changed");
    }

    private void QueueVisibleThumbnailLoad(string reason)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        AttachImageItemsWrapGrid();

        if (TryGetVisibleIndexRange(out var firstVisibleIndex, out var lastVisibleIndex))
        {
            var visibleCount = Math.Max(1, lastVisibleIndex - firstVisibleIndex + 1);
            var fastPreviewPadding = Math.Max(visibleCount * FastPreviewPrefetchScreenCount, FastPreviewStartBudgetPerTick);
            ThumbnailQueueHelper.QueueVisibleOrRealizedFallback(
                ImageGridView.Items,
                Math.Max(0, firstVisibleIndex - fastPreviewPadding),
                Math.Min(ImageGridView.Items.Count - 1, lastVisibleIndex + fastPreviewPadding),
                0,
                _thumbnailCoordinator.PendingFastPreviewLoads,
                _thumbnailCoordinator.RealizedImageItems,
                ViewModel.Images,
                FastPreviewStartBudgetPerTick,
                imageInfo => !imageInfo.HasFastPreview);

            var targetFirstIndex = Math.Max(0, firstVisibleIndex - TargetThumbnailPrefetchItemCount);
            var targetLastIndex = Math.Min(ImageGridView.Items.Count - 1, lastVisibleIndex + TargetThumbnailPrefetchItemCount);
            ThumbnailQueueHelper.QueueVisibleOrRealizedFallback(
                ImageGridView.Items,
                targetFirstIndex,
                targetLastIndex,
                0,
                _thumbnailCoordinator.PendingTargetThumbnailLoads,
                _thumbnailCoordinator.RealizedImageItems,
                ViewModel.Images,
                TargetThumbnailStartBudgetPerTick,
                imageInfo => !imageInfo.HasTargetThumbnail);
            TrimTargetThumbnails(targetFirstIndex, targetLastIndex);

            QueueBackgroundPreviewWarmup(firstVisibleIndex, lastVisibleIndex, prioritize: true);
        }
        else
        {
            var realizedFallbackLimit = FastPreviewStartBudgetPerTick + TargetThumbnailPrefetchItemCount;
            ThumbnailQueueHelper.QueueVisibleOrRealizedFallback(
                ImageGridView.Items,
                null,
                null,
                0,
                _thumbnailCoordinator.PendingFastPreviewLoads,
                _thumbnailCoordinator.RealizedImageItems,
                ViewModel.Images,
                realizedFallbackLimit,
                imageInfo => !imageInfo.HasFastPreview);
            ThumbnailQueueHelper.QueueVisibleOrRealizedFallback(
                ImageGridView.Items,
                null,
                null,
                0,
                _thumbnailCoordinator.PendingTargetThumbnailLoads,
                _thumbnailCoordinator.RealizedImageItems,
                ViewModel.Images,
                realizedFallbackLimit,
                imageInfo => !imageInfo.HasTargetThumbnail);

            QueueBackgroundPreviewWarmup(0, Math.Max(0, ImageGridView.Items.Count - 1), prioritize: false);
        }

        RestartVisibleThumbnailTimerIfNeeded();
    }

    private void QueueVisibleThumbnailLoad(ImageFileInfo imageInfo, string reason)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        if (!imageInfo.HasFastPreview)
        {
            _thumbnailCoordinator.PendingFastPreviewLoads.Add(imageInfo);
        }

        if (!imageInfo.HasTargetThumbnail)
        {
            _thumbnailCoordinator.PendingTargetThumbnailLoads.Add(imageInfo);
        }

        QueueBackgroundPreviewWarmup(imageInfo, prioritize: true);
        RestartVisibleThumbnailTimerIfNeeded();
    }

    private void RestartVisibleThumbnailTimerIfNeeded()
    {
        _thumbnailCoordinator.RestartVisibleThumbnailTimerIfNeeded();
    }

    private bool TryGetVisibleIndexRange(out int firstVisibleIndex, out int lastVisibleIndex)
    {
        AttachImageItemsWrapGrid();

        if (_imageItemsWrapGrid != null &&
            ThumbnailQueueHelper.TryClampVisibleRange(
                _imageItemsWrapGrid.FirstVisibleIndex,
                _imageItemsWrapGrid.LastVisibleIndex,
                ImageGridView.Items.Count,
                out firstVisibleIndex,
                out lastVisibleIndex))
        {
            return true;
        }

        firstVisibleIndex = -1;
        lastVisibleIndex = -1;
        return false;
    }

    private bool IsItemInCurrentTargetRange(ImageFileInfo imageInfo)
    {
        if (!TryGetVisibleIndexRange(out var firstVisibleIndex, out var lastVisibleIndex))
            return IsItemContainerRealized(imageInfo);

        var index = ViewModel.Images.IndexOf(imageInfo);
        if (index < 0)
            return false;

        if (!ThumbnailQueueHelper.TryGetPrefetchWindow(
                firstVisibleIndex,
                lastVisibleIndex,
                ViewModel.Images.Count,
                TargetThumbnailPrefetchItemCount,
                out var firstIndex,
                out var lastIndex))
        {
            return false;
        }

        return ThumbnailQueueHelper.IsIndexInRange(index, firstIndex, lastIndex);
    }

    private bool IsItemContainerRealized(ImageFileInfo imageInfo)
    {
        return _thumbnailCoordinator.RealizedImageItems.Contains(imageInfo);
    }

    private void ExecuteProgrammaticSelectionChange(Action action)
    {
        action();
    }

    private async Task ScrollItemIntoViewAsync(ImageFileInfo imageInfo, string reason, ScrollIntoViewAlignment alignment)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown || ViewModel.Images.Count == 0)
            return;

        AttachImageGridScrollViewer();

        _isProgrammaticScrollActive = true;
        try
        {
            await Task.Yield();

            if (_isUnloaded || AppLifetime.IsShuttingDown)
                return;

            ImageGridView.ScrollIntoView(imageInfo, alignment);
        }
        finally
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _isProgrammaticScrollActive = false;
                QueueVisibleThumbnailLoad($"post-scroll:{reason}");
            });
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void DebugTargetThumbnail(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[MainPageTargetThumb] {DateTime.Now:HH:mm:ss.fff} {message}");
    }
}

