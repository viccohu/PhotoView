using Windows.Globalization;

using PhotoView.Contracts.Services;

namespace PhotoView.Services;

public class LanguageService : ILanguageService
{
    private readonly ILocalSettingsService _localSettingsService;
    private string _currentLanguage;

    private static readonly Dictionary<string, string> SupportedLanguagesMap = new()
    {
        { "default", "Use system language" },
        { "en-US", "English" },
        { "zh-CN", "中文（简体）" }
    };

    public string CurrentLanguage => _currentLanguage;

    public IEnumerable<string> SupportedLanguages => SupportedLanguagesMap.Keys;

    public event EventHandler<string>? LanguageChanged;

    public LanguageService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
        _currentLanguage = ApplicationLanguages.PrimaryLanguageOverride;
        
        if (string.IsNullOrEmpty(_currentLanguage))
        {
            _currentLanguage = "default";
        }
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        if (!SupportedLanguagesMap.ContainsKey(languageCode))
        {
            throw new ArgumentException($"Unsupported language: {languageCode}");
        }

        var previousLanguage = _currentLanguage;
        
        if (languageCode == "default")
        {
            ApplicationLanguages.PrimaryLanguageOverride = "";
            _currentLanguage = "default";
        }
        else
        {
            ApplicationLanguages.PrimaryLanguageOverride = languageCode;
            _currentLanguage = languageCode;
        }

        await _localSettingsService.SaveSettingAsync("AppLanguage", languageCode);

        if (previousLanguage != _currentLanguage)
        {
            LanguageChanged?.Invoke(this, _currentLanguage);
        }
    }

    public async Task InitializeAsync()
    {
        var savedLanguage = await _localSettingsService.ReadSettingAsync<string>("AppLanguage");
        
        if (!string.IsNullOrEmpty(savedLanguage) && SupportedLanguagesMap.ContainsKey(savedLanguage))
        {
            if (savedLanguage == "default")
            {
                ApplicationLanguages.PrimaryLanguageOverride = "";
                _currentLanguage = "default";
            }
            else
            {
                ApplicationLanguages.PrimaryLanguageOverride = savedLanguage;
                _currentLanguage = savedLanguage;
            }
        }
    }

    public static string GetLanguageDisplayName(string languageCode)
    {
        return SupportedLanguagesMap.TryGetValue(languageCode, out var displayName) 
            ? displayName 
            : languageCode;
    }
}
