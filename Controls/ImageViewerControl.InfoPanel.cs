using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoView.Models;
using System;
using System.Threading.Tasks;

namespace PhotoView.Controls;

public sealed partial class ImageViewerControl
{
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
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _ = ViewModel.CopyToClipboardAsync(text);
    }

    private void ImageRatingControl_ValueChanged(RatingControl sender, object args)
    {
        try
        {
            var newRating = sender.Value < 0
                ? -1
                : Math.Clamp((int)Math.Round(sender.Value, MidpointRounding.AwayFromZero), 0, 5);

            _ = ViewModel.SetRatingCommand.ExecuteAsync(newRating);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] ImageRatingControl_ValueChanged error: {ex}");
        }
    }

    private async Task LoadExifAfterImageAsync(ImageFileInfo imageFileInfo)
    {
        try
        {
            await ViewModel.LoadFileDetailsAsync(imageFileInfo.ImageFile, imageFileInfo.DateTaken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageViewer] LoadExifAfterImageAsync error: {ex}");
        }
    }
}
