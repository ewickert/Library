using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Library.Models;
using Library.Services;
using System.Windows.Input;

namespace Library.ViewModels;

public partial class CardSlotViewModel : ObservableObject
{
    private readonly ScryfallService _scryfall;

    public Card Card { get; }

    /// <summary>True when this card is a shopping-list placeholder (not physically owned).</summary>
    public bool IsWanted => Card.IsPlaceholder;

    /// <summary>True when this card qualifies as a commander (legendary creature or planeswalker).</summary>
    public bool IsCommanderEligible =>
        !string.IsNullOrEmpty(Card.TypeLine) &&
        Card.TypeLine.Contains("Legendary", StringComparison.OrdinalIgnoreCase) &&
        (Card.TypeLine.Contains("Creature", StringComparison.OrdinalIgnoreCase) ||
         Card.TypeLine.Contains("Planeswalker", StringComparison.OrdinalIgnoreCase));

    /// <summary>Set by DecksViewModel when this slot is used in a deck context. Removes the card from the deck.</summary>
    public ICommand? RemoveFromDeckCommand { get; set; }

    /// <summary>Set by DecksViewModel. Promotes this card to commander. Null when not commander-eligible.</summary>
    public ICommand? SetAsCommanderCommand { get; set; }

    // ── Price display helpers ─────────────────────────────────────────────────
    /// <summary>Current market price formatted as "$X.XX", or "—" if unavailable.</summary>
    public string CurrentPriceLabel =>
        Card.CurrentMarketPrice.HasValue ? $"${Card.CurrentMarketPrice.Value:F2}" : "—";

    /// <summary>Delta vs baseline, e.g. "+$1.23" or "-$0.50". Empty string when no data.</summary>
    public string PriceDeltaLabel
    {
        get
        {
            if (!Card.BaselineMarketPrice.HasValue || !Card.CurrentMarketPrice.HasValue) return string.Empty;
            var delta = Card.CurrentMarketPrice.Value - Card.BaselineMarketPrice.Value;
            return delta >= 0 ? $"+${delta:F2}" : $"-${Math.Abs(delta):F2}";
        }
    }

    /// <summary>True when the current price is above baseline.</summary>
    public bool IsPriceUp =>
        Card.BaselineMarketPrice.HasValue && Card.CurrentMarketPrice.HasValue &&
        Card.CurrentMarketPrice.Value > Card.BaselineMarketPrice.Value;

    /// <summary>True when the current price is below baseline.</summary>
    public bool IsPriceDown =>
        Card.BaselineMarketPrice.HasValue && Card.CurrentMarketPrice.HasValue &&
        Card.CurrentMarketPrice.Value < Card.BaselineMarketPrice.Value;

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isSelected;

    public CardSlotViewModel(Card card, ScryfallService scryfall)
    {
        Card = card;
        _scryfall = scryfall;
    }

    public async Task LoadImageAsync(CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            Bitmap? bmp = null;
            if (!string.IsNullOrEmpty(Card.ScryfallId))
                bmp = await _scryfall.GetCardImageAsync(Card.ScryfallId, ct);
            else if (!string.IsNullOrEmpty(Card.SetCode) && !string.IsNullOrEmpty(Card.CollectorNumber))
                bmp = await _scryfall.GetCardImageByCollectorAsync(Card.SetCode, Card.CollectorNumber, Card.Foil, ct);

            if (!ct.IsCancellationRequested)
                Image = bmp;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoading = false;
        }
    }
}
