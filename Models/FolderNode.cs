using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using Windows.Storage;

namespace PhotoView.Models;

public enum NodeType
{
    ThisPC,
    ExternalDevice,
    Drive,
    Folder
}

public partial class FolderNode : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _children;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _allChildren;

    [ObservableProperty]
    private NodeType _nodeType;

    public StorageFolder? Folder { get; set; }

    public FolderNode? Parent { get; set; }

    public bool HasDummyChild { get; set; }

    public FolderNode(StorageFolder? folder = null, NodeType nodeType = NodeType.Folder, FolderNode? parent = null)
    {
        Name = folder?.DisplayName ?? "This PC";
        Folder = folder;
        NodeType = nodeType;
        Parent = parent;
        Children = new ObservableCollection<FolderNode>();
        AllChildren = new ObservableCollection<FolderNode>();
        HasDummyChild = true;
    }
}
