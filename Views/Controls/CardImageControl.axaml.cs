using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Library.Models;
using Library.Services;

namespace Library.Views.Controls;

public partial class CardImageControl : UserControl
{
    public static readonly StyledProperty<Card?> CardProperty =
        AvaloniaProperty.Register<CardImageControl, Card?>(nameof(Card));

    public static readonly StyledProperty<Bitmap?> ImageProperty =
        AvaloniaProperty.Register<CardImageControl, Bitmap?>(nameof(Image));

    public static readonly StyledProperty<bool> IsLoadingProperty =
        AvaloniaProperty.Register<CardImageControl, bool>(nameof(IsLoading), true);

    public static readonly StyledProperty<string> CardNameProperty =
        AvaloniaProperty.Register<CardImageControl, string>(nameof(CardName), string.Empty);

    public Card? Card { get => GetValue(CardProperty); set => SetValue(CardProperty, value); }
    public Bitmap? Image { get => GetValue(ImageProperty); set => SetValue(ImageProperty, value); }
    public bool IsLoading { get => GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public string CardName { get => GetValue(CardNameProperty); set => SetValue(CardNameProperty, value); }

    private CancellationTokenSource? _cts;

    public CardImageControl() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CardProperty)
        {
            var card = change.GetNewValue<Card?>();
            CardName = card?.Name ?? string.Empty;
            _ = LoadImageAsync(card);
        }
    }

    private async Task LoadImageAsync(Card? card)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Image = null;
        IsLoading = true;

        if (card == null) { IsLoading = false; return; }

        try
        {
            var scryfall = ScryfallService.Instance;
            if (scryfall == null) return;

            Bitmap? bmp = null;
            if (!string.IsNullOrEmpty(card.ScryfallId))
                bmp = await scryfall.GetCardImageAsync(card.ScryfallId, ct);
            else if (!string.IsNullOrEmpty(card.SetCode) && !string.IsNullOrEmpty(card.CollectorNumber))
                bmp = await scryfall.GetCardImageByCollectorAsync(card.SetCode, card.CollectorNumber, card.Foil, ct);

            if (!ct.IsCancellationRequested)
                Image = bmp;
        }
        catch (OperationCanceledException) { }
        finally { if (!ct.IsCancellationRequested) IsLoading = false; }
    }
}
