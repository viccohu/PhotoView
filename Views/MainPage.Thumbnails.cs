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
        _visibleThumbnailLoadTimer.Stop();

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
            _pendingFastPreviewLoads,
            ViewModel.Images,
            FastPreviewStartBudgetPerTick,
            imageInfo => !imageInfo.HasFastPreview,
            imageInfo => _ = imageInfo.EnsureFastPreviewAsync(size));

        var targetCandidates = ThumbnailQueueHelper.GetOrderedExistingItems(
            _pendingTargetThumbnailLoads,
            ViewModel.Images);
        _pendingTargetThumbnailLoads.Clear();

        var targetStartedCount = 0;
        if (!_isUserScrollInProgress)
        {
            foreach (var imageInfo in targetCandidates)
            {
                if (!IsItemContainerRealized(imageInfo) || !IsItemInCurrentTargetRange(imageInfo))
                    continue;

                if (targetStartedCount >= TargetThumbnailStartBudgetPerTick)
                {
                    _pendingTargetThumbnailLoads.Add(imageInfo);
                    continue;
                }

                _ = LoadTargetThumbnailAsync(
                    imageInfo,
                    size,
                    Volatile.Read(ref _thumbnailQueueVersion));
                targetStartedCount++;
            }
        }
        else
        {
            foreach (var imageInfo in targetCandidates)
            {
                _pendingTargetThumbnailLoads.Add(imageInfo);
            }
        }

        StartWarmPreviewLoads(_isUserScrollInProgress ? WarmPreviewScrollBudgetPerTick : WarmPreviewIdleBudgetPerTick);

        if (HasPendingThumbnailWork())
        {
            _visibleThumbnailLoadTimer.Start();
        }
    }

    private void TryStartImmediateFastPreviewLoad(ImageFileInfo imageInfo, string reason)
    {
        if (_isUnloaded ||
            AppLifetime.IsShuttingDown ||
            _isProgrammaticScrollActive ||
            _immediateVisibleThumbnailStartCount >= FastPreviewStartBudgetPerTick)
        {
            return;
        }

        if (!IsItemContainerRealized(imageInfo) || !IsItemInCurrentVisibleRange(imageInfo))
            return;

        if (!_immediateVisibleThumbnailLoads.Add(imageInfo))
            return;

        _pendingFastPreviewLoads.Remove(imageInfo);
        _immediateVisibleThumbnailStartCount++;
        _ = imageInfo.EnsureFastPreviewAsync(ViewModel.ThumbnailSize);
    }

    private async Task LoadTargetThumbnailAsync(
        ImageFileInfo imageInfo,
        ThumbnailSize size,
        int queueVersion)
    {
        try
        {
            DebugTargetThumbnail($"load start item={imageInfo.ImageName} queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailQueueVersion)} hasTarget={imageInfo.HasTargetThumbnail} userScroll={_isUserScrollInProgress}");
            await imageInfo.EnsureFastPreviewAsync(size);
            if (queueVersion != Volatile.Read(ref _thumbnailQueueVersion))
            {
                DebugTargetThumbnail($"load stop after fast item={imageInfo.ImageName} stale queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailQueueVersion)}");
                return;
            }

            await imageInfo.EnsureThumbnailAsync(size);
            if (queueVersion != Volatile.Read(ref _thumbnailQueueVersion))
            {
                DebugTargetThumbnail($"load stop after target item={imageInfo.ImageName} stale queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailQueueVersion)}");
                return;
            }

            if (!_isUnloaded &&
                !AppLifetime.IsShuttingDown &&
                queueVersion == Volatile.Read(ref _thumbnailQueueVersion) &&
                IsItemInCurrentTargetRange(imageInfo))
            {
                if (_targetThumbnailRetainedItems.Add(imageInfo))
                {
                    DebugTargetThumbnail($"retain item={imageInfo.ImageName} count={_targetThumbnailRetainedItems.Count}");
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
        _immediateVisibleThumbnailLoads.Clear();
        _immediateVisibleThumbnailStartCount = 0;
    }

    private bool HasPendingThumbnailWork()
    {
        return _pendingFastPreviewLoads.Count > 0 ||
               _pendingTargetThumbnailLoads.Count > 0 ||
               _pendingWarmPreviewLoads.Count > 0;
    }

    private void ClearThumbnailQueues()
    {
        _warmPreviewCts.Cancel();
        _warmPreviewCts = new CancellationTokenSource();
        Interlocked.Increment(ref _thumbnailQueueVersion);
        _pendingFastPreviewLoads.Clear();
        _pendingTargetThumbnailLoads.Clear();
        _pendingWarmPreviewLoads.Clear();
        _queuedWarmPreviewLoads.Clear();
        _targetThumbnailRetainedItems.Clear();
        ResetImmediateVisibleThumbnailLoadState();
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
                _pendingFastPreviewLoads,
                _realizedImageItems,
                ViewModel.Images,
                FastPreviewStartBudgetPerTick,
                imageInfo => !imageInfo.HasFastPreview);

            var targetFirstIndex = Math.Max(0, firstVisibleIndex - TargetThumbnailPrefetchItemCount);
            var targetLastIndex = Math.Min(ImageGridView.Items.Count - 1, lastVisibleIndex + TargetThumbnailPrefetchItemCount);
            ThumbnailQueueHelper.AddItemsInRange(
                ImageGridView.Items,
                targetFirstIndex,
                targetLastIndex,
                _pendingTargetThumbnailLoads,
                imageInfo => !imageInfo.HasTargetThumbnail);
            TrimTargetThumbnails(targetFirstIndex, targetLastIndex);

            QueueBackgroundPreviewWarmup(firstVisibleIndex, lastVisibleIndex, prioritize: true);
        }
        else
        {
            var realizedFallbackLimit = FastPreviewStartBudgetPerTick + TargetThumbnailPrefetchItemCount;
            foreach (var imageInfo in ThumbnailQueueHelper.GetRealizedFallbackItems(
                _realizedImageItems,
                ViewModel.Images,
                realizedFallbackLimit))
            {
                if (!imageInfo.HasFastPreview)
                {
                    _pendingFastPreviewLoads.Add(imageInfo);
                }

                if (!imageInfo.HasTargetThumbnail)
                {
                    _pendingTargetThumbnailLoads.Add(imageInfo);
                }
            }

            QueueBackgroundPreviewWarmup(0, Math.Max(0, ImageGridView.Items.Count - 1), prioritize: false);
        }

        if (!HasPendingThumbnailWork())
            return;

        _visibleThumbnailLoadTimer.Stop();
        _visibleThumbnailLoadTimer.Start();
    }

    private void QueueVisibleThumbnailLoad(ImageFileInfo imageInfo, string reason)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        if (!imageInfo.HasFastPreview)
        {
            _pendingFastPreviewLoads.Add(imageInfo);
        }

        if (!imageInfo.HasTargetThumbnail)
        {
            _pendingTargetThumbnailLoads.Add(imageInfo);
        }

        QueueBackgroundPreviewWarmup(imageInfo, prioritize: true);
        _visibleThumbnailLoadTimer.Stop();
        _visibleThumbnailLoadTimer.Start();
    }

    private bool TryGetVisibleIndexRange(out int firstVisibleIndex, out int lastVisibleIndex)
    {
        AttachImageItemsWrapGrid();

        if (_imageItemsWrapGrid != null &&
            _imageItemsWrapGrid.FirstVisibleIndex >= 0 &&
            _imageItemsWrapGrid.LastVisibleIndex >= _imageItemsWrapGrid.FirstVisibleIndex &&
            ImageGridView.Items.Count > 0)
        {
            firstVisibleIndex = Math.Clamp(_imageItemsWrapGrid.FirstVisibleIndex, 0, ImageGridView.Items.Count - 1);
            lastVisibleIndex = Math.Clamp(_imageItemsWrapGrid.LastVisibleIndex, firstVisibleIndex, ImageGridView.Items.Count - 1);
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

        var firstIndex = Math.Max(0, firstVisibleIndex - TargetThumbnailPrefetchItemCount);
        var lastIndex = Math.Min(ViewModel.Images.Count - 1, lastVisibleIndex + TargetThumbnailPrefetchItemCount);
        return index >= firstIndex && index <= lastIndex;
    }

    private void QueueBackgroundPreviewWarmup(int firstVisibleIndex, int lastVisibleIndex, bool prioritize)
    {
        var itemCount = ImageGridView.Items.Count;
        if (itemCount == 0)
            return;

        if (prioritize)
        {
            var visibleCount = Math.Max(1, lastVisibleIndex - firstVisibleIndex + 1);
            var padding = Math.Max(visibleCount * FastPreviewPrefetchScreenCount, FastPreviewStartBudgetPerTick);
            var firstIndex = Math.Max(0, firstVisibleIndex - padding);
            var lastIndex = Math.Min(itemCount - 1, lastVisibleIndex + padding);

            for (int i = firstIndex; i <= lastIndex; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo)
                {
                    QueueBackgroundPreviewWarmup(imageInfo, prioritize: true);
                }
            }
        }

        if (!prioritize || _pendingWarmPreviewLoads.Count == 0)
        {
            for (int i = 0; i < itemCount; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo)
                {
                    QueueBackgroundPreviewWarmup(imageInfo, prioritize: false);
                }
            }
        }
    }

    private void QueueBackgroundPreviewWarmup(ImageFileInfo imageInfo, bool prioritize)
    {
        if (imageInfo.HasFastPreview)
            return;

        if (_queuedWarmPreviewLoads.Contains(imageInfo))
        {
            if (prioritize && _pendingWarmPreviewLoads.Remove(imageInfo))
            {
                _pendingWarmPreviewLoads.Insert(0, imageInfo);
            }
            return;
        }

        _queuedWarmPreviewLoads.Add(imageInfo);
        if (prioritize)
        {
            _pendingWarmPreviewLoads.Insert(0, imageInfo);
        }
        else
        {
            _pendingWarmPreviewLoads.Add(imageInfo);
        }
    }

    private void TrimTargetThumbnails(int firstIndex, int lastIndex)
    {
        if (_targetThumbnailRetainedItems.Count == 0)
            return;

        var retainFirstIndex = firstIndex;
        var retainLastIndex = lastIndex;
        var centerIndex = (firstIndex + lastIndex) / 2d;

        if (TryGetVisibleIndexRange(out var visibleFirstIndex, out var visibleLastIndex))
        {
            var visibleCount = Math.Max(1, visibleLastIndex - visibleFirstIndex + 1);
            var retainPadding = (visibleCount * TargetThumbnailRetainScreenCount) + TargetThumbnailPrefetchItemCount;
            retainFirstIndex = Math.Max(0, visibleFirstIndex - retainPadding);
            retainLastIndex = Math.Min(ViewModel.Images.Count - 1, visibleLastIndex + retainPadding);
            centerIndex = (visibleFirstIndex + visibleLastIndex) / 2d;
        }

        foreach (var imageInfo in _targetThumbnailRetainedItems.ToArray())
        {
            var index = ViewModel.Images.IndexOf(imageInfo);
            if (index < 0)
            {
                _targetThumbnailRetainedItems.Remove(imageInfo);
                DebugTargetThumbnail($"drop missing item={imageInfo.ImageName} count={_targetThumbnailRetainedItems.Count}");
            }
        }

        if (_targetThumbnailRetainedItems.Count <= MaxTargetThumbnailRetainedItems)
            return;

        var overflowCandidates = _targetThumbnailRetainedItems
            .Select(imageInfo => new
            {
                ImageInfo = imageInfo,
                Index = ViewModel.Images.IndexOf(imageInfo)
            })
            .Where(candidate => candidate.Index >= 0)
            .OrderByDescending(candidate => Math.Abs(candidate.Index - centerIndex))
            .ToArray();

        foreach (var candidate in overflowCandidates)
        {
            if (_targetThumbnailRetainedItems.Count <= MaxTargetThumbnailRetainedItems)
                break;

            if (candidate.Index >= retainFirstIndex && candidate.Index <= retainLastIndex)
                continue;

            DowngradeRetainedTargetThumbnail(candidate.ImageInfo, candidate.Index, "retain-cap");
        }
    }

    private void DowngradeRetainedTargetThumbnail(ImageFileInfo imageInfo, int index, string reason)
    {
        imageInfo.DowngradeToFastPreview();
        _targetThumbnailRetainedItems.Remove(imageInfo);
        DebugTargetThumbnail($"downgrade reason={reason} item={imageInfo.ImageName} index={index} count={_targetThumbnailRetainedItems.Count}");
    }

    private void StartWarmPreviewLoads(int budget)
    {
        var startedCount = 0;
        while (startedCount < budget &&
               _activeWarmPreviewLoads < MaxActiveWarmPreviewLoads &&
               _pendingWarmPreviewLoads.Count > 0)
        {
            var imageInfo = _pendingWarmPreviewLoads[0];
            _pendingWarmPreviewLoads.RemoveAt(0);
            _queuedWarmPreviewLoads.Remove(imageInfo);

            if (imageInfo.HasFastPreview || ViewModel.Images.IndexOf(imageInfo) < 0)
                continue;

            var queueVersion = Volatile.Read(ref _thumbnailQueueVersion);
            var cancellationToken = _warmPreviewCts.Token;
            if (cancellationToken.IsCancellationRequested)
                break;

            Interlocked.Increment(ref _activeWarmPreviewLoads);
            _ = WarmFastPreviewAsync(imageInfo, queueVersion, cancellationToken);
            startedCount++;
        }
    }

    private async Task WarmFastPreviewAsync(
        ImageFileInfo imageInfo,
        int queueVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailService.WarmFastPreviewAsync(
                imageInfo.ImageFile,
                WarmPreviewLongSidePixels,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] WarmFastPreviewAsync failed for {imageInfo.ImageName}: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _activeWarmPreviewLoads);
            if (!_isUnloaded &&
                !AppLifetime.IsShuttingDown &&
                !cancellationToken.IsCancellationRequested &&
                queueVersion == Volatile.Read(ref _thumbnailQueueVersion) &&
                HasPendingThumbnailWork())
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isUnloaded &&
                        !AppLifetime.IsShuttingDown &&
                        !cancellationToken.IsCancellationRequested &&
                        queueVersion == Volatile.Read(ref _thumbnailQueueVersion) &&
                        HasPendingThumbnailWork())
                    {
                        _visibleThumbnailLoadTimer.Stop();
                        _visibleThumbnailLoadTimer.Start();
                    }
                });
            }
        }
    }

    private bool IsItemContainerRealized(ImageFileInfo imageInfo)
    {
        return _realizedImageItems.Contains(imageInfo);
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
