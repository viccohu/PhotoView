using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        SelectionService = new ImageSelectionService();
        InitializeComponent();
        FolderTreeView.DataContext = ViewModel;

        SelectionService.SelectionChanged += SelectionService_SelectionChanged;
        ViewModel.ImagesChanged += ViewModel_ImagesChanged;
        ViewModel.ThumbnailSizeChanged += ViewModel_ThumbnailSizeChanged;
        KeyDown += MainPage_KeyDown;
    }

    private async void ViewModel_ThumbnailSizeChanged(object? sender, System.EventArgs e)
    {
        if (ViewModel.Images == null)
            return;

        try
        {
            // 首先清除所有已加载的缩略图
            foreach (var imageInfo in ViewModel.Images)
            {
                imageInfo.ClearThumbnail();
            }

            // 仅加载当前可见区域的缩略图
            for (var i = 0; i < ImageRepeater.ItemsSourceView.Count; i++)
            {
                if (ImageRepeater.TryGetElement(i) != null)
                {
                    if (ImageRepeater.ItemsSourceView.GetAt(i) is ImageFileInfo imageInfo)
                    {
                        await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
                    }
                }
            }
        }
        catch (Exception)
        {
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
            await ViewModel.LoadChildrenAsync(node);
        }
    }

    private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FolderNode node)
        {
            await ViewModel.LoadImagesAsync(node);
        }
    }

    private async void BreadcrumbBar_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            await ViewModel.LoadImagesAsync(node);
        }
    }

    private void ViewModel_ImagesChanged(object? sender, System.EventArgs e)
    {
        SelectionService.ClearSelection();
    }

    private async void ImageRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        try
        {
            if (args.Index < 0 || args.Index >= sender.ItemsSourceView.Count)
                return;

            if (sender.ItemsSourceView.GetAt(args.Index) is ImageFileInfo imageInfo)
            {
                // 设置选择状态
                if (args.Element is ContentControl control)
                {
                    var isSelected = SelectionService.IsSelected(imageInfo);
                    VisualStateManager.GoToState(control, isSelected ? "Selected" : "Unselected", false);
                }

                await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
            }
        }
        catch (Exception)
        {
        }
    }



    private void SelectionService_SelectionChanged(object? sender, Services.SelectionChangedEventArgs e)
    {
        // 清除之前选择的项
        foreach (var deselected in e.DeselectedItems)
        {
            if (deselected is ImageFileInfo imageInfo)
            {
                imageInfo.IsSelected = false;
            }
        }

        // 设置新选择的项
        foreach (var selected in e.SelectedItems)
        {
            if (selected is ImageFileInfo imageInfo)
            {
                imageInfo.IsSelected = true;
            }
        }

        // 仅更新可见元素的 VisualState
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

    private void UpdateItemSelectionStates()
    {
        if (ImageRepeater.ItemsSource == null)
            return;

        for (var i = 0; i < ImageRepeater.ItemsSourceView.Count; i++)
        {
            if (ImageRepeater.TryGetElement(i) is ContentControl control)
            {
                var image = ImageRepeater.ItemsSourceView.GetAt(i) as ImageFileInfo;
                var isSelected = image != null && SelectionService.IsSelected(image);
                VisualStateManager.GoToState(control, isSelected ? "Selected" : "Unselected", false);
            }
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
