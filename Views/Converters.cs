using Avalonia.Data.Converters;
using System.Globalization;

namespace Library.Views;

/// <summary>Returns true when an int value is greater than 1 — used to show the quantity badge on deck card rows.</summary>
public sealed class IntGreaterThanOneConverter : IValueConverter
{
    public static readonly IntGreaterThanOneConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i > 1;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
