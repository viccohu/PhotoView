using System.Text.RegularExpressions;
using System.Xml.Linq;
using PhotoView.Helpers;

namespace PhotoView.LogicTests;

internal static class MainPageLocalizationChecks
{
    private static readonly string[] ResourceCultures = ["zh-cn", "en-us"];
    private static readonly string[] ThumbnailSizes = ["Small", "Medium", "Large"];

    public static void Run()
    {
        MainPage_XamlUidKeys_AreDefinedInAllResourceDictionaries();
        MainPage_CodeBehindKeys_AreDefinedInAllResourceDictionaries();
        ResourceKeyHelper_ProvidesPriPathFallbackForPropertyKeys();
    }

    private static void MainPage_XamlUidKeys_AreDefinedInAllResourceDictionaries()
    {
        var root = FindRepositoryRoot();
        var mainPage = XDocument.Load(Path.Combine(root, "Views", "MainPage.xaml"));
        var expectedKeys = mainPage
            .Descendants()
            .Select(element => new
            {
                ElementName = element.Name.LocalName,
                Uid = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Uid")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Uid))
            .Select(item => $"{item.Uid}.{GetLocalizedPropertyName(item.ElementName)}")
            .OrderBy(key => key)
            .ToArray();

        AssertKeysExistInAllResourceDictionaries(root, expectedKeys, "MainPage XAML x:Uid resource keys");
    }

    private static void MainPage_CodeBehindKeys_AreDefinedInAllResourceDictionaries()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "Views", "MainPage.xaml.cs"));
        var literalKeys = Regex.Matches(code, "\"(?<key>[A-Za-z0-9_.]+)\"\\.GetLocalized\\(")
            .Select(match => match.Groups["key"].Value);
        var dynamicThumbnailKeys = ThumbnailSizes.Select(size => $"MainPage_Size{size}.Text");
        var expectedKeys = literalKeys
            .Concat(dynamicThumbnailKeys)
            .Distinct()
            .OrderBy(key => key)
            .ToArray();

        AssertKeysExistInAllResourceDictionaries(root, expectedKeys, "MainPage code-behind resource keys");
    }

    private static void ResourceKeyHelper_ProvidesPriPathFallbackForPropertyKeys()
    {
        var candidates = ResourceKeyHelper.GetLookupCandidates("MainPage_SizeSmall.Text").ToArray();

        TestAssert.Equal(2, candidates.Length, "Property resource keys should produce original and PRI path candidates.");
        TestAssert.Equal("MainPage_SizeSmall.Text", candidates[0], "The original resource key should be tried first.");
        TestAssert.Equal("MainPage_SizeSmall/Text", candidates[1], "The PRI path candidate should replace dots with slashes.");
    }

    private static void AssertKeysExistInAllResourceDictionaries(string root, IReadOnlyCollection<string> expectedKeys, string scenario)
    {
        TestAssert.True(expectedKeys.Count > 0, $"{scenario} should discover at least one resource key.");

        foreach (var culture in ResourceCultures)
        {
            var resources = LoadResourceKeys(Path.Combine(root, "Strings", culture, "Resources.resw"));
            var missing = expectedKeys
                .Where(key => !resources.Contains(key))
                .ToArray();

            TestAssert.True(
                missing.Length == 0,
                $"{scenario} missing from Strings/{culture}/Resources.resw: {string.Join(", ", missing)}");
        }
    }

    private static HashSet<string> LoadResourceKeys(string resourcePath)
    {
        var document = XDocument.Load(resourcePath);
        return document
            .Descendants("data")
            .Select(element => element.Attribute("name")?.Value)
            .OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string GetLocalizedPropertyName(string elementName)
    {
        return elementName switch
        {
            "MenuFlyoutItem" => "Text",
            "TextBlock" => "Text",
            _ => throw new InvalidOperationException($"MainPage x:Uid element '{elementName}' needs an explicit localization property mapping.")
        };
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PhotoView.csproj")) &&
                File.Exists(Path.Combine(directory.FullName, "Views", "MainPage.xaml")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the PhotoView repository root for localization checks.");
    }
}
