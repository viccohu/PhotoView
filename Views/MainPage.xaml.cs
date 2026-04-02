using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Helpers;
using PhotoView.Models;
using PhotoView.ViewModels;
using System.Linq;
using Windows.System;

namespace PhotoView.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    private readonly DispatcherTimer _loadImagesThrottleTimer;
    private FolderNode? _pendingLoadNode;
    private ScrollView? _imageScrollView;
    private bool _isUnloaded;
    private bool _isUpdatingSelectionState;
    private bool _isThumbnailBatchRunning;
    private bool _hasPendingThumbnailBatch;
    private DateTime _lastClickTime;
    private FolderNode? _lastClickedNode;

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        InitializeComponent();
        FolderTreeView.DataContext = ViewModel;

        _loadImagesThrottleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _loadImagesThrottleTimer.Tick += LoadImagesThrottleTimer_Tick;

        ViewModel.ImagesChanged += ViewModel_ImagesChanged;
        ViewModel.ThumbnailSizeChanged += ViewModel_ThumbnailSizeChanged;
        Loaded += MainPage_Loaded;
        KeyDown += MainPage_KeyDown;
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        AttachItemsViewScrollView();
        QueueVisibleThumbnailLoad();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _loadImagesThrottleTimer.Stop();

        if (_imageScrollView != null)
        {
            _imageScrollView.ViewChanged -= ImageScrollView_ViewChanged;
            _imageScrollView = null;
        }
    }

    private async void LoadImagesThrottleTimer_Tick(object? sender, object e)
    {
        _loadImagesThrottleTimer.Stop();

        if (_pendingLoadNode == null || _isUnloaded || AppLifetime.IsShuttingDown)
        {
            return;
        }

        var node = _pendingLoadNode;
        _pendingLoadNode = null;

        try
        {
            await ViewModel.LoadImagesAsync(node);
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
        }
    }

    private void ViewModel_ThumbnailSizeChanged(object? sender, EventArgs e)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
        {
            return;
        }

        ImageLinedFlowLayout.InvalidateItemsInfo();
        QueueVisibleThumbnailLoad();
    }

    private void ThumbnailSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string sizeString)
        {
            if (Enum.TryParse<ThumbnailSize>(sizeString, out var size))
            {
                ViewModel.ThumbnailSize = size;
            }
        }
    }

    private async void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            FolderTreeView.SelectedItem = node;
            await ViewModel.LoadChildrenAsync(node);
            if (_isUnloaded || AppLifetime.IsShuttingDown)
            {
                return;
            }

            ThrottleLoadImages(node);
        }
    }

    private void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNode node)
        {
            var now = DateTime.Now;
            var isDoubleClick = (now - _lastClickTime).TotalMilliseconds < 300
                && _lastClickedNode == node;
            _lastClickTime = now;
            _lastClickedNode = node;

            if (FolderTreeView.SelectedItem == node || isDoubleClick)
            {
                var treeViewItem = sender.ContainerFromItem(node) as TreeViewItem;
                if (treeViewItem != null)
                {
                    treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
                }
            }

            ThrottleLoadImages(node);
        }
    }

    private void ThrottleLoadImages(FolderNode node)
    {
        if (IsCurrentDisplayedNode(node) || _pendingLoadNode == node)
        {
            return;
        }

        _pendingLoadNode = node;
        _loadImagesThrottleTimer.Stop();
        _loadImagesThrottleTimer.Start();
    }

    private bool IsCurrentDisplayedNode(FolderNode node)
    {
        var selectedFolder = ViewModel.SelectedFolder;
        if (selectedFolder == null)
        {
            return false;
        }

        if (ReferenceEquals(selectedFolder, node))
        {
            return true;
        }

        return selectedFolder.NodeType == node.NodeType
            && !string.IsNullOrEmpty(selectedFolder.FullPath)
            && string.Equals(selectedFolder.FullPath, node.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private async void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            await ExpandTreeViewPathAsync(node);
            if (_isUnloaded || AppLifetime.IsShuttingDown)
            {
                return;
            }

            ThrottleLoadImages(node);
        }
    }

    private async System.Threading.Tasks.Task ExpandTreeViewPathAsync(FolderNode targetNode)
    {
        try
        {
            if (_isUnloaded || AppLifetime.IsShuttingDown)
            {
                return;
            }

            var path = new List<FolderNode>();
            var current = targetNode;
            while (current != null)
            {
                path.Insert(0, current);
                current = current.Parent;
            }

            if (path.Count == 0)
            {
                return;
            }

            FolderNode? lastNode = null;
            for (var i = 0; i < path.Count; i++)
            {
                var node = path[i];
                lastNode = node;

                if (i < path.Count - 1 && !node.IsExpanded && node.Children.Count > 0)
                {
                    node.IsExpanded = true;
                }

                if (!node.IsLoaded && node.NodeType != NodeType.ThisPC && node.NodeType != NodeType.ExternalDevice)
                {
                    await ViewModel.LoadChildrenAsync(node);
                    if (_isUnloaded || AppLifetime.IsShuttingDown)
                    {
                        return;
                    }
                }
            }

            if (lastNode != null)
            {
                FolderTreeView.SelectedItem = lastNode;
                await System.Threading.Tasks.Task.Delay(100);
                if (_isUnloaded || AppLifetime.IsShuttingDown)
                {
                    return;
                }

                ScrollToTreeViewItem(lastNode);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ExpandTreeViewPathAsync error: {ex}");
        }
    }

    private void ScrollToTreeViewItem(FolderNode node)
    {
        try
        {
            if (FolderTreeView.ContainerFromItem(node) is TreeViewItem container)
            {
                container.StartBringIntoView();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ScrollToTreeViewItem error: {ex}");
        }
    }

    private void ViewModel_ImagesChanged(object? sender, EventArgs e)
    {
        ClearItemsViewSelection();
        SyncSelectedStateFromItemsView();
        ImageLinedFlowLayout.InvalidateItemsInfo();
        QueueVisibleThumbnailLoad();
    }

    private void ImageItem_Loaded(object sender, RoutedEventArgs e)
    {
        QueueVisibleThumbnailLoad();
    }

    private void ImageItem_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
        {
            return;
        }

        if (TryGetImageInfo(sender) is ImageFileInfo imageInfo)
        {
            imageInfo.CancelThumbnailLoad();
        }
    }

    private void ImageItemsView_SelectionChanged(ItemsView sender, ItemsViewSelectionChangedEventArgs args)
    {
        SyncSelectedStateFromItemsView();
    }

    private void ImageLinedFlowLayout_ItemsInfoRequested(LinedFlowLayout sender, LinedFlowLayoutItemsInfoRequestedEventArgs args)
    {
        if (ViewModel.Images.Count == 0)
        {
            return;
        }

        var aspectRatios = new double[ViewModel.Images.Count];
        for (var i = 0; i < ViewModel.Images.Count; i++)
        {
            var aspectRatio = ViewModel.Images[i].AspectRatio;
            if (double.IsNaN(aspectRatio) || double.IsInfinity(aspectRatio) || aspectRatio <= 0)
            {
                aspectRatio = 1d;
            }

            aspectRatios[i] = aspectRatio;
        }

        args.ItemsRangeStartIndex = 0;
        args.SetDesiredAspectRatios(aspectRatios);
    }

    private void ImageScrollView_ViewChanged(ScrollView sender, object args)
    {
        QueueVisibleThumbnailLoad();
    }

    private void SyncSelectedStateFromItemsView()
    {
        if (_isUpdatingSelectionState)
        {
            return;
        }

        _isUpdatingSelectionState = true;
        try
        {
            foreach (var image in ViewModel.Images)
            {
                image.IsSelected = false;
            }

            foreach (var selectedItem in EnumerateSelectedItems())
            {
                if (selectedItem is ImageFileInfo imageInfo)
                {
                    imageInfo.IsSelected = true;
                }
            }
        }
        finally
        {
            _isUpdatingSelectionState = false;
        }
    }

    private void ImageItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if (TryGetImageInfo(element) is ImageFileInfo imageInfo && !IsItemCurrentlySelected(imageInfo))
        {
            var itemIndex = ViewModel.Images.IndexOf(imageInfo);
            if (itemIndex >= 0)
            {
                ImageItemsView.DeselectAll();
                ImageItemsView.Select(itemIndex);
                SyncSelectedStateFromItemsView();
            }
        }

        FlyoutBase.ShowAttachedFlyout(element);
        e.Handled = true;
    }

    private bool IsItemCurrentlySelected(ImageFileInfo imageInfo)
    {
        foreach (var selectedItem in EnumerateSelectedItems())
        {
            if (ReferenceEquals(selectedItem, imageInfo))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<object> EnumerateSelectedItems()
    {
        return ImageItemsView.SelectedItems?.Cast<object>() ?? Enumerable.Empty<object>();
    }

    private IEnumerable<ImageFileInfo> GetVisibleImageItems()
    {
        foreach (var container in FindDescendants<ItemContainer>(ImageItemsView))
        {
            if (TryGetImageInfo(container) is ImageFileInfo imageInfo)
            {
                yield return imageInfo;
            }
        }
    }

    private void ClearItemsViewSelection()
    {
        var selectedItems = EnumerateSelectedItems().ToList();
        if (selectedItems.Count == 0)
        {
            return;
        }

        ImageItemsView.DeselectAll();
    }

    private void AttachItemsViewScrollView()
    {
        var scrollView = ImageItemsView.ScrollView;
        if (scrollView == null || ReferenceEquals(scrollView, _imageScrollView))
        {
            return;
        }

        if (_imageScrollView != null)
        {
            _imageScrollView.ViewChanged -= ImageScrollView_ViewChanged;
        }

        _imageScrollView = scrollView;
        _imageScrollView.ViewChanged += ImageScrollView_ViewChanged;
    }

    private void QueueVisibleThumbnailLoad()
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
        {
            return;
        }

        AttachItemsViewScrollView();
        _ = ProcessVisibleThumbnailBatchAsync();
    }

    private async Task ProcessVisibleThumbnailBatchAsync()
    {
        if (_isThumbnailBatchRunning)
        {
            _hasPendingThumbnailBatch = true;
            return;
        }

        _isThumbnailBatchRunning = true;
        try
        {
            do
            {
                _hasPendingThumbnailBatch = false;

                var visibleImages = GetVisibleImageItems()
                    .Distinct()
                    .ToList();

                if (visibleImages.Count == 0)
                {
                    return;
                }

                await Task.WhenAll(visibleImages.Select(image => image.EnsureThumbnailAsync(ViewModel.ThumbnailSize)));
            }
            while (_hasPendingThumbnailBatch && !_isUnloaded && !AppLifetime.IsShuttingDown);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ProcessVisibleThumbnailBatchAsync error: {ex}");
        }
        finally
        {
            _isThumbnailBatchRunning = false;
        }
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static ImageFileInfo? TryGetImageInfo(object sender)
    {
        return sender switch
        {
            FrameworkElement { Tag: ImageFileInfo imageInfo } => imageInfo,
            FrameworkElement element => element.DataContext as ImageFileInfo,
            _ => null
        };
    }

    private void Share_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
    }

    private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.A)
        {
            var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (isCtrlPressed)
            {
                ImageItemsView.SelectAll();
                e.Handled = true;
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            ClearItemsViewSelection();
            e.Handled = true;
        }
    }
}
