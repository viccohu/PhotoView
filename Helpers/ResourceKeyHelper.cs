namespace PhotoView.Helpers;

public static class ResourceKeyHelper
{
    public static IEnumerable<string> GetLookupCandidates(string resourceKey)
    {
        if (resourceKey.Contains('.'))
        {
            yield return resourceKey.Replace('.', '/');
        }

        yield return resourceKey;
    }
}
