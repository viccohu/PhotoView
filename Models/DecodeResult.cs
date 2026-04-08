using Microsoft.UI.Xaml.Media;

namespace PhotoView.Models;

public class DecodeResult
{
    public uint Width { get; set; }
    public uint Height { get; set; }
    public ImageSource? ImageSource { get; set; }

    public DecodeResult(uint width, uint height, ImageSource? imageSource)
    {
        Width = width;
        Height = height;
        ImageSource = imageSource;
    }
}
