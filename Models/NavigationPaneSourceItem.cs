namespace PhotoView.Models;

public sealed class NavigationPaneSourceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; set; } = string.Empty;

    public object? Payload { get; set; }
}
