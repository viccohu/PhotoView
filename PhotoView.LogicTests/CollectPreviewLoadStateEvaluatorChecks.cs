using PhotoView.Models;

namespace PhotoView.LogicTests;

internal static class CollectPreviewLoadStateEvaluatorChecks
{
    public static void Run()
    {
        NoLoadedPreview_ReturnsLoad();
        AddedSource_ReturnsAppend();
        MatchingLoadedSources_ReturnsRefresh();
        IncludeSubfoldersChange_ReturnsAppend();
    }

    private static void NoLoadedPreview_ReturnsLoad()
    {
        var sources = new[] { new PreviewSource(@"C:\Photos") };

        var state = CollectPreviewLoadStateEvaluator.Determine(
            hasLoadedPreview: false,
            sources,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

        TestAssert.Equal(CollectPreviewLoadState.Load, state, "First preview load should show Load.");
    }

    private static void AddedSource_ReturnsAppend()
    {
        var sources = new[]
        {
            new PreviewSource(@"C:\Photos"),
            new PreviewSource(@"D:\More")
        };
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Photos" };
        var loadedInclude = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Photos"] = false
        };

        var state = CollectPreviewLoadStateEvaluator.Determine(true, sources, loadedPaths, loadedInclude);

        TestAssert.Equal(CollectPreviewLoadState.Append, state, "Adding a new source should show Append.");
    }

    private static void MatchingLoadedSources_ReturnsRefresh()
    {
        var sources = new[] { new PreviewSource(@"C:\Photos", includeSubfolders: true) };
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Photos" };
        var loadedInclude = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Photos"] = true
        };

        var state = CollectPreviewLoadStateEvaluator.Determine(true, sources, loadedPaths, loadedInclude);

        TestAssert.Equal(CollectPreviewLoadState.Refresh, state, "Unchanged loaded sources should show Refresh.");
    }

    private static void IncludeSubfoldersChange_ReturnsAppend()
    {
        var sources = new[] { new PreviewSource(@"C:\Photos", includeSubfolders: true) };
        var loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\Photos" };
        var loadedInclude = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\Photos"] = false
        };

        var state = CollectPreviewLoadStateEvaluator.Determine(true, sources, loadedPaths, loadedInclude);

        TestAssert.Equal(CollectPreviewLoadState.Append, state, "Changing subfolder inclusion should require another load.");
    }
}
