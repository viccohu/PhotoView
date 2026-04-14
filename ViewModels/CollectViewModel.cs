using CommunityToolkit.Mvvm.ComponentModel;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using PhotoView.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using Windows.Storage;
using Windows.Storage.Search;

namespace PhotoView.ViewModels;

public partial class CollectViewModel : ObservableRecipient, IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp",
        ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".sr2", ".raf",
        ".orf", ".rw2", ".pef", ".dng", ".srw", ".raw", ".iiq", ".3fr",
        ".mef", ".mos", ".x3f", ".erf", ".dcr", ".kdc"
    };

    private const uint FirstBatchSize = 30;
    private const int DeferredImageInfoLoadDelayMs = 400;
    private readonly PreviewWorkspaceService _workspaceService;
    private readonly ISettingsService _settingsService;
    private readonly RatingService _ratingService;
    private readonly SemaphoreSlim _metadataHydrationGate = new(2);
    private readonly List<ImageFileInfo> _allImages = new();
    private readonly HashSet<string> _loadedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loadCts;
    private int _pendingDeleteCount;
    private bool _loadedIncludeSubfolders;

    public CollectViewModel(
        PreviewWorkspaceService workspaceService,
        ISettingsService settingsService,
        RatingService ratingService)
    {
        _workspaceService = workspaceService;
        _settingsService = settingsService;
        _ratingService = ratingService;

        FolderTree = new ObservableCollection<FolderNode>();
        Images = new ObservableCollection<ImageFileInfo>();
        Filter = new FilterViewModel();
        Filter.FilterChanged += OnFilterChanged;
        _workspaceService.SourcesChanged += WorkspaceService_SourcesChanged;
        _ = _ratingService.InitializeAsync();
        _ = LoadDrivesAsync();
    }

    public ObservableCollection<FolderNode> FolderTree { get; }

    public ObservableCollection<PreviewSource> SelectedSources => _workspaceService.SelectedSources;

    public ObservableCollection<ImageFileInfo> Images { get; }

    public FilterViewModel Filter { get; }

    public int SelectedSourceCount => SelectedSources.Count;

    public int MaxSourceCount => PreviewWorkspaceService.MaxSourceCount;

    [ObservableProperty]
    private bool _includeSubfolders;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "添加最多 5 个文件夹后载入";

    [ObservableProperty]
    private ImageFileInfo? _selectedImage;

    [ObservableProperty]
    private ThumbnailSize _thumbnailSize = ThumbnailSize.Medium;

    [ObservableProperty]
    private bool _isThumbnailStripCollapsed;

    [ObservableProperty]
    private bool _isInfoDrawerOpen;

    public int PendingDeleteCount
    {
        get => _pendingDeleteCount;
        private set => SetProperty(ref _pendingDeleteCount, value);
    }

    partial void OnThumbnailSizeChanged(ThumbnailSize value)
    {
        foreach (var image in Images)
        {
            image.UpdateDisplaySize(value);
            image.ClearThumbnail();
        }
    }

    public bool AddSource(FolderNode? node)
    {
        if (node?.Folder == null || string.IsNullOrWhiteSpace(node.FullPath))
            return false;

        return AddSource(node.FullPath);
    }

    public bool AddSource(string path)
    {
        var added = _workspaceService.AddSource(path);
        if (!added)
        {
            StatusText = $"最多只能放入 {MaxSourceCount} 个文件夹";
        }
        return added;
    }

    public void RemoveSource(PreviewSource source)
    {
        _workspaceService.RemoveSource(source);
    }

    public async Task LoadPreviewAsync()
    {
        var sourcePaths = SelectedSources.Select(source => source.Path).ToArray();
        if (sourcePaths.Length == 0)
        {
            ReplaceImageList(Array.Empty<ImageFileInfo>());
            _allImages.Clear();
            _loadedSourcePaths.Clear();
            StatusText = "添加文件夹后载入";
            return;
        }

        var canAppend = _loadedSourcePaths.Count > 0 &&
            IncludeSubfolders == _loadedIncludeSubfolders &&
            _loadedSourcePaths.IsSubsetOf(sourcePaths);

        var pathsToLoad = canAppend
            ? sourcePaths.Where(path => !_loadedSourcePaths.Contains(path)).ToArray()
            : sourcePaths;

        if (pathsToLoad.Length == 0)
        {
            StatusText = "当前预览列表已是最新";
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;
        IsLoading = true;
        StatusText = canAppend ? "正在追加载入..." : "正在载入...";

        if (!canAppend)
        {
            foreach (var image in _allImages)
            {
                image.CancelThumbnailLoad();
            }
            _allImages.Clear();
            Images.Clear();
            SelectedImage = null;
            PendingDeleteCount = 0;
            _loadedSourcePaths.Clear();
            await Task.Yield();
        }

        try
        {
            var loadedCount = await LoadSourcesRoundRobinAsync(pathsToLoad, IncludeSubfolders, cancellationToken);
            foreach (var path in pathsToLoad)
            {
                _loadedSourcePaths.Add(path);
            }
            _loadedIncludeSubfolders = IncludeSubfolders;
            ApplyFilter();
            SelectedImage ??= Images.FirstOrDefault();
            StatusText = $"已载入 {_allImages.Count} 张图片";
            if (loadedCount == 0 && _allImages.Count == 0)
            {
                StatusText = "没有找到可预览图片";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "载入已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"载入失败: {ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] LoadPreviewAsync failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void TogglePendingDelete(ImageFileInfo? image)
    {
        if (image == null)
            return;

        image.IsPendingDelete = !image.IsPendingDelete;
        UpdatePendingDeleteCount();
    }

    public void TogglePendingDeleteForSelected(IEnumerable<ImageFileInfo> selectedImages)
    {
        var images = selectedImages.ToList();
        if (images.Count == 0)
            return;

        var markPending = images.Any(image => !image.IsPendingDelete);
        foreach (var image in images)
        {
            image.IsPendingDelete = markPending;
        }
        UpdatePendingDeleteCount();
    }

    public void ClearAllPendingDelete()
    {
        foreach (var image in _allImages)
        {
            image.IsPendingDelete = false;
        }
        UpdatePendingDeleteCount();
    }

    public List<ImageFileInfo> GetPendingDeleteImages()
    {
        return _allImages.Where(image => image.IsPendingDelete).ToList();
    }

    public void RemoveDeletedImages(IEnumerable<ImageFileInfo> deletedImages)
    {
        var deletedSet = deletedImages.ToHashSet();
        if (deletedSet.Count == 0)
            return;

        foreach (var deletedImage in deletedSet)
        {
            deletedImage.CancelThumbnailLoad();
            _allImages.Remove(deletedImage);
            Images.Remove(deletedImage);
        }

        if (SelectedImage != null && deletedSet.Contains(SelectedImage))
        {
            SelectedImage = Images.FirstOrDefault();
        }

        UpdatePendingDeleteCount();
    }

    public async Task LoadChildrenAsync(FolderNode node)
    {
        if (node.IsLoaded || node.IsLoading)
            return;

        node.IsLoading = true;
        node.Children.Clear();

        try
        {
            if (node.NodeType == NodeType.ThisPC || node.NodeType == NodeType.ExternalDevice)
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady)
                        continue;

                    try
                    {
                        var storageFolder = await StorageFolder.GetFolderFromPathAsync(drive.Name);
                        var driveNode = new FolderNode(storageFolder, NodeType.Drive, node)
                        {
                            Name = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                                ? drive.Name
                                : $"{drive.Name} ({drive.VolumeLabel})",
                            IsRemovable = drive.DriveType == DriveType.Removable
                        };
                        driveNode.CheckHasSubFolders();

                        var isRemovable = drive.DriveType == DriveType.Removable;
                        if ((node.NodeType == NodeType.ThisPC && !isRemovable) ||
                            (node.NodeType == NodeType.ExternalDevice && isRemovable))
                        {
                            node.Children.Add(driveNode);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CollectViewModel] Load drive failed: {ex.Message}");
                    }
                }
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] LoadChildrenAsync failed: {ex.Message}");
        }
        finally
        {
            node.IsLoading = false;
            node.RefreshExpandableState();
        }
    }

    public void ApplyFilter()
    {
        Images.Clear();
        foreach (var image in _allImages.Where(MatchFilter))
        {
            Images.Add(image);
        }

        if (SelectedImage != null && !Images.Contains(SelectedImage))
        {
            SelectedImage = Images.FirstOrDefault();
        }

        UpdatePendingDeleteCount();
    }

    public void Dispose()
    {
        _workspaceService.SourcesChanged -= WorkspaceService_SourcesChanged;
        _loadCts?.Cancel();
        foreach (var image in _allImages)
        {
            image.CancelThumbnailLoad();
        }
    }

    private async Task LoadDrivesAsync()
    {
        try
        {
            FolderTree.Add(new FolderNode(null, NodeType.ThisPC)
            {
                Name = "这台电脑",
                HasSubFolders = true
            });
            FolderTree.Add(new FolderNode(null, NodeType.ExternalDevice)
            {
                Name = "外接设备",
                HasSubFolders = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] LoadDrivesAsync failed: {ex.Message}");
        }
    }

    private async Task<int> LoadSourcesRoundRobinAsync(
        IReadOnlyList<string> sourcePaths,
        bool includeSubfolders,
        CancellationToken cancellationToken)
    {
        var states = new List<SourceLoadState>();
        foreach (var path in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(path).AsTask(cancellationToken);
                var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, ImageExtensions.ToList())
                {
                    IndexerOption = IndexerOption.DoNotUseIndexer,
                    FolderDepth = includeSubfolders ? FolderDepth.Deep : FolderDepth.Shallow
                };
                states.Add(new SourceLoadState(path, folder.CreateFileQueryWithOptions(queryOptions)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectViewModel] Source open failed {path}: {ex.Message}");
            }
        }

        var totalAdded = 0;
        var batchSize = (uint)Math.Max(1, _settingsService.BatchSize);

        while (states.Any(state => !state.IsComplete))
        {
            foreach (var state in states.Where(state => !state.IsComplete).ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requestBatchSize = state.Index == 0 ? Math.Min(batchSize, FirstBatchSize) : batchSize;
                var batch = await state.Query.GetFilesAsync(state.Index, requestBatchSize).AsTask(cancellationToken);
                if (batch.Count == 0)
                {
                    state.IsComplete = true;
                    continue;
                }

                state.Index += (uint)batch.Count;
                var added = AddFilesFromSourceBatch(state, batch, cancellationToken);
                totalAdded += added;

                if (added > 0)
                {
                    ApplyFilter();
                    SelectedImage ??= Images.FirstOrDefault();
                    StatusText = $"已载入 {_allImages.Count} 张图片";
                    await Task.Yield();
                }
            }
        }

        return totalAdded;
    }

    private int AddFilesFromSourceBatch(
        SourceLoadState state,
        IReadOnlyList<StorageFile> batch,
        CancellationToken cancellationToken)
    {
        var newGroupKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in batch)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var groupKey = CreateGroupKey(state.SourcePath, file);
            if (!state.FileNameMap.TryGetValue(groupKey, out var list))
            {
                list = new List<StorageFile>();
                state.FileNameMap[groupKey] = list;
            }
            list.Add(file);

            if (!state.ProcessedGroups.Contains(groupKey))
            {
                newGroupKeys.Add(groupKey);
            }
        }

        var addedCount = 0;
        foreach (var groupKey in newGroupKeys)
        {
            if (!state.FileNameMap.TryGetValue(groupKey, out var groupFiles))
                continue;

            var sortedFiles = groupFiles
                .OrderBy(file => ImageGroup.GetFormatPriority(file.FileType))
                .ToList();
            if (sortedFiles.Count == 0)
                continue;

            var imageInfos = sortedFiles
                .Select(CreatePlaceholderImageInfo)
                .ToList();
            var group = new ImageGroup(groupKey, imageInfos);
            group.PrimaryImage.UpdateDisplaySize(ThumbnailSize);

            _allImages.Add(group.PrimaryImage);
            StartDeferredImageInfoLoad(group.PrimaryImage, cancellationToken);
            state.ProcessedGroups.Add(groupKey);
            addedCount++;
        }

        return addedCount;
    }

    private static string CreateGroupKey(string sourcePath, StorageFile file)
    {
        var folderPath = string.IsNullOrWhiteSpace(file.Path)
            ? sourcePath
            : Path.GetDirectoryName(file.Path) ?? sourcePath;
        return $"{NormalizePath(folderPath)}|{ImageGroup.GetGroupName(file.Name)}";
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

            if (!cancellationToken.IsCancellationRequested)
            {
                await imageInfo.LoadRatingAsync(_ratingService);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] deferred info load failed: {ex.Message}");
        }
    }

    private static async Task HydrateImageMetadataAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await imageInfo.ImageFile.Properties.GetImagePropertiesAsync().AsTask(cancellationToken);
            var width = (int)Math.Max(properties.Width, 200);
            var height = (int)Math.Max(properties.Height, 200);
            var orientation = properties.Orientation;
            if (orientation == Windows.Storage.FileProperties.PhotoOrientation.Rotate90 ||
                orientation == Windows.Storage.FileProperties.PhotoOrientation.Rotate270 ||
                orientation == Windows.Storage.FileProperties.PhotoOrientation.Transpose ||
                orientation == Windows.Storage.FileProperties.PhotoOrientation.Transverse)
            {
                (width, height) = (height, width);
            }
            imageInfo.UpdateMetadata(width, height, properties.Title);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] metadata load failed: {ex.Message}");
        }
    }

    private void WorkspaceService_SourcesChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SelectedSourceCount));
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        ApplyFilter();
    }

    private bool MatchFilter(ImageFileInfo image)
    {
        return MatchFileType(image) && MatchRating(image) && MatchPendingDelete(image);
    }

    private bool MatchPendingDelete(ImageFileInfo image)
    {
        return !Filter.IsPendingDeleteFilter || image.IsPendingDelete;
    }

    private bool MatchFileType(ImageFileInfo image)
    {
        if (!Filter.IsImageFilter && !Filter.IsRawFilter)
            return true;

        var ext = Path.GetExtension(image.ImageFile.Path);
        var isRaw = FilterViewModel.IsRawExtension(ext);

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
        return Filter.RatingMode switch
        {
            RatingFilterMode.All => true,
            RatingFilterMode.NoRating => image.Rating == 0,
            RatingFilterMode.HasRating => MatchRatingCondition(ImageFileInfo.RatingToStars(image.Rating)),
            _ => true
        };
    }

    private bool MatchRatingCondition(int stars)
    {
        return Filter.RatingCondition switch
        {
            RatingCondition.Equals => stars == Filter.RatingStars,
            RatingCondition.GreaterOrEqual => stars >= Filter.RatingStars,
            RatingCondition.LessOrEqual => stars <= Filter.RatingStars,
            _ => true
        };
    }

    private void ReplaceImageList(IEnumerable<ImageFileInfo> images)
    {
        _allImages.Clear();
        _allImages.AddRange(images);
        ApplyFilter();
    }

    private void UpdatePendingDeleteCount()
    {
        PendingDeleteCount = _allImages.Count(image => image.IsPendingDelete);
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed class SourceLoadState
    {
        public SourceLoadState(string sourcePath, StorageFileQueryResult query)
        {
            SourcePath = NormalizePath(sourcePath);
            Query = query;
        }

        public string SourcePath { get; }

        public StorageFileQueryResult Query { get; }

        public uint Index { get; set; }

        public bool IsComplete { get; set; }

        public Dictionary<string, List<StorageFile>> FileNameMap { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ProcessedGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
