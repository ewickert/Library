using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;

namespace Library.ViewModels;

/// <summary>
/// A shopping list item associated with a specific deck wishlist.
/// "Remove" untags it from the deck without deleting the global shopping list entry.
/// "Add to Deck" adds the placeholder card to the current deck.
/// </summary>
public partial class WishlistItemViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;
    private readonly DecksViewModel  _decks;
    private readonly int             _deckId;
    private readonly Action          _reloadParent;

    public ShoppingItem Item { get; }

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _isLoadingImage = true;

    public string Name            => Item.Name;
    public string SetName         => Item.SetName;
    public string SetCode         => Item.SetCode.ToUpperInvariant();
    public string CollectorNumber => Item.CollectorNumber;
    public string ManaCost        => Item.ManaCost;
    public string TypeLine        => Item.TypeLine;
    public string Rarity          => Item.Rarity;

    public WishlistItemViewModel(ShoppingItem item, DatabaseService db, ScryfallService scryfall,
        DecksViewModel decks, int deckId, Action reloadParent)
    {
        Item          = item;
        _db           = db;
        _scryfall     = scryfall;
        _decks        = decks;
        _deckId       = deckId;
        _reloadParent = reloadParent;
        _ = LoadImageAsync();
    }

    private async Task LoadImageAsync()
    {
        IsLoadingImage = true;
        try
        {
            if (!string.IsNullOrEmpty(Item.ScryfallId))
                Image = await _scryfall.GetCardImageAsync(Item.ScryfallId, CancellationToken.None);
        }
        finally { IsLoadingImage = false; }
    }

    /// <summary>Adds the placeholder card into the currently selected deck.</summary>
    [RelayCommand]
    private void AddToDeck()
    {
        if (_decks.SelectedDeck?.Id != _deckId || Item.PlaceholderCardId == null) return;
        _db.AddCardToDeck(_deckId, Item.PlaceholderCardId.Value, 1, false);
        _decks.ReloadDeckCards();
    }

    /// <summary>Removes this item from the deck wishlist (does not delete from global shopping list).</summary>
    [RelayCommand]
    private void RemoveFromWishlist()
    {
        _db.UntagShoppingItemFromDeck(Item.Id, _deckId);
        _reloadParent();
    }

    /// <summary>Removes this item from the global shopping list entirely.</summary>
    [RelayCommand]
    private void RemoveFromShoppingList()
    {
        _db.RemoveFromShoppingList(Item.Id);
        _reloadParent();
    }
}
