namespace PhotoView.Models;

public sealed class NavigationPaneNodeAction
{
    public string Text { get; init; } = string.Empty;

    public string Glyph { get; init; } = string.Empty;

    public bool IsSeparator { get; init; }

    public Func<FolderNode, Task>? ExecuteAsync { get; init; }
}
