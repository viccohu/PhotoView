using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PhotoView.Controls;

public sealed partial class ImageViewerControl : UserControl
{
    public event EventHandler? Closed;

    private ImageFileInfo? _imageFileInfo;
    private bool _is1To1Scale = false;
    private double _originalZoomFactor = 1.0;

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
        _ = LoadFileSizeAsync();
        _ = LoadFilePathsAsync();
        _ = LoadImagePropertiesAsync();
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
        
        _ = LoadHighResolutionImageAfterAnimationAsync();
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

    private async Task LoadHighResolutionImageAfterAnimationAsync()
    {
        try
        {
            await Task.Delay(300);
            await LoadHighResolutionImageAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadHighResolutionImageAfterAnimationAsync error: {ex}");
        }
    }

    private async Task LoadHighResolutionImageAsync()
    {
        try
        {
            if (_imageFileInfo?.ImageFile == null)
                return;
                
            var displayInfo = DisplayInformation.GetForCurrentView();
            var dpiScale = displayInfo.RawPixelsPerViewPixel;
            var screenWidth = (uint)(1920 * dpiScale);
            var screenHeight = (uint)(1080 * dpiScale);
            
            var decodeWidth = Math.Min(screenWidth, (uint)_imageFileInfo.Width);
            var decodeHeight = Math.Min(screenHeight, (uint)_imageFileInfo.Height);
            
            using var stream = await _imageFileInfo.ImageFile.OpenReadAsync();
            
            var bitmap = new BitmapImage();
            bitmap.DecodePixelWidth = (int)decodeWidth;
            await bitmap.SetSourceAsync(stream);
            
            DispatcherQueue.TryEnqueue(() =>
            {
                mainImage.Source = bitmap;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadHighResolutionImageAsync error: {ex}");
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
