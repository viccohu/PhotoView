using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
    private bool _isUnloaded;
    private bool _isUpdatingSelectionState;
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
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _loadImagesThrottleTimer.Stop();
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

        TriggerVisibleItemsThumbnailLoad();
    }

    private void TriggerVisibleItemsThumbnailLoad()
    {
        try
        {
            // 触发第一张图片的加载
            if (ViewModel.Images.Count > 0)
            {
                var firstImage = ViewModel.Images[0];
                _ = firstImage.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
            }

            // 触发所有元素的加载，确保尺寸切换时所有图片都能重新加载
            for (int i = 0; i < ImageGridView.Items.Count; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo)
                {
                    _ = imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TriggerVisibleItemsThumbnailLoad error: {ex}");
        }
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
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            ClearGridViewSelection();

            if (ViewModel.Images.Count > 0)
            {
                ImageGridView.ScrollIntoView(ViewModel.Images[0]);
            }
        });
    }

    private void ImageGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not ImageFileInfo imageInfo)
            return;

        if (args.InRecycleQueue)
        {
            imageInfo.CancelThumbnailLoad();
            return;
        }

        if (args.Phase == 0)
        {
            args.RegisterUpdateCallback(1u, ImageGridView_ContainerContentChanging);
        }
        else if (args.Phase == 1)
        {
            _ = imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
        }
    }

    private void ImageGridView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncSelectedStateFromGridView();
    }

    private void SyncSelectedStateFromGridView()
    {
        if (_isUpdatingSelectionState)
            return;

        _isUpdatingSelectionState = true;
        try
        {
            foreach (var image in ViewModel.Images)
            {
                image.IsSelected = false;
            }

            foreach (var selectedItem in ImageGridView.SelectedItems)
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

    private void ClearGridViewSelection()
    {
        if (ImageGridView.SelectedItems.Count == 0)
            return;

        ImageGridView.SelectedItem = null;
    }

    private void ImageItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        if (element.Tag is ImageFileInfo imageInfo && !ImageGridView.SelectedItems.Contains(imageInfo))
        {
            ImageGridView.SelectedItem = imageInfo;
        }

        FlyoutBase.ShowAttachedFlyout(element);
        e.Handled = true;
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

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.GoBackAsync();
    }

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.GoUpAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshAsync();
    }

    private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.A)
        {
            var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (isCtrlPressed)
            {
                ImageGridView.SelectAll();
                e.Handled = true;
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            ClearGridViewSelection();
            e.Handled = true;
        }
    }
}
