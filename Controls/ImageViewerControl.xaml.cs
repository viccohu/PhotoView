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
    private bool _hasCalculatedFitScale = false;

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
        _hasCalculatedFitScale = false;

        mainImage.Source = imageFileInfo.Thumbnail;
        ImageNameTextBox.Text = imageFileInfo.ImageName;
        ResolutionTextBlock.Text = $"{imageFileInfo.Width} x {imageFileInfo.Height}";
        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 已设置缩略图, 文件名={imageFileInfo.ImageName}, 尺寸={imageFileInfo.Width}x{imageFileInfo.Height}");

        _ = LoadFileInfoAsync();
    }

    public async Task PrepareForAnimationAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareForAnimationAsync: 开始等待布局完成");

        int retryCount = 0;
        while ((ImageScrollView.ViewportWidth <= 0 || ImageScrollView.ViewportHeight <= 0) && retryCount < 50)
        {
            await Task.Delay(10);
            retryCount++;
            ImageScrollView.UpdateLayout();
        }

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareForAnimationAsync: 布局完成, ViewportWidth={ImageScrollView.ViewportWidth}, ViewportHeight={ImageScrollView.ViewportHeight}, 重试次数={retryCount}");

        CalculateFitScale();

        _ = LoadHighResolutionImageAsync();
    }

    private void CalculateFitScale()
    {
        if (mainImage.Source is not BitmapSource currentSource)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] CalculateFitScale: Source 不是 BitmapSource");
            return;
        }

        double vWidth = ImageScrollView.ViewportWidth;
        double vHeight = ImageScrollView.ViewportHeight;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] CalculateFitScale: vWidth={vWidth}, vHeight={vHeight}");

        if (vWidth <= 0 || vHeight <= 0)
            return;

        double iWidth = currentSource.PixelWidth;
        double iHeight = currentSource.PixelHeight;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] CalculateFitScale: iWidth={iWidth}, iHeight={iHeight}");

        if (iWidth <= 0 || iHeight <= 0)
            return;

        double scaleX = vWidth / iWidth;
        double scaleY = vHeight / iHeight;
        _fitScale = Math.Min(scaleX, scaleY);

        _fitScale = Math.Max(_fitScale, 0.1);

        ImageScrollView.ZoomTo((float)_fitScale, null);
        _hasCalculatedFitScale = true;

        System.Diagnostics.Debug.WriteLine($"[ImageViewer] CalculateFitScale: scaleX={scaleX:F3}, scaleY={scaleY:F3}, fitScale={_fitScale:F3}, 已应用缩放");
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
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadFileInfoAsync: 文件名={file.Name}, 扩展名={fileExtension}, 是否RAW={isRawFile}");

            try
            {
                var basicProps = await file.GetBasicPropertiesAsync();
                FileSizeTextBlock.Text = FormatFileSize(basicProps.Size);
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadFileInfoAsync: 文件大小读取完成");
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

    private async Task LoadHighResolutionImageAsync()
    {
        try
        {
            if (_imageFileInfo?.ImageFile == null)
                return;

            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 开始加载高清图, 文件名={_imageFileInfo.ImageName}");

            double viewerWidth = 800;
            double viewerHeight = 600;

            var tcs = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var vw = ImageScrollView.ViewportWidth;
                    var vh = ImageScrollView.ViewportHeight;
                    if (vw > 0 && vh > 0)
                    {
                        viewerWidth = vw;
                        viewerHeight = vh;
                    }
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 获取尺寸时出错，使用默认值: {ex}");
                    tcs.SetResult(true);
                }
            });
            await tcs.Task;

            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: viewerWidth={viewerWidth}, viewerHeight={viewerHeight}");

            var targetLongSide = (uint)Math.Max(viewerWidth, viewerHeight);
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 查看器最长边={targetLongSide}, 开始用 WIC 解码");

            var thumbnailService = App.GetService<IThumbnailService>();
            var imageSource = await thumbnailService.GetThumbnailByLongSideAsync(_imageFileInfo.ImageFile, targetLongSide, CancellationToken.None);

            if (imageSource != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 高清图加载完成");

                DispatcherQueue.TryEnqueue(() =>
                {
                    var visualWidth = mainImage.ActualWidth;
                    var visualHeight = mainImage.ActualHeight;

                    mainImage.Width = visualWidth;
                    mainImage.Height = visualHeight;

                    mainImage.Source = imageSource;

                    mainImage.UpdateLayout();

                    if (imageSource is BitmapSource newSource)
                    {
                        double vWidth = ImageScrollView.ViewportWidth;
                        double vHeight = ImageScrollView.ViewportHeight;

                        if (vWidth > 0 && vHeight > 0)
                        {
                            double iWidth = newSource.PixelWidth;
                            double iHeight = newSource.PixelHeight;

                            double scaleX = vWidth / iWidth;
                            double scaleY = vHeight / iHeight;
                            _fitScale = Math.Min(scaleX, scaleY);
                            _fitScale = Math.Max(_fitScale, 0.1);
                        }
                    }

                    mainImage.ClearValue(FrameworkElement.WidthProperty);
                    mainImage.ClearValue(FrameworkElement.HeightProperty);

                    ImageScrollView.ZoomTo((float)_fitScale, null);

                    System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadHighResolutionImageAsync: 高清图比例已校正为: {_fitScale:F3}");
                });
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
