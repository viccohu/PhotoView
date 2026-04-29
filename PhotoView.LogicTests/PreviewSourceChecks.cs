using PhotoView.Models;

namespace PhotoView.LogicTests;

internal static class PreviewSourceChecks
{
    public static void Run()
    {
        using var sandbox = new TempDirectorySandbox();
        DisplayName_ContainsParentAndFolderName(sandbox.RootPath);
        IncludeSubfolders_DefaultsToFalse(sandbox.RootPath);
        IncludeSubfolders_UsesConstructorValue(sandbox.RootPath);
    }

    private static void DisplayName_ContainsParentAndFolderName(string rootPath)
    {
        var parent = Directory.CreateDirectory(Path.Combine(rootPath, "Projects"));
        var folder = Directory.CreateDirectory(Path.Combine(parent.FullName, "Album"));
        var source = new PreviewSource(folder.FullName + Path.DirectorySeparatorChar);

        TestAssert.Equal(folder.FullName, source.Path, "PreviewSource should normalize the stored path.");
        TestAssert.Equal("Projects / Album", source.DisplayName, "PreviewSource should show parent and folder name.");
    }

    private static void IncludeSubfolders_DefaultsToFalse(string rootPath)
    {
        var folder = Directory.CreateDirectory(Path.Combine(rootPath, "FlatSource"));
        var source = new PreviewSource(folder.FullName);

        TestAssert.False(source.IncludeSubfolders, "PreviewSource should default IncludeSubfolders to false.");
    }

    private static void IncludeSubfolders_UsesConstructorValue(string rootPath)
    {
        var folder = Directory.CreateDirectory(Path.Combine(rootPath, "RecursiveSource"));
        var source = new PreviewSource(folder.FullName, includeSubfolders: true);

        TestAssert.True(source.IncludeSubfolders, "PreviewSource should use the requested IncludeSubfolders value.");
    }
}
