using Microsoft.UI.Xaml;

namespace PhotoView.Services;

public sealed class ShellToolbarService
{
    private object? _owner;

    public event EventHandler? ToolbarChanged;
    public event EventHandler? ProgressChanged;

    public UIElement? CurrentToolbar { get; private set; }

    public bool IsProgressVisible { get; private set; }
    public bool IsProgressIndeterminate { get; private set; }
    public double ProgressValue { get; private set; }

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

    public void UpdateProgress(bool isVisible, bool isIndeterminate, double value)
    {
        IsProgressVisible = isVisible;
        IsProgressIndeterminate = isIndeterminate;
        ProgressValue = value;
        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }
}
