using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Portal.UI;

/// <summary>
/// Returns Visible when the integer value is greater than 1, Collapsed otherwise.
/// Used to show/hide quantity displays in inventory lists.
/// </summary>
public sealed class QuantityVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int quantity)
            return quantity > 1 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}