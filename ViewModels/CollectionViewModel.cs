using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using MtgJson;
using System.Collections.ObjectModel;
using LibraryCard = Library.Models.Card;

namespace Library.ViewModels;

public partial class CollectionViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;

    [ObservableProperty] private ObservableCollection<Card> _cards = new();
    [ObservableProperty] private Card? _selectedCard;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _cardImage;
    [ObservableProperty] private bool _isLoadingImage;
    [ObservableProperty] private bool _isGridView;
    [ObservableProperty] private double _gridZoom = 1.0;
    public double TileWidth  => Math.Round(160 * _gridZoom);
    public double TileHeight => Math.Round(224 * _gridZoom);
    partial void OnGridZoomChanged(double v) { OnPropertyChanged(nameof(TileWidth)); OnPropertyChanged(nameof(TileHeight)); Services.PreferencesService.Instance.CollectionGridZoom = v; }

    partial void OnIsGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(CollectionViewModeIndex));
        Services.PreferencesService.Instance.CollectionIsGridView = value;
    }

    public int CollectionViewModeIndex
    {
        get => IsGridView ? 1 : 0;
        set => IsGridView = value == 1;
    }
    [ObservableProperty] private ObservableCollection<CardSlotViewModel> _cardSlots = new();
    [ObservableProperty] private ObservableCollection<SetItem> _availableSets = new();
    [ObservableProperty] private SetItem? _selectedSetFilter;
    [ObservableProperty] private ObservableCollection<AlternatePrintingViewModel> _alternatePrintings = new();
    [ObservableProperty] private bool _isLoadingAlternates;
    [ObservableProperty] private bool _isBackfilling;
    [ObservableProperty] private string _backfillStatus = string.Empty;
    [ObservableProperty] private bool _isRefreshingPrices;
    [ObservableProperty] private string _priceRefreshStatus = string.Empty;

    private CancellationTokenSource? _imageCts;
    private CancellationTokenSource? _slotsCts;
    private CancellationTokenSource? _alternatesCts;
    private CancellationTokenSource? _backfillCts;
    private CancellationTokenSource? _priceCts;
    private CancellationTokenSource? _searchDebounceCts;

    // ── Browse pane (Scryfall + MTGJSON) ─────────────────────────────────────
    public BrowsePaneViewModel BrowsePane { get; }
    [ObservableProperty] private bool _isBrowsePaneOpen;

    /// <summary>Set by host (MainWindowViewModel) to navigate to and import into the deck builder.</summary>
    public Func<MtgJson.Models.Deck, Task>? RequestCloneToDeckBuilder { get; set; }

    public CollectionViewModel(DatabaseService db, ScryfallService scryfall, MtgJsonService mtgJson)
    {
        _db = db;
        _scryfall = scryfall;

        var prefs = Services.PreferencesService.Instance;
        _isGridView = prefs.CollectionIsGridView;
        _gridZoom   = prefs.CollectionGridZoom;

        BrowsePane = new BrowsePaneViewModel(scryfall, mtgJson, db);
        BrowsePane.OnCollectionChanged = () => { RefreshAvailableSets(); LoadCards(); };
        BrowsePane.OnCloneToDeckBuilder = deck =>
            RequestCloneToDeckBuilder?.Invoke(deck) ?? Task.CompletedTask;
        BrowsePane.RequestClose = () => IsBrowsePaneOpen = false;

        SetIconService.Instance.SetsUpdated += (_, _) => RefreshAvailableSets();

        RefreshAvailableSets();
        LoadCards();
        AutoBackfillPricesIfNeeded();
    }

    /// <summary>Automatically fetches baseline prices in the background for any cards missing them.</summary>
    private void AutoBackfillPricesIfNeeded()
    {
        if (_db.GetCardsNeedingBaselinePrice().Count == 0) return;
        // Fire-and-forget; RefreshPrices handles the IsRefreshingPrices guard and progress reporting
        _ = RefreshPricesCommand.ExecuteAsync(null);
    }

    /// <summary>Rebuilds the set filter dropdown from all cards in the database.</summary>
    public void RefreshAvailableSets()
    {
        var all = _db.GetAllCards();
        var svc = SetIconService.Instance;
        var sets = all
            .GroupBy(c => c.SetCode)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var code = g.Key;
                var name = g.First().SetName;
                if (string.IsNullOrWhiteSpace(name))
                    name = svc.TryGetName(code) ?? code;
                return new SetItem(code, name);
            })
            .ToList();

        var current = SelectedSetFilter;
        AvailableSets = new ObservableCollection<SetItem>(
            new[] { SetItem.All }.Concat(sets));

        // Restore previous selection or default to All; set backing field directly to avoid re-entrant LoadCards
#pragma warning disable MVVMTK0034
        _selectedSetFilter = current != null && sets.Any(s => s.Code == current.Code)
            ? AvailableSets.FirstOrDefault(s => s.Code == current.Code) ?? AvailableSets[0]
            : AvailableSets[0];
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(SelectedSetFilter));
    }

    public void LoadCards()
    {
        var all = _db.GetAllCards();

        IEnumerable<Card> filtered = all;

        if (SelectedSetFilter?.Code is { Length: > 0 } setCode)
            filtered = filtered.Where(c => c.SetCode == setCode);

        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(c => LocalCardFilter.Matches(c, SearchText));

        var filteredList = filtered.ToList();
        Cards = new ObservableCollection<Card>(filteredList);
        RebuildSlots(filteredList);
    }

    private void RebuildSlots(IEnumerable<Card> cards)
    {
        _slotsCts?.Cancel();
        _slotsCts = new CancellationTokenSource();
        var slots = cards.Select(c => new CardSlotViewModel(c, _scryfall)).ToList();
        CardSlots = new ObservableCollection<CardSlotViewModel>(slots);
        _ = LoadSlotsSequentiallyAsync(slots, _slotsCts.Token);
    }

    private static async Task LoadSlotsSequentiallyAsync(List<CardSlotViewModel> slots, CancellationToken ct)
    {
        foreach (var slot in slots)
        {
            if (ct.IsCancellationRequested) break;
            await slot.LoadImageAsync(ct);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var cts = _searchDebounceCts;
        _ = Task.Delay(200, cts.Token).ContinueWith(
            _ => {
                LoadCards();
            },
            cts.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }
    partial void OnSelectedSetFilterChanged(SetItem? value) => LoadCards();

    [RelayCommand]
    private void ToggleBrowsePane() => IsBrowsePaneOpen = !IsBrowsePaneOpen;

    /// <summary>Called by the collection drop target when a MTGJSON deck card is dragged in.</summary>
    public void AddMtgJsonCardToCollection(MtgJson.Models.DeckCard card)
    {
        var scryfallId = card.Identifiers.ScryfallId;
        if (!string.IsNullOrEmpty(scryfallId))
        {
            var existing = _db.GetOwnedCardByScryfallId(scryfallId);
            if (existing != null)
            {
                existing.Quantity += 1;
                _db.UpdateCard(existing);
                RefreshAvailableSets();
                LoadCards();
                return;
            }
        }

        _db.AddCard(new LibraryCard
        {
            Name = card.Name,
            ScryfallId = scryfallId ?? string.Empty,
            SetCode = card.SetCode,
            SetName = string.Empty,
            CollectorNumber = card.Number,
            Quantity = 1,
            IsPlaceholder = false,
            ColorIdentity = string.Join(",", card.ColorIdentity),
            ManaCost = card.ManaCost ?? string.Empty,
            TypeLine = BuildMtgJsonTypeLine(card),
            Added = DateTime.UtcNow,
        });

        RefreshAvailableSets();
        LoadCards();
    }

    private static string BuildMtgJsonTypeLine(MtgJson.Models.DeckCard card)
    {
        var superAndType = string.Join(" ",
            card.Supertypes.Concat(card.Types).Where(s => !string.IsNullOrEmpty(s)));
        var subTypes = string.Join(" ", card.Subtypes.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(subTypes) ? superAndType : $"{superAndType} — {subTypes}";
    }

    [RelayCommand]
    private async Task BackfillMetadata()
    {
        if (IsBackfilling) { _backfillCts?.Cancel(); return; }

        var cards = _db.GetCardsNeedingMetadata();
        if (cards.Count == 0) { BackfillStatus = "All card data is up to date."; return; }

        _backfillCts = new CancellationTokenSource();
        IsBackfilling = true;
        BackfillStatus = $"Fetching card data: 0 of {cards.Count}";

        var progress = new Progress<(int done, int total)>(p =>
            BackfillStatus = $"Fetching card data: {p.done} of {p.total}");

        await _scryfall.BackfillCardMetadataAsync(
            cards,
            (card, ci, mc, tl) => _db.UpdateCardMetadata(card.Id, ci, mc, tl),
            progress,
            _backfillCts.Token);

        IsBackfilling = false;
        BackfillStatus = _backfillCts.IsCancellationRequested
            ? "Card data fetch stopped."
            : "Card data up to date.";

        RefreshAvailableSets();
        LoadCards();
    }

    [RelayCommand]
    private async Task RefreshPrices()
    {
        if (IsRefreshingPrices) { _priceCts?.Cancel(); return; }

        // Only fetch cards missing a baseline price first; users can trigger a full refresh via a second
        // pass, but for the initial import-time use-case we focus on cards missing baseline.
        var cards = _db.GetCardsNeedingBaselinePrice();
        if (cards.Count == 0)
        {
            // All have baselines — do a full current-price refresh instead
            cards = _db.GetCardsForPriceRefresh();
        }
        if (cards.Count == 0) { PriceRefreshStatus = "All prices are current."; return; }

        bool settingBaseline = _db.GetCardsNeedingBaselinePrice().Count > 0;

        _priceCts = new CancellationTokenSource();
        IsRefreshingPrices = true;
        PriceRefreshStatus = $"Fetching prices: 0 of {cards.Count}";

        var progress = new Progress<(int done, int total)>(p =>
            PriceRefreshStatus = $"Fetching prices: {p.done} of {p.total}");

        await _scryfall.BackfillCardPricesAsync(
            cards,
            (card, price) =>
            {
                bool needsBaseline = card.BaselineMarketPrice == null;
                _db.UpdateCardPrices(card.Id, price, needsBaseline);
            },
            progress,
            _priceCts.Token);

        IsRefreshingPrices = false;
        PriceRefreshStatus = _priceCts.IsCancellationRequested ? "Price fetch stopped." : "Prices up to date.";

        LoadCards();
    }

    [RelayCommand]
    private void ToggleView() => IsGridView = !IsGridView;

    [RelayCommand]
    private void SelectCard(Card? card) => SelectedCard = card;

    [RelayCommand]
    private void SelectAlternate(AlternatePrintingViewModel alt)
    {
        _imageCts?.Cancel();
        _imageCts = new CancellationTokenSource();
        _ = LoadAlternateImageAsync(alt, _imageCts.Token);
    }

    partial void OnSelectedCardChanged(Card? value)
    {
        CardImage = null;
        AlternatePrintings.Clear();
        if (value != null)
        {
            _ = LoadDetailImageAsync(value);
            _ = LoadAlternatesAsync(value);
        }
    }

    private async Task LoadDetailImageAsync(Card card)
    {
        _imageCts?.Cancel();
        _imageCts = new CancellationTokenSource();
        var ct = _imageCts.Token;
        IsLoadingImage = true;
        try
        {
            Avalonia.Media.Imaging.Bitmap? bmp = null;
            if (!string.IsNullOrEmpty(card.ScryfallId))
                bmp = await _scryfall.GetCardImageAsync(card.ScryfallId, ct);
            else if (!string.IsNullOrEmpty(card.SetCode) && !string.IsNullOrEmpty(card.CollectorNumber))
                bmp = await _scryfall.GetCardImageByCollectorAsync(card.SetCode, card.CollectorNumber, card.Foil, ct);
            if (!ct.IsCancellationRequested)
                CardImage = bmp;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested) IsLoadingImage = false;
        }
    }

    private async Task LoadAlternateImageAsync(AlternatePrintingViewModel alt, CancellationToken ct)
    {
        IsLoadingImage = true;
        try
        {
            var bmp = await _scryfall.GetCardImageAsync(alt.Data.ScryfallId, ct);
            if (!ct.IsCancellationRequested) CardImage = bmp;
        }
        catch (OperationCanceledException) { }
        finally { if (!ct.IsCancellationRequested) IsLoadingImage = false; }
    }

    private async Task LoadAlternatesAsync(Card card)
    {
        _alternatesCts?.Cancel();
        _alternatesCts = new CancellationTokenSource();
        var ct = _alternatesCts.Token;
        IsLoadingAlternates = true;
        AlternatePrintings.Clear();
        try
        {
            var printings = await _scryfall.GetAlternatePrintingsAsync(card.Name, ct);
            if (ct.IsCancellationRequested) return;
            var vms = printings.Select(p => new AlternatePrintingViewModel(p, _scryfall)).ToList();
            foreach (var vm in vms) AlternatePrintings.Add(vm);
            _ = LoadAlternateImagesSequentiallyAsync(vms, ct);
        }
        catch (OperationCanceledException) { }
        finally { if (!ct.IsCancellationRequested) IsLoadingAlternates = false; }
    }

    private static async Task LoadAlternateImagesSequentiallyAsync(List<AlternatePrintingViewModel> vms, CancellationToken ct)
    {
        foreach (var vm in vms)
        {
            if (ct.IsCancellationRequested) break;
            await vm.LoadImageAsync(ct);
        }
    }

    [RelayCommand]
    private void DeleteCard()
    {
        if (SelectedCard == null) return;
        _db.DeleteCard(SelectedCard.Id);
        Cards.Remove(SelectedCard);
        SelectedCard = null;
    }

}

public class SetItem
{
    public static readonly SetItem All = new("", "All Sets");

    public string Code { get; }
    public string Name { get; }

    public SetItem(string code, string name)
    {
        Code = code;
        Name = name;
    }

    public string Display => string.IsNullOrEmpty(Code) ? "All Sets" : $"{Code} – {Name}";
}
