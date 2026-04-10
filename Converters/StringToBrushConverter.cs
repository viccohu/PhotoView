using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace PhotoView.Converters;

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string colorString && !string.IsNullOrEmpty(colorString))
        {
            try
            {
                if (colorString.StartsWith("#") && colorString.Length == 7)
                {
                    var color = ColorHelper.FromArgb(
                        0xFF,
                        System.Convert.ToByte(colorString.Substring(1, 2), 16),
                        System.Convert.ToByte(colorString.Substring(3, 2), 16),
                        System.Convert.ToByte(colorString.Substring(5, 2), 16)
                    );
                    return new SolidColorBrush(color);
                }
            }
            catch
            {
            }
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
