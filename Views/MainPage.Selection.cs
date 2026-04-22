using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Helpers;
using PhotoView.Models;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace PhotoView.Views;

public sealed partial class MainPage
{
    private void ImageGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (_suppressNextImageDragStart)
        {
            _suppressNextImageDragStart = false;
            e.Cancel = true;
            return;
        }

        var dragImages = GetDragImagesFromItems(e.Items).ToList();
        if (dragImages.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        var storageFiles = dragImages
            .Select(image => image.ImageFile)
            .Where(file => file != null && !string.IsNullOrWhiteSpace(file.Path) && File.Exists(file.Path))
            .Cast<StorageFile>()
            .ToList();

        if (storageFiles.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        e.Data.RequestedOperation = DataPackageOperation.Copy;
        e.Data.Properties.ApplicationName = "PhotoView";
        e.Data.Properties.Title = storageFiles.Count == 1
            ? storageFiles[0].Name
            : $"{storageFiles.Count} files";
        e.Data.SetStorageItems(storageFiles);
    }

    private IEnumerable<ImageFileInfo> GetDragImagesFromItems(IList<object> dragItems)
    {
        var dragImage = dragItems.OfType<ImageFileInfo>().FirstOrDefault();
        if (dragImage == null)
            return Array.Empty<ImageFileInfo>();

        if (ImageGridView.SelectedItems.Contains(dragImage))
        {
            return GetOrderedSelectedImagesForDrag();
        }

        ExecuteProgrammaticSelectionChange(() =>
        {
            ImageGridView.SelectedItems.Clear();
            ImageGridView.SelectedItem = dragImage;
        });

        return ViewModel.ExpandSelectionImages(new[] { dragImage });
    }

    private IEnumerable<ImageFileInfo> GetOrderedSelectedImagesForDrag()
    {
        var selectedImages = ViewModel.ExpandSelectionImages(
            ImageGridView.SelectedItems.OfType<ImageFileInfo>());
        if (selectedImages.Count == 0)
            return Array.Empty<ImageFileInfo>();

        return selectedImages;
    }

    private void ImageGridView_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        SyncSelectedStateFromGridView(e);
    }

    private void SyncSelectedStateFromGridView(Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        var semanticSelectedImages = ViewModel.ExpandSelectionImages(
            ImageGridView.SelectedItems.OfType<ImageFileInfo>());
        var semanticSelectedSet = semanticSelectedImages.ToHashSet();

        foreach (var removedItem in _selectedImageState
                     .Where(image => !semanticSelectedSet.Contains(image))
                     .ToList())
        {
            if (_selectedImageState.Remove(removedItem))
            {
                removedItem.IsSelected = false;
            }
        }

        foreach (var addedItem in semanticSelectedImages)
        {
            if (_selectedImageState.Add(addedItem))
            {
                addedItem.IsSelected = true;
            }
        }

        ViewModel.UpdateSelectedCount(_selectedImageState.Count);
    }

    private void ClearSelectedImageState()
    {
        foreach (var imageInfo in _selectedImageState.ToArray())
        {
            imageInfo.IsSelected = false;
        }

        _selectedImageState.Clear();
        ViewModel.UpdateSelectedCount(0);
    }

    private void ClearGridViewSelection()
    {
        if (ImageGridView.SelectedItems.Count == 0)
            return;

        ExecuteProgrammaticSelectionChange(() => ImageGridView.SelectedItem = null);
    }

    private void ImageItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ImageFileInfo })
        {
            AutoCollapseImageBrowsingChrome("image-tapped");
        }
    }

    private async void ImageGridView_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        var clickedImage = FindImageInfoFromOriginalSource(e.OriginalSource);
        var targetImage = clickedImage ?? GetCurrentImageForViewerOrSelection();
        var targetGroup = targetImage?.BurstGroup;
        var anchorTop = targetImage != null ? TryGetItemTopRelativeToScrollViewer(targetImage) : null;

        if (!ViewModel.CollapseExpandedBurstGroupsExcept(clickedImage?.BurstGroup))
            return;

        if (targetImage != null && !ViewModel.Images.Contains(targetImage) && targetGroup != null)
        {
            targetImage = targetGroup.Images.FirstOrDefault(ViewModel.Images.Contains);
        }

        if (targetImage == null || !ViewModel.Images.Contains(targetImage))
            return;

        await RestoreGridFocusAfterMouseCollapseAsync(targetImage, anchorTop);
    }

    private async Task RestoreGridFocusAfterMouseCollapseAsync(ImageFileInfo imageInfo, double? previousTop)
    {
        await Task.Yield();

        if (_isUnloaded || AppLifetime.IsShuttingDown || !ViewModel.Images.Contains(imageInfo))
            return;

        ExecuteProgrammaticSelectionChange(() =>
        {
            ImageGridView.SelectedItems.Clear();
            ImageGridView.SelectedItem = imageInfo;
        });

        if (previousTop.HasValue)
        {
            RestoreItemTopRelativeToScrollViewer(imageInfo, previousTop);
            await Task.Yield();
        }

        await ScrollItemIntoViewAsync(imageInfo, "tap-collapse-burst", ScrollIntoViewAlignment.Default);
        await Task.Yield();

        if (ImageGridView.ContainerFromItem(imageInfo) is GridViewItem container)
        {
            container.Focus(FocusState.Programmatic);
        }
        else
        {
            ImageGridView.ScrollIntoView(imageInfo, ScrollIntoViewAlignment.Default);
        }

        QueueVisibleThumbnailLoad(imageInfo, "tap-collapse-burst");
    }

    private static ImageFileInfo? FindImageInfoFromOriginalSource(object originalSource)
    {
        var current = originalSource as DependencyObject;
        while (current != null)
        {
            if (current is FrameworkElement { Tag: ImageFileInfo tagImage })
                return tagImage;

            if (current is FrameworkElement { DataContext: ImageFileInfo contextImage })
                return contextImage;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private async void BurstToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        var imageInfo = element.DataContext as ImageFileInfo;
        if (imageInfo == null)
            return;

        await ToggleBurstExpansionAsync(imageInfo);
    }

    private async Task ToggleBurstExpansionAsync(ImageFileInfo imageInfo)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown || imageInfo.BurstGroup == null)
            return;

        AttachImageGridScrollViewer();
        var anchorTop = TryGetItemTopRelativeToScrollViewer(imageInfo);
        var warmupImages = ViewModel.GetBurstExpansionWarmupImages(imageInfo);
        foreach (var warmupImage in warmupImages.Take(TargetThumbnailStartBudgetPerTick))
        {
            _pendingFastPreviewLoads.Remove(warmupImage);
            await warmupImage.EnsureFastPreviewAsync(ViewModel.ThumbnailSize);
        }

        ViewModel.ToggleBurstExpansion(imageInfo);
        QueueVisibleThumbnailLoad("burst-toggle");
        var restoreImage = imageInfo;
        if (!ViewModel.Images.Contains(restoreImage) && imageInfo.BurstGroup != null)
        {
            restoreImage = imageInfo.BurstGroup.Images.FirstOrDefault(ViewModel.Images.Contains) ?? imageInfo;
        }

        if (ViewModel.Images.Contains(restoreImage))
        {
            ExecuteProgrammaticSelectionChange(() =>
            {
                ImageGridView.SelectedItems.Clear();
                ImageGridView.SelectedItem = restoreImage;
            });
        }

        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Yield();
            RestoreItemTopRelativeToScrollViewer(restoreImage, anchorTop);
            await Task.Yield();
            FocusImageGridItemIfRealized(restoreImage);
        });
    }

    private void FocusImageGridItemIfRealized(ImageFileInfo imageInfo)
    {
        if (!ViewModel.Images.Contains(imageInfo))
            return;

        if (ImageGridView.ContainerFromItem(imageInfo) is GridViewItem container)
        {
            container.Focus(FocusState.Programmatic);
        }
    }

    private double? TryGetItemTopRelativeToScrollViewer(ImageFileInfo imageInfo)
    {
        if (_imageGridScrollViewer == null ||
            ImageGridView.ContainerFromItem(imageInfo) is not FrameworkElement container)
        {
            return null;
        }

        try
        {
            return container
                .TransformToVisual(_imageGridScrollViewer)
                .TransformPoint(new Windows.Foundation.Point(0, 0))
                .Y;
        }
        catch
        {
            return null;
        }
    }

    private void RestoreItemTopRelativeToScrollViewer(ImageFileInfo imageInfo, double? previousTop)
    {
        if (!previousTop.HasValue ||
            _isUnloaded ||
            AppLifetime.IsShuttingDown ||
            _imageGridScrollViewer == null)
        {
            return;
        }

        var currentTop = TryGetItemTopRelativeToScrollViewer(imageInfo);
        if (!currentTop.HasValue)
            return;

        var targetOffset = _imageGridScrollViewer.VerticalOffset + currentTop.Value - previousTop.Value;
        targetOffset = Math.Clamp(targetOffset, 0d, _imageGridScrollViewer.ScrollableHeight);

        _isProgrammaticScrollActive = true;
        try
        {
            _imageGridScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
        }
        finally
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _isProgrammaticScrollActive = false;
                QueueVisibleThumbnailLoad("burst-anchor-restore");
            });
        }
    }

    private void ImageItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressNextImageDragStart = IsWithinRatingControl(e.OriginalSource as DependencyObject);
    }

    private static bool IsWithinRatingControl(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is RatingControl)
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }
}
