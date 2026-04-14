using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
    private const double LoadDrawerExpandedWidth = 292;
    private const double LoadDrawerCollapsedWidth = 30;
    private const int VisibleThumbnailStartBudgetPerTick = 16;
    private const int VisibleThumbnailPrefetchItemCount = 12;
    private readonly DispatcherTimer _visibleThumbnailLoadTimer;
    private readonly DispatcherTimer _ratingDebounceTimer;
    private readonly HashSet<ImageFileInfo> _pendingVisibleThumbnailLoads = new();
    private readonly HashSet<ImageFileInfo> _realizedImageItems = new();
    private readonly Dictionary<RatingControl, bool> _ratingControlEventMap = new();
    private readonly ISettingsService _settingsService;
    private readonly ShellToolbarService _shellToolbarService;
    private FolderNode? _rightClickedFolderNode;
    private ItemsStackPanel? _thumbnailItemsPanel;
    private ScrollViewer? _thumbnailScrollViewer;
    private PointerEventHandler? _thumbnailWheelHandler;
    private Storyboard? _loadDrawerStoryboard;
    private bool _isUnloaded;
    private bool _isDisposed;
    private bool _isUpdatingZoomSlider;
    private bool _isLoadDrawerPinnedCollapsed;
    private bool _isLoadDrawerTemporarilyExpanded;
    private Button? _shellDeleteButton;
    private SplitButton? _shellFilterSplitButton;
    private (ImageFileInfo Image, uint Rating)? _pendingRatingUpdate;

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
        NavigationCacheMode = NavigationCacheMode.Disabled;
        InitializeComponent();
        DataContext = ViewModel;
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
        UpdateLoadDrawerState(animate: false);
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
        Loaded -= CollectPage_Loaded;
        Unloaded -= CollectPage_Unloaded;
        KeyDown -= CollectPage_KeyDown;
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
            ThumbnailStripHost.Height = ViewModel.IsThumbnailStripCollapsed ? 90 : 135;
        }
        else if (e.PropertyName == nameof(CollectViewModel.PendingDeleteCount))
        {
            UpdateShellToolbarState();
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
            Content = CreateToolbarIcon("\uE71C"),
            Flyout = CreateFilterFlyout()
        };
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
        control.MinWidth = 40;
        control.Height = 40;
        control.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        control.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        control.BorderThickness = new Thickness(0);
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

    private void Filter_FilterChanged(object? sender, EventArgs e)
    {
        UpdateFilterButtonState();
    }

    private void UpdateFilterButtonState()
    {
        if (ViewModel.Filter.IsFilterActive)
        {
            var activeColor = Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
            if (_shellFilterSplitButton != null)
            {
                _shellFilterSplitButton.Background = new SolidColorBrush(activeColor);
            }
        }
        else
        {
            if (_shellFilterSplitButton != null)
            {
                ApplyToolbarButtonChrome(_shellFilterSplitButton);
            }
        }
    }

    private async void UpdateSelectedImageUi()
    {
        var imageInfo = ViewModel.SelectedImage;
        if (imageInfo == null)
        {
            PreviewInfoViewModel.Clear();
            return;
        }

        PreviewInfoViewModel.SetBasicInfo(imageInfo);
        await PreviewInfoViewModel.LoadFileDetailsAsync(imageInfo.ImageFile);
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
        PreviewCanvas.ToggleOriginalOrFitZoom();
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
            HandleRatingShortcut(e.Key);
            e.Handled = true;
        }
        else if (e.Key >= VirtualKey.NumberPad0 && e.Key <= VirtualKey.NumberPad5)
        {
            HandleRatingShortcut(e.Key - (VirtualKey.NumberPad0 - VirtualKey.Number0));
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Left || e.Key == VirtualKey.Right)
        {
            MoveSelection(e.Key == VirtualKey.Right ? 1 : -1);
            e.Handled = true;
        }
    }

    private void HandleRatingShortcut(VirtualKey key)
    {
        var selectedImages = PreviewThumbnailGridView.SelectedItems
            .OfType<ImageFileInfo>()
            .ToList();

        if (selectedImages.Count == 0)
            return;

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
        PreviewThumbnailGridView.ScrollIntoView(nextImage);
    }
}
