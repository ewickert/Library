using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Library.Services;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Library.Views.Controls;

/// <summary>
/// Renders oracle text with Scryfall SVG symbols inline.
/// Built on StackPanel + WrapPanel (one row per paragraph) so it works reliably in Avalonia 11.
/// </summary>
public partial class OracleTextControl : UserControl
{
    public static readonly StyledProperty<string?> OracleTextProperty =
        AvaloniaProperty.Register<OracleTextControl, string?>(nameof(OracleText));

    public string? OracleText
    {
        get => GetValue(OracleTextProperty);
        set => SetValue(OracleTextProperty, value);
    }

    private readonly StackPanel _stack;

    public OracleTextControl()
    {
        _stack = new StackPanel { Spacing = 3 };
        Content = _stack;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SymbolService.Instance.SymbolsUpdated += OnSymbolsUpdated;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SymbolService.Instance.SymbolsUpdated -= OnSymbolsUpdated;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == OracleTextProperty)
            Rebuild();
    }

    private void OnSymbolsUpdated(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(Rebuild);

    private void Rebuild()
    {
        _stack.Children.Clear();
        var text = OracleText;
        if (string.IsNullOrEmpty(text)) return;

        foreach (var para in text.Split('\n'))
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal };

            foreach (var (kind, value) in Tokenize(para))
            {
                if (kind == Kind.Symbol)
                {
                    var img = SymbolService.Instance.TryGet(value);
                    if (img != null)
                        row.Children.Add(MakeImage(img, value));
                    else
                        row.Children.Add(MakeWord(value));
                }
                else
                {
                    foreach (var word in SplitWords(value))
                        row.Children.Add(MakeWord(word));
                }
            }

            _stack.Children.Add(row);
        }
    }

    private Image MakeImage(IImage img, string tip)
    {
        var sz = FontSize * 1.1;
        var image = new Image
        {
            Source = img,
            Width = sz,
            Height = sz,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0),
        };
        ToolTip.SetTip(image, tip);
        return image;
    }

    private TextBlock MakeWord(string word) => new()
    {
        Text = word,
        VerticalAlignment = VerticalAlignment.Center,
    };

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex TokenRe();

    private static IEnumerable<(Kind, string)> Tokenize(string para)
    {
        var last = 0;
        foreach (Match m in TokenRe().Matches(para))
        {
            if (m.Index > last)
                yield return (Kind.Text, para[last..m.Index]);
            yield return (Kind.Symbol, m.Value);
            last = m.Index + m.Length;
        }
        if (last < para.Length)
            yield return (Kind.Text, para[last..]);
    }

    // Splits "word1 word2 word3" into ["word1 ", "word2 ", "word3"] so each word wraps independently.
    private static IEnumerable<string> SplitWords(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var space = text.IndexOf(' ', start);
            if (space < 0)
            {
                var tail = text[start..];
                if (tail.Length > 0) yield return tail;
                yield break;
            }
            var chunk = text[start..(space + 1)]; // include trailing space
            if (chunk.Length > 0) yield return chunk;
            start = space + 1;
        }
    }

    private enum Kind { Text, Symbol }
}
