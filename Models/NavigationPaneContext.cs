using System.Collections.ObjectModel;
using PhotoView.Contracts.Services;

namespace PhotoView.Models;

public partial class NavigationPaneContext : ObservableObject, INavigationPaneContext
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string? _subtitle;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _rootNodes = new();

    [ObservableProperty]
    private FolderNode? _selectedNode;

    [ObservableProperty]
    private bool _activateOnSingleClick = true;

    [ObservableProperty]
    private bool _activateOnDoubleTap;

    public Func<FolderNode, Task>? ExpandNodeHandler { get; set; }

    public Func<FolderNode, Task>? SelectNodeHandler { get; set; }

    public Func<FolderNode, Task>? ActivateNodeHandler { get; set; }

    public Func<FolderNode, Task>? ActivateNodeSecondaryHandler { get; set; }

    public Func<FolderNode, IReadOnlyList<NavigationPaneNodeAction>>? NodeActionsProvider { get; set; }

    public Task ExpandNodeAsync(FolderNode node)
    {
        return ExpandNodeHandler?.Invoke(node) ?? Task.CompletedTask;
    }

    public Task SelectNodeAsync(FolderNode node)
    {
        SelectedNode = node;
        return SelectNodeHandler?.Invoke(node) ?? Task.CompletedTask;
    }

    public Task ActivateNodeAsync(FolderNode node)
    {
        SelectedNode = node;
        return ActivateNodeHandler?.Invoke(node) ?? Task.CompletedTask;
    }

    public Task ActivateNodeSecondaryAsync(FolderNode node)
    {
        SelectedNode = node;
        return ActivateNodeSecondaryHandler?.Invoke(node) ?? Task.CompletedTask;
    }

    public IReadOnlyList<NavigationPaneNodeAction> GetNodeActions(FolderNode node)
    {
        return NodeActionsProvider?.Invoke(node) ?? Array.Empty<NavigationPaneNodeAction>();
    }
}
