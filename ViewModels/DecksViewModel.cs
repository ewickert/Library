using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using System.Collections.ObjectModel;

namespace Library.ViewModels;

public partial class DecksViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;
    private CancellationTokenSource? _slotsCts;

    // ── Deck list & selected deck ─────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<Deck> _decks = new();
    [ObservableProperty] private Deck? _selectedDeck;

    // ── Deck card content ─────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<DeckCard> _deckCards = new();
    [ObservableProperty] private ObservableCollection<CardSlotViewModel> _deckCardSlots = new();
    [ObservableProperty] private bool _isGridView;
    [ObservableProperty] private double _gridZoom = 1.0;
    public double TileWidth  => Math.Round(150 * _gridZoom);
    public double TileHeight => Math.Round(210 * _gridZoom);
    partial void OnGridZoomChanged(double v) { OnPropertyChanged(nameof(TileWidth)); OnPropertyChanged(nameof(TileHeight)); Services.PreferencesService.Instance.DeckGridZoom = v; }

    // ── Sorted / category view ──────────────────────────────────────────────────
    [ObservableProperty] private bool _isSortedView;
    [ObservableProperty] private ObservableCollection<DeckCategoryViewModel> _deckCategories = new();
    [ObservableProperty] private CardSlotViewModel? _commanderSlot;

    /// <summary>True when the plain flat list is active (sorted off, grid off).</summary>
    public bool IsListViewMode => !IsGridView && !IsSortedView;
    /// <summary>True when the plain flat grid is active (sorted off, grid on).</summary>
    public bool IsGridViewMode => IsGridView && !IsSortedView;

    partial void OnIsGridViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListViewMode));
        OnPropertyChanged(nameof(IsGridViewMode));
        Services.PreferencesService.Instance.DeckIsGridView = value;
    }

    partial void OnIsSortedViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListViewMode));
        OnPropertyChanged(nameof(IsGridViewMode));
        Services.PreferencesService.Instance.DeckIsSortedView = value;
        if (value) RebuildDeckCategories();
    }

    // ── Commander ─────────────────────────────────────────────────────────────
    [ObservableProperty] private DeckCard? _commanderDeckCard;
    [ObservableProperty] private string _commanderColorIdentity = string.Empty;

    partial void OnCommanderDeckCardChanged(DeckCard? value) =>
        OnPropertyChanged(nameof(ShowCommanderPanel));
    [ObservableProperty] private bool _isLoadingCommanderColorIdentity;

    public bool IsCommanderFormat =>
        SelectedDeck?.Format?.Equals("Commander", StringComparison.OrdinalIgnoreCase) == true ||
        SelectedDeck?.Format?.Equals("EDH", StringComparison.OrdinalIgnoreCase) == true;

    public bool ShowCommanderPanel =>
        SelectedDeck?.Format?.Equals("Commander", StringComparison.OrdinalIgnoreCase) == true ||
        SelectedDeck?.Format?.Equals("EDH", StringComparison.OrdinalIgnoreCase) == true ||
        CommanderDeckCard != null;

    // ── Collection pane ───────────────────────────────────────────────────────
    [ObservableProperty] private bool _isCollectionPaneOpen = true;
    [ObservableProperty] private ObservableCollection<Card> _collectionCards = new();
    [ObservableProperty] private ObservableCollection<CardSlotViewModel> _collectionCardSlots = new();
    [ObservableProperty] private bool _isCollectionGridView;
    [ObservableProperty] private double _collectionGridZoom = 1.0;
    public double CollectionTileWidth  => Math.Round(120 * _collectionGridZoom);
    public double CollectionTileHeight => Math.Round(168 * _collectionGridZoom);
    partial void OnCollectionGridZoomChanged(double v) { OnPropertyChanged(nameof(CollectionTileWidth)); OnPropertyChanged(nameof(CollectionTileHeight)); }
    [ObservableProperty] private string _collectionSearchText = string.Empty;
    private CancellationTokenSource? _searchDebounceCts;
    [ObservableProperty] private bool _isColorFiltered;

    // Color identity toggle filters (OR logic)
    [ObservableProperty] private bool _filterW;
    [ObservableProperty] private bool _filterU;
    [ObservableProperty] private bool _filterB;
    [ObservableProperty] private bool _filterR;
    [ObservableProperty] private bool _filterG;
    [ObservableProperty] private bool _filterC;

    // Extra text filters
    [ObservableProperty] private string _collectionManaCostFilter = string.Empty;
    [ObservableProperty] private string _collectionTypeFilter = string.Empty;

    private CancellationTokenSource? _collectionSlotsCts;

    // ── New deck form ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _newDeckName = string.Empty;
    [ObservableProperty] private string _newDeckFormat = string.Empty;
    [ObservableProperty] private string _newDeckDescription = string.Empty;
    [ObservableProperty] private bool _isCreatingDeck;

    public DecksViewModel(DatabaseService db, ScryfallService scryfall)
    {
        _db = db;
        _scryfall = scryfall;

        // Restore last view preferences before loading data so the UI starts in the right mode
        var prefs = Services.PreferencesService.Instance;
        _isGridView    = prefs.DeckIsGridView;
        _isSortedView  = prefs.DeckIsSortedView;
        _gridZoom      = prefs.DeckGridZoom;

        LoadDecks();
        LoadCollectionCards();
    }

    public void LoadDecks() => Decks = new ObservableCollection<Deck>(_db.GetAllDecks());

    // ── View toggle ───────────────────────────────────────────────────────────
    [RelayCommand]
    private void ToggleView() => IsGridView = !IsGridView;

    [RelayCommand]
    private void ToggleSortedView() => IsSortedView = !IsSortedView;

    [RelayCommand]
    private void ToggleCollectionPane() => IsCollectionPaneOpen = !IsCollectionPaneOpen;

    [RelayCommand]
    private void ToggleCollectionView() => IsCollectionGridView = !IsCollectionGridView;

    partial void OnIsCollectionGridViewChanged(bool value)
    {
        if (value) RebuildCollectionSlots();
        else _collectionSlotsCts?.Cancel();
    }

    // ── Selected deck ─────────────────────────────────────────────────────────
    partial void OnSelectedDeckChanged(Deck? value)
    {
        _slotsCts?.Cancel();
        _slotsCts = new CancellationTokenSource();

        DeckCards.Clear();
        DeckCardSlots.Clear();
        CommanderDeckCard = null;
        CommanderColorIdentity = string.Empty;

        OnPropertyChanged(nameof(IsCommanderFormat));
        OnPropertyChanged(nameof(ShowCommanderPanel));

        if (value == null) { RefreshCollectionFilter(); return; }

        var full = _db.GetDeckWithCards(value.Id);
        if (full == null) { RefreshCollectionFilter(); return; }

        DeckCards = new ObservableCollection<DeckCard>(full.Cards);

        // Restore commander from DB
        CommanderDeckCard = full.Cards.FirstOrDefault(dc => dc.IsCommander);
        CommanderColorIdentity = full.CommanderColorIdentity ?? string.Empty;

        var slots = full.Cards
            .Where(dc => dc.Card != null)
            .Select(dc =>
            {
                var slot = new CardSlotViewModel(dc.Card!, _scryfall);
                slot.RemoveFromDeckCommand = new RelayCommand(() => RemoveCardFromDeck(dc));
                if (slot.IsCommanderEligible)
                    slot.SetAsCommanderCommand = new RelayCommand(() => SetAsCommander(dc));
                return slot;
            })
            .ToList();
        DeckCardSlots = new ObservableCollection<CardSlotViewModel>(slots);
        _ = LoadSlotsSequentiallyAsync(slots, _slotsCts.Token);

        if (IsSortedView) RebuildDeckCategories();

        RefreshCollectionFilter();
    }

    private static async Task LoadSlotsSequentiallyAsync(List<CardSlotViewModel> slots, CancellationToken ct)
    {
        foreach (var slot in slots)
        {
            if (ct.IsCancellationRequested) break;
            await slot.LoadImageAsync(ct);
        }
    }

    // ── Collection pane ───────────────────────────────────────────────────────
    private void LoadCollectionCards()
    {
        _allCollectionCards = _db.GetAllCards();
        RefreshCollectionFilter();
    }

    private List<Card> _allCollectionCards = new();

    private void RefreshCollectionFilter()
    {
        IEnumerable<Card> source = _allCollectionCards;

        // Commander color identity filter (subset filter — card must fit in commander's CI)
        IsColorFiltered = false;
        if (IsCommanderFormat && !string.IsNullOrWhiteSpace(CommanderColorIdentity))
        {
            var commanderColors = CommanderColorIdentity
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            source = source.Where(c =>
            {
                if (string.IsNullOrWhiteSpace(c.ColorIdentity))
                    return true; // unknown — show by default (benefit of the doubt)
                var cardColors = c.ColorIdentity
                    .Split(',', StringSplitOptions.RemoveEmptyEntries);
                return cardColors.All(col => commanderColors.Contains(col));
            });
            IsColorFiltered = true;
        }

        // User color filter buttons (OR logic — show cards containing any selected color)
        var activeColors = new List<string>();
        if (FilterW) activeColors.Add("W");
        if (FilterU) activeColors.Add("U");
        if (FilterB) activeColors.Add("B");
        if (FilterR) activeColors.Add("R");
        if (FilterG) activeColors.Add("G");
        if (activeColors.Count > 0 || FilterC)
        {
            source = source.Where(c =>
            {
                var ci = c.ColorIdentity ?? string.Empty;
                if (string.IsNullOrWhiteSpace(ci) && FilterC) return true;
                var cardColors = ci.Split(',', StringSplitOptions.RemoveEmptyEntries);
                return cardColors.Any(col => activeColors.Contains(col, StringComparer.OrdinalIgnoreCase));
            });
        }

        // Name / set / Scryfall-syntax search
        if (!string.IsNullOrWhiteSpace(CollectionSearchText))
            source = source.Where(c => LocalCardFilter.Matches(c, CollectionSearchText));

        // Mana cost filter
        if (!string.IsNullOrWhiteSpace(CollectionManaCostFilter))
        {
            var q = CollectionManaCostFilter;
            source = source.Where(c => c.ManaCost != null &&
                c.ManaCost.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        // Type line filter
        if (!string.IsNullOrWhiteSpace(CollectionTypeFilter))
        {
            var q = CollectionTypeFilter;
            source = source.Where(c => c.TypeLine != null &&
                c.TypeLine.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        CollectionCards = new ObservableCollection<Card>(source.OrderBy(c => c.Name));

        if (IsCollectionGridView) RebuildCollectionSlots();
    }

    private void RebuildCollectionSlots()
    {
        _collectionSlotsCts?.Cancel();
        _collectionSlotsCts = new CancellationTokenSource();
        var slots = CollectionCards
            .Select(c => new CardSlotViewModel(c, _scryfall))
            .ToList();
        CollectionCardSlots = new ObservableCollection<CardSlotViewModel>(slots);
        _ = LoadSlotsSequentiallyAsync(slots, _collectionSlotsCts.Token);
    }

    partial void OnCommanderColorIdentityChanged(string value) => RefreshCollectionFilter();
    partial void OnFilterWChanged(bool value) => RefreshCollectionFilter();
    partial void OnFilterUChanged(bool value) => RefreshCollectionFilter();
    partial void OnFilterBChanged(bool value) => RefreshCollectionFilter();
    partial void OnFilterRChanged(bool value) => RefreshCollectionFilter();
    partial void OnFilterGChanged(bool value) => RefreshCollectionFilter();
    partial void OnFilterCChanged(bool value) => RefreshCollectionFilter();
    partial void OnCollectionManaCostFilterChanged(string value) => RefreshCollectionFilter();
    partial void OnCollectionTypeFilterChanged(string value) => RefreshCollectionFilter();

    [RelayCommand]
    private void AddCardToDeck(Card? card)
    {
        if (card == null || SelectedDeck == null) return;
        _db.AddCardToDeck(SelectedDeck.Id, card.Id, 1, false);
        ReloadDeckCards();
    }

    /// <summary>
    /// Adds a Scryfall card to both the shopping list (as a placeholder) and the current deck.
    /// Called when dragging from the external Scryfall results into the deck pane.
    /// </summary>
    [RelayCommand]
    private async Task AddScryfallCardToDeck(ScryfallResultViewModel? result)
    {
        if (result == null || SelectedDeck == null) return;
        var chosen = result.Data;
        if (ScryfallResultViewModel.GlobalPickPrintingAsync != null)
        {
            var picked = await ScryfallResultViewModel.GlobalPickPrintingAsync(result.Data);
            if (picked == null) return;
            chosen = picked;
        }
        _db.AddToShoppingList(chosen);
        result.IsOnShoppingList = true;
        var cardId = _db.GetPlaceholderCardId(chosen.ScryfallId);
        if (cardId.HasValue)
        {
            _db.AddCardToDeck(SelectedDeck.Id, cardId.Value, 1, false);
            ReloadDeckCards();
        }
    }

    /// <summary>Reloads the current deck's card lists and rebuilds slots. Called externally by ShoppingViewModel.</summary>
    public void ReloadDeckCards()
    {
        if (SelectedDeck == null) return;
        var full = _db.GetDeckWithCards(SelectedDeck.Id);
        if (full == null) return;
        DeckCards = new ObservableCollection<DeckCard>(full.Cards);
        var commander = full.Cards.FirstOrDefault(dc => dc.IsCommander);
        if (commander != null) CommanderDeckCard = commander;

        _slotsCts?.Cancel();
        _slotsCts = new CancellationTokenSource();
        var slots = full.Cards
            .Where(dc => dc.Card != null)
            .Select(dc =>
            {
                var slot = new CardSlotViewModel(dc.Card!, _scryfall);
                slot.RemoveFromDeckCommand = new RelayCommand(() => RemoveCardFromDeck(dc));
                if (slot.IsCommanderEligible)
                    slot.SetAsCommanderCommand = new RelayCommand(() => SetAsCommander(dc));
                return slot;
            })
            .ToList();
        DeckCardSlots = new ObservableCollection<CardSlotViewModel>(slots);
        _ = LoadSlotsSequentiallyAsync(slots, _slotsCts.Token);

        if (IsSortedView) RebuildDeckCategories();
    }

    // ── Sorted category builder ─────────────────────────────────────────────
    private void RebuildDeckCategories()
    {
        // Update commander slot (image binding for the commander panel)
        CommanderSlot = CommanderDeckCard?.Card != null
            ? DeckCardSlots.FirstOrDefault(s => s.Card.Id == CommanderDeckCard.Card.Id)
            : null;

        var nonCommander = DeckCards.Where(dc => !dc.IsCommander).ToList();
        var assigned     = new HashSet<int>();
        var slotById     = DeckCardSlots.Where(s => s.Card != null)
                              .DistinctBy(s => s.Card.Id)
                              .ToDictionary(s => s.Card.Id);

        static bool HasType(DeckCard dc, string type) =>
            dc.Card?.TypeLine?.Contains(type, StringComparison.OrdinalIgnoreCase) == true;

        var categories = new (string Icon, string Name, Func<DeckCard, bool> Match)[]
        {
            ("⚔",  "Creatures",     dc => HasType(dc, "Creature")),
            ("⭐", "Planeswalkers", dc => HasType(dc, "Planeswalker")),
            ("⚡", "Instants",      dc => HasType(dc, "Instant")),
            ("◎",  "Sorceries",     dc => HasType(dc, "Sorcery")),
            ("✦",  "Enchantments",  dc => HasType(dc, "Enchantment") && !HasType(dc, "Creature") && !HasType(dc, "Artifact")),
            ("⚙",  "Artifacts",     dc => HasType(dc, "Artifact")    && !HasType(dc, "Creature")),
            ("⛰",  "Lands",         dc => HasType(dc, "Land")),
        };

        var groups = new List<DeckCategoryViewModel>();
        foreach (var (icon, name, match) in categories)
        {
            var cards = nonCommander.Where(dc => match(dc) && !assigned.Contains(dc.Id)).ToList();
            foreach (var dc in cards) assigned.Add(dc.Id);
            if (cards.Count > 0)
                groups.Add(new DeckCategoryViewModel(icon, name, cards, slotById,
                    RemoveCardFromDeck, dc => SetAsCommander(dc)));
        }

        var other = nonCommander.Where(dc => !assigned.Contains(dc.Id)).ToList();
        if (other.Count > 0)
            groups.Add(new DeckCategoryViewModel("•", "Other", other, slotById,
                RemoveCardFromDeck, dc => SetAsCommander(dc)));

        DeckCategories = new ObservableCollection<DeckCategoryViewModel>(groups);
    }

    // ── Commander management ──────────────────────────────────────────────────
    [RelayCommand]
    private void SetAsCommander(DeckCard? deckCard)
    {
        if (deckCard == null || SelectedDeck == null) return;

        _db.SetDeckCardCommander(SelectedDeck.Id, deckCard.Id);

        // Reload local state
        var full = _db.GetDeckWithCards(SelectedDeck.Id);
        if (full == null) return;
        var foundCommander = full.Cards.FirstOrDefault(dc => dc.IsCommander);
        DeckCards = new ObservableCollection<DeckCard>(full.Cards);
        CommanderDeckCard = foundCommander;

        // Refresh the commander image slot
        CommanderSlot = CommanderDeckCard?.Card != null
            ? DeckCardSlots.FirstOrDefault(s => s.Card.Id == CommanderDeckCard.Card.Id)
            : null;

        // Rebuild categories and switch to sorted view so the commander column appears
        RebuildDeckCategories();
        IsSortedView = true;

        // Fetch color identity from Scryfall in the background (does not block the command)
        _ = FetchAndSaveCommanderColorIdentityAsync(deckCard.Card, SelectedDeck);
    }

    private async Task FetchAndSaveCommanderColorIdentityAsync(Card? card, Deck deck)
    {
        IsLoadingCommanderColorIdentity = true;
        try
        {
            string? ci = null;
            if (card != null)
            {
                if (!string.IsNullOrWhiteSpace(card.ColorIdentity))
                    ci = card.ColorIdentity;
                else if (!string.IsNullOrWhiteSpace(card.ScryfallId))
                    ci = await _scryfall.GetColorIdentityAsync(card.ScryfallId);
            }
            CommanderColorIdentity = ci ?? string.Empty;
            deck.CommanderColorIdentity = CommanderColorIdentity;
            _db.UpdateDeck(deck);
        }
        catch { /* color identity is cosmetic — don't crash if Scryfall is unavailable */ }
        finally { IsLoadingCommanderColorIdentity = false; }
    }

    [RelayCommand]
    private void ClearCommander()
    {
        if (SelectedDeck == null) return;
        _db.SetDeckCardCommander(SelectedDeck.Id, null);
        CommanderDeckCard = null;
        CommanderColorIdentity = string.Empty;
        SelectedDeck.CommanderColorIdentity = null;
        _db.UpdateDeck(SelectedDeck);

        var full = _db.GetDeckWithCards(SelectedDeck.Id);
        if (full != null) DeckCards = new ObservableCollection<DeckCard>(full.Cards);
    }

    // ── Deck CRUD ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private void StartCreateDeck()
    {
        NewDeckName = string.Empty;
        NewDeckFormat = string.Empty;
        NewDeckDescription = string.Empty;
        IsCreatingDeck = true;
    }

    [RelayCommand]
    private void ConfirmCreateDeck()
    {
        if (string.IsNullOrWhiteSpace(NewDeckName)) return;
        var deck = new Deck
        {
            Name = NewDeckName,
            Format = string.IsNullOrWhiteSpace(NewDeckFormat) ? null : NewDeckFormat,
            Description = string.IsNullOrWhiteSpace(NewDeckDescription) ? null : NewDeckDescription
        };
        _db.AddDeck(deck);
        Decks.Add(deck);
        IsCreatingDeck = false;
        SelectedDeck = deck;
    }

    [RelayCommand]
    private void CancelCreateDeck() => IsCreatingDeck = false;

    [RelayCommand]
    private void DeleteDeck()
    {
        if (SelectedDeck == null) return;
        _db.DeleteDeck(SelectedDeck.Id);
        Decks.Remove(SelectedDeck);
        SelectedDeck = null;
    }

    [RelayCommand]
    private void RemoveCardFromDeck(DeckCard? deckCard)
    {
        if (deckCard == null) return;
        _db.RemoveCardFromDeck(deckCard.Id);
        if (CommanderDeckCard?.Id == deckCard.Id)
        {
            CommanderDeckCard = null;
            CommanderColorIdentity = string.Empty;
            if (SelectedDeck != null)
            {
                SelectedDeck.CommanderColorIdentity = null;
                _db.UpdateDeck(SelectedDeck);
            }
        }
        ReloadDeckCards();
    }

    [RelayCommand]
    private void RemoveCardByCardFromDeck(Card? card)
    {
        if (card == null) return;
        var match = DeckCards.FirstOrDefault(dc => dc.Card?.Id == card.Id);
        if (match != null)
            RemoveCardFromDeck(match);
    }

    // ── External Scryfall search for collection pane ──────────────────────────

    [ObservableProperty] private bool _isCollectionExternalSearch;
    [ObservableProperty] private bool _isCollectionExternalSearching;
    [ObservableProperty] private bool _isCollectionExternalGridView;
    [ObservableProperty] private ObservableCollection<ScryfallResultViewModel> _collectionExternalResults = new();
    private CancellationTokenSource? _externalSearchCts;
    private CancellationTokenSource? _externalGridCts;

    partial void OnIsCollectionExternalGridViewChanged(bool value)
    {
        if (value) _ = LoadExternalGridImagesAsync();
    }

    private async Task LoadExternalGridImagesAsync()
    {
        _externalGridCts?.Cancel();
        _externalGridCts = new CancellationTokenSource();
        var cts = _externalGridCts;
        foreach (var vm in CollectionExternalResults)
        {
            if (cts.IsCancellationRequested) break;
            await vm.LoadImageAsync(cts.Token);
        }
    }

    partial void OnIsCollectionExternalSearchChanged(bool value)
    {
        CollectionExternalResults.Clear();
        if (value && !string.IsNullOrWhiteSpace(CollectionSearchText))
            _ = RunExternalSearchAsync(CollectionSearchText);
    }

    partial void OnCollectionSearchTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts = new CancellationTokenSource();
        var cts = _searchDebounceCts;
        _ = Task.Delay(200, cts.Token).ContinueWith(
            _ => {
                RefreshCollectionFilter();
                if (IsCollectionExternalSearch) _ = RunExternalSearchAsync(value);
            },
            cts.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    [RelayCommand]
    private async Task SearchCollectionExternal()
    {
        await RunExternalSearchAsync(CollectionSearchText);
    }

    [RelayCommand]
    private void ToggleCollectionExternalView()
    {
        IsCollectionExternalGridView = !IsCollectionExternalGridView;
    }

    private async Task RunExternalSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) { CollectionExternalResults.Clear(); return; }

        _externalSearchCts?.Cancel();
        _externalSearchCts = new CancellationTokenSource();
        var cts = _externalSearchCts;

        IsCollectionExternalSearching = true;
        try
        {
            var results = await _scryfall.SearchCardsAsync(query, cts.Token);
            if (cts.IsCancellationRequested) return;

            var ownedIds = _db.GetAllCards()
                              .Where(c => c.Quantity > 0 && !c.IsPlaceholder)
                              .Select(c => c.ScryfallId)
                              .Where(id => id != null)
                              .ToHashSet()!;

            var vms = results
                .Where(r => !ownedIds.Contains(r.ScryfallId))
                .Select(r => new ScryfallResultViewModel(r, _db, _scryfall))
                .ToList();

            CollectionExternalResults = new ObservableCollection<ScryfallResultViewModel>(vms);
            if (IsCollectionExternalGridView) _ = LoadExternalGridImagesAsync();
        }
        catch (OperationCanceledException) { }
        finally { if (!cts.IsCancellationRequested) IsCollectionExternalSearching = false; }
    }
}
