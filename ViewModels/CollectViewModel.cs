using CommunityToolkit.Mvvm.ComponentModel;
using PhotoView.Models;
using Windows.Storage.Search;
using Windows.Storage;
using System.Collections.ObjectModel;

namespace PhotoView.ViewModels;

public class CollectViewModel : ObservableRecipient
{
    public ObservableCollection<ImageFileInfo> Images
    {
        get;
        set;
    }
    public CollectViewModel()
    {
        Images = new ObservableCollection<ImageFileInfo>();
        _ = GetItemsAsync();
    }
    private async Task GetItemsAsync()
    {
        StorageFolder picturesFolder = await StorageFolder.GetFolderFromPathAsync("D:\\test");

        var result = picturesFolder.CreateFileQueryWithOptions(new QueryOptions());

        IReadOnlyList<StorageFile> imageFiles = await result.GetFilesAsync();
        foreach (StorageFile file in imageFiles)
        {
            Images.Add(await LoadImageInfo(file));
        }
    }

    public static async Task<ImageFileInfo> LoadImageInfo(StorageFile file)
    {
        var properties = await file.Properties.GetImagePropertiesAsync();
        return new ImageFileInfo(
            (int)properties.Width,
            (int)properties.Height,
            properties.Title,
            file,
            file.DisplayName,
            file.DisplayType);
    }
}
