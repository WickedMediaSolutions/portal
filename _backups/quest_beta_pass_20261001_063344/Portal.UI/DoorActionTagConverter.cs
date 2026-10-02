using System;
using System.Globalization;
using System.Windows.Data;

namespace Portal.UI;

/// <summary>
/// Builds the composite Tag carried by each door action button, in the form
/// <c>"direction|action"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every door row renders its own Open / Close / Lock / Unlock buttons rather
/// than relying on a selected-item list, so a click has to name BOTH which
/// door it belongs to and which action it performs. A binding alone can only
/// supply one value, so this converter combines the row's direction with the
/// static <c>ConverterParameter</c> action name.
/// </para>
/// <para>
/// The separator is a pipe, which cannot appear in a canonical Keystone
/// direction, so the composite is always unambiguous to split.
/// </para>
/// </remarks>
public sealed class DoorActionTagConverter : IValueConverter
{
    /// <summary>Separator between the direction and the action.</summary>
    public const char Separator = '|';

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var direction = value as string;
        var action = parameter as string;

        if (string.IsNullOrWhiteSpace(direction) || string.IsNullOrWhiteSpace(action))
            return string.Empty;

        return $"{direction}{Separator}{action}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
