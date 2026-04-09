using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.UI.Xaml.Media;

namespace PhotoView.Models;

public class FilePathItem : INotifyPropertyChanged
{
    private string _path = string.Empty;
    private string _fileName = string.Empty;
    private string _fileFormat = string.Empty;
    private Brush? _fileFormatBackgroundBrush;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string Path
    {
        get => _path;
        set
        {
            if (_path != value)
            {
                _path = value;
                OnPropertyChanged();
            }
        }
    }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName != value)
            {
                _fileName = value;
                OnPropertyChanged();
            }
        }
    }

    public string FileFormat
    {
        get => _fileFormat;
        set
        {
            if (_fileFormat != value)
            {
                _fileFormat = value;
                OnPropertyChanged();
            }
        }
    }

    public Brush? FileFormatBackgroundBrush
    {
        get => _fileFormatBackgroundBrush;
        set
        {
            if (_fileFormatBackgroundBrush != value)
            {
                _fileFormatBackgroundBrush = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand? OpenInExplorerCommand { get; set; }
    public ICommand? CopyPathCommand { get; set; }
}
