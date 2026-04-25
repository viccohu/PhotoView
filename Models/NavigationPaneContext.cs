using System.Collections.ObjectModel;
using System.Collections.Specialized;
using PhotoView.Contracts.Services;

namespace PhotoView.Models;

public partial class NavigationPaneContext : ObservableObject, INavigationPaneContext
{
    public NavigationPaneContext()
    {
        _sourceItems.CollectionChanged += SourceItems_CollectionChanged;
    }

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

    [ObservableProperty]
    private ObservableCollection<NavigationPaneSourceItem> _sourceItems = new();

    [ObservableProperty]
    private NavigationPaneHeaderAction? _primaryAction;

    [ObservableProperty]
    private string? _toggleOptionText;

    [ObservableProperty]
    private bool _isToggleOptionVisible;

    [ObservableProperty]
    private bool _toggleOptionValue;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private bool _isProgressVisible;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private double _progressValue;

    public Func<FolderNode, Task>? ExpandNodeHandler { get; set; }

    public Func<FolderNode, Task>? SelectNodeHandler { get; set; }

    public Func<FolderNode, Task>? ActivateNodeHandler { get; set; }

    public Func<FolderNode, Task>? ActivateNodeSecondaryHandler { get; set; }

    public Func<FolderNode, IReadOnlyList<NavigationPaneNodeAction>>? NodeActionsProvider { get; set; }

    public Func<NavigationPaneSourceItem, Task>? RemoveSourceHandler { get; set; }

    public Func<Task>? PrimaryActionHandler { get; set; }

    public Func<bool, Task>? ToggleOptionHandler { get; set; }

    public bool HasSourceItems => SourceItems.Count > 0;

    public bool HasPrimaryAction => PrimaryAction != null;

    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    partial void OnSourceItemsChanged(ObservableCollection<NavigationPaneSourceItem> value)
    {
        value.CollectionChanged += SourceItems_CollectionChanged;
        OnPropertyChanged(nameof(HasSourceItems));
    }

    partial void OnSourceItemsChanging(ObservableCollection<NavigationPaneSourceItem> value)
    {
        value.CollectionChanged -= SourceItems_CollectionChanged;
    }

    partial void OnPrimaryActionChanged(NavigationPaneHeaderAction? value)
    {
        OnPropertyChanged(nameof(HasPrimaryAction));
    }

    partial void OnStatusTextChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatusText));
    }

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

    public Task RemoveSourceAsync(NavigationPaneSourceItem item)
    {
        return RemoveSourceHandler?.Invoke(item) ?? Task.CompletedTask;
    }

    public Task ExecutePrimaryActionAsync()
    {
        return PrimaryActionHandler?.Invoke() ?? Task.CompletedTask;
    }

    public Task ToggleOptionAsync(bool value)
    {
        ToggleOptionValue = value;
        return ToggleOptionHandler?.Invoke(value) ?? Task.CompletedTask;
    }

    private void SourceItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSourceItems));
    }
}
