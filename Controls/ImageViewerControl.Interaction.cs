using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoView.Controls;

public sealed partial class ImageViewerControl
{
    private void StartPhysics()
    {
        if (_isRunning) return;
        if (!_isLoaded || _isClosing) return;

        try
        {
            CompositionTarget.Rendering += OnPhysicsRendering;
            _isRunning = true;
        }
        catch (Exception ex)
        {
            _isRunning = false;
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] StartPhysics error: {ex}");
        }
    }

    private void StopPhysics()
    {
        if (!_isRunning) return;

        try
        {
            CompositionTarget.Rendering -= OnPhysicsRendering;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] StopPhysics error: {ex}");
        }
        finally
        {
            _isRunning = false;
        }
    }

    private void OnPhysicsRendering(object? sender, object e)
    {
        try
        {
            if (!_isLoaded || _isClosing) return;
            if (!_isRunning) return;
            if (_isLoadingHighRes) return;
            if (MainImage?.Source == null || ImageContainer == null) return;

            bool needsUpdate = false;
            bool isZooming = Math.Abs(_zoomScale - _targetZoomScale) > 0.0001;

            if (isZooming)
            {
                var oldScale = _zoomScale;
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

                ClampTranslation();
                needsUpdate = true;
            }
            else if (_hasZoomAnchor)
            {
                _hasZoomAnchor = false;
            }

            if (!_isDragging &&
                (Math.Abs(_velocityX) > VelocityThreshold || Math.Abs(_velocityY) > VelocityThreshold))
            {
                _translateX += _velocityX;
                _translateY += _velocityY;
                _velocityX *= InertiaDamping;
                _velocityY *= InertiaDamping;
                needsUpdate = true;
            }

            if (!_isDragging)
            {
                needsUpdate |= ApplyBoundsWithSpring();
            }

            if (needsUpdate)
            {
                ApplyTransform();
            }
        }
        catch (Exception ex)
        {
            StopPhysics();
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] OnPhysicsRendering error: {ex}");
        }
    }

    private void ImageContainer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (MainImage?.Source == null || ImageContainer == null) return;

        var pointer = e.GetCurrentPoint(ImageContainer);
        var mouse = pointer.Position;
        var delta = pointer.Properties.MouseWheelDelta;

        if (delta == 0) return;

        double scaleFactor = Math.Pow(1.0015, delta);
        double newTarget = _targetZoomScale * scaleFactor;

        double originalScaleForFit = CalculateOriginalScale();

        if (_justSnappedTo100Percent)
        {
            _snapStayCounter++;
            if (_snapStayCounter < SnapStayCount)
            {
                newTarget = originalScaleForFit;
            }
            else
            {
                _justSnappedTo100Percent = false;
                _snapStayCounter = 0;
            }
        }
        else
        {
            bool wasBelow100 = _targetZoomScale < originalScaleForFit * (1.0 - SnapThreshold);
            bool wasAbove100 = _targetZoomScale > originalScaleForFit * (1.0 + SnapThreshold);
            bool willBeAbove100 = newTarget > originalScaleForFit * (1.0 + SnapThreshold);
            bool willBeBelow100 = newTarget < originalScaleForFit * (1.0 - SnapThreshold);
            bool isNear100 = Math.Abs(_targetZoomScale - originalScaleForFit) < originalScaleForFit * SnapThreshold * 1.5;

            if ((wasBelow100 && willBeAbove100) || (wasAbove100 && willBeBelow100) || isNear100)
            {
                newTarget = originalScaleForFit;
                _justSnappedTo100Percent = true;
                _snapStayCounter = 0;
            }
        }

        newTarget = Math.Clamp(newTarget, 0.1, 8.0);

        var containerCenterX = ImageContainer.ActualWidth / 2;
        var containerCenterY = ImageContainer.ActualHeight / 2;

        var mouseRelativeToCenterX = mouse.X - containerCenterX;
        var mouseRelativeToCenterY = mouse.Y - containerCenterY;

        _zoomAnchorImgX = (mouseRelativeToCenterX - _translateX) / _zoomScale;
        _zoomAnchorImgY = (mouseRelativeToCenterY - _translateY) / _zoomScale;
        _zoomAnchorScreenX = mouseRelativeToCenterX;
        _zoomAnchorScreenY = mouseRelativeToCenterY;
        _hasZoomAnchor = true;

        _targetZoomScale = newTarget;
        TryStartOriginalImageLoad();
        e.Handled = true;
    }

    private void ImageContainer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!CanPan() && !_isDragging) return;

        var pointer = e.GetCurrentPoint(ImageContainer);
        if (!pointer.Properties.IsLeftButtonPressed) return;

        _isDragging = true;
        _lastDragPoint = pointer.Position;
        ImageContainer.CapturePointer(e.Pointer);

        _velocityX = 0;
        _velocityY = 0;
        _hasZoomAnchor = false;
    }

    private void ImageContainer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;

        var pointer = e.GetCurrentPoint(ImageContainer);
        var currentPoint = pointer.Position;

        var deltaX = currentPoint.X - _lastDragPoint.X;
        var deltaY = currentPoint.Y - _lastDragPoint.Y;

        if (CanPanHorizontal())
        {
            var offsetX = GetBoundsOffsetX();
            var adjustedDeltaX = ApplyRubberBand(deltaX, offsetX);
            _translateX += adjustedDeltaX;
            _velocityX = deltaX;
        }
        else
        {
            _velocityX = 0;
        }

        if (CanPanVertical())
        {
            var offsetY = GetBoundsOffsetY();
            var adjustedDeltaY = ApplyRubberBand(deltaY, offsetY);
            _translateY += adjustedDeltaY;
            _velocityY = deltaY;
        }
        else
        {
            _velocityY = 0;
        }

        _lastDragPoint = currentPoint;
        ApplyTransform();
    }

    private void ImageContainer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        ImageContainer.ReleasePointerCapture(e.Pointer);

        _velocityX = 0;
        _velocityY = 0;
    }

    private void ImageContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            if (_isClosing)
            {
                return;
            }

            ImageClip.Rect = new Windows.Foundation.Rect(0, 0, ImageContainer.ActualWidth, ImageContainer.ActualHeight);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] ImageContainer_SizeChanged error: {ex}");
        }
    }

    private void ClampTranslation()
    {
        if (MainImage?.Source == null || ImageContainer == null) return;

        var containerWidth = ImageContainer.ActualWidth;
        var containerHeight = ImageContainer.ActualHeight;

        if (containerWidth <= 0 || containerHeight <= 0) return;

        var (imageWidth, imageHeight) = GetScaledImageSize();

        if (imageWidth <= containerWidth)
        {
            _translateX = 0;
        }
        else
        {
            var maxTranslateX = imageWidth / 2 - containerWidth / 2;
            _translateX = Math.Max(-maxTranslateX, Math.Min(maxTranslateX, _translateX));
        }

        if (imageHeight <= containerHeight)
        {
            _translateY = 0;
        }
        else
        {
            var maxTranslateY = imageHeight / 2 - containerHeight / 2;
            _translateY = Math.Max(-maxTranslateY, Math.Min(maxTranslateY, _translateY));
        }
    }

    private double GetBoundsOffsetX()
    {
        if (MainImage?.Source == null || ImageContainer == null) return 0;

        var containerWidth = ImageContainer.ActualWidth;
        var (imageWidth, _) = GetScaledImageSize();

        if (imageWidth <= containerWidth)
        {
            return _translateX;
        }
        else
        {
            var maxTranslateX = imageWidth / 2 - containerWidth / 2;
            if (_translateX > maxTranslateX)
            {
                return _translateX - maxTranslateX;
            }
            else if (_translateX < -maxTranslateX)
            {
                return _translateX + maxTranslateX;
            }
            return 0;
        }
    }

    private double GetBoundsOffsetY()
    {
        if (MainImage?.Source == null || ImageContainer == null) return 0;

        var containerHeight = ImageContainer.ActualHeight;
        var (_, imageHeight) = GetScaledImageSize();

        if (imageHeight <= containerHeight)
        {
            return _translateY;
        }
        else
        {
            var maxTranslateY = imageHeight / 2 - containerHeight / 2;
            if (_translateY > maxTranslateY)
            {
                return _translateY - maxTranslateY;
            }
            else if (_translateY < -maxTranslateY)
            {
                return _translateY + maxTranslateY;
            }
            return 0;
        }
    }

    private double ApplyRubberBand(double delta, double offset)
    {
        if (offset == 0)
            return delta;

        double factor = 1.0 / (Math.Abs(offset) * RubberBandResistance + 1);
        return delta * factor;
    }

    private bool ApplyBoundsWithSpring()
    {
        bool changed = false;

        double offsetX = GetBoundsOffsetX();
        double offsetY = GetBoundsOffsetY();

        if (Math.Abs(offsetX) > SpringEpsilon)
        {
            _translateX -= offsetX * SpringStiffness;
            changed = true;
        }
        else if (Math.Abs(offsetX) > 0)
        {
            _translateX -= offsetX;
            changed = true;
        }

        if (Math.Abs(offsetY) > SpringEpsilon)
        {
            _translateY -= offsetY * SpringStiffness;
            changed = true;
        }
        else if (Math.Abs(offsetY) > 0)
        {
            _translateY -= offsetY;
            changed = true;
        }

        return changed;
    }

    private void ApplyTransform()
    {
        if (_cachedScaleTransform == null || _cachedTranslateTransform == null) return;

        _cachedScaleTransform.ScaleX = _zoomScale;
        _cachedScaleTransform.ScaleY = _zoomScale;
        _cachedTranslateTransform.X = _translateX;
        _cachedTranslateTransform.Y = _translateY;
    }

    private bool CanPan()
    {
        if (MainImage?.Source == null || ImageContainer == null) return false;

        var (imageWidth, imageHeight) = GetScaledImageSize();
        return imageWidth > ImageContainer.ActualWidth || imageHeight > ImageContainer.ActualHeight;
    }

    private bool CanPanHorizontal()
    {
        if (MainImage == null || ImageContainer == null) return false;
        var (imageWidth, _) = GetScaledImageSize();
        return imageWidth > ImageContainer.ActualWidth;
    }

    private bool CanPanVertical()
    {
        if (MainImage == null || ImageContainer == null) return false;
        var (_, imageHeight) = GetScaledImageSize();
        return imageHeight > ImageContainer.ActualHeight;
    }

    private (double width, double height) GetScaledImageSize()
    {
        if (MainImage == null) return (0, 0);

        var actualWidth = MainImage.ActualWidth * _zoomScale;
        var actualHeight = MainImage.ActualHeight * _zoomScale;

        return (actualWidth, actualHeight);
    }

    private double CalculateFitToScreenScale()
    {
        if (_imageFileInfo == null || _imageFileInfo.Width <= 0 || _imageFileInfo.Height <= 0) return 1.0;
        if (ImageContainer == null) return 1.0;

        var containerWidth = ImageContainer.ActualWidth;
        var containerHeight = ImageContainer.ActualHeight;

        if (containerWidth <= 0 || containerHeight <= 0) return 1.0;

        var scaleX = containerWidth / _imageFileInfo.Width;
        var scaleY = containerHeight / _imageFileInfo.Height;

        return Math.Min(scaleX, scaleY);
    }

    private double CalculateOriginalScale()
    {
        var fitScale = CalculateFitToScreenScale();
        if (fitScale <= 0) return 1.0;

        var dpiScale = XamlRoot?.RasterizationScale ?? 1.0;
        return (1.0 / fitScale) / dpiScale;
    }

    private double GetTargetZoomPercent()
    {
        var originalScale = CalculateOriginalScale();
        return (_targetZoomScale / Math.Max(originalScale, 0.0001)) * 100d;
    }

    private uint GetOriginalLongSide()
    {
        if (_imageFileInfo == null)
            return 0;

        return (uint)Math.Max(0, Math.Max(_imageFileInfo.Width, _imageFileInfo.Height));
    }

    private uint ClampDecodeLongSideToOriginal(uint longSidePixels)
    {
        var originalLongSide = GetOriginalLongSide();
        return originalLongSide > 0 ? Math.Min(longSidePixels, originalLongSide) : longSidePixels;
    }

    private void SetMainImageSource(DecodeResult result)
    {
        MainImage.Source = result.ImageSource;
        _activeDecodeLongSide = Math.Max(_activeDecodeLongSide, Math.Max(result.Width, result.Height));

        var originalLongSide = GetOriginalLongSide();
        if (originalLongSide > 0 && _activeDecodeLongSide >= originalLongSide)
        {
            _isOriginalImageLoaded = true;
        }
    }

    private void TryStartOriginalImageLoad()
    {
        if (_imageFileInfo?.ImageFile == null || _isClosing || _isOriginalImageLoaded)
            return;

        var originalLongSide = GetOriginalLongSide();
        if (originalLongSide == 0 || _activeDecodeLongSide >= originalLongSide)
        {
            _isOriginalImageLoaded = originalLongSide > 0;
            return;
        }

        if (GetTargetZoomPercent() < OriginalDecodeZoomPercentThreshold)
            return;

        var currentCts = _originalImageLoadCts;
        if (currentCts != null && !currentCts.IsCancellationRequested)
            return;

        var version = ++_originalImageLoadVersion;
        var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        _originalImageLoadCts = cts;
        _ = LoadOriginalImageAsync(_imageFileInfo, originalLongSide, version, cts.Token);
    }

    private async Task LoadOriginalImageAsync(
        ImageFileInfo imageInfo,
        uint originalLongSide,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnailService = App.GetService<IThumbnailService>();
            var forceFullDecodeRaw = IsRawFile(imageInfo.ImageFile.FileType) && App.GetService<ISettingsService>().AlwaysDecodeRaw;
            var result = await thumbnailService.GetThumbnailWithSizeAsync(
                imageInfo.ImageFile,
                originalLongSide,
                forceFullDecodeRaw,
                cancellationToken);

            if (result?.ImageSource == null ||
                cancellationToken.IsCancellationRequested ||
                version != _originalImageLoadVersion ||
                !ReferenceEquals(_imageFileInfo, imageInfo))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isLoaded ||
                    _isClosing ||
                    version != _originalImageLoadVersion ||
                    !ReferenceEquals(_imageFileInfo, imageInfo) ||
                    result.ImageSource == null)
                {
                    return;
                }

                SetMainImageSource(result);
                ApplyTransform();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] original image load failed: {ex.Message}");
        }
    }
}
