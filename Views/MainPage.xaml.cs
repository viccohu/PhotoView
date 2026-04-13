using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PhotoView.Contracts.Services;
using PhotoView.Controls;
using PhotoView.Dialogs;
using PhotoView.Helpers;
using PhotoView.Models;
using PhotoView.Services;
using PhotoView.ViewModels;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.System;

namespace PhotoView.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    private readonly DispatcherTimer _loadImagesThrottleTimer;
    private readonly DispatcherTimer _ratingDebounceTimer;
    private readonly DispatcherTimer _visibleThumbnailLoadTimer;
    private readonly ISettingsService _settingsService;
    private readonly HashSet<ImageFileInfo> _pendingVisibleThumbnailLoads = new();
    private readonly HashSet<ImageFileInfo> _realizedImageItems = new();
    private readonly HashSet<ImageFileInfo> _selectedImageState = new();
    private const double GridViewItemMargin = 4d;
    private const double GridViewItemGap = GridViewItemMargin * 2d;
    private const double FolderDrawerExpandedMaxHeight = 150d;
    private const int FolderDrawerAnimationDurationMs = 160;
    private FolderNode? _pendingLoadNode;
    private ScrollViewer? _imageGridScrollViewer;
    private ItemsWrapGrid? _imageItemsWrapGrid;
    private bool _isUnloaded;
    private bool _isProgrammaticScrollActive;
    private bool _isUserScrollInProgress;
    private DateTime _lastClickTime;
    private FolderNode? _lastClickedNode;
    private bool _hasAttemptedRestoreLastFolder;
    private bool _isFolderDrawerExpanded = true;
    private bool _isFolderDrawerContentVisible;
    private int _folderDrawerAnimationVersion;
    private (ImageFileInfo Image, uint Rating)? _pendingRatingUpdate;
    private ImageFileInfo? _storedImageFileInfo;
    private Controls.ImageViewerControl? _currentViewer;

    public MainPage()
    {
        System.Diagnostics.Debug.WriteLine($"[MainPage] 构造函数开始");
        
        ViewModel = App.GetService<MainViewModel>();
        _settingsService = App.GetService<ISettingsService>();
        System.Diagnostics.Debug.WriteLine($"[MainPage] ViewModel 已获取, FolderTree.Count={ViewModel.FolderTree.Count}");
        
        NavigationCacheMode = NavigationCacheMode.Enabled;
        InitializeComponent();
        FolderTreeView.DataContext = ViewModel;

        _loadImagesThrottleTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _loadImagesThrottleTimer.Tick += LoadImagesThrottleTimer_Tick;

        _ratingDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _ratingDebounceTimer.Tick += RatingDebounceTimer_Tick;

        _visibleThumbnailLoadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _visibleThumbnailLoadTimer.Tick += VisibleThumbnailLoadTimer_Tick;

        ViewModel.ImagesChanged += ViewModel_ImagesChanged;
        ViewModel.ThumbnailSizeChanged += ViewModel_ThumbnailSizeChanged;
        ViewModel.FolderTreeLoaded += ViewModel_FolderTreeLoaded;
        ViewModel.SelectedFolderChanged += ViewModel_SelectedFolderChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        Loaded += MainPage_Loaded;
        KeyDown += MainPage_KeyDown;
        PreviewKeyDown += MainPage_PreviewKeyDown;
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
        FilterFlyoutControl.FilterViewModel = ViewModel.Filter;
        ViewModel.Filter.FilterChanged += Filter_FilterChanged;
        ImageGridView.DoubleTapped += ImageGridView_DoubleTapped;
        AttachImageGridScrollViewer();
        UpdateImageGridTileSize();
        QueueVisibleThumbnailLoad("page-loaded");
        UpdateFilterButtonState();
        UpdateFolderDrawerState(animate: false);
    }

    private async void ImageViewer_Closed(object? sender, EventArgs e)
    {
        var viewer = _currentViewer;
        if (viewer == null)
        {
            return;
        }

        try
        {
            if (_storedImageFileInfo != null)
            {
                await ScrollItemIntoViewAsync(_storedImageFileInfo, "viewer-close", ScrollIntoViewAlignment.Default);

                var animation = ConnectedAnimationService.GetForCurrentView().GetAnimation("BackConnectedAnimation");
                if (animation != null)
                {
                    await ImageGridView.TryStartConnectedAnimationAsync(animation, _storedImageFileInfo, "thumbnailImage");
                }
            }

            await viewer.CompleteCloseAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] ImageViewer_Closed error: {ex}");
        }
        finally
        {
            // 清理资源
            viewer.Closed -= ImageViewer_Closed;
            viewer.ViewModel.RatingUpdated -= ViewModel_RatingUpdated;
            if (ReferenceEquals(_currentViewer, viewer))
            {
                ViewerContainer.Content = null;
                _currentViewer = null;
            }

            await _settingsService.ResumeAlwaysDecodeRawPersistenceAsync("viewer-close");
        }
    }

    private void ViewModel_RatingUpdated(object? sender, (ImageFileInfo Image, uint Rating) e)
    {
        System.Diagnostics.Debug.WriteLine($"[MainPage] ViewModel_RatingUpdated: 图片 {e.Image.ImageName} 评级已更新为 {e.Rating}");
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SubFolderCount) ||
            e.PropertyName == nameof(MainViewModel.HasSubFoldersInCurrentFolder) ||
            e.PropertyName == nameof(MainViewModel.CurrentSubFolders))
        {
            _isFolderDrawerExpanded = ViewModel.HasSubFoldersInCurrentFolder;

            UpdateFolderDrawerState();
        }
    }

    private async void ImageGridView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element && element.DataContext is ImageFileInfo imageFileInfo)
        {
            _storedImageFileInfo = imageFileInfo;

            // 每次打开图片，都 new 一个新的实例
            var newViewer = new Controls.ImageViewerControl();
            ViewerContainer.Content = newViewer;
            _currentViewer = newViewer;
            _settingsService.SuspendAlwaysDecodeRawPersistence("viewer-open");

            // 订阅关闭事件
            newViewer.Closed += ImageViewer_Closed;
            
            // 订阅评级更新事件
            newViewer.ViewModel.RatingUpdated += ViewModel_RatingUpdated;

            newViewer.PrepareContent(imageFileInfo);

            // 等待布局完成并应用初始缩放，然后再启动动画
            await newViewer.PrepareForAnimationAsync();

            if (ImageGridView.ContainerFromItem(imageFileInfo) is GridViewItem container)
            {
                ImageGridView.PrepareConnectedAnimation("ForwardConnectedAnimation", imageFileInfo, "thumbnailImage");
            }

            var imageAnimation = ConnectedAnimationService.GetForCurrentView().GetAnimation("ForwardConnectedAnimation");
            if (imageAnimation != null)
            {
                imageAnimation.TryStart(newViewer.GetMainImage(), newViewer.GetCoordinatedElements());
            }

            await newViewer.ShowAfterAnimationAsync();

            e.Handled = true;
        }
    }

    private void Filter_FilterChanged(object? sender, EventArgs e)
    {
        UpdateFilterButtonState();
    }

    private void UpdateFilterButtonState()
    {
        var isActive = ViewModel.Filter.IsFilterActive;
        
        if (isActive)
        {
            FilterSplitButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));
        }
        else
        {
            FilterSplitButton.ClearValue(Control.BackgroundProperty);
        }
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

    private async void ViewModel_FolderTreeLoaded(object? sender, EventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[MainPage] FolderTreeLoaded 事件触发");
        await TryRestoreLastFolderAsync();
    }

    private async void ViewModel_SelectedFolderChanged(object? sender, FolderNode? node)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown || node == null)
            return;

        System.Diagnostics.Debug.WriteLine($"[MainPage] SelectedFolderChanged 事件触发, 节点: {node.Name}");
        await ExpandTreeViewPathAsync(node);
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
        if (_currentViewer != null)
        {
            _currentViewer.Closed -= ImageViewer_Closed;
            _currentViewer.ViewModel.RatingUpdated -= ViewModel_RatingUpdated;
            _currentViewer = null;
            _ = _settingsService.ResumeAlwaysDecodeRawPersistenceAsync("main-page-unloaded");
        }

        _loadImagesThrottleTimer.Stop();
        _ratingDebounceTimer.Stop();
        _visibleThumbnailLoadTimer.Stop();
        _pendingRatingUpdate = null;
        _pendingVisibleThumbnailLoads.Clear();
        _realizedImageItems.Clear();
        _selectedImageState.Clear();
        DetachImageGridScrollViewer();
        _imageItemsWrapGrid = null;
        
        foreach (var ratingControl in _ratingControlEventMap.Keys)
        {
            ratingControl.ValueChanged -= RatingControl_ValueChanged;
        }
        _ratingControlEventMap.Clear();
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

        UpdateImageGridTileSize();
        TriggerVisibleItemsThumbnailLoad();
    }

    private void ImageGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateImageGridTileSize();
    }

    private void UpdateImageGridTileSize()
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        AttachImageItemsWrapGrid();
        if (_imageItemsWrapGrid == null)
            return;

        var availableWidth = ImageGridView.ActualWidth - ImageGridView.Padding.Left - ImageGridView.Padding.Right;
        if (availableWidth <= 0)
            return;

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

        tileSize = Math.Clamp(tileSize, minimumTileSize, maximumTileSize);

        if (Math.Abs(_imageItemsWrapGrid.ItemWidth - tileSize) < 0.5 &&
            Math.Abs(_imageItemsWrapGrid.ItemHeight - tileSize) < 0.5)
        {
            return;
        }

        _imageItemsWrapGrid.ItemWidth = tileSize;
        _imageItemsWrapGrid.ItemHeight = tileSize;
        System.Diagnostics.Debug.WriteLine($"[MainPage] Tile size updated: size={tileSize:F0}, columns={columnCount}, width={availableWidth:F0}");

        QueueVisibleThumbnailLoad("tile-size-changed");
    }

    private static double CalculateTileSize(double availableWidth, int columnCount)
    {
        return Math.Max(1d, (availableWidth - (GridViewItemGap * Math.Max(0, columnCount - 1))) / columnCount);
    }

    private static double GetTargetTileSize(ThumbnailSize size)
    {
        return size switch
        {
            ThumbnailSize.Small => 160d,
            ThumbnailSize.Medium => 256d,
            ThumbnailSize.Large => 512d,
            _ => 256d
        };
    }

    private static double GetMinimumTileSize(ThumbnailSize size)
    {
        return size switch
        {
            ThumbnailSize.Small => 140d,
            ThumbnailSize.Medium => 208d,
            ThumbnailSize.Large => 360d,
            _ => 208d
        };
    }

    private static double GetMaximumTileSize(ThumbnailSize size)
    {
        return size switch
        {
            ThumbnailSize.Small => 200d,
            ThumbnailSize.Medium => 320d,
            ThumbnailSize.Large => 640d,
            _ => 320d
        };
    }

    private void TriggerVisibleItemsThumbnailLoad()
    {
        try
        {
            // 触发第一张图片的加载
            if (ViewModel.Images.Count > 0)
            {
                var firstImage = ViewModel.Images[0];
                QueueVisibleThumbnailLoad(ViewModel.Images[0], "thumbnail-size-first");
            }

            // 触发所有元素的加载，确保尺寸切换时所有图片都能重新加载
            QueueVisibleThumbnailLoad("thumbnail-size-changed");
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
            await NavigateToFolderNodeAsync(node);
        }
    }

    private async Task NavigateToFolderNodeAsync(FolderNode node)
    {
        await ExpandTreeViewPathAsync(node);
        if (_isUnloaded || AppLifetime.IsShuttingDown)
        {
            return;
        }

        ThrottleLoadImages(node);
    }

    private void UpdateFolderDrawerState(bool animate = true)
    {
        var hasFolders = ViewModel.HasSubFoldersInCurrentFolder;
        var shouldShowContent = hasFolders && _isFolderDrawerExpanded;
        FolderDrawerRoot.Visibility = Visibility.Visible;
        FolderDrawerChevron.Glyph = shouldShowContent ? "\xE70D" : "\xE70E";
        FolderDrawerToggleButton.IsEnabled = hasFolders;

        if (!animate)
        {
            _folderDrawerAnimationVersion++;
            _isFolderDrawerContentVisible = shouldShowContent;
            SubFolderGridView.Visibility = shouldShowContent ? Visibility.Visible : Visibility.Collapsed;
            SubFolderGridView.Opacity = shouldShowContent ? 1d : 0d;
            SubFolderGridView.MaxHeight = shouldShowContent ? FolderDrawerExpandedMaxHeight : 0d;
            return;
        }

        if (_isFolderDrawerContentVisible == shouldShowContent)
        {
            return;
        }

        AnimateFolderDrawerContent(shouldShowContent);
    }

    private void AnimateFolderDrawerContent(bool showContent)
    {
        var animationVersion = ++_folderDrawerAnimationVersion;
        _isFolderDrawerContentVisible = showContent;

        if (showContent)
        {
            SubFolderGridView.Visibility = Visibility.Visible;
        }

        var heightAnimation = new DoubleAnimation
        {
            From = SubFolderGridView.MaxHeight,
            To = showContent ? FolderDrawerExpandedMaxHeight : 0d,
            Duration = new Duration(TimeSpan.FromMilliseconds(FolderDrawerAnimationDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(heightAnimation, SubFolderGridView);
        Storyboard.SetTargetProperty(heightAnimation, "MaxHeight");

        var opacityAnimation = new DoubleAnimation
        {
            From = SubFolderGridView.Opacity,
            To = showContent ? 1d : 0d,
            Duration = new Duration(TimeSpan.FromMilliseconds(FolderDrawerAnimationDurationMs)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnimation, SubFolderGridView);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(heightAnimation);
        storyboard.Children.Add(opacityAnimation);
        storyboard.Completed += (_, _) =>
        {
            if (animationVersion != _folderDrawerAnimationVersion)
            {
                return;
            }

            SubFolderGridView.MaxHeight = showContent ? FolderDrawerExpandedMaxHeight : 0d;
            SubFolderGridView.Opacity = showContent ? 1d : 0d;
            SubFolderGridView.Visibility = showContent ? Visibility.Visible : Visibility.Collapsed;
        };
        storyboard.Begin();
    }

    private void FolderDrawerToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasSubFoldersInCurrentFolder)
        {
            return;
        }

        _isFolderDrawerExpanded = !_isFolderDrawerExpanded;
        UpdateFolderDrawerState();
    }

    private async void SubFolderGridView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TryGetFolderNodeFromElement(e.OriginalSource, out var folderNode) && folderNode.Folder != null)
        {
            SubFolderGridView.SelectedItem = folderNode;
            await NavigateToFolderNodeAsync(folderNode);
            e.Handled = true;
        }
    }

    private static bool TryGetFolderNodeFromElement(object originalSource, out FolderNode folderNode)
    {
        var current = originalSource as DependencyObject;
        while (current != null)
        {
            if (current is FrameworkElement element)
            {
                if (element.Tag is FolderNode tagNode)
                {
                    folderNode = tagNode;
                    return true;
                }

                if (element.DataContext is FolderNode contextNode)
                {
                    folderNode = contextNode;
                    return true;
                }
            }

            current = VisualTreeHelper.GetParent(current);
        }

        folderNode = null!;
        return false;
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

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (ViewModel.Images.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[MainPage] ImagesChanged -> clearing transient grid state for empty collection");
                ClearGridViewSelection();
                _pendingVisibleThumbnailLoads.Clear();
                _realizedImageItems.Clear();
                ClearSelectedImageState();
            }

            QueueVisibleThumbnailLoad("images-changed");
        });
    }

    private void ImageGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
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
            args.RegisterUpdateCallback(1u, ImageGridView_ContainerContentChanging);
        }
        else if (args.Phase == 1)
        {
            _realizedImageItems.Add(imageInfo);
            QueueVisibleThumbnailLoad(imageInfo, "container-phase1");
        }
    }

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
        var candidates = _pendingVisibleThumbnailLoads.ToArray();
        _pendingVisibleThumbnailLoads.Clear();

        foreach (var imageInfo in candidates)
        {
            if (!IsItemContainerRealized(imageInfo))
                continue;

            _ = imageInfo.EnsureThumbnailAsync(size);
        }
    }

    private readonly Dictionary<RatingControl, bool> _ratingControlEventMap = new();

    private void RatingControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RatingControl ratingControl && !_ratingControlEventMap.ContainsKey(ratingControl))
        {
            ratingControl.ValueChanged += RatingControl_ValueChanged;
            _ratingControlEventMap[ratingControl] = true;
        }
    }

    private void RatingControl_ValueChanged(RatingControl sender, object args)
    {
        try
        {
            if (_isUnloaded || AppLifetime.IsShuttingDown)
                return;

            var imageInfo = FindImageInfoFromRatingControl(sender);
            if (imageInfo == null)
                return;

            double controlValue = sender.Value;
            uint ratingValue = 0;

            if (controlValue > 0)
            {
                int stars = (int)Math.Round(controlValue, MidpointRounding.AwayFromZero);
                stars = Math.Clamp(stars, 1, 5);
                ratingValue = ImageFileInfo.StarsToRating(stars);
            }

            _pendingRatingUpdate = (imageInfo, ratingValue);
            _ratingDebounceTimer.Stop();
            _ratingDebounceTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RatingControl_ValueChanged] 错误: {ex.Message}");
        }
    }

    private async void RatingDebounceTimer_Tick(object? sender, object e)
    {
        _ratingDebounceTimer.Stop();
        
        if (_pendingRatingUpdate.HasValue)
        {
            var (image, rating) = _pendingRatingUpdate.Value;
            _pendingRatingUpdate = null;
            
            var allImagesToProcess = new List<ImageFileInfo>();
            
            if (image.Group != null)
            {
                foreach (var groupImage in image.Group.Images)
                {
                    if (!allImagesToProcess.Contains(groupImage))
                    {
                        allImagesToProcess.Add(groupImage);
                    }
                }
            }
            else
            {
                allImagesToProcess.Add(image);
            }
            
            foreach (var imageInfo in allImagesToProcess)
            {
                await UpdateRatingAsync(imageInfo, rating);
            }
        }
    }

    private ImageFileInfo? FindImageInfoFromRatingControl(RatingControl ratingControl)
    {
        try
        {
            var parent = VisualTreeHelper.GetParent(ratingControl);
            while (parent != null)
            {
                if (parent is FrameworkElement fe && fe.DataContext is ImageFileInfo imageInfo)
                {
                    return imageInfo;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task UpdateRatingAsync(ImageFileInfo imageInfo, uint rating)
    {
        try
        {
            var ratingService = App.GetService<RatingService>();
            await imageInfo.SetRatingAsync(ratingService, rating);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[UpdateRatingAsync] 错误: {ex.Message}");
        }
    }

    private void ImageGridView_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        SyncSelectedStateFromGridView(e);
    }

    private void SyncSelectedStateFromGridView(Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        foreach (var removedItem in e.RemovedItems)
        {
            if (removedItem is ImageFileInfo imageInfo && _selectedImageState.Remove(imageInfo))
            {
                imageInfo.IsSelected = false;
            }
        }

        foreach (var addedItem in e.AddedItems)
        {
            if (addedItem is ImageFileInfo imageInfo && _selectedImageState.Add(imageInfo))
            {
                imageInfo.IsSelected = true;
                System.Diagnostics.Debug.WriteLine($"[Selected] {imageInfo.ImageName}, Rating: {imageInfo.Rating}, Source: {imageInfo.RatingSource}, Dimensions: {imageInfo.Width}x{imageInfo.Height}, DisplaySize: {imageInfo.DisplayWidth:F0}x{imageInfo.DisplayHeight:F0}");
            }
        }

        if (ImageGridView.SelectedItems.Count == 0 && _selectedImageState.Count > 0)
        {
            ClearSelectedImageState();
        }
    }

    private void ClearSelectedImageState()
    {
        foreach (var imageInfo in _selectedImageState.ToArray())
        {
            imageInfo.IsSelected = false;
        }

        _selectedImageState.Clear();
    }

    private void ClearGridViewSelection()
    {
        if (ImageGridView.SelectedItems.Count == 0)
            return;

        ExecuteProgrammaticSelectionChange(() => ImageGridView.SelectedItem = null);
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
                ExecuteProgrammaticSelectionChange(() => ImageGridView.SelectedItem = imageInfo);
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

    private void SubFolderItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        if (element.Tag is FolderNode node)
        {
            _rightClickedSubFolderNode = node;
            SubFolderGridView.SelectedItem = node;
            FlyoutBase.ShowAttachedFlyout(element);
            e.Handled = true;
        }
    }

    private FolderNode? _rightClickedFolderNode;
    private FolderNode? _rightClickedSubFolderNode;

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

    private void OpenSubFolderInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedSubFolderNode == null || string.IsNullOrEmpty(_rightClickedSubFolderNode.FullPath))
        {
            return;
        }

        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _rightClickedSubFolderNode.FullPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processInfo);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenSubFolderInExplorer_Click error: {ex}");
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

    private void MainPage_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_PreviewKeyDown: 按键={e.Key}, _currentViewer={( _currentViewer == null ? "null" : "not null" )}, e.Handled初始值={e.Handled}");
        
        if (_currentViewer != null)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_PreviewKeyDown: _currentViewer 存在，进入预览模式处理");
            
            if (e.Key == VirtualKey.Escape)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_PreviewKeyDown: 处理了Escape键");
                _currentViewer.PrepareCloseAnimation();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Left || e.Key == VirtualKey.Right ||
                     e.Key == VirtualKey.Up || e.Key == VirtualKey.Down)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_PreviewKeyDown: 阻止了方向键 {e.Key}");
                e.Handled = true;
            }
            else if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number5)
            {
                // 处理数字键评级，传递给 ImageViewerControl
                System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_PreviewKeyDown: 处理数字键评级 {e.Key}");
                e.Handled = true;
                
                // 调用 ImageViewerControl 的 HandleRatingKey 方法
                _currentViewer.HandleRatingKey(e.Key);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_PreviewKeyDown: 未处理的按键 {e.Key}");
            }
            
            System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_PreviewKeyDown: 预览模式处理完成，e.Handled最终值={e.Handled}");
        }
    }

    private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_KeyDown: 按键={e.Key}, _currentViewer={( _currentViewer == null ? "null" : "not null" )}, e.Handled初始值={e.Handled}");
        
        if (_currentViewer != null)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_KeyDown: _currentViewer 存在，进入预览模式处理");
            
            if (e.Key == VirtualKey.Escape)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_KeyDown: 处理了Escape键");
                _currentViewer.PrepareCloseAnimation();
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Left || e.Key == VirtualKey.Right ||
                     e.Key == VirtualKey.Up || e.Key == VirtualKey.Down)
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_KeyDown: 阻止了方向键 {e.Key}");
                e.Handled = true;
            }
            else if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number5)
            {
                // 处理数字键评级，传递给 ImageViewerControl
                System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_KeyDown: 处理数字键评级 {e.Key}");
                e.Handled = true;
                
                // 调用 ImageViewerControl 的 HandleRatingKey 方法
                _currentViewer.HandleRatingKey(e.Key);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_KeyDown: 未处理的按键 {e.Key}");
            }
            
            System.Diagnostics.Debug.WriteLine($"[MainPage] MainPage_KeyDown: 预览模式处理完成，e.Handled最终值={e.Handled}");
            return;
        }

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
        else if (e.Key == VirtualKey.Delete)
        {
            TogglePendingDeleteForSelectedItems();
            e.Handled = true;
        }
        else if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number5)
        {
            HandleRatingShortcut(e.Key);
            e.Handled = true;
        }
        else if (e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad5)
        {
            HandleRatingShortcut(e.Key - (VirtualKey.NumberPad0 - VirtualKey.Number0));
            e.Handled = true;
        }
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

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ExportDialog(_settingsService, ViewModel.Images.ToList());
        dialog.XamlRoot = XamlRoot;
        await dialog.ShowAsync();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var pendingImages = ViewModel.GetPendingDeleteImages();
        if (pendingImages.Count == 0)
            return;

        var allExtensions = new HashSet<string>();
        foreach (var image in pendingImages)
        {
            if (image.ImageFile != null)
            {
                var ext = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
                allExtensions.Add(ext);
            }
            if (image.HasAlternateFormats && image.AlternateFormats != null)
            {
                foreach (var altImage in image.AlternateFormats)
                {
                    if (altImage.ImageFile != null)
                    {
                        var altExt = Path.GetExtension(altImage.ImageFile.Path).ToLowerInvariant();
                        allExtensions.Add(altExt);
                    }
                }
            }
        }

        if (allExtensions.Count == 0)
            return;

        var dialog = new DeleteConfirmDialog(allExtensions.ToList(), pendingImages.Count)
        {
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var filesToDelete = GetFilesToDelete(pendingImages, dialog.SelectedExtensions);
            if (filesToDelete.Count == 0)
            {
                var emptyDialog = new ContentDialog
                {
                    Title = "无可删除文件",
                    Content = "没有符合条件的文件需要删除。",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                await emptyDialog.ShowAsync();
                return;
            }

            dialog.StartProgress();
            
            var deletedImages = new List<ImageFileInfo>();
            var failedCount = 0;

            for (int i = 0; i < filesToDelete.Count; i++)
            {
                var file = filesToDelete[i];
                try
                {
                    await DeleteFileToRecycleBinAsync(file);
                    
                    var imageToDelete = pendingImages.FirstOrDefault(img => img.ImageFile?.Path == file.Path);
                    if (imageToDelete != null && !deletedImages.Contains(imageToDelete))
                    {
                        deletedImages.Add(imageToDelete);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"删除文件失败: {file.Path}, 错误: {ex.Message}");
                    failedCount++;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    dialog.SetProgress(i + 1, filesToDelete.Count);
                });

                await System.Threading.Tasks.Task.Delay(10);
            }

            dialog.SetComplete();
            await System.Threading.Tasks.Task.Delay(500);

            RemoveDeletedImagesFromList(deletedImages);
        }
    }

    private List<StorageFile> GetFilesToDelete(List<ImageFileInfo> pendingImages, List<string> selectedExtensions)
    {
        var files = new List<StorageFile>();

        foreach (var image in pendingImages)
        {
            if (image.ImageFile != null)
            {
                var extension = Path.GetExtension(image.ImageFile.Path).ToLowerInvariant();
                if (selectedExtensions.Contains(extension))
                {
                    files.Add(image.ImageFile);
                }
            }

            if (image.HasAlternateFormats && image.AlternateFormats != null)
            {
                foreach (var altImage in image.AlternateFormats)
                {
                    if (altImage.ImageFile != null)
                    {
                        var altExtension = Path.GetExtension(altImage.ImageFile.Path).ToLowerInvariant();
                        if (selectedExtensions.Contains(altExtension))
                        {
                            files.Add(altImage.ImageFile);
                        }
                    }
                }
            }
        }

        return files.DistinctBy(f => f.Path).ToList();
    }

    private async System.Threading.Tasks.Task DeleteFileToRecycleBinAsync(StorageFile file)
    {
        var settingsService = App.GetService<ISettingsService>();
        var useRecycleBin = settingsService.DeleteToRecycleBin;
        
        var isRemovableDrive = IsRemovableDrive(file.Path);
        
        if (isRemovableDrive)
        {
            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        else if (useRecycleBin)
        {
            await file.DeleteAsync(StorageDeleteOption.Default);
        }
        else
        {
            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
    }

    private static bool IsRemovableDrive(string filePath)
    {
        try
        {
            var driveLetter = Path.GetPathRoot(filePath);
            if (string.IsNullOrEmpty(driveLetter))
                return false;
            
            var driveInfo = new DriveInfo(driveLetter);
            return driveInfo.DriveType == DriveType.Removable;
        }
        catch
        {
            return false;
        }
    }

    private void RemoveDeletedImagesFromList(List<ImageFileInfo> deletedImages)
    {
        if (deletedImages.Count == 0)
            return;

        var firstVisibleIndex = GetFirstVisibleItemIndex();
        var selectedItem = ImageGridView.SelectedItem as ImageFileInfo;

        var groupsToProcess = new Dictionary<ImageGroup, List<ImageFileInfo>>();
        var singleImagesToRemove = new List<ImageFileInfo>();

        foreach (var deletedImage in deletedImages)
        {
            if (deletedImage.Group != null)
            {
                if (!groupsToProcess.ContainsKey(deletedImage.Group))
                {
                    groupsToProcess[deletedImage.Group] = new List<ImageFileInfo>();
                }
                groupsToProcess[deletedImage.Group].Add(deletedImage);
            }
            else
            {
                singleImagesToRemove.Add(deletedImage);
            }
        }

        foreach (var image in singleImagesToRemove)
        {
            ViewModel.Images.Remove(image);
        }

        foreach (var group in groupsToProcess.Keys)
        {
            var deletedInGroup = groupsToProcess[group];
            var oldPrimary = group.PrimaryImage;
            var remainingImages = group.Images.Where(img => !deletedInGroup.Contains(img)).ToList();

            if (remainingImages.Count > 0)
            {
                var newGroup = new ImageGroup(group.GroupName, remainingImages);
                var newPrimary = newGroup.PrimaryImage;
                var index = ViewModel.Images.IndexOf(oldPrimary);
                if (index >= 0)
                {
                    newPrimary.Rating = oldPrimary.Rating;
                    newPrimary.RatingSource = oldPrimary.RatingSource;
                    newPrimary.IsRatingLoading = false;
                    newPrimary.IsPendingDelete = false;
                    
                    ViewModel.Images[index] = newPrimary;
                    newPrimary.RefreshGroupProperties();
                }
            }
            else
            {
                ViewModel.Images.Remove(oldPrimary);
            }
        }

        ViewModel.ClearAllPendingDelete();

        DispatcherQueue.TryEnqueue(async () =>
        {
            if (ViewModel.Images.Count > 0)
            {
                var indexToScroll = Math.Min(firstVisibleIndex, ViewModel.Images.Count - 1);
                if (indexToScroll >= 0)
                {
                    await ScrollItemIntoViewAsync(ViewModel.Images[indexToScroll], "delete-restore", ScrollIntoViewAlignment.Default);
                }

                if (deletedImages.Contains(selectedItem))
                {
                    var newSelectedIndex = Math.Min(firstVisibleIndex, ViewModel.Images.Count - 1);
                    if (newSelectedIndex >= 0)
                    {
                        ExecuteProgrammaticSelectionChange(() => ImageGridView.SelectedItem = ViewModel.Images[newSelectedIndex]);
                    }
                }
            }

            QueueVisibleThumbnailLoad("delete-restore");
        });
    }

    private int GetFirstVisibleItemIndex()
    {
        try
        {
            AttachImageItemsWrapGrid();
            if (_imageItemsWrapGrid != null &&
                _imageItemsWrapGrid.FirstVisibleIndex >= 0 &&
                _imageItemsWrapGrid.FirstVisibleIndex < ViewModel.Images.Count)
            {
                return _imageItemsWrapGrid.FirstVisibleIndex;
            }

            if (_realizedImageItems.Count > 0)
            {
                return _realizedImageItems
                    .Select(image => ViewModel.Images.IndexOf(image))
                    .Where(index => index >= 0)
                    .DefaultIfEmpty(0)
                    .Min();
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        if (parent is ScrollViewer scrollViewer)
            return scrollViewer;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var result = FindScrollViewer(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private static ItemsWrapGrid? FindItemsWrapGrid(DependencyObject parent)
    {
        if (parent is ItemsWrapGrid itemsWrapGrid)
            return itemsWrapGrid;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var result = FindItemsWrapGrid(child);
            if (result != null)
                return result;
        }

        return null;
    }

    private void AttachImageItemsWrapGrid()
    {
        if (_imageItemsWrapGrid != null)
            return;

        _imageItemsWrapGrid = FindItemsWrapGrid(ImageGridView);
    }

    private void AttachImageGridScrollViewer()
    {
        if (_imageGridScrollViewer != null)
            return;

        _imageGridScrollViewer = FindScrollViewer(ImageGridView);
        if (_imageGridScrollViewer == null)
            return;

        _imageGridScrollViewer.ViewChanging += ImageGridScrollViewer_ViewChanging;
        _imageGridScrollViewer.ViewChanged += ImageGridScrollViewer_ViewChanged;
    }

    private void DetachImageGridScrollViewer()
    {
        if (_imageGridScrollViewer == null)
            return;

        _imageGridScrollViewer.ViewChanging -= ImageGridScrollViewer_ViewChanging;
        _imageGridScrollViewer.ViewChanged -= ImageGridScrollViewer_ViewChanged;
        _imageGridScrollViewer = null;
    }

    private void ImageGridScrollViewer_ViewChanging(object? sender, ScrollViewerViewChangingEventArgs e)
    {
        if (_isProgrammaticScrollActive)
            return;

        if (!_isUserScrollInProgress)
            System.Diagnostics.Debug.WriteLine("[MainPage] User scroll started");

        _isUserScrollInProgress = true;
        QueueVisibleThumbnailLoad("user-scroll");
    }

    private void ImageGridScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
            return;

        if (_isProgrammaticScrollActive)
        {
            System.Diagnostics.Debug.WriteLine("[MainPage] Programmatic scroll settled");
        }
        else if (_isUserScrollInProgress)
        {
            System.Diagnostics.Debug.WriteLine("[MainPage] User scroll settled");
        }

        _isUserScrollInProgress = false;
        QueueVisibleThumbnailLoad("view-changed");
    }

    private void QueueVisibleThumbnailLoad(string reason)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        AttachImageItemsWrapGrid();

        if (_imageItemsWrapGrid != null &&
            _imageItemsWrapGrid.FirstVisibleIndex >= 0 &&
            _imageItemsWrapGrid.LastVisibleIndex >= _imageItemsWrapGrid.FirstVisibleIndex)
        {
            var firstIndex = Math.Max(0, _imageItemsWrapGrid.FirstVisibleIndex);
            var lastIndex = Math.Min(ImageGridView.Items.Count - 1, _imageItemsWrapGrid.LastVisibleIndex);
            for (int i = firstIndex; i <= lastIndex; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo)
                {
                    _pendingVisibleThumbnailLoads.Add(imageInfo);
                }
            }
        }
        else
        {
            foreach (var imageInfo in _realizedImageItems)
            {
                _pendingVisibleThumbnailLoads.Add(imageInfo);
            }
        }

        if (_pendingVisibleThumbnailLoads.Count == 0)
            return;

        System.Diagnostics.Debug.WriteLine($"[MainPage] QueueVisibleThumbnailLoad reason={reason}, count={_pendingVisibleThumbnailLoads.Count}");
        _visibleThumbnailLoadTimer.Stop();
        _visibleThumbnailLoadTimer.Start();
    }

    private void QueueVisibleThumbnailLoad(ImageFileInfo imageInfo, string reason)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        _pendingVisibleThumbnailLoads.Add(imageInfo);
        System.Diagnostics.Debug.WriteLine($"[MainPage] QueueVisibleThumbnailLoad reason={reason}, item={imageInfo.ImageName}");
        _visibleThumbnailLoadTimer.Stop();
        _visibleThumbnailLoadTimer.Start();
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

        System.Diagnostics.Debug.WriteLine($"[MainPage] ScrollIntoView reason={reason}, item={imageInfo.ImageName}");

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

    private void ClearAllPendingDelete_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAllPendingDelete();
    }
}
