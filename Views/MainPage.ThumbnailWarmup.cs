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

        if (!prioritize || !_thumbnailCoordinator.HasPendingWarmPreviewLoads())
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
        _thumbnailCoordinator.QueueWarmPreviewIfNeeded(imageInfo, prioritize);
    }

    private void TrimTargetThumbnails(int firstIndex, int lastIndex)
    {
        if (!_thumbnailCoordinator.HasRetainedTargetThumbnails())
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

        foreach (var imageInfo in _thumbnailCoordinator.GetRetainedTargetThumbnailSnapshot())
        {
            if (!TryGetImageIndex(imageInfo, out var index))
            {
                if (_thumbnailCoordinator.DropRetainedTargetThumbnail(imageInfo))
                {
                    DebugTargetThumbnail($"drop missing item={imageInfo.ImageName} count={_thumbnailCoordinator.RetainedTargetThumbnailCount()}");
                }
            }
        }

        if (_thumbnailCoordinator.RetainedTargetThumbnailCount() <= MaxTargetThumbnailRetainedItems)
            return;

        var overflowCandidates = _thumbnailCoordinator.GetRetainedTargetThumbnailSnapshot()
            .Where(imageInfo => TryGetImageIndex(imageInfo, out _))
            .Select(imageInfo =>
            {
                TryGetImageIndex(imageInfo, out var index);
                return new { ImageInfo = imageInfo, Index = index };
            })
            .OrderByDescending(candidate => Math.Abs(candidate.Index - centerIndex))
            .ToArray();

        foreach (var candidate in overflowCandidates)
        {
            if (_thumbnailCoordinator.RetainedTargetThumbnailCount() <= MaxTargetThumbnailRetainedItems)
                break;

            if (candidate.Index >= retainFirstIndex && candidate.Index <= retainLastIndex)
                continue;

            DowngradeRetainedTargetThumbnail(candidate.ImageInfo, candidate.Index, "retain-cap");
        }
    }

    private void DowngradeRetainedTargetThumbnail(ImageFileInfo imageInfo, int index, string reason)
    {
        if (_thumbnailCoordinator.TryDowngradeRetainedTargetThumbnail(imageInfo))
        {
            DebugTargetThumbnail($"downgrade reason={reason} item={imageInfo.ImageName} index={index} count={_thumbnailCoordinator.RetainedTargetThumbnailCount()}");
        }
    }

    private void StartWarmPreviewLoads(int budget)
    {
        var startedCount = 0;
        while (startedCount < budget &&
               _thumbnailCoordinator.TryTakeNextWarmPreviewLoad(MaxActiveWarmPreviewLoads, out var imageInfo))
        {
            if (imageInfo.HasFastPreview || !TryGetImageIndex(imageInfo, out _))
            {
                _thumbnailCoordinator.CompleteWarmPreviewLoad();
                continue;
            }

            var queueVersion = Volatile.Read(ref _thumbnailCoordinator.ThumbnailQueueVersion);
            var cancellationToken = _thumbnailCoordinator.WarmPreviewCts.Token;
            if (cancellationToken.IsCancellationRequested)
            {
                _thumbnailCoordinator.CompleteWarmPreviewLoad();
                break;
            }

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
            _thumbnailCoordinator.CompleteWarmPreviewLoad();
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

