using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private string? _lastPageKey;
    private bool _isOnSettingsPage;

    public ShellPage(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        _themeSelectorService = App.GetService<IThemeSelectorService>();
        _themeSelectorService.ThemeChanged += OnThemeChanged;

        _navigationService = App.GetService<INavigationService>();
        _navigationService.Navigated += OnNavigationServiceNavigated;

        App.MainWindow.ExtendsContentIntoTitleBar = true;
        App.MainWindow.SetTitleBar(AppTitleBar);
        App.MainWindow.Activated += MainWindow_Activated;
        AppTitleBarText.Text = "AppDisplayName".GetLocalized();

        // 立即设置标题栏颜色
        UpdateTitleBarColor();
    }

    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        UpdateTitleBarColor(args.WindowActivationState == WindowActivationState.Deactivated);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateTitleBarColor();
    }

    private void OnNavigationServiceNavigated(object sender, NavigationEventArgs e)
    {
        _isOnSettingsPage = e.SourcePageType == typeof(SettingsPage);
    }

    private void UpdateTitleBarColor(bool isDeactivated = false)
    {
        // 更新标题栏文字颜色
        var resource = isDeactivated ? "WindowCaptionForegroundDisabled" : "WindowCaptionForeground";
        AppTitleBarText.Foreground = (SolidColorBrush)Application.Current.Resources[resource];

        // 更新标题栏按钮颜色
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        if (appWindow?.TitleBar != null)
        {
            var titleBar = appWindow.TitleBar;

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (isDeactivated)
            {
                titleBar.ButtonForegroundColor = GetThemeColor("WindowCaptionForegroundDisabled");
                titleBar.ButtonHoverForegroundColor = GetThemeColor("WindowCaptionForegroundDisabled");
                titleBar.ButtonPressedForegroundColor = GetThemeColor("WindowCaptionForegroundDisabled");
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
            }
            else
            {
                titleBar.ButtonForegroundColor = GetThemeColor("WindowCaptionForeground");
                titleBar.ButtonHoverForegroundColor = GetThemeColor("WindowCaptionForeground");
                titleBar.ButtonPressedForegroundColor = GetThemeColor("WindowCaptionForeground");
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF);
            }
        }
    }

    private Color GetThemeColor(string resourceKey)
    {
        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is SolidColorBrush brush)
        {
            return brush.Color;
        }
        return Colors.White;
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
                _lastPageKey = ViewModel.NavigationViewService.GetSelectedItemKey();
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
