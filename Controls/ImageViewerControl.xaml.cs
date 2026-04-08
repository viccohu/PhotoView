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

    public ImageViewerControl()
    {
        InitializeComponent();
    }

    public void PrepareContent(ImageFileInfo imageFileInfo)
    {
        _imageFileInfo = imageFileInfo;
        _is1To1Scale = false;

        // 在 UI 线程获取分辨率
        _targetDecodeLongSide = GetTargetDecodeLongSide();
        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 解码最长边={_targetDecodeLongSide}");

        // 阶段1：只设置动画层
        AnimationImage.Source = imageFileInfo.Thumbnail;
        ImageNameTextBox.Text = imageFileInfo.ImageName;
        ResolutionTextBlock.Text = $"{imageFileInfo.Width} x {imageFileInfo.Height}";

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 已设置动画层缩略图, 文件名={imageFileInfo.ImageName}");

        // 启动高清图异步加载（不等待）
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
        var monitorLongSide = GetMonitorLongSide();

        uint targetLongSide = monitorLongSide switch
        {
            <= 1920 => 1920,
            <= 2560 => 2560,
            <= 3840 => 3840,
            _ => monitorLongSide
        };

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] GetTargetDecodeLongSide: 显示器最长边={monitorLongSide}, 解码最长边={targetLongSide}");
        return targetLongSide;
    }

    public async Task ShowAfterAnimationAsync()
    {
        Visibility = Visibility.Visible;

        // 入场动画
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

        // 等待高清图加载完成（带超时）
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

        // 切换到查看层
        await SwitchToViewerLayerAsync(highResResult);
    }

    private async Task SwitchToViewerLayerAsync(DecodeResult? highResResult)
    {
        // 1. 等待布局完成
        int retryCount = 0;
        while ((ImageScrollView.ViewportWidth <= 0 || ImageScrollView.ViewportHeight <= 0) && retryCount < 50)
        {
            await Task.Delay(10);
            retryCount++;
            ImageScrollView.UpdateLayout();
        }

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 布局完成, ViewportWidth={ImageScrollView.ViewportWidth}, ViewportHeight={ImageScrollView.ViewportHeight}");

        // 2. 设置查看层图片（优先使用高清图）
        var source = highResResult?.ImageSource ?? _imageFileInfo?.Thumbnail;
        MainImage.Source = source;
        MainImage.Stretch = Stretch.Uniform;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 使用 {(highResResult != null ? "高清图" : "缩略图")}");

        // 3. 切换层
        AnimationImage.Opacity = 0;
        ImageScrollView.Opacity = 1;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] SwitchToViewerLayerAsync: 已切换到查看层");

        // 4. 如果高清图还没加载完，启动渐进式加载
        if (highResResult == null && _highResLoadTask != null)
        {
            _ = WaitForHighResAndReplaceAsync();
        }
    }

    private async Task WaitForHighResAndReplaceAsync()
    {
        try
        {
            var highResResult = await _highResLoadTask;
            if (highResResult?.ImageSource != null)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    MainImage.Source = highResResult.ImageSource;
                    System.Diagnostics.Debug.WriteLine($"[ImageViewer] WaitForHighResAndReplaceAsync: 高清图渐进式替换完成");
                });
            }
        }
        catch (Exception ex)
        {
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
        // 准备返回动画（使用 AnimationImage）
        AnimationImage.Source = MainImage.Source;
        AnimationImage.Opacity = 1;
        ImageScrollView.Opacity = 0;

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
            ImageScrollView.ZoomTo((float)_fitScale, null);
            _is1To1Scale = false;
        }
        else
        {
            ImageScrollView.ZoomTo(1.0f, null);
            _is1To1Scale = true;
        }
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
