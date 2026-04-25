using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using PhotoView.Contracts.Services;
using PhotoView.Controls;
using PhotoView.Helpers;
using PhotoView.Services;
using PhotoView.ViewModels;
using PhotoView.Views;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.UI;

namespace PhotoView.Views;

public sealed partial class ShellPage : Page
{
    private const double LeftNavigationOpenPaneLength = 300d;
    private const double DefaultNavigationOpenPaneLength = 300d;
    private const double CompactNavigationPaneFlyoutWidth = 360d;
    private readonly NavigationPaneExplorer _compactNavigationPaneHost = new();
    private readonly Flyout _compactNavigationPaneFlyout;

    public ShellViewModel ViewModel
    {
        get;
    }

    private readonly IThemeSelectorService _themeSelectorService;
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private readonly ShellToolbarService _shellToolbarService;
    private readonly INavigationPaneService _navigationPaneService;
    private string? _lastPageKey;
    private bool _isOnSettingsPage;
    private int _shellToolbarUpdateVersion;
    private long _isPaneOpenChangedCallbackToken;

    public ShellPage(ShellViewModel viewModel, ISettingsService settingsService)
    {
        ViewModel = viewModel;
        _settingsService = settingsService;
        _shellToolbarService = App.GetService<ShellToolbarService>();
        _navigationPaneService = App.GetService<INavigationPaneService>();
        InitializeComponent();
        _compactNavigationPaneFlyout = CreateCompactNavigationPaneFlyout();
        _isPaneOpenChangedCallbackToken = NavigationViewControl.RegisterPropertyChangedCallback(
            NavigationView.IsPaneOpenProperty,
            OnNavigationViewPaneOpenChanged);

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        // 璁㈤槄璁剧疆鍙樺寲浜嬩欢
        _settingsService.NavigationViewModeChanged += OnNavigationViewModeChanged;
        
        // 寮傛鍔犺浇瀵艰埅妯″紡璁剧疆
        _ = InitializeNavigationViewModeAsync();

        _themeSelectorService = App.GetService<IThemeSelectorService>();
        _themeSelectorService.ThemeChanged += OnThemeChanged;
        UpdateNavigationIcons();

        _navigationService = App.GetService<INavigationService>();
        _navigationService.Navigated += OnNavigationServiceNavigated;
        _shellToolbarService.ToolbarChanged += OnShellToolbarChanged;
        _navigationPaneService.CurrentContextChanged += OnNavigationPaneContextChanged;

        // 浣跨敤瀹樻柟 TitleBar
        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(titleBar);
        App.MainWindow.Activated += MainWindow_Activated;
    }

    private async Task InitializeNavigationViewModeAsync()
    {
        var mode = await _settingsService.LoadNavigationViewModeAsync();
        if (mode == NavigationViewPaneDisplayMode.LeftCompact)
        {
            mode = NavigationViewPaneDisplayMode.Left;
            _settingsService.NavigationViewMode = mode;
            await _settingsService.SaveNavigationViewModeAsync(mode);
        }

        _ = await _settingsService.LoadBatchSizeAsync();
        _ = await _settingsService.LoadPerformanceModeAsync();
        _ = await _settingsService.LoadThumbnailSizeAsync();
        _ = await _settingsService.LoadRememberLastFolderAsync();
        _ = await _settingsService.LoadLastFolderPathAsync();
        
        
        // 鍦?UI 绾跨▼涓婃洿鏂?
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyNavigationViewMode(mode);
        });
    }

    private void OnNavigationViewModeChanged(object? sender, NavigationViewPaneDisplayMode mode)
    {
        // 鍦?UI 绾跨▼涓婃洿鏂?
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyNavigationViewMode(mode);
        });
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // 鍔犺浇瀹屾垚鍚庢洿鏂版爣棰樻爮棰滆壊
        UpdateTitleBarColor();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        UpdateTitleBarColor(args.WindowActivationState == WindowActivationState.Deactivated);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // 寤惰繜涓€涓嬶紝纭繚涓婚瀹屽叏搴旂敤鍚庡啀璁剧疆棰滆壊
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(50);
            UpdateNavigationIcons();
            UpdateTitleBarColor();
        });
    }

    private void OnShellToolbarChanged(object? sender, EventArgs e)
    {
        var version = ++_shellToolbarUpdateVersion;
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Yield();
            if (version != _shellToolbarUpdateVersion)
                return;

            ShellToolbarHost.Content = _shellToolbarService.CurrentToolbar;
        });
    }

    private void OnNavigationServiceNavigated(object sender, NavigationEventArgs e)
    {
        _isOnSettingsPage = e.SourcePageType == typeof(SettingsPage);

        if (!_isOnSettingsPage)
        {
            var selectedItemKey = ViewModel.NavigationViewService.GetSelectedItemKey();
            if (!string.IsNullOrEmpty(selectedItemKey))
            {
                _lastPageKey = selectedItemKey;
            }
        }

        UpdateNavigationPaneState();
    }

    private void UpdateTitleBarColor(bool isDeactivated = false)
    {
        // 鏇存柊鏍囬鏍忔寜閽鑹?
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow?.TitleBar != null)
        {
            var titleBar = appWindow.TitleBar;
            
            // 浣跨敤涓婚鏈嶅姟鑾峰彇褰撳墠涓婚锛屾渶鍙潬
            var isDarkTheme = _themeSelectorService.Theme == ElementTheme.Dark || 
                              (_themeSelectorService.Theme == ElementTheme.Default && 
                               Application.Current.RequestedTheme == ApplicationTheme.Dark);

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (isDarkTheme)
            {
                // 娣辫壊涓婚锛氱櫧鑹叉寜閽?
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
                if (isDeactivated)
                {
                    titleBar.ButtonForegroundColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
                    titleBar.ButtonHoverForegroundColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
                    titleBar.ButtonPressedForegroundColor = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF);
                }
            }
            else
            {
                // 娴呰壊涓婚锛氶粦鑹叉寜閽?- 鐩存帴纭紪鐮佷负榛戣壊锛?
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedForegroundColor = Colors.Black;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x20, 0x00, 0x00, 0x00);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x40, 0x00, 0x00, 0x00);
                if (isDeactivated)
                {
                    titleBar.ButtonForegroundColor = Color.FromArgb(0x66, 0x00, 0x00, 0x00);
                    titleBar.ButtonHoverForegroundColor = Color.FromArgb(0x66, 0x00, 0x00, 0x00);
                    titleBar.ButtonPressedForegroundColor = Color.FromArgb(0x66, 0x00, 0x00, 0x00);
                }
            }
        }
    }

    private void ApplyNavigationViewMode(NavigationViewPaneDisplayMode mode)
    {
        var normalizedMode = mode == NavigationViewPaneDisplayMode.LeftCompact
            ? NavigationViewPaneDisplayMode.Left
            : mode;
        var useLeftNavigation = normalizedMode == NavigationViewPaneDisplayMode.Left;

        NavigationViewControl.PaneDisplayMode = normalizedMode;
        NavigationViewControl.IsPaneToggleButtonVisible = useLeftNavigation;
        NavigationViewControl.IsPaneOpen = useLeftNavigation;
        NavigationViewControl.OpenPaneLength = useLeftNavigation
            ? LeftNavigationOpenPaneLength
            : DefaultNavigationOpenPaneLength;
        _compactNavigationPaneFlyout.Hide();

        UpdateNavigationPaneState();
        UpdateTitleBarLayout(useLeftNavigation);
    }

    private void OnNavigationViewPaneOpenChanged(DependencyObject sender, DependencyProperty dependency)
    {
        DispatcherQueue.TryEnqueue(UpdateNavigationPaneState);
    }

    private void UpdateTitleBarLayout(bool useLeftNavigation)
    {
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow?.TitleBar == null)
        {
            return;
        }

        appWindow.TitleBar.PreferredHeightOption = useLeftNavigation
            ? TitleBarHeightOption.Standard
            : TitleBarHeightOption.Tall;
    }

    private void UpdateNavigationIcons()
    {
        var isDarkTheme = _themeSelectorService.Theme == ElementTheme.Dark ||
                          (_themeSelectorService.Theme == ElementTheme.Default &&
                           Application.Current.RequestedTheme == ApplicationTheme.Dark);

        MainNavigationItem.Icon = CreateNavigationIcon(isDarkTheme
            ? "ms-appx:///Assets/mainpge-black.png"
            : "ms-appx:///Assets/mainpge-white.png");
        PreviewNavigationItem.Icon = CreateNavigationIcon(isDarkTheme
            ? "ms-appx:///Assets/preview-black.png"
            : "ms-appx:///Assets/preview-white.png");
    }

    private static BitmapIcon CreateNavigationIcon(string uri)
    {
        return new BitmapIcon
        {
            Width = 24,
            Height = 24,
            ShowAsMonochrome = false,
            UriSource = new Uri(uri)
        };
    }
    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        // 澶勭悊杩斿洖閫昏緫
        if (NavigationFrame.CanGoBack)
        {
            NavigationFrame.GoBack();
        }
    }

    private void NavigationViewControl_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
                if (args.InvokedItemContainer == ExpandedNavigationPaneItem)
        {
            return;
        }

        if (args.InvokedItemContainer == CompactNavigationPaneItem)
        {
            ShowCompactNavigationPaneFlyout();
            return;
        }

        if (args.IsSettingsInvoked)
        {
            if (_isOnSettingsPage)
            {
                if (!string.IsNullOrEmpty(_lastPageKey))
                {
                    _navigationService.NavigateTo(_lastPageKey);
                }
            }
            else
            {
                _navigationService.NavigateTo(typeof(SettingsViewModel).FullName!);
            }
        }
        else
        {
            if (args.InvokedItemContainer != null)
            {
                var navItemTag = ViewModel.NavigationViewService.GetNameForItem(args.InvokedItemContainer);
                if (!string.IsNullOrEmpty(navItemTag))
                {
                    _lastPageKey = navItemTag;
                    _navigationService.NavigateTo(navItemTag);
                }
            }
        }
    }

    private void OnNavigationPaneContextChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateNavigationPaneState);
    }

    private void UpdateNavigationPaneState()
    {
        var mode = NavigationViewControl.PaneDisplayMode;
        var useLeftNavigation = mode == NavigationViewPaneDisplayMode.Left;
        var isPaneOpen = useLeftNavigation && NavigationViewControl.IsPaneOpen;
        var context = !_isOnSettingsPage && useLeftNavigation
            ? _navigationPaneService.CurrentContext
            : null;

        NavigationPaneHost.Context = isPaneOpen ? context : null;
         ExpandedNavigationPaneItem.Visibility = isPaneOpen && context != null ? Visibility.Visible : Visibility.Collapsed;

        _compactNavigationPaneHost.Context = !isPaneOpen ? context : null;
          CompactNavigationPaneItem.Visibility = !isPaneOpen && context != null ? Visibility.Visible : Visibility.Collapsed;
        CompactNavigationPaneItem.Content = context?.Title ?? "目录";

        if (isPaneOpen || context == null)
        {
            _compactNavigationPaneFlyout.Hide();
        }
    }

    private void ShowCompactNavigationPaneFlyout()
    {
        if (CompactNavigationPaneItem.Visibility == Visibility.Visible)
        {
             _compactNavigationPaneFlyout.ShowAt(CompactNavigationPaneItem);
        }
    }

    private void CompactNavigationPaneButton_Click(object sender, RoutedEventArgs e)
    {
        ShowCompactNavigationPaneFlyout();
    }

    private Flyout CreateCompactNavigationPaneFlyout()
    {
        return new Flyout
        {
            Placement = FlyoutPlacementMode.Right,
            Content = new Border
            {
                Width = CompactNavigationPaneFlyoutWidth,
                MaxHeight = 720,
                Padding = new Thickness(0),
                Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = _compactNavigationPaneHost
            }
        };
    }
}
