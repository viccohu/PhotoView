using PhotoView.Models;
using System.Collections.ObjectModel;
using System.IO;

namespace PhotoView.Services;

public sealed class PreviewWorkspaceService
{
    public const int MaxSourceCount = 5;

    public ObservableCollection<PreviewSource> SelectedSources { get; } = new();

    public event EventHandler? SourcesChanged;

    public bool AddSource(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var normalizedPath = NormalizePath(path);
        if (!Directory.Exists(normalizedPath))
            return false;

        if (SelectedSources.Any(source => string.Equals(source.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (SelectedSources.Count >= MaxSourceCount)
            return false;

        SelectedSources.Add(new PreviewSource(normalizedPath));
        SourcesChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RemoveSource(PreviewSource source)
    {
        if (SelectedSources.Remove(source))
        {
            SourcesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        if (SelectedSources.Count == 0)
            return;

        SelectedSources.Clear();
        SourcesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
