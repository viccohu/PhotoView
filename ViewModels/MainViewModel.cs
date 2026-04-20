using CommunityToolkit.Mvvm.ComponentModel;
using PhotoView.Contracts.Services;
using PhotoView.Helpers;
using PhotoView.Models;
using PhotoView.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using Windows.Storage;
using Windows.Storage.Search;

namespace PhotoView.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    private readonly ISettingsService _settingsService;
    private readonly RatingService _ratingService;
    private readonly FolderTreeService _folderTreeService;
    private readonly IExternalDeviceWatcherService _externalDeviceWatcherService;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // 常见图片格式
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".psd", ".psb",
        
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

    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".sr2", ".raf",
        ".orf", ".rw2", ".pef", ".dng", ".srw", ".raw", ".iiq", ".3fr",
        ".mef", ".mos", ".x3f", ".erf", ".dcr", ".kdc"
    };

    private const uint PageSize = 100;
    private const uint InitialFileEnumerationBatchSize = 30;
    private const int DeferredImageInfoLoadDelayMs = 400;
    private const int RatingPreloadCommitBatchSize = 75;
    private const double BurstGroupingWindowSeconds = 2d;
    private CancellationTokenSource? _loadImagesCts;
    private CancellationTokenSource? _ratingPreloadCts;
    private readonly Stack<FolderNode> _navigationHistory = new();
    private int _pendingDeleteCount;
    private readonly List<ImageFileInfo> _allImages = new();
    private readonly List<BurstPhotoGroup> _burstGroups = new();
    private readonly SemaphoreSlim _metadataHydrationGate = new(2);
    private readonly object _ratingPreloadLock = new();
    private readonly Queue<RatingPreloadRequest> _pendingRatingPreloadItems = new();
    private readonly HashSet<string> _queuedRatingPreloadPaths = new(StringComparer.OrdinalIgnoreCase);
    private FolderNode? _favoritesRootNode;
    private FolderNode? _thisPcNode;
    private FolderNode? _externalDeviceNode;
    private bool _isRatingPreloadRunning;
    private int _ratingPreloadVersion;
    public FilterViewModel Filter { get; }

    public event EventHandler? ImagesChanged;
    public event EventHandler? ThumbnailSizeChanged;
    public event EventHandler? FolderTreeLoaded;
    public event EventHandler<FolderNode?>? SelectedFolderChanged;

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

    public ObservableCollection<FolderNode> CurrentSubFolders { get; }

    public int PendingDeleteCount
    {
        get => _pendingDeleteCount;
        private set => SetProperty(ref _pendingDeleteCount, value);
    }

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
        foreach (var image in _allImages)
        {
            image.UpdateDisplaySize(value);
            image.ClearThumbnail();
        }
        ThumbnailSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedFolderChanged(FolderNode? value)
    {
        SelectedFolderChanged?.Invoke(this, value);
    }

    public MainViewModel(
        ISettingsService settingsService,
        RatingService ratingService,
        FolderTreeService folderTreeService,
        IExternalDeviceWatcherService externalDeviceWatcherService)
    {
        _settingsService = settingsService;
        _ratingService = ratingService;
        _folderTreeService = folderTreeService;
        _externalDeviceWatcherService = externalDeviceWatcherService;
        _settingsService.PreferPsdAsPrimaryPreviewChanged += OnPreferPsdAsPrimaryPreviewChanged;
        _settingsService.CollapseBurstGroupsChanged += OnCollapseBurstGroupsChanged;
        _externalDeviceWatcherService.ExternalDevicesChanged += OnExternalDevicesChanged;
        _thumbnailSize = _settingsService.ThumbnailSize;
        _folderTree = new ObservableCollection<FolderNode>();
        _breadcrumbPath = new ObservableCollection<FolderNode>();
        CurrentSubFolders = new ObservableCollection<FolderNode>();
        _images = new ObservableCollection<ImageFileInfo>();
        Filter = new FilterViewModel();
        Filter.FilterChanged += OnFilterChanged;
        _ = _ratingService.InitializeAsync();
        _ = LoadDrivesAsync();
    }

    public int SubFolderCount => CurrentSubFolders.Count;

    public bool HasSubFoldersInCurrentFolder => CurrentSubFolders.Count > 0;

    private async System.Threading.Tasks.Task LoadDrivesAsync()
    {
        try
        {
            var roots = await _folderTreeService.CreateRootNodesAsync();
            _favoritesRootNode = roots.FavoritesRoot;
            _thisPcNode = roots.ThisPc;
            _externalDeviceNode = roots.ExternalDevices;

            FolderTree.Clear();
            FolderTree.Add(_favoritesRootNode);
            FolderTree.Add(_thisPcNode);
            FolderTree.Add(_externalDeviceNode);

            FolderTreeLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
        }
    }

    public async System.Threading.Tasks.Task LoadChildrenAsync(FolderNode node)
    {
        await _folderTreeService.LoadChildrenAsync(node);
    }

    public async Task RefreshFavoriteFoldersAsync()
    {
        await _folderTreeService.RefreshFavoriteFoldersAsync(_favoritesRootNode);
    }

    public bool IsFolderPinned(FolderNode? node)
    {
        return _folderTreeService.IsFolderPinned(node);
    }

    public async Task PinFolderAsync(FolderNode? node)
    {
        await _folderTreeService.PinFolderAsync(node, _favoritesRootNode);
        await SyncCurrentSubFoldersAsync(SelectedFolder, CancellationToken.None);
    }

    public async Task UnpinFolderAsync(FolderNode? node)
    {
        await _folderTreeService.UnpinFolderAsync(node, _favoritesRootNode);
        await SyncCurrentSubFoldersAsync(SelectedFolder, CancellationToken.None);
    }

    public async Task RefreshExternalDevicesAsync()
    {
        if (_externalDeviceNode == null)
            return;

        var selectedPath = SelectedFolder?.FullPath;
        var selectedWasExternal = IsPathUnderAnyChild(selectedPath, _externalDeviceNode);

        await _folderTreeService.RefreshExternalDevicesAsync(_externalDeviceNode);

        if (selectedWasExternal &&
            !string.IsNullOrWhiteSpace(selectedPath) &&
            !IsPathUnderAnyChild(selectedPath, _externalDeviceNode))
        {
            SelectedFolder = _externalDeviceNode;
            BreadcrumbPath.Clear();
            BreadcrumbPath.Add(_externalDeviceNode);
            CancelCurrentImageWork();
            _allImages.Clear();
            Images.Clear();
            ImagesChanged?.Invoke(this, EventArgs.Empty);
        }

        await SyncCurrentSubFoldersAsync(SelectedFolder ?? _externalDeviceNode, CancellationToken.None);
    }

    private void OnExternalDevicesChanged(object? sender, EventArgs e)
    {
        var dispatcher = App.MainWindow.DispatcherQueue;
        if (dispatcher == null)
            return;

        dispatcher.TryEnqueue(async () =>
        {
            if (AppLifetime.IsShuttingDown || _externalDeviceNode == null)
                return;

            if (!_externalDeviceNode.IsLoaded && !ReferenceEquals(SelectedFolder, _externalDeviceNode))
            {
                _externalDeviceNode.HasSubFolders = true;
                _externalDeviceNode.RefreshExpandableState();
                return;
            }

            await RefreshExternalDevicesAsync();
        });
    }

    private static bool IsPathUnderAnyChild(string? path, FolderNode parent)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return parent.Children.Any(child =>
            !string.IsNullOrWhiteSpace(child.FullPath) &&
            NormalizePath(path).StartsWith(NormalizePath(child.FullPath), StringComparison.OrdinalIgnoreCase));
    }

    private bool IsNodeUnderFavoritesRoot(FolderNode? node)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, _favoritesRootNode))
                return true;

            node = node.Parent;
        }

        return false;
    }

    public async System.Threading.Tasks.Task LoadImagesAsync(FolderNode folderNode)
    {
        _loadImagesCts?.Cancel();
        _loadImagesCts = new CancellationTokenSource();
        var cancellationToken = _loadImagesCts.Token;
        ResetRatingPreload(cancellationToken);

        if (folderNode != null && folderNode != SelectedFolder)
        {
            _navigationHistory.Push(folderNode);
            OnPropertyChanged(nameof(CanGoBack));
        }

        SelectedFolder = folderNode;
        UpdateBreadcrumbPath(folderNode);
        OnPropertyChanged(nameof(CanGoUp));

        // 加载目录前确认当前设置的缩略图尺寸
        ThumbnailSize = _settingsService.ThumbnailSize;

        CancelCurrentImageWork();
        ClearBurstGroups();
        _allImages.Clear();
        Images.Clear();
        ImagesChanged?.Invoke(this, EventArgs.Empty);
        await Task.Yield();

        await SyncCurrentSubFoldersAsync(folderNode, cancellationToken);

        if (folderNode?.Folder == null)
            return;

        await RecordFolderVisitAsync(folderNode);

        try
        {
            var fileTypeFilter = ImageExtensions.ToList();
            var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilter)
            {
                IndexerOption = IndexerOption.DoNotUseIndexer,
                FolderDepth = FolderDepth.Shallow
            };

            var result = folderNode.Folder.CreateFileQueryWithOptions(queryOptions);
            
            var fileNameMap = new Dictionary<string, List<StorageFile>>(StringComparer.OrdinalIgnoreCase);
            var processedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            var batchSize = (uint)Math.Max(1, _settingsService.BatchSize);
            uint index = 0;
            var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                var requestBatchSize = index == 0
                    ? Math.Min(batchSize, InitialFileEnumerationBatchSize)
                    : batchSize;
                var batch = await result.GetFilesAsync(index, requestBatchSize);
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
                        var sortedFiles = groupFiles
                            .OrderBy(f => ImageGroup.GetFormatPriority(f.FileType, _settingsService.PreferPsdAsPrimaryPreview))
                            .ToList();
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
                    // System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] 批次 {index/batchSize + 1}, 加载新主图片 {newPrimaryFiles.Count} 个");
                    
                    AddPlaceholderGroups(newGroupMap, cancellationToken);
                    if (index == 0)
                    {
                        // System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] First placeholders added in {loadStopwatch.ElapsedMilliseconds}ms, files={batch.Count}, primary={newPrimaryFiles.Count}");
                    }
                }
                
                index += (uint)batch.Count;
            }
            


            _allImages.Clear();
            _allImages.AddRange(Images);
            await HydrateDateTakenForBurstCandidatesAsync(cancellationToken);
            RebuildBurstGroups();
            ApplyFilter();
            QueueRatingPreloadForCurrentImages(cancellationToken);

            if (_settingsService.RememberLastFolder && !string.IsNullOrEmpty(folderNode.FullPath))
            {
                await _settingsService.SaveLastFolderPathAsync(folderNode.FullPath);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
        }
    }

    public FolderNode? FindNodeByPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var rootNode in FolderTree)
        {
            var found = FindNodeByPathRecursive(rootNode, path);
            if (found != null)
                return found;
        }
        return null;
    }

    private FolderNode? FindNodeByPathRecursive(FolderNode node, string targetPath)
    {
        if (!string.IsNullOrEmpty(node.FullPath) && 
            string.Equals(node.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindNodeByPathRecursive(child, targetPath);
            if (found != null)
                return found;
        }
        return null;
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

    private async Task SyncCurrentSubFoldersAsync(FolderNode? folderNode, CancellationToken cancellationToken)
    {
        if (folderNode == null)
        {
            CurrentSubFolders.Clear();
            OnPropertyChanged(nameof(SubFolderCount));
            OnPropertyChanged(nameof(HasSubFoldersInCurrentFolder));
            return;
        }

        if (!folderNode.IsLoaded)
        {
            await LoadChildrenAsync(folderNode);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var subFolders = folderNode.Children
            .Where(child => child.Folder != null)
            .ToList();

        CurrentSubFolders.Clear();
        foreach (var child in subFolders)
        {
            CurrentSubFolders.Add(child);
            _ = child.EnsureListIconAsync(cancellationToken);
        }

        OnPropertyChanged(nameof(SubFolderCount));
        OnPropertyChanged(nameof(HasSubFoldersInCurrentFolder));
    }

    private static bool IsImageFile(StorageFile file)
    {
        return ImageFormatRegistry.IsSupported(file.FileType);
    }

    private async Task RecordFolderVisitAsync(FolderNode folderNode)
    {
        await _folderTreeService.RecordFolderVisitAsync(
            folderNode,
            IsNodeUnderFavoritesRoot(folderNode) ? null : _favoritesRootNode);
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

    private void AddPlaceholderGroups(
        IReadOnlyDictionary<string, List<StorageFile>> newGroupMap,
        CancellationToken cancellationToken)
    {
        var addedAny = false;

        foreach (var (groupName, allFilesInGroup) in newGroupMap)
        {
            if (cancellationToken.IsCancellationRequested || allFilesInGroup.Count == 0)
            {
                break;
            }

            var imageInfos = allFilesInGroup
                .Select(file => CreatePlaceholderImageInfo(file))
                .ToList();

            var group = new ImageGroup(groupName, imageInfos, _settingsService.PreferPsdAsPrimaryPreview);
            group.PrimaryImage.UpdateDisplaySize(ThumbnailSize);

            Images.Add(group.PrimaryImage);
            _allImages.Add(group.PrimaryImage);
            StartDeferredImageInfoLoad(group.PrimaryImage, cancellationToken);
            addedAny = true;
        }

        if (addedAny)
        {
            ImagesChanged?.Invoke(this, EventArgs.Empty);
            QueueRatingPreloadForCurrentImages(cancellationToken);
        }
    }

    private readonly record struct RatingPreloadResult(
        ImageFileInfo ImageInfo,
        uint Rating,
        RatingSource Source,
        int EditVersion);

    private readonly record struct RatingPreloadRequest(
        ImageFileInfo ImageInfo,
        int EditVersion);

    private void ResetRatingPreload(CancellationToken loadCancellationToken)
    {
        CancelRatingPreload();
        _ratingPreloadCts = CancellationTokenSource.CreateLinkedTokenSource(loadCancellationToken);
        Interlocked.Increment(ref _ratingPreloadVersion);

        lock (_ratingPreloadLock)
        {
            _pendingRatingPreloadItems.Clear();
            _queuedRatingPreloadPaths.Clear();
            _isRatingPreloadRunning = false;
        }
    }

    private void CancelRatingPreload()
    {
        _ratingPreloadCts?.Cancel();
        Interlocked.Increment(ref _ratingPreloadVersion);

        lock (_ratingPreloadLock)
        {
            _pendingRatingPreloadItems.Clear();
            _queuedRatingPreloadPaths.Clear();
            _isRatingPreloadRunning = false;
        }
    }

    private void QueueRatingPreloadForCurrentImages(CancellationToken loadCancellationToken)
    {
        if (loadCancellationToken.IsCancellationRequested)
            return;

        var preloadToken = _ratingPreloadCts?.Token;
        if (preloadToken == null || preloadToken.Value.IsCancellationRequested)
            return;

        var preloadVersion = Volatile.Read(ref _ratingPreloadVersion);
        var shouldStartWorker = false;

        lock (_ratingPreloadLock)
        {
            foreach (var image in _allImages)
            {
                var path = image.ImageFile.Path;
                if (_queuedRatingPreloadPaths.Add(path))
                {
                    var editVersion = image.BeginRatingPreload();
                    if (editVersion >= 0)
                    {
                        _pendingRatingPreloadItems.Enqueue(new RatingPreloadRequest(image, editVersion));
                    }
                    else
                    {
                        _queuedRatingPreloadPaths.Remove(path);
                    }
                }
            }

            if (!_isRatingPreloadRunning && _pendingRatingPreloadItems.Count > 0)
            {
                _isRatingPreloadRunning = true;
                shouldStartWorker = true;
            }
        }

        if (shouldStartWorker)
        {
            _ = Task.Run(() => RunRatingPreloadAsync(preloadVersion, preloadToken.Value));
        }
    }

    private async Task RunRatingPreloadAsync(int preloadVersion, CancellationToken cancellationToken)
    {
        var commitBatch = new List<RatingPreloadResult>(RatingPreloadCommitBatchSize);

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   preloadVersion == Volatile.Read(ref _ratingPreloadVersion))
            {
                var request = DequeueRatingPreloadItem(preloadVersion, cancellationToken);
                if (request == null)
                    break;

                var imageInfo = request.Value.ImageInfo;

                try
                {
                    var (rating, source) = await _ratingService.GetRatingAsync(imageInfo.ImageFile).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (preloadVersion != Volatile.Read(ref _ratingPreloadVersion))
                        break;

                    commitBatch.Add(new RatingPreloadResult(imageInfo, rating, source, request.Value.EditVersion));
                    if (commitBatch.Count >= RatingPreloadCommitBatchSize)
                    {
                        await CommitRatingPreloadBatchAsync(commitBatch, preloadVersion, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    await CancelRatingPreloadOnUIThreadAsync(imageInfo, request.Value.EditVersion).ConfigureAwait(false);
                    System.Diagnostics.Debug.WriteLine($"[RatingPreload] failed for {imageInfo.ImageName}: {ex.Message}");
                }
            }

            if (commitBatch.Count > 0 &&
                !cancellationToken.IsCancellationRequested &&
                preloadVersion == Volatile.Read(ref _ratingPreloadVersion))
            {
                await CommitRatingPreloadBatchAsync(commitBatch, preloadVersion, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            var shouldRestart = false;
            lock (_ratingPreloadLock)
            {
                if (preloadVersion == Volatile.Read(ref _ratingPreloadVersion))
                {
                    _isRatingPreloadRunning = false;
                    shouldRestart = !cancellationToken.IsCancellationRequested && _pendingRatingPreloadItems.Count > 0;
                    if (shouldRestart)
                    {
                        _isRatingPreloadRunning = true;
                    }
                }
            }

            if (shouldRestart)
            {
                _ = Task.Run(() => RunRatingPreloadAsync(preloadVersion, cancellationToken));
            }
        }
    }

    private RatingPreloadRequest? DequeueRatingPreloadItem(int preloadVersion, CancellationToken cancellationToken)
    {
        lock (_ratingPreloadLock)
        {
            while (!cancellationToken.IsCancellationRequested &&
                   preloadVersion == Volatile.Read(ref _ratingPreloadVersion) &&
                   _pendingRatingPreloadItems.Count > 0)
            {
                var request = _pendingRatingPreloadItems.Dequeue();
                _queuedRatingPreloadPaths.Remove(request.ImageInfo.ImageFile.Path);

                if (!request.ImageInfo.IsRatingLoaded)
                    return request;
            }
        }

        return null;
    }

    private async Task CancelRatingPreloadOnUIThreadAsync(ImageFileInfo imageInfo, int editVersion)
    {
        var dispatcher = App.MainWindow.DispatcherQueue;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            imageInfo.CancelRatingPreload(editVersion);
            tcs.TrySetResult(null);
        }))
        {
            return;
        }

        await tcs.Task.ConfigureAwait(false);
    }

    private async Task CommitRatingPreloadBatchAsync(
        List<RatingPreloadResult> commitBatch,
        int preloadVersion,
        CancellationToken cancellationToken)
    {
        if (commitBatch.Count == 0)
            return;

        var batch = commitBatch.ToArray();
        commitBatch.Clear();

        var dispatcher = App.MainWindow.DispatcherQueue;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try
            {
                if (!cancellationToken.IsCancellationRequested &&
                    preloadVersion == Volatile.Read(ref _ratingPreloadVersion))
                {
                    foreach (var result in batch)
                    {
                        result.ImageInfo.ApplyLoadedRating(result.Rating, result.Source, result.EditVersion);
                        RefreshBurstCoverAfterRatingChanged(result.ImageInfo);
                    }
                }

                tcs.TrySetResult(null);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            foreach (var result in batch)
            {
                result.ImageInfo.CancelRatingPreload(result.EditVersion);
            }
            return;
        }

        await tcs.Task.ConfigureAwait(false);
    }

    private static ImageFileInfo CreatePlaceholderImageInfo(StorageFile file)
    {
        return new ImageFileInfo(
            200,
            200,
            string.Empty,
            file,
            file.DisplayName,
            file.FileType);
    }

    private void StartDeferredImageInfoLoad(ImageFileInfo imageInfo, CancellationToken cancellationToken)
    {
        _ = StartDeferredImageInfoLoadAsync(imageInfo, cancellationToken);
    }

    private async Task StartDeferredImageInfoLoadAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DeferredImageInfoLoadDelayMs, cancellationToken);

            await _metadataHydrationGate.WaitAsync(cancellationToken);
            try
            {
                await HydrateImageMetadataAsync(imageInfo, cancellationToken);
            }
            finally
            {
                _metadataHydrationGate.Release();
            }

        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[StartDeferredImageInfoLoadAsync] failed for {imageInfo.ImageName}: {ex.Message}");
        }
    }

    private async Task HydrateImageMetadataAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await LoadImageMetadataAsync(imageInfo.ImageFile, cancellationToken);
            if (metadata.HasValue && !cancellationToken.IsCancellationRequested)
            {
                var (width, height, title, dateTaken) = metadata.Value;
                imageInfo.UpdateMetadata(width, height, title);
                imageInfo.SetDateTakenFromProperties(dateTaken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadImageMetadataAsync] failed for {imageInfo.ImageName}: {ex.Message}");
        }
    }

    private async Task HydrateDateTakenForBurstCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = _allImages
            .Where(image => !image.DateTaken.HasValue && !string.IsNullOrWhiteSpace(GetBurstGroupKey(image)))
            .ToList();

        if (candidates.Count == 0)
            return;

        using var gate = new SemaphoreSlim(4);
        var tasks = candidates.Select(async image =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(image.ImageFile, cancellationToken);
                var dateTaken = ImageMetadataDateHelper.NormalizeDateTaken(properties.DateTaken, image.FileType);
                if (dateTaken.HasValue)
                {
                    image.SetDateTakenFromProperties(dateTaken);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HydrateDateTakenForBurstCandidatesAsync] failed for {image.ImageName}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    public static async System.Threading.Tasks.Task<ImageFileInfo> LoadImageInfo(StorageFile file)
    {
        var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(file);
        int width = (int)properties.Width;
        int height = (int)properties.Height;
        
        var orientation = properties.Orientation;
        if (orientation == Windows.Storage.FileProperties.PhotoOrientation.Rotate90 || 
            orientation == Windows.Storage.FileProperties.PhotoOrientation.Rotate270 ||
            orientation == Windows.Storage.FileProperties.PhotoOrientation.Transpose ||
            orientation == Windows.Storage.FileProperties.PhotoOrientation.Transverse)
        {
            (width, height) = (height, width);
        }
        
        return new ImageFileInfo(
            width,
            height,
            properties.Title,
            file,
            file.DisplayName,
            file.FileType);
    }

    private async Task<(int Width, int Height, string Title, DateTime? DateTaken)?> LoadImageMetadataAsync(
        StorageFile file,
        CancellationToken cancellationToken)
    {
        int width = 200;
        int height = 200;
        string title = string.Empty;
        DateTime? dateTaken = null;

        try
        {
            var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(file, cancellationToken);

            if (properties.Width > 0 && properties.Height > 0)
            {
                width = (int)properties.Width;
                height = (int)properties.Height;

                var orientation = properties.Orientation;
                if (orientation == Windows.Storage.FileProperties.PhotoOrientation.Rotate90 ||
                    orientation == Windows.Storage.FileProperties.PhotoOrientation.Rotate270 ||
                    orientation == Windows.Storage.FileProperties.PhotoOrientation.Transpose ||
                    orientation == Windows.Storage.FileProperties.PhotoOrientation.Transverse)
                {
                    (width, height) = (height, width);
                }
            }

            title = properties.Title;

            dateTaken = ImageMetadataDateHelper.NormalizeDateTaken(properties.DateTaken, file.FileType);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadImageMetadataAsync] Properties failed for {file.Name}: {ex.Message}");
        }

        if (width == 200 && height == 200 && ImageFormatRegistry.IsPhotoshop(file.FileType))
        {
            var photoshopSize = await PhotoshopImageInfoReader.TryReadSizeAsync(file, cancellationToken);
            if (photoshopSize.HasValue)
            {
                width = photoshopSize.Value.Width;
                height = photoshopSize.Value.Height;
            }
        }

        return (width, height, title, dateTaken);
    }

    private async Task<ImageFileInfo?> LoadImageInfoSafeAsync(StorageFile file, CancellationToken cancellationToken)
    {
        try
        {
            using var testStream = await file.OpenReadAsync().AsTask(cancellationToken);
            testStream.Dispose();
        }
        catch (Exception ex)
        {
            return null;
        }

        try
        {
            int width = 200;
            int height = 200;
            string title = string.Empty;
            var fileExtension = Path.GetExtension(file.Name);
            var isRaw = ImageFormatRegistry.IsRaw(fileExtension);

            // 先尝试用 GetImagePropertiesAsync（包括 RAW 文件）
            try
            {
                var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(file, cancellationToken);
                
                if (properties.Width > 0 && properties.Height > 0)
                {
                    width = (int)properties.Width;
                    height = (int)properties.Height;
                    
                    var orientation = properties.Orientation;
                    if (orientation == Windows.Storage.FileProperties.PhotoOrientation.Rotate90 || 
                        orientation == Windows.Storage.FileProperties.PhotoOrientation.Rotate270 ||
                        orientation == Windows.Storage.FileProperties.PhotoOrientation.Transpose ||
                        orientation == Windows.Storage.FileProperties.PhotoOrientation.Transverse)
                    {
                        (width, height) = (height, width);
                    }
                }
                title = properties.Title;
            }
            catch (Exception ex)
            {
            }

            if (width == 200 && height == 200 && ImageFormatRegistry.IsPhotoshop(file.FileType))
            {
                var photoshopSize = await PhotoshopImageInfoReader.TryReadSizeAsync(file, cancellationToken);
                if (photoshopSize.HasValue)
                {
                    width = photoshopSize.Value.Width;
                    height = photoshopSize.Value.Height;
                }
            }

            // 如果还是默认值，再尝试用 BitmapDecoder
            if (width == 200 && height == 200)
            {
                try
                {
                    using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
                    var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                    width = (int)decoder.PixelWidth;
                    height = (int)decoder.PixelHeight;
                    
                    try
                    {
                        var properties = await decoder.BitmapProperties.GetPropertiesAsync(new[] { "System.Photo.Orientation" });
                        if (properties.TryGetValue("System.Photo.Orientation", out var orientationValue))
                        {
                            var exifOrientation = Convert.ToUInt16(orientationValue.Value);
                            if (exifOrientation == 6 || exifOrientation == 8 || exifOrientation == 5 || exifOrientation == 7)
                            {
                                (width, height) = (height, width);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
                catch (Exception ex)
                {
                }
            }

            var imageInfo = new ImageFileInfo(
                width,
                height,
                title,
                file,
                file.DisplayName,
                file.FileType);

            return imageInfo;
        }
        catch (Exception ex)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _settingsService.PreferPsdAsPrimaryPreviewChanged -= OnPreferPsdAsPrimaryPreviewChanged;
        _settingsService.CollapseBurstGroupsChanged -= OnCollapseBurstGroupsChanged;
        _externalDeviceWatcherService.ExternalDevicesChanged -= OnExternalDevicesChanged;
        _loadImagesCts?.Cancel();
        CancelRatingPreload();

        foreach (var image in _allImages)
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
        currentFolder.IsLoaded = false;
        CancelRatingPreload();
        CancelCurrentImageWork();
        ClearBurstGroups();
        Images.Clear();
        ImagesChanged?.Invoke(this, EventArgs.Empty);
        
        await LoadImagesWithoutHistoryAsync(currentFolder);
    }

    private async System.Threading.Tasks.Task LoadImagesWithoutHistoryAsync(FolderNode folderNode)
    {
        _loadImagesCts?.Cancel();
        _loadImagesCts = new CancellationTokenSource();
        var cancellationToken = _loadImagesCts.Token;
        ResetRatingPreload(cancellationToken);

        SelectedFolder = folderNode;
        UpdateBreadcrumbPath(folderNode);
        OnPropertyChanged(nameof(CanGoUp));

        // 加载目录前确认当前设置的缩略图尺寸
        ThumbnailSize = _settingsService.ThumbnailSize;

        CancelCurrentImageWork();
        ClearBurstGroups();
        _allImages.Clear();
        Images.Clear();
        ImagesChanged?.Invoke(this, EventArgs.Empty);
        await Task.Yield();

        await SyncCurrentSubFoldersAsync(folderNode, cancellationToken);

        if (folderNode?.Folder == null)
            return;

        await RecordFolderVisitAsync(folderNode);

        try
        {
            var fileTypeFilter = ImageExtensions.ToList();
            var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, fileTypeFilter)
            {
                IndexerOption = IndexerOption.DoNotUseIndexer,
                FolderDepth = FolderDepth.Shallow
            };

            var result = folderNode.Folder.CreateFileQueryWithOptions(queryOptions);
            
            var fileNameMap = new Dictionary<string, List<StorageFile>>(StringComparer.OrdinalIgnoreCase);
            var processedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            var batchSize = (uint)Math.Max(1, _settingsService.BatchSize);
            uint index = 0;
            var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                var requestBatchSize = index == 0
                    ? Math.Min(batchSize, InitialFileEnumerationBatchSize)
                    : batchSize;
                var batch = await result.GetFilesAsync(index, requestBatchSize);
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
                        var sortedFiles = groupFiles
                            .OrderBy(f => ImageGroup.GetFormatPriority(f.FileType, _settingsService.PreferPsdAsPrimaryPreview))
                            .ToList();
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

                    AddPlaceholderGroups(newGroupMap, cancellationToken);
                    if (index == 0)
                    {
                        // System.Diagnostics.Debug.WriteLine($"[LoadImagesWithoutHistoryAsync] First placeholders added in {loadStopwatch.ElapsedMilliseconds}ms, files={batch.Count}, primary={newPrimaryFiles.Count}");
                    }
                }
                
                index += (uint)batch.Count;
            }
            


            _allImages.Clear();
            _allImages.AddRange(Images);
            await HydrateDateTakenForBurstCandidatesAsync(cancellationToken);
            RebuildBurstGroups();
            ApplyFilter();
            QueueRatingPreloadForCurrentImages(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
        }
    }

    private void CancelCurrentImageWork()
    {
        foreach (var image in _allImages)
        {
            image.CancelThumbnailLoad();
        }
    }

    public void TogglePendingDelete(ImageFileInfo image)
    {
        if (image == null)
            return;

        image.IsPendingDelete = !image.IsPendingDelete;
        UpdatePendingDeleteCount();
    }

    public void TogglePendingDeleteForSelected(IEnumerable<ImageFileInfo> selectedImages)
    {
        if (selectedImages == null)
            return;

        var images = selectedImages.ToList();
        if (images.Count == 0)
            return;

        var hasAnyUnmarked = images.Any(img => !img.IsPendingDelete);

        foreach (var image in images)
        {
            image.IsPendingDelete = hasAnyUnmarked;
        }
        UpdatePendingDeleteCount();
    }

    public void ClearAllPendingDelete()
    {
        foreach (var image in _allImages)
        {
            image.IsPendingDelete = false;
        }
        PendingDeleteCount = 0;
    }

    public List<ImageFileInfo> GetPendingDeleteImages()
    {
        return Images.Where(i => i.IsPendingDelete).ToList();
    }

    public void RemoveImagesFromLibrary(IEnumerable<ImageFileInfo> images)
    {
        foreach (var image in images.ToList())
        {
            _allImages.Remove(image);
            Images.Remove(image);
        }

        RebuildBurstGroups();
        ApplyFilter();
    }

    public void ReplaceImageInLibrary(ImageFileInfo oldImage, ImageFileInfo newImage)
    {
        var sourceIndex = _allImages.IndexOf(oldImage);
        if (sourceIndex >= 0)
        {
            _allImages[sourceIndex] = newImage;
        }

        var visibleIndex = Images.IndexOf(oldImage);
        if (visibleIndex >= 0)
        {
            Images[visibleIndex] = newImage;
        }

        RebuildBurstGroups();
        ApplyFilter();
    }

    private void UpdatePendingDeleteCount()
    {
        PendingDeleteCount = Images.Count(i => i.IsPendingDelete);
    }

    public static bool IsRawFile(string extension)
    {
        return ImageFormatRegistry.IsRaw(extension);
    }

    public static bool IsJpgFile(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext == ".jpg" || ext == ".jpeg";
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        ApplyFilter();
    }

    public void ToggleBurstExpansion(ImageFileInfo image)
    {
        if (!_settingsService.CollapseBurstGroups)
            return;

        var burstGroup = image.BurstGroup;
        if (burstGroup == null || burstGroup.Images.Count < 2)
            return;

        var visibleMembers = burstGroup.Images
            .Where(MatchFilter)
            .OrderBy(member => _allImages.IndexOf(member))
            .ToList();
        if (visibleMembers.Count <= 1)
            return;

        var displayIndex = GetFirstVisibleBurstIndex(burstGroup);
        if (displayIndex < 0)
            return;

        var currentDisplay = Images[displayIndex];
        var expand = !burstGroup.IsExpanded;
        if (!expand && HasPendingDeleteBurstMember(burstGroup))
            return;

        if (!expand)
        {
            burstGroup.RecalculateCover();
        }

        burstGroup.SetExpanded(expand);

        if (expand)
        {
            foreach (var member in visibleMembers)
            {
                member.SetBurstDisplayCover(false);
            }

            var displayPrimary = burstGroup.GetCoverImage(visibleMembers);
            var insertIndex = displayIndex;
            if (!ReferenceEquals(currentDisplay, displayPrimary))
            {
                Images[displayIndex] = displayPrimary;
                currentDisplay.SetBurstChildVisible(true);
                displayPrimary.SetBurstChildVisible(false);
                insertIndex = displayIndex + 1;
            }
            else
            {
                currentDisplay.SetBurstChildVisible(false);
                insertIndex = displayIndex + 1;
            }

            foreach (var member in visibleMembers)
            {
                if (ReferenceEquals(member, displayPrimary))
                    continue;

                if (!Images.Contains(member))
                {
                    Images.Insert(insertIndex, member);
                    insertIndex++;
                }

                member.SetBurstChildVisible(true);
            }
        }
        else
        {
            var coverImage = burstGroup.GetCoverImage(visibleMembers);
            foreach (var member in visibleMembers)
            {
                Images.Remove(member);
                member.SetBurstChildVisible(false);
                member.SetBurstDisplayCover(false);
            }

            Images.Insert(Math.Clamp(displayIndex, 0, Images.Count), coverImage);
            coverImage.SetBurstChildVisible(false);
            coverImage.SetBurstDisplayCover(true);
        }

        ImagesChanged?.Invoke(this, EventArgs.Empty);
        UpdatePendingDeleteCount();
    }

    private int GetFirstVisibleBurstIndex(BurstPhotoGroup burstGroup)
    {
        for (var i = 0; i < Images.Count; i++)
        {
            if (ReferenceEquals(Images[i].BurstGroup, burstGroup))
            {
                return i;
            }
        }

        return -1;
    }

    public ImageFileInfo? ExpandBurstForDirectionalNavigation(ImageFileInfo image, int direction)
    {
        if (!_settingsService.CollapseBurstGroups)
            return null;

        var burstGroup = image.BurstGroup;
        if (burstGroup == null || burstGroup.Images.Count < 2)
            return null;

        var visibleMembers = GetVisibleBurstMembers(burstGroup);
        if (visibleMembers.Count <= 1)
            return image;

        if (!burstGroup.IsExpanded)
        {
            var displayIndex = GetFirstVisibleBurstIndex(burstGroup);
            if (displayIndex < 0)
                return null;

            var currentDisplay = Images[displayIndex];
            burstGroup.SetExpanded(true);

            foreach (var member in visibleMembers)
            {
                member.SetBurstDisplayCover(false);
            }

            var displayMembers = GetExpandedBurstDisplayMembers(burstGroup, visibleMembers);
            var displayPrimary = displayMembers[0];
            var insertIndex = displayIndex;

            if (!ReferenceEquals(currentDisplay, displayPrimary))
            {
                Images[displayIndex] = displayPrimary;
                currentDisplay.SetBurstChildVisible(true);
                displayPrimary.SetBurstChildVisible(false);
                insertIndex = displayIndex + 1;
            }
            else
            {
                currentDisplay.SetBurstChildVisible(false);
                insertIndex = displayIndex + 1;
            }

            foreach (var member in displayMembers.Skip(1))
            {
                if (!Images.Contains(member))
                {
                    Images.Insert(insertIndex, member);
                    insertIndex++;
                }

                member.SetBurstChildVisible(true);
            }

            ImagesChanged?.Invoke(this, EventArgs.Empty);
            UpdatePendingDeleteCount();
        }

        var expandedMembers = GetExpandedBurstDisplayMembers(burstGroup, visibleMembers);
        return direction < 0 ? expandedMembers[^1] : expandedMembers[0];
    }

    public ImageFileInfo? CollapseBurstGroup(BurstPhotoGroup burstGroup)
    {
        if (!_settingsService.CollapseBurstGroups)
            return null;

        if (burstGroup.Images.Count < 2 || !burstGroup.IsExpanded)
            return null;

        if (HasPendingDeleteBurstMember(burstGroup))
            return null;

        var visibleMembers = GetVisibleBurstMembers(burstGroup);
        if (visibleMembers.Count == 0)
            return null;

        var displayIndex = GetFirstVisibleBurstIndex(burstGroup);
        if (displayIndex < 0)
            return null;

        burstGroup.RecalculateCover();
        burstGroup.SetExpanded(false);
        var coverImage = burstGroup.GetCoverImage(visibleMembers);

        foreach (var member in visibleMembers)
        {
            Images.Remove(member);
            member.SetBurstChildVisible(false);
            member.SetBurstDisplayCover(false);
        }

        Images.Insert(Math.Clamp(displayIndex, 0, Images.Count), coverImage);
        coverImage.SetBurstChildVisible(false);
        coverImage.SetBurstDisplayCover(true);

        ImagesChanged?.Invoke(this, EventArgs.Empty);
        UpdatePendingDeleteCount();
        return coverImage;
    }

    public bool CollapseExpandedBurstGroupsExcept(BurstPhotoGroup? preservedGroup)
    {
        if (!_settingsService.CollapseBurstGroups)
            return false;

        var expandedGroups = _burstGroups
            .Where(group => group.IsExpanded && !ReferenceEquals(group, preservedGroup))
            .ToList();
        if (expandedGroups.Count == 0)
            return false;

        var collapsedAny = false;
        foreach (var group in expandedGroups)
        {
            collapsedAny |= CollapseBurstGroup(group) != null;
        }

        return collapsedAny;
    }

    private static bool HasPendingDeleteBurstMember(BurstPhotoGroup burstGroup)
    {
        return burstGroup.Images.Any(member => member.IsPendingDelete);
    }

    private List<ImageFileInfo> GetVisibleBurstMembers(BurstPhotoGroup burstGroup)
    {
        return burstGroup.Images
            .Where(MatchFilter)
            .OrderBy(member => _allImages.IndexOf(member))
            .ToList();
    }

    private List<ImageFileInfo> GetExpandedBurstDisplayMembers(
        BurstPhotoGroup burstGroup,
        IReadOnlyList<ImageFileInfo> visibleMembers)
    {
        var displayPrimary = burstGroup.GetCoverImage(visibleMembers);
        return visibleMembers
            .OrderBy(member => ReferenceEquals(member, displayPrimary) ? 0 : 1)
            .ThenBy(member => _allImages.IndexOf(member))
            .ToList();
    }

    public IReadOnlyList<ImageFileInfo> GetBurstExpansionWarmupImages(ImageFileInfo image)
    {
        if (!_settingsService.CollapseBurstGroups)
            return Array.Empty<ImageFileInfo>();

        var burstGroup = image.BurstGroup;
        if (burstGroup == null || burstGroup.Images.Count < 2 || burstGroup.IsExpanded)
            return Array.Empty<ImageFileInfo>();

        return burstGroup.Images
            .Where(member => !ReferenceEquals(member, image) && MatchFilter(member))
            .ToList();
    }

    public void RefreshBurstCoverAfterRatingChanged(ImageFileInfo image)
    {
        var burstGroup = image.BurstGroup;
        if (burstGroup == null || burstGroup.Images.Count < 2)
            return;

        burstGroup.RecalculateCover();
        if (!_settingsService.CollapseBurstGroups)
        {
            foreach (var member in burstGroup.Images)
            {
                member.SetBurstDisplayCover(false);
                member.SetBurstChildVisible(true);
            }

            ImagesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (burstGroup.IsExpanded)
        {
            var expandedVisibleMembers = burstGroup.Images
                .Where(MatchFilter)
                .OrderBy(member => _allImages.IndexOf(member))
                .ToList();
            if (expandedVisibleMembers.Count == 0)
                return;

            var expandedVisibleIndex = Images
                .Select((member, index) => new { Member = member, Index = index })
                .FirstOrDefault(candidate => candidate.Member.BurstGroup == burstGroup)
                ?.Index ?? -1;
            if (expandedVisibleIndex < 0)
                return;

            var expandedCoverImage = burstGroup.GetCoverImage(expandedVisibleMembers);
            foreach (var member in expandedVisibleMembers)
            {
                Images.Remove(member);
                member.SetBurstDisplayCover(false);
                member.SetBurstChildVisible(!ReferenceEquals(member, expandedCoverImage));
            }

            Images.Insert(Math.Clamp(expandedVisibleIndex, 0, Images.Count), expandedCoverImage);
            var expandedInsertIndex = expandedVisibleIndex + 1;
            foreach (var member in expandedVisibleMembers)
            {
                if (ReferenceEquals(member, expandedCoverImage))
                    continue;

                Images.Insert(Math.Clamp(expandedInsertIndex, 0, Images.Count), member);
                expandedInsertIndex++;
            }

            ImagesChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var visibleMembers = burstGroup.Images
            .Where(MatchFilter)
            .OrderBy(member => _allImages.IndexOf(member))
            .ToList();
        if (visibleMembers.Count == 0)
            return;

        var visibleIndex = Images
            .Select((member, index) => new { Member = member, Index = index })
            .FirstOrDefault(candidate => candidate.Member.BurstGroup == burstGroup)
            ?.Index ?? -1;
        if (visibleIndex < 0)
            return;

        var coverImage = burstGroup.GetCoverImage(visibleMembers);
        if (!ReferenceEquals(Images[visibleIndex], coverImage))
        {
            Images[visibleIndex] = coverImage;
        }

        foreach (var member in visibleMembers)
        {
            member.SetBurstChildVisible(false);
            member.SetBurstDisplayCover(ReferenceEquals(member, coverImage));
        }

        ImagesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyFilter()
    {
        Images.Clear();
        foreach (var image in _allImages)
        {
            image.SetBurstDisplayCover(false);
            image.SetBurstChildVisible(false);
        }

        if (!_settingsService.CollapseBurstGroups)
        {
            foreach (var image in _allImages)
            {
                if (!MatchFilter(image))
                    continue;

                var isBurstMember = image.BurstGroup?.Images.Count > 1;
                image.SetBurstDisplayCover(false);
                image.SetBurstChildVisible(isBurstMember == true);
                Images.Add(image);
            }

            ImagesChanged?.Invoke(this, EventArgs.Empty);
            UpdatePendingDeleteCount();
            return;
        }

        var processedBurstGroups = new HashSet<BurstPhotoGroup>();
        foreach (var image in _allImages)
        {
            var burstGroup = image.BurstGroup;
            if (burstGroup == null || burstGroup.Images.Count < 2)
            {
                if (MatchFilter(image))
                {
                    Images.Add(image);
                    image.SetBurstChildVisible(false);
                }
                continue;
            }

            if (!processedBurstGroups.Add(burstGroup))
                continue;

            var visibleMembers = burstGroup.Images
                .Where(MatchFilter)
                .OrderBy(member => _allImages.IndexOf(member))
                .ToList();

            if (visibleMembers.Count == 0)
                continue;

            var displayPrimary = burstGroup.GetCoverImage(visibleMembers);

            Images.Add(displayPrimary);
            displayPrimary.SetBurstChildVisible(false);
            displayPrimary.SetBurstDisplayCover(!burstGroup.IsExpanded);

            if (!burstGroup.IsExpanded || visibleMembers.Count <= 1)
            {
                foreach (var hiddenMember in visibleMembers.Where(member => !ReferenceEquals(member, displayPrimary)))
                {
                    hiddenMember.SetBurstChildVisible(false);
                    hiddenMember.SetBurstDisplayCover(false);
                }
                continue;
            }

            displayPrimary.SetBurstDisplayCover(false);
            foreach (var member in visibleMembers)
            {
                if (ReferenceEquals(member, displayPrimary))
                    continue;

                Images.Add(member);
                member.SetBurstChildVisible(true);
                member.SetBurstDisplayCover(false);
            }
        }
        ImagesChanged?.Invoke(this, EventArgs.Empty);
        UpdatePendingDeleteCount();
    }

    public void RebuildBurstGroups()
    {
        foreach (var image in _allImages)
        {
            image.ClearBurstInfo();
        }

        _burstGroups.Clear();

        var candidates = _allImages
            .Select((image, index) => new BurstCandidate(
                image,
                index,
                GetBurstGroupKey(image),
                GetBurstSortTime(image),
                image.DateTaken.HasValue))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.GroupKey) && candidate.SortTime != DateTimeOffset.MinValue)
            .GroupBy(candidate => candidate.GroupKey, StringComparer.OrdinalIgnoreCase);

        foreach (var keyGroup in candidates)
        {
            var orderedCandidates = keyGroup
                .OrderBy(candidate => candidate.SortTime)
                .ThenBy(candidate => candidate.OriginalIndex)
                .ToList();

            var cluster = new List<BurstCandidate>();
            foreach (var candidate in orderedCandidates)
            {
                if (cluster.Count == 0)
                {
                    cluster.Add(candidate);
                    continue;
                }

                var previous = cluster[^1];
                if (AreBurstCandidatesAdjacent(previous, candidate))
                {
                    cluster.Add(candidate);
                }
                else
                {
                    AddBurstGroupIfNeeded(keyGroup.Key, cluster);
                    cluster = new List<BurstCandidate> { candidate };
                }
            }

            AddBurstGroupIfNeeded(keyGroup.Key, cluster);
        }

        foreach (var image in _allImages)
        {
            image.RefreshBurstProperties();
        }
    }

    private void ClearBurstGroups()
    {
        foreach (var image in _allImages)
        {
            image.ClearBurstInfo();
        }

        _burstGroups.Clear();
    }

    private void AddBurstGroupIfNeeded(string groupKey, IReadOnlyList<BurstCandidate> candidates)
    {
        if (candidates.Count < 2)
            return;

        var orderedImages = candidates
            .OrderBy(candidate => candidate.OriginalIndex)
            .Select(candidate => candidate.Image)
            .ToList();

        _burstGroups.Add(new BurstPhotoGroup(groupKey, orderedImages));
    }

    private static string GetBurstGroupKey(ImageFileInfo image)
    {
        var name = Path.GetFileNameWithoutExtension(image.ImageFile?.Name ?? image.ImageName);
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var end = name.Length - 1;
        while (end >= 0 && char.IsDigit(name[end]))
        {
            end--;
        }

        if (end == name.Length - 1)
            return string.Empty;

        while (end >= 0 && IsBurstNameSeparator(name[end]))
        {
            end--;
        }

        if (end < 2)
            return string.Empty;

        return name[..(end + 1)].ToUpperInvariant();
    }

    private static bool IsBurstNameSeparator(char value)
    {
        return value == '_' || value == '-' || value == '.' || char.IsWhiteSpace(value);
    }

    private static DateTimeOffset GetBurstSortTime(ImageFileInfo image)
    {
        if (image.DateTaken.HasValue)
            return new DateTimeOffset(image.DateTaken.Value);

        if (ImageFormatRegistry.IsPhotoshop(image.FileType))
            return DateTimeOffset.MinValue;

        var created = image.ImageFile?.DateCreated;
        if (created.HasValue && created.Value != default)
            return created.Value;

        return DateTimeOffset.MinValue;
    }

    private static bool AreBurstCandidatesAdjacent(BurstCandidate previous, BurstCandidate candidate)
    {
        var deltaSeconds = Math.Abs((candidate.SortTime - previous.SortTime).TotalSeconds);
        if (deltaSeconds > BurstGroupingWindowSeconds)
            return false;

        if (previous.HasDateTaken && candidate.HasDateTaken)
            return true;

        return deltaSeconds > 0;
    }

    private readonly record struct BurstCandidate(
        ImageFileInfo Image,
        int OriginalIndex,
        string GroupKey,
        DateTimeOffset SortTime,
        bool HasDateTaken);

    private void OnPreferPsdAsPrimaryPreviewChanged(object? sender, bool enabled)
    {
        ReapplyPrimaryImagesForCurrentGroups();
    }

    private void OnCollapseBurstGroupsChanged(object? sender, bool enabled)
    {
        ResetBurstExpansionState();
        ApplyFilter();
    }

    private void ResetBurstExpansionState()
    {
        foreach (var group in _burstGroups)
        {
            group.SetExpanded(false);
        }

        foreach (var image in _allImages)
        {
            image.SetBurstDisplayCover(false);
            image.SetBurstChildVisible(false);
        }
    }

    private void ReapplyPrimaryImagesForCurrentGroups()
    {
        var groups = _allImages
            .Select(image => image.Group)
            .Where(group => group != null)
            .Distinct()
            .Cast<ImageGroup>()
            .ToList();

        if (groups.Count == 0)
            return;

        var cancellationToken = _loadImagesCts?.Token ?? CancellationToken.None;
        _allImages.Clear();

        foreach (var group in groups)
        {
            group.ReapplyPrimary(_settingsService.PreferPsdAsPrimaryPreview);
            group.PrimaryImage.UpdateDisplaySize(ThumbnailSize);
            _allImages.Add(group.PrimaryImage);
            if (!cancellationToken.IsCancellationRequested)
            {
                StartDeferredImageInfoLoad(group.PrimaryImage, cancellationToken);
            }
        }

        RebuildBurstGroups();
        ApplyFilter();
        QueueRatingPreloadForCurrentImages(cancellationToken);
    }

    private bool MatchFilter(ImageFileInfo image)
    {
        return MatchFileType(image) && MatchRating(image) && MatchPendingDelete(image);
    }

    private bool MatchPendingDelete(ImageFileInfo image)
    {
        if (!Filter.IsPendingDeleteFilter)
            return true;
        return image.IsPendingDelete;
    }

    private bool MatchFileType(ImageFileInfo image)
    {
        if (!Filter.IsImageFilter && !Filter.IsRawFilter)
            return true;
        
        if (image.ImageFile == null)
            return false;
        
        var ext = Path.GetExtension(image.ImageFile.Path);
        bool isRaw = FilterViewModel.IsRawExtension(ext);
        
        if (Filter.IsImageFilter && Filter.IsRawFilter)
            return true;
        if (Filter.IsImageFilter)
            return !isRaw;
        if (Filter.IsRawFilter)
            return isRaw;
        
        return true;
    }

    private bool MatchRating(ImageFileInfo image)
    {
        switch (Filter.RatingMode)
        {
            case RatingFilterMode.All:
                return true;
            case RatingFilterMode.NoRating:
                return image.Rating == 0;
            case RatingFilterMode.HasRating:
                var stars = ImageFileInfo.RatingToStars(image.Rating);
                switch (Filter.RatingCondition)
                {
                    case RatingCondition.Equals:
                        return stars == Filter.RatingStars;
                    case RatingCondition.GreaterOrEqual:
                        return stars >= Filter.RatingStars;
                    case RatingCondition.LessOrEqual:
                        return stars <= Filter.RatingStars;
                }
                return false;
        }
        return true;
    }
}
