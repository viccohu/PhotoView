using CommunityToolkit.Mvvm.ComponentModel;
using PhotoView.Models;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Windows.Storage;
using Windows.Storage.Search;

namespace PhotoView.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 常见图片格式
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp",
        
        // RAW 格式 - Canon
        ".cr2", ".cr3", ".crw",
        
        // RAW 格式 - Nikon
        ".nef", ".nrw",
        
        // RAW 格式 - Sony
        ".arw", ".sr2",
        
        // RAW 格式 - Fujifilm
        ".raf",
        
        // RAW 格式 - Olympus
        ".orf",
        
        // RAW 格式 - Panasonic/Leica
        ".rw2",
        
        // RAW 格式 - Pentax
        ".pef",
        
        // RAW 格式 - Adobe (通用)
        ".dng",
        
        // RAW 格式 - Samsung
        ".srw",
        
        // RAW 格式 - 其他品牌
        ".raw",      // 通用 RAW
        ".iiq",      // Phase One
        ".3fr",      // Hasselblad
        ".mef",      // Mamiya
        ".mos",      // Leaf
        ".x3f",      // Sigma
        ".erf",      // Epson
        ".dcr",      // Kodak
        ".kdc"       // Kodak
    };

    private const uint PageSize = 100;
    private CancellationTokenSource? _loadImagesCts;
    private readonly Stack<FolderNode> _navigationHistory = new();

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

    public bool CanGoBack => _navigationHistory.Count > 1;
    
    public bool CanGoUp => SelectedFolder?.Parent != null;

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

        if (folderNode != null && folderNode != SelectedFolder)
        {
            _navigationHistory.Push(folderNode);
            OnPropertyChanged(nameof(CanGoBack));
        }

        SelectedFolder = folderNode;
        UpdateBreadcrumbPath(folderNode);
        OnPropertyChanged(nameof(CanGoUp));

        Images.Clear();
        ImagesChanged?.Invoke(this, EventArgs.Empty);

        if (folderNode?.Folder == null)
            return;

        try
        {
            var fileTypeFilter = ImageExtensions.ToList();
            var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilter)
            {
                IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                FolderDepth = FolderDepth.Shallow
            };

            var result = folderNode.Folder.CreateFileQueryWithOptions(queryOptions);
            
            var fileNameMap = new Dictionary<string, List<StorageFile>>(StringComparer.OrdinalIgnoreCase);
            var processedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            const uint batchSize = 30;
            uint index = 0;
            
            System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] 开始分批加载, 批次大小={batchSize}");
            
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await result.GetFilesAsync(index, batchSize);
                if (batch.Count == 0)
                    break;
                
                var newGroupFiles = new List<StorageFile>();
                
                foreach (var file in batch)
                {
                    var groupName = ImageGroup.GetGroupName(file.Name);
                    if (!fileNameMap.TryGetValue(groupName, out var list))
                    {
                        list = new List<StorageFile>();
                        fileNameMap[groupName] = list;
                    }
                    list.Add(file);
                    
                    if (!processedGroups.Contains(groupName))
                    {
                        newGroupFiles.Add(file);
                    }
                }
                
                var newPrimaryFiles = new List<StorageFile>();
                var newGroupMap = new Dictionary<string, List<StorageFile>>();
                
                foreach (var file in newGroupFiles)
                {
                    var groupName = ImageGroup.GetGroupName(file.Name);
                    if (!processedGroups.Contains(groupName) && fileNameMap.TryGetValue(groupName, out var groupFiles))
                    {
                        var sortedFiles = groupFiles.OrderBy(f => ImageGroup.GetFormatPriority(f.FileType)).ToList();
                        var primaryFile = sortedFiles.First();
                        
                        if (!newPrimaryFiles.Contains(primaryFile))
                        {
                            newPrimaryFiles.Add(primaryFile);
                            newGroupMap[groupName] = sortedFiles;
                            processedGroups.Add(groupName);
                        }
                    }
                }
                
                if (newPrimaryFiles.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] 批次 {index/batchSize + 1}, 加载新主图片 {newPrimaryFiles.Count} 个");
                    
                    var newPrimaryInfos = await System.Threading.Tasks.Task.Run(async () =>
                    {
                        var tasks = newPrimaryFiles.Select(f => LoadImageInfoSafeAsync(f, cancellationToken));
                        var results = await Task.WhenAll(tasks);
                        return results.Where(r => r != null).Cast<ImageFileInfo>().ToList();
                    }, cancellationToken);
                    
                    var newFinalGroups = new List<ImageGroup>();
                    
                    foreach (var primaryInfo in newPrimaryInfos)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;
                        
                        var groupName = ImageGroup.GetGroupName(primaryInfo.ImageFile.Name);
                        if (newGroupMap.TryGetValue(groupName, out var allFilesInGroup))
                        {
                            var imageInfos = new List<ImageFileInfo> { primaryInfo };
                            
                            foreach (var file in allFilesInGroup.Where(f => f != primaryInfo.ImageFile))
                            {
                                var dummyInfo = new ImageFileInfo(0, 0, string.Empty, 0, file, file.DisplayName, file.DisplayType);
                                imageInfos.Add(dummyInfo);
                            }
                            
                            var finalGroup = new ImageGroup(groupName, imageInfos);
                            newFinalGroups.Add(finalGroup);
                        }
                    }
                    
                    foreach (var group in newFinalGroups)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            group.PrimaryImage.UpdateDisplaySize(ThumbnailSize);
                            Images.Add(group.PrimaryImage);
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] 批次追加完成, 当前 Images.Count={Images.Count}");
                    ImagesChanged?.Invoke(this, EventArgs.Empty);
                }
                
                index += (uint)batch.Count;
            }
            
            System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] 全部加载完成, Images.Count={Images.Count}, 组数={processedGroups.Count}");
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
            System.Diagnostics.Debug.WriteLine($"[LoadImageInfo] 失败: 文件={file.Name}, 错误={ex.Message}");
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

    public async System.Threading.Tasks.Task GoBackAsync()
    {
        if (_navigationHistory.Count <= 1)
            return;

        _navigationHistory.Pop();
        var previousFolder = _navigationHistory.Peek();
        
        SelectedFolder = null;
        OnPropertyChanged(nameof(CanGoBack));
        
        await LoadImagesWithoutHistoryAsync(previousFolder);
    }

    public async System.Threading.Tasks.Task GoUpAsync()
    {
        if (SelectedFolder?.Parent == null)
            return;

        var parentFolder = SelectedFolder.Parent;
        await LoadImagesAsync(parentFolder);
    }

    public async System.Threading.Tasks.Task RefreshAsync()
    {
        if (SelectedFolder == null)
            return;

        var currentFolder = SelectedFolder;
        
        SelectedFolder = null;
        Images.Clear();
        ImagesChanged?.Invoke(this, EventArgs.Empty);
        
        await LoadImagesWithoutHistoryAsync(currentFolder);
    }

    private async System.Threading.Tasks.Task LoadImagesWithoutHistoryAsync(FolderNode folderNode)
    {
        _loadImagesCts?.Cancel();
        _loadImagesCts = new CancellationTokenSource();
        var cancellationToken = _loadImagesCts.Token;

        SelectedFolder = folderNode;
        UpdateBreadcrumbPath(folderNode);
        OnPropertyChanged(nameof(CanGoUp));

        Images.Clear();
        ImagesChanged?.Invoke(this, EventArgs.Empty);

        if (folderNode?.Folder == null)
            return;

        try
        {
            var fileTypeFilter = ImageExtensions.ToList();
            var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilter)
            {
                IndexerOption = IndexerOption.UseIndexerWhenAvailable,
                FolderDepth = FolderDepth.Shallow
            };

            var result = folderNode.Folder.CreateFileQueryWithOptions(queryOptions);
            
            var fileNameMap = new Dictionary<string, List<StorageFile>>(StringComparer.OrdinalIgnoreCase);
            var processedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            const uint batchSize = 30;
            uint index = 0;
            
            System.Diagnostics.Debug.WriteLine($"[LoadImagesWithoutHistoryAsync] 开始分批加载, 批次大小={batchSize}");
            
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await result.GetFilesAsync(index, batchSize);
                if (batch.Count == 0)
                    break;
                
                var newGroupFiles = new List<StorageFile>();
                
                foreach (var file in batch)
                {
                    var groupName = ImageGroup.GetGroupName(file.Name);
                    if (!fileNameMap.TryGetValue(groupName, out var list))
                    {
                        list = new List<StorageFile>();
                        fileNameMap[groupName] = list;
                    }
                    list.Add(file);
                    
                    if (!processedGroups.Contains(groupName))
                    {
                        newGroupFiles.Add(file);
                    }
                }
                
                var newPrimaryFiles = new List<StorageFile>();
                var newGroupMap = new Dictionary<string, List<StorageFile>>();
                
                foreach (var file in newGroupFiles)
                {
                    var groupName = ImageGroup.GetGroupName(file.Name);
                    if (!processedGroups.Contains(groupName) && fileNameMap.TryGetValue(groupName, out var groupFiles))
                    {
                        var sortedFiles = groupFiles.OrderBy(f => ImageGroup.GetFormatPriority(f.FileType)).ToList();
                        var primaryFile = sortedFiles.First();
                        
                        if (!newPrimaryFiles.Contains(primaryFile))
                        {
                            newPrimaryFiles.Add(primaryFile);
                            newGroupMap[groupName] = sortedFiles;
                            processedGroups.Add(groupName);
                        }
                    }
                }
                
                if (newPrimaryFiles.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[LoadImagesWithoutHistoryAsync] 批次 {index/batchSize + 1}, 加载新主图片 {newPrimaryFiles.Count} 个");
                    
                    var newPrimaryInfos = await System.Threading.Tasks.Task.Run(async () =>
                    {
                        var tasks = newPrimaryFiles.Select(f => LoadImageInfoSafeAsync(f, cancellationToken));
                        var results = await Task.WhenAll(tasks);
                        return results.Where(r => r != null).Cast<ImageFileInfo>().ToList();
                    }, cancellationToken);
                    
                    var newFinalGroups = new List<ImageGroup>();
                    
                    foreach (var primaryInfo in newPrimaryInfos)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;
                        
                        var groupName = ImageGroup.GetGroupName(primaryInfo.ImageFile.Name);
                        if (newGroupMap.TryGetValue(groupName, out var allFilesInGroup))
                        {
                            var imageInfos = new List<ImageFileInfo> { primaryInfo };
                            
                            foreach (var file in allFilesInGroup.Where(f => f != primaryInfo.ImageFile))
                            {
                                var dummyInfo = new ImageFileInfo(0, 0, string.Empty, 0, file, file.DisplayName, file.DisplayType);
                                imageInfos.Add(dummyInfo);
                            }
                            
                            var finalGroup = new ImageGroup(groupName, imageInfos);
                            newFinalGroups.Add(finalGroup);
                        }
                    }
                    
                    foreach (var group in newFinalGroups)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            group.PrimaryImage.UpdateDisplaySize(ThumbnailSize);
                            Images.Add(group.PrimaryImage);
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[LoadImagesWithoutHistoryAsync] 批次追加完成, 当前 Images.Count={Images.Count}");
                    ImagesChanged?.Invoke(this, EventArgs.Empty);
                }
                
                index += (uint)batch.Count;
            }
            
            System.Diagnostics.Debug.WriteLine($"[LoadImagesWithoutHistoryAsync] 全部加载完成, Images.Count={Images.Count}, 组数={processedGroups.Count}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadImagesWithoutHistoryAsync error: {ex}");
        }
    }
}

