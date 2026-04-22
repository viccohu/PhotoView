using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace PhotoView.Converters;

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string? str = value?.ToString();
        if (!string.IsNullOrEmpty(str))
        {
            return Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
