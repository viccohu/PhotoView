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
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
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
    private readonly ShellToolbarService _shellToolbarService;
    private readonly IThumbnailService _thumbnailService;
    private readonly HashSet<ImageFileInfo> _pendingFastPreviewLoads = new();
    private readonly HashSet<ImageFileInfo> _pendingTargetThumbnailLoads = new();
    private readonly List<ImageFileInfo> _pendingWarmPreviewLoads = new();
    private readonly HashSet<ImageFileInfo> _queuedWarmPreviewLoads = new();
    private readonly HashSet<ImageFileInfo> _realizedImageItems = new();
    private readonly HashSet<ImageFileInfo> _immediateVisibleThumbnailLoads = new();
    private readonly HashSet<ImageFileInfo> _targetThumbnailRetainedItems = new();
    private readonly HashSet<ImageFileInfo> _selectedImageState = new();
    private const double GridViewItemMargin = 4d;
    private const double GridViewItemGap = GridViewItemMargin * 2d;
    private const double NavigationDrawerExpandedWidth = 265d;
    private const double NavigationDrawerCollapsedWidth = 25d;
    private const double FolderDrawerExpandedMaxHeight = 260d;
    private const double FolderDrawerCollapsedOffsetY = -8d;
    private const double ImageGridTopScrollTolerance = 1d;
    private const int FolderDrawerAnimationDurationMs = 220;
    private const int FastPreviewStartBudgetPerTick = 24;
    private const int TargetThumbnailStartBudgetPerTick = 8;
    private const int FastPreviewPrefetchScreenCount = 8;
    private const int TargetThumbnailPrefetchItemCount = 4;
    private const int TargetThumbnailRetainScreenCount = 2;
    private const int MaxTargetThumbnailRetainedItems = 160;
    private const int WarmPreviewIdleBudgetPerTick = 3;
    private const int WarmPreviewScrollBudgetPerTick = 1;
    private const int MaxActiveWarmPreviewLoads = 2;
    private const uint WarmPreviewLongSidePixels = 160;
    private static readonly Windows.UI.Color ToolbarActiveColor = Windows.UI.Color.FromArgb(0xFF, 0x08, 0x6e, 0xbe);
    private readonly PointerEventHandler _imageGridPointerWheelHandler;
    private readonly KeyEventHandler _imageGridKeyDownHandler;
    private readonly IKeyboardShortcutService _shortcutService;
    private FolderNode? _pendingLoadNode;
    private ScrollViewer? _imageGridScrollViewer;
    private ItemsWrapGrid? _imageItemsWrapGrid;
    private bool _isUnloaded;
    private bool _isProgrammaticScrollActive;
    private bool _isUserScrollInProgress;
    private bool _hasAttemptedRestoreLastFolder;
    private bool _isFolderDrawerExpanded = true;
    private bool _isFolderDrawerContentVisible;
    private bool _isNavigationDrawerPinnedCollapsed;
    private bool _isNavigationDrawerTemporarilyExpanded;
    private bool _isImageGridPointerWheelHandlerAttached;
    private bool _isImageGridKeyDownHandlerAttached;
    private bool _isHandlingDirectionalBurstNavigation;
    private bool _suppressNextImageDragStart;
    private double _lastImageGridVerticalOffset;
    private int _immediateVisibleThumbnailStartCount;
    private int _folderDrawerAnimationVersion;
    private int _thumbnailQueueVersion;
    private int _activeWarmPreviewLoads;
    private CancellationTokenSource _warmPreviewCts = new();
    private Storyboard? _navigationDrawerStoryboard;
    private Storyboard? _folderDrawerStoryboard;
    private (ImageFileInfo Image, uint Rating)? _pendingRatingUpdate;
    private (int Direction, ImageFileInfo? StartImage)? _pendingGridDirectionalNavigation;
    private ImageFileInfo? _lastSelectionRemovedImage;
    private ImageFileInfo? _lastSelectionAddedImage;
    private long _lastSelectionChangedTick;
    private ImageFileInfo? _storedImageFileInfo;
    private Controls.ImageViewerControl? _currentViewer;
    private Button? _shellDeleteButton;
    private ToggleButton? _shellAutoExpandBurstToggleButton;
    private SplitButton? _shellFilterSplitButton;

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        _settingsService = App.GetService<ISettingsService>();
        _shellToolbarService = App.GetService<ShellToolbarService>();
        _thumbnailService = App.GetService<IThumbnailService>();
        _shortcutService = App.GetService<IKeyboardShortcutService>();
        
        NavigationCacheMode = NavigationCacheMode.Enabled;
        InitializeComponent();
        _imageGridPointerWheelHandler = ImageGridView_PointerWheelChanged;
        _imageGridKeyDownHandler = ImageGridView_KeyDown;
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
        Unloaded += MainPage_Unloaded;
        
        _shortcutService.RegisterPageShortcutHandler("MainPage", HandleShortcut);
        
        if (ViewModel.FolderTree.Count > 0)
        {
            _ = TryRestoreLastFolderAsync();
        }
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        FilterFlyoutControl.FilterViewModel = ViewModel.Filter;
        RegisterShellToolbar();
        ViewModel.Filter.FilterChanged += Filter_FilterChanged;
        ImageGridView.DoubleTapped += ImageGridView_DoubleTapped;
        ImageGridView.Tapped += ImageGridView_Tapped;
        AttachImageGridKeyDown();
        AttachImageGridScrollViewer();
        AttachImageGridPointerWheel();
        UpdateImageGridTileSize();
        QueueVisibleThumbnailLoad("page-loaded");
        UpdateFilterButtonState();
        UpdateNavigationDrawerState(animate: false);
        UpdateFolderDrawerState(animate: false);
        ReattachActiveViewerAfterNavigation();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _shortcutService.SetCurrentPage("MainPage");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _shortcutService.SetCurrentPage("");
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
            var imageToFocus = _storedImageFileInfo;
            // 清理资源
            viewer.Closed -= ImageViewer_Closed;
            viewer.ViewModel.RatingUpdated -= ViewModel_RatingUpdated;
            if (ReferenceEquals(_currentViewer, viewer))
            {
                ViewerContainer.Content = null;
                _currentViewer = null;
            }

            if (imageToFocus != null)
            {
                await SelectImageForViewerAsync(imageToFocus, "viewer-close-focus", focusThumbnail: true);
            }

            await _settingsService.ResumeAlwaysDecodeRawPersistenceAsync("viewer-close");
        }
    }

    private void ReattachActiveViewerAfterNavigation()
    {
        if (ViewerContainer.Content is not Controls.ImageViewerControl viewer)
            return;

        _currentViewer = viewer;
        viewer.Closed -= ImageViewer_Closed;
        viewer.Closed += ImageViewer_Closed;
        viewer.ViewModel.RatingUpdated -= ViewModel_RatingUpdated;
        viewer.ViewModel.RatingUpdated += ViewModel_RatingUpdated;
        viewer.ReactivateAfterNavigation();
        _settingsService.SuspendAlwaysDecodeRawPersistence("viewer-return");
    }

    private void ViewModel_RatingUpdated(object? sender, (ImageFileInfo Image, uint Rating) e)
    {
        // System.Diagnostics.Debug.WriteLine($"[MainPage] ViewModel_RatingUpdated: 图片 {e.Image.ImageName} 评级已更新为 {e.Rating}");
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SubFolderCount) ||
            e.PropertyName == nameof(MainViewModel.HasSubFoldersInCurrentFolder) ||
            e.PropertyName == nameof(MainViewModel.CurrentSubFolders))
        {
            SetFolderDrawerExpanded(ViewModel.HasSubFoldersInCurrentFolder, "subfolders-changed");
        }
        else if (e.PropertyName == nameof(MainViewModel.PendingDeleteCount))
        {
            UpdateShellToolbarState();
        }
    }

    private async void RegisterShellToolbar()
    {
        await Task.Yield();
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

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
        ApplyToolbarButtonChrome(_shellFilterSplitButton);
        _shellFilterSplitButton.Click += FilterSplitButton_Click;
        toolbar.Children.Add(_shellFilterSplitButton);

        _shellAutoExpandBurstToggleButton = new ToggleButton
        {
            Padding = new Thickness(8),
            IsChecked = _settingsService.AutoExpandBurstOnDirectionalNavigation,
            Content = CreateToolbarFoldIcon()
        };
        ApplyToolbarButtonChrome(_shellAutoExpandBurstToggleButton);
        ApplyToolbarToggleButtonCheckedChrome(_shellAutoExpandBurstToggleButton);
        ToolTipService.SetToolTip(_shellAutoExpandBurstToggleButton, "方向键展开连拍堆叠");
        _shellAutoExpandBurstToggleButton.Checked += AutoExpandBurstToggleButton_CheckedChanged;
        _shellAutoExpandBurstToggleButton.Unchecked += AutoExpandBurstToggleButton_CheckedChanged;
        toolbar.Children.Add(_shellAutoExpandBurstToggleButton);

        var sizeButton = new DropDownButton
        {
            Padding = new Thickness(8),
            Content = CreateToolbarIcon("\uECA5"),
            Flyout = CreateThumbnailSizeFlyout()
        };
        ApplyToolbarButtonChrome(sizeButton);
        ToolTipService.SetToolTip(sizeButton, "缩略图大小");
        toolbar.Children.Add(sizeButton);

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
        ApplyToolbarButtonChrome(button);
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static void ApplyToolbarButtonChrome(Control control)
    {
        control.MinWidth = 40;
        control.Height = 40;
        control.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        control.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        control.BorderThickness = new Thickness(0);
    }

    private static void ApplyToolbarToggleButtonCheckedChrome(ToggleButton toggleButton)
    {
        var activeBrush = new SolidColorBrush(ToolbarActiveColor);
        var activePointerOverBrush = new SolidColorBrush(ToolbarActiveColor);
        var activePressedBrush = new SolidColorBrush(ToolbarActiveColor);
        var transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        toggleButton.Resources["ToggleButtonBackgroundChecked"] = activeBrush;
        toggleButton.Resources["ToggleButtonBackgroundCheckedPointerOver"] = activePointerOverBrush;
        toggleButton.Resources["ToggleButtonBackgroundCheckedPressed"] = activePressedBrush;
        toggleButton.Resources["ToggleButtonBorderBrushChecked"] = transparentBrush;
        toggleButton.Resources["ToggleButtonBorderBrushCheckedPointerOver"] = transparentBrush;
        toggleButton.Resources["ToggleButtonBorderBrushCheckedPressed"] = transparentBrush;
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

    private UIElement CreateToolbarFoldIcon()
    {
        return Resources["ToolbarFoldIconTemplate"] is DataTemplate template &&
            template.LoadContent() is UIElement icon
                ? icon
                : CreateToolbarIcon("\uE8B9");
    }

    private async void AutoExpandBurstToggleButton_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggleButton)
            return;

        var enabled = toggleButton.IsChecked == true;
        _settingsService.AutoExpandBurstOnDirectionalNavigation = enabled;

        try
        {
            await _settingsService.SaveAutoExpandBurstOnDirectionalNavigationAsync(enabled);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] save auto expand burst setting failed: {ex.Message}");
        }
    }

    private Flyout CreateFilterFlyout()
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom
        };
        flyout.Opening += (_, _) =>
        {
            if (flyout.Content == null)
            {
                flyout.Content = new FilterFlyout
                {
                    FilterViewModel = ViewModel.Filter
                };
            }
        };
        return flyout;
    }

    private MenuFlyout CreateThumbnailSizeFlyout()
    {
        var flyout = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.Bottom
        };

        foreach (var size in new[] { "Small", "Medium", "Large" })
        {
            var item = new MenuFlyoutItem
            {
                Text = size,
                Tag = size
            };
            item.Click += ThumbnailSize_Click;
            flyout.Items.Add(item);
        }

        return flyout;
    }

    private async void ImageGridView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element && element.DataContext is ImageFileInfo imageFileInfo)
        {
            await OpenImageViewerAsync(imageFileInfo, useConnectedAnimation: true);
            e.Handled = true;
        }
    }

    private async Task OpenImageViewerAsync(ImageFileInfo imageFileInfo, bool useConnectedAnimation)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        if (_currentViewer != null)
            return;

        AutoCollapseImageBrowsingChrome("viewer-open");
        await SelectImageForViewerAsync(imageFileInfo, "viewer-open", focusThumbnail: false);
        _storedImageFileInfo = imageFileInfo;

        var newViewer = new Controls.ImageViewerControl();
        ViewerContainer.Content = newViewer;
        _currentViewer = newViewer;
        _settingsService.SuspendAlwaysDecodeRawPersistence("viewer-open");

        newViewer.Closed += ImageViewer_Closed;
        newViewer.ViewModel.RatingUpdated += ViewModel_RatingUpdated;

        newViewer.PrepareContent(imageFileInfo);
        await newViewer.PrepareForAnimationAsync();

        if (useConnectedAnimation && ImageGridView.ContainerFromItem(imageFileInfo) is GridViewItem)
        {
            ImageGridView.PrepareConnectedAnimation("ForwardConnectedAnimation", imageFileInfo, "thumbnailImage");
        }

        var imageAnimation = ConnectedAnimationService.GetForCurrentView().GetAnimation("ForwardConnectedAnimation");
        if (imageAnimation != null)
        {
            imageAnimation.TryStart(newViewer.GetMainImage(), newViewer.GetCoordinatedElements());
        }

        await newViewer.ShowAfterAnimationAsync();
    }

    private async Task ToggleImageViewerForCurrentSelectionAsync()
    {
        if (_currentViewer != null)
        {
            _currentViewer.PrepareCloseAnimation();
            return;
        }

        var currentImage = GetCurrentImageForViewerOrSelection();
        if (currentImage != null)
        {
            await OpenImageViewerAsync(currentImage, useConnectedAnimation: true);
        }
    }

    private async Task SwitchViewerImageAsync(int delta)
    {
        var viewer = _currentViewer;
        if (viewer == null)
            return;

        var currentImage = GetCurrentImageForViewerOrSelection();
        if (currentImage == null)
            return;

        BurstPhotoGroup? burstGroupToCollapse = null;
        var nextImage = GetDirectionalNavigationImage(currentImage, delta, out burstGroupToCollapse);
        if (nextImage == null || ReferenceEquals(nextImage, currentImage))
        {
            if (nextImage == null)
                return;

            _storedImageFileInfo = nextImage;
            await SelectImageForViewerAsync(nextImage, "viewer-key-burst-expand", focusThumbnail: false);
            return;
        }

        _storedImageFileInfo = nextImage;
        await SelectImageForViewerAsync(nextImage, "viewer-key-switch", focusThumbnail: false);
        await viewer.SwitchImageAsync(nextImage);

        if (burstGroupToCollapse != null)
        {
            ViewModel.CollapseBurstGroup(burstGroupToCollapse);
            await SelectImageForViewerAsync(nextImage, "viewer-key-collapse-burst", focusThumbnail: false);
        }
    }

    private ImageFileInfo? GetDirectionalNavigationImage(
        ImageFileInfo currentImage,
        int delta,
        out BurstPhotoGroup? burstGroupToCollapse)
    {
        burstGroupToCollapse = null;

        if (!_settingsService.AutoExpandBurstOnDirectionalNavigation)
        {
            return GetAdjacentImage(currentImage, delta);
        }

        if (currentImage.BurstGroup is { Images.Count: > 1, IsExpanded: false })
        {
            var expandedTarget = ViewModel.ExpandBurstForDirectionalNavigation(currentImage, delta);
            return expandedTarget ?? currentImage;
        }

        var currentExpandedGroup = currentImage.BurstGroup?.IsExpanded == true
            ? currentImage.BurstGroup
            : null;
        var nextImage = GetAdjacentImage(currentImage, delta);
        if (nextImage == null)
            return null;

        if (nextImage.BurstGroup is { Images.Count: > 1, IsExpanded: false })
        {
            nextImage = ViewModel.ExpandBurstForDirectionalNavigation(nextImage, delta) ?? nextImage;
        }

        if (currentExpandedGroup != null && !ReferenceEquals(nextImage.BurstGroup, currentExpandedGroup))
        {
            burstGroupToCollapse = currentExpandedGroup;
        }

        return nextImage;
    }

    private async Task SelectImageForViewerAsync(ImageFileInfo imageInfo, string reason, bool focusThumbnail)
    {
        if (!ViewModel.Images.Contains(imageInfo))
            return;

        ExecuteProgrammaticSelectionChange(() =>
        {
            ImageGridView.SelectedItems.Clear();
            ImageGridView.SelectedItem = imageInfo;
        });

        await ScrollItemIntoViewAsync(imageInfo, reason, ScrollIntoViewAlignment.Default);

        if (focusThumbnail && ImageGridView.ContainerFromItem(imageInfo) is GridViewItem container)
        {
            container.Focus(FocusState.Programmatic);
        }
    }

    private ImageFileInfo? GetCurrentImageForViewerOrSelection()
    {
        if (_currentViewer != null && _storedImageFileInfo != null && ViewModel.Images.Contains(_storedImageFileInfo))
            return _storedImageFileInfo;

        if (ImageGridView.SelectedItem is ImageFileInfo selectedImage && ViewModel.Images.Contains(selectedImage))
            return selectedImage;

        var firstSelectedImage = ImageGridView.SelectedItems
            .OfType<ImageFileInfo>()
            .FirstOrDefault(ViewModel.Images.Contains);
        if (firstSelectedImage != null)
            return firstSelectedImage;

        return ViewModel.Images.FirstOrDefault();
    }

    private ImageFileInfo? GetAdjacentImage(ImageFileInfo currentImage, int delta)
    {
        if (ViewModel.Images.Count == 0)
            return null;

        var currentIndex = ViewModel.Images.IndexOf(currentImage);
        if (currentIndex < 0)
            return null;

        var nextIndex = Math.Clamp(currentIndex + delta, 0, ViewModel.Images.Count - 1);
        return ViewModel.Images[nextIndex];
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
            FilterSplitButton.Background = new SolidColorBrush(ToolbarActiveColor);
            if (_shellFilterSplitButton != null)
            {
                _shellFilterSplitButton.Background = new SolidColorBrush(ToolbarActiveColor);
            }
        }
        else
        {
            FilterSplitButton.ClearValue(Control.BackgroundProperty);
            if (_shellFilterSplitButton != null)
            {
                ApplyToolbarButtonChrome(_shellFilterSplitButton);
            }
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
        // System.Diagnostics.Debug.WriteLine($"[MainPage] FolderTreeLoaded 事件触发");
        await TryRestoreLastFolderAsync();
    }

    private async void ViewModel_SelectedFolderChanged(object? sender, FolderNode? node)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown || node == null)
            return;

        // System.Diagnostics.Debug.WriteLine($"[MainPage] SelectedFolderChanged 事件触发, 节点: {node.Name}");
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
                // System.Diagnostics.Debug.WriteLine($"[MainPage] 未启用记住上次路径，跳过恢复");
                return;
            }
            
            await System.Threading.Tasks.Task.Delay(100);
            waitCount++;
        }
        
        // System.Diagnostics.Debug.WriteLine($"[MainPage] FolderTreeLoaded 触发, RememberLastFolder={settingsService.RememberLastFolder}, LastFolderPath={settingsService.LastFolderPath}, 等待次数={waitCount}");
        
        if (!settingsService.RememberLastFolder || string.IsNullOrEmpty(settingsService.LastFolderPath))
        {
            // System.Diagnostics.Debug.WriteLine($"[MainPage] 路径为空或未启用，跳过恢复");
            return;
        }

        await System.Threading.Tasks.Task.Delay(200);
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        // System.Diagnostics.Debug.WriteLine($"[MainPage] 尝试恢复上次路径: {settingsService.LastFolderPath}");

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
            // System.Diagnostics.Debug.WriteLine($"[MainPage] 未找到上次路径: {settingsService.LastFolderPath}");
        }
    }

    private async System.Threading.Tasks.Task<FolderNode?> TryFindAndLoadNodeByPathAsync(string targetPath)
    {
        targetPath = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // System.Diagnostics.Debug.WriteLine($"[TryFindAndLoadNodeByPathAsync] 目标路径: {targetPath}");

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
                    if (child.FullPath != null &&
                        string.Equals(
                            child.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        currentNode = child;
                        break;
                    }
                }

                if (currentNode == null)
                {
                    foreach (var child in rootNode.Children)
                    {
                        if (child.FullPath != null && child.FullPath.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            currentNode = child;
                            break;
                        }
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

        // System.Diagnostics.Debug.WriteLine($"[TryFindAndLoadNodeByPathAsync] 最终找到节点: {currentNode.Name}, 完整路径: {currentNode.FullPath}");
        return currentNode;
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        _shellToolbarService.ClearToolbar(this);
        _shellDeleteButton = null;
        _shellAutoExpandBurstToggleButton = null;
        _shellFilterSplitButton = null;
        if (_currentViewer != null)
        {
            _currentViewer.Closed -= ImageViewer_Closed;
            _currentViewer.ViewModel.RatingUpdated -= ViewModel_RatingUpdated;
            _currentViewer = null;
            _ = _settingsService.ResumeAlwaysDecodeRawPersistenceAsync("main-page-unloaded");
        }

        ViewModel.Filter.FilterChanged -= Filter_FilterChanged;
        ImageGridView.DoubleTapped -= ImageGridView_DoubleTapped;
        ImageGridView.Tapped -= ImageGridView_Tapped;
        DetachImageGridKeyDown();
        _loadImagesThrottleTimer.Stop();
        _ratingDebounceTimer.Stop();
        _visibleThumbnailLoadTimer.Stop();
        StopNavigationDrawerAnimation();
        _pendingRatingUpdate = null;
        ClearThumbnailQueues();
        _realizedImageItems.Clear();
        _selectedImageState.Clear();
        DetachImageGridPointerWheel();
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

        ClearThumbnailQueues();
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
        // System.Diagnostics.Debug.WriteLine($"[MainPage] Tile size updated: size={tileSize:F0}, columns={columnCount}, width={availableWidth:F0}");

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
            if (sender.ContainerFromItem(node) is TreeViewItem treeViewItem
                && node.HasExpandableChildren
                && !treeViewItem.IsExpanded)
            {
                treeViewItem.IsExpanded = true;
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

    private void NavigationDrawerToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isNavigationDrawerPinnedCollapsed = !_isNavigationDrawerPinnedCollapsed;
        _isNavigationDrawerTemporarilyExpanded = false;
        UpdateNavigationDrawerState();
    }

    private void NavigationDrawerRoot_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!_isNavigationDrawerPinnedCollapsed || _isNavigationDrawerTemporarilyExpanded)
            return;

        _isNavigationDrawerTemporarilyExpanded = true;
        UpdateNavigationDrawerState();
    }

    private void NavigationDrawerRoot_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isNavigationDrawerPinnedCollapsed || !_isNavigationDrawerTemporarilyExpanded)
            return;

        _isNavigationDrawerTemporarilyExpanded = false;
        UpdateNavigationDrawerState();
    }

    private bool IsNavigationDrawerExpanded => !_isNavigationDrawerPinnedCollapsed || _isNavigationDrawerTemporarilyExpanded;

    private void UpdateNavigationDrawerState(bool animate = true)
    {
        var isExpanded = IsNavigationDrawerExpanded;
        var targetWidth = isExpanded ? NavigationDrawerExpandedWidth : NavigationDrawerCollapsedWidth;
        var targetChevronAngle = isExpanded ? 180d : 0d;

        if (isExpanded)
        {
            SetNavigationDrawerContentVisibility(Visibility.Visible);
        }

        if (!animate)
        {
            StopNavigationDrawerAnimation();
            NavigationDrawerRoot.Width = targetWidth;
            NavigationDrawerChevronTransform.Angle = targetChevronAngle;
            if (!isExpanded)
            {
                SetNavigationDrawerContentVisibility(Visibility.Collapsed);
            }
            return;
        }

        AnimateNavigationDrawer(targetWidth, targetChevronAngle, isExpanded);
    }

    private void AnimateNavigationDrawer(double targetWidth, double targetChevronAngle, bool isExpanding)
    {
        StopNavigationDrawerAnimation();

        var currentWidth = NavigationDrawerRoot.ActualWidth > 0
            ? NavigationDrawerRoot.ActualWidth
            : NavigationDrawerRoot.Width;
        if (double.IsNaN(currentWidth) || currentWidth <= 0)
        {
            currentWidth = isExpanding ? NavigationDrawerCollapsedWidth : NavigationDrawerExpandedWidth;
        }

        if (Math.Abs(currentWidth - targetWidth) < 0.5)
        {
            NavigationDrawerRoot.Width = targetWidth;
            NavigationDrawerChevronTransform.Angle = targetChevronAngle;
            if (!IsNavigationDrawerExpanded)
            {
                SetNavigationDrawerContentVisibility(Visibility.Collapsed);
            }
            return;
        }

        var widthAnimation = new DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(FolderDrawerAnimationDurationMs)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(widthAnimation, NavigationDrawerRoot);
        Storyboard.SetTargetProperty(widthAnimation, "Width");

        var chevronAnimation = new DoubleAnimation
        {
            From = NavigationDrawerChevronTransform.Angle,
            To = targetChevronAngle,
            Duration = new Duration(TimeSpan.FromMilliseconds(FolderDrawerAnimationDurationMs)),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(chevronAnimation, NavigationDrawerChevronTransform);
        Storyboard.SetTargetProperty(chevronAnimation, "Angle");

        var storyboard = new Storyboard();
        storyboard.Children.Add(widthAnimation);
        storyboard.Children.Add(chevronAnimation);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_navigationDrawerStoryboard, storyboard))
                return;

            NavigationDrawerRoot.Width = targetWidth;
            NavigationDrawerChevronTransform.Angle = targetChevronAngle;
            if (!IsNavigationDrawerExpanded)
            {
                SetNavigationDrawerContentVisibility(Visibility.Collapsed);
            }
            _navigationDrawerStoryboard = null;
        };

        _navigationDrawerStoryboard = storyboard;
        storyboard.Begin();
    }

    private void StopNavigationDrawerAnimation()
    {
        var storyboard = _navigationDrawerStoryboard;
        _navigationDrawerStoryboard = null;
        storyboard?.Stop();
    }

    private void SetNavigationDrawerContentVisibility(Visibility visibility)
    {
        NavigationDrawerHeader.Visibility = visibility;
        NavigationDrawerTree.Visibility = visibility;
    }

    private void UpdateFolderDrawerState(bool animate = true)
    {
        var hasFolders = ViewModel.HasSubFoldersInCurrentFolder;
        var shouldShowContent = hasFolders && _isFolderDrawerExpanded;
        FolderDrawerRoot.Visibility = Visibility.Visible;
        FolderDrawerOpenIcon.Visibility = shouldShowContent ? Visibility.Visible : Visibility.Collapsed;
        FolderDrawerClosedIcon.Visibility = shouldShowContent ? Visibility.Collapsed : Visibility.Visible;
        FolderDrawerChevronTransform.Angle = shouldShowContent ? 180d : 0d;
        FolderDrawerToggleButton.IsEnabled = hasFolders;

        if (!animate)
        {
            _folderDrawerAnimationVersion++;
            _folderDrawerStoryboard?.Stop();
            _folderDrawerStoryboard = null;
            _isFolderDrawerContentVisible = shouldShowContent;
            FolderDrawerContentHost.Visibility = shouldShowContent ? Visibility.Visible : Visibility.Collapsed;
            FolderDrawerContentHost.Opacity = shouldShowContent ? 1d : 0d;
            FolderDrawerContentHost.MaxHeight = shouldShowContent ? FolderDrawerExpandedMaxHeight : 0d;
            FolderDrawerContentTransform.Y = shouldShowContent ? 0d : FolderDrawerCollapsedOffsetY;
            SubFolderGridView.Visibility = shouldShowContent ? Visibility.Visible : Visibility.Collapsed;
            UpdateFolderDrawerContentClip(FolderDrawerContentHost.ActualWidth, FolderDrawerContentHost.ActualHeight);
            return;
        }

        if (_isFolderDrawerContentVisible == shouldShowContent)
        {
            return;
        }

        AnimateFolderDrawerContent(shouldShowContent);
    }

    private void SetFolderDrawerExpanded(bool expanded, string reason, bool animate = true)
    {
        if (!ViewModel.HasSubFoldersInCurrentFolder)
        {
            expanded = false;
        }

        var changed = _isFolderDrawerExpanded != expanded;
        _isFolderDrawerExpanded = expanded;
        if (changed)
        {
            // System.Diagnostics.Debug.WriteLine($"[MainPage] Folder drawer {(expanded ? "expanded" : "collapsed")}, reason={reason}");
        }

        UpdateFolderDrawerState(animate);
    }

    private bool CanAutoToggleFolderDrawer()
    {
        return ViewModel.Images.Count > 0 && ViewModel.HasSubFoldersInCurrentFolder;
    }

    private void AutoCollapseImageBrowsingChrome(string reason)
    {
        AutoCollapseNavigationDrawer(reason);
        AutoCollapseFolderDrawer(reason);
    }

    private void AutoExpandImageBrowsingChrome(string reason)
    {
        if (!CanAutoToggleFolderDrawer())
            return;

        AutoExpandNavigationDrawer(reason);
        AutoExpandFolderDrawer(reason);
    }

    private void AutoCollapseNavigationDrawer(string reason)
    {
        if (!_settingsService.MainPageAutoCollapseSidebar ||
            ViewModel.Images.Count == 0 ||
            _isNavigationDrawerPinnedCollapsed)
            return;

        _isNavigationDrawerPinnedCollapsed = true;
        _isNavigationDrawerTemporarilyExpanded = false;
        // System.Diagnostics.Debug.WriteLine($"[MainPage] Navigation drawer auto collapsed, reason={reason}");
        UpdateNavigationDrawerState();
    }

    private void AutoExpandNavigationDrawer(string reason)
    {
        if (!_settingsService.MainPageAutoCollapseSidebar ||
            ViewModel.Images.Count == 0 ||
            !_isNavigationDrawerPinnedCollapsed)
            return;

        _isNavigationDrawerPinnedCollapsed = false;
        _isNavigationDrawerTemporarilyExpanded = false;
        // System.Diagnostics.Debug.WriteLine($"[MainPage] Navigation drawer auto expanded, reason={reason}");
        UpdateNavigationDrawerState();
    }

    private void AutoCollapseFolderDrawer(string reason)
    {
        if (!CanAutoToggleFolderDrawer())
            return;

        SetFolderDrawerExpanded(false, reason);
    }

    private void AutoExpandFolderDrawer(string reason)
    {
        if (!CanAutoToggleFolderDrawer())
            return;

        SetFolderDrawerExpanded(true, reason);
    }

    private void AnimateFolderDrawerContent(bool showContent)
    {
        var animationVersion = ++_folderDrawerAnimationVersion;
        _isFolderDrawerContentVisible = showContent;
        _folderDrawerStoryboard?.Stop();
        _folderDrawerStoryboard = null;

        var currentMaxHeight = double.IsInfinity(FolderDrawerContentHost.MaxHeight)
            ? FolderDrawerContentHost.ActualHeight
            : FolderDrawerContentHost.MaxHeight;
        var fromHeight = Math.Max(0d, FolderDrawerContentHost.ActualHeight > 0d
            ? FolderDrawerContentHost.ActualHeight
            : currentMaxHeight);
        var toHeight = showContent ? FolderDrawerExpandedMaxHeight : 0d;

        if (showContent)
        {
            FolderDrawerContentHost.Visibility = Visibility.Visible;
            FolderDrawerContentHost.MaxHeight = fromHeight;
            SubFolderGridView.Visibility = Visibility.Visible;
        }
        else
        {
            FolderDrawerContentHost.MaxHeight = fromHeight;
        }

        var easing = new CubicEase
        {
            EasingMode = showContent ? EasingMode.EaseOut : EasingMode.EaseInOut
        };

        var opacityAnimation = new DoubleAnimation
        {
            From = FolderDrawerContentHost.Opacity,
            To = showContent ? 1d : 0d,
            Duration = new Duration(TimeSpan.FromMilliseconds(FolderDrawerAnimationDurationMs)),
            EasingFunction = easing
        };
        Storyboard.SetTarget(opacityAnimation, FolderDrawerContentHost);
        Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

        var heightAnimation = new DoubleAnimation
        {
            From = fromHeight,
            To = toHeight,
            Duration = new Duration(TimeSpan.FromMilliseconds(FolderDrawerAnimationDurationMs)),
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(heightAnimation, FolderDrawerContentHost);
        Storyboard.SetTargetProperty(heightAnimation, "MaxHeight");

        var translateAnimation = new DoubleAnimation
        {
            From = FolderDrawerContentTransform.Y,
            To = showContent ? 0d : FolderDrawerCollapsedOffsetY,
            Duration = new Duration(TimeSpan.FromMilliseconds(FolderDrawerAnimationDurationMs)),
            EasingFunction = easing
        };
        Storyboard.SetTarget(translateAnimation, FolderDrawerContentTransform);
        Storyboard.SetTargetProperty(translateAnimation, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(heightAnimation);
        storyboard.Children.Add(opacityAnimation);
        storyboard.Children.Add(translateAnimation);
        storyboard.Completed += (_, _) =>
        {
            if (animationVersion != _folderDrawerAnimationVersion)
            {
                return;
            }

            FolderDrawerContentHost.MaxHeight = showContent ? FolderDrawerExpandedMaxHeight : 0d;
            FolderDrawerContentHost.Opacity = showContent ? 1d : 0d;
            FolderDrawerContentHost.Visibility = showContent ? Visibility.Visible : Visibility.Collapsed;
            FolderDrawerContentTransform.Y = showContent ? 0d : FolderDrawerCollapsedOffsetY;
            SubFolderGridView.Visibility = showContent ? Visibility.Visible : Visibility.Collapsed;
            _folderDrawerStoryboard = null;
            UpdateFolderDrawerContentClip(FolderDrawerContentHost.ActualWidth, FolderDrawerContentHost.ActualHeight);
        };
        _folderDrawerStoryboard = storyboard;
        storyboard.Begin();
    }

    private void FolderDrawerContentHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateFolderDrawerContentClip(e.NewSize.Width, e.NewSize.Height);
    }

    private void UpdateFolderDrawerContentClip(double width, double height)
    {
        FolderDrawerContentClip.Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, width), Math.Max(0, height));
    }

    private void FolderDrawerToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasSubFoldersInCurrentFolder)
        {
            return;
        }

        SetFolderDrawerExpanded(!_isFolderDrawerExpanded, "manual-toggle");
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

            // System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 开始展开, 目标节点: {targetNode.Name}, 路径: {targetNode.FullPath}");

            var path = new List<FolderNode>();
            var current = targetNode;
            while (current != null)
            {
                path.Insert(0, current);
                // System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 路径节点: {current.Name}, NodeType={current.NodeType}, Parent={current.Parent?.Name ?? "null"}");
                current = current.Parent;
            }

            // System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 路径长度: {path.Count}");

            if (path.Count == 0)
            {
                return;
            }

            FolderNode? lastNode = null;
            for (var i = 0; i < path.Count; i++)
            {
                var node = path[i];
                lastNode = node;
                // System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 处理节点 {i}: {node.Name}, IsLoaded={node.IsLoaded}, IsExpanded={node.IsExpanded}, Children.Count={node.Children.Count}");

                if (i < path.Count - 1)
                {
                    if (!node.IsLoaded)
                    {
                        // System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 加载子节点: {node.Name}");
                        await ViewModel.LoadChildrenAsync(node);
                        if (_isUnloaded || AppLifetime.IsShuttingDown)
                        {
                            return;
                        }
                    }

                    if (!node.IsExpanded)
                    {
                        // System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 展开节点: {node.Name}");
                        node.IsExpanded = true;
                        await System.Threading.Tasks.Task.Delay(50);
                    }
                }

                var treeViewItem = FolderTreeView.ContainerFromItem(node) as TreeViewItem;
                // System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] TreeViewItem for {node.Name}: {(treeViewItem != null ? "found" : "null")}");
            }

            if (lastNode != null)
            {
                // System.Diagnostics.Debug.WriteLine($"[ExpandTreeViewPathAsync] 设置选中节点: {lastNode.Name}");
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
                // System.Diagnostics.Debug.WriteLine("[MainPage] ImagesChanged -> clearing transient grid state for empty collection");
                ClearGridViewSelection();
                ClearThumbnailQueues();
                _realizedImageItems.Clear();
                ResetImmediateVisibleThumbnailLoadState();
                ClearSelectedImageState();
            }
            else
            {
                QueueBackgroundPreviewWarmup(0, ViewModel.Images.Count - 1, prioritize: false);
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
            imageInfo.CancelTargetThumbnailLoad();
            _pendingFastPreviewLoads.Remove(imageInfo);
            _pendingTargetThumbnailLoads.Remove(imageInfo);
            _realizedImageItems.Remove(imageInfo);
            _immediateVisibleThumbnailLoads.Remove(imageInfo);
            return;
        }

        if (args.Phase == 0)
        {
            args.RegisterUpdateCallback(1u, ImageGridView_ContainerContentChanging);
        }
        else if (args.Phase == 1)
        {
            _realizedImageItems.Add(imageInfo);
            TryStartImmediateFastPreviewLoad(imageInfo, "container-phase1");
            QueueVisibleThumbnailLoad("container-phase1");
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
        var fastPreviewCandidates = GetPendingItemsByIndex(_pendingFastPreviewLoads);
        _pendingFastPreviewLoads.Clear();

        var fastPreviewStartedCount = 0;
        foreach (var imageInfo in fastPreviewCandidates)
        {
            if (imageInfo.HasFastPreview)
                continue;

            if (fastPreviewStartedCount >= FastPreviewStartBudgetPerTick)
            {
                _pendingFastPreviewLoads.Add(imageInfo);
                continue;
            }

            _ = imageInfo.EnsureFastPreviewAsync(size);
            fastPreviewStartedCount++;
        }

        var targetCandidates = GetPendingItemsByIndex(_pendingTargetThumbnailLoads);
        _pendingTargetThumbnailLoads.Clear();

        var targetStartedCount = 0;
        if (!_isUserScrollInProgress)
        {
            foreach (var imageInfo in targetCandidates)
            {
                if (!IsItemContainerRealized(imageInfo) || !IsItemInCurrentTargetRange(imageInfo))
                    continue;

                if (targetStartedCount >= TargetThumbnailStartBudgetPerTick)
                {
                    _pendingTargetThumbnailLoads.Add(imageInfo);
                    continue;
                }

                _ = LoadTargetThumbnailAsync(
                    imageInfo,
                    size,
                    Volatile.Read(ref _thumbnailQueueVersion));
                targetStartedCount++;
            }
        }
        else
        {
            foreach (var imageInfo in targetCandidates)
            {
                _pendingTargetThumbnailLoads.Add(imageInfo);
            }
        }

        StartWarmPreviewLoads(_isUserScrollInProgress ? WarmPreviewScrollBudgetPerTick : WarmPreviewIdleBudgetPerTick);

        if (HasPendingThumbnailWork())
        {
            _visibleThumbnailLoadTimer.Start();
        }
    }

    private void TryStartImmediateFastPreviewLoad(ImageFileInfo imageInfo, string reason)
    {
        if (_isUnloaded ||
            AppLifetime.IsShuttingDown ||
            _isProgrammaticScrollActive ||
            _immediateVisibleThumbnailStartCount >= FastPreviewStartBudgetPerTick)
        {
            return;
        }

        if (!IsItemContainerRealized(imageInfo) || !IsItemInCurrentVisibleRange(imageInfo))
            return;

        if (!_immediateVisibleThumbnailLoads.Add(imageInfo))
            return;

        _pendingFastPreviewLoads.Remove(imageInfo);
        _immediateVisibleThumbnailStartCount++;
        // System.Diagnostics.Debug.WriteLine($"[MainPage] Immediate thumbnail start reason={reason}, item={imageInfo.ImageName}, count={_immediateVisibleThumbnailStartCount}");
        _ = imageInfo.EnsureFastPreviewAsync(ViewModel.ThumbnailSize);
    }

    private async Task LoadTargetThumbnailAsync(
        ImageFileInfo imageInfo,
        ThumbnailSize size,
        int queueVersion)
    {
        try
        {
            DebugTargetThumbnail($"load start item={imageInfo.ImageName} queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailQueueVersion)} hasTarget={imageInfo.HasTargetThumbnail} userScroll={_isUserScrollInProgress}");
            await imageInfo.EnsureFastPreviewAsync(size);
            if (queueVersion != Volatile.Read(ref _thumbnailQueueVersion))
            {
                DebugTargetThumbnail($"load stop after fast item={imageInfo.ImageName} stale queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailQueueVersion)}");
                return;
            }

            await imageInfo.EnsureThumbnailAsync(size);
            if (queueVersion != Volatile.Read(ref _thumbnailQueueVersion))
            {
                DebugTargetThumbnail($"load stop after target item={imageInfo.ImageName} stale queueVersion={queueVersion} currentVersion={Volatile.Read(ref _thumbnailQueueVersion)}");
                return;
            }

            if (!_isUnloaded &&
                !AppLifetime.IsShuttingDown &&
                queueVersion == Volatile.Read(ref _thumbnailQueueVersion) &&
                IsItemInCurrentTargetRange(imageInfo))
            {
                if (_targetThumbnailRetainedItems.Add(imageInfo))
                {
                    DebugTargetThumbnail($"retain item={imageInfo.ImageName} count={_targetThumbnailRetainedItems.Count}");
                }
            }
            else
            {
                DebugTargetThumbnail($"load complete not retained item={imageInfo.ImageName} inRange={IsItemInCurrentTargetRange(imageInfo)} unloaded={_isUnloaded}");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] LoadTargetThumbnailAsync failed for {imageInfo.ImageName}: {ex.Message}");
        }
    }

    private bool IsItemInCurrentVisibleRange(ImageFileInfo imageInfo)
    {
        AttachImageItemsWrapGrid();
        if (_imageItemsWrapGrid == null ||
            _imageItemsWrapGrid.FirstVisibleIndex < 0 ||
            _imageItemsWrapGrid.LastVisibleIndex < _imageItemsWrapGrid.FirstVisibleIndex)
        {
            return true;
        }

        var index = ViewModel.Images.IndexOf(imageInfo);
        return index >= _imageItemsWrapGrid.FirstVisibleIndex &&
               index <= _imageItemsWrapGrid.LastVisibleIndex;
    }

    private void ResetImmediateVisibleThumbnailLoadState()
    {
        _immediateVisibleThumbnailLoads.Clear();
        _immediateVisibleThumbnailStartCount = 0;
    }

    private ImageFileInfo[] GetPendingItemsByIndex(HashSet<ImageFileInfo> pendingItems)
    {
        return pendingItems
            .Select(imageInfo => new
            {
                ImageInfo = imageInfo,
                Index = ViewModel.Images.IndexOf(imageInfo)
            })
            .Where(candidate => candidate.Index >= 0)
            .OrderBy(candidate => candidate.Index)
            .Select(candidate => candidate.ImageInfo)
            .ToArray();
    }

    private bool HasPendingThumbnailWork()
    {
        return _pendingFastPreviewLoads.Count > 0 ||
               _pendingTargetThumbnailLoads.Count > 0 ||
               _pendingWarmPreviewLoads.Count > 0;
    }

    private void ClearThumbnailQueues()
    {
        _warmPreviewCts.Cancel();
        _warmPreviewCts = new CancellationTokenSource();
        Interlocked.Increment(ref _thumbnailQueueVersion);
        _pendingFastPreviewLoads.Clear();
        _pendingTargetThumbnailLoads.Clear();
        _pendingWarmPreviewLoads.Clear();
        _queuedWarmPreviewLoads.Clear();
        _targetThumbnailRetainedItems.Clear();
        ResetImmediateVisibleThumbnailLoadState();
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

            if (!imageInfo.CanEditGridRating)
                return;

            if (imageInfo.IsRatingLoading && !imageInfo.IsRatingLoaded)
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
                ViewModel.RefreshBurstCoverAfterRatingChanged(imageInfo);
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

    private async void ImageGridView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_currentViewer != null ||
            _isHandlingDirectionalBurstNavigation ||
            !_settingsService.AutoExpandBurstOnDirectionalNavigation ||
            !TryGetDirectionalNavigationDelta(e.Key, out var direction))
        {
            return;
        }

        var currentImage = GetCurrentImageForViewerOrSelection();
        if (currentImage == null)
            return;

        var recentSelectionStart = GetRecentDirectionalSelectionStart();
        var recentSelectionTarget = GetRecentDirectionalSelectionTarget();

        if (recentSelectionStart?.BurstGroup is { Images.Count: > 1, IsExpanded: false } recentCollapsedStart &&
            !ReferenceEquals(recentSelectionTarget, recentSelectionStart))
        {
            e.Handled = true;
            await ExpandBurstForGridNavigationAsync(recentSelectionStart, direction, "grid-key-current-burst");
            return;
        }

        if (recentSelectionTarget?.BurstGroup is { Images.Count: > 1, IsExpanded: false })
        {
            e.Handled = true;
            await ExpandBurstForGridNavigationAsync(recentSelectionTarget, direction, "grid-key-enter-burst");
            return;
        }

        if (currentImage.BurstGroup is { Images.Count: > 1, IsExpanded: false })
        {
            e.Handled = true;
            await ExpandBurstForGridNavigationAsync(currentImage, direction, "grid-key-current-burst");
            return;
        }

        if (recentSelectionStart?.BurstGroup is { IsExpanded: true } recentExpandedStart &&
            !ReferenceEquals(recentSelectionTarget?.BurstGroup, recentExpandedStart))
        {
            e.Handled = true;
            await CollapseBurstAfterGridNavigationAsync(recentExpandedStart, recentSelectionTarget, "grid-key-leave-burst");
            return;
        }

        _pendingGridDirectionalNavigation = (direction, currentImage);
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Yield();
            await CompletePendingGridDirectionalNavigationAsync("grid-key");
        });
    }

    private ImageFileInfo? GetRecentDirectionalSelectionStart()
    {
        return Environment.TickCount64 - _lastSelectionChangedTick <= 250
            ? _lastSelectionRemovedImage
            : null;
    }

    private ImageFileInfo? GetRecentDirectionalSelectionTarget()
    {
        return Environment.TickCount64 - _lastSelectionChangedTick <= 250
            ? _lastSelectionAddedImage
            : null;
    }

    private async Task CompletePendingGridDirectionalNavigationAsync(string reason)
    {
        if (!_pendingGridDirectionalNavigation.HasValue ||
            _isHandlingDirectionalBurstNavigation ||
            !_settingsService.AutoExpandBurstOnDirectionalNavigation)
        {
            return;
        }

        var (direction, startImage) = _pendingGridDirectionalNavigation.Value;
        _pendingGridDirectionalNavigation = null;

        var currentImage = GetCurrentImageForViewerOrSelection();
        if (currentImage == null)
            return;

        if (currentImage.BurstGroup is { Images.Count: > 1, IsExpanded: false })
        {
            await ExpandBurstForGridNavigationAsync(currentImage, direction, $"{reason}-enter-burst");
            return;
        }

        if (startImage?.BurstGroup is { IsExpanded: true } startExpandedGroup &&
            !ReferenceEquals(currentImage.BurstGroup, startExpandedGroup))
        {
            await CollapseBurstAfterGridNavigationAsync(startExpandedGroup, currentImage, $"{reason}-leave-burst");
        }
    }

    private async Task ExpandBurstForGridNavigationAsync(ImageFileInfo imageInfo, int direction, string reason)
    {
        _isHandlingDirectionalBurstNavigation = true;
        try
        {
            var targetImage = ViewModel.ExpandBurstForDirectionalNavigation(imageInfo, direction);
            if (targetImage != null && ViewModel.Images.Contains(targetImage))
            {
                await SelectImageForViewerAsync(targetImage, reason, focusThumbnail: true);
            }
        }
        finally
        {
            _isHandlingDirectionalBurstNavigation = false;
        }
    }

    private async Task CollapseBurstAfterGridNavigationAsync(
        BurstPhotoGroup burstGroup,
        ImageFileInfo? targetImage,
        string reason)
    {
        if (targetImage == null || !ViewModel.Images.Contains(targetImage))
            return;

        _isHandlingDirectionalBurstNavigation = true;
        try
        {
            ViewModel.CollapseBurstGroup(burstGroup);
            if (ViewModel.Images.Contains(targetImage))
            {
                await SelectImageForViewerAsync(targetImage, reason, focusThumbnail: true);
            }
        }
        finally
        {
            _isHandlingDirectionalBurstNavigation = false;
        }
    }

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
            : $"{storageFiles.Count} 个文件";
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

        return new[] { dragImage };
    }

    private IEnumerable<ImageFileInfo> GetOrderedSelectedImagesForDrag()
    {
        var selected = ImageGridView.SelectedItems
            .OfType<ImageFileInfo>()
            .ToHashSet();

        if (selected.Count == 0)
            return Array.Empty<ImageFileInfo>();

        return ViewModel.Images.Where(selected.Contains).ToList();
    }

    private void SyncSelectedStateFromGridView(Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        _lastSelectionRemovedImage = e.RemovedItems.OfType<ImageFileInfo>().FirstOrDefault();
        _lastSelectionAddedImage = e.AddedItems.OfType<ImageFileInfo>().FirstOrDefault();
        _lastSelectionChangedTick = Environment.TickCount64;

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
                // System.Diagnostics.Debug.WriteLine($"[Selected] {imageInfo.ImageName}, Rating: {imageInfo.Rating}, Source: {imageInfo.RatingSource}, Dimensions: {imageInfo.Width}x{imageInfo.Height}, DisplaySize: {imageInfo.DisplayWidth:F0}x{imageInfo.DisplayHeight:F0}");
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

        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Yield();
            RestoreItemTopRelativeToScrollViewer(restoreImage, anchorTop);
        });
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

    private async void PinFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PinFolderAsync(_rightClickedFolderNode);
    }

    private async void UnpinFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.UnpinFolderAsync(_rightClickedFolderNode);
    }

    private async void PinSubFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PinFolderAsync(_rightClickedSubFolderNode);
    }

    private async void UnpinSubFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.UnpinFolderAsync(_rightClickedSubFolderNode);
    }

    private async void RefreshExternalDevices_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshExternalDevicesAsync();
    }

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

    private void AddFolderToPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedFolderNode == null || string.IsNullOrEmpty(_rightClickedFolderNode.FullPath))
            return;

        var previewWorkspace = App.GetService<PreviewWorkspaceService>();
        previewWorkspace.AddSource(_rightClickedFolderNode.FullPath);
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

    private void AddSubFolderToPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_rightClickedSubFolderNode == null || string.IsNullOrEmpty(_rightClickedSubFolderNode.FullPath))
            return;

        var previewWorkspace = App.GetService<PreviewWorkspaceService>();
        previewWorkspace.AddSource(_rightClickedSubFolderNode.FullPath);
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
        App.GetService<IThumbnailService>().Clear();
        await ViewModel.RefreshAsync();
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
        if (e.Key == VirtualKey.Space || e.Key == VirtualKey.Escape)
        {
            _currentViewer.PrepareCloseAnimation();
            return true;
        }
        else if (e.Key == VirtualKey.Left || e.Key == VirtualKey.Up)
        {
            _ = SwitchViewerImageAsync(-1);
            return true;
        }
        else if (e.Key == VirtualKey.Right || e.Key == VirtualKey.Down)
        {
            _ = SwitchViewerImageAsync(1);
            return true;
        }
        else if (e.Key >= VirtualKey.Number0 && e.Key <= VirtualKey.Number5)
        {
            _currentViewer.HandleRatingKey(e.Key);
            return true;
        }
        else if (e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad5)
        {
            _currentViewer.HandleRatingKey(e.Key - (VirtualKey.NumberPad0 - VirtualKey.Number0));
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
            var thumbnailService = App.GetService<IThumbnailService>();
            var failedCount = 0;

            for (int i = 0; i < filesToDelete.Count; i++)
            {
                var file = filesToDelete[i];
                try
                {
                    await DeleteFileToRecycleBinAsync(file);
                    thumbnailService.Invalidate(file);
                    
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

        var thumbnailService = App.GetService<IThumbnailService>();
        foreach (var deletedImage in deletedImages)
        {
            thumbnailService.Invalidate(deletedImage.ImageFile);
        }

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
            ViewModel.RemoveImagesFromLibrary(new[] { image });
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
                    
                    ViewModel.ReplaceImageInLibrary(oldPrimary, newPrimary);
                    newPrimary.RefreshGroupProperties();
                }
            }
            else
            {
                ViewModel.RemoveImagesFromLibrary(new[] { oldPrimary });
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

        _lastImageGridVerticalOffset = _imageGridScrollViewer.VerticalOffset;
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

    private void AttachImageGridKeyDown()
    {
        if (_isImageGridKeyDownHandlerAttached)
            return;

        ImageGridView.AddHandler(UIElement.KeyDownEvent, _imageGridKeyDownHandler, true);
        _isImageGridKeyDownHandlerAttached = true;
    }

    private void DetachImageGridKeyDown()
    {
        if (!_isImageGridKeyDownHandlerAttached)
            return;

        ImageGridView.RemoveHandler(UIElement.KeyDownEvent, _imageGridKeyDownHandler);
        _isImageGridKeyDownHandlerAttached = false;
    }

    private void AttachImageGridPointerWheel()
    {
        if (_isImageGridPointerWheelHandlerAttached)
            return;

        ImageGridView.AddHandler(UIElement.PointerWheelChangedEvent, _imageGridPointerWheelHandler, true);
        _isImageGridPointerWheelHandlerAttached = true;
    }

    private void DetachImageGridPointerWheel()
    {
        if (!_isImageGridPointerWheelHandlerAttached)
            return;

        ImageGridView.RemoveHandler(UIElement.PointerWheelChangedEvent, _imageGridPointerWheelHandler);
        _isImageGridPointerWheelHandlerAttached = false;
    }

    private void ImageGridScrollViewer_ViewChanging(object? sender, ScrollViewerViewChangingEventArgs e)
    {
        if (_isProgrammaticScrollActive)
            return;

        if (!_isUserScrollInProgress)
            // System.Diagnostics.Debug.WriteLine("[MainPage] User scroll started");

        _isUserScrollInProgress = true;
        HandleFolderDrawerScrollIntent(e.NextView.VerticalOffset, "view-changing");
        QueueVisibleThumbnailLoad("user-scroll");
    }

    private void ImageGridView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_currentViewer != null)
            return;

        var delta = e.GetCurrentPoint(ImageGridView).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        if (IsCtrlPressed())
        {
            ChangeThumbnailSizeByWheel(delta);
            e.Handled = true;
            return;
        }

        if (_isProgrammaticScrollActive)
            return;

        AttachImageGridScrollViewer();
        var verticalOffset = _imageGridScrollViewer?.VerticalOffset ?? _lastImageGridVerticalOffset;
        _lastImageGridVerticalOffset = verticalOffset;

        if (delta < 0)
        {
            AutoCollapseImageBrowsingChrome("wheel-down");
        }
        else if (verticalOffset <= ImageGridTopScrollTolerance)
        {
            AutoExpandImageBrowsingChrome("wheel-up-at-top");
        }
    }

    private static bool IsCtrlPressed()
    {
        return InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private void ChangeThumbnailSizeByWheel(int delta)
    {
        var nextSize = delta > 0
            ? GetLargerThumbnailSize(ViewModel.ThumbnailSize)
            : GetSmallerThumbnailSize(ViewModel.ThumbnailSize);

        if (nextSize == ViewModel.ThumbnailSize)
            return;

        ViewModel.ThumbnailSize = nextSize;
    }

    private static ThumbnailSize GetLargerThumbnailSize(ThumbnailSize size)
    {
        return size switch
        {
            ThumbnailSize.Small => ThumbnailSize.Medium,
            ThumbnailSize.Medium => ThumbnailSize.Large,
            _ => ThumbnailSize.Large
        };
    }

    private static ThumbnailSize GetSmallerThumbnailSize(ThumbnailSize size)
    {
        return size switch
        {
            ThumbnailSize.Large => ThumbnailSize.Medium,
            ThumbnailSize.Medium => ThumbnailSize.Small,
            _ => ThumbnailSize.Small
        };
    }

    private void HandleFolderDrawerScrollIntent(double nextVerticalOffset, string reason)
    {
        var previousOffset = _lastImageGridVerticalOffset;
        var delta = nextVerticalOffset - previousOffset;
        _lastImageGridVerticalOffset = nextVerticalOffset;

        if (delta > ImageGridTopScrollTolerance)
        {
            AutoCollapseImageBrowsingChrome(reason);
        }
        else if (delta < -ImageGridTopScrollTolerance &&
                 previousOffset <= ImageGridTopScrollTolerance &&
                 nextVerticalOffset <= ImageGridTopScrollTolerance)
        {
            AutoExpandImageBrowsingChrome($"{reason}-top");
        }
    }

    private void ImageGridScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
            return;

        if (_isProgrammaticScrollActive)
        {
            // System.Diagnostics.Debug.WriteLine("[MainPage] Programmatic scroll settled");
        }
        else if (_isUserScrollInProgress)
        {
            // System.Diagnostics.Debug.WriteLine("[MainPage] User scroll settled");
        }

        _isUserScrollInProgress = false;
        _lastImageGridVerticalOffset = _imageGridScrollViewer?.VerticalOffset ?? _lastImageGridVerticalOffset;
        QueueVisibleThumbnailLoad("view-changed");
    }

    private void QueueVisibleThumbnailLoad(string reason)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        AttachImageItemsWrapGrid();

        if (TryGetVisibleIndexRange(out var firstVisibleIndex, out var lastVisibleIndex))
        {
            var visibleCount = Math.Max(1, lastVisibleIndex - firstVisibleIndex + 1);
            var fastPreviewPadding = Math.Max(visibleCount * FastPreviewPrefetchScreenCount, FastPreviewStartBudgetPerTick);
            var fastPreviewFirstIndex = Math.Max(0, firstVisibleIndex - fastPreviewPadding);
            var fastPreviewLastIndex = Math.Min(ImageGridView.Items.Count - 1, lastVisibleIndex + fastPreviewPadding);

            for (int i = fastPreviewFirstIndex; i <= fastPreviewLastIndex; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo && !imageInfo.HasFastPreview)
                {
                    _pendingFastPreviewLoads.Add(imageInfo);
                }
            }

            var targetFirstIndex = Math.Max(0, firstVisibleIndex - TargetThumbnailPrefetchItemCount);
            var targetLastIndex = Math.Min(ImageGridView.Items.Count - 1, lastVisibleIndex + TargetThumbnailPrefetchItemCount);
            for (int i = targetFirstIndex; i <= targetLastIndex; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo && !imageInfo.HasTargetThumbnail)
                {
                    _pendingTargetThumbnailLoads.Add(imageInfo);
                }
            }
            TrimTargetThumbnails(targetFirstIndex, targetLastIndex);

            QueueBackgroundPreviewWarmup(firstVisibleIndex, lastVisibleIndex, prioritize: true);
        }
        else
        {
            var realizedFallbackLimit = FastPreviewStartBudgetPerTick + TargetThumbnailPrefetchItemCount;
            foreach (var imageInfo in _realizedImageItems
                .Select(imageInfo => new
                {
                    ImageInfo = imageInfo,
                    Index = ViewModel.Images.IndexOf(imageInfo)
                })
                .Where(candidate => candidate.Index >= 0)
                .OrderBy(candidate => candidate.Index)
                .Take(realizedFallbackLimit)
                .Select(candidate => candidate.ImageInfo))
            {
                if (!imageInfo.HasFastPreview)
                {
                    _pendingFastPreviewLoads.Add(imageInfo);
                }

                if (!imageInfo.HasTargetThumbnail)
                {
                    _pendingTargetThumbnailLoads.Add(imageInfo);
                }
            }

            QueueBackgroundPreviewWarmup(0, Math.Max(0, ImageGridView.Items.Count - 1), prioritize: false);
        }

        if (!HasPendingThumbnailWork())
            return;

        _visibleThumbnailLoadTimer.Stop();
        _visibleThumbnailLoadTimer.Start();
    }

    private void QueueVisibleThumbnailLoad(ImageFileInfo imageInfo, string reason)
    {
        if (_isUnloaded || AppLifetime.IsShuttingDown)
            return;

        if (!imageInfo.HasFastPreview)
        {
            _pendingFastPreviewLoads.Add(imageInfo);
        }

        if (!imageInfo.HasTargetThumbnail)
        {
            _pendingTargetThumbnailLoads.Add(imageInfo);
        }

        QueueBackgroundPreviewWarmup(imageInfo, prioritize: true);
        // System.Diagnostics.Debug.WriteLine($"[MainPage] QueueVisibleThumbnailLoad reason={reason}, item={imageInfo.ImageName}");
        _visibleThumbnailLoadTimer.Stop();
        _visibleThumbnailLoadTimer.Start();
    }

    private bool TryGetVisibleIndexRange(out int firstVisibleIndex, out int lastVisibleIndex)
    {
        AttachImageItemsWrapGrid();

        if (_imageItemsWrapGrid != null &&
            _imageItemsWrapGrid.FirstVisibleIndex >= 0 &&
            _imageItemsWrapGrid.LastVisibleIndex >= _imageItemsWrapGrid.FirstVisibleIndex &&
            ImageGridView.Items.Count > 0)
        {
            firstVisibleIndex = Math.Clamp(_imageItemsWrapGrid.FirstVisibleIndex, 0, ImageGridView.Items.Count - 1);
            lastVisibleIndex = Math.Clamp(_imageItemsWrapGrid.LastVisibleIndex, firstVisibleIndex, ImageGridView.Items.Count - 1);
            return true;
        }

        firstVisibleIndex = -1;
        lastVisibleIndex = -1;
        return false;
    }

    private bool IsItemInCurrentTargetRange(ImageFileInfo imageInfo)
    {
        if (!TryGetVisibleIndexRange(out var firstVisibleIndex, out var lastVisibleIndex))
            return IsItemContainerRealized(imageInfo);

        var index = ViewModel.Images.IndexOf(imageInfo);
        if (index < 0)
            return false;

        var firstIndex = Math.Max(0, firstVisibleIndex - TargetThumbnailPrefetchItemCount);
        var lastIndex = Math.Min(ViewModel.Images.Count - 1, lastVisibleIndex + TargetThumbnailPrefetchItemCount);
        return index >= firstIndex && index <= lastIndex;
    }

    private void QueueBackgroundPreviewWarmup(int firstVisibleIndex, int lastVisibleIndex, bool prioritize)
    {
        var itemCount = ImageGridView.Items.Count;
        if (itemCount == 0)
            return;

        if (prioritize)
        {
            var visibleCount = Math.Max(1, lastVisibleIndex - firstVisibleIndex + 1);
            var padding = Math.Max(visibleCount * FastPreviewPrefetchScreenCount, FastPreviewStartBudgetPerTick);
            var firstIndex = Math.Max(0, firstVisibleIndex - padding);
            var lastIndex = Math.Min(itemCount - 1, lastVisibleIndex + padding);

            for (int i = firstIndex; i <= lastIndex; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo)
                {
                    QueueBackgroundPreviewWarmup(imageInfo, prioritize: true);
                }
            }
        }

        if (!prioritize || _pendingWarmPreviewLoads.Count == 0)
        {
            for (int i = 0; i < itemCount; i++)
            {
                if (ImageGridView.Items[i] is ImageFileInfo imageInfo)
                {
                    QueueBackgroundPreviewWarmup(imageInfo, prioritize: false);
                }
            }
        }
    }

    private void QueueBackgroundPreviewWarmup(ImageFileInfo imageInfo, bool prioritize)
    {
        if (imageInfo.HasFastPreview)
            return;

        if (_queuedWarmPreviewLoads.Contains(imageInfo))
        {
            if (prioritize && _pendingWarmPreviewLoads.Remove(imageInfo))
            {
                _pendingWarmPreviewLoads.Insert(0, imageInfo);
            }
            return;
        }

        _queuedWarmPreviewLoads.Add(imageInfo);
        if (prioritize)
        {
            _pendingWarmPreviewLoads.Insert(0, imageInfo);
        }
        else
        {
            _pendingWarmPreviewLoads.Add(imageInfo);
        }
    }

    private void TrimTargetThumbnails(int firstIndex, int lastIndex)
    {
        if (_targetThumbnailRetainedItems.Count == 0)
            return;

        var retainFirstIndex = firstIndex;
        var retainLastIndex = lastIndex;
        var centerIndex = (firstIndex + lastIndex) / 2d;

        if (TryGetVisibleIndexRange(out var visibleFirstIndex, out var visibleLastIndex))
        {
            var visibleCount = Math.Max(1, visibleLastIndex - visibleFirstIndex + 1);
            var retainPadding = (visibleCount * TargetThumbnailRetainScreenCount) + TargetThumbnailPrefetchItemCount;
            retainFirstIndex = Math.Max(0, visibleFirstIndex - retainPadding);
            retainLastIndex = Math.Min(ViewModel.Images.Count - 1, visibleLastIndex + retainPadding);
            centerIndex = (visibleFirstIndex + visibleLastIndex) / 2d;
        }

        foreach (var imageInfo in _targetThumbnailRetainedItems.ToArray())
        {
            var index = ViewModel.Images.IndexOf(imageInfo);
            if (index < 0)
            {
                _targetThumbnailRetainedItems.Remove(imageInfo);
                DebugTargetThumbnail($"drop missing item={imageInfo.ImageName} count={_targetThumbnailRetainedItems.Count}");
            }
        }

        if (_targetThumbnailRetainedItems.Count <= MaxTargetThumbnailRetainedItems)
            return;

        var overflowCandidates = _targetThumbnailRetainedItems
            .Select(imageInfo => new
            {
                ImageInfo = imageInfo,
                Index = ViewModel.Images.IndexOf(imageInfo)
            })
            .Where(candidate => candidate.Index >= 0)
            .OrderByDescending(candidate => Math.Abs(candidate.Index - centerIndex))
            .ToArray();

        foreach (var candidate in overflowCandidates)
        {
            if (_targetThumbnailRetainedItems.Count <= MaxTargetThumbnailRetainedItems)
                break;

            if (candidate.Index >= retainFirstIndex && candidate.Index <= retainLastIndex)
                continue;

            DowngradeRetainedTargetThumbnail(candidate.ImageInfo, candidate.Index, "retain-cap");
        }
    }

    private void DowngradeRetainedTargetThumbnail(ImageFileInfo imageInfo, int index, string reason)
    {
        imageInfo.DowngradeToFastPreview();
        _targetThumbnailRetainedItems.Remove(imageInfo);
        DebugTargetThumbnail($"downgrade reason={reason} item={imageInfo.ImageName} index={index} count={_targetThumbnailRetainedItems.Count}");
    }

    private void StartWarmPreviewLoads(int budget)
    {
        var startedCount = 0;
        while (startedCount < budget &&
               _activeWarmPreviewLoads < MaxActiveWarmPreviewLoads &&
               _pendingWarmPreviewLoads.Count > 0)
        {
            var imageInfo = _pendingWarmPreviewLoads[0];
            _pendingWarmPreviewLoads.RemoveAt(0);
            _queuedWarmPreviewLoads.Remove(imageInfo);

            if (imageInfo.HasFastPreview || ViewModel.Images.IndexOf(imageInfo) < 0)
                continue;

            var queueVersion = Volatile.Read(ref _thumbnailQueueVersion);
            var cancellationToken = _warmPreviewCts.Token;
            if (cancellationToken.IsCancellationRequested)
                break;

            System.Threading.Interlocked.Increment(ref _activeWarmPreviewLoads);
            _ = WarmFastPreviewAsync(imageInfo, queueVersion, cancellationToken);
            startedCount++;
        }
    }

    private async Task WarmFastPreviewAsync(
        ImageFileInfo imageInfo,
        int queueVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailService.WarmFastPreviewAsync(
                imageInfo.ImageFile,
                WarmPreviewLongSidePixels,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] WarmFastPreviewAsync failed for {imageInfo.ImageName}: {ex.Message}");
        }
        finally
        {
            System.Threading.Interlocked.Decrement(ref _activeWarmPreviewLoads);
            if (!_isUnloaded &&
                !AppLifetime.IsShuttingDown &&
                !cancellationToken.IsCancellationRequested &&
                queueVersion == Volatile.Read(ref _thumbnailQueueVersion) &&
                HasPendingThumbnailWork())
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_isUnloaded &&
                        !AppLifetime.IsShuttingDown &&
                        !cancellationToken.IsCancellationRequested &&
                        queueVersion == Volatile.Read(ref _thumbnailQueueVersion) &&
                        HasPendingThumbnailWork())
                    {
                        _visibleThumbnailLoadTimer.Stop();
                        _visibleThumbnailLoadTimer.Start();
                    }
                });
            }
        }
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

        // System.Diagnostics.Debug.WriteLine($"[MainPage] ScrollIntoView reason={reason}, item={imageInfo.ImageName}");

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

    [System.Diagnostics.Conditional("DEBUG")]
    private static void DebugTargetThumbnail(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[MainPageTargetThumb] {DateTime.Now:HH:mm:ss.fff} {message}");
    }

    private void ClearAllPendingDelete_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAllPendingDelete();
    }
}
