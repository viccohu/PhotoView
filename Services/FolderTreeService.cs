using PhotoView.Contracts.Services;
using PhotoView.Models;
using Windows.Storage;

namespace PhotoView.Services;

public sealed class FolderTreeService
{
    private const int RecentFolderDisplayCount = 5;
    private readonly ISettingsService _settingsService;
    private FolderAccessHistory _folderAccessHistory = new();
    private bool _isInitialized;

    public FolderTreeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        _folderAccessHistory = NormalizeFolderAccessHistory(await _settingsService.LoadFolderAccessHistoryAsync());
        _isInitialized = true;
    }

    public async Task<(FolderNode FavoritesRoot, FolderNode ThisPc, FolderNode ExternalDevices)> CreateRootNodesAsync()
    {
        await InitializeAsync();

        var favoritesRoot = new FolderNode(null, NodeType.FavoritesRoot)
        {
            Name = "常用文件夹",
            HasSubFolders = true,
            IsExpanded = true
        };

        var thisPc = new FolderNode(null, NodeType.ThisPC)
        {
            Name = "这台电脑",
            HasSubFolders = true
        };

        var externalDevices = new FolderNode(null, NodeType.ExternalDevice)
        {
            Name = "外接设备",
            HasSubFolders = true
        };

        await RefreshFavoriteFoldersAsync(favoritesRoot);
        return (favoritesRoot, thisPc, externalDevices);
    }

    public async Task LoadChildrenAsync(FolderNode node)
    {
        if (node.IsLoaded || node.IsLoading)
            return;

        node.IsLoading = true;
        node.Children.Clear();

        try
        {
            if (node.NodeType == NodeType.FavoritesRoot)
            {
                await RefreshFavoriteFoldersAsync(node);
            }
            else if (node.NodeType == NodeType.ThisPC)
            {
                await PopulateThisPcChildrenAsync(node);
            }
            else if (node.NodeType == NodeType.ExternalDevice)
            {
                await PopulateExternalDeviceChildrenAsync(node);
            }
            else if (node.Folder != null)
            {
                var folders = await node.Folder.GetFoldersAsync();
                foreach (var folder in folders.OrderBy(folder => folder.Name, StringComparer.CurrentCultureIgnoreCase))
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
            System.Diagnostics.Debug.WriteLine($"[FolderTreeService] LoadChildrenAsync failed for {node.Name}: {ex.Message}");
        }
        finally
        {
            node.IsLoading = false;
            node.RefreshExpandableState();
        }
    }

    public async Task RefreshExternalDevicesAsync(FolderNode externalDevicesRoot)
    {
        externalDevicesRoot.IsLoaded = false;
        externalDevicesRoot.Children.Clear();
        await LoadChildrenAsync(externalDevicesRoot);
    }

    public async Task RefreshFavoriteFoldersAsync(FolderNode? favoritesRoot)
    {
        if (favoritesRoot == null)
            return;

        await InitializeAsync();

        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var desiredItems = new List<(string Path, NodeType NodeType)>();
        foreach (var path in _folderAccessHistory.PinnedPaths)
        {
            var normalizedPath = NormalizePath(path);
            if (usedPaths.Add(normalizedPath))
            {
                desiredItems.Add((normalizedPath, NodeType.PinnedFolder));
            }
        }

        foreach (var path in _folderAccessHistory.RecentPaths
            .Where(path => !usedPaths.Contains(NormalizePath(path)))
            .Take(RecentFolderDisplayCount)
            .ToArray())
        {
            var normalizedPath = NormalizePath(path);
            if (usedPaths.Add(normalizedPath))
            {
                desiredItems.Add((normalizedPath, NodeType.RecentFolder));
            }
        }

        await ApplyFavoriteFolderChildrenAsync(favoritesRoot, desiredItems);

        favoritesRoot.IsLoaded = true;
        favoritesRoot.HasSubFolders = favoritesRoot.Children.Count > 0;
        favoritesRoot.RefreshExpandableState();
    }

    public bool IsFolderPinned(FolderNode? node)
    {
        return !string.IsNullOrWhiteSpace(node?.FullPath) &&
               _folderAccessHistory.PinnedPaths.Any(path => IsSamePath(path, node.FullPath));
    }

    public async Task PinFolderAsync(FolderNode? node, FolderNode? favoritesRoot)
    {
        if (string.IsNullOrWhiteSpace(node?.FullPath) || !Directory.Exists(node.FullPath))
            return;

        await InitializeAsync();

        var normalizedPath = NormalizePath(node.FullPath);
        if (_folderAccessHistory.PinnedPaths.Any(path => IsSamePath(path, normalizedPath)))
        {
            var removedRecent = _folderAccessHistory.RecentPaths.RemoveAll(path => IsSamePath(path, normalizedPath)) > 0;
            if (removedRecent)
            {
                await SaveHistoryAndRefreshFavoritesAsync(favoritesRoot);
            }
            else
            {
                await RefreshFavoriteFoldersAsync(favoritesRoot);
            }
            return;
        }

        _folderAccessHistory.PinnedPaths.Insert(0, normalizedPath);
        _folderAccessHistory.RecentPaths.RemoveAll(path => IsSamePath(path, normalizedPath));
        await SaveHistoryAndRefreshFavoritesAsync(favoritesRoot);
    }

    public async Task UnpinFolderAsync(FolderNode? node, FolderNode? favoritesRoot)
    {
        if (string.IsNullOrWhiteSpace(node?.FullPath))
            return;

        await InitializeAsync();

        var normalizedPath = NormalizePath(node.FullPath);
        var removed = _folderAccessHistory.PinnedPaths.RemoveAll(path => IsSamePath(path, normalizedPath)) > 0;
        if (!removed)
            return;

        if (!_folderAccessHistory.RecentPaths.Any(path => IsSamePath(path, normalizedPath)))
        {
            _folderAccessHistory.RecentPaths.Add(normalizedPath);
            TrimRecentPaths();
        }

        await SaveHistoryAndRefreshFavoritesAsync(favoritesRoot);
    }

    public async Task RecordFolderVisitAsync(FolderNode folderNode, FolderNode? favoritesRoot)
    {
        if (string.IsNullOrWhiteSpace(folderNode.FullPath) || !Directory.Exists(folderNode.FullPath))
            return;

        await InitializeAsync();

        var exactPath = NormalizePath(folderNode.FullPath);
        if (_folderAccessHistory.PinnedPaths.Any(path => IsSamePath(path, exactPath)) ||
            _folderAccessHistory.RecentPaths.Any(path => IsSamePath(path, exactPath)))
        {
            return;
        }

        _folderAccessHistory.RecentPaths.Add(exactPath);
        TrimRecentPaths();
        await SaveHistoryAndRefreshFavoritesAsync(favoritesRoot);
    }

    private async Task SaveHistoryAndRefreshFavoritesAsync(FolderNode? favoritesRoot)
    {
        _folderAccessHistory = NormalizeFolderAccessHistory(_folderAccessHistory);
        await _settingsService.SaveFolderAccessHistoryAsync(_folderAccessHistory);
        await RefreshFavoriteFoldersAsync(favoritesRoot);
    }

    private void TrimRecentPaths()
    {
        while (_folderAccessHistory.RecentPaths.Count > RecentFolderDisplayCount)
        {
            _folderAccessHistory.RecentPaths.RemoveAt(0);
        }
    }

    private async Task PopulateThisPcChildrenAsync(FolderNode node)
    {
        await AddKnownFolderNodeAsync(node, "桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        await AddKnownFolderNodeAsync(node, "图片", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        await AddKnownFolderNodeAsync(node, "下载", Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads"));

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType == DriveType.Removable)
                continue;

            var driveNode = await CreateDriveNodeAsync(drive, node);
            if (driveNode != null)
            {
                node.Children.Add(driveNode);
            }
        }
    }

    private async Task PopulateExternalDeviceChildrenAsync(FolderNode node)
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Removable)
                continue;

            var driveNode = await CreateDriveNodeAsync(drive, node);
            if (driveNode != null)
            {
                node.Children.Add(driveNode);
            }
        }
    }

    private async Task AddKnownFolderNodeAsync(FolderNode parent, string displayName, string path)
    {
        var node = await CreateFolderNodeFromPathAsync(path, NodeType.KnownFolder, parent, displayName);
        if (node != null && !parent.Children.Any(child => IsSamePath(child.FullPath, node.FullPath)))
        {
            parent.Children.Add(node);
        }
    }

    private async Task<FolderNode?> CreateDriveNodeAsync(DriveInfo drive, FolderNode parent)
    {
        try
        {
            var storageFolder = await StorageFolder.GetFolderFromPathAsync(drive.Name);
            var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name.TrimEnd('\\')
                : drive.VolumeLabel;
            var driveNode = new FolderNode(storageFolder, NodeType.Drive, parent)
            {
                Name = $"{drive.Name} ({label})",
                IsRemovable = drive.DriveType == DriveType.Removable
            };
            driveNode.CheckHasSubFolders();
            return driveNode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderTreeService] CreateDriveNodeAsync failed for {drive.Name}: {ex.Message}");
            return null;
        }
    }

    private async Task<FolderNode?> CreateFolderNodeFromPathAsync(
        string? path,
        NodeType nodeType,
        FolderNode? parent,
        string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return null;

        try
        {
            var normalizedPath = NormalizePath(path);
            var storageFolder = await StorageFolder.GetFolderFromPathAsync(normalizedPath);
            var node = new FolderNode(storageFolder, nodeType, parent)
            {
                Name = displayName ?? GetDisplayNameForPath(normalizedPath)
            };
            node.CheckHasSubFolders();
            return node;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderTreeService] CreateFolderNodeFromPathAsync failed for {path}: {ex.Message}");
            return null;
        }
    }

    private static async Task ApplyFavoriteFolderChildrenAsync(
        FolderNode favoritesRoot,
        IReadOnlyList<(string Path, NodeType NodeType)> desiredItems)
    {
        var desiredNodes = new List<FolderNode>();
        foreach (var desiredItem in desiredItems)
        {
            if (!Directory.Exists(desiredItem.Path))
                continue;

            var existingNode = favoritesRoot.Children
                .FirstOrDefault(child => IsSamePath(child.FullPath, desiredItem.Path));

            if (existingNode == null)
            {
                var storageFolder = await StorageFolder.GetFolderFromPathAsync(desiredItem.Path);
                existingNode = new FolderNode(storageFolder, desiredItem.NodeType, favoritesRoot);
                existingNode.CheckHasSubFolders();
            }

            existingNode.Parent = favoritesRoot;
            existingNode.NodeType = desiredItem.NodeType;
            existingNode.Name = GetDisplayNameForPath(desiredItem.Path);
            desiredNodes.Add(existingNode);
        }

        if (favoritesRoot.Children.Count == desiredNodes.Count &&
            favoritesRoot.Children.SequenceEqual(desiredNodes))
        {
            return;
        }

        favoritesRoot.Children.Clear();
        foreach (var node in desiredNodes)
        {
            favoritesRoot.Children.Add(node);
        }
    }

    private static FolderAccessHistory NormalizeFolderAccessHistory(FolderAccessHistory? history)
    {
        history ??= new FolderAccessHistory();
        var pinnedPaths = (history.PinnedPaths ?? new List<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FolderAccessHistory
        {
            PinnedPaths = pinnedPaths,
            RecentPaths = (history.RecentPaths ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !pinnedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                .Take(RecentFolderDisplayCount)
                .ToList()
        };
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsSamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDriveRootPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        var root = Path.GetPathRoot(normalizedPath);
        return !string.IsNullOrEmpty(root) &&
               string.Equals(
                   normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDisplayNameForPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (IsDriveRootPath(normalizedPath))
            return normalizedPath;

        var name = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(name) ? normalizedPath : name;
    }
}
