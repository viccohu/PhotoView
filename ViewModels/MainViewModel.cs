using CommunityToolkit.Mvvm.ComponentModel;
using PhotoView.Models;
using System.Collections.ObjectModel;
using System.IO;
using Windows.Storage;
using Windows.Storage.Search;

namespace PhotoView.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    [ObservableProperty]
    private ObservableCollection<FolderNode> _folderTree;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _breadcrumbPath;

    [ObservableProperty]
    private FolderNode _selectedFolder;

    [ObservableProperty]
    private ObservableCollection<ImageFileInfo> _images;

    public MainViewModel()
    {
        _folderTree = new ObservableCollection<FolderNode>();
        _breadcrumbPath = new ObservableCollection<FolderNode>();
        _images = new ObservableCollection<ImageFileInfo>();
        _ = LoadDrivesAsync();
    }

    private async System.Threading.Tasks.Task LoadDrivesAsync()
    {
        try
        {
            var thisPCNode = new FolderNode(null, NodeType.ThisPC)
            {
                Name = "这台电脑"
            };
            thisPCNode.Children.Add(new FolderNode(null, NodeType.Folder) { Name = "" });

            var externalDeviceNode = new FolderNode(null, NodeType.ExternalDevice)
            {
                Name = "外接设备"
            };
            externalDeviceNode.Children.Add(new FolderNode(null, NodeType.Folder) { Name = "" });

            FolderTree.Add(thisPCNode);
            FolderTree.Add(externalDeviceNode);
        }
        catch (Exception)
        {
        }
    }

    public async System.Threading.Tasks.Task LoadChildrenAsync(FolderNode node)
    {
        if (!node.HasDummyChild || node.IsLoading)
            return;

        node.IsLoading = true;
        node.Children.Clear();
        node.HasDummyChild = false;

        try
        {
            if (node.NodeType == NodeType.ThisPC || node.NodeType == NodeType.ExternalDevice)
            {
                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (!drive.IsReady)
                        continue;

                    try
                    {
                        var storageFolder = await StorageFolder.GetFolderFromPathAsync(drive.Name);
                        var driveNode = new FolderNode(storageFolder, NodeType.Drive)
                        {
                            Name = $"{drive.Name} ({drive.VolumeLabel})"
                        };
                        driveNode.Children.Add(new FolderNode(null, NodeType.Folder) { Name = "" });
                        driveNode.HasDummyChild = true;

                        bool isRemovable = drive.DriveType == DriveType.Removable;
                        
                        if ((node.NodeType == NodeType.ThisPC && !isRemovable) ||
                            (node.NodeType == NodeType.ExternalDevice && isRemovable))
                        {
                            node.Children.Add(driveNode);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            else if (node.Folder != null)
            {
                var folders = await node.Folder.GetFoldersAsync();
                foreach (var folder in folders)
                {
                    var childNode = new FolderNode(folder, NodeType.Folder);
                    childNode.Children.Add(new FolderNode(null, NodeType.Folder) { Name = "" });
                    childNode.HasDummyChild = true;
                    node.Children.Add(childNode);
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    public async System.Threading.Tasks.Task LoadImagesAsync(FolderNode folderNode)
    {
        Images.Clear();
        SelectedFolder = folderNode;

        UpdateBreadcrumbPath(folderNode);

        if (folderNode?.Folder == null)
            return;

        try
        {
            var result = folderNode.Folder.CreateFileQueryWithOptions(new QueryOptions());
            var imageFiles = await result.GetFilesAsync();
            foreach (var file in imageFiles)
            {
                if (IsImageFile(file))
                {
                    Images.Add(await LoadImageInfo(file));
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void UpdateBreadcrumbPath(FolderNode folderNode)
    {
        BreadcrumbPath.Clear();
        if (folderNode == null)
            return;

        var path = new List<FolderNode>();
        var current = folderNode;
        while (current != null)
        {
            path.Insert(0, current);
            current = FindParentNode(FolderTree, current);
        }
        foreach (var node in path)
        {
            BreadcrumbPath.Add(node);
        }
    }

    private FolderNode FindParentNode(ObservableCollection<FolderNode> nodes, FolderNode target)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Contains(target))
                return node;
            var parent = FindParentNode(node.Children, target);
            if (parent != null)
                return parent;
        }
        return null;
    }

    private bool IsImageFile(StorageFile file)
    {
        var extensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };
        return extensions.Contains(file.FileType.ToLower());
    }

    public static async System.Threading.Tasks.Task<ImageFileInfo> LoadImageInfo(StorageFile file)
    {
        var properties = await file.Properties.GetImagePropertiesAsync();
        ImageFileInfo info = new(properties, file, file.DisplayName, file.DisplayType);
        return info;
    }
}
