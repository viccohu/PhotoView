using System.Collections.ObjectModel;
using PhotoView.Models;

namespace PhotoView.Contracts.Services;

public interface INavigationPaneContext
{
    string Title { get; }

    string? Subtitle { get; }

    ObservableCollection<FolderNode> RootNodes { get; }

    FolderNode? SelectedNode { get; set; }

    bool ActivateOnSingleClick { get; }

    bool ActivateOnDoubleTap { get; }

    Task ExpandNodeAsync(FolderNode node);

    Task SelectNodeAsync(FolderNode node);

    Task ActivateNodeAsync(FolderNode node);

    Task ActivateNodeSecondaryAsync(FolderNode node);

    IReadOnlyList<NavigationPaneNodeAction> GetNodeActions(FolderNode node);
}
