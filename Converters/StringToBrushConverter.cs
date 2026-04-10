using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace PhotoView.Converters;

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string format && !string.IsNullOrEmpty(format))
        {
            System.Diagnostics.Debug.WriteLine($"[StringToBrushConverter] Format: {format}");
            
            SolidColorBrush brush;
            
            switch (format.ToUpper())
            {
                case "JPG":
                case "JPEG":
                case "PNG":
                case "GIF":
                case "WebP":
                    brush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x00, 0xC8, 0xFF));
                    System.Diagnostics.Debug.WriteLine($"[StringToBrushConverter] Selected color: #00c8ff (Blue)");
                    break;
                
                case "RAW":
                case "CR2":
                case "CR3":
                case "CRW":
                case "NEF":
                case "NRW":
                case "ARW":
                case "SRF":
                case "SR2":
                case "DNG":
                case "ORF":
                case "PEF":
                case "RAF":
                case "RW2":
                    brush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xFF, 0xB3, 0x00));
                    System.Diagnostics.Debug.WriteLine($"[StringToBrushConverter] Selected color: #ffb300 (Orange)");
                    break;
                
                case "TIF":
                case "TIFF":
                case "BMP":
                    brush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x2B, 0xFF, 0x00));
                    System.Diagnostics.Debug.WriteLine($"[StringToBrushConverter] Selected color: #2bff00 (Green)");
                    break;
                
                default:
                    brush = new SolidColorBrush(Colors.Gray);
                    System.Diagnostics.Debug.WriteLine($"[StringToBrushConverter] Selected color: Gray (Default)");
                    break;
            }
            
            return brush;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[StringToBrushConverter] Input value is null or empty: {value}");
            return new SolidColorBrush(Colors.Gray);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
