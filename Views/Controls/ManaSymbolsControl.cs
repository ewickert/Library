using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Library.Views.Controls;

/// <summary>Renders a mana cost string like "{2}{W}{B}" as a row of colored symbol badges.</summary>
public partial class ManaSymbolsControl : UserControl
{
    public static readonly StyledProperty<string?> ManaCostProperty =
        AvaloniaProperty.Register<ManaSymbolsControl, string?>(nameof(ManaCost));

    public string? ManaCost
    {
        get => GetValue(ManaCostProperty);
        set => SetValue(ManaCostProperty, value);
    }

    private readonly WrapPanel _panel;

    public ManaSymbolsControl()
    {
        _panel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        Content = _panel;
        Padding = new Thickness(0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ManaCostProperty)
            Rebuild(change.GetNewValue<string?>());
    }

    private void Rebuild(string? manaCost)
    {
        _panel.Children.Clear();
        if (string.IsNullOrWhiteSpace(manaCost)) return;

        foreach (var sym in ParseSymbols(manaCost))
            _panel.Children.Add(MakeBadge(sym));
    }

    private static Border MakeBadge((string text, Color bg, Color fg) sym) => new()
    {
        Width = 16,
        Height = 16,
        CornerRadius = new CornerRadius(8),
        Background = new SolidColorBrush(sym.bg),
        Margin = new Thickness(1, 0),
        Child = new TextBlock
        {
            Text = sym.text,
            FontSize = 8.5,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(sym.fg),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        }
    };

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex SymbolRegex();

    private static List<(string text, Color bg, Color fg)> ParseSymbols(string cost)
    {
        var result = new List<(string, Color, Color)>();
        foreach (Match m in SymbolRegex().Matches(cost))
            result.Add(MapSymbol(m.Groups[1].Value.ToUpperInvariant()));
        return result;
    }

    private static (string text, Color bg, Color fg) MapSymbol(string code) => code switch
    {
        "W"                     => ("W", Color.Parse("#F0EDD0"), Color.Parse("#5A4A10")),
        "U"                     => ("U", Color.Parse("#1466B0"), Colors.White),
        "B"                     => ("B", Color.Parse("#28182A"), Color.Parse("#DDDDDD")),
        "R"                     => ("R", Color.Parse("#CC2020"), Colors.White),
        "G"                     => ("G", Color.Parse("#1A7A30"), Colors.White),
        "C"                     => ("◇", Color.Parse("#C0B090"), Color.Parse("#1A1510")),
        "X"                     => ("X", Color.Parse("#888888"), Colors.White),
        "Y"                     => ("Y", Color.Parse("#888888"), Colors.White),
        "Z"                     => ("Z", Color.Parse("#888888"), Colors.White),
        "T"                     => ("⟳", Color.Parse("#CC6600"), Colors.White),
        "Q"                     => ("Q", Color.Parse("#CC6600"), Colors.White),
        "S"                     => ("S", Color.Parse("#88CCFF"), Color.Parse("#334466")),
        "E"                     => ("E", Color.Parse("#9944CC"), Colors.White),
        _ when code.Contains('/') => HybridSymbol(code),
        _ when int.TryParse(code, out _) => (code, Color.Parse("#888888"), Colors.White),
        _                       => (code.Length > 3 ? code[..3] : code, Color.Parse("#888888"), Colors.White),
    };

    private static (string text, Color bg, Color fg) HybridSymbol(string code)
    {
        // e.g. "W/U", "2/W", "W/P" — colour from the first meaningful part
        var parts = code.Split('/');
        var first = parts[0];
        var (_, bg, fg) = MapSymbol(first);
        var label = parts[0].Length > 0 ? parts[0][0].ToString() : "?";
        return (label, bg, fg);
    }
}
