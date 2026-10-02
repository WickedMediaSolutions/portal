using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Portal.UI;

/// <summary>
/// Converts the monster / NPC portrait asset path exposed by
/// <c>Portal.State.TargetStats.PortraitImagePath</c> (for example
/// <c>/Assets/monsterimages/rat-types.png</c>) into a frozen BitmapImage for
/// the Current Target portrait's <c>Image.Source</c>.
///
/// The relative asset path is expanded to an absolute application pack URI so
/// that it resolves reliably for the images declared as <c>Resource</c> in
/// Portal.UI.csproj. If the bound value is missing or cannot be resolved, the
/// default monster portrait is used.
/// </summary>
public sealed class MonsterPortraitImageConverter : IValueConverter
{
    private const string PackPrefix = "pack://application:,,,";

    private const string DefaultMonsterImagePath = "/Assets/monsterimages/defaultmonster.png";

    private static readonly Dictionary<string, BitmapImage> Cache =
        new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value as string;

        if (string.IsNullOrWhiteSpace(path))
            path = DefaultMonsterImagePath;

        var image = GetOrCreate(path);

        // Missing resource or malformed path — fall back to the default
        // monster portrait rather than leaving the portrait blank.
        if (image is null && !string.Equals(path, DefaultMonsterImagePath, StringComparison.Ordinal))
            image = GetOrCreate(DefaultMonsterImagePath);

        return image ?? DependencyProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// Returns a cached, frozen BitmapImage for the given asset path, or null
    /// when the path is malformed or the resource does not exist.
    /// </summary>
    private static BitmapImage? GetOrCreate(string path)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var cached))
                return cached;
        }

        BitmapImage bitmap;

        try
        {
            bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(
                path.StartsWith("pack:", StringComparison.OrdinalIgnoreCase)
                    ? path
                    : PackPrefix + path,
                UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
        }
        catch (Exception)
        {
            // Malformed path or missing resource — treated as "no image".
            return null;
        }

        lock (Cache)
        {
            Cache[path] = bitmap;
        }

        return bitmap;
    }
}
