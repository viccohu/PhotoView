using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using PhotoView.Contracts.Services;
using PhotoView.Dialogs;
using PhotoView.Helpers;
using PhotoView.Models;
using PhotoView.Services;
using PhotoView.ViewModels;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace PhotoView.Views;

public sealed partial class CollectPage : Page
{
    private const double LoadDrawerExpandedWidth = 292;
    private const double LoadDrawerCollapsedWidth = 25;
    private const double InfoDrawerExpandedWidth = 340;
    private const double InfoDrawerCollapsedWidth = 0;
    private const int VisibleThumbnailStartBudgetPerTick = 16;
    private const int VisibleThumbnailPrefetchItemCount = 12;
    private const int DirectionalNavigationRepeatIntervalMs = 180;
    private readonly DispatcherTimer _visibleThumbnailLoadTimer;
    private readonly DispatcherTimer _ratingDebounceTimer;
    private readonly IKeyboardShortcutService _shortcutService;
    private readonly HashSet<ImageFileInfo> _pendingVisibleThumbnailLoads = new();
    private readonly HashSet<ImageFileInfo> _realizedImageItems = new();
    private readonly Dictionary<RatingControl, bool> _ratingControlEventMap = new();
    private readonly ISettingsService _settingsService;
    private readonly ShellToolbarService _shellToolbarService;
    private FolderNode? _rightClickedFolderNode;
    private ItemsStackPanel? _thumbnailItemsPanel;
    private ScrollViewer? _thumbnailScrollViewer;
    private PointerEventHandler? _thumbnailWheelHandler;
    private KeyEventHandler? _thumbnailPreviewKeyDownHandler;
    private Storyboard? _loadDrawerStoryboard;
    private Storyboard? _infoDrawerStoryboard;
    private bool _isUnloaded;
    private bool _isDisposed;
    private bool _isUpdatingZoomSlider;
    private bool _isLoadDrawerPinnedCollapsed;
    private bool _isLoadDrawerTemporarilyExpanded;
    private bool _suppressNextThumbnailDragStart;
    private Button? _shellDeleteButton;
    private SplitButton? _shellFilterSplitButton;
    private Microsoft.UI.Xaml.Shapes.Rectangle? _shellFilterActiveIndicator;
    private (ImageFileInfo Image, uint Rating)? _pendingRatingUpdate;
    private int _selectedThumbnailLoadVersion;
    private int _lastDirectionalNavigationDirection;
    private long _lastDirectionalNavigationTick;

    public CollectViewModel ViewModel
    {
        get;
    }

    public ImageViewerViewModel PreviewInfoViewModel
    {
        get;
    }

    public CollectPage()
    {
        ViewModel = App.GetService<CollectViewModel>();
        PreviewInfoViewModel = App.GetService<ImageViewerViewModel>();
        _settingsService = App.GetService<ISettingsService>();
        _shellToolbarService = App.GetService<ShellToolbarService>();
        _shortcutService = App.GetService<IKeyboardShortcutService>();
        
        NavigationCacheMode = NavigationCacheMode.Disabled;
        InitializeComponent();
        DataContext = ViewModel;
        UpdateInfoDrawerState(animate: false);
        UpdateZoomValueButtonContent(100d);
        _visibleThumbnailLoadTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _visibleThumbnailLoadTimer.Tick += VisibleThumbnailLoadTimer_Tick;
        _ratingDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _ratingDebounceTimer.Tick += RatingDebounceTimer_Tick;
        ViewModel.Images.CollectionChanged += Images_CollectionChanged;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Filter.FilterChanged += Filter_FilterChanged;
        PreviewInfoViewModel.RatingUpdated += PreviewInfoViewModel_RatingUpdated;
        PreviewCanvas.ZoomPercentChanged += PreviewCanvas_ZoomPercentChanged;
        _thumbnailWheelHandler = PreviewThumbnailGridView_PointerWheelChanged;
        PreviewThumbnailGridView.AddHandler(UIElement.PointerWheelChangedEvent, _thumbnailWheelHandler, true);
        _thumbnailPreviewKeyDownHandler = PreviewThumbnailGridView_PreviewKeyDown;
        PreviewThumbnailGridView.AddHandler(UIElement.PreviewKeyDownEvent, _thumbnailPreviewKeyDownHandler, true);
        Loaded += CollectPage_Loaded;
        Unloaded += CollectPage_Unloaded;
        
        _shortcutService.RegisterPageShortcutHandler("CollectPage", HandleShortcut);
    }

    private void CollectPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isDisposed)
            return;

        _isUnloaded = false;
        RegisterShellToolbar();
        UpdateSelectedImageUi();
        UpdateLoadDrawerState(animate: false);
        QueueVisibleThumbnailLoad("page-loaded");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _shortcutService.SetCurrentPage("CollectPage");
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _shortcutService.SetCurrentPage("");
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
        _shellFilterActiveIndicator = null;
        _visibleThumbnailLoadTimer.Tick -= VisibleThumbnailLoadTimer_Tick;
        _ratingDebounceTimer.Tick -= RatingDebounceTimer_Tick;
        ViewModel.Images.CollectionChanged -= Images_CollectionChanged;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Filter.FilterChanged -= Filter_FilterChanged;
        PreviewInfoViewModel.RatingUpdated -= PreviewInfoViewModel_RatingUpdated;
        Bindings.StopTracking();
        PreviewCanvas.ZoomPercentChanged -= PreviewCanvas_ZoomPercentChanged;
        if (_thumbnailWheelHandler != null)
        {
            PreviewThumbnailGridView.RemoveHandler(UIElement.PointerWheelChangedEvent, _thumbnailWheelHandler);
            _thumbnailWheelHandler = null;
        }
        if (_thumbnailPreviewKeyDownHandler != null)
        {
            PreviewThumbnailGridView.RemoveHandler(UIElement.PreviewKeyDownEvent, _thumbnailPreviewKeyDownHandler);
            _thumbnailPreviewKeyDownHandler = null;
        }
        Loaded -= CollectPage_Loaded;
        Unloaded -= CollectPage_Unloaded;
    }

    private async void LoadPreview_Click(object sender, RoutedEventArgs e)
    {
        CollapseLoadDrawer();
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

    private void FolderTreeMenuFlyout_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
            return;

        var hasFolderPath = !string.IsNullOrWhiteSpace(_rightClickedFolderNode?.FullPath);
        var isPinned = hasFolderPath && ViewModel.IsFolderPinned(_rightClickedFolderNode);
        var isExternalDeviceNode = IsNodeUnderExternalDevices(_rightClickedFolderNode);

        foreach (var item in flyout.Items)
        {
            if (item is not FrameworkElement element || element.Tag is not string tag)
                continue;

            element.Visibility = tag switch
            {
                "PinFolder" => hasFolderPath && !isPinned ? Visibility.Visible : Visibility.Collapsed,
                "UnpinFolder" => hasFolderPath && isPinned ? Visibility.Visible : Visibility.Collapsed,
                "PinSectionSeparator" => hasFolderPath ? Visibility.Visible : Visibility.Collapsed,
                "RefreshExternalDevices" => isExternalDeviceNode ? Visibility.Visible : Visibility.Collapsed,
                "ExternalDeviceSectionSeparator" => isExternalDeviceNode ? Visibility.Visible : Visibility.Collapsed,
                _ => element.Visibility
            };
        }
    }

    private static bool IsNodeUnderExternalDevices(FolderNode? node)
    {
        while (node != null)
        {
            if (node.NodeType == NodeType.ExternalDevice)
                return true;

            node = node.Parent;
        }

        return false;
    }

    private async void PinFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PinFolderAsync(_rightClickedFolderNode);
    }

    private async void UnpinFolder_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.UnpinFolderAsync(_rightClickedFolderNode);
    }

    private async void RefreshExternalDevices_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshExternalDevicesAsync();
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

    private void PreviewThumbnailGridView_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (PreviewThumbnailGridView.SelectedItem is ImageFileInfo imageInfo)
        {
            ViewModel.SelectedImage = imageInfo;
        }
    }

    private void PreviewThumbnailGridView_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (_suppressNextThumbnailDragStart)
        {
            _suppressNextThumbnailDragStart = false;
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
            : $"{storageFiles.Count} files";
        e.Data.SetStorageItems(storageFiles);
    }

    private IEnumerable<ImageFileInfo> GetDragImagesFromItems(IList<object> dragItems)
    {
        var dragImage = dragItems.OfType<ImageFileInfo>().FirstOrDefault();
        if (dragImage == null)
            return Array.Empty<ImageFileInfo>();

        if (PreviewThumbnailGridView.SelectedItems.Contains(dragImage))
        {
            return GetOrderedSelectedImagesForDrag();
        }

        PreviewThumbnailGridView.SelectedItems.Clear();
        PreviewThumbnailGridView.SelectedItem = dragImage;
        ViewModel.SelectedImage = dragImage;
        return new[] { dragImage };
    }

    private IEnumerable<ImageFileInfo> GetOrderedSelectedImagesForDrag()
    {
        var selected = PreviewThumbnailGridView.SelectedItems
            .OfType<ImageFileInfo>()
            .ToHashSet();

        if (selected.Count == 0)
            return Array.Empty<ImageFileInfo>();

        return ViewModel.Images.Where(selected.Contains).ToList();
    }

    private void PreviewThumbnailItem_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressNextThumbnailDragStart = IsWithinRatingControl(e.OriginalSource as DependencyObject);
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

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CollectViewModel.SelectedImage))
        {
            UpdateSelectedImageUi();
            if (ViewModel.SelectedImage != null)
            {
                StartSelectedThumbnailLoad(ViewModel.SelectedImage);
            }
        }
        else if (e.PropertyName == nameof(CollectViewModel.IsThumbnailStripCollapsed))
        {
            ThumbnailStripHost.Height = ViewModel.IsThumbnailStripCollapsed ? 90 : 135;
        }
        else if (e.PropertyName == nameof(CollectViewModel.PendingDeleteCount))
        {
            UpdateShellToolbarState();
        }
        else if (e.PropertyName == nameof(CollectViewModel.IsInfoDrawerOpen))
        {
            UpdateInfoDrawerState();
        }
    }

    private void LoadDrawerToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isLoadDrawerPinnedCollapsed = !_isLoadDrawerPinnedCollapsed;
        _isLoadDrawerTemporarilyExpanded = false;
        UpdateLoadDrawerState();
    }

    private void LoadDrawerRoot_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!_isLoadDrawerPinnedCollapsed || _isLoadDrawerTemporarilyExpanded)
            return;

        _isLoadDrawerTemporarilyExpanded = true;
        UpdateLoadDrawerState();
    }

    private void LoadDrawerRoot_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isLoadDrawerPinnedCollapsed || !_isLoadDrawerTemporarilyExpanded)
            return;

        _isLoadDrawerTemporarilyExpanded = false;
        UpdateLoadDrawerState();
    }

    private bool IsLoadDrawerExpanded => !_isLoadDrawerPinnedCollapsed || _isLoadDrawerTemporarilyExpanded;

    private void CollapseLoadDrawer()
    {
        _isLoadDrawerPinnedCollapsed = true;
        _isLoadDrawerTemporarilyExpanded = false;
        UpdateLoadDrawerState();
    }

    private void UpdateLoadDrawerState(bool animate = true)
    {
        var isExpanded = IsLoadDrawerExpanded;
        var targetWidth = isExpanded ? LoadDrawerExpandedWidth : LoadDrawerCollapsedWidth;
        var targetChevronAngle = isExpanded ? 180 : 0;

        if (isExpanded)
        {
            SetLoadDrawerContentVisibility(Visibility.Visible);
        }

        if (!animate)
        {
            StopLoadDrawerAnimation();
            LoadDrawerRoot.Width = targetWidth;
            LoadDrawerChevronTransform.Angle = targetChevronAngle;
            if (!isExpanded)
            {
                SetLoadDrawerContentVisibility(Visibility.Collapsed);
            }
            return;
        }

        AnimateLoadDrawer(targetWidth, targetChevronAngle, isExpanded);
    }

    private void AnimateLoadDrawer(double targetWidth, double targetChevronAngle, bool isExpanding)
    {
        StopLoadDrawerAnimation();

        var currentWidth = LoadDrawerRoot.ActualWidth > 0
            ? LoadDrawerRoot.ActualWidth
            : LoadDrawerRoot.Width;
        if (double.IsNaN(currentWidth) || currentWidth <= 0)
        {
            currentWidth = isExpanding ? LoadDrawerCollapsedWidth : LoadDrawerExpandedWidth;
        }

        if (Math.Abs(currentWidth - targetWidth) < 0.5)
        {
            LoadDrawerRoot.Width = targetWidth;
            LoadDrawerChevronTransform.Angle = targetChevronAngle;
            if (!IsLoadDrawerExpanded)
            {
                SetLoadDrawerContentVisibility(Visibility.Collapsed);
            }
            return;
        }

        var animation = new DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EnableDependentAnimation = true,
            EasingFunction = new QuadraticEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        Storyboard.SetTarget(animation, LoadDrawerRoot);
        Storyboard.SetTargetProperty(animation, "Width");

        var chevronAnimation = new DoubleAnimation
        {
            From = LoadDrawerChevronTransform.Angle,
            To = targetChevronAngle,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EnableDependentAnimation = true,
            EasingFunction = new QuadraticEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        Storyboard.SetTarget(chevronAnimation, LoadDrawerChevronTransform);
        Storyboard.SetTargetProperty(chevronAnimation, "Angle");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Children.Add(chevronAnimation);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_loadDrawerStoryboard, storyboard))
                return;

            LoadDrawerRoot.Width = targetWidth;
            LoadDrawerChevronTransform.Angle = targetChevronAngle;
            if (!IsLoadDrawerExpanded)
            {
                SetLoadDrawerContentVisibility(Visibility.Collapsed);
            }
            _loadDrawerStoryboard = null;
        };

        _loadDrawerStoryboard = storyboard;
        storyboard.Begin();
    }

    private void StopLoadDrawerAnimation()
    {
        var storyboard = _loadDrawerStoryboard;
        _loadDrawerStoryboard = null;
        storyboard?.Stop();
    }

    private void SetLoadDrawerContentVisibility(Visibility visibility)
    {
        LoadDrawerHeader.Visibility = visibility;
        LoadDrawerTree.Visibility = visibility;
    }

    private void UpdateInfoDrawerState(bool animate = true)
    {
        var isExpanded = ViewModel.IsInfoDrawerOpen;
        var targetWidth = isExpanded ? InfoDrawerExpandedWidth : InfoDrawerCollapsedWidth;

        if (isExpanded)
        {
            SetInfoDrawerContentVisibility(Visibility.Visible);
        }

        if (!animate)
        {
            StopInfoDrawerAnimation();
            PreviewInfoDrawer.Width = targetWidth;
            PreviewInfoDrawer.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        AnimateInfoDrawer(targetWidth, isExpanded);
    }

    private void AnimateInfoDrawer(double targetWidth, bool isExpanding)
    {
        StopInfoDrawerAnimation();

        var currentWidth = PreviewInfoDrawer.ActualWidth > 0
            ? PreviewInfoDrawer.ActualWidth
            : PreviewInfoDrawer.Width;
        if (double.IsNaN(currentWidth) || currentWidth < 0)
        {
            currentWidth = isExpanding ? InfoDrawerCollapsedWidth : InfoDrawerExpandedWidth;
        }

        if (Math.Abs(currentWidth - targetWidth) < 0.5)
        {
            PreviewInfoDrawer.Width = targetWidth;
            PreviewInfoDrawer.Visibility = isExpanding ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        PreviewInfoDrawer.Visibility = Visibility.Visible;

        var animation = new DoubleAnimation
        {
            From = currentWidth,
            To = targetWidth,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EnableDependentAnimation = true,
            EasingFunction = new QuadraticEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        Storyboard.SetTarget(animation, PreviewInfoDrawer);
        Storyboard.SetTargetProperty(animation, "Width");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_infoDrawerStoryboard, storyboard))
                return;

            PreviewInfoDrawer.Width = targetWidth;
            if (!ViewModel.IsInfoDrawerOpen)
            {
                SetInfoDrawerContentVisibility(Visibility.Collapsed);
                PreviewInfoDrawer.Visibility = Visibility.Collapsed;
            }

            _infoDrawerStoryboard = null;
        };

        _infoDrawerStoryboard = storyboard;
        storyboard.Begin();
    }

    private void StopInfoDrawerAnimation()
    {
        var storyboard = _infoDrawerStoryboard;
        _infoDrawerStoryboard = null;
        storyboard?.Stop();
    }

    private void SetInfoDrawerContentVisibility(Visibility visibility)
    {
        PreviewInfoDrawer.Visibility = visibility;
    }

    private async void RegisterShellToolbar()
    {
        await Task.Yield();
        if (_isDisposed || _isUnloaded)
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
            Flyout = CreateFilterFlyout()
        };
        _shellFilterSplitButton.Content = CreateToolbarActiveIndicatorContent(
            CreateToolbarIcon("\uE71C"),
            out _shellFilterActiveIndicator);
        ApplyToolbarButtonChrome(_shellFilterSplitButton);
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
        ApplyToolbarButtonChrome(button);
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static void ApplyToolbarButtonChrome(Control control)
    {
        var transparentBrush = GetThemeBrush("TransparentFillColor", Microsoft.UI.Colors.Transparent);
        var disabledForegroundBrush = GetThemeBrush("TextFillColorDisabledBrush", Windows.UI.Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF));

        control.MinWidth = 40;
        control.Height = 40;
        control.Background = transparentBrush;
        control.BorderBrush = transparentBrush;
        control.BorderThickness = new Thickness(0);
        control.Resources["ButtonBackgroundDisabled"] = transparentBrush;
        control.Resources["ButtonBorderBrushDisabled"] = transparentBrush;
        control.Resources["ButtonForegroundDisabled"] = disabledForegroundBrush;
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

    private static Brush GetToolbarActiveIndicatorBrush()
    {
        return GetThemeBrush("AccentFillColorDefaultBrush", Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));
    }

    private static Brush GetThemeBrush(string resourceKey, Windows.UI.Color fallbackColor)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallbackColor);
    }

    private static Grid CreateToolbarActiveIndicatorContent(FrameworkElement icon, out Microsoft.UI.Xaml.Shapes.Rectangle indicator)
    {
        var root = new Grid
        {
            Width = 24,
            Height = 24,
            IsHitTestVisible = false
        };

        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });

        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;

        Grid.SetRow(icon, 0);
        root.Children.Add(icon);

        indicator = new Microsoft.UI.Xaml.Shapes.Rectangle
        {
            Width = 25,
            Height = 3,
            Fill = GetToolbarActiveIndicatorBrush(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 1, 0, 0),
            RadiusX = 1.5,
            RadiusY = 1.5,
            Opacity = 0
        };

        Grid.SetRow(indicator, 1);
        root.Children.Add(indicator);

        return root;
    }

    private static void UpdateToolbarActiveIndicator(Microsoft.UI.Xaml.Shapes.Rectangle? indicator, bool isActive)
    {
        if (indicator != null)
        {
            indicator.Opacity = isActive ? 1 : 0;
        }
    }

    private Flyout CreateFilterFlyout()
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight
        };
        flyout.FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(Control.PaddingProperty, new Thickness(16))
            }
        };
        flyout.Opening += (_, _) =>
        {
            if (flyout.Content == null)
            {
                flyout.Content = new FilterFlyout
                {
                    FilterViewModel = ViewModel.Filter,
                    ShowBurstFilter = false
                };
            }
        };
        return flyout;
    }

    private void Filter_FilterChanged(object? sender, EventArgs e)
    {
        UpdateFilterButtonState();
    }

    private void UpdateFilterButtonState()
    {
        var isActive = ViewModel.Filter.IsFilterActive;
        UpdateToolbarActiveIndicator(_shellFilterActiveIndicator, isActive);

        if (_shellFilterSplitButton != null)
        {
            ApplyToolbarButtonChrome(_shellFilterSplitButton);
        }
    }

    private int _selectedImageVersion;

    private async void UpdateSelectedImageUi()
    {
        var imageInfo = ViewModel.SelectedImage;
        if (imageInfo == null)
        {
            PreviewInfoViewModel.Clear();
            return;
        }

        var version = ++_selectedImageVersion;
        PreviewInfoViewModel.SetBasicInfo(imageInfo);

        await PreviewInfoViewModel.LoadFileDetailsAsync(imageInfo.ImageFile, imageInfo.DateTaken);

        if (version != _selectedImageVersion)
        {
            return;
        }
    }

    private void StartSelectedThumbnailLoad(ImageFileInfo imageInfo)
    {
        if (_isDisposed || _isUnloaded)
            return;

        var version = ++_selectedThumbnailLoadVersion;
        _pendingVisibleThumbnailLoads.Remove(imageInfo);
        DebugThumbnailLoad($"Selected thumbnail start image={GetDebugName(imageInfo)} version={version}");
        _ = LoadSelectedThumbnailAsync(imageInfo, version);
    }

    private async Task LoadSelectedThumbnailAsync(ImageFileInfo imageInfo, int version)
    {
        try
        {
            await imageInfo.EnsureFastPreviewAsync(ViewModel.ThumbnailSize);
            if (!IsCurrentSelectedThumbnailLoad(imageInfo, version))
            {
                DebugThumbnailLoad($"Selected thumbnail skip stale after fast image={GetDebugName(imageInfo)} version={version}");
                return;
            }

            await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
            if (!IsCurrentSelectedThumbnailLoad(imageInfo, version))
            {
                DebugThumbnailLoad($"Selected thumbnail completed stale image={GetDebugName(imageInfo)} version={version}");
                return;
            }

            DebugThumbnailLoad($"Selected thumbnail complete image={GetDebugName(imageInfo)} version={version}");
        }
        catch (OperationCanceledException)
        {
            DebugThumbnailLoad($"Selected thumbnail canceled image={GetDebugName(imageInfo)} version={version}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectPage] selected thumbnail load failed: {ex.Message}");
        }
    }

    private bool IsCurrentSelectedThumbnailLoad(ImageFileInfo imageInfo, int version)
    {
        return !_isDisposed &&
            !_isUnloaded &&
            version == _selectedThumbnailLoadVersion &&
            ReferenceEquals(ViewModel.SelectedImage, imageInfo);
    }

    private void PreviewInfoViewModel_RatingUpdated(object? sender, (ImageFileInfo Image, uint Rating) e)
    {
        e.Image.Rating = e.Rating;
        QueueVisibleThumbnailLoad("preview-info-rating-updated");
    }

    private void PreviewThumbnailRatingControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RatingControl ratingControl && !_ratingControlEventMap.ContainsKey(ratingControl))
        {
            ratingControl.ValueChanged += PreviewThumbnailRatingControl_ValueChanged;
            _ratingControlEventMap[ratingControl] = true;
        }
    }

    private void PreviewThumbnailRatingControl_ValueChanged(RatingControl sender, object args)
    {
        try
        {
            if (_isDisposed || _isUnloaded)
                return;

            var imageInfo = FindImageInfoFromRatingControl(sender);
            if (imageInfo == null)
                return;

            var controlValue = sender.Value;
            uint ratingValue = 0;
            if (controlValue > 0)
            {
                var stars = (int)Math.Round(controlValue, MidpointRounding.AwayFromZero);
                ratingValue = ImageFileInfo.StarsToRating(Math.Clamp(stars, 1, 5));
            }

            _pendingRatingUpdate = (imageInfo, ratingValue);
            _ratingDebounceTimer.Stop();
            _ratingDebounceTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectPage] rating change failed: {ex.Message}");
        }
    }

    private async void RatingDebounceTimer_Tick(object? sender, object e)
    {
        _ratingDebounceTimer.Stop();
        if (!_pendingRatingUpdate.HasValue)
            return;

        var (image, rating) = _pendingRatingUpdate.Value;
        _pendingRatingUpdate = null;

        var imagesToProcess = GetImagesForRatingUpdate(image);
        foreach (var imageInfo in imagesToProcess)
        {
            await UpdateRatingAsync(imageInfo, rating);
        }
    }

    private static ImageFileInfo? FindImageInfoFromRatingControl(RatingControl ratingControl)
    {
        try
        {
            var parent = VisualTreeHelper.GetParent(ratingControl);
            while (parent != null)
            {
                if (parent is FrameworkElement { DataContext: ImageFileInfo imageInfo })
                {
                    return imageInfo;
                }

                parent = VisualTreeHelper.GetParent(parent);
            }
        }
        catch
        {
        }

        return null;
    }

    private static List<ImageFileInfo> GetImagesForRatingUpdate(ImageFileInfo image)
    {
        var imagesToProcess = new List<ImageFileInfo>();
        if (image.Group != null)
        {
            foreach (var groupImage in image.Group.Images)
            {
                if (!imagesToProcess.Contains(groupImage))
                {
                    imagesToProcess.Add(groupImage);
                }
            }
        }
        else
        {
            imagesToProcess.Add(image);
        }

        return imagesToProcess;
    }

    private static async Task UpdateRatingAsync(ImageFileInfo imageInfo, uint rating)
    {
        try
        {
            var ratingService = App.GetService<RatingService>();
            await imageInfo.SetRatingAsync(ratingService, rating);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectPage] update rating failed: {ex.Message}");
        }
    }

    private void PreviewCanvas_ZoomPercentChanged(object? sender, double percent)
    {
        _isUpdatingZoomSlider = true;
        ZoomSlider.Value = Math.Clamp(percent, ZoomSlider.Minimum, ZoomSlider.Maximum);
        UpdateZoomValueButtonContent(percent);
        _isUpdatingZoomSlider = false;
    }

    private void ZoomSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingZoomSlider)
            return;

        PreviewCanvas.SetZoomPercent(e.NewValue);
        UpdateZoomValueButtonContent(e.NewValue);
    }

    private void ZoomValueButton_Click(object sender, RoutedEventArgs e)
    {
        PreviewCanvas.ToggleOriginalOrFitZoom();
    }
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        StepZoomPercent(-10d);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        StepZoomPercent(10d);
    }

    private void StepZoomPercent(double deltaPercent)
    {
        var targetPercent = Math.Clamp(ZoomSlider.Value + deltaPercent, ZoomSlider.Minimum, ZoomSlider.Maximum);
        PreviewCanvas.SetZoomPercent(targetPercent);
        ZoomSlider.Value = targetPercent;
        UpdateZoomValueButtonContent(targetPercent);
    }

    private void UpdateZoomValueButtonContent(double percent)
    {
        var iconLayer = new Grid
        {
            Width = 20,
            Height = 20
        };

        iconLayer.Children.Add(new FontIcon
        {
            Glyph = "\uE9A6",
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        if (PreviewCanvas.IsFitZoomActive)
        {
            iconLayer.Children.Add(new FontIcon
            {
                Glyph = "\uE915",
                FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            });
        }

        ZoomValueButton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                iconLayer,
                new TextBlock
                {
                    Text = $"{percent:F0}%",
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
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

        dialog.ConfirmDeleteAsync = async currentDialog =>
        {
            var filesToDelete = GetFilesToDelete(pendingImages, currentDialog.SelectedExtensions);
            if (filesToDelete.Count == 0)
            {
                currentDialog.SetComplete();
                return;
            }

            var imagePathMap = DeleteWorkflowHelper.BuildPrimaryImagePathMap(pendingImages);
            var deleteResult = await DeleteWorkflowHelper.DeleteFilesAsync(
                filesToDelete,
                imagePathMap,
                currentDialog,
                App.GetService<IThumbnailService>(),
                message => System.Diagnostics.Debug.WriteLine($"[CollectPage] delete failed {message}"));

            ViewModel.RemoveDeletedImages(deleteResult.DeletedImages);
            QueueVisibleThumbnailLoad("delete");
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return;

        var filesToDelete = GetFilesToDelete(pendingImages, dialog.SelectedExtensions);
        if (filesToDelete.Count == 0)
            return;

        var imagePathMap = DeleteWorkflowHelper.BuildPrimaryImagePathMap(pendingImages);
        var deleteResult = await DeleteWorkflowHelper.DeleteFilesAsync(
            filesToDelete,
            imagePathMap,
            dialog,
            App.GetService<IThumbnailService>(),
            message => System.Diagnostics.Debug.WriteLine($"[CollectPage] delete failed {message}"));

        ViewModel.RemoveDeletedImages(deleteResult.DeletedImages);
        QueueVisibleThumbnailLoad("delete");
        return;
    }

    private static List<StorageFile> GetFilesToDelete(List<ImageFileInfo> pendingImages, List<string> selectedExtensions)
    {
        return DeleteWorkflowHelper.GetFilesToDelete(pendingImages, selectedExtensions);
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

    private bool HandleShortcut(KeyRoutedEventArgs e)
    {
        if (_isDisposed || _isUnloaded)
            return false;

        if (e.Key == VirtualKey.Space && ViewModel.SelectedImage != null)
        {
            PreviewCanvas.ToggleOriginalOrFitZoom();
            return true;
        }
        else if (e.Key == VirtualKey.Escape && ViewModel.SelectedImage != null)
        {
            PreviewCanvas.ResetToFitZoom();
            return true;
        }
        else if (e.Key == VirtualKey.Delete)
        {
            ViewModel.TogglePendingDeleteForSelected(PreviewThumbnailGridView.SelectedItems.OfType<ImageFileInfo>());
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
            if (ShouldProcessDirectionalNavigation(e.KeyStatus.WasKeyDown, direction))
            {
                MoveSelection(direction);
            }

            return true;
        }

        return false;
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        var current = element;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void HandleRatingShortcut(VirtualKey key)
    {
        var selectedImages = PreviewThumbnailGridView.SelectedItems
            .OfType<ImageFileInfo>()
            .ToList();

        if (selectedImages.Count == 0)
        {
            if (ViewModel.SelectedImage == null)
                return;

            selectedImages.Add(ViewModel.SelectedImage);
        }

        var stars = key - VirtualKey.Number0;
        var imagesToProcess = new List<ImageFileInfo>();
        foreach (var selectedImage in selectedImages)
        {
            foreach (var imageInfo in GetImagesForRatingUpdate(selectedImage))
            {
                if (!imagesToProcess.Contains(imageInfo))
                {
                    imagesToProcess.Add(imageInfo);
                }
            }
        }

        foreach (var imageInfo in imagesToProcess)
        {
            uint newRating;
            if (stars == 0)
            {
                newRating = 0;
            }
            else
            {
                var currentStars = ImageFileInfo.RatingToStars(imageInfo.Rating);
                newRating = currentStars == stars
                    ? 0
                    : ImageFileInfo.StarsToRating(stars);
            }

            _ = UpdateRatingAsync(imageInfo, newRating);
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
        PreviewThumbnailGridView.ScrollIntoView(nextImage, ScrollIntoViewAlignment.Default);
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

    private bool ShouldProcessDirectionalNavigation(bool wasKeyDown, int direction)
    {
        var now = Environment.TickCount64;
        if (!wasKeyDown ||
            _lastDirectionalNavigationDirection != direction ||
            now - _lastDirectionalNavigationTick >= DirectionalNavigationRepeatIntervalMs)
        {
            _lastDirectionalNavigationDirection = direction;
            _lastDirectionalNavigationTick = now;
            return true;
        }

        return false;
    }

    private static string GetDebugName(ImageFileInfo? imageInfo)
    {
        if (imageInfo == null)
            return "<null>";

        return string.IsNullOrWhiteSpace(imageInfo.ImageFile?.Name)
            ? imageInfo.ImageName
            : imageInfo.ImageFile.Name;
    }

    [Conditional("DEBUG")]
    private static void DebugThumbnailLoad(string message)
    {
        //Debug.WriteLine($"[CollectThumbnailLoad] {DateTime.Now:HH:mm:ss.fff} {message}");
    }
}
