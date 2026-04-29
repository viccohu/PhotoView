using PhotoView.Models;

namespace PhotoView.LogicTests;

internal static class CollectPreviewLoadStateEvaluatorChecks
{
    public static void Run()
    {
        using var sandbox = new TempDirectorySandbox();
        InitialState_IsLoad(sandbox.RootPath);
        LoadedMatchingSources_AreRefresh(sandbox.RootPath);
        LoadedThenNewSource_IsAppend(sandbox.RootPath);
        LoadedThenIncludeSubfoldersChanged_IsAppend(sandbox.RootPath);
        LoadedThenSourceRemoved_IsRefresh(sandbox.RootPath);
        LoadedThenSourcesCleared_IsLoad(sandbox.RootPath);
    }

    private static void InitialState_IsLoad(string rootPath)
    {
        var source = CreateSource(rootPath, "Initial");
        var state = Determine(hasLoadedPreview: false, new[] { source }, loaded: Array.Empty<PreviewSource>());

        TestAssert.Equal(CollectPreviewLoadState.Load, state, "Initial state should be Load.");
    }

    private static void LoadedMatchingSources_AreRefresh(string rootPath)
    {
        var source = CreateSource(rootPath, "Loaded");
        var state = Determine(hasLoadedPreview: true, new[] { source }, new[] { source });

        TestAssert.Equal(CollectPreviewLoadState.Refresh, state, "Matching loaded sources should be Refresh.");
    }

    private static void LoadedThenNewSource_IsAppend(string rootPath)
    {
        var loaded = CreateSource(rootPath, "LoadedWithNew");
        var added = CreateSource(rootPath, "NewSource");
        var state = Determine(hasLoadedPreview: true, new[] { loaded, added }, new[] { loaded });

        TestAssert.Equal(CollectPreviewLoadState.Append, state, "New source after loading should be Append.");
    }

    private static void LoadedThenIncludeSubfoldersChanged_IsAppend(string rootPath)
    {
        var loaded = CreateSource(rootPath, "LoadedFlat");
        var current = new PreviewSource(loaded.Path, includeSubfolders: true);
        var state = Determine(hasLoadedPreview: true, new[] { current }, new[] { loaded });

        TestAssert.Equal(CollectPreviewLoadState.Append, state, "IncludeSubfolders changes should be Append.");
    }

    private static void LoadedThenSourceRemoved_IsRefresh(string rootPath)
    {
        var kept = CreateSource(rootPath, "Kept");
        var removed = CreateSource(rootPath, "Removed");
        var state = Determine(hasLoadedPreview: true, new[] { kept }, new[] { kept, removed });

        TestAssert.Equal(CollectPreviewLoadState.Refresh, state, "Removing a loaded source should be Refresh.");
    }

    private static void LoadedThenSourcesCleared_IsLoad(string rootPath)
    {
        var loaded = CreateSource(rootPath, "Cleared");
        var state = Determine(hasLoadedPreview: true, Array.Empty<PreviewSource>(), new[] { loaded });

        TestAssert.Equal(CollectPreviewLoadState.Load, state, "No selected sources should be Load.");
    }

    private static PreviewSource CreateSource(string rootPath, string folderName)
    {
        var folder = Directory.CreateDirectory(Path.Combine(rootPath, folderName));
        return new PreviewSource(folder.FullName);
    }

    private static CollectPreviewLoadState Determine(
        bool hasLoadedPreview,
        IReadOnlyList<PreviewSource> selected,
        IReadOnlyList<PreviewSource> loaded)
    {
        var loadedPaths = loaded
            .Select(source => source.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var loadedIncludeSubfolders = loaded
            .ToDictionary(source => source.Path, source => source.IncludeSubfolders, StringComparer.OrdinalIgnoreCase);

        return CollectPreviewLoadStateEvaluator.Determine(
            hasLoadedPreview,
            selected,
            loadedPaths,
            loadedIncludeSubfolders);
    }
}
