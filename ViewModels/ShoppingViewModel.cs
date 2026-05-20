using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using System.Collections.ObjectModel;

namespace Library.ViewModels;

public partial class ShoppingViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;
    private readonly DecksViewModel  _decks;

    [ObservableProperty] private ObservableCollection<ShoppingItemViewModel> _items = new();
    [ObservableProperty] private string _searchText = string.Empty;

    private List<ShoppingItemViewModel> _allItems = new();
    private CancellationTokenSource? _searchDebounceCts;

    public ShoppingViewModel(DatabaseService db, ScryfallService scryfall, DecksViewModel decks)
    {
        _db      = db;
        _scryfall = scryfall;
        _decks   = decks;
        Reload();
    }

    public void Reload()
    {
        _allItems = _db.GetShoppingList()
                       .Select(s => new ShoppingItemViewModel(s, _db, _scryfall, _decks, Reload))
                       .ToList();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var cts = _searchDebounceCts;
        _ = Task.Delay(200, cts.Token).ContinueWith(
            _ => ApplyFilter(),
            cts.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ApplyFilter()
    {
        var q = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? _allItems
            : _allItems.Where(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.TypeLine.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.SetCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.SetName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        Items = new ObservableCollection<ShoppingItemViewModel>(filtered);
    }
}

public partial class ShoppingItemViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;
    private readonly DecksViewModel  _decks;
    private readonly Action          _reloadParent;

    public ShoppingItem Item { get; }

    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _image;
    [ObservableProperty] private bool _isLoadingImage = true;

    public string Name            => Item.Name;
    public string SetCode         => Item.SetCode;
    public string SetName         => Item.SetName;
    public string CollectorNumber => Item.CollectorNumber;
    public string ManaCost        => Item.ManaCost;
    public string TypeLine        => Item.TypeLine;
    public string Rarity          => Item.Rarity;

    public ShoppingItemViewModel(ShoppingItem item, DatabaseService db, ScryfallService scryfall,
        DecksViewModel decks, Action reloadParent)
    {
        Item          = item;
        _db           = db;
        _scryfall     = scryfall;
        _decks        = decks;
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

    [RelayCommand]
    private void Remove()
    {
        _db.RemoveFromShoppingList(Item.Id);
        _reloadParent();
    }

    [RelayCommand]
    private void AddToDeck()
    {
        if (_decks.SelectedDeck == null || Item.PlaceholderCardId == null) return;
        _db.AddCardToDeck(_decks.SelectedDeck.Id, Item.PlaceholderCardId.Value, 1, false);
        _decks.ReloadDeckCards();
    }
}
