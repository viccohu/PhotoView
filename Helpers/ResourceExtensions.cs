using Microsoft.Windows.ApplicationModel.Resources;

namespace PhotoView.Helpers;

public static class ResourceExtensions
{
    private static readonly ResourceLoader _resourceLoader = new();

    public static string GetLocalized(this string resourceKey)
    {
        foreach (var candidate in ResourceKeyHelper.GetLookupCandidates(resourceKey))
        {
            var value = TryGetString(candidate);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return resourceKey;
    }

    private static string TryGetString(string resourceKey)
    {
        try
        {
            return _resourceLoader.GetString(resourceKey);
        }
        catch
        {
            return string.Empty;
        }
    }
}
