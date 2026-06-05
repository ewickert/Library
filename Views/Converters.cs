using Avalonia.Data.Converters;
using Avalonia.Media;
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

/// <summary>Returns green for true (win) and red for false (loss).</summary>
public sealed class BoolToWinColorConverter : IValueConverter
{
    public static readonly BoolToWinColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? new SolidColorBrush(Color.Parse("#3A7A5A")) : new SolidColorBrush(Color.Parse("#7A3A3A"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Two-way converter that compares a value to an int parameter; used for player-count selector buttons.</summary>
public sealed class IntEqualityConverter : IValueConverter
{
    public static readonly IntEqualityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int v && parameter is string s && int.TryParse(s, out var p))
            return v == p;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && int.TryParse(s, out var p))
            return p;
        return Avalonia.AvaloniaProperty.UnsetValue;
    }
}
