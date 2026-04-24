using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PhotoView.Contracts.Services;
using PhotoView.Helpers;
using PhotoView.Models;

namespace PhotoView.Views;

public sealed partial class MainPage
{
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

        var exportButton = CreateToolbarButton("\uE72D", "Common_Export".GetLocalized());
        exportButton.Click += ExportButton_Click;
        toolbar.Children.Add(exportButton);

        _shellDeleteButton = CreateToolbarButton("\uE74D", "Common_Delete".GetLocalized());
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
        ToolTipService.SetToolTip(_shellFilterSplitButton, "Common_Filter".GetLocalized());
        toolbar.Children.Add(_shellFilterSplitButton);

        _shellAutoExpandBurstToggleButton = new ToggleButton
        {
            Padding = new Thickness(8),
            IsChecked = _settingsService.AutoExpandBurstOnDirectionalNavigation,
        };
        ApplyToolbarButtonChrome(_shellAutoExpandBurstToggleButton);
        ApplyToolbarToggleButtonCheckedChrome(_shellAutoExpandBurstToggleButton);
        _shellAutoExpandBurstToggleButton.Content = CreateToolbarActiveIndicatorContent(
            CreateToolbarFoldIcon(GetToolbarTemplateIconBrush()),
            out _shellAutoExpandBurstActiveIndicator);
        UpdateToolbarActiveIndicator(_shellAutoExpandBurstActiveIndicator, _shellAutoExpandBurstToggleButton.IsChecked == true);
        ToolTipService.SetToolTip(_shellAutoExpandBurstToggleButton, "MainPage_Tooltip_AutoExpandBurst".GetLocalized());
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
        ToolTipService.SetToolTip(sizeButton, "Common_ThumbnailSize".GetLocalized());
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

    private static void ApplyToolbarToggleButtonCheckedChrome(ToggleButton toggleButton)
    {
        var transparentBrush = GetThemeBrush("TransparentFillColor", Microsoft.UI.Colors.Transparent);
        var pointerOverBrush = GetThemeBrush("SubtleFillColorSecondaryBrush", Windows.UI.Color.FromArgb(0x14, 0x80, 0x80, 0x80));
        var pressedBrush = GetThemeBrush("SubtleFillColorTertiaryBrush", Windows.UI.Color.FromArgb(0x1F, 0x80, 0x80, 0x80));

        toggleButton.Resources["ToggleButtonBackgroundChecked"] = GetToolbarActiveBackgroundBrush();
        toggleButton.Resources["ToggleButtonBackgroundCheckedPointerOver"] = pointerOverBrush;
        toggleButton.Resources["ToggleButtonBackgroundCheckedPressed"] = pressedBrush;
        toggleButton.Resources["ToggleButtonBorderBrushChecked"] = transparentBrush;
        toggleButton.Resources["ToggleButtonBorderBrushCheckedPointerOver"] = transparentBrush;
        toggleButton.Resources["ToggleButtonBorderBrushCheckedPressed"] = transparentBrush;
    }

    private static Brush GetToolbarActiveBackgroundBrush()
    {
        return GetThemeBrush("TransparentFillColor", Microsoft.UI.Colors.Transparent);
    }

    private static Brush GetToolbarActiveIndicatorBrush()
    {
        return GetThemeBrush("AccentFillColorDefaultBrush", Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));
    }

    private static Brush GetToolbarTemplateIconBrush()
    {
        var themeSelectorService = App.GetService<IThemeSelectorService>();
        var isDarkTheme = themeSelectorService.Theme == ElementTheme.Dark ||
                          (themeSelectorService.Theme == ElementTheme.Default &&
                           Application.Current.RequestedTheme == ApplicationTheme.Dark);

        return new SolidColorBrush(isDarkTheme
            ? Microsoft.UI.Colors.White
            : Microsoft.UI.Colors.Black);
    }

    private static Brush GetThemeBrush(string resourceKey, Windows.UI.Color fallbackColor)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(fallbackColor);
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

    private FrameworkElement CreateToolbarFoldIcon(Brush foregroundBrush)
    {
        return Application.Current.Resources["BurstIconTemplate"] is DataTemplate template
            ? new ContentControl
            {
                Content = foregroundBrush,
                ContentTemplate = template
            }
            : CreateToolbarIcon("\uE8B9");
    }

    private async void AutoExpandBurstToggleButton_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton toggleButton)
            return;

        var enabled = toggleButton.IsChecked == true;
        if (ReferenceEquals(toggleButton, _shellAutoExpandBurstToggleButton))
        {
            UpdateToolbarActiveIndicator(_shellAutoExpandBurstActiveIndicator, enabled);
        }

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

    private void NavigationDrawerToggleButton_Click(object sender, RoutedEventArgs e)
    {
    }

    private void NavigationDrawerRoot_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
    }

    private void NavigationDrawerRoot_PointerExited(object sender, PointerRoutedEventArgs e)
    {
    }

    private bool IsNavigationDrawerExpanded => true;

    private void UpdateNavigationDrawerState(bool animate = true)
    {
    }

    private void AnimateNavigationDrawer(double targetWidth, double targetChevronAngle, bool isExpanding)
    {
    }

    private void StopNavigationDrawerAnimation()
    {
    }

    private void SetNavigationDrawerContentVisibility(Visibility visibility)
    {
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
    }

    private void AutoExpandNavigationDrawer(string reason)
    {
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
}

