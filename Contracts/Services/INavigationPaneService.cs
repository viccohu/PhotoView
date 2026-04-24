namespace PhotoView.Contracts.Services;

public interface INavigationPaneService
{
    event EventHandler? CurrentContextChanged;

    INavigationPaneContext? CurrentContext { get; }

    void SetContext(object owner, INavigationPaneContext context);

    void ClearContext(object owner);
}
