using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
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

    [ObservableProperty]
    private bool _hasSubFolders;

    public bool HasExpandableChildren => IsLoaded ? Children.Count > 0 : HasSubFolders;

    partial void OnHasSubFoldersChanged(bool value)
    {
        OnPropertyChanged(nameof(HasExpandableChildren));
    }

    public StorageFolder? Folder { get; set; }

    public FolderNode? Parent { get; set; }

    public string? FullPath { get; set; }

    public bool IsLoaded { get; set; }

    public bool IsRemovable { get; set; }

    public FolderNode(StorageFolder? folder = null, NodeType nodeType = NodeType.Folder, FolderNode? parent = null)
    {
        Name = folder?.DisplayName ?? "This PC";
        Folder = folder;
        NodeType = nodeType;
        Parent = parent;
        FullPath = folder?.Path;
        Children = new ObservableCollection<FolderNode>();
        AllChildren = new ObservableCollection<FolderNode>();
    }

    public void CheckHasSubFolders()
    {
        if (NodeType == NodeType.ThisPC || NodeType == NodeType.ExternalDevice)
        {
            HasSubFolders = true;
            return;
        }

        if (string.IsNullOrEmpty(FullPath))
        {
            HasSubFolders = false;
            return;
        }

        try
        {
            HasSubFolders = Directory.EnumerateDirectories(FullPath, "*", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            HasSubFolders = false;
        }
    }

    public void RefreshExpandableState()
    {
        OnPropertyChanged(nameof(HasExpandableChildren));
    }
}
