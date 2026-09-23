using System;
using System.Globalization;
using System.Windows.Data;

namespace Portal.UI;

/// <summary>
/// Converts a ratio (0.0–1.0) to a bar width for resource bars.
/// The converter parameter specifies the total available width in pixels.
/// </summary>
public sealed class RatioBarWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double ratio = System.Convert.ToDouble(value);
        double totalWidth = parameter is string s && double.TryParse(s, out var w) ? w : 100.0;
        return Math.Max(0, ratio * totalWidth);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}