using System.Windows.Input;

namespace PhotoView.Models;

public class FilePathItem
{
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileFormat { get; set; } = string.Empty;
    public string FileFormatColor { get; set; } = "Gray";
    public ICommand? OpenInExplorerCommand { get; set; }
    public ICommand? CopyPathCommand { get; set; }
}
