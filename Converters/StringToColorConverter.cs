using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace PhotoView.Converters;

public class StringToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string colorName)
        {
            return colorName.ToLower() switch
            {
                "blue" => new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue),
                "green" => new SolidColorBrush(Microsoft.UI.Colors.SeaGreen),
                "orange" => new SolidColorBrush(Microsoft.UI.Colors.Orange),
                "red" => new SolidColorBrush(Microsoft.UI.Colors.Crimson),
                "purple" => new SolidColorBrush(Microsoft.UI.Colors.MediumPurple),
                "gray" => new SolidColorBrush(Microsoft.UI.Colors.Gray),
                _ => new SolidColorBrush(Microsoft.UI.Colors.Gray)
            };
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
