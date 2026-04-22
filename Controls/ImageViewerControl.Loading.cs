using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Display;

namespace PhotoView.Controls;

public sealed partial class ImageViewerControl
{
    private uint GetMonitorLongSide()
    {
        try
        {
            var displayInfo = DisplayInformation.GetForCurrentView();
            var screenWidth = displayInfo.ScreenWidthInRawPixels;
            var screenHeight = displayInfo.ScreenHeightInRawPixels;
            var monitorLongSide = (uint)Math.Max(screenWidth, screenHeight);
            return monitorLongSide;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] GetMonitorLongSide error: {ex}");
            return 1920;
        }
    }

    private uint GetTargetDecodeLongSide()
    {
        return ClampDecodeLongSideToOriginal(GetViewportDecodeLongSide());
    }

    private uint GetViewportDecodeLongSide()
    {
        var settingsService = App.GetService<ISettingsService>();
        var scaleFactor = settingsService.DecodeScaleFactor;

        if (ImageContainer.ActualWidth > 0 && ImageContainer.ActualHeight > 0)
        {
            var containerLongSide = Math.Max(ImageContainer.ActualWidth, ImageContainer.ActualHeight);
            return (uint)Math.Clamp(containerLongSide * scaleFactor, 1d, ViewerFitDecodeMaxLongSidePixels);
        }

        const uint fallbackSize = 1080u;
        return (uint)Math.Clamp(fallbackSize * scaleFactor, 1d, ViewerFitDecodeMaxLongSidePixels);
    }

    public async Task ShowAfterAnimationAsync()
    {
        if (_hasShown || _isClosing)
        {
            return;
        }

        try
        {
            Visibility = Visibility.Visible;

            var storyboard = new Storyboard();

            var fadeInBackground = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            Storyboard.SetTarget(fadeInBackground, BackgroundOverlay);
            Storyboard.SetTargetProperty(fadeInBackground, "Opacity");
            storyboard.Children.Add(fadeInBackground);

            var fadeInContainer = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            Storyboard.SetTarget(fadeInContainer, AnimationContainer);
            Storyboard.SetTargetProperty(fadeInContainer, "Opacity");
            storyboard.Children.Add(fadeInContainer);

            var tcs = new TaskCompletionSource<bool>();
            storyboard.Completed += (_, _) => tcs.TrySetResult(true);
            storyboard.Begin();
            await tcs.Task;

            if (!_isLoaded || _isClosing)
            {
                return;
            }

            Focus(FocusState.Programmatic);
            _hasShown = true;
            await SwitchToViewerLayerAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] ShowAfterAnimationAsync error: {ex}");
        }
    }

    private async Task SwitchToViewerLayerAsync()
    {
        if (_isViewerLayerReady || _isClosing)
        {
            return;
        }

        try
        {
            int retryCount = 0;
            while ((ImageContainer.ActualWidth <= 0 || ImageContainer.ActualHeight <= 0) && retryCount < 50)
            {
                await Task.Delay(10);

                if (!_isLoaded || _isClosing)
                {
                    return;
                }

                retryCount++;
                ImageContainer.UpdateLayout();
            }

            _isViewerLayerReady = true;
            _targetDecodeLongSide = GetTargetDecodeLongSide();

            StopPhysics();
            MainImage.Source = _imageFileInfo?.Thumbnail;
            MainImage.Stretch = Stretch.Uniform;
            if (!_isClosing)
            {
                StartPhysics();
            }

            AnimationImage.Opacity = 0;
            ImageContainer.Opacity = 1;

            ResetViewer();

            _highResLoadCts?.Cancel();
            _highResLoadCts = new CancellationTokenSource();
            _highResLoadCts.CancelAfter(TimeSpan.FromSeconds(5));

            var loadVersion = ++_highResLoadVersion;
            _highResLoadTask = LoadHighResolutionImageAsync(loadVersion, _highResLoadCts.Token);
            _ = WaitForHighResAndReplaceAsync(loadVersion);
        }
        catch (Exception ex)
        {
            _isViewerLayerReady = false;
            CancelHighResLoad();
            StopPhysics();
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync error: {ex}");
        }
    }

    private void ResetViewer()
    {
        _zoomScale = 1.0;
        _targetZoomScale = 1.0;
        _translateX = 0;
        _translateY = 0;
        _velocityX = 0;
        _velocityY = 0;
        _hasZoomAnchor = false;
        _justSnappedTo100Percent = false;
        _snapStayCounter = 0;
        _is1To1Scale = false;
        _activeDecodeLongSide = 0;
        _isOriginalImageLoaded = false;

        ApplyTransform();
    }

    private async Task WaitForHighResAndReplaceAsync(int loadVersion)
    {
        try
        {
            _isLoadingHighRes = true;
            var highResLoadTask = _highResLoadTask;
            if (highResLoadTask == null)
            {
                _isLoadingHighRes = false;
                return;
            }

            var highResResult = await highResLoadTask;

            if (!_isLoaded || _isClosing || !_isRunning || loadVersion != _highResLoadVersion)
            {
                _isLoadingHighRes = false;
                return;
            }

            if (highResResult?.ImageSource == null)
            {
                _isLoadingHighRes = false;
                return;
            }

            var enqueued = DispatcherQueue.TryEnqueue(() =>
            {
                if (!_isLoaded || _isClosing || !_isRunning || loadVersion != _highResLoadVersion)
                {
                    _isLoadingHighRes = false;
                    return;
                }

                try
                {
                    StopPhysics();
                    if (!_isOriginalImageLoaded)
                    {
                        SetMainImageSource(highResResult);
                    }

                    _isLoadingHighRes = false;
                    if (!_isClosing)
                    {
                        StartPhysics();
                    }

                    if (_imageFileInfo?.ImageFile != null)
                    {
                        _ = LoadExifAfterImageAsync(_imageFileInfo);
                    }
                }
                catch (Exception ex)
                {
                    _isLoadingHighRes = false;
                    StopPhysics();
                    System.Diagnostics.Debug.WriteLine($"[ImageViewer] WaitForHighResAndReplaceAsync UI update error: {ex}");
                }
            });

            if (!enqueued)
            {
                _isLoadingHighRes = false;
            }
        }
        catch (Exception ex)
        {
            _isLoadingHighRes = false;
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] WaitForHighResAndReplaceAsync error: {ex}");
        }
    }

    private async Task<DecodeResult?> LoadHighResolutionImageAsync(int loadVersion, CancellationToken cancellationToken)
    {
        try
        {
            if (_imageFileInfo?.ImageFile == null)
            {
                return null;
            }

            var imageFile = _imageFileInfo.ImageFile;
            var targetDecodeLongSide = _targetDecodeLongSide;
            var forceFullDecodeRaw = IsRawFile(imageFile.FileType) && App.GetService<ISettingsService>().AlwaysDecodeRaw;

            var thumbnailService = App.GetService<IThumbnailService>();
            var decodeResult = await thumbnailService.GetThumbnailWithSizeAsync(imageFile, targetDecodeLongSide, forceFullDecodeRaw, cancellationToken);

            if (decodeResult?.ImageSource != null && loadVersion == _highResLoadVersion && !cancellationToken.IsCancellationRequested)
            {
                return decodeResult;
            }

            System.Diagnostics.Debug.WriteLine("[ImageViewer] LoadHighResolutionImageAsync: high-resolution decode returned no usable result.");
            return null;
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync canceled, version={loadVersion}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadHighResolutionImageAsync error: {ex}");
            return null;
        }
    }

    private static bool IsRawFile(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        extension = extension.Trim();
        if (!extension.StartsWith('.'))
        {
            extension = $".{extension}";
        }

        return RawFileExtensions.Contains(extension);
    }

    private void CancelHighResLoad()
    {
        _highResLoadVersion++;
        _highResLoadCts?.Cancel();
        _originalImageLoadVersion++;
        _originalImageLoadCts?.Cancel();
        _isLoadingHighRes = false;
    }
}
