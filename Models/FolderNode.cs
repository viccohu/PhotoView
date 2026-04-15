using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PhotoView.Models;

public enum NodeType
{
    FavoritesRoot,
    PinnedFolder,
    RecentFolder,
    ThisPC,
    ExternalDevice,
    Drive,
    KnownFolder,
    Folder
}

public partial class FolderNode : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _children;

    [ObservableProperty]
    private ObservableCollection<FolderNode> _allChildren;

    [ObservableProperty]
    private NodeType _nodeType;

    [ObservableProperty]
    private bool _hasSubFolders;

    private ImageSource? _listIcon;
    private bool _isListIconLoading;

    public ImageSource? ListIcon
    {
        get => _listIcon;
        private set => SetProperty(ref _listIcon, value);
    }

    public bool HasExpandableChildren => IsLoaded ? Children.Count > 0 : HasSubFolders;

    partial void OnHasSubFoldersChanged(bool value)
    {
        OnPropertyChanged(nameof(HasExpandableChildren));
    }

    public StorageFolder? Folder { get; set; }

    public FolderNode? Parent { get; set; }

    public string? FullPath { get; set; }

    public bool IsLoaded { get; set; }

    public bool IsRemovable { get; set; }

    public string CreatedTimeText => Folder == null
        ? string.Empty
        : Folder.DateCreated.ToLocalTime().ToString("yyyy/M/d HH:mm", CultureInfo.CurrentCulture);

    public async Task EnsureListIconAsync(CancellationToken cancellationToken)
    {
        if (ListIcon != null || _isListIconLoading || Folder == null)
            return;

        _isListIconLoading = true;
        try
        {
            var thumbnail = await Folder.GetThumbnailAsync(
                ThumbnailMode.ListView,
                32,
                ThumbnailOptions.None).AsTask(cancellationToken);

            if (thumbnail == null || thumbnail.Size <= 0)
                return;

            var bitmap = new BitmapImage
            {
                DecodePixelWidth = 20
            };
            await bitmap.SetSourceAsync(thumbnail).AsTask(cancellationToken);
            ListIcon = bitmap;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderNode] Load folder icon failed for {Name}: {ex.Message}");
        }
        finally
        {
            _isListIconLoading = false;
        }
    }

    public FolderNode(StorageFolder? folder = null, NodeType nodeType = NodeType.Folder, FolderNode? parent = null)
    {
        Name = folder?.DisplayName ?? "This PC";
        Folder = folder;
        NodeType = nodeType;
        Parent = parent;
        FullPath = folder?.Path;
        Children = new ObservableCollection<FolderNode>();
        AllChildren = new ObservableCollection<FolderNode>();
    }

    public void CheckHasSubFolders()
    {
        if (NodeType == NodeType.FavoritesRoot ||
            NodeType == NodeType.ThisPC ||
            NodeType == NodeType.ExternalDevice)
        {
            HasSubFolders = true;
            return;
        }

        if (string.IsNullOrEmpty(FullPath))
        {
            HasSubFolders = false;
            return;
        }

        try
        {
            HasSubFolders = Directory.EnumerateDirectories(FullPath, "*", SearchOption.TopDirectoryOnly).Any();
        }
        catch
        {
            HasSubFolders = false;
        }
    }

    public void RefreshExpandableState()
    {
        OnPropertyChanged(nameof(HasExpandableChildren));
    }
}
