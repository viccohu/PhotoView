using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using PhotoView.Models;
using System;
using System.Threading;

namespace PhotoView.ViewModels;

// Legacy unconnected path. MainPage currently binds ImageFileInfo directly.
public partial class ThumbnailItemViewModel : ObservableObject
{
    private const double MinWidth = 96d;
    private const double FallbackWidth = 200d;
    private int _requestVersion;

    public ThumbnailItemViewModel(ImageFileInfo file, ThumbnailSize size)
    {
        File = file;
        _thumbnailSize = size;
    }

    public ImageFileInfo File { get; }

    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private ThumbnailSize _thumbnailSize;

    public double ThumbnailHeight => (int)ThumbnailSize;

    public double ThumbnailWidth
    {
        get
        {
            if (File.Height <= 0 || File.Width <= 0)
            {
                return FallbackWidth;
            }

            var width = File.Width * ThumbnailHeight / File.Height;
            return Math.Max(MinWidth, width);
        }
    }

    public void UpdateSize(ThumbnailSize size)
    {
        if (ThumbnailSize == size)
        {
            return;
        }

        ThumbnailSize = size;
        OnPropertyChanged(nameof(ThumbnailHeight));
        OnPropertyChanged(nameof(ThumbnailWidth));
    }

    public void ClearThumbnail()
    {
        Thumbnail = null;
        IsLoading = false;
    }

    public int BeginLoad(ThumbnailSize size)
    {
        UpdateSize(size);
        Thumbnail = null;
        IsLoading = true;
        return Interlocked.Increment(ref _requestVersion);
    }

    public bool IsCurrentRequest(int requestVersion, ThumbnailSize size)
    {
        return requestVersion == _requestVersion && ThumbnailSize == size;
    }
}
