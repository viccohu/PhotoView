using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PhotoView.Helpers;

public class EnumToBooleanConverter : IValueConverter
{
    public EnumToBooleanConverter()
    {
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (parameter is string enumString && value != null)
        {
            var enumType = value.GetType();
            if (!Enum.IsDefined(enumType, value))
            {
                throw new ArgumentException("ExceptionEnumToBooleanConverterValueMustBeAnEnum");
            }

            var enumValue = Enum.Parse(enumType, enumString);

            return enumValue.Equals(value);
        }

        throw new ArgumentException("ExceptionEnumToBooleanConverterParameterMustBeAnEnumName");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (parameter is string enumString && targetType.IsEnum)
        {
            return Enum.Parse(targetType, enumString);
        }

        throw new ArgumentException("ExceptionEnumToBooleanConverterParameterMustBeAnEnumName");
    }
}
