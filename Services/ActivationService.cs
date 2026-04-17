using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PhotoView.Activation;
using PhotoView.Contracts.Services;
using PhotoView.Views;

namespace PhotoView.Services;

public class ActivationService : IActivationService
{
    private readonly ActivationHandler<LaunchActivatedEventArgs> _defaultHandler;
    private readonly IEnumerable<IActivationHandler> _activationHandlers;
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILanguageService _languageService;
    private readonly ISettingsService _settingsService;
    private readonly IKeyboardShortcutService _shortcutService;
    private UIElement? _shell = null;

    public ActivationService(
        ActivationHandler<LaunchActivatedEventArgs> defaultHandler, 
        IEnumerable<IActivationHandler> activationHandlers, 
        IThemeSelectorService themeSelectorService, 
        ILanguageService languageService, 
        ISettingsService settingsService,
        IKeyboardShortcutService shortcutService)
    {
        _defaultHandler = defaultHandler;
        _activationHandlers = activationHandlers;
        _themeSelectorService = themeSelectorService;
        _languageService = languageService;
        _settingsService = settingsService;
        _shortcutService = shortcutService;
    }

    public async Task ActivateAsync(object activationArgs)
    {
        await InitializeAsync();

        if (App.MainWindow.Content == null)
        {
            _shell = App.GetService<ShellPage>();
            App.MainWindow.Content = _shell ?? new Frame();
        }

        _shortcutService.Initialize(App.MainWindow);

        await HandleActivationAsync(activationArgs);

        App.MainWindow.Activate();

        await StartupAsync();
    }

    private async Task HandleActivationAsync(object activationArgs)
    {
        var activationHandler = _activationHandlers.FirstOrDefault(h => h.CanHandle(activationArgs));

        if (activationHandler != null)
        {
            await activationHandler.HandleAsync(activationArgs);
        }

        if (_defaultHandler.CanHandle(activationArgs))
        {
            await _defaultHandler.HandleAsync(activationArgs);
        }
    }

    private async Task InitializeAsync()
    {
        await _themeSelectorService.InitializeAsync().ConfigureAwait(false);
        await _settingsService.InitializeAsync().ConfigureAwait(false);
        
        if (_languageService is Services.LanguageService languageService)
        {
            await languageService.InitializeAsync().ConfigureAwait(false);
        }
        
        await Task.CompletedTask;
    }

    private async Task StartupAsync()
    {
        await _themeSelectorService.SetRequestedThemeAsync();
        await Task.CompletedTask;
    }
}
