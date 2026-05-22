using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Library.Services;
using System.Threading;
using System.Threading.Tasks;

namespace Library.Views.Controls;

/// <summary>Displays the art-crop thumbnail for a card given its Scryfall ID. Loads asynchronously with caching.</summary>
public class ArtCropControl : UserControl
{
    public static readonly StyledProperty<string?> ScryfallIdProperty =
        AvaloniaProperty.Register<ArtCropControl, string?>(nameof(ScryfallId));

    public string? ScryfallId
    {
        get => GetValue(ScryfallIdProperty);
        set => SetValue(ScryfallIdProperty, value);
    }

    private readonly Image _image;
    private CancellationTokenSource? _cts;

    public ArtCropControl()
    {
        _image = new Image { Stretch = Stretch.UniformToFill };
        Content = new Border
        {
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = _image
        };
        ClipToBounds = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ScryfallIdProperty)
            _ = LoadAsync(change.GetNewValue<string?>());
    }

    private async Task LoadAsync(string? scryfallId)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _image.Source = null;
        if (string.IsNullOrWhiteSpace(scryfallId)) return;

        try
        {
            var scryfall = ScryfallService.Instance;
            if (scryfall == null) return;

            var bmp = await scryfall.GetCardArtCropAsync(scryfallId, ct);
            if (!ct.IsCancellationRequested)
                _image.Source = bmp;
        }
        catch (OperationCanceledException) { }
        catch { /* swallow — art is cosmetic */ }
    }
}
