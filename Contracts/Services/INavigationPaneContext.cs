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

    ObservableCollection<NavigationPaneSourceItem> SourceItems { get; }

    bool HasSourceItems { get; }

    NavigationPaneHeaderAction? PrimaryAction { get; }

    bool HasPrimaryAction { get; }

    string? ToggleOptionText { get; }

    bool IsToggleOptionVisible { get; }

    bool ToggleOptionValue { get; set; }

    string? StatusText { get; }

    bool HasStatusText { get; }

    bool IsProgressVisible { get; }

    bool IsProgressIndeterminate { get; }

    double ProgressValue { get; }

    Task ExpandNodeAsync(FolderNode node);

    Task SelectNodeAsync(FolderNode node);

    Task ActivateNodeAsync(FolderNode node);

    Task ActivateNodeSecondaryAsync(FolderNode node);

    IReadOnlyList<NavigationPaneNodeAction> GetNodeActions(FolderNode node);

    Task RemoveSourceAsync(NavigationPaneSourceItem item);

    Task ExecutePrimaryActionAsync();

    Task ToggleOptionAsync(bool value);
}
