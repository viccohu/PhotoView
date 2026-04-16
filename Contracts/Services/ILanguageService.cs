namespace PhotoView.Contracts.Services;

public interface ILanguageService
{
    string CurrentLanguage
    {
        get;
    }

    IEnumerable<string> SupportedLanguages
    {
        get;
    }

    Task SetLanguageAsync(string languageCode);

    event EventHandler<string>? LanguageChanged;
}
