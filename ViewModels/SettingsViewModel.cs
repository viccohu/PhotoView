using System.Reflection;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PhotoView.Contracts.Services;
using PhotoView.Helpers;
using PhotoView.Models;

using Windows.ApplicationModel;

namespace PhotoView.ViewModels;

public class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ISettingsService _settingsService;
    private ElementTheme _elementTheme;
    private string _versionDescription;

    public ElementTheme ElementTheme
    {
        get => _elementTheme;
        set => SetProperty(ref _elementTheme, value);
    }

    public string VersionDescription
    {
        get => _versionDescription;
        set => SetProperty(ref _versionDescription, value);
    }

    public NavigationViewPaneDisplayMode NavigationViewMode
    {
        get => _settingsService.NavigationViewMode;
        set
        {
            if (_settingsService.NavigationViewMode != value)
            {
                _settingsService.NavigationViewMode = value;
                OnPropertyChanged();
            }
        }
    }

    public int BatchSize
    {
        get => _settingsService.BatchSize;
        set
        {
            if (_settingsService.BatchSize != value)
            {
                _settingsService.BatchSize = value;
                OnPropertyChanged();
            }
        }
    }

    public PerformanceMode PerformanceMode
    {
        get => _settingsService.PerformanceMode;
        set
        {
            if (_settingsService.PerformanceMode != value)
            {
                _settingsService.PerformanceMode = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand SwitchThemeCommand
    {
        get;
    }

    public ICommand SwitchNavigationModeCommand
    {
        get;
    }

    public ICommand SetBatchSizeCommand
    {
        get;
    }

    public ICommand SetPerformanceModeCommand
    {
        get;
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, ISettingsService settingsService)
    {
        _themeSelectorService = themeSelectorService;
        _settingsService = settingsService;
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();

        SwitchThemeCommand = new RelayCommand<ElementTheme>(
            async (param) =>
            {
                if (ElementTheme != param)
                {
                    ElementTheme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });

        SwitchNavigationModeCommand = new RelayCommand<NavigationViewPaneDisplayMode>(
            async (param) =>
            {
                if (NavigationViewMode != param)
                {
                    NavigationViewMode = param;
                    await _settingsService.SaveNavigationViewModeAsync(param);
                }
            });

        SetBatchSizeCommand = new RelayCommand<int>(
            async (param) =>
            {
                if (BatchSize != param)
                {
                    BatchSize = param;
                    await _settingsService.SaveBatchSizeAsync(param);
                }
            });

        SetPerformanceModeCommand = new RelayCommand<PerformanceMode>(
            async (param) =>
            {
                if (PerformanceMode != param)
                {
                    PerformanceMode = param;
                    await _settingsService.SavePerformanceModeAsync(param);
                }
            });
    }

    private static string GetVersionDescription()
    {
        Version version;

        if (RuntimeHelper.IsMSIX)
        {
            var packageVersion = Package.Current.Id.Version;

            version = new(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
        }
        else
        {
            version = Assembly.GetExecutingAssembly().GetName().Version!;
        }

        return $"{"AppDisplayName".GetLocalized()} - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}