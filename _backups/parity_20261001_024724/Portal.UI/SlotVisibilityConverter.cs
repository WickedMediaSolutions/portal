using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Portal.Protocol;

namespace Portal.UI;

/// <summary>
/// Converts an ObservableCollection of EquippedItemRecord to a Visibility
/// value based on whether a specific slot (passed as ConverterParameter)
/// is occupied.
/// </summary>
public sealed class SlotVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var slot = parameter as string;
        if (string.IsNullOrEmpty(slot))
            return Visibility.Collapsed;

        if (value is ObservableCollection<EquippedItemRecord> equipped)
        {
            return equipped.Any(e => e.Slot == slot)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}