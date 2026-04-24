namespace PhotoView.Models;

public sealed class NavigationPaneSourceItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string DisplayName { get; init; } = string.Empty;

    public object? Payload { get; init; }
}
