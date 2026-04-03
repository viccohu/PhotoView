using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using PhotoView.ViewModels;

namespace PhotoView.Views;

public sealed partial class SettingsPage : Page
{
    private int lastNavigationSelectionMode = 0;

    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        Loaded += OnSettingsPageLoaded;
    }

    private void OnSettingsPageLoaded(object sender, RoutedEventArgs e)
    {
        var currentTheme = ViewModel.ElementTheme;
        switch (currentTheme)
        {
            case ElementTheme.Light:
                themeMode.SelectedIndex = 0;
                break;
            case ElementTheme.Dark:
                themeMode.SelectedIndex = 1;
                break;
            case ElementTheme.Default:
                themeMode.SelectedIndex = 2;
                break;
        }

        if (ViewModel.NavigationViewMode == Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Top)
        {
            navigationLocation.SelectedIndex = 0;
        }
        else
        {
            navigationLocation.SelectedIndex = 1;
        }

        lastNavigationSelectionMode = navigationLocation.SelectedIndex;
    }

    private void themeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not UIElement senderUiElement ||
            (themeMode.SelectedItem as ComboBoxItem)?.Tag.ToString() is not string selectedTheme)
        {
            return;
        }

        if (Enum.TryParse<ElementTheme>(selectedTheme, out var theme))
        {
            ViewModel.SwitchThemeCommand.Execute(theme);
        }
    }

    private void navigationLocation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (navigationLocation.SelectedIndex != lastNavigationSelectionMode)
        {
            var mode = navigationLocation.SelectedIndex == 0 
                ? Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Top 
                : Microsoft.UI.Xaml.Controls.NavigationViewPaneDisplayMode.Left;
            ViewModel.SwitchNavigationModeCommand.Execute(mode);
            lastNavigationSelectionMode = navigationLocation.SelectedIndex;
        }
    }
}