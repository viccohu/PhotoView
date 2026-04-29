using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace PhotoView.Models;

public partial class PreviewSource : ObservableObject
{
    public PreviewSource(string path, bool includeSubfolders = false)
    {
        Path = NormalizePath(path);
        DisplayName = CreateDisplayName(Path);
        IncludeSubfolders = includeSubfolders;
    }

    public string Path { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    private bool _includeSubfolders;

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
    }

    private static string CreateDisplayName(string path)
    {
        try
        {
            var folderName = System.IO.Path.GetFileName(path);
            var parentPath = System.IO.Path.GetDirectoryName(path);
            var parentName = string.IsNullOrWhiteSpace(parentPath)
                ? string.Empty
                : System.IO.Path.GetFileName(parentPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

            if (string.IsNullOrWhiteSpace(folderName))
            {
                return path;
            }

            return string.IsNullOrWhiteSpace(parentName)
                ? folderName
                : $"{parentName} / {folderName}";
        }
        catch
        {
            return path;
        }
    }
}
