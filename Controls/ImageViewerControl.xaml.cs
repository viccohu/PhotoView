using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Contracts.Services;
using PhotoView.Models;
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

    private ImageFileInfo? _imageFileInfo;
    private bool _is1To1Scale = false;
    private double _fitScale = 1.0;
    private uint _targetDecodeLongSide = 1920;
    private Task<DecodeResult?>? _highResLoadTask;

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
        InitializeComponent();
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
        _imageFileInfo = imageFileInfo;
        _is1To1Scale = false;
        _isClosing = false;
        _isViewerLayerReady = false;

        _targetDecodeLongSide = GetTargetDecodeLongSide();
        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 解码最长边={_targetDecodeLongSide}");

        AnimationImage.Source = imageFileInfo.Thumbnail;
        ImageNameTextBox.Text = imageFileInfo.ImageName;
        ResolutionTextBlock.Text = $"{imageFileInfo.Width} x {imageFileInfo.Height}";

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 已设置动画层缩略图, 文件名={imageFileInfo.ImageName}");

        _highResLoadTask = LoadHighResolutionImageAsync();

        _ = LoadFileInfoAsync();
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

        DecodeResult? highResResult = null;
        if (_highResLoadTask != null)
        {
            var timeoutTask = Task.Delay(2000);
            var completedTask = await Task.WhenAny(_highResLoadTask, timeoutTask);
            if (completedTask == _highResLoadTask)
            {
                highResResult = await _highResLoadTask;
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] ShowAfterAnimationAsync: 高清图已加载");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] ShowAfterAnimationAsync: 高清图加载超时，使用缩略图");
            }
        }

        await SwitchToViewerLayerAsync(highResResult);
    }

    private async Task SwitchToViewerLayerAsync(DecodeResult? highResResult)
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

        StopPhysics();
        var source = highResResult?.ImageSource ?? _imageFileInfo?.Thumbnail;
        MainImage.Source = source;
        MainImage.Stretch = Stretch.Uniform;
        StartPhysics();

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 使用 {(highResResult != null ? "高清图" : "缩略图")}");

        AnimationImage.Opacity = 0;
        ImageContainer.Opacity = 1;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 已切换到查看层");

        ResetViewer();

        if (highResResult == null && _highResLoadTask != null)
        {
            _ = WaitForHighResAndReplaceAsync();
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
                DispatcherQueue.TryEnqueue(() =>
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

    private async Task LoadFileInfoAsync()
    {
        try
        {
            if (_imageFileInfo?.ImageFile == null)
                return;

            var file = _imageFileInfo.ImageFile;
            var fileExtension = Path.GetExtension(file.Name);
            var isRawFile = RawFileExtensions.Contains(fileExtension);

            try
            {
                var basicProps = await file.GetBasicPropertiesAsync();
                FileSizeTextBlock.Text = FormatFileSize(basicProps.Size);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadFileInfoAsync - 文件大小读取错误: {ex}");
            }

            LoadFilePaths();

            try
            {
                if (isRawFile)
                {
                    await LoadImagePropertiesFromWicAsync(file);
                }
                else
                {
                    await LoadImagePropertiesFromWinRTAsync(file);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadFileInfoAsync - EXIF读取错误: {ex}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFileInfoAsync error: {ex}");
        }
    }

    private void LoadFilePaths()
    {
        FilePathPanel.Children.Clear();

        if (_imageFileInfo == null)
            return;

        var allFiles = new List<ImageFileInfo>();

        if (_imageFileInfo.Group != null)
        {
            allFiles.AddRange(_imageFileInfo.Group.Images);
        }
        else
        {
            allFiles.Add(_imageFileInfo);
        }

        foreach (var file in allFiles)
        {
            if (file.ImageFile == null)
                continue;

            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathTextBlock = new TextBlock
            {
                Text = file.ImageFile.Path,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)),
                IsTextSelectionEnabled = true
            };

            pathTextBlock.Tapped += (s, e) =>
            {
                try
                {
                    var folderPath = Path.GetDirectoryName(file.ImageFile.Path);
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{file.ImageFile.Path}\"",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Open folder error: {ex}");
                }
                e.Handled = true;
            };

            var copyButton = new Button
            {
                Content = new FontIcon { Glyph = "\uE8C8" },
                Padding = new Thickness(8)
            };

            copyButton.Click += (s, e) =>
            {
                try
                {
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(file.ImageFile.Path);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Copy path error: {ex}");
                }
            };

            Grid.SetColumn(pathTextBlock, 0);
            Grid.SetColumn(copyButton, 1);

            grid.Children.Add(pathTextBlock);
            grid.Children.Add(copyButton);

            FilePathPanel.Children.Add(grid);
        }
    }

    private async Task LoadImagePropertiesFromWinRTAsync(StorageFile file)
    {
        var props = await file.Properties.GetImagePropertiesAsync();
        try
        {
            var dateTaken = props.DateTaken;
            CaptureDatePicker.Date = dateTaken.Date;
            CaptureTimePicker.Time = dateTaken.TimeOfDay;
        }
        catch
        {
        }

        var deviceInfo = new List<string>();
        if (!string.IsNullOrEmpty(props.CameraManufacturer))
        {
            deviceInfo.Add(props.CameraManufacturer);
        }
        if (!string.IsNullOrEmpty(props.CameraModel))
        {
            deviceInfo.Add(props.CameraModel);
        }

        if (deviceInfo.Count > 0)
        {
            DeviceInfoPanel.Children.Clear();
            foreach (var info in deviceInfo)
            {
                var textBlock = new TextBlock
                {
                    Text = info,
                    Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
                };
                DeviceInfoPanel.Children.Add(textBlock);
            }
            DeviceInfoPanel.Visibility = Visibility.Visible;
        }
    }

    private async Task LoadImagePropertiesFromWicAsync(StorageFile file)
    {
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);

        try
        {
            var properties = await decoder.BitmapProperties.GetPropertiesAsync(
                new[] { "System.Photo.DateTaken" });

            if (properties.TryGetValue("System.Photo.DateTaken", out var dateProp))
            {
                if (dateProp.Value is DateTime dateTaken)
                {
                    CaptureDatePicker.Date = dateTaken.Date;
                    CaptureTimePicker.Time = dateTaken.TimeOfDay;
                }
            }
        }
        catch
        {
        }

        try
        {
            var properties = await decoder.BitmapProperties.GetPropertiesAsync(
                new[] { "System.Photo.CameraManufacturer", "System.Photo.CameraModel" });

            var deviceInfo = new List<string>();
            if (properties.TryGetValue("System.Photo.CameraManufacturer", out var manuProp))
            {
                if (manuProp.Value is string manufacturer && !string.IsNullOrEmpty(manufacturer))
                {
                    deviceInfo.Add(manufacturer);
                }
            }
            if (properties.TryGetValue("System.Photo.CameraModel", out var modelProp))
            {
                if (modelProp.Value is string model && !string.IsNullOrEmpty(model))
                {
                    deviceInfo.Add(model);
                }
            }

            if (deviceInfo.Count > 0)
            {
                DeviceInfoPanel.Children.Clear();
                foreach (var info in deviceInfo)
                {
                    var textBlock = new TextBlock
                    {
                        Text = info,
                        Style = (Style)Application.Current.Resources["BodyTextBlockStyle"]
                    };
                    DeviceInfoPanel.Children.Add(textBlock);
                }
                DeviceInfoPanel.Visibility = Visibility.Visible;
            }
        }
        catch
        {
        }
    }

    private static string FormatFileSize(ulong size)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double len = size;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.0} {sizes[order]}";
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
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            PrepareCloseAnimation();
            e.Handled = true;
        }
        base.OnKeyDown(e);
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
}
