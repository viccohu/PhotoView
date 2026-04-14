using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoView.Controls;

public sealed partial class PreviewImageCanvasControl : UserControl
{
    private const double MinZoomPercent = 10d;
    private const double MaxZoomPercent = 800d;
    private const double ZoomEasingFactor = 0.18;
    private const double InertiaDamping = 0.88;
    private const double VelocityThreshold = 0.08;

    private ImageFileInfo? _imageFileInfo;
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private double _zoomScale = 1d;
    private double _targetZoomScale = 1d;
    private double _translateX;
    private double _translateY;
    private double _velocityX;
    private double _velocityY;
    private bool _isDragging;
    private bool _isLoaded;
    private bool _isLoadingHighRes;
    private bool _hasZoomAnchor;
    private double _zoomAnchorImgX;
    private double _zoomAnchorImgY;
    private double _zoomAnchorScreenX;
    private double _zoomAnchorScreenY;
    private Windows.Foundation.Point _lastDragPoint;
    private int _rotationDegrees;
    private int _mirrorX = 1;
    private int _mirrorY = 1;
    private bool _reloadImageOnLoaded;

    public event EventHandler<double>? ZoomPercentChanged;

    public PreviewImageCanvasControl()
    {
        InitializeComponent();
        Loaded += PreviewImageCanvasControl_Loaded;
        Unloaded += PreviewImageCanvasControl_Unloaded;
    }

    public ImageFileInfo? CurrentImage
    {
        get => _imageFileInfo;
        set
        {
            if (ReferenceEquals(_imageFileInfo, value))
                return;

            _imageFileInfo = value;
            _ = LoadImageAsync(value);
        }
    }

    public double ZoomPercent => GetCurrentZoomPercent();

    public void SetZoomPercent(double percent)
    {
        if (_imageFileInfo == null)
            return;

        var originalScale = CalculateOriginalScale();
        percent = Math.Clamp(percent, MinZoomPercent, MaxZoomPercent);
        _targetZoomScale = originalScale * (percent / 100d);
        _hasZoomAnchor = false;
        ClampTranslation();
        StartPhysics();
        ZoomPercentChanged?.Invoke(this, percent);
    }

    public void ToggleOriginalOrFitZoom()
    {
        if (_imageFileInfo == null)
            return;

        var currentPercent = GetTargetZoomPercent();
        SetZoomPercent(Math.Abs(currentPercent - 100d) < 1d ? GetFitZoomPercent() : 100d);
    }

    public void RotateClockwise()
    {
        _rotationDegrees = (_rotationDegrees + 90) % 360;
        ApplyTransform();
    }

    public void FlipHorizontal()
    {
        _mirrorX *= -1;
        ApplyTransform();
    }

    public void FlipVertical()
    {
        _mirrorY *= -1;
        ApplyTransform();
    }

    private void PreviewImageCanvasControl_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        if (_reloadImageOnLoaded && _imageFileInfo != null)
        {
            _reloadImageOnLoaded = false;
            _ = LoadImageAsync(_imageFileInfo);
            return;
        }

        StartPhysics();
    }

    private void PreviewImageCanvasControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _reloadImageOnLoaded = _imageFileInfo != null;
        _isDragging = false;
        _isLoadingHighRes = false;
        _loadVersion++;
        _loadCts?.Cancel();
        _loadCts = null;
        CompositionTarget.Rendering -= OnRendering;
    }

    private async Task LoadImageAsync(ImageFileInfo? imageFileInfo)
    {
        _loadVersion++;
        _loadCts?.Cancel();
        _loadCts = null;
        _reloadImageOnLoaded = !_isLoaded && imageFileInfo != null;
        ResetViewer(resetTransforms: true);

        if (imageFileInfo == null)
        {
            MainImage.Source = null;
            EmptyText.Visibility = Visibility.Visible;
            ZoomPercentChanged?.Invoke(this, 100d);
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        MainImage.Source = imageFileInfo.Thumbnail;
        ZoomPercentChanged?.Invoke(this, GetCurrentZoomPercent());
        var localVersion = _loadVersion;
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        _loadCts = cts;

        try
        {
            var thumbnailService = App.GetService<IThumbnailService>();
            var targetLongSide = GetTargetDecodeLongSide();
            var forceFullDecodeRaw = IsRawFile(imageFileInfo.FileType) && App.GetService<ISettingsService>().AlwaysDecodeRaw;
            _isLoadingHighRes = true;
            var result = await thumbnailService.GetThumbnailWithSizeAsync(
                imageFileInfo.ImageFile,
                targetLongSide,
                forceFullDecodeRaw,
                cts.Token);

            if (!_isLoaded ||
                localVersion != _loadVersion ||
                cts.IsCancellationRequested ||
                !ReferenceEquals(_imageFileInfo, imageFileInfo) ||
                result?.ImageSource == null)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isLoaded ||
                    localVersion != _loadVersion ||
                    cts.IsCancellationRequested ||
                    !ReferenceEquals(_imageFileInfo, imageFileInfo))
                {
                    return;
                }

                MainImage.Source = result.ImageSource;
                _isLoadingHighRes = false;
                ApplyTransform();
                ZoomPercentChanged?.Invoke(this, GetCurrentZoomPercent());
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PreviewImageCanvas] high-res load failed: {ex.Message}");
        }
        finally
        {
            _isLoadingHighRes = false;
        }
    }

    private uint GetTargetDecodeLongSide()
    {
        var longSide = Math.Max(ImageContainer.ActualWidth, ImageContainer.ActualHeight);
        if (longSide <= 0)
            longSide = 1080;

        var scale = XamlRoot?.RasterizationScale ?? 1d;
        return (uint)Math.Clamp(Math.Round(longSide * scale * 1.5d), 512d, 4096d);
    }

    private void ResetViewer(bool resetTransforms)
    {
        _zoomScale = 1d;
        _targetZoomScale = 1d;
        _translateX = 0;
        _translateY = 0;
        _velocityX = 0;
        _velocityY = 0;
        _hasZoomAnchor = false;

        if (resetTransforms)
        {
            _rotationDegrees = 0;
            _mirrorX = 1;
            _mirrorY = 1;
        }

        ApplyTransform();
    }

    private void ImageContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ImageClip.Rect = new Windows.Foundation.Rect(0, 0, ImageContainer.ActualWidth, ImageContainer.ActualHeight);
        ClampTranslation();
        ApplyTransform();
        ZoomPercentChanged?.Invoke(this, GetCurrentZoomPercent());
    }

    private void ImageContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_imageFileInfo == null)
            return;

        var point = e.GetCurrentPoint(ImageContainer);
        var delta = point.Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        var scaleFactor = Math.Pow(1.0015, delta);
        var originalScale = CalculateOriginalScale();
        var newTarget = Math.Clamp(
            _targetZoomScale * scaleFactor,
            originalScale * (MinZoomPercent / 100d),
            originalScale * (MaxZoomPercent / 100d));

        var containerCenterX = ImageContainer.ActualWidth / 2d;
        var containerCenterY = ImageContainer.ActualHeight / 2d;
        var mouseRelativeToCenterX = point.Position.X - containerCenterX;
        var mouseRelativeToCenterY = point.Position.Y - containerCenterY;

        _zoomAnchorImgX = (mouseRelativeToCenterX - _translateX) / Math.Max(_zoomScale, 0.0001);
        _zoomAnchorImgY = (mouseRelativeToCenterY - _translateY) / Math.Max(_zoomScale, 0.0001);
        _zoomAnchorScreenX = mouseRelativeToCenterX;
        _zoomAnchorScreenY = mouseRelativeToCenterY;
        _hasZoomAnchor = true;

        _targetZoomScale = newTarget;
        StartPhysics();
        ZoomPercentChanged?.Invoke(this, GetTargetZoomPercent());
        e.Handled = true;
    }

    private void ImageTransformContainer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_imageFileInfo == null)
            return;

        ToggleOriginalOrFitZoom();
        e.Handled = true;
    }

    private void ImageContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_imageFileInfo == null || !CanPan())
            return;

        var pointer = e.GetCurrentPoint(ImageContainer);
        if (!pointer.Properties.IsLeftButtonPressed)
            return;

        _isDragging = true;
        _lastDragPoint = pointer.Position;
        _velocityX = 0;
        _velocityY = 0;
        _hasZoomAnchor = false;
        ImageContainer.CapturePointer(e.Pointer);
    }

    private void ImageContainer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
            return;

        var pointer = e.GetCurrentPoint(ImageContainer);
        var current = pointer.Position;
        var deltaX = current.X - _lastDragPoint.X;
        var deltaY = current.Y - _lastDragPoint.Y;

        _translateX += deltaX;
        _translateY += deltaY;
        _velocityX = deltaX;
        _velocityY = deltaY;
        _lastDragPoint = current;
        ClampTranslation();
        ApplyTransform();
    }

    private void ImageContainer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        ImageContainer.ReleasePointerCapture(e.Pointer);
        StartPhysics();
    }

    private void StartPhysics()
    {
        if (!_isLoaded)
            return;

        CompositionTarget.Rendering -= OnRendering;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object e)
    {
        if (!_isLoaded || _isLoadingHighRes)
            return;

        var needsUpdate = false;
        if (Math.Abs(_zoomScale - _targetZoomScale) > 0.0005)
        {
            var oldScale = Math.Max(_zoomScale, 0.0001);
            _zoomScale += (_targetZoomScale - _zoomScale) * ZoomEasingFactor;

            if (_hasZoomAnchor)
            {
                _translateX = _zoomAnchorScreenX - _zoomAnchorImgX * _zoomScale;
                _translateY = _zoomAnchorScreenY - _zoomAnchorImgY * _zoomScale;
            }
            else
            {
                var scaleRatio = _zoomScale / oldScale;
                _translateX *= scaleRatio;
                _translateY *= scaleRatio;
            }

            needsUpdate = true;
        }
        else if (_hasZoomAnchor)
        {
            _hasZoomAnchor = false;
        }

        if (!_isDragging && (Math.Abs(_velocityX) > VelocityThreshold || Math.Abs(_velocityY) > VelocityThreshold))
        {
            _translateX += _velocityX;
            _translateY += _velocityY;
            _velocityX *= InertiaDamping;
            _velocityY *= InertiaDamping;
            needsUpdate = true;
        }

        if (needsUpdate)
        {
            ClampTranslation();
            ApplyTransform();
            ZoomPercentChanged?.Invoke(this, GetCurrentZoomPercent());
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
    }

    private void ApplyTransform()
    {
        ImageTransform.ScaleX = _zoomScale * _mirrorX;
        ImageTransform.ScaleY = _zoomScale * _mirrorY;
        ImageTransform.TranslateX = _translateX;
        ImageTransform.TranslateY = _translateY;
        ImageTransform.Rotation = _rotationDegrees;
    }

    private void ClampTranslation()
    {
        if (_imageFileInfo == null)
            return;

        var (width, height) = GetScaledImageSize();
        if (width <= ImageContainer.ActualWidth)
        {
            _translateX = 0;
        }
        else
        {
            var maxX = width / 2d - ImageContainer.ActualWidth / 2d;
            _translateX = Math.Clamp(_translateX, -maxX, maxX);
        }

        if (height <= ImageContainer.ActualHeight)
        {
            _translateY = 0;
        }
        else
        {
            var maxY = height / 2d - ImageContainer.ActualHeight / 2d;
            _translateY = Math.Clamp(_translateY, -maxY, maxY);
        }
    }

    private bool CanPan()
    {
        var (width, height) = GetScaledImageSize();
        return width > ImageContainer.ActualWidth || height > ImageContainer.ActualHeight;
    }

    private (double Width, double Height) GetScaledImageSize()
    {
        return (MainImage.ActualWidth * _zoomScale, MainImage.ActualHeight * _zoomScale);
    }

    private double CalculateFitToScreenScale()
    {
        if (_imageFileInfo == null || _imageFileInfo.Width <= 0 || _imageFileInfo.Height <= 0)
            return 1d;

        var containerWidth = ImageContainer.ActualWidth;
        var containerHeight = ImageContainer.ActualHeight;
        if (containerWidth <= 0 || containerHeight <= 0)
            return 1d;

        return Math.Min(containerWidth / _imageFileInfo.Width, containerHeight / _imageFileInfo.Height);
    }

    private double CalculateOriginalScale()
    {
        var fitScale = CalculateFitToScreenScale();
        if (fitScale <= 0)
            return 1d;

        var dpiScale = XamlRoot?.RasterizationScale ?? 1d;
        return (1d / fitScale) / Math.Max(dpiScale, 0.0001);
    }

    private double GetCurrentZoomPercent()
    {
        var originalScale = CalculateOriginalScale();
        return Math.Clamp((_zoomScale / Math.Max(originalScale, 0.0001)) * 100d, MinZoomPercent, MaxZoomPercent);
    }

    private double GetTargetZoomPercent()
    {
        var originalScale = CalculateOriginalScale();
        return Math.Clamp((_targetZoomScale / Math.Max(originalScale, 0.0001)) * 100d, MinZoomPercent, MaxZoomPercent);
    }

    private double GetFitZoomPercent()
    {
        var originalScale = CalculateOriginalScale();
        return Math.Clamp((1d / Math.Max(originalScale, 0.0001)) * 100d, MinZoomPercent, MaxZoomPercent);
    }

    private static bool IsRawFile(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        extension = extension.Trim();
        if (!extension.StartsWith('.'))
            extension = $".{extension}";

        return extension.Equals(".cr2", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".cr3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".crw", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".nef", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".nrw", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".arw", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".sr2", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".raf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".orf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".rw2", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".pef", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dng", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".srw", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".raw", StringComparison.OrdinalIgnoreCase);
    }
}
