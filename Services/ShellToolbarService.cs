using Microsoft.UI.Xaml;

namespace PhotoView.Services;

public sealed class ShellToolbarService
{
    private object? _owner;

    public event EventHandler? ToolbarChanged;

    public UIElement? CurrentToolbar { get; private set; }

    public void SetToolbar(object owner, UIElement toolbar)
    {
        _owner = owner;
        CurrentToolbar = toolbar;
        ToolbarChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearToolbar(object owner)
    {
        if (!ReferenceEquals(_owner, owner))
            return;

        _owner = null;
        CurrentToolbar = null;
        ToolbarChanged?.Invoke(this, EventArgs.Empty);
    }
}
