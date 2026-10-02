using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Portal.UI;

/// <summary>
/// Returns Visible when the value is not null and (for strings) not empty,
/// and (for integers) greater than zero. Otherwise Collapsed.
/// Used to hide item card fields that have no data.
/// </summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
            return Visibility.Collapsed;

        if (value is string s)
            return string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;

        if (value is int i)
            return i > 0 ? Visibility.Visible : Visibility.Collapsed;

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}