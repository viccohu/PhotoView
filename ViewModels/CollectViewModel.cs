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

public enum DualPageMode
{
    Compare,
    Continuous
}

public enum PreviewPageSlot
{
    Left,
    Right
}

public partial class CollectViewModel : ObservableRecipient, IDisposable
{
    public readonly record struct LoadPreviewResult(
        bool StartedLoading,
        bool CompletedSuccessfully,
        bool LoadedAnyFiles,
        bool AutoCollapseDrawer);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".psd", ".psb",
        ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".sr2", ".raf",
        ".orf", ".rw2", ".pef", ".dng", ".srw", ".raw", ".iiq", ".3fr",
        ".mef", ".mos", ".x3f", ".erf", ".dcr", ".kdc"
    };

    private const uint FirstBatchSize = 30;
    private const int DeferredImageInfoLoadDelayMs = 400;
    private const int CompletedLoadProgressHoldMs = 450;
    private const double BurstGroupingWindowSeconds = 2d;
    private readonly PreviewWorkspaceService _workspaceService;
    private readonly ISettingsService _settingsService;
    private readonly RatingService _ratingService;
    private readonly FolderTreeService _folderTreeService;
    private readonly IExternalDeviceWatcherService _externalDeviceWatcherService;
    private readonly SemaphoreSlim _metadataHydrationGate = new(2);
    private readonly object _metadataHydrationQueueLock = new();
    private readonly List<ImageFileInfo> _allImages = new();
    private readonly List<BurstPhotoGroup> _burstGroups = new();
    private readonly HashSet<ImageFileInfo> _metadataHydrationQueued = new();
    private readonly HashSet<string> _loadedSourcePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _loadedSourceIncludeSubfolders = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _metadataCts;
    private DateTime _lastLoadStatusUpdateUtc = DateTime.MinValue;
    private int _pendingDeleteCount;
    private int _filteredPhotoCount;

    public CollectViewModel(
        PreviewWorkspaceService workspaceService,
        ISettingsService settingsService,
        RatingService ratingService,
        FolderTreeService folderTreeService,
        IExternalDeviceWatcherService externalDeviceWatcherService)
    {
        _workspaceService = workspaceService;
        _settingsService = settingsService;
        _settingsService.PreferPsdAsPrimaryPreviewChanged += OnPreferPsdAsPrimaryPreviewChanged;
        _ratingService = ratingService;
        _folderTreeService = folderTreeService;
        _externalDeviceWatcherService = externalDeviceWatcherService;
        _externalDeviceWatcherService.ExternalDevicesChanged += OnExternalDevicesChanged;

        FolderTree = new ObservableCollection<FolderNode>();
        Images = new ObservableCollection<ImageFileInfo>();
        Filter = new FilterViewModel();
        Filter.FilterChanged += OnFilterChanged;
        UpdateFilteredPhotoCount();
        _workspaceService.SourcesChanged += WorkspaceService_SourcesChanged;
        _ = _ratingService.InitializeAsync();
        _ = LoadDrivesAsync();
    }

    public ObservableCollection<FolderNode> FolderTree { get; }

    public ObservableCollection<PreviewSource> SelectedSources => _workspaceService.SelectedSources;

    public ObservableCollection<ImageFileInfo> Images { get; }

    public FilterViewModel Filter { get; }

    public bool IsFilterActive => Filter.IsFilterActive;

    public int SelectedSourceCount => SelectedSources.Count;

    public int MaxSourceCount => PreviewWorkspaceService.MaxSourceCount;

    public int FilteredPhotoCount
    {
        get => _filteredPhotoCount;
        private set => SetProperty(ref _filteredPhotoCount, value);
    }

    [ObservableProperty]
    private bool _includeSubfolders;

    partial void OnIncludeSubfoldersChanged(bool value)
    {
        foreach (var source in SelectedSources)
        {
            source.IncludeSubfolders = value;
        }

        RefreshPreviewLoadState();
    }

    [ObservableProperty]
    private bool _hasLoadedPreview;

    partial void OnHasLoadedPreviewChanged(bool value)
    {
        RefreshPreviewLoadState();
    }

    [ObservableProperty]
    private CollectPreviewLoadState _previewLoadState = CollectPreviewLoadState.Load;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _loadProgressValue;

    [ObservableProperty]
    private bool _isLoadProgressIndeterminate;

    [ObservableProperty]
    private string _statusText = string.Format("CollectPage_Status_AddUpToFolders".GetLocalized(), PreviewWorkspaceService.MaxSourceCount);

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

    [ObservableProperty]
    private bool _isDualPageMode;

    [ObservableProperty]
    private DualPageMode _dualPageMode = DualPageMode.Compare;

    [ObservableProperty]
    private PreviewPageSlot _focusedPageSlot = PreviewPageSlot.Left;

    [ObservableProperty]
    private ImageFileInfo? _leftPageImage;

    [ObservableProperty]
    private ImageFileInfo? _rightPageImage;

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
            image.InvalidateThumbnailForSizeChange();
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
        var added = _workspaceService.AddSource(path, IncludeSubfolders);
        if (!added)
        {
            StatusText = string.Format("CollectPage_Status_MaxSources".GetLocalized(), MaxSourceCount);
        }
        return added;
    }

    public void RemoveSource(PreviewSource source)
    {
        _workspaceService.RemoveSource(source);
    }

    public void RefreshPreviewLoadState()
    {
        PreviewLoadState = CollectPreviewLoadStateEvaluator.Determine(
            HasLoadedPreview,
            SelectedSources.ToList(),
            _loadedSourcePaths,
            _loadedSourceIncludeSubfolders);
    }

    public async Task PinFolderAsync(FolderNode? node)
    {
        await _folderTreeService.PinFolderAsync(node, GetFavoritesRootNode());
    }

    public bool IsFolderPinned(FolderNode? node)
    {
        return _folderTreeService.IsFolderPinned(node);
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

    private void OnExternalDevicesChanged(object? sender, EventArgs e)
    {
        var dispatcher = App.MainWindow.DispatcherQueue;
        if (dispatcher == null)
            return;

        dispatcher.TryEnqueue(async () =>
        {
            if (AppLifetime.IsShuttingDown)
                return;

            var externalDevicesRoot = FolderTree.FirstOrDefault(node => node.NodeType == NodeType.ExternalDevice);
            if (externalDevicesRoot == null)
                return;

            if (!externalDevicesRoot.IsLoaded)
            {
                externalDevicesRoot.HasSubFolders = true;
                externalDevicesRoot.RefreshExpandableState();
                return;
            }

            await RefreshExternalDevicesAsync();
        });
    }

    public async Task<LoadPreviewResult> LoadPreviewAsync(bool forceRefresh = false)
    {
        ResetLoadProgress();
        var sourceRequests = SelectedSources
            .Select(source => new SourceLoadRequest(source.Path, source.IncludeSubfolders))
            .ToArray();
        if (sourceRequests.Length == 0)
        {
            ResetMetadataHydrationQueue();
            ReplaceImageList(Array.Empty<ImageFileInfo>());
            _allImages.Clear();
            ClearBurstGroups();
            UpdateFilteredPhotoCount();
            _loadedSourcePaths.Clear();
            _loadedSourceIncludeSubfolders.Clear();
            HasLoadedPreview = false;
            RefreshPreviewLoadState();
            StatusText = "CollectPage_Status_AddFolder".GetLocalized();
            return new LoadPreviewResult(false, false, false, false);
        }

        var canAppend = !forceRefresh &&
            _loadedSourcePaths.Count > 0 &&
            IsLoadedSourceConfigurationSubsetOf(sourceRequests);

        var sourcesToLoad = canAppend
            ? sourceRequests.Where(source => !_loadedSourcePaths.Contains(source.Path)).ToArray()
            : sourceRequests;

        if (sourcesToLoad.Length == 0)
        {
            StatusText = "CollectPage_Status_UpToDate".GetLocalized();
            return new LoadPreviewResult(false, false, false, false);
        }

        var metadataCancellationToken = canAppend
            ? EnsureMetadataHydrationQueue()
            : ResetMetadataHydrationQueue();

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;
        var currentLoadCts = _loadCts;
        var completedSuccessfully = false;
        IsLoading = true;
        IsLoadProgressIndeterminate = false;
        LoadProgressValue = 0d;
        StatusText = (canAppend ? "CollectPage_Status_Appending" : "CollectPage_Status_Loading").GetLocalized();

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
            UpdateFilteredPhotoCount();
            _loadedSourcePaths.Clear();
            _loadedSourceIncludeSubfolders.Clear();
            await Task.Yield();
        }

        try
        {
            var loadedCount = await LoadSourcesRoundRobinAsync(
                sourcesToLoad,
                cancellationToken,
                metadataCancellationToken);

            foreach (var source in sourcesToLoad)
            {
                _loadedSourcePaths.Add(source.Path);
                _loadedSourceIncludeSubfolders[source.Path] = source.IncludeSubfolders;
            }

            SelectedImage ??= Images.FirstOrDefault();
            UpdateLoadProgress(100d, isIndeterminate: false);
            completedSuccessfully = true;
            HasLoadedPreview = true;
            RefreshPreviewLoadState();
            StatusText = string.Format("CollectPage_Status_LoadedImages".GetLocalized(), _allImages.Count);
            if (loadedCount == 0 && _allImages.Count == 0)
            {
                StatusText = "CollectPage_Status_NoPreviewImages".GetLocalized();
            }

            return new LoadPreviewResult(true, true, loadedCount > 0, true);
        }
        catch (OperationCanceledException)
        {
            StatusText = "CollectPage_Status_Canceled".GetLocalized();
            return new LoadPreviewResult(true, false, false, false);
        }
        catch (Exception ex)
        {
            StatusText = string.Format("CollectPage_Status_Failed".GetLocalized(), ex.Message);
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] LoadPreviewAsync failed: {ex}");
            return new LoadPreviewResult(true, false, false, false);
        }
        finally
        {
            if (completedSuccessfully)
            {
                UpdateLoadProgress(100d, isIndeterminate: false);
                await Task.Delay(CompletedLoadProgressHoldMs);
            }

            if (ReferenceEquals(_loadCts, currentLoadCts))
            {
                IsLoading = false;
                ResetLoadProgress();
            }
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

    public List<ImageFileInfo> GetLoadedExportImages()
    {
        return _allImages.Where(image => !image.IsPendingDelete).ToList();
    }

    public List<ImageFileInfo> GetFilteredExportImages()
    {
        var countedGroups = new HashSet<ImageGroup>();
        var filteredImages = new List<ImageFileInfo>();

        foreach (var image in _allImages)
        {
            if (image.Group is { } group && !countedGroups.Add(group))
                continue;

            filteredImages.AddRange(GetMatchingGroupFiles(image));
        }

        return filteredImages
            .Where(image => !image.IsPendingDelete)
            .ToList();
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

        RebuildBurstGroups();

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
        UpdateFilteredPhotoCount();
        DebugLoad($"ApplyFilter reset complete visibleAfter={Images.Count} selected={GetDebugName(SelectedImage)}");
    }

    public void Dispose()
    {
        _settingsService.PreferPsdAsPrimaryPreviewChanged -= OnPreferPsdAsPrimaryPreviewChanged;
        _externalDeviceWatcherService.ExternalDevicesChanged -= OnExternalDevicesChanged;
        _workspaceService.SourcesChanged -= WorkspaceService_SourcesChanged;
        _loadCts?.Cancel();
        _metadataCts?.Cancel();
        foreach (var image in _allImages)
        {
            image.CancelThumbnailLoad();
        }
        Filter.FilterChanged -= OnFilterChanged;
        ClearMetadataHydrationQueue();
    }

    private async Task LoadDrivesAsync()
    {
        try
        {
            var thisPcNode = await _folderTreeService.CreateRootNodesAsync();
            FolderTree.Clear();
            FolderTree.Add(thisPcNode);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CollectViewModel] LoadDrivesAsync failed: {ex.Message}");
        }
    }

    private FolderNode? GetFavoritesRootNode()
    {
        var thisPcNode = FolderTree.FirstOrDefault(node => node.NodeType == NodeType.ThisPC);
        return FolderTreeService.GetFavoritesRootFromThisPc(thisPcNode);
    }

    private bool IsLoadedSourceConfigurationSubsetOf(IReadOnlyList<SourceLoadRequest> sourceRequests)
    {
        foreach (var loadedPath in _loadedSourcePaths)
        {
            if (!_loadedSourceIncludeSubfolders.TryGetValue(loadedPath, out var loadedIncludeSubfolders))
            {
                return false;
            }

            var matchingSource = sourceRequests.FirstOrDefault(source =>
                string.Equals(source.Path, loadedPath, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(matchingSource.Path) ||
                matchingSource.IncludeSubfolders != loadedIncludeSubfolders)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<int> LoadSourcesRoundRobinAsync(
        IReadOnlyList<SourceLoadRequest> sourceRequests,
        CancellationToken cancellationToken,
        CancellationToken metadataCancellationToken)
    {
        var states = new List<SourceLoadState>();
        foreach (var source in sourceRequests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(source.Path).AsTask(cancellationToken);
                var queryOptions = new QueryOptions(CommonFileQuery.OrderByName, ImageExtensions.ToList())
                {
                    IndexerOption = IndexerOption.DoNotUseIndexer,
                    FolderDepth = source.IncludeSubfolders ? FolderDepth.Deep : FolderDepth.Shallow
                };
                states.Add(new SourceLoadState(source.Path, folder.CreateFileQueryWithOptions(queryOptions)));
                UpdateSourceInitializationProgress(states.Count, sourceRequests.Count);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CollectViewModel] Source open failed {source.Path}: {ex.Message}");
            }
        }

        if (states.Count > 0)
        {
            UpdateLoadProgress(25d, isIndeterminate: false);
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
                state.LoadedBatchCount++;
                var addedImages = AddFilesFromSourceBatch(state, batch, cancellationToken);
                totalAdded += addedImages.Count;

                if (addedImages.Count > 0)
                {
                    RebuildBurstGroups();
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

                UpdateRoundRobinProgress(states);
            }
        }

        UpdateLoadProgress(90d, isIndeterminate: false);
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
                .OrderBy(file => ImageGroup.GetFormatPriority(file.FileType, _settingsService.PreferPsdAsPrimaryPreview))
                .ToList();
            if (sortedFiles.Count == 0)
                continue;

            var imageInfos = sortedFiles
                .Select(CreatePlaceholderImageInfo)
                .ToList();
            var group = new ImageGroup(groupKey, imageInfos, _settingsService.PreferPsdAsPrimaryPreview);
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

        UpdateFilteredPhotoCount();
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
                RefreshBurstGroupsAfterMetadataLoad();
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

            if ((imageInfo.Width <= 200 || imageInfo.Height <= 200) &&
                ImageFormatRegistry.IsPhotoshop(imageInfo.ImageFile.FileType))
            {
                var photoshopSize = await PhotoshopImageInfoReader.TryReadSizeAsync(imageInfo.ImageFile, cancellationToken);
                if (photoshopSize.HasValue)
                {
                    imageInfo.UpdateMetadata(photoshopSize.Value.Width, photoshopSize.Value.Height, properties.Title);
                }
            }

            imageInfo.SetRatingFromProperties(properties.Rating, RatingSource.WinRT);

            var dateTaken = ImageMetadataDateHelper.NormalizeDateTaken(properties.DateTaken, imageInfo.FileType);
            if (dateTaken.HasValue)
            {
                imageInfo.SetDateTakenFromProperties(dateTaken);
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
        RefreshPreviewLoadState();
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsFilterActive));
        ApplyFilter();
    }

    private void OnPreferPsdAsPrimaryPreviewChanged(object? sender, bool enabled)
    {
        var groups = _allImages
            .Select(image => image.Group)
            .Where(group => group != null)
            .Distinct()
            .Cast<ImageGroup>()
            .ToList();

        if (groups.Count == 0)
            return;

        var cancellationToken = _metadataCts?.Token ?? CancellationToken.None;
        _allImages.Clear();
        ClearMetadataHydrationQueue();

        foreach (var group in groups)
        {
            group.ReapplyPrimary(enabled);
            group.PrimaryImage.UpdateDisplaySize(ThumbnailSize);
            _allImages.Add(group.PrimaryImage);
            StartImageInfoLoad(group.PrimaryImage, cancellationToken, immediate: false);
        }

        RebuildBurstGroups();
        ApplyFilter();
    }

    private void RebuildBurstGroups()
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

    private void RefreshBurstGroupsAfterMetadataLoad()
    {
        var dispatcher = App.MainWindow.DispatcherQueue;
        if (dispatcher.HasThreadAccess)
        {
            RebuildBurstGroups();
            return;
        }

        dispatcher.TryEnqueue(RebuildBurstGroups);
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

    private void UpdateFilteredPhotoCount()
    {
        FilteredPhotoCount = CountMatchingFiles(_allImages);
    }

    private int CountMatchingFiles(IEnumerable<ImageFileInfo> images)
    {
        var countedGroups = new HashSet<ImageGroup>();
        var total = 0;

        foreach (var image in images)
        {
            if (image.Group is { } group)
            {
                if (!countedGroups.Add(group))
                    continue;

                total += GetMatchingGroupFiles(image).Count;
                continue;
            }

            total += GetMatchingGroupFiles(image).Count;
        }

        return total;
    }

    private bool MatchFilter(ImageFileInfo image)
    {
        return HasAnyMatchingFile(image);
    }

    private bool HasAnyMatchingFile(ImageFileInfo image)
    {
        return GetMatchingGroupFiles(image).Count > 0;
    }

    private List<ImageFileInfo> GetMatchingGroupFiles(ImageFileInfo image)
    {
        if (!MatchBurst(image))
            return new List<ImageFileInfo>();

        return GetFormatMatchedFiles(image)
            .Where(MatchNonFormatFilters)
            .ToList();
    }

    private IEnumerable<ImageFileInfo> GetSemanticFiles(ImageFileInfo image)
    {
        return image.Group?.Images ?? Enumerable.Repeat(image, 1);
    }

    private bool MatchNonFormatFilters(ImageFileInfo image)
    {
        return MatchRating(image) && MatchPendingDelete(image);
    }

    private IEnumerable<ImageFileInfo> GetFormatMatchedFiles(ImageFileInfo image)
    {
        var semanticFiles = GetSemanticFiles(image).ToList();
        if (semanticFiles.Count == 0)
            return Enumerable.Empty<ImageFileInfo>();

        if (!HasActiveFormatFilter())
            return semanticFiles;

        var imageFiles = semanticFiles.Where(candidate => !IsRawCandidate(candidate)).ToList();
        var rawFiles = semanticFiles.Where(IsRawCandidate).ToList();
        var hasImageFiles = imageFiles.Count > 0;
        var hasRawFiles = rawFiles.Count > 0;
        var isDualFormatGroup = hasImageFiles && hasRawFiles;
        var isImageOnlyGroup = hasImageFiles && !hasRawFiles;
        var isRawOnlyGroup = hasRawFiles && !hasImageFiles;
        var matchedFiles = new HashSet<ImageFileInfo>();

        if (Filter.IsImageFilter)
        {
            foreach (var candidate in Filter.IsImageSingleOnlyFilter
                         ? isImageOnlyGroup ? imageFiles : Enumerable.Empty<ImageFileInfo>()
                         : imageFiles)
            {
                matchedFiles.Add(candidate);
            }
        }

        if (Filter.IsRawFilter)
        {
            foreach (var candidate in Filter.IsRawSingleOnlyFilter
                         ? isRawOnlyGroup ? rawFiles : Enumerable.Empty<ImageFileInfo>()
                         : rawFiles)
            {
                matchedFiles.Add(candidate);
            }
        }

        if (Filter.IsDualFormatFilter)
        {
            var dualCandidates = Filter.IsDualFormatInverseFilter
                ? !isDualFormatGroup ? semanticFiles : Enumerable.Empty<ImageFileInfo>()
                : isDualFormatGroup ? semanticFiles : Enumerable.Empty<ImageFileInfo>();

            foreach (var candidate in dualCandidates)
            {
                matchedFiles.Add(candidate);
            }
        }

        return matchedFiles;
    }

    private bool HasActiveFormatFilter()
    {
        return Filter.IsImageFilter ||
               Filter.IsRawFilter ||
               Filter.IsDualFormatFilter;
    }

    private bool MatchPendingDelete(ImageFileInfo image)
    {
        if (!Filter.IsPendingDeleteFilter)
            return true;

        return image.IsPendingDelete;
    }

    private bool MatchBurst(ImageFileInfo image)
    {
        if (!Filter.IsBurstFilter)
            return true;

        return image.BurstGroup?.Images.Count > 1;
    }

    private static bool IsRawCandidate(ImageFileInfo image)
    {
        var path = image.ImageFile?.Path;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return FilterViewModel.IsRawExtension(Path.GetExtension(path));
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

    private void ReplaceImageList(IEnumerable<ImageFileInfo> images)
    {
        _allImages.Clear();
        ClearMetadataHydrationQueue();
        _allImages.AddRange(images);
        RebuildBurstGroups();
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
        StatusText = string.Format("CollectPage_Status_LoadedImages".GetLocalized(), _allImages.Count);
    }

    private void UpdateSourceInitializationProgress(int initializedCount, int totalCount)
    {
        if (totalCount <= 0)
            return;

        var ratio = Math.Clamp(initializedCount / (double)totalCount, 0d, 1d);
        var progress = 10d + (15d * ratio);
        UpdateLoadProgress(progress, isIndeterminate: initializedCount == 0);
    }

    private void UpdateRoundRobinProgress(IReadOnlyList<SourceLoadState> states)
    {
        if (states.Count == 0)
            return;

        var completedCount = states.Count(state => state.IsComplete);
        var activeContribution = states
            .Where(state => !state.IsComplete && state.LoadedBatchCount > 0)
            .Sum(state => 0.9d * (1d - (1d / (state.LoadedBatchCount + 1d))));
        var activeRatio = Math.Clamp((completedCount + activeContribution) / states.Count, 0d, 1d);
        var progress = 25d + (60d * activeRatio);
        UpdateLoadProgress(progress, isIndeterminate: false);
    }

    private void UpdateLoadProgress(double value, bool isIndeterminate)
    {
        if (!isIndeterminate)
        {
            LoadProgressValue = Math.Clamp(Math.Max(LoadProgressValue, value), 0d, 100d);
        }

        IsLoadProgressIndeterminate = isIndeterminate;
    }

    private void ResetLoadProgress()
    {
        LoadProgressValue = 0d;
        IsLoadProgressIndeterminate = false;
    }
    private void UpdatePendingDeleteCount()
    {
        PendingDeleteCount = _allImages.Count(image => image.IsPendingDelete);
        UpdateFilteredPhotoCount();
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
        //System.Diagnostics.Debug.WriteLine($"[CollectSelection] {DateTime.Now:HH:mm:ss.fff} {message}");
    }

    [System.Diagnostics.Conditional("DEBUG")]
    private static void DebugLoad(string message)
    {
        //System.Diagnostics.Debug.WriteLine($"[CollectLoad] {DateTime.Now:HH:mm:ss.fff} {message}");
    }

    private readonly record struct SourceLoadRequest(string Path, bool IncludeSubfolders);

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

        public int LoadedBatchCount { get; set; }

        public bool IsComplete { get; set; }

        public Dictionary<string, List<StorageFile>> FileNameMap { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ProcessedGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
