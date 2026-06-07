using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Svg.Skia;
using Library.Services;
using System;

namespace Library.Views.Controls;

/// <summary>
/// Renders a Scryfall set icon for the given SetCode.
/// Hides itself until the icon is available.
/// </summary>
public class SetIconControl : UserControl
{
    public static readonly StyledProperty<string?> SetCodeProperty =
        AvaloniaProperty.Register<SetIconControl, string?>(nameof(SetCode));

    public static readonly StyledProperty<string?> RarityProperty =
        AvaloniaProperty.Register<SetIconControl, string?>(nameof(Rarity));

    public string? SetCode
    {
        get => GetValue(SetCodeProperty);
        set => SetValue(SetCodeProperty, value);
    }

    /// <summary>
    /// When set, renders the icon tinted with the standard MTG rarity color
    /// (common=white, uncommon=silver, rare=gold, mythic=orange).
    /// </summary>
    public string? Rarity
    {
        get => GetValue(RarityProperty);
        set => SetValue(RarityProperty, value);
    }

    private readonly Image _image;

    public SetIconControl()
    {
        _image = new Image
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = _image;
        Padding = new Thickness(0);
        IsVisible = false;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetIconService.Instance.SetsUpdated += OnSetsUpdated;
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += OnThemeVariantChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SetIconService.Instance.SetsUpdated -= OnSetsUpdated;
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged -= OnThemeVariantChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SetCodeProperty || change.Property == RarityProperty)
            Refresh();
    }

    private void OnSetsUpdated(object? sender, EventArgs e) => Refresh();
    private void OnThemeVariantChanged(object? sender, EventArgs e) => Refresh();

    private bool IsDarkTheme =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    private void Refresh()
    {
        var code = SetCode;
        if (string.IsNullOrEmpty(code))
        {
            _image.Source = null;
            IsVisible = false;
            return;
        }

        // TryGetSource/TryGetRaritySource return SvgSource (thread-safe plain class);
        // SvgImage is created here on the UI thread to satisfy AvaloniaObject affinity.
        var rarity = Rarity;
        SvgSource? source = !string.IsNullOrEmpty(rarity)
            ? SetIconService.Instance.TryGetRaritySource(code, rarity)
            : SetIconService.Instance.TryGetSource(code, IsDarkTheme)
              ?? SetIconService.Instance.TryGetSource(code, false);

        _image.Source = source != null ? new SvgImage { Source = source } : null;
        IsVisible = source != null;
    }
}
