using CommunityToolkit.Mvvm.ComponentModel;
using PhotoView.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Windows.Storage;
using Windows.Storage.Search;

namespace PhotoView.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp"
    };

    private const uint PageSize = 100;
    private CancellationTokenSource? _loadImagesCts;

    public event EventHandler? ImagesChanged;
    public event EventHandler? ThumbnailSizeChanged;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _folderTree;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _breadcrumbPath;

    [ObservableProperty]
    private FolderNode _selectedFolder;

    [ObservableProperty]
    private ObservableCollection<ImageFileInfo> _images;

    [ObservableProperty]
    private ThumbnailSize _thumbnailSize = ThumbnailSize.Medium;

    public double ThumbnailHeight => ThumbnailSize switch
    {
        ThumbnailSize.Small => 120,
        ThumbnailSize.Medium => 256,
        ThumbnailSize.Large => 512,
        _ => 256
    };

    partial void OnThumbnailSizeChanged(ThumbnailSize value)
    {
        OnPropertyChanged(nameof(ThumbnailHeight));
        foreach (var image in Images)
        {
            image.UpdateDisplaySize(value);
            image.ClearThumbnail();
        }
        ThumbnailSizeChanged?.Invoke(this, EventArgs.Empty);
    }

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
                Name = "这台电脑",
                HasSubFolders = true
            };

            var externalDeviceNode = new FolderNode(null, NodeType.ExternalDevice)
            {
                Name = "外接设备",
                HasSubFolders = true
            };

            FolderTree.Add(thisPCNode);
            FolderTree.Add(externalDeviceNode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadDrivesAsync error: {ex}");
        }
    }

    public async System.Threading.Tasks.Task LoadChildrenAsync(FolderNode node)
    {
        if (node.IsLoaded || node.IsLoading)
            return;

        node.IsLoading = true;
        node.Children.Clear();

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
                        var driveNode = new FolderNode(storageFolder, NodeType.Drive, node)
                        {
                            Name = $"{drive.Name} ({drive.VolumeLabel})",
                            IsRemovable = drive.DriveType == DriveType.Removable
                        };
                        driveNode.CheckHasSubFolders();

                        bool isRemovable = drive.DriveType == DriveType.Removable;

                        if ((node.NodeType == NodeType.ThisPC && !isRemovable) ||
                            (node.NodeType == NodeType.ExternalDevice && isRemovable))
                        {
                            node.Children.Add(driveNode);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"LoadChildrenAsync drive error: {ex}");
                    }
                }
            }
            else if (node.Folder != null)
            {
                var folders = await node.Folder.GetFoldersAsync();
                foreach (var folder in folders)
                {
                    var childNode = new FolderNode(folder, NodeType.Folder, node);
                    childNode.CheckHasSubFolders();
                    node.Children.Add(childNode);
                }
            }

            node.IsLoaded = true;
            node.HasSubFolders = node.Children.Count > 0;
            node.RefreshExpandableState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadChildrenAsync error: {ex}");
        }
        finally
        {
            node.IsLoading = false;
            node.RefreshExpandableState();
        }
    }

    public async System.Threading.Tasks.Task LoadImagesAsync(FolderNode folderNode)
    {
        _loadImagesCts?.Cancel();
        _loadImagesCts = new CancellationTokenSource();
        var cancellationToken = _loadImagesCts.Token;

        SelectedFolder = folderNode;
        UpdateBreadcrumbPath(folderNode);

        Images.Clear();

        System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] 清空完成");

        if (folderNode?.Folder == null)
            return;

        try
        {
            var result = folderNode.Folder.CreateFileQueryWithOptions(new QueryOptions());
            uint index = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await result.GetFilesAsync(index, PageSize);
                if (batch.Count == 0)
                    break;

                var imageInfos = await System.Threading.Tasks.Task.Run(async () =>
                {
                    var tasks = new List<Task<ImageFileInfo?>>();

                    foreach (var file in batch)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        if (IsImageFile(file))
                        {
                            tasks.Add(LoadImageInfoSafeAsync(file, cancellationToken));
                        }
                    }

                    var results = await Task.WhenAll(tasks);
                    return results.Where(r => r != null).Cast<ImageFileInfo>().ToList();
                }, cancellationToken);

                if (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var info in imageInfos)
                    {
                        info.UpdateDisplaySize(ThumbnailSize);
                        Images.Add(info);
                    }
                }

                index += PageSize;
            }

            System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] 加载完成, Images.Count={Images.Count}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadImagesAsync error: {ex}");
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
            current = current.Parent;
        }
        foreach (var node in path)
        {
            BreadcrumbPath.Add(node);
        }
    }

    private static bool IsImageFile(StorageFile file)
    {
        return ImageExtensions.Contains(file.FileType);
    }

    public static async System.Threading.Tasks.Task<ImageFileInfo> LoadImageInfo(StorageFile file)
    {
        var properties = await file.Properties.GetImagePropertiesAsync();
        return new ImageFileInfo(
            (int)properties.Width,
            (int)properties.Height,
            properties.Title,
            (int)properties.Rating,
            file,
            file.DisplayName,
            file.DisplayType);
    }

    private static async Task<ImageFileInfo?> LoadImageInfoSafeAsync(StorageFile file, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await file.Properties.GetImagePropertiesAsync().AsTask(cancellationToken);
            return new ImageFileInfo(
                (int)properties.Width,
                (int)properties.Height,
                properties.Title,
                (int)properties.Rating,
                file,
                file.DisplayName,
                file.DisplayType);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadImageInfo error: {ex}");
            return null;
        }
    }

    public void Dispose()
    {
        _loadImagesCts?.Cancel();

        foreach (var image in Images)
        {
            image.CancelThumbnailLoad();
        }
    }
}

