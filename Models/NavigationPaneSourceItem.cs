namespace PhotoView.Models;

public sealed class NavigationPaneSourceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public bool IncludeSubfolders { get; set; }

    public object? Payload { get; set; }
}
