using System.Reflection;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PhotoView.Contracts.Services;
using PhotoView.Helpers;
using PhotoView.Models;
using PhotoView.Services;

using Windows.ApplicationModel;

namespace PhotoView.ViewModels;

public class SettingsViewModel : ObservableRecipient
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ISettingsService _settingsService;
    private readonly ILanguageService _languageService;
    private ElementTheme _elementTheme;
    private string _versionDescription;
    private string _currentLanguage;

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

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set => SetProperty(ref _currentLanguage, value);
    }

    public IEnumerable<string> SupportedLanguages => _languageService.SupportedLanguages;

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

    public ThumbnailSize ThumbnailSize
    {
        get => _settingsService.ThumbnailSize;
        set
        {
            if (_settingsService.ThumbnailSize != value)
            {
                _settingsService.ThumbnailSize = value;
                OnPropertyChanged();
            }
        }
    }

    public bool RememberLastFolder
    {
        get => _settingsService.RememberLastFolder;
        set
        {
            if (_settingsService.RememberLastFolder != value)
            {
                _settingsService.RememberLastFolder = value;
                OnPropertyChanged();
            }
        }
    }

    public bool DeleteToRecycleBin
    {
        get => _settingsService.DeleteToRecycleBin;
        set
        {
            if (_settingsService.DeleteToRecycleBin != value)
            {
                _settingsService.DeleteToRecycleBin = value;
                OnPropertyChanged();
            }
        }
    }

    public bool AlwaysDecodeRaw
    {
        get => _settingsService.AlwaysDecodeRaw;
        set
        {
            if (_settingsService.AlwaysDecodeRaw != value)
            {
                _settingsService.AlwaysDecodeRaw = value;
                OnPropertyChanged();
            }
        }
    }

    public bool MainPageAutoCollapseSidebar
    {
        get => _settingsService.MainPageAutoCollapseSidebar;
        set
        {
            if (_settingsService.MainPageAutoCollapseSidebar != value)
            {
                _settingsService.MainPageAutoCollapseSidebar = value;
                OnPropertyChanged();
            }
        }
    }

    public bool PreferPsdAsPrimaryPreview
    {
        get => _settingsService.PreferPsdAsPrimaryPreview;
        set
        {
            if (_settingsService.PreferPsdAsPrimaryPreview != value)
            {
                _settingsService.PreferPsdAsPrimaryPreview = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CollapseBurstGroups
    {
        get => _settingsService.CollapseBurstGroups;
        set
        {
            if (_settingsService.CollapseBurstGroups != value)
            {
                _settingsService.CollapseBurstGroups = value;
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

    public ICommand SetThumbnailSizeCommand
    {
        get;
    }

    public ICommand SetRememberLastFolderCommand
    {
        get;
    }

    public ICommand SetDeleteToRecycleBinCommand
    {
        get;
    }

    public ICommand SetAlwaysDecodeRawCommand
    {
        get;
    }

    public ICommand SetMainPageAutoCollapseSidebarCommand
    {
        get;
    }

    public ICommand SetPreferPsdAsPrimaryPreviewCommand
    {
        get;
    }

    public ICommand SetCollapseBurstGroupsCommand
    {
        get;
    }

    public ICommand SetLanguageCommand
    {
        get;
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, ISettingsService settingsService, ILanguageService languageService)
    {
        _themeSelectorService = themeSelectorService;
        _settingsService = settingsService;
        _languageService = languageService;
        _elementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();
        _currentLanguage = _languageService.CurrentLanguage;

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

        SetThumbnailSizeCommand = new RelayCommand<ThumbnailSize>(
            async (param) =>
            {
                if (ThumbnailSize != param)
                {
                    ThumbnailSize = param;
                    await _settingsService.SaveThumbnailSizeAsync(param);
                }
            });

        SetRememberLastFolderCommand = new RelayCommand<bool>(
            async (param) =>
            {
                if (RememberLastFolder != param)
                {
                    RememberLastFolder = param;
                    await _settingsService.SaveRememberLastFolderAsync(param);
                }
            });

        SetDeleteToRecycleBinCommand = new RelayCommand<bool>(
            async (param) =>
            {
                if (DeleteToRecycleBin != param)
                {
                    DeleteToRecycleBin = param;
                    await _settingsService.SaveDeleteToRecycleBinAsync(param);
                }
            });

        SetAlwaysDecodeRawCommand = new RelayCommand<bool>(
            async (param) =>
            {
                if (AlwaysDecodeRaw != param)
                {
                    AlwaysDecodeRaw = param;
                    await _settingsService.SaveAlwaysDecodeRawAsync(param);
                }
            });

        SetMainPageAutoCollapseSidebarCommand = new RelayCommand<bool>(
            async (param) =>
            {
                if (MainPageAutoCollapseSidebar != param)
                {
                    MainPageAutoCollapseSidebar = param;
                    await _settingsService.SaveMainPageAutoCollapseSidebarAsync(param);
                }
            });

        SetPreferPsdAsPrimaryPreviewCommand = new RelayCommand<bool>(
            async (param) =>
            {
                if (PreferPsdAsPrimaryPreview != param)
                {
                    PreferPsdAsPrimaryPreview = param;
                    await _settingsService.SavePreferPsdAsPrimaryPreviewAsync(param);
                }
            });

        SetCollapseBurstGroupsCommand = new RelayCommand<bool>(
            async (param) =>
            {
                if (CollapseBurstGroups != param)
                {
                    CollapseBurstGroups = param;
                    await _settingsService.SaveCollapseBurstGroupsAsync(param);
                }
            });

        SetLanguageCommand = new RelayCommand<string>(
            async (param) =>
            {
                if (!string.IsNullOrWhiteSpace(param) && CurrentLanguage != param)
                {
                    CurrentLanguage = param;
                    await _languageService.SetLanguageAsync(param);
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
