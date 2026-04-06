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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PhotoView.Controls;

public sealed partial class ImageViewerControl : UserControl
{
    public event EventHandler? Closed;

    private ImageFileInfo? _imageFileInfo;
    private bool _is1To1Scale = false;
    private double _originalZoomFactor = 1.0;
    private bool _hasAppliedInitialZoom = false;
    private ImageSource? _pendingHighResImageSource = null;

    public ImageViewerControl()
    {
        InitializeComponent();
    }

    public void PrepareContent(ImageFileInfo imageFileInfo)
    {
        _imageFileInfo = imageFileInfo;
        mainImage.Source = imageFileInfo.Thumbnail;
        ImageNameTextBox.Text = imageFileInfo.ImageName;
        ResolutionTextBlock.Text = $"{imageFileInfo.Width} x {imageFileInfo.Height}";
        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 已设置缩略图, 文件名={imageFileInfo.ImageName}, 尺寸={imageFileInfo.Width}x{imageFileInfo.Height}");
        _ = LoadFileSizeAsync();
        _ = LoadFilePathsAsync();
        _ = LoadImagePropertiesAsync();
        
        // 立即开始后台加载高清图（双缓冲机制）
        _ = LoadHighResolutionImageAsync();
    }

    private async Task LoadImagePropertiesAsync()
    {
        try
        {
            if (_imageFileInfo?.ImageFile != null)
            {
                var props = await _imageFileInfo.ImageFile.Properties.GetImagePropertiesAsync();
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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadImagePropertiesAsync error: {ex}");
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

        // 只设置标志位，让 ConnectedAnimation 保持原样，不调用 ApplyInitialZoomToFit()
        _hasAppliedInitialZoom = true;
        // ApplyInitialZoomToFit();
    }

    private void ApplyInitialZoomToFit()
    {
        if (_imageFileInfo == null)
            return;

        var viewerWidth = ImageScrollViewer.ActualWidth;
        var viewerHeight = ImageScrollViewer.ActualHeight;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] ApplyInitialZoomToFit: viewerWidth={viewerWidth}, viewerHeight={viewerHeight}");

        if (viewerWidth <= 0 || viewerHeight <= 0)
            return;

        var imageWidth = (double)_imageFileInfo.Width;
        var imageHeight = (double)_imageFileInfo.Height;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] ApplyInitialZoomToFit: imageWidth={imageWidth}, imageHeight={imageHeight}");

        if (imageWidth <= 0 || imageHeight <= 0)
            return;

        var scaleX = viewerWidth / imageWidth;
        var scaleY = viewerHeight / imageHeight;
        var fitScale = Math.Min(scaleX, scaleY);

        fitScale = Math.Max(0.1, fitScale); // 去掉了上限 1.0

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] ApplyInitialZoomToFit: scaleX={scaleX:F3}, scaleY={scaleY:F3}, fitScale={fitScale:F3}");

        ImageScrollViewer.ChangeView(null, null, (float)fitScale);
        _originalZoomFactor = fitScale;
        _is1To1Scale = false;
        _hasAppliedInitialZoom = true;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] ApplyInitialZoomToFit: 已设置 _hasAppliedInitialZoom=true");

        if (_pendingHighResImageSource != null)
        {
            mainImage.Source = _pendingHighResImageSource;
            _pendingHighResImageSource = null;

            // 关键修复：强制刷新布局
            mainImage.UpdateLayout();
            ImageScrollViewer.InvalidateScrollInfo();
        }
    }

    private async Task LoadFileSizeAsync()
    {
        try
        {
            if (_imageFileInfo?.ImageFile != null)
            {
                var props = await _imageFileInfo.ImageFile.GetBasicPropertiesAsync();
                FileSizeTextBlock.Text = FormatFileSize(props.Size);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadFileSizeAsync error: {ex}");
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

    private async Task LoadFilePathsAsync()
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
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4)),
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



    private async Task LoadHighResolutionImageAsync()
    {
        try
        {
            if (_imageFileInfo?.ImageFile == null)
                return;

            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 开始加载高清图, 文件名={_imageFileInfo.ImageName}");

            // 在 UI 线程上获取 ActualWidth 和 ActualHeight
            double viewerWidth = 0, viewerHeight = 0;
            var tcs = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    viewerWidth = ImageScrollViewer.ActualWidth;
                    viewerHeight = ImageScrollViewer.ActualHeight;
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            await tcs.Task;

            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: viewerWidth={viewerWidth}, viewerHeight={viewerHeight}");

            // 如果尺寸为 0，说明控件还没布局完成，等待一下
            int retryCount = 0;
            while ((viewerWidth <= 0 || viewerHeight <= 0) && retryCount < 10)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 尺寸为 0，等待布局完成，重试次数={retryCount}");
                await Task.Delay(50);
                
                tcs = new TaskCompletionSource<bool>();
                DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        viewerWidth = ImageScrollViewer.ActualWidth;
                        viewerHeight = ImageScrollViewer.ActualHeight;
                        tcs.SetResult(true);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                await tcs.Task;
                
                retryCount++;
            }

            if (viewerWidth <= 0 || viewerHeight <= 0)
            {
                viewerWidth = 800;
                viewerHeight = 600;
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 使用默认尺寸 {viewerWidth}x{viewerHeight}");
            }

            // 取查看器的最长边作为目标尺寸
            var targetLongSide = (uint)Math.Max(viewerWidth, viewerHeight);
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 查看器最长边={targetLongSide}, 开始用 WIC 解码");

            var thumbnailService = App.GetService<IThumbnailService>();
            var imageSource = await thumbnailService.GetThumbnailByLongSideAsync(_imageFileInfo.ImageFile, targetLongSide, CancellationToken.None);

            if (imageSource != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 高清图加载完成, _hasAppliedInitialZoom={_hasAppliedInitialZoom}");
                
                if (_hasAppliedInitialZoom)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        // 1. 保存当前缩放和滚动位置
                        var currentZoom = ImageScrollViewer.ZoomFactor;
                        var currentHorizontalOffset = ImageScrollViewer.HorizontalOffset;
                        var currentVerticalOffset = ImageScrollViewer.VerticalOffset;

                        System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 保存状态, zoom={currentZoom}, offset=({currentHorizontalOffset},{currentVerticalOffset})");

                        // 2. 替换图片
                        mainImage.Source = imageSource;

                        // 3. 立即恢复缩放和滚动位置（无动画）
                        ImageScrollViewer.ChangeView(
                            horizontalOffset: currentHorizontalOffset,
                            verticalOffset: currentVerticalOffset,
                            zoomFactor: (float?)currentZoom,
                            disableAnimation: true);

                        System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 已恢复状态");
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 还未应用初始缩放，保存等待");
                    _pendingHighResImageSource = imageSource;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 高清图加载失败, imageSource 为 null");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadHighResolutionImageAsync error: {ex}");
        }
    }

    public void PrepareCloseAnimation()
    {
        ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("BackConnectedAnimation", mainImage);
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

    public FrameworkElement GetMainImage() => mainImage;

    public UIElement[] GetCoordinatedElements() => new UIElement[] { InfoPanel };

    private void BackgroundOverlay_Tapped(object sender, TappedRoutedEventArgs e)
    {
        PrepareCloseAnimation();
    }

    private void ImageScrollViewer_Tapped(object sender, TappedRoutedEventArgs e)
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

    private void mainImage_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_is1To1Scale)
        {
            ImageScrollViewer.ChangeView(null, null, (float?)_originalZoomFactor);
            _is1To1Scale = false;
        }
        else
        {
            _originalZoomFactor = ImageScrollViewer.ZoomFactor;
            ImageScrollViewer.ChangeView(null, null, 1.0f);
            _is1To1Scale = true;
        }
        e.Handled = true;
    }

    private void ImageScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var scrollViewer = sender as ScrollViewer;
        if (scrollViewer == null) return;

        var pointerPoint = e.GetCurrentPoint(scrollViewer);
        var delta = pointerPoint.Properties.MouseWheelDelta;

        double oldZoomFactor = scrollViewer.ZoomFactor;
        double newZoomFactor = oldZoomFactor;

        if (delta > 0)
        {
            newZoomFactor *= 1.1;
        }
        else if (delta < 0)
        {
            newZoomFactor /= 1.1;
        }

        newZoomFactor = Math.Max(0.1, Math.Min(newZoomFactor, 5.0));

        if (Math.Abs(newZoomFactor - oldZoomFactor) < 0.001)
        {
            e.Handled = true;
            return;
        }

        var pointerPosition = pointerPoint.Position;

        var contentWidth = scrollViewer.ExtentWidth * oldZoomFactor;
        var contentHeight = scrollViewer.ExtentHeight * oldZoomFactor;

        var pointerXRatio = (scrollViewer.HorizontalOffset + pointerPosition.X) / contentWidth;
        var pointerYRatio = (scrollViewer.VerticalOffset + pointerPosition.Y) / contentHeight;

        var newContentWidth = scrollViewer.ExtentWidth * newZoomFactor;
        var newContentHeight = scrollViewer.ExtentHeight * newZoomFactor;

        var newHorizontalOffset = pointerXRatio * newContentWidth - pointerPosition.X;
        var newVerticalOffset = pointerYRatio * newContentHeight - pointerPosition.Y;

        newHorizontalOffset = Math.Max(0, Math.Min(newHorizontalOffset, newContentWidth - scrollViewer.ViewportWidth));
        newVerticalOffset = Math.Max(0, Math.Min(newVerticalOffset, newContentHeight - scrollViewer.ViewportHeight));

        scrollViewer.ChangeView(newHorizontalOffset, newVerticalOffset, (float)newZoomFactor, true);

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
}
