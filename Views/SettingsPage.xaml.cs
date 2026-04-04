using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using PhotoView.Models;
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

        UpdateBatchSizeSelection();
        UpdatePerformanceModeSelection();
    }

    private void UpdateBatchSizeSelection()
    {
        var batchSize = ViewModel.BatchSize;
        for (int i = 0; i < BatchSizeComboBox.Items.Count; i++)
        {
            if (BatchSizeComboBox.Items[i] is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var tagValue) &&
                tagValue == batchSize)
            {
                BatchSizeComboBox.SelectedIndex = i;
                break;
            }
        }
    }

    private void UpdatePerformanceModeSelection()
    {
        var performanceMode = ViewModel.PerformanceMode;
        for (int i = 0; i < PerformanceModeComboBox.Items.Count; i++)
        {
            if (PerformanceModeComboBox.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == performanceMode.ToString())
            {
                PerformanceModeComboBox.SelectedIndex = i;
                break;
            }
        }
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

    private void BatchSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BatchSizeComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var batchSize))
        {
            ViewModel.SetBatchSizeCommand.Execute(batchSize);
        }
    }

    private void PerformanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PerformanceModeComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag?.ToString() is string tag &&
            Enum.TryParse<PerformanceMode>(tag, out var mode))
        {
            ViewModel.SetPerformanceModeCommand.Execute(mode);
        }
    }
}
