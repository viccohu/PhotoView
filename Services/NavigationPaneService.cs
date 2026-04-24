using PhotoView.Contracts.Services;

namespace PhotoView.Services;

public sealed class NavigationPaneService : INavigationPaneService
{
    private object? _owner;

    public event EventHandler? CurrentContextChanged;

    public INavigationPaneContext? CurrentContext { get; private set; }

    public void SetContext(object owner, INavigationPaneContext context)
    {
        _owner = owner;
        CurrentContext = context;
        CurrentContextChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearContext(object owner)
    {
        if (!ReferenceEquals(_owner, owner))
        {
            return;
        }

        _owner = null;
        CurrentContext = null;
        CurrentContextChanged?.Invoke(this, EventArgs.Empty);
    }
}
