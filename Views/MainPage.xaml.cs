using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Helpers;
using PhotoView.Models;
using PhotoView.Services;
using PhotoView.ViewModels;
using System.Linq;
using Windows.System;

namespace PhotoView.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    public ImageSelectionService SelectionService { get; }

    private readonly DispatcherTimer _loadImagesThrottleTimer;
    private FolderNode? _pendingLoadNode;
    private bool _isUnloaded;

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        SelectionService = new ImageSelectionService();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        InitializeComponent();
        FolderTreeView.DataContext = ViewModel;

        _loadImagesThrottleTimer = new DispatcherTimer();
        _loadImagesThrottleTimer.Interval = TimeSpan.FromMilliseconds(300);
        _loadImagesThrottleTimer.Tick += LoadImagesThrottleTimer_Tick;

        SelectionService.SelectionChanged += SelectionService_SelectionChanged;
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
            return;

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

    private async void ViewModel_ThumbnailSizeChanged(object? sender, System.EventArgs e)
    {
        if (ViewModel.Images == null || _isUnloaded || AppLifetime.IsShuttingDown)
            return;

        try
        {
            foreach (var imageInfo in ViewModel.Images)
            {
                if (AppLifetime.IsShuttingDown)
                    return;

                imageInfo.UpdateDisplaySize(ViewModel.ThumbnailSize);
            }

            foreach (var control in GetVisibleImageControls())
            {
                if (control.DataContext is ImageFileInfo imageInfo)
                {
                    await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
                    if (_isUnloaded || AppLifetime.IsShuttingDown)
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ViewModel_ThumbnailSizeChanged error: {ex}");
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
                return;
            ThrottleLoadImages(node);
        }
    }

    private DateTime _lastClickTime;
    private FolderNode? _lastClickedNode;

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
                return;
            ThrottleLoadImages(node);
        }
    }

    private async System.Threading.Tasks.Task ExpandTreeViewPathAsync(FolderNode targetNode)
    {
        try
        {
            if (_isUnloaded || AppLifetime.IsShuttingDown)
                return;

            var path = new List<FolderNode>();
            var current = targetNode;
            while (current != null)
            {
                path.Insert(0, current);
                current = current.Parent;
            }

            if (path.Count == 0)
                return;

            FolderNode? lastNode = null;
            for (int i = 0; i < path.Count; i++)
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
                        return;
                }
            }

            if (lastNode != null)
            {
                FolderTreeView.SelectedItem = lastNode;
                await System.Threading.Tasks.Task.Delay(100);
                if (_isUnloaded || AppLifetime.IsShuttingDown)
                    return;
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

    private void ViewModel_ImagesChanged(object? sender, System.EventArgs e)
    {
        SelectionService.ClearSelection();
    }

    private bool _isUpdatingSelectionState;

    private async void ImageItem_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_isUnloaded || AppLifetime.IsShuttingDown)
                return;

            if (sender is ContentControl control && control.DataContext is ImageFileInfo imageInfo)
            {
                if (!_isUpdatingSelectionState)
                {
                    var isSelected = SelectionService.IsSelected(imageInfo);
                    VisualStateManager.GoToState(control, isSelected ? "Selected" : "Unselected", false);
                }

                await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ImageItem_Loaded error: {ex}");
        }
    }

    private void ImageItem_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        if (sender is ContentControl control && control.DataContext is ImageFileInfo imageInfo)
        {
            imageInfo.CancelThumbnailLoad();
        }
    }


    private void SelectionService_SelectionChanged(object? sender, Services.SelectionChangedEventArgs e)
    {
        if (_isUpdatingSelectionState)
            return;

        _isUpdatingSelectionState = true;
        try
        {
            foreach (var deselected in e.RemovedItems)
            {
                if (deselected is ImageFileInfo imageInfo)
                {
                    imageInfo.IsSelected = false;
                }
            }

            foreach (var selected in e.AddedItems)
            {
                if (selected is ImageFileInfo imageInfo)
                {
                    imageInfo.IsSelected = true;
                }
            }

            foreach (var control in GetVisibleImageControls())
            {
                if (control.DataContext is ImageFileInfo image)
                {
                    VisualStateManager.GoToState(control, image.IsSelected ? "Selected" : "Unselected", false);
                }
            }
        }
        finally
        {
            _isUpdatingSelectionState = false;
        }
    }



    private void ImageBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ContentControl control && control.DataContext is ImageFileInfo image)
        {
            var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var isShiftPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            SelectionService.HandleItemClick(image, isCtrlPressed, isShiftPressed);
            e.Handled = true;
        }
    }

    private void Image_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            FlyoutBase.ShowAttachedFlyout(element);
        }
    }

    private IEnumerable<ContentControl> GetVisibleImageControls()
    {
        for (var i = 0; i < ViewModel.Images.Count; i++)
        {
            if (ImageGridView.ContainerFromIndex(i) is GridViewItem container)
            {
                var control = FindDescendant<ContentControl>(container);
                if (control != null)
                {
                    yield return control;
                }
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
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
            var isCtrlPressed = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (isCtrlPressed)
            {
                SelectionService.SelectAll(ViewModel.Images.ToList());
                e.Handled = true;
            }
        }
        else if (e.Key == VirtualKey.Escape)
        {
            SelectionService.ClearSelection();
            e.Handled = true;
        }
    }
}
