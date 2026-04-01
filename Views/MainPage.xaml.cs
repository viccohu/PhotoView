using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        SelectionService = new ImageSelectionService();
        InitializeComponent();
        FolderTreeView.DataContext = ViewModel;

        _loadImagesThrottleTimer = new DispatcherTimer();
        _loadImagesThrottleTimer.Interval = TimeSpan.FromMilliseconds(300);
        _loadImagesThrottleTimer.Tick += LoadImagesThrottleTimer_Tick;

        SelectionService.SelectionChanged += SelectionService_SelectionChanged;
        ViewModel.ImagesChanged += ViewModel_ImagesChanged;
        ViewModel.ThumbnailSizeChanged += ViewModel_ThumbnailSizeChanged;
        KeyDown += MainPage_KeyDown;
        Unloaded += MainPage_Unloaded;
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _loadImagesThrottleTimer.Stop();
        SelectionService.SelectionChanged -= SelectionService_SelectionChanged;
        ViewModel.ImagesChanged -= ViewModel_ImagesChanged;
        ViewModel.ThumbnailSizeChanged -= ViewModel_ThumbnailSizeChanged;
        ViewModel.Dispose();
    }

    private async void LoadImagesThrottleTimer_Tick(object? sender, object e)
    {
        _loadImagesThrottleTimer.Stop();

        if (_pendingLoadNode == null || AppLifetime.IsShuttingDown)
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
        if (ViewModel.Images == null || AppLifetime.IsShuttingDown)
            return;

        try
        {
            for (var i = 0; i < ImageRepeater.ItemsSourceView.Count; i++)
            {
                if (AppLifetime.IsShuttingDown)
                    return;

                if (ImageRepeater.TryGetElement(i) != null)
                {
                    if (ImageRepeater.ItemsSourceView.GetAt(i) is ImageFileInfo imageInfo)
                    {
                        await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
                    }
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
        _pendingLoadNode = node;
        _loadImagesThrottleTimer.Stop();
        _loadImagesThrottleTimer.Start();
    }

    private async void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            await ExpandTreeViewPathAsync(node);
            ThrottleLoadImages(node);
        }
    }

    private async System.Threading.Tasks.Task ExpandTreeViewPathAsync(FolderNode targetNode)
    {
        try
        {
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
                }
            }

            if (lastNode != null)
            {
                FolderTreeView.SelectedItem = lastNode;
                await System.Threading.Tasks.Task.Delay(100);
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

    private async void ImageRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        try
        {
            if (AppLifetime.IsShuttingDown)
                return;

            if (args.Index < 0 || args.Index >= sender.ItemsSourceView.Count)
                return;

            if (sender.ItemsSourceView.GetAt(args.Index) is ImageFileInfo imageInfo)
            {
                if (args.Element is ContentControl control && !_isUpdatingSelectionState)
                {
                    var isSelected = SelectionService.IsSelected(imageInfo);
                    VisualStateManager.GoToState(control, isSelected ? "Selected" : "Unselected", false);
                }

                await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ImageRepeater_ElementPrepared error: {ex}");
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

            for (var i = 0; i < ImageRepeater.ItemsSourceView.Count; i++)
            {
                if (ImageRepeater.TryGetElement(i) is ContentControl control)
                {
                    if (ImageRepeater.ItemsSourceView.GetAt(i) is ImageFileInfo image)
                    {
                        VisualStateManager.GoToState(control, image.IsSelected ? "Selected" : "Unselected", false);
                    }
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
