using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using PhotoView.Contracts.Services;
using PhotoView.Helpers;
using PhotoView.ViewModels;
using PhotoView.Views;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.UI;

namespace PhotoView.Views;

public sealed partial class ShellPage : Page
{
    public ShellViewModel ViewModel
    {
        get;
    }

    private readonly IThemeSelectorService _themeSelectorService;
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private string? _lastPageKey;
    private bool _isOnSettingsPage;

    public ShellPage(ShellViewModel viewModel, ISettingsService settingsService)
    {
        ViewModel = viewModel;
        _settingsService = settingsService;
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        // 订阅设置变化事件
        _settingsService.NavigationViewModeChanged += OnNavigationViewModeChanged;
        
        // 异步加载导航模式设置
        _ = InitializeNavigationViewModeAsync();

        _themeSelectorService = App.GetService<IThemeSelectorService>();
        _themeSelectorService.ThemeChanged += OnThemeChanged;

        _navigationService = App.GetService<INavigationService>();
        _navigationService.Navigated += OnNavigationServiceNavigated;

        // 使用官方 TitleBar
        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(titleBar);
        App.MainWindow.Activated += MainWindow_Activated;
    }

    private async Task InitializeNavigationViewModeAsync()
    {
        var mode = await _settingsService.LoadNavigationViewModeAsync();
        _ = await _settingsService.LoadBatchSizeAsync();
        _ = await _settingsService.LoadPerformanceModeAsync();
        _ = await _settingsService.LoadThumbnailSizeAsync();
        _ = await _settingsService.LoadRememberLastFolderAsync();
        _ = await _settingsService.LoadLastFolderPathAsync();
        
        
        // �?UI 线程上更�?
        DispatcherQueue.TryEnqueue(() =>
        {
            NavigationViewControl.PaneDisplayMode = mode;
            NavigationViewControl.IsPaneToggleButtonVisible = mode == NavigationViewPaneDisplayMode.Left;
        });
    }

    private void OnNavigationViewModeChanged(object sender, NavigationViewPaneDisplayMode mode)
    {
        // �?UI 线程上更�?
        DispatcherQueue.TryEnqueue(() =>
        {
            var useLeftNavigation = mode == NavigationViewPaneDisplayMode.Left;
            
            NavigationViewControl.PaneDisplayMode = mode;
            NavigationViewControl.IsPaneToggleButtonVisible = useLeftNavigation;
        });
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        // 加载完成后更新标题栏颜色
        UpdateTitleBarColor();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        UpdateTitleBarColor(args.WindowActivationState == WindowActivationState.Deactivated);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // 延迟一下，确保主题完全应用后再设置颜色
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(50);
            UpdateTitleBarColor();
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
    }

    private void UpdateTitleBarColor(bool isDeactivated = false)
    {
        // 更新标题栏按钮颜�?
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow?.TitleBar != null)
        {
            var titleBar = appWindow.TitleBar;
            
            // 使用主题服务获取当前主题，最可靠
            var isDarkTheme = _themeSelectorService.Theme == ElementTheme.Dark || 
                              (_themeSelectorService.Theme == ElementTheme.Default && 
                               Application.Current.RequestedTheme == ApplicationTheme.Dark);

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (isDarkTheme)
            {
                // 深色主题：白色按�?
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
                // 浅色主题：黑色按�?- 直接硬编码为黑色�?
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

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        // 处理返回逻辑
        if (NavigationFrame.CanGoBack)
        {
            NavigationFrame.GoBack();
        }
    }

    private void NavigationViewControl_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
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
}
