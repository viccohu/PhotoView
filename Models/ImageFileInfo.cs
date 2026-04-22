using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PhotoView.Helpers;
using PhotoView.Services;
using Windows.Graphics.Display;
using Windows.Storage.FileProperties;
using Windows.Storage;

namespace PhotoView.Models;

public partial class ImageFileInfo : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isPendingDelete;
    private int _width;
    private int _height;
    private string _imageTitle = string.Empty;
    private double _displayWidth;
    private double _displayHeight;

    public ImageFileInfo(
        int width,
        int height,
        string title,
        StorageFile imageFile,
        string name,
        string type)
    {
        _width = width;
        _height = height;
        _imageTitle = title;
        ImageFile = imageFile ?? throw new ArgumentNullException(nameof(imageFile));
        ImageName = name;
        ImageFileType = type;
        _isSelected = false;
        UpdateDisplaySize(ThumbnailSize.Medium);
        UpdateFormatTags();
    }

    public int Width
    {
        get => _width;
        private set
        {
            if (SetProperty(ref _width, value))
            {
                OnPropertyChanged(nameof(ImageDimensions));
                OnPropertyChanged(nameof(AutoWidth));
                OnPropertyChanged(nameof(AspectRatio));
            }
        }
    }

    public int Height
    {
        get => _height;
        private set
        {
            if (SetProperty(ref _height, value))
            {
                OnPropertyChanged(nameof(ImageDimensions));
                OnPropertyChanged(nameof(AutoWidth));
                OnPropertyChanged(nameof(AspectRatio));
            }
        }
    }

    public StorageFile ImageFile { get; }


    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsPendingDelete
    {
        get => _isPendingDelete;
        set => SetProperty(ref _isPendingDelete, value);
    }

    public double DisplayWidth
    {
        get => _displayWidth;
        private set => SetProperty(ref _displayWidth, value);
    }

    public double DisplayHeight
    {
        get => _displayHeight;
        private set => SetProperty(ref _displayHeight, value);
    }


    public void UpdateDisplaySize(ThumbnailSize size)
    {
        var designSize = size switch
        {
            ThumbnailSize.Small => 160d,
            ThumbnailSize.Medium => 256d,
            ThumbnailSize.Large => 512d,
            _ => 256d
        };

        DisplayWidth = designSize;
        DisplayHeight = designSize;
    }

    public void UpdateMetadata(int width, int height, string title)
    {
        Width = width;
        Height = height;
        ImageTitle = title;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        return false;
    }


    public string ImageName { get; }

    public string ImageFileType { get; }

    public string FileType => ImageFileType;

    public string ImageDimensions => $"{Width} x {Height}";

    public string ImageTitle
    {
        get => _imageTitle;
        private set => SetProperty(ref _imageTitle, value);
    }

    public int AutoWidth => Height == 0 ? 200 : (int)((Width * 200.0) / Height);

    public double AspectRatio => Height == 0 ? 1.5 : (double)Width / Height;
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (AppLifetime.IsShuttingDown)
            return;

        var dispatcherQueue = App.MainWindow.DispatcherQueue;
        if (dispatcherQueue.HasThreadAccess)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        else
        {
            if (!dispatcherQueue.TryEnqueue(() =>
            {
                if (AppLifetime.IsShuttingDown)
                    return;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }))
            {
                // System.Diagnostics.Debug.WriteLine($"[ImageFileInfo] Skip PropertyChanged enqueue for {propertyName} on {ImageName}");
                return;
            }
        }
    }
}
