using Microsoft.UI.Xaml.Controls;
using PhotoView.Helpers;
using PhotoView.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoView.Views;

public sealed partial class MainPage
{
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

            for (var i = firstIndex; i <= lastIndex; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo)
                {
                    QueueBackgroundPreviewWarmup(imageInfo, prioritize: true);
                }
            }
        }

        if (!prioritize || _thumbnailCoordinator.PendingWarmPreviewLoads.Count == 0)
        {
            for (var i = 0; i < itemCount; i++)
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

        if (_thumbnailCoordinator.QueuedWarmPreviewLoads.Contains(imageInfo))
        {
            if (prioritize && _thumbnailCoordinator.PendingWarmPreviewLoads.Remove(imageInfo))
            {
                _thumbnailCoordinator.PendingWarmPreviewLoads.Insert(0, imageInfo);
            }

            return;
        }

        _thumbnailCoordinator.QueuedWarmPreviewLoads.Add(imageInfo);
        if (prioritize)
        {
            _thumbnailCoordinator.PendingWarmPreviewLoads.Insert(0, imageInfo);
        }
        else
        {
            _thumbnailCoordinator.PendingWarmPreviewLoads.Add(imageInfo);
        }
    }

    private void TrimTargetThumbnails(int firstIndex, int lastIndex)
    {
        if (_thumbnailCoordinator.TargetThumbnailRetainedItems.Count == 0)
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

        foreach (var imageInfo in _thumbnailCoordinator.TargetThumbnailRetainedItems.ToArray())
        {
            var index = ViewModel.Images.IndexOf(imageInfo);
            if (index < 0)
            {
                _thumbnailCoordinator.TargetThumbnailRetainedItems.Remove(imageInfo);
                DebugTargetThumbnail($"drop missing item={imageInfo.ImageName} count={_thumbnailCoordinator.TargetThumbnailRetainedItems.Count}");
            }
        }

        if (_thumbnailCoordinator.TargetThumbnailRetainedItems.Count <= MaxTargetThumbnailRetainedItems)
            return;

        var overflowCandidates = _thumbnailCoordinator.TargetThumbnailRetainedItems
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
            if (_thumbnailCoordinator.TargetThumbnailRetainedItems.Count <= MaxTargetThumbnailRetainedItems)
                break;

            if (candidate.Index >= retainFirstIndex && candidate.Index <= retainLastIndex)
                continue;

            DowngradeRetainedTargetThumbnail(candidate.ImageInfo, candidate.Index, "retain-cap");
        }
    }

    private void DowngradeRetainedTargetThumbnail(ImageFileInfo imageInfo, int index, string reason)
    {
        imageInfo.DowngradeToFastPreview();
        _thumbnailCoordinator.TargetThumbnailRetainedItems.Remove(imageInfo);
        DebugTargetThumbnail($"downgrade reason={reason} item={imageInfo.ImageName} index={index} count={_thumbnailCoordinator.TargetThumbnailRetainedItems.Count}");
    }

    private void StartWarmPreviewLoads(int budget)
    {
        var startedCount = 0;
        while (startedCount < budget &&
               _thumbnailCoordinator.ActiveWarmPreviewLoads < MaxActiveWarmPreviewLoads &&
               _thumbnailCoordinator.PendingWarmPreviewLoads.Count > 0)
        {
            var imageInfo = _thumbnailCoordinator.PendingWarmPreviewLoads[0];
            _thumbnailCoordinator.PendingWarmPreviewLoads.RemoveAt(0);
            _thumbnailCoordinator.QueuedWarmPreviewLoads.Remove(imageInfo);

            if (imageInfo.HasFastPreview || ViewModel.Images.IndexOf(imageInfo) < 0)
                continue;

            var queueVersion = Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion);
            var cancellationToken = _thumbnailCoordinator.WarmPreviewCts.Token;
            if (cancellationToken.IsCancellationRequested)
                break;

            Interlocked.Increment(ref _thumbnailCoordinator.ActiveWarmPreviewLoads);
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
            Interlocked.Decrement(ref _thumbnailCoordinator.ActiveWarmPreviewLoads);
            if (!_isUnloaded &&
                !AppLifetime.IsShuttingDown &&
                !cancellationToken.IsCancellationRequested &&
                queueVersion == Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion) &&
                HasPendingThumbnailWork())
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isUnloaded &&
                        !AppLifetime.IsShuttingDown &&
                        !cancellationToken.IsCancellationRequested &&
                        queueVersion == Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion) &&
                        HasPendingThumbnailWork())
                    {
                        RestartVisibleThumbnailTimerIfNeeded();
                    }
                });
            }
        }
    }
}

