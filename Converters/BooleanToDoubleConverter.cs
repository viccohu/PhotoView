using Microsoft.UI.Xaml.Data;

namespace PhotoView.Converters;

public class BooleanToDoubleConverter : IValueConverter
{
    public double TrueValue { get; set; }

    public double FalseValue { get; set; }

    public object Convert(object? value, Type targetType, object parameter, string language)
    {
        return value is bool boolValue && boolValue
            ? TrueValue
            : FalseValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
