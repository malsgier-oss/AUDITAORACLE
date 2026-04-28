using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WorkAudit.Converters;

/// <summary>Converts hex color string (e.g. #0078D4) to SolidColorBrush.</summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrEmpty(hex)) return System.Windows.Media.Brushes.Black;
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch { return System.Windows.Media.Brushes.Black; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
