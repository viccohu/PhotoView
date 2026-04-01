using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using PhotoView.Models;
using System;

namespace PhotoView.Converters;

public class NodeTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is FolderNode node)
        {
            return node.NodeType switch
            {
                NodeType.ThisPC => "\xe977",
                NodeType.ExternalDevice => "\xE88E",
                NodeType.Drive => node.IsRemovable ? "\xE88E" : "\xeda2",
                NodeType.Folder => "\xE8B7",
                _ => "\xE8B7"
            };
        }
        
        if (value is NodeType nodeType)
        {
            return nodeType switch
            {
                NodeType.ThisPC => "\xE774",
                NodeType.ExternalDevice => "\xE88E",
                NodeType.Drive => "\xE8DA",
                NodeType.Folder => "\xE8B7",
                _ => "\xE8B7"
            };
        }
        
        return "\xE8B7";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
