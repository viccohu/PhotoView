using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PhotoView.Contracts.Services;
using PhotoView.Dialogs;
using PhotoView.Models;
using PhotoView.Services;
using PhotoView.ViewModels;
using System.Collections.Specialized;
using System.IO;
using Windows.Storage;
using Windows.System;

namespace PhotoView.Views;

public sealed partial class CollectPage : Page
{
    private const int VisibleThumbnailStartBudgetPerTick = 16;
    private const int VisibleThumbnailPrefetchItemCount = 12;
    private readonly DispatcherTimer _visibleThumbnailLoadTimer;
    private readonly HashSet<ImageFileInfo> _pendingVisibleThumbnailLoads = new();
    private readonly HashSet<ImageFileInfo> _realizedImageItems = new();
    private readonly ISettingsService _settingsService;
    private readonly ShellToolbarService _shellToolbarService;
    private FolderNode? _rightClickedFolderNode;
    private ItemsStackPanel? _thumbnailItemsPanel;
    private ScrollViewer? _thumbnailScrollViewer;
    private PointerEventHandler? _thumbnailWheelHandler;
    private bool _isUnloaded;
    private bool _isDisposed;
    private bool _isUpdatingRatingControl;
    private bool _isUpdatingZoomSlider;
    private Button? _shellDeleteButton;
    private SplitButton? _shellFilterSplitButton;

    public CollectViewModel ViewModel
    {
        get;
    }

    public CollectPage()
    {
        ViewModel = App.GetService<CollectViewModel>();
        _settingsService = App.GetService<ISettingsService>();
        _shellToolbarService = App.GetService<ShellToolbarService>();
        NavigationCacheMode = NavigationCacheMode.Disabled;
        InitializeComponent();
        DataContext = ViewModel;
        FilterFlyoutControl.FilterViewModel = ViewModel.Filter;
        _visibleThumbnailLoadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _visibleThumbnailLoadTimer.Tick += VisibleThumbnailLoadTimer_Tick;
        ViewModel.Images.CollectionChanged += Images_CollectionChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Filter.FilterChanged += Filter_FilterChanged;
        PreviewCanvas.ZoomPercentChanged += PreviewCanvas_ZoomPercentChanged;
        _thumbnailWheelHandler = PreviewThumbnailGridView_PointerWheelChanged;
        PreviewThumbnailGridView.AddHandler(UIElement.PointerWheelChangedEvent, _thumbnailWheelHandler, true);
        Loaded += CollectPage_Loaded;
        Unloaded += CollectPage_Unloaded;
        KeyDown += CollectPage_KeyDown;
    }

    private void CollectPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isDisposed)
            return;

        _isUnloaded = false;
        RegisterShellToolbar();
        UpdateSelectedImageUi();
        QueueVisibleThumbnailLoad("page-loaded");
    }

    private void CollectPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _visibleThumbnailLoadTimer.Stop();
        _pendingVisibleThumbnailLoads.Clear();
        _realizedImageItems.Clear();
        _thumbnailItemsPanel = null;
        _thumbnailScrollViewer = null;
        DisposePageSubscriptions();
    }

    private void DisposePageSubscriptions()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _shellToolbarService.ClearToolbar(this);
        _shellDeleteButton = null;
        _shellFilterSplitButton = null;
        _visibleThumbnailLoadTimer.Tick -= VisibleThumbnailLoadTimer_Tick;
        ViewModel.Images.CollectionChanged -= Images_CollectionChanged;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Filter.FilterChanged -= Filter_FilterChanged;
        Bindings.StopTracking();
        PreviewCanvas.ZoomPercentChanged -= PreviewCanvas_ZoomPercentChanged;
        if (_thumbnailWheelHandler != null)
        {
            PreviewThumbnailGridView.RemoveHandler(UIElement.PointerWheelChangedEvent, _thumbnailWheelHandler);
            _thumbnailWheelHandler = null;
        }
        Loaded -= CollectPage_Loaded;
        Unloaded -= CollectPage_Unloaded;
        KeyDown -= CollectPage_KeyDown;
    }

    private async void LoadPreview_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadPreviewAsync();
        QueueVisibleThumbnailLoad("load-preview");
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: PreviewSource source })
        {
            ViewModel.RemoveSource(source);
        }
    }

    private async void PreviewFolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            await ViewModel.LoadChildrenAsync(node);
        }
    }

    private async void PreviewFolderTreeItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TryGetFolderNode(sender, out var node))
        {
            ViewModel.AddSource(node);
            e.Handled = true;
        }
    }

    private void PreviewFolderTreeItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (TryGetFolderNode(sender, out var node))
        {
            _rightClickedFolderNode = node;
            if (sender is TreeViewItem { Content: Grid grid })
            {
                FlyoutBase.ShowAttachedFlyout(grid);
            }
            e.Handled = true;
        }
    }

    private void AddFolderToPreviewSource_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AddSource(_rightClickedFolderNode);
    }

    private static bool TryGetFolderNode(object sender, out FolderNode? node)
    {
        node = null;
        if (sender is TreeViewItem { Content: Grid { Tag: FolderNode taggedNode } })
        {
            node = taggedNode;
            return true;
        }
        if (sender is FrameworkElement { DataContext: FolderNode dataNode })
        {
            node = dataNode;
            return true;
        }
        return false;
    }

    private void PreviewThumbnailGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ImageFileInfo imageInfo)
        {
            ViewModel.SelectedImage = imageInfo;
            PreviewThumbnailGridView.SelectedItem = imageInfo;
        }
    }

    private void PreviewThumbnailGridView_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (PreviewThumbnailGridView.SelectedItem is ImageFileInfo imageInfo)
        {
            ViewModel.SelectedImage = imageInfo;
        }
    }

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

    private void PreviewThumbnailGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not ImageFileInfo imageInfo)
            return;

        if (args.InRecycleQueue)
        {
            imageInfo.CancelThumbnailLoad();
            _pendingVisibleThumbnailLoads.Remove(imageInfo);
            _realizedImageItems.Remove(imageInfo);
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

        var candidates = _pendingVisibleThumbnailLoads
            .Select(imageInfo => new
            {
                ImageInfo = imageInfo,
                Index = ViewModel.Images.IndexOf(imageInfo)
            })
            .Where(candidate => candidate.Index >= 0)
            .OrderBy(candidate => candidate.Index)
            .Select(candidate => candidate.ImageInfo)
            .ToArray();
        _pendingVisibleThumbnailLoads.Clear();

        var started = 0;
        foreach (var imageInfo in candidates)
        {
            if (!_realizedImageItems.Contains(imageInfo))
                continue;

            if (started >= VisibleThumbnailStartBudgetPerTick)
            {
                _pendingVisibleThumbnailLoads.Add(imageInfo);
                continue;
            }

            _ = imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
            started++;
        }

        if (_pendingVisibleThumbnailLoads.Count > 0)
        {
            _visibleThumbnailLoadTimer.Start();
        }
    }

    private void QueueVisibleThumbnailLoad(string reason)
    {
        if (_isUnloaded)
            return;

        AttachThumbnailItemsPanel();

        if (_thumbnailItemsPanel != null &&
            _thumbnailItemsPanel.FirstVisibleIndex >= 0 &&
            _thumbnailItemsPanel.LastVisibleIndex >= _thumbnailItemsPanel.FirstVisibleIndex)
        {
            var firstIndex = Math.Max(0, _thumbnailItemsPanel.FirstVisibleIndex - VisibleThumbnailPrefetchItemCount);
            var lastIndex = Math.Min(PreviewThumbnailGridView.Items.Count - 1, _thumbnailItemsPanel.LastVisibleIndex + VisibleThumbnailPrefetchItemCount);

            for (var index = firstIndex; index <= lastIndex; index++)
            {
                if (PreviewThumbnailGridView.Items[index] is ImageFileInfo imageInfo)
                {
                    _pendingVisibleThumbnailLoads.Add(imageInfo);
                }
            }
        }
        else
        {
            foreach (var imageInfo in _realizedImageItems
                         .Select(imageInfo => new { ImageInfo = imageInfo, Index = ViewModel.Images.IndexOf(imageInfo) })
                         .Where(candidate => candidate.Index >= 0)
                         .OrderBy(candidate => candidate.Index)
                         .Take(VisibleThumbnailStartBudgetPerTick + VisibleThumbnailPrefetchItemCount)
                         .Select(candidate => candidate.ImageInfo))
            {
                _pendingVisibleThumbnailLoads.Add(imageInfo);
            }
        }

        if (_pendingVisibleThumbnailLoads.Count > 0)
        {
            _visibleThumbnailLoadTimer.Stop();
            _visibleThumbnailLoadTimer.Start();
        }
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
        }
        QueueVisibleThumbnailLoad("images-changed");
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CollectViewModel.SelectedImage))
        {
            UpdateSelectedImageUi();
        }
        else if (e.PropertyName == nameof(CollectViewModel.IsThumbnailStripCollapsed))
        {
            ThumbnailStripHost.Height = ViewModel.IsThumbnailStripCollapsed ? 64 : 160;
        }
        else if (e.PropertyName == nameof(CollectViewModel.PendingDeleteCount))
        {
            UpdateShellToolbarState();
        }
    }

    private void RegisterShellToolbar()
    {
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        var exportButton = CreateToolbarButton("\uE72D", "导出");
        exportButton.Click += ExportButton_Click;
        toolbar.Children.Add(exportButton);

        _shellDeleteButton = CreateToolbarButton("\uE74D", "删除");
        _shellDeleteButton.Click += DeleteButton_Click;
        toolbar.Children.Add(_shellDeleteButton);

        _shellFilterSplitButton = new SplitButton
        {
            Padding = new Thickness(8),
            Content = CreateToolbarIcon("\uE71C"),
            Flyout = CreateFilterFlyout()
        };
        _shellFilterSplitButton.Click += FilterSplitButton_Click;
        toolbar.Children.Add(_shellFilterSplitButton);

        UpdateShellToolbarState();
        UpdateFilterButtonState();
        _shellToolbarService.SetToolbar(this, toolbar);
    }

    private void UpdateShellToolbarState()
    {
        if (_shellDeleteButton != null)
        {
            _shellDeleteButton.IsEnabled = ViewModel.PendingDeleteCount > 0;
        }
    }

    private static Button CreateToolbarButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Padding = new Thickness(8),
            Content = CreateToolbarIcon(glyph)
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static FontIcon CreateToolbarIcon(string glyph)
    {
        return new FontIcon
        {
            Glyph = glyph,
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            FontSize = 16
        };
    }

    private Flyout CreateFilterFlyout()
    {
        return new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
            Content = new FilterFlyout
            {
                FilterViewModel = ViewModel.Filter
            }
        };
    }

    private void Filter_FilterChanged(object? sender, EventArgs e)
    {
        UpdateFilterButtonState();
    }

    private void UpdateFilterButtonState()
    {
        if (ViewModel.Filter.IsFilterActive)
        {
            var activeColor = Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
            FilterSplitButton.Background = new SolidColorBrush(activeColor);
            if (_shellFilterSplitButton != null)
            {
                _shellFilterSplitButton.Background = new SolidColorBrush(activeColor);
            }
        }
        else
        {
            FilterSplitButton.ClearValue(Control.BackgroundProperty);
            _shellFilterSplitButton?.ClearValue(Control.BackgroundProperty);
        }
    }

    private void UpdateSelectedImageUi()
    {
        PreviewInfoDrawer.DataContext = ViewModel.SelectedImage;
        _isUpdatingRatingControl = true;
        PreviewRatingControl.Value = ViewModel.SelectedImage?.RatingValue ?? -1;
        _isUpdatingRatingControl = false;
    }

    private async void PreviewRatingControl_ValueChanged(RatingControl sender, object args)
    {
        if (_isUpdatingRatingControl || ViewModel.SelectedImage == null)
            return;

        var stars = sender.Value <= 0 ? 0 : (int)Math.Round(sender.Value, MidpointRounding.AwayFromZero);
        var rating = ImageFileInfo.StarsToRating(Math.Clamp(stars, 0, 5));
        await ViewModel.SelectedImage.SetRatingAsync(App.GetService<RatingService>(), rating);
    }

    private void PreviewCanvas_ZoomPercentChanged(object? sender, double percent)
    {
        _isUpdatingZoomSlider = true;
        ZoomSlider.Value = Math.Clamp(percent, ZoomSlider.Minimum, ZoomSlider.Maximum);
        ZoomValueButton.Content = $"{percent:F0}%";
        _isUpdatingZoomSlider = false;
    }

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingZoomSlider)
            return;

        PreviewCanvas.SetZoomPercent(e.NewValue);
        ZoomValueButton.Content = $"{e.NewValue:F0}%";
    }

    private void ZoomValueButton_Click(object sender, RoutedEventArgs e)
    {
        PreviewCanvas.SetZoomPercent(100);
    }

    private void RotatePreview_Click(object sender, RoutedEventArgs e)
    {
        PreviewCanvas.RotateClockwise();
    }

    private void FlipPreviewHorizontal_Click(object sender, RoutedEventArgs e)
    {
        PreviewCanvas.FlipHorizontal();
    }

    private void FlipPreviewVertical_Click(object sender, RoutedEventArgs e)
    {
        PreviewCanvas.FlipVertical();
    }

    private void ToggleThumbnailStrip_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsThumbnailStripCollapsed = !ViewModel.IsThumbnailStripCollapsed;
    }

    private void ToggleInfoDrawer_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsInfoDrawerOpen = !ViewModel.IsInfoDrawerOpen;
    }

    private void FilterSplitButton_Click(SplitButton sender, SplitButtonClickEventArgs args)
    {
        if (ViewModel.Filter.IsFilterActive)
        {
            ViewModel.Filter.Reset();
        }
        else
        {
            sender.Flyout?.ShowAt(sender);
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportDialog(_settingsService, ViewModel.Images.ToList())
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var pendingImages = ViewModel.GetPendingDeleteImages();
        if (pendingImages.Count == 0)
            return;

        var allExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in pendingImages)
        {
            allExtensions.Add(Path.GetExtension(image.ImageFile.Path).ToLowerInvariant());
            if (image.AlternateFormats != null)
            {
                foreach (var alternate in image.AlternateFormats)
                {
                    allExtensions.Add(Path.GetExtension(alternate.ImageFile.Path).ToLowerInvariant());
                }
            }
        }

        var dialog = new DeleteConfirmDialog(allExtensions.Where(ext => !string.IsNullOrWhiteSpace(ext)).ToList(), pendingImages.Count)
        {
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var filesToDelete = GetFilesToDelete(pendingImages, dialog.SelectedExtensions);
        if (filesToDelete.Count == 0)
            return;

        dialog.StartProgress();
        var deletedImages = new List<ImageFileInfo>();
        var thumbnailService = App.GetService<IThumbnailService>();

        for (var index = 0; index < filesToDelete.Count; index++)
        {
            var file = filesToDelete[index];
            try
            {
                await DeleteFileToRecycleBinAsync(file);
                thumbnailService.Invalidate(file);
                var image = pendingImages.FirstOrDefault(candidate => candidate.ImageFile.Path == file.Path);
                if (image != null && !deletedImages.Contains(image))
                {
                    deletedImages.Add(image);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectPage] delete failed {file.Path}: {ex.Message}");
            }

            dialog.SetProgress(index + 1, filesToDelete.Count);
            await Task.Delay(10);
        }

        dialog.SetComplete();
        await Task.Delay(350);
        ViewModel.RemoveDeletedImages(deletedImages);
        QueueVisibleThumbnailLoad("delete");
    }

    private static List<StorageFile> GetFilesToDelete(List<ImageFileInfo> pendingImages, List<string> selectedExtensions)
    {
        var files = new List<StorageFile>();
        foreach (var image in pendingImages)
        {
            var extension = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
            if (selectedExtensions.Contains(extension))
            {
                files.Add(image.ImageFile);
            }

            if (image.AlternateFormats != null)
            {
                foreach (var alternate in image.AlternateFormats)
                {
                    var alternateExtension = Path.GetExtension(alternate.ImageFile.Path).ToLowerInvariant();
                    if (selectedExtensions.Contains(alternateExtension))
                    {
                        files.Add(alternate.ImageFile);
                    }
                }
            }
        }

        return files.DistinctBy(file => file.Path).ToList();
    }

    private static async Task DeleteFileToRecycleBinAsync(StorageFile file)
    {
        var settingsService = App.GetService<ISettingsService>();
        var driveRoot = Path.GetPathRoot(file.Path);
        var removable = !string.IsNullOrWhiteSpace(driveRoot) &&
            DriveInfo.GetDrives().Any(drive =>
                string.Equals(drive.Name, driveRoot, StringComparison.OrdinalIgnoreCase) &&
                drive.DriveType == DriveType.Removable);

        var deleteOption = removable || !settingsService.DeleteToRecycleBin
            ? StorageDeleteOption.PermanentDelete
            : StorageDeleteOption.Default;
        await file.DeleteAsync(deleteOption);
    }

    private void CollectPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Delete)
        {
            ViewModel.TogglePendingDeleteForSelected(PreviewThumbnailGridView.SelectedItems.OfType<ImageFileInfo>());
            e.Handled = true;
        }
        else if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number5)
        {
            var stars = e.Key - VirtualKey.Number0;
            var rating = ImageFileInfo.StarsToRating(stars);
            foreach (var image in PreviewThumbnailGridView.SelectedItems.OfType<ImageFileInfo>())
            {
                _ = image.SetRatingAsync(App.GetService<RatingService>(), rating);
            }
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Left || e.Key == VirtualKey.Right)
        {
            MoveSelection(e.Key == VirtualKey.Right ? 1 : -1);
            e.Handled = true;
        }
    }

    private void MoveSelection(int delta)
    {
        if (ViewModel.Images.Count == 0)
            return;

        var currentIndex = ViewModel.SelectedImage != null
            ? ViewModel.Images.IndexOf(ViewModel.SelectedImage)
            : -1;
        var nextIndex = Math.Clamp(currentIndex + delta, 0, ViewModel.Images.Count - 1);
        var nextImage = ViewModel.Images[nextIndex];
        ViewModel.SelectedImage = nextImage;
        PreviewThumbnailGridView.SelectedItem = nextImage;
        PreviewThumbnailGridView.ScrollIntoView(nextImage);
    }
}
