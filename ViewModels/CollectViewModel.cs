using CommunityToolkit.Mvvm.ComponentModel;
using PhotoView.Contracts.Services;
using PhotoView.Helpers;
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
    private readonly FolderTreeService _folderTreeService;
    private readonly SemaphoreSlim _metadataHydrationGate = new(2);
    private readonly object _metadataHydrationQueueLock = new();
    private readonly List<ImageFileInfo> _allImages = new();
    private readonly HashSet<ImageFileInfo> _metadataHydrationQueued = new();
    private readonly HashSet<string> _loadedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _metadataCts;
    private DateTime _lastLoadStatusUpdateUtc = DateTime.MinValue;
    private int _pendingDeleteCount;
    private bool _loadedIncludeSubfolders;

    public CollectViewModel(
        PreviewWorkspaceService workspaceService,
        ISettingsService settingsService,
        RatingService ratingService,
        FolderTreeService folderTreeService)
    {
        _workspaceService = workspaceService;
        _settingsService = settingsService;
        _ratingService = ratingService;
        _folderTreeService = folderTreeService;

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

    partial void OnSelectedImageChanged(ImageFileInfo? value)
    {
        DebugSelection($"SelectedImage changed to {GetDebugName(value)}");
    }

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

    public async Task PinFolderAsync(FolderNode? node)
    {
        await _folderTreeService.PinFolderAsync(node, GetFavoritesRootNode());
    }

    public async Task UnpinFolderAsync(FolderNode? node)
    {
        await _folderTreeService.UnpinFolderAsync(node, GetFavoritesRootNode());
    }

    public async Task RefreshExternalDevicesAsync()
    {
        var externalDevicesRoot = FolderTree.FirstOrDefault(node => node.NodeType == NodeType.ExternalDevice);
        if (externalDevicesRoot != null)
        {
            await _folderTreeService.RefreshExternalDevicesAsync(externalDevicesRoot);
        }
    }

    public async Task LoadPreviewAsync()
    {
        var sourcePaths = SelectedSources.Select(source => source.Path).ToArray();
        if (sourcePaths.Length == 0)
        {
            ResetMetadataHydrationQueue();
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

        var metadataCancellationToken = canAppend
            ? EnsureMetadataHydrationQueue()
            : ResetMetadataHydrationQueue();

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
            var loadedCount = await LoadSourcesRoundRobinAsync(
                pathsToLoad,
                IncludeSubfolders,
                cancellationToken,
                metadataCancellationToken);
            foreach (var path in pathsToLoad)
            {
                _loadedSourcePaths.Add(path);
            }
            _loadedIncludeSubfolders = IncludeSubfolders;
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
        await _folderTreeService.LoadChildrenAsync(node);
    }

    public void ApplyFilter()
    {
        DebugLoad($"ApplyFilter reset begin all={_allImages.Count} visibleBefore={Images.Count}");
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
        DebugLoad($"ApplyFilter reset complete visibleAfter={Images.Count} selected={GetDebugName(SelectedImage)}");
    }

    public void Dispose()
    {
        _workspaceService.SourcesChanged -= WorkspaceService_SourcesChanged;
        _loadCts?.Cancel();
        _metadataCts?.Cancel();
        foreach (var image in _allImages)
        {
            image.CancelThumbnailLoad();
        }
        ClearMetadataHydrationQueue();
    }

    private async Task LoadDrivesAsync()
    {
        try
        {
            var roots = await _folderTreeService.CreateRootNodesAsync();
            FolderTree.Clear();
            FolderTree.Add(roots.FavoritesRoot);
            FolderTree.Add(roots.ThisPc);
            FolderTree.Add(roots.ExternalDevices);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] LoadDrivesAsync failed: {ex.Message}");
        }
    }

    private FolderNode? GetFavoritesRootNode()
    {
        return FolderTree.FirstOrDefault(node => node.NodeType == NodeType.FavoritesRoot);
    }

    private async Task<int> LoadSourcesRoundRobinAsync(
        IReadOnlyList<string> sourcePaths,
        bool includeSubfolders,
        CancellationToken cancellationToken,
        CancellationToken metadataCancellationToken)
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
                var addedImages = AddFilesFromSourceBatch(state, batch, cancellationToken);
                totalAdded += addedImages.Count;

                if (addedImages.Count > 0)
                {
                    var appendResult = AppendImagesForCurrentFilter(addedImages, cancellationToken);
                    foreach (var imageInfo in addedImages)
                    {
                        StartImageInfoLoad(
                            imageInfo,
                            metadataCancellationToken,
                            immediate: ReferenceEquals(imageInfo, appendResult.InitialSelection));
                    }

                    UpdateLoadStatus(force: false);
                    DebugLoad(
                        $"Batch appended source={state.SourcePath} files={batch.Count} new={addedImages.Count} visibleAdded={appendResult.VisibleAdded} total={_allImages.Count} visible={Images.Count} selected={GetDebugName(SelectedImage)}");
                    await Task.Yield();
                }
            }
        }

        return totalAdded;
    }

    private List<ImageFileInfo> AddFilesFromSourceBatch(
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

        var addedImages = new List<ImageFileInfo>();
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
            addedImages.Add(group.PrimaryImage);
            state.ProcessedGroups.Add(groupKey);
        }

        return addedImages;
    }

    private (int VisibleAdded, ImageFileInfo? InitialSelection) AppendImagesForCurrentFilter(
        IReadOnlyList<ImageFileInfo> images,
        CancellationToken cancellationToken)
    {
        ImageFileInfo? initialSelection = null;
        var visibleAdded = 0;

        foreach (var image in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchFilter(image))
                continue;

            Images.Add(image);
            visibleAdded++;
            if (SelectedImage == null && initialSelection == null)
            {
                initialSelection = image;
            }
        }

        if (initialSelection != null)
        {
            SelectedImage = initialSelection;
            DebugLoad($"Initial selection set image={GetDebugName(initialSelection)}");
        }

        return (visibleAdded, initialSelection);
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

    private void StartImageInfoLoad(ImageFileInfo imageInfo, CancellationToken cancellationToken, bool immediate)
    {
        lock (_metadataHydrationQueueLock)
        {
            if (!_metadataHydrationQueued.Add(imageInfo))
            {
                DebugLoad($"Metadata load already queued image={GetDebugName(imageInfo)} immediate={immediate}");
                return;
            }
        }

        _ = StartImageInfoLoadAsync(imageInfo, cancellationToken, immediate);
    }

    private async Task StartImageInfoLoadAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken, bool immediate)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(DeferredImageInfoLoadDelayMs, cancellationToken);
            }

            await _metadataHydrationGate.WaitAsync(cancellationToken);
            try
            {
                DebugLoad($"Metadata load begin image={GetDebugName(imageInfo)} immediate={immediate}");
                await HydrateImageMetadataAsync(imageInfo, cancellationToken);
                DebugLoad($"Metadata load complete image={GetDebugName(imageInfo)} size={imageInfo.Width}x{imageInfo.Height}");
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
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] deferred info load failed: {ex.Message}");
        }
    }

    private static async Task HydrateImageMetadataAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
    {
        try
        {
            var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(imageInfo.ImageFile, cancellationToken);

            if (imageInfo.Width <= 200 || imageInfo.Height <= 200)
            {
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

            imageInfo.SetRatingFromProperties(properties.Rating, RatingSource.WinRT);

            if (properties.DateTaken != DateTimeOffset.MinValue && properties.DateTaken != default)
            {
                imageInfo.SetDateTakenFromProperties(properties.DateTaken.DateTime);
            }
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
        ClearMetadataHydrationQueue();
        _allImages.AddRange(images);
        ApplyFilter();
    }

    private CancellationToken EnsureMetadataHydrationQueue()
    {
        _metadataCts ??= new CancellationTokenSource();
        return _metadataCts.Token;
    }

    private CancellationToken ResetMetadataHydrationQueue()
    {
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataCts = new CancellationTokenSource();
        ClearMetadataHydrationQueue();
        return _metadataCts.Token;
    }

    private void UpdateLoadStatus(bool force)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastLoadStatusUpdateUtc < TimeSpan.FromMilliseconds(150))
            return;

        _lastLoadStatusUpdateUtc = now;
        StatusText = $"已载入 {_allImages.Count} 张图片";
    }

    private void UpdatePendingDeleteCount()
    {
        PendingDeleteCount = _allImages.Count(image => image.IsPendingDelete);
    }

    private void ClearMetadataHydrationQueue()
    {
        lock (_metadataHydrationQueueLock)
        {
            _metadataHydrationQueued.Clear();
        }
    }

    private static string NormalizePath(string path)
    {
        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string GetDebugName(ImageFileInfo? imageInfo)
    {
        if (imageInfo == null)
            return "<null>";

        return string.IsNullOrWhiteSpace(imageInfo.ImageFile?.Name)
            ? imageInfo.ImageName
            : imageInfo.ImageFile.Name;
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void DebugSelection(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[CollectSelection] {DateTime.Now:HH:mm:ss.fff} {message}");
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void DebugLoad(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[CollectLoad] {DateTime.Now:HH:mm:ss.fff} {message}");
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
