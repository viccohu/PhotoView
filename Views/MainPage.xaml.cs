using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PhotoView.Contracts.Services;
using PhotoView.Helpers;
using PhotoView.Models;
using PhotoView.ViewModels;
using System.IO;
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
    private bool _hasAttemptedRestoreLastFolder;

    public MainPage()
    {
        System.Diagnostics.Debug.WriteLine($"[MainPage] 构造函数开始");
        
        ViewModel = App.GetService<MainViewModel>();
        System.Diagnostics.Debug.WriteLine($"[MainPage] ViewModel 已获取, FolderTree.Count={ViewModel.FolderTree.Count}");
        
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
        ViewModel.FolderTreeLoaded += ViewModel_FolderTreeLoaded;
        Loaded += MainPage_Loaded;
        KeyDown += MainPage_KeyDown;
        Unloaded += MainPage_Unloaded;
        
        System.Diagnostics.Debug.WriteLine($"[MainPage] 事件已订阅, FolderTree.Count={ViewModel.FolderTree.Count}");
        
        // 如果 FolderTree 已经加载完成（事件在订阅前已触发），直接调用恢复逻辑
        if (ViewModel.FolderTree.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] 构造函数中检测到 FolderTree 已加载, Count={ViewModel.FolderTree.Count}");
            _ = TryRestoreLastFolderAsync();
        }
        
        System.Diagnostics.Debug.WriteLine($"[MainPage] 构造函数结束");
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
    }

    private async void ViewModel_FolderTreeLoaded(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[MainPage] FolderTreeLoaded 事件触发");
        await TryRestoreLastFolderAsync();
    }

    private async System.Threading.Tasks.Task TryRestoreLastFolderAsync()
    {
        if (_hasAttemptedRestoreLastFolder)
            return;

        _hasAttemptedRestoreLastFolder = true;

        var settingsService = App.GetService<ISettingsService>();
        
        // 等待设置加载完成（最多等待 2 秒）
        int waitCount = 0;
        while (waitCount < 20)
        {
            // 如果 RememberLastFolder 为 true 且路径不为空，可以开始恢复
            if (settingsService.RememberLastFolder && !string.IsNullOrEmpty(settingsService.LastFolderPath))
                break;
            
            // 如果 RememberLastFolder 为 false 且已经等待过一轮，说明设置已加载且未启用
            if (!settingsService.RememberLastFolder && waitCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] 未启用记住上次路径，跳过恢复");
                return;
            }
            
            await System.Threading.Tasks.Task.Delay(100);
            waitCount++;
        }
        
        System.Diagnostics.Debug.WriteLine($"[MainPage] FolderTreeLoaded 触发, RememberLastFolder={settingsService.RememberLastFolder}, LastFolderPath={settingsService.LastFolderPath}, 等待次数={waitCount}");
        
        if (!settingsService.RememberLastFolder || string.IsNullOrEmpty(settingsService.LastFolderPath))
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] 路径为空或未启用，跳过恢复");
            return;
        }

        await System.Threading.Tasks.Task.Delay(200);
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        System.Diagnostics.Debug.WriteLine($"[MainPage] 尝试恢复上次路径: {settingsService.LastFolderPath}");

        var targetNode = await TryFindAndLoadNodeByPathAsync(settingsService.LastFolderPath);
        if (targetNode != null)
        {
            await ExpandTreeViewPathAsync(targetNode);
            if (_isUnloaded || AppLifetime.IsShuttingDown)
                return;
            ThrottleLoadImages(targetNode);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] 未找到上次路径: {settingsService.LastFolderPath}");
        }
    }

    private async System.Threading.Tasks.Task<FolderNode?> TryFindAndLoadNodeByPathAsync(string targetPath)
    {
        targetPath = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        System.Diagnostics.Debug.WriteLine($"[TryFindAndLoadNodeByPathAsync] 目标路径: {targetPath}");

        var foundNode = ViewModel.FindNodeByPath(targetPath);
        if (foundNode != null)
            return foundNode;

        var pathParts = targetPath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (pathParts.Length == 0)
            return null;

        var currentPath = pathParts[0] + Path.DirectorySeparatorChar;
        FolderNode? currentNode = null;

        foreach (var rootNode in ViewModel.FolderTree)
        {
            if (rootNode.NodeType == NodeType.ThisPC || rootNode.NodeType == NodeType.ExternalDevice)
            {
                if (!rootNode.IsLoaded)
                {
                    await ViewModel.LoadChildrenAsync(rootNode);
                }

                foreach (var child in rootNode.Children)
                {
                    if (child.FullPath != null && child.FullPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        currentNode = child;
                        break;
                    }
                }

                if (currentNode != null)
                    break;
            }
        }

        if (currentNode == null)
            return null;

        for (int i = 1; i < pathParts.Length; i++)
        {
            if (!currentNode.IsLoaded)
            {
                await ViewModel.LoadChildrenAsync(currentNode);
            }

            var nextPart = pathParts[i];
            currentPath = Path.Combine(currentPath, nextPart);

            FolderNode? nextNode = null;
            foreach (var child in currentNode.Children)
            {
                if (child.FullPath != null && string.Equals(child.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
                {
                    nextNode = child;
                    break;
                }
            }

            if (nextNode == null)
                return null;

            currentNode = nextNode;
        }

        System.Diagnostics.Debug.WriteLine($"[TryFindAndLoadNodeByPathAsync] 最终找到节点: {currentNode.Name}, 完整路径: {currentNode.FullPath}");
        return currentNode;
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

            System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 开始展开, 目标节点: {targetNode.Name}, 路径: {targetNode.FullPath}");

            var path = new List<FolderNode>();
            var current = targetNode;
            while (current != null)
            {
                path.Insert(0, current);
                System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 路径节点: {current.Name}, NodeType={current.NodeType}, Parent={current.Parent?.Name ?? "null"}");
                current = current.Parent;
            }

            System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 路径长度: {path.Count}");

            if (path.Count == 0)
            {
                return;
            }

            FolderNode? lastNode = null;
            for (var i = 0; i < path.Count; i++)
            {
                var node = path[i];
                lastNode = node;
                System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 处理节点 {i}: {node.Name}, IsLoaded={node.IsLoaded}, IsExpanded={node.IsExpanded}, Children.Count={node.Children.Count}");

                if (i < path.Count - 1)
                {
                    if (!node.IsLoaded)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 加载子节点: {node.Name}");
                        await ViewModel.LoadChildrenAsync(node);
                        if (_isUnloaded || AppLifetime.IsShuttingDown)
                        {
                            return;
                        }
                    }

                    if (!node.IsExpanded)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 展开节点: {node.Name}");
                        node.IsExpanded = true;
                        await System.Threading.Tasks.Task.Delay(50);
                    }
                }

                var treeViewItem = FolderTreeView.ContainerFromItem(node) as TreeViewItem;
                System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] TreeViewItem for {node.Name}: {(treeViewItem != null ? "found" : "null")}");
            }

            if (lastNode != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 设置选中节点: {lastNode.Name}");
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

        if (element.Tag is ImageFileInfo imageInfo)
        {
            _rightClickedImageInfo = imageInfo;
            if (!ImageGridView.SelectedItems.Contains(imageInfo))
            {
                ImageGridView.SelectedItem = imageInfo;
            }
        }

        FlyoutBase.ShowAttachedFlyout(element);
        e.Handled = true;
    }

    private void TreeViewItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not TreeViewItem treeViewItem)
            return;

        if (treeViewItem.Content is Grid grid && grid.Tag is FolderNode node)
        {
            _rightClickedFolderNode = node;
            FlyoutBase.ShowAttachedFlyout(grid);
        }
        e.Handled = true;
    }

    private FolderNode? _rightClickedFolderNode;

    private void OpenFolderInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolderNode == null || string.IsNullOrEmpty(_rightClickedFolderNode.FullPath))
        {
            return;
        }

        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _rightClickedFolderNode.FullPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenFolderInExplorer_Click error: {ex}");
        }
    }

    private ImageFileInfo? _rightClickedImageInfo;

    private void OpenImageInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedImageInfo == null || _rightClickedImageInfo.ImageFile == null)
        {
            return;
        }

        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_rightClickedImageInfo.ImageFile.Path}\"",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenImageInExplorer_Click error: {ex}");
        }
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
