using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using PhotoView.Models;
using PhotoView.ViewModels;

namespace PhotoView.Views;

public sealed partial class SettingsPage : Page
{
    private int lastNavigationSelectionMode = 0;
    private bool _isInitialized = false;

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
        _isInitialized = false;
        
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
        UpdateThumbnailSizeSelection();
        UpdateRememberLastFolderSelection();
        UpdateDeleteToRecycleBinSelection();
        UpdateAlwaysDecodeRawSelection();
        UpdateMainPageAutoCollapseSidebarSelection();
        UpdatePreferPsdAsPrimaryPreviewSelection();
        UpdateLanguageSelection();
        
        _isInitialized = true;
    }

    private void UpdateLanguageSelection()
    {
        var currentLanguage = ViewModel.CurrentLanguage;
        for (int i = 0; i < languageComboBox.Items.Count; i++)
        {
            if (languageComboBox.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == currentLanguage)
            {
                languageComboBox.SelectedIndex = i;
                break;
            }
        }
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

    private void UpdateThumbnailSizeSelection()
    {
        var thumbnailSize = ViewModel.ThumbnailSize;
        for (int i = 0; i < ThumbnailSizeComboBox.Items.Count; i++)
        {
            if (ThumbnailSizeComboBox.Items[i] is ComboBoxItem item &&
                item.Tag?.ToString() == thumbnailSize.ToString())
            {
                ThumbnailSizeComboBox.SelectedIndex = i;
                break;
            }
        }
    }

    private void UpdateRememberLastFolderSelection()
    {
        RememberLastFolderToggleSwitch.IsOn = ViewModel.RememberLastFolder;
    }

    private void UpdateDeleteToRecycleBinSelection()
    {
        DeleteToRecycleBinToggleSwitch.IsOn = ViewModel.DeleteToRecycleBin;
    }

    private void UpdateAlwaysDecodeRawSelection()
    {
        AlwaysDecodeRawToggleSwitch.IsOn = ViewModel.AlwaysDecodeRaw;
    }

    private void UpdateMainPageAutoCollapseSidebarSelection()
    {
        MainPageAutoCollapseSidebarToggleSwitch.IsOn = ViewModel.MainPageAutoCollapseSidebar;
    }

    private void UpdatePreferPsdAsPrimaryPreviewSelection()
    {
        PreferPsdAsPrimaryPreviewToggleSwitch.IsOn = ViewModel.PreferPsdAsPrimaryPreview;
    }

    private void themeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
            return;
            
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
        if (!_isInitialized)
            return;
            
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
        if (!_isInitialized)
            return;
            
        if (BatchSizeComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var batchSize))
        {
            ViewModel.SetBatchSizeCommand.Execute(batchSize);
        }
    }

    private void PerformanceModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
            return;
            
        if (PerformanceModeComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag?.ToString() is string tag &&
            Enum.TryParse<PerformanceMode>(tag, out var mode))
        {
            ViewModel.SetPerformanceModeCommand.Execute(mode);
        }
    }

    private void ThumbnailSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
            return;
            
        if (ThumbnailSizeComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag?.ToString() is string tag &&
            Enum.TryParse<ThumbnailSize>(tag, out var size))
        {
            ViewModel.SetThumbnailSizeCommand.Execute(size);
        }
    }

    private void RememberLastFolderToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
            return;

        ViewModel.SetRememberLastFolderCommand.Execute(RememberLastFolderToggleSwitch.IsOn);
    }

    private void DeleteToRecycleBinToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
            return;

        ViewModel.SetDeleteToRecycleBinCommand.Execute(DeleteToRecycleBinToggleSwitch.IsOn);
    }

    private void AlwaysDecodeRawToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
            return;

        ViewModel.SetAlwaysDecodeRawCommand.Execute(AlwaysDecodeRawToggleSwitch.IsOn);
    }

    private void MainPageAutoCollapseSidebarToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
            return;

        ViewModel.SetMainPageAutoCollapseSidebarCommand.Execute(MainPageAutoCollapseSidebarToggleSwitch.IsOn);
    }

    private void PreferPsdAsPrimaryPreviewToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
            return;

        ViewModel.SetPreferPsdAsPrimaryPreviewCommand.Execute(PreferPsdAsPrimaryPreviewToggleSwitch.IsOn);
    }

    private void languageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
            return;

        if (languageComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag?.ToString() is string languageCode)
        {
            ViewModel.SetLanguageCommand.Execute(languageCode);
        }
    }
}
