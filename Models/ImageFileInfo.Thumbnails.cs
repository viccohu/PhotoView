using Microsoft.UI.Xaml.Media;
using PhotoView.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoView.Models;

public partial class ImageFileInfo
{
    private static readonly SemaphoreSlim _globalThumbnailLoadSemaphore = new(8);

    private ImageSource? _thumbnail;
    private ImageSource? _fastPreviewThumbnail;
    private int _fastPreviewLoadVersion;
    private int _targetThumbnailLoadVersion;
    private CancellationTokenSource? _fastPreviewLoadCts;
    private CancellationTokenSource? _targetThumbnailLoadCts;
    private bool _isThumbnailLoading;
    private bool _isThumbnailFailed;
    private readonly object _thumbnailLoadLock = new();
    private ThumbnailSize? _loadedTargetThumbnailSize;
    private ThumbnailSize? _requestedFastPreviewSize;
    private ThumbnailSize? _requestedTargetThumbnailSize;
    private ThumbnailLoadStage _thumbnailStage = ThumbnailLoadStage.None;

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public bool IsThumbnailLoading
    {
        get => _isThumbnailLoading;
        private set => SetProperty(ref _isThumbnailLoading, value);
    }

    public bool IsThumbnailFailed
    {
        get => _isThumbnailFailed;
        private set => SetProperty(ref _isThumbnailFailed, value);
    }

    public bool HasFastPreview
    {
        get
        {
            lock (_thumbnailLoadLock)
            {
                return _thumbnailStage >= ThumbnailLoadStage.FastPreview;
            }
        }
    }

    public bool HasTargetThumbnail
    {
        get
        {
            lock (_thumbnailLoadLock)
            {
                return _thumbnailStage == ThumbnailLoadStage.Target;
            }
        }
    }

    public async Task EnsureFastPreviewAsync(ThumbnailSize size)
    {
        if (AppLifetime.IsShuttingDown)
        {
            return;
        }

        lock (_thumbnailLoadLock)
        {
            if (Thumbnail != null && _thumbnailStage >= ThumbnailLoadStage.FastPreview)
            {
                return;
            }

            if (_fastPreviewLoadCts != null &&
                !_fastPreviewLoadCts.IsCancellationRequested &&
                _requestedFastPreviewSize == size)
            {
                return;
            }
        }

        CancellationTokenSource localCts;
        int localVersion;

        lock (_thumbnailLoadLock)
        {
            _fastPreviewLoadCts?.Cancel();
            _fastPreviewLoadCts = new CancellationTokenSource();
            localCts = _fastPreviewLoadCts;
            localVersion = ++_fastPreviewLoadVersion;
            _requestedFastPreviewSize = size;
            IsThumbnailLoading = Thumbnail == null;
            IsThumbnailFailed = false;
        }

        var cancellationToken = localCts.Token;

        try
        {
            await _globalThumbnailLoadSemaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await ImageThumbnailRequestHelper.RequestFastPreviewAsync(ImageFile, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                CommitThumbnailResult(localVersion, size, result, ThumbnailLoadStage.FastPreview, cancellationToken);
            }
            finally
            {
                _globalThumbnailLoadSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage.FastPreview, localVersion, size);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] EnsureFastPreviewAsync failed for {ImageName}: {ex.Message}");
            ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage.FastPreview, localVersion, size);
        }
    }

    public async Task EnsureThumbnailAsync(ThumbnailSize size)
    {
        if (AppLifetime.IsShuttingDown)
        {
            return;
        }

        lock (_thumbnailLoadLock)
        {
            if (Thumbnail != null &&
                _thumbnailStage == ThumbnailLoadStage.Target &&
                _loadedTargetThumbnailSize == size)
            {
                return;
            }

            if (_targetThumbnailLoadCts != null &&
                !_targetThumbnailLoadCts.IsCancellationRequested &&
                _requestedTargetThumbnailSize == size)
            {
                return;
            }
        }

        CancellationTokenSource localCts;
        int localVersion;

        lock (_thumbnailLoadLock)
        {
            _targetThumbnailLoadCts?.Cancel();
            _targetThumbnailLoadCts = new CancellationTokenSource();
            localCts = _targetThumbnailLoadCts;
            localVersion = ++_targetThumbnailLoadVersion;
            _requestedTargetThumbnailSize = size;
            IsThumbnailLoading = true;
            IsThumbnailFailed = false;
        }

        var cancellationToken = localCts.Token;

        try
        {
            await _globalThumbnailLoadSemaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await ImageThumbnailRequestHelper.RequestTargetThumbnailAsync(ImageFile, size, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                CommitThumbnailResult(localVersion, size, result, ThumbnailLoadStage.Target, cancellationToken);
            }
            finally
            {
                _globalThumbnailLoadSemaphore.Release();
            }
        }
        catch (OperationCanceledException)
        {
            ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage.Target, localVersion, size);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] EnsureThumbnailAsync failed for {ImageName}: {ex.Message}");
            ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage.Target, localVersion, size);
        }
    }

    private void CommitThumbnailResult(
        int loadVersion,
        ThumbnailSize size,
        ImageSource? result,
        ThumbnailLoadStage resultStage,
        CancellationToken cancellationToken)
    {
        var dispatcher = App.MainWindow.DispatcherQueue;
        if (dispatcher.HasThreadAccess)
        {
            CommitThumbnailResultOnUIThread(loadVersion, size, result, resultStage, cancellationToken);
            return;
        }

        if (!dispatcher.TryEnqueue(() =>
                CommitThumbnailResultOnUIThread(loadVersion, size, result, resultStage, cancellationToken)))
        {
            ClearThumbnailLoadingIfCurrent(resultStage, loadVersion, size);
        }
    }

    private void CommitThumbnailResultOnUIThread(
        int loadVersion,
        ThumbnailSize size,
        ImageSource? result,
        ThumbnailLoadStage resultStage,
        CancellationToken cancellationToken)
    {
        if (AppLifetime.IsShuttingDown)
        {
            return;
        }

        lock (_thumbnailLoadLock)
        {
            if (cancellationToken.IsCancellationRequested || !IsCurrentThumbnailRequest(resultStage, loadVersion, size))
            {
                return;
            }

            if (result != null && resultStage >= _thumbnailStage)
            {
                var previousStage = _thumbnailStage;
                if (resultStage == ThumbnailLoadStage.FastPreview)
                {
                    _fastPreviewThumbnail = result;
                }

                Thumbnail = result;
                _thumbnailStage = resultStage;
                if (resultStage == ThumbnailLoadStage.Target)
                {
                    _loadedTargetThumbnailSize = size;
                }

                if (previousStage != _thumbnailStage)
                {
                    OnPropertyChanged(nameof(HasFastPreview));
                    OnPropertyChanged(nameof(HasTargetThumbnail));
                }
            }

            if (resultStage == ThumbnailLoadStage.FastPreview)
            {
                _requestedFastPreviewSize = null;
            }

            if (resultStage == ThumbnailLoadStage.Target)
            {
                _requestedTargetThumbnailSize = null;
                IsThumbnailLoading = false;
                IsThumbnailFailed = result == null && Thumbnail == null;
            }
            else if (_requestedTargetThumbnailSize == null)
            {
                IsThumbnailLoading = false;
                IsThumbnailFailed = result == null && Thumbnail == null;
            }
        }
    }

    private bool IsCurrentThumbnailRequest(ThumbnailLoadStage stage, int loadVersion, ThumbnailSize size)
    {
        return stage switch
        {
            ThumbnailLoadStage.FastPreview => loadVersion == _fastPreviewLoadVersion && _requestedFastPreviewSize == size,
            ThumbnailLoadStage.Target => loadVersion == _targetThumbnailLoadVersion && _requestedTargetThumbnailSize == size,
            _ => false
        };
    }

    private void ClearThumbnailLoadingIfCurrent(ThumbnailLoadStage stage, int loadVersion, ThumbnailSize size)
    {
        lock (_thumbnailLoadLock)
        {
            if (!IsCurrentThumbnailRequest(stage, loadVersion, size))
            {
                return;
            }

            if (stage == ThumbnailLoadStage.Target || _requestedTargetThumbnailSize == null)
            {
                IsThumbnailLoading = false;
            }
        }
    }

    public void CancelThumbnailLoad()
    {
        lock (_thumbnailLoadLock)
        {
            _fastPreviewLoadCts?.Cancel();
            _targetThumbnailLoadCts?.Cancel();
            _fastPreviewLoadVersion++;
            _targetThumbnailLoadVersion++;
            _requestedFastPreviewSize = null;
            _requestedTargetThumbnailSize = null;
            IsThumbnailLoading = false;
            IsThumbnailFailed = false;
        }
    }

    public void CancelTargetThumbnailLoad()
    {
        lock (_thumbnailLoadLock)
        {
            _targetThumbnailLoadCts?.Cancel();
            _targetThumbnailLoadVersion++;
            _requestedTargetThumbnailSize = null;
            IsThumbnailLoading = false;
            IsThumbnailFailed = false;
        }
    }

    public void ClearThumbnail()
    {
        lock (_thumbnailLoadLock)
        {
            _fastPreviewLoadCts?.Cancel();
            _targetThumbnailLoadCts?.Cancel();
            _fastPreviewLoadVersion++;
            _targetThumbnailLoadVersion++;
            Thumbnail = null;
            _fastPreviewThumbnail = null;
            _thumbnailStage = ThumbnailLoadStage.None;
            _loadedTargetThumbnailSize = null;
            _requestedFastPreviewSize = null;
            _requestedTargetThumbnailSize = null;
            IsThumbnailLoading = false;
            IsThumbnailFailed = false;
            OnPropertyChanged(nameof(HasFastPreview));
            OnPropertyChanged(nameof(HasTargetThumbnail));
        }
    }

    public void DowngradeToFastPreview()
    {
        lock (_thumbnailLoadLock)
        {
            _targetThumbnailLoadCts?.Cancel();
            _targetThumbnailLoadVersion++;
            _loadedTargetThumbnailSize = null;
            _requestedTargetThumbnailSize = null;

            if (_thumbnailStage == ThumbnailLoadStage.Target && _fastPreviewThumbnail != null)
            {
                Thumbnail = _fastPreviewThumbnail;
                _thumbnailStage = ThumbnailLoadStage.FastPreview;
                IsThumbnailLoading = false;
                IsThumbnailFailed = false;
                OnPropertyChanged(nameof(HasTargetThumbnail));
            }
        }
    }
}
