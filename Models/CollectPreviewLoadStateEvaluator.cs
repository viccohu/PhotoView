namespace PhotoView.Models;

public static class CollectPreviewLoadStateEvaluator
{
    public static CollectPreviewLoadState Determine(
        bool hasLoadedPreview,
        IReadOnlyList<PreviewSource> selectedSources,
        IReadOnlySet<string> loadedSourcePaths,
        IReadOnlyDictionary<string, bool> loadedSourceIncludeSubfolders)
    {
        if (!hasLoadedPreview || selectedSources.Count == 0)
        {
            return CollectPreviewLoadState.Load;
        }

        foreach (var source in selectedSources)
        {
            if (!loadedSourcePaths.Contains(source.Path) ||
                !loadedSourceIncludeSubfolders.TryGetValue(source.Path, out var loadedIncludeSubfolders) ||
                source.IncludeSubfolders != loadedIncludeSubfolders)
            {
                return CollectPreviewLoadState.Append;
            }
        }

        return CollectPreviewLoadState.Refresh;
    }
}
