using PhotoView.Services;

namespace PhotoView.LogicTests;

internal static class PreviewWorkspaceServiceChecks
{
    public static void Run()
    {
        using var sandbox = new TempDirectorySandbox();
        AddSource_NormalizesPath_AndAvoidsDuplicates(sandbox.RootPath);
        AddSource_StoresIncludeSubfolders(sandbox.RootPath);
        AddSource_DuplicateDoesNotOverwriteIncludeSubfolders(sandbox.RootPath);
        AddSource_StopsAtMaximumCount(sandbox.RootPath);
        RemoveSource_RaisesSourcesChanged(sandbox.RootPath);
    }

    private static void AddSource_NormalizesPath_AndAvoidsDuplicates(string rootPath)
    {
        var service = new PreviewWorkspaceService();
        var folder = Directory.CreateDirectory(Path.Combine(rootPath, "SessionA"));

        var first = service.AddSource(folder.FullName + Path.DirectorySeparatorChar);
        var duplicate = service.AddSource(folder.FullName);

        TestAssert.True(first, "First add should succeed.");
        TestAssert.True(duplicate, "Duplicate add should be treated as success.");
        TestAssert.Equal(1, service.SelectedSources.Count, "Duplicate add should not create a second source.");
        TestAssert.Equal(folder.FullName, service.SelectedSources[0].Path, "Stored path should be normalized.");
    }

    private static void AddSource_StoresIncludeSubfolders(string rootPath)
    {
        var service = new PreviewWorkspaceService();
        var folder = Directory.CreateDirectory(Path.Combine(rootPath, "RecursiveSession"));

        var added = service.AddSource(folder.FullName, includeSubfolders: true);

        TestAssert.True(added, "Source should be added.");
        TestAssert.True(service.SelectedSources[0].IncludeSubfolders, "Added source should store the include-subfolders option.");
    }

    private static void AddSource_DuplicateDoesNotOverwriteIncludeSubfolders(string rootPath)
    {
        var service = new PreviewWorkspaceService();
        var folder = Directory.CreateDirectory(Path.Combine(rootPath, "DuplicateRecursiveSession"));

        service.AddSource(folder.FullName, includeSubfolders: true);
        var duplicate = service.AddSource(folder.FullName + Path.DirectorySeparatorChar, includeSubfolders: false);

        TestAssert.True(duplicate, "Duplicate add should be treated as success.");
        TestAssert.Equal(1, service.SelectedSources.Count, "Duplicate add should not create a second source.");
        TestAssert.True(service.SelectedSources[0].IncludeSubfolders, "Duplicate add should not overwrite the existing include-subfolders option.");
    }

    private static void AddSource_StopsAtMaximumCount(string rootPath)
    {
        var service = new PreviewWorkspaceService();

        for (var index = 0; index < PreviewWorkspaceService.MaxSourceCount; index++)
        {
            var path = Directory.CreateDirectory(Path.Combine(rootPath, $"Folder{index}")).FullName;
            TestAssert.True(service.AddSource(path), $"Source {index} should be added.");
        }

        var overflowPath = Directory.CreateDirectory(Path.Combine(rootPath, "Overflow")).FullName;

        TestAssert.False(service.AddSource(overflowPath), "Source count should stop at MaxSourceCount.");
        TestAssert.Equal(PreviewWorkspaceService.MaxSourceCount, service.SelectedSources.Count, "Selected sources should stop at max count.");
    }

    private static void RemoveSource_RaisesSourcesChanged(string rootPath)
    {
        var service = new PreviewWorkspaceService();
        var path = Directory.CreateDirectory(Path.Combine(rootPath, "Selected")).FullName;
        service.AddSource(path);
        var source = service.SelectedSources.Single();
        var changedCount = 0;

        service.SourcesChanged += (_, _) => changedCount++;
        service.RemoveSource(source);

        TestAssert.Equal(0, service.SelectedSources.Count, "Removing source should empty the selection.");
        TestAssert.Equal(1, changedCount, "Removing source should raise SourcesChanged once.");
    }
}
