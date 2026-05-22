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
    [ObservableProperty] private bool _exportCopied;
    [ObservableProperty] private bool _tcgExportCopied;

    /// Deck names for the filter dropdown. First entry is always "(All decks)".
    [ObservableProperty] private ObservableCollection<string> _deckFilterOptions = new();
    [ObservableProperty] private string _selectedDeckFilter = string.Empty;

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
        RebuildDeckFilterOptions();
        ApplyFilter();
    }

    private void RebuildDeckFilterOptions()
    {
        const string all = "(All decks)";
        var names = _allItems
            .SelectMany(i => i.DeckNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        DeckFilterOptions = new ObservableCollection<string>(
            new[] { all }.Concat(names));

        // Keep current selection if it still exists, else reset to "All"
        if (!DeckFilterOptions.Contains(SelectedDeckFilter))
            SelectedDeckFilter = all;
    }

    partial void OnSelectedDeckFilterChanged(string value) => ApplyFilter();

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
        IEnumerable<ShoppingItemViewModel> filtered = _allItems;

        // Deck filter
        const string all = "(All decks)";
        if (!string.IsNullOrEmpty(SelectedDeckFilter) && SelectedDeckFilter != all)
            filtered = filtered.Where(i =>
                i.DeckNames.Contains(SelectedDeckFilter, StringComparer.OrdinalIgnoreCase));

        // Text search
        var q = SearchText.Trim();
        if (!string.IsNullOrEmpty(q))
            filtered = filtered.Where(i =>
                i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.TypeLine.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.SetCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.SetName.Contains(q, StringComparison.OrdinalIgnoreCase));

        Items = new ObservableCollection<ShoppingItemViewModel>(filtered);
    }

    /// <summary>
    /// Builds a Card Kingdom-compatible deck list from the currently visible (filtered) items.
    /// Format: "1 Card Name" per line, one line per unique card name.
    /// </summary>
    public string BuildCardKingdomExport()
    {
        // Use visible filtered list; group by name so duplicate wants collapse to a quantity
        var lines = Items
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key}");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Builds a TCGPlayer Mass Entry-compatible list from the currently visible (filtered) items.
    /// Format: "1 Lightning Bolt [M10] 146" — quantity, name, [SET], collector number.
    /// </summary>
    public string BuildTcgPlayerExport()
    {
        var lines = Items
            .GroupBy(i => $"{i.Name}||{i.SetCode.ToUpperInvariant()}||{i.CollectorNumber}")
            .OrderBy(g => g.First().Name)
            .Select(g =>
            {
                var item = g.First();
                var qty = g.Count();
                var setPart = !string.IsNullOrWhiteSpace(item.SetCode) ? $" [{item.SetCode.ToUpperInvariant()}]" : string.Empty;
                var numPart = !string.IsNullOrWhiteSpace(item.CollectorNumber) ? $" {item.CollectorNumber}" : string.Empty;
                return $"{qty} {item.Name}{setPart}{numPart}";
            });
        return string.Join(Environment.NewLine, lines);
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

    /// Deck names this card has been added to (empty if not in any deck yet).
    public List<string> DeckNames { get; }

    /// Human-readable label like "Deck A, Deck B" or "(no deck)" for display.
    public string DeckNamesLabel => DeckNames.Count > 0
        ? string.Join(", ", DeckNames)
        : "(no deck)";

    public ShoppingItemViewModel(ShoppingItem item, DatabaseService db, ScryfallService scryfall,
        DecksViewModel decks, Action reloadParent)
    {
        Item          = item;
        _db           = db;
        _scryfall     = scryfall;
        _decks        = decks;
        _reloadParent = reloadParent;
        DeckNames     = item.PlaceholderCardId.HasValue
            ? db.GetDeckNamesForCard(item.PlaceholderCardId.Value)
            : new List<string>();
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
