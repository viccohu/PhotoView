using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoView.Models;
using Windows.ApplicationModel;
using Windows.Storage.Search;
using Windows.Storage;

namespace PhotoView.ViewModels;
public class CollectViewModel : ObservableRecipient
{
    public ObservableCollection<ImageFileInfo> Images
    {
        get;
    } = new ObservableCollection<ImageFileInfo>();
    public CollectViewModel()
    {

    }
}
