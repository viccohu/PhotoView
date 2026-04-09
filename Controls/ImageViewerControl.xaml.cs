using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Contracts.Services;
using PhotoView.Models;
using PhotoView.ViewModels;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PhotoView.Controls;

public sealed partial class ImageViewerControl : UserControl
{
    public event EventHandler? Closed;

    public ImageViewerViewModel ViewModel { get; }

    private ImageFileInfo? _imageFileInfo;
    private bool _is1To1Scale = false;
    private double _fitScale = 1.0;
    private uint _targetDecodeLongSide = 1920;
    private Task<DecodeResult?>? _highResLoadTask;
    private bool _hasPrepared = false;
    private bool _hasShown = false;

    private static readonly HashSet<string> RawFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".arw", ".srf", ".sr2", ".crw", ".cr2", ".cr3", ".nef", ".nrw", ".orf", ".pef", ".raf", ".rw2", ".dng", ".erf", ".3fr", ".kdc", ".mrw", ".mef", ".mos", ".rwl", ".srw", ".x3f", ".iiq", ".fff"
    };

    #region 物理状态

    private double _zoomScale = 1.0;
    private double _targetZoomScale = 1.0;
    private double _translateX = 0;
    private double _translateY = 0;
    private double _velocityX = 0;
    private double _velocityY = 0;

    private bool _isDragging = false;
    private Windows.Foundation.Point _lastDragPoint;

    private bool _hasZoomAnchor = false;
    private double _zoomAnchorImgX = 0;
    private double _zoomAnchorImgY = 0;
    private double _zoomAnchorScreenX = 0;
    private double _zoomAnchorScreenY = 0;

    private bool _justSnappedTo100Percent = false;
    private int _snapStayCounter = 0;

    private bool _isClosing = false;
    private bool _isRunning = false;
    private bool _isLoadingHighRes = false;
    private bool _isViewerLayerReady = false;
    private bool _isLoadingExif = false;

    private ScaleTransform? _cachedScaleTransform;
    private TranslateTransform? _cachedTranslateTransform;

    #endregion

    #region 物理参数

    /// <summary>
    /// 惯性衰减系数（影响拖动后的滑动距离）
    /// 值越大：滑动距离越远，手感越滑（推荐范围：0.85 - 0.95）
    /// 值越小：滑动距离越短，手感越粘
    /// </summary>
    private const double InertiaDamping = 0.92;

    /// <summary>
    /// 缩放缓动系数（影响缩放的响应速度）
    /// 值越大：缩放响应越快，过渡越生硬（推荐范围：0.1 - 0.3）
    /// 值越小：缩放响应越慢，过渡越平滑
    /// </summary>
    private const double ZoomEasingFactor = 0.15;

    /// <summary>
    /// 边界阻尼系数（已弃用，保留用于兼容）
    /// </summary>
    private const double BoundaryDamping = 0.3;

    /// <summary>
    /// 回弹速度系数（已弃用，保留用于兼容）
    /// </summary>
    private const double SpringBackFactor = 0.12;

    /// <summary>
    /// 速度阈值（用于判断惯性是否停止）
    /// 值越大：停止得越早
    /// 值越小：滑动得越久（推荐范围：0.1 - 1.0）
    /// </summary>
    private const double VelocityThreshold = 0.5;

    /// <summary>
    /// 100% 吸附阈值（影响吸附的灵敏度）
    /// 值越大：吸附范围越大，越容易吸附到 100%（推荐范围：0.03 - 0.1）
    /// 值越小：吸附范围越小，越难吸附到 100%
    /// </summary>
    private const double SnapThreshold = 0.05;

    /// <summary>
    /// 100% 吸附停留次数（影响离开 100% 的难度）
    /// 值越大：需要滚动更多次才能离开 100%（推荐范围：2 - 6）
    /// 值越小：很容易离开 100%
    /// </summary>
    private const int SnapStayCount = 4;

    /// <summary>
    /// 橡皮筋阻力系数（拖拽时拉出边界的阻力感）
    /// 值越大：越难拉出边界，阻力感越强（推荐范围：0.1 - 0.3）
    /// 值越小：越容易拉出边界，阻力感越弱
    /// </summary>
    private const double RubberBandResistance = 0.15;

    /// <summary>
    /// 弹簧刚度系数（松手后回弹的速度）
    /// 值越大：回弹速度越快，手感越硬（推荐范围：0.1 - 0.3）
    /// 值越小：回弹速度越慢，手感越软
    /// </summary>
    private const double SpringStiffness = 0.2;

    /// <summary>
    /// 弹簧阻尼系数（回弹时的衰减速度，防止过度抖动）
    /// 值越大：衰减越快，回弹越干脆（推荐范围：0.6 - 0.8）
    /// 值越小：衰减越慢，回弹越有弹性但可能抖动
    /// </summary>
    private const double SpringDamping = 0.7;

    /// <summary>
    /// 弹簧停止阈值（判断回弹是否完成的精度）
    /// 值越大：停止得越早，但可能停在偏差位置
    /// 值越小：停止得越晚，但位置更精确（推荐范围：0.05 - 0.2）
    /// </summary>
    private const double SpringEpsilon = 0.1;

    #endregion

    public ImageViewerControl()
    {
        ViewModel = App.GetService<ImageViewerViewModel>();
        InitializeComponent();
        this.DataContext = ViewModel;
        Loaded += ImageViewerControl_Loaded;
        Unloaded += ImageViewerControl_Unloaded;
    }

    private void StartPhysics()
    {
        if (_isRunning) return;

        CompositionTarget.Rendering += OnPhysicsRendering;
        _isRunning = true;
    }

    private void StopPhysics()
    {
        if (!_isRunning) return;

        CompositionTarget.Rendering -= OnPhysicsRendering;
        _isRunning = false;
    }

    private void ImageViewerControl_Loaded(object sender, RoutedEventArgs e)
    {
        _cachedScaleTransform = ImageScaleTransform;
        _cachedTranslateTransform = ImageTranslateTransform;
        StartPhysics();
    }

    private void ImageViewerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        StopPhysics();
    }

    public void PrepareContent(ImageFileInfo imageFileInfo)
    {
        if (_hasPrepared)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 已准备过，跳过重复调用");
            return;
        }

        _imageFileInfo = imageFileInfo;
        _is1To1Scale = false;
        _isClosing = false;
        _isViewerLayerReady = false;
        _hasPrepared = true;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 已设置动画层缩略图, 文件名={imageFileInfo.ImageName}");

        AnimationImage.Source = imageFileInfo.Thumbnail;

        ViewModel.SetBasicInfo(imageFileInfo);
    }

    public Task PrepareForAnimationAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareForAnimationAsync: 完成");
        return Task.CompletedTask;
    }

    private uint GetMonitorLongSide()
    {
        try
        {
            var displayInfo = DisplayInformation.GetForCurrentView();
            var screenWidth = displayInfo.ScreenWidthInRawPixels;
            var screenHeight = displayInfo.ScreenHeightInRawPixels;
            var monitorLongSide = (uint)Math.Max(screenWidth, screenHeight);
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] GetMonitorLongSide: 显示器分辨率={screenWidth}x{screenHeight}, 最长边={monitorLongSide}");
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
        var settingsService = App.GetService<ISettingsService>();
        var scaleFactor = settingsService.DecodeScaleFactor;

        if (ImageContainer.ActualWidth > 0 && ImageContainer.ActualHeight > 0)
        {
            var containerLongSide = Math.Max(ImageContainer.ActualWidth, ImageContainer.ActualHeight);
            var targetSize = (uint)(containerLongSide * scaleFactor);
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] GetTargetDecodeLongSide: 视窗尺寸={ImageContainer.ActualWidth}x{ImageContainer.ActualHeight}, 系数={scaleFactor}, 解码尺寸={targetSize}");
            return targetSize;
        }
        else
        {
            var fallbackSize = 1080u;
            var targetSize = (uint)(fallbackSize * scaleFactor);
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] GetTargetDecodeLongSide: 视窗未布局，使用兜底尺寸={fallbackSize}, 系数={scaleFactor}, 解码尺寸={targetSize}");
            return targetSize;
        }
    }

    public async Task ShowAfterAnimationAsync()
    {
        if (_hasShown)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] ShowAfterAnimationAsync: 已显示过，跳过重复调用");
            return;
        }

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
        storyboard.Completed += (s, e) => tcs.SetResult(true);
        storyboard.Begin();
        await tcs.Task;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] ShowAfterAnimationAsync: 动画完成");

        // 获取焦点，确保键盘事件能够被正确处理
        this.Focus(FocusState.Programmatic);
        System.Diagnostics.Debug.WriteLine($"[ImageViewer] ShowAfterAnimationAsync: 已设置焦点");

        _hasShown = true;
        await SwitchToViewerLayerAsync();
    }

    private async Task SwitchToViewerLayerAsync()
    {
        if (_isViewerLayerReady)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 已切换过，跳过");
            return;
        }

        int retryCount = 0;
        while ((ImageContainer.ActualWidth <= 0 || ImageContainer.ActualHeight <= 0) && retryCount < 50)
        {
            await Task.Delay(10);
            retryCount++;
            ImageContainer.UpdateLayout();
        }

        if (ImageContainer.ActualWidth <= 0 || ImageContainer.ActualHeight <= 0)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 布局未完成，跳过");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 布局完成, ContainerWidth={ImageContainer.ActualWidth}, ContainerHeight={ImageContainer.ActualHeight}");

        _isViewerLayerReady = true;

        _targetDecodeLongSide = GetTargetDecodeLongSide();
        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 解码最长边={_targetDecodeLongSide}");

        StopPhysics();
        MainImage.Source = _imageFileInfo?.Thumbnail;
        MainImage.Stretch = Stretch.Uniform;
        StartPhysics();

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 使用缩略图");

        AnimationImage.Opacity = 0;
        ImageContainer.Opacity = 1;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 已切换到查看层");

        ResetViewer();

        _highResLoadTask = LoadHighResolutionImageAsync();
        _ = WaitForHighResAndReplaceAsync();
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

        ApplyTransform();
    }

    private async Task WaitForHighResAndReplaceAsync()
    {
        try
        {
            _isLoadingHighRes = true;
            var highResResult = await _highResLoadTask;
            
            if (_isClosing || !_isRunning)
            {
                _isLoadingHighRes = false;
                return;
            }

            if (highResResult?.ImageSource != null)
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    if (_isClosing || !_isRunning)
                    {
                        _isLoadingHighRes = false;
                        return;
                    }

                    StopPhysics();
                    MainImage.Source = highResResult.ImageSource;
                    System.Diagnostics.Debug.WriteLine($"[ImageViewer] WaitForHighResAndReplaceAsync: 高清图渐进式替换完成");
                    _isLoadingHighRes = false;
                    StartPhysics();

                    if (_imageFileInfo?.ImageFile != null)
                    {
                        _ = LoadExifAfterImageAsync(_imageFileInfo.ImageFile);
                    }
                });
            }
            else
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

    private async Task<DecodeResult?> LoadHighResolutionImageAsync()
    {
        try
        {
            if (_imageFileInfo?.ImageFile == null)
                return null;

            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 开始加载高清图, 解码最长边={_targetDecodeLongSide}");

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(5000);

            var thumbnailService = App.GetService<IThumbnailService>();
            var decodeResult = await thumbnailService.GetThumbnailWithSizeAsync(_imageFileInfo.ImageFile, _targetDecodeLongSide, cts.Token);

            if (decodeResult?.ImageSource != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 高清图加载完成, 实际解码尺寸={decodeResult.Width}x{decodeResult.Height}");
                return decodeResult;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 高清图加载失败");
                return null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadHighResolutionImageAsync error: {ex}");
            return null;
        }
    }

    public void PrepareCloseAnimation()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        StopPhysics();

        AnimationImage.Source = MainImage.Source;
        AnimationImage.Opacity = 1;
        ImageContainer.Opacity = 0;

        ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("BackConnectedAnimation", AnimationImage);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public void HandleRatingKey(Windows.System.VirtualKey key)
    {
        System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] HandleRatingKey: 处理数字键 {key}");
        
        if (key >= Windows.System.VirtualKey.Number1 && key <= Windows.System.VirtualKey.Number5)
        {
            // 处理数字键评级
            uint rating = (uint)(key - Windows.System.VirtualKey.Number1 + 1);
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] HandleRatingKey: 数字键评级, rating={rating}");
            
            if (ViewModel != null)
            {
                _ = ViewModel.SetRatingCommand.ExecuteAsync(rating);
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] HandleRatingKey: 执行了评级命令");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] HandleRatingKey: ViewModel 为 null，无法执行评级");
            }
        }
        else if (key == Windows.System.VirtualKey.Number0)
        {
            // 数字0清除评级
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] HandleRatingKey: 数字键清除评级");
            
            if (ViewModel != null)
            {
                _ = ViewModel.SetRatingCommand.ExecuteAsync(0);
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] HandleRatingKey: 执行了清除评级命令");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] HandleRatingKey: ViewModel 为 null，无法清除评级");
            }
        }
    }

    public async Task CompleteCloseAsync()
    {
        var storyboard = new Storyboard();

        var fadeOutBackground = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300)
        };
        Storyboard.SetTarget(fadeOutBackground, BackgroundOverlay);
        Storyboard.SetTargetProperty(fadeOutBackground, "Opacity");
        storyboard.Children.Add(fadeOutBackground);

        var fadeOutContainer = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300)
        };
        Storyboard.SetTarget(fadeOutContainer, AnimationContainer);
        Storyboard.SetTargetProperty(fadeOutContainer, "Opacity");
        storyboard.Children.Add(fadeOutContainer);

        var tcs = new TaskCompletionSource<bool>();
        storyboard.Completed += (s, e) => tcs.SetResult(true);
        storyboard.Begin();
        await tcs.Task;

        Visibility = Visibility.Collapsed;
    }

    public FrameworkElement GetMainImage() => AnimationImage;

    public UIElement[] GetCoordinatedElements() => new UIElement[] { InfoPanel };

    private void BackgroundOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        PrepareCloseAnimation();
    }

    private void ImageContainer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void InfoPanel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        PrepareCloseAnimation();
    }

    private void ImageTransformContainer_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_is1To1Scale)
        {
            _targetZoomScale = 1.0;
            _is1To1Scale = false;
        }
        else
        {
            _targetZoomScale = CalculateOriginalScale();
            _is1To1Scale = true;
        }
        _hasZoomAnchor = false;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 按键={e.Key}, e.Handled={e.Handled}");
            
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                PrepareCloseAnimation();
                e.Handled = true;
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 处理了Escape键");
            }
            else if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.Right ||
                     e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.Down)
            {
                e.Handled = true;
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 阻止了方向键 {e.Key}");
            }
            else if (e.Key >= Windows.System.VirtualKey.Number1 && e.Key <= Windows.System.VirtualKey.Number5)
            {
                // 处理数字键评级
                uint rating = (uint)(e.Key - Windows.System.VirtualKey.Number1 + 1);
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 数字键评级, rating={rating}");
                
                if (ViewModel != null)
                {
                    _ = ViewModel.SetRatingCommand.ExecuteAsync(rating);
                    e.Handled = true;
                    System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 执行了评级命令");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: ViewModel 为 null，无法执行评级");
                }
            }
            else if (e.Key == Windows.System.VirtualKey.Number0)
            {
                // 数字0清除评级
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 数字键清除评级");
                
                if (ViewModel != null)
                {
                    _ = ViewModel.SetRatingCommand.ExecuteAsync(0);
                    e.Handled = true;
                    System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 执行了清除评级命令");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: ViewModel 为 null，无法清除评级");
                }
            }
            base.OnKeyDown(e);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown 错误: {ex}");
        }
    }

    #region 物理引擎

    private void OnPhysicsRendering(object sender, object e)
    {
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

    #endregion

    #region 输入处理

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

        // 松手时清除速度，回弹时只按位置回弹，不保留拖拽惯性
        _velocityX = 0;
        _velocityY = 0;
    }

    private void ImageContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ImageClip.Rect = new Windows.Foundation.Rect(0, 0, ImageContainer.ActualWidth, ImageContainer.ActualHeight);
    }

    #endregion

    #region 边界处理

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

    /// <summary>
    /// 计算 X 轴当前超出边界的距离
    /// </summary>
    /// <returns>正数表示向右超出，负数表示向左超出，0 表示在边界内</returns>
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

    /// <summary>
    /// 计算 Y 轴当前超出边界的距离
    /// </summary>
    /// <returns>正数表示向下超出，负数表示向上超出，0 表示在边界内</returns>
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

    /// <summary>
    /// 应用橡皮筋效果：越超出边界，移动越困难
    /// </summary>
    /// <param name="delta">原始的拖拽距离</param>
    /// <param name="offset">当前超出边界的距离</param>
    /// <returns>经过阻尼调整后的实际移动距离</returns>
    private double ApplyRubberBand(double delta, double offset)
    {
        if (offset == 0)
            return delta;

        double factor = 1.0 / (Math.Abs(offset) * RubberBandResistance + 1);
        return delta * factor;
    }

    /// <summary>
    /// 应用弹簧回弹效果（仅在松手后调用）
    /// 直接平滑回弹到边界内，不保留拖拽速度
    /// </summary>
    /// <returns>是否需要更新 UI</returns>
    private bool ApplyBoundsWithSpring()
    {
        bool changed = false;

        double offsetX = GetBoundsOffsetX();
        double offsetY = GetBoundsOffsetY();

        // X 轴回弹：直接向边界移动，忽略拖拽速度
        if (Math.Abs(offsetX) > SpringEpsilon)
        {
            // 直接向目标位置平滑移动
            _translateX -= offsetX * SpringStiffness;
            changed = true;
        }
        else if (Math.Abs(offsetX) > 0)
        {
            // 距离足够小时直接归位
            _translateX -= offsetX;
            changed = true;
        }

        // Y 轴回弹
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

    #endregion

    #region 辅助方法

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

    #endregion

    #region 信息面板事件处理

    private void InfoItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"];
        }
    }

    private void InfoItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private void FileName_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.ImageName);
        e.Handled = true;
    }

    private void DateTime_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.FormattedDateTime);
        e.Handled = true;
    }

    private void Resolution_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.Resolution);
        e.Handled = true;
    }

    private void FileSize_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.FileSize);
        e.Handled = true;
    }

    private void Dpi_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.Dpi);
        e.Handled = true;
    }

    private void BitDepth_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.BitDepth);
        e.Handled = true;
    }

    private void RatingSource_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.RatingSource);
        e.Handled = true;
    }

    private void CameraModel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.DeviceInfo.Count > 0)
        {
            CopyToClipboard(string.Join(" ", ViewModel.DeviceInfo));
        }
        e.Handled = true;
    }

    private void LensModel_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.LensModel);
        e.Handled = true;
    }

    private void FocalLength_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.FocalLength);
        e.Handled = true;
    }

    private void Exposure_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var text = $"{ViewModel.ExposureTime} {ViewModel.FNumber} {ViewModel.Iso}";
        CopyToClipboard(text);
        e.Handled = true;
    }

    private void ExposureTime_Tapped(object sender, TappedRoutedEventArgs e)
    {
        var text = $"{ViewModel.ExposureTime}  {ViewModel.FNumber}  {ViewModel.Iso}";
        CopyToClipboard(text);
        e.Handled = true;
    }

    private void ExposureProgram_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.ExposureProgram);
        e.Handled = true;
    }

    private void ExposureBias_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.ExposureBias);
        e.Handled = true;
    }

    private void Flash_Tapped(object sender, TappedRoutedEventArgs e)
    {
        CopyToClipboard(ViewModel.Flash);
        e.Handled = true;
    }

    private void PathText_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.Text is string path)
        {
            _ = ViewModel.OpenInExplorerAsync(path);
        }
        e.Handled = true;
    }

    private void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        _ = ViewModel.CopyToClipboardAsync(text);
    }

    private void ImageRatingControl_ValueChanged(Microsoft.UI.Xaml.Controls.RatingControl sender, object args)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] ImageRatingControl_ValueChanged: 触发事件");
            
            if (ViewModel == null)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] ImageRatingControl_ValueChanged: ViewModel 为 null，退出");
                return;
            }
            
            double senderValue = sender.Value;
            uint viewModelRating = ViewModel.Rating;
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] ImageRatingControl_ValueChanged: sender.Value={senderValue}, ViewModel.Rating={viewModelRating}");
            
            uint newRating = (uint)senderValue;
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] ImageRatingControl_ValueChanged: 准备执行 SetRatingCommand, newRating={newRating}");
            
            // 无论值是否变化都执行命令，确保评级操作能够成功
            _ = ViewModel.SetRatingCommand.ExecuteAsync(newRating);
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] ImageRatingControl_ValueChanged: 执行了 SetRatingCommand");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] ImageRatingControl_ValueChanged 错误: {ex}");
        }
    }

    private async Task LoadExifAfterImageAsync(StorageFile file)
    {
        try
        {
            _isLoadingExif = true;
            await ViewModel.LoadFileDetailsAsync(file);
            _isLoadingExif = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadExifAfterImageAsync error: {ex}");
            _isLoadingExif = false;
        }
    }

    #endregion
}
