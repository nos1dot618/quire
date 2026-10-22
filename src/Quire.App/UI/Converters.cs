using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quire.Models;

namespace Quire.UI;

/// <summary>
/// Resolves an asset path to an image using the document's asset store.
/// Bindings: [0] the asset path, [1] the <see cref="QuireDocument"/>. Returns null when the asset is missing or unreadable.
/// </summary>
public sealed class AssetImageConverter : IMultiValueConverter
{
    // Decoded images are cached per byte array, so scrolling and re-templating do not decode again.
    private static readonly ConditionalWeakTable<byte[], ImageSource> Cache = [];

    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string path, QuireDocument document] || !document.Assets.TryGet(path, out var data))
            return null;

        if (Cache.TryGetValue(data, out var cached))
            return cached;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(data, writable: false);
            image.EndInit();
            image.Freeze();
            Cache.AddOrUpdate(data, image);
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Returns an asset's raw bytes, or null when missing. Bindings: [0] the asset path, [1] the document.</summary>
public sealed class AssetBytesConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [string path, QuireDocument document] && document.Assets.TryGet(path, out var data) ? data : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible when the bound value is null, collapsed otherwise. Used for missing-asset placeholders.</summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Describes an attachment's size, or that it is missing. Bindings: [0] the asset path, [1] the document.</summary>
public sealed class AssetSizeConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string path, QuireDocument document] || !document.Assets.TryGet(path, out var data))
            return "missing from this document";

        return data.Length switch
        {
            < 1024 => $"{data.Length} bytes",
            < 1024 * 1024 => $"{data.Length / 1024.0:0.#} KB",
            _ => $"{data.Length / (1024.0 * 1024):0.#} MB",
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows "(not found)" for a file reference whose target does not exist on this machine.</summary>
public sealed class FileExistsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path && path.Length > 0 && !File.Exists(path) && !Directory.Exists(path) ? "(not found)" : "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
