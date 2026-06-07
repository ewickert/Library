using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
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

    public string? SetCode
    {
        get => GetValue(SetCodeProperty);
        set => SetValue(SetCodeProperty, value);
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
        if (change.Property == SetCodeProperty)
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

        var icon = SetIconService.Instance.TryGetIcon(code, IsDarkTheme)
                ?? SetIconService.Instance.TryGetIcon(code, false);
        _image.Source = icon;
        IsVisible = icon != null;
    }
}
