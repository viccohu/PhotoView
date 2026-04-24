using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Helpers;
using PhotoView.Models;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;

namespace PhotoView.Views;

public sealed partial class MainPage
{
    private async void ImageGridView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled)
            return;

        if (_currentViewer != null || !TryGetDirectionalNavigationDelta(e.Key, out var direction))
        {
            return;
        }

        e.Handled = true;
        if (_isHandlingDirectionalBurstNavigation)
            return;

        if (!ShouldProcessDirectionalNavigation(e.KeyStatus.WasKeyDown, direction, DirectionalNavigationScopeGrid))
            return;

        await NavigateImageGridDirectionAsync(e.Key, direction);
    }

    private async Task NavigateImageGridDirectionAsync(VirtualKey key, int direction)
    {
        var currentImage = GetCurrentImageForViewerOrSelection();
        if (currentImage == null)
            return;

        _isHandlingDirectionalBurstNavigation = true;
        try
        {
            if (_settingsService.CollapseBurstGroups &&
                _settingsService.AutoExpandBurstOnDirectionalNavigation &&
                currentImage.BurstGroup is { Images.Count: > 1, IsExpanded: false })
            {
                var expandedTarget = ViewModel.ExpandBurstForDirectionalNavigation(currentImage, direction);
                if (expandedTarget != null && ViewModel.Images.Contains(expandedTarget))
                {
                    await SelectImageForViewerAsync(expandedTarget, "grid-key-current-burst", focusThumbnail: true);
                }

                return;
            }

            var startExpandedGroup = _settingsService.CollapseBurstGroups &&
                _settingsService.AutoExpandBurstOnDirectionalNavigation &&
                currentImage.BurstGroup?.IsExpanded == true
                    ? currentImage.BurstGroup
                    : null;
            var targetImage = GetGridDirectionalNavigationTarget(currentImage, key, direction);
            if (targetImage == null || ReferenceEquals(targetImage, currentImage))
                return;

            if (_settingsService.CollapseBurstGroups &&
                _settingsService.AutoExpandBurstOnDirectionalNavigation &&
                targetImage.BurstGroup is { Images.Count: > 1, IsExpanded: false })
            {
                targetImage = ViewModel.ExpandBurstForDirectionalNavigation(targetImage, direction) ?? targetImage;
            }

            if (ViewModel.Images.Contains(targetImage))
            {
                await SelectImageForViewerAsync(targetImage, "grid-key-directional", focusThumbnail: true);
            }

            if (startExpandedGroup != null && !ReferenceEquals(targetImage.BurstGroup, startExpandedGroup))
            {
                ViewModel.CollapseBurstGroup(startExpandedGroup);
                if (ViewModel.Images.Contains(targetImage))
                {
                    await SelectImageForViewerAsync(targetImage, "grid-key-leave-burst", focusThumbnail: true);
                }
            }
        }
        finally
        {
            _isHandlingDirectionalBurstNavigation = false;
        }
    }

    private ImageFileInfo? GetGridDirectionalNavigationTarget(ImageFileInfo currentImage, VirtualKey key, int direction)
    {
        var currentIndex = ViewModel.Images.IndexOf(currentImage);
        if (currentIndex < 0)
            return null;

        var itemDelta = direction;
        if (key == VirtualKey.Up || key == VirtualKey.Down)
        {
            itemDelta *= GetImageGridColumnCount();
        }

        var targetIndex = Math.Clamp(currentIndex + itemDelta, 0, ViewModel.Images.Count - 1);
        return targetIndex == currentIndex ? currentImage : ViewModel.Images[targetIndex];
    }

    private int GetImageGridColumnCount()
    {
        var availableWidth = ImageGridView.ActualWidth - ImageGridView.Padding.Left - ImageGridView.Padding.Right;
        if (availableWidth <= 0)
            return 1;

        var targetTileSize = GetTargetTileSize(ViewModel.ThumbnailSize);
        var minimumTileSize = GetMinimumTileSize(ViewModel.ThumbnailSize);
        var maximumTileSize = GetMaximumTileSize(ViewModel.ThumbnailSize);
        var columnCount = Math.Max(1, (int)Math.Floor((availableWidth + GridViewItemGap) / (targetTileSize + GridViewItemGap)));
        var tileSize = CalculateTileSize(availableWidth, columnCount);

        while (columnCount > 1 && tileSize < minimumTileSize)
        {
            columnCount--;
            tileSize = CalculateTileSize(availableWidth, columnCount);
        }

        while (tileSize > maximumTileSize)
        {
            columnCount++;
            tileSize = CalculateTileSize(availableWidth, columnCount);
        }

        return Math.Max(1, columnCount);
    }

    private bool HandleShortcut(KeyRoutedEventArgs e)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return false;

        if (_currentViewer != null)
        {
            return HandleViewerShortcut(e);
        }

        return HandleMainPageShortcut(e);
    }

    private bool HandleViewerShortcut(KeyRoutedEventArgs e)
    {
        var viewer = _currentViewer;
        if (viewer == null)
            return false;

        if (e.Key == VirtualKey.Space || e.Key == VirtualKey.Escape)
        {
            viewer.PrepareCloseAnimation();
            return true;
        }
        else if (TryGetDirectionalNavigationDelta(e.Key, out var direction))
        {
            if (ShouldProcessDirectionalNavigation(e.KeyStatus.WasKeyDown, direction, DirectionalNavigationScopeViewer))
            {
                _ = SwitchViewerImageAsync(direction);
            }

            return true;
        }
        else if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number5)
        {
            viewer.HandleRatingKey(e.Key);
            return true;
        }
        else if (e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad5)
        {
            viewer.HandleRatingKey(e.Key - (VirtualKey.NumberPad0 - VirtualKey.Number0));
            return true;
        }

        return false;
    }

    private bool HandleMainPageShortcut(KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space)
        {
            _ = ToggleImageViewerForCurrentSelectionAsync();
            return true;
        }
        else if (e.Key == VirtualKey.A)
        {
            var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (isCtrlPressed)
            {
                ImageGridView.SelectAll();
                return true;
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            ClearGridViewSelection();
            return true;
        }
        else if (e.Key == VirtualKey.Delete)
        {
            TogglePendingDeleteForSelectedItems();
            return true;
        }
        else if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number5)
        {
            HandleRatingShortcut(e.Key);
            return true;
        }
        else if (e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad5)
        {
            HandleRatingShortcut(e.Key - (VirtualKey.NumberPad0 - VirtualKey.Number0));
            return true;
        }
        else if (TryGetDirectionalNavigationDelta(e.Key, out var direction))
        {
            if (ShouldProcessDirectionalNavigation(e.KeyStatus.WasKeyDown, direction, DirectionalNavigationScopeGrid))
            {
                _ = NavigateImageGridDirectionAsync(e.Key, direction);
            }

            return true;
        }

        return false;
    }

    private static bool TryGetDirectionalNavigationDelta(VirtualKey key, out int direction)
    {
        if (key == VirtualKey.Left || key == VirtualKey.Up)
        {
            direction = -1;
            return true;
        }

        if (key == VirtualKey.Right || key == VirtualKey.Down)
        {
            direction = 1;
            return true;
        }

        direction = 0;
        return false;
    }

    private bool IsKeyboardFocusWithin(DependencyObject ancestor)
    {
        var focusedElement = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focusedElement != null)
        {
            if (ReferenceEquals(focusedElement, ancestor))
                return true;

            focusedElement = VisualTreeHelper.GetParent(focusedElement);
        }

        return false;
    }

    private bool ShouldProcessDirectionalNavigation(bool wasKeyDown, int direction, int scope)
    {
        var now = Environment.TickCount64;
        if (!wasKeyDown ||
            _lastDirectionalNavigationScope != scope ||
            _lastDirectionalNavigationDirection != direction ||
            now - _lastDirectionalNavigationTick >= DirectionalNavigationRepeatIntervalMs)
        {
            _lastDirectionalNavigationScope = scope;
            _lastDirectionalNavigationDirection = direction;
            _lastDirectionalNavigationTick = now;
            return true;
        }

        return false;
    }

    private void HandleRatingShortcut(VirtualKey key)
    {
        var selectedImages = ImageGridView.SelectedItems
            .OfType<ImageFileInfo>()
            .ToList();

        if (selectedImages.Count == 0)
            return;

        int stars = key - VirtualKey.Number0;

        var allImagesToProcess = new List<ImageFileInfo>();

        foreach (var imageInfo in selectedImages)
        {
            if (imageInfo.Group != null)
            {
                foreach (var groupImage in imageInfo.Group.Images)
                {
                    if (!allImagesToProcess.Contains(groupImage))
                    {
                        allImagesToProcess.Add(groupImage);
                    }
                }
            }
            else
            {
                if (!allImagesToProcess.Contains(imageInfo))
                {
                    allImagesToProcess.Add(imageInfo);
                }
            }
        }

        foreach (var imageInfo in allImagesToProcess)
        {
            uint newRating;

            if (stars == 0)
            {
                newRating = 0;
            }
            else
            {
                int currentStars = ImageFileInfo.RatingToStars(imageInfo.Rating);

                if (currentStars == stars)
                {
                    newRating = 0;
                }
                else
                {
                    newRating = ImageFileInfo.StarsToRating(stars);
                }
            }

            _ = UpdateRatingAsync(imageInfo, newRating);
        }
    }

    private void TogglePendingDeleteForSelectedItems()
    {
        if (ImageGridView.SelectedItems.Count == 0)
            return;

        var selectedImages = ImageGridView.SelectedItems
            .OfType<ImageFileInfo>()
            .ToList();

        if (selectedImages.Count > 0)
        {
            ViewModel.TogglePendingDeleteForSelected(selectedImages);
        }
    }
}

