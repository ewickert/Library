using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;

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
        OnPropertyChanged(nameof(DeckViewModeIndex));
        Services.PreferencesService.Instance.DeckIsGridView = value;
    }

    partial void OnIsSortedViewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsListViewMode));
        OnPropertyChanged(nameof(IsGridViewMode));
        OnPropertyChanged(nameof(DeckViewModeIndex));
        Services.PreferencesService.Instance.DeckIsSortedView = value;
        if (value) RebuildDeckCategories();
    }

    public int DeckViewModeIndex
    {
        get => IsSortedView ? 2 : IsGridView ? 1 : 0;
        set
        {
            IsGridView = value == 1;
            IsSortedView = value == 2;
        }
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

    // ── Deck sidebar ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isDeckSidebarOpen = true;

    [RelayCommand]
    private void ToggleDeckSidebar() => IsDeckSidebarOpen = !IsDeckSidebarOpen;

    // ── Card detail panel ─────────────────────────────────────────────────────
    [ObservableProperty] private CardSlotViewModel? _detailSlot;
    [ObservableProperty] private string? _detailOracleText;
    [ObservableProperty] private string? _detailPowerToughness;
    public bool IsCardDetailOpen => DetailSlot != null;
    public bool HasDetailOracleText => !string.IsNullOrEmpty(DetailOracleText);
    public bool HasDetailPowerToughness => !string.IsNullOrEmpty(DetailPowerToughness);
    private const double CardDetailHeight = 270;
    public Avalonia.Thickness SidePaneBottomMargin =>
        IsCardDetailOpen ? new Avalonia.Thickness(4, 8, 8, CardDetailHeight + 8) : new Avalonia.Thickness(4, 8, 8, 8);
    private CancellationTokenSource? _detailCts;

    partial void OnDetailSlotChanged(CardSlotViewModel? value)
    {
        OnPropertyChanged(nameof(IsCardDetailOpen));
        OnPropertyChanged(nameof(SidePaneBottomMargin));
    }

    partial void OnDetailOracleTextChanged(string? value) =>
        OnPropertyChanged(nameof(HasDetailOracleText));

    partial void OnDetailPowerToughnessChanged(string? value) =>
        OnPropertyChanged(nameof(HasDetailPowerToughness));

    [RelayCommand]
    private void CloseCardDetail()
    {
        _detailCts?.Cancel();
        DetailSlot = null;
        DetailOracleText = null;
        DetailPowerToughness = null;
    }

    [RelayCommand]
    private async Task OpenCardDetail(Card card)
    {
        if (DetailSlot?.Card.Id == card.Id)
        {
            CloseCardDetail();
            return;
        }
        _detailCts?.Cancel();
        _detailCts = new CancellationTokenSource();
        var ct = _detailCts.Token;

        var slot = new CardSlotViewModel(card, _scryfall);
        DetailSlot = slot;
        DetailOracleText = null;
        DetailPowerToughness = null;

        var imageTask = slot.LoadImageAsync(ct);
        var textTask = _scryfall.GetCardTextDataAsync(card, ct);
        await Task.WhenAll(imageTask, textTask);

        if (!ct.IsCancellationRequested)
        {
            var text = textTask.Result;
            DetailOracleText = text?.OracleText;
            DetailPowerToughness = text switch
            {
                { Power: not null, Toughness: not null } => $"{text.Power}/{text.Toughness}",
                { Loyalty: not null } => $"Loyalty: {text.Loyalty}",
                { Defense: not null } => $"Defense: {text.Defense}",
                _ => null
            };
        }
    }

    // ── Collection pane ───────────────────────────────────────────────────────
    [ObservableProperty] private bool _isCollectionPaneOpen = true;
    [ObservableProperty] private bool _isStatsPaneOpen;
    [ObservableProperty] private string? _activeDeckColorFilter;
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

    // ── Deck card count ───────────────────────────────────────────────────────
    public int DeckTotalCardCount => _allDeckCards.Sum(dc => dc.Quantity);
    public int? DeckCardLimit => SelectedDeck?.Format?.Equals("Commander", StringComparison.OrdinalIgnoreCase) == true ||
                                 SelectedDeck?.Format?.Equals("EDH", StringComparison.OrdinalIgnoreCase) == true ? 100 :
                                 SelectedDeck?.Format?.Equals("Standard", StringComparison.OrdinalIgnoreCase) == true ||
                                 SelectedDeck?.Format?.Equals("Modern", StringComparison.OrdinalIgnoreCase) == true ||
                                 SelectedDeck?.Format?.Equals("Pioneer", StringComparison.OrdinalIgnoreCase) == true ? 60 :
                                 (int?)null;
    public string DeckCountText => DeckCardLimit.HasValue ? $"{DeckTotalCardCount}/{DeckCardLimit}" : $"{DeckTotalCardCount}";
    public bool DeckCountOverLimit => DeckCardLimit.HasValue && DeckTotalCardCount > DeckCardLimit.Value;

    [ObservableProperty] private string? _duplicateWarning;

    [RelayCommand]
    private void ClearDuplicateWarning() => DuplicateWarning = null;

    private void NotifyDeckCount()
    {
        OnPropertyChanged(nameof(DeckTotalCardCount));
        OnPropertyChanged(nameof(DeckCardLimit));
        OnPropertyChanged(nameof(DeckCountText));
        OnPropertyChanged(nameof(DeckCountOverLimit));
    }

    // ── New deck form ─────────────────────────────────────────────────────────
    [ObservableProperty] private string _newDeckName = string.Empty;
    [ObservableProperty] private string _newDeckFormat = string.Empty;
    [ObservableProperty] private string _newDeckDescription = string.Empty;
    [ObservableProperty] private bool _isCreatingDeck;

    // ── Deck stats pane ──────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<DeckCurveBinViewModel> _deckCurveBins = new();
    [ObservableProperty] private int _deckLandCount;
    [ObservableProperty] private int _deckLandW;
    [ObservableProperty] private int _deckLandU;
    [ObservableProperty] private int _deckLandB;
    [ObservableProperty] private int _deckLandR;
    [ObservableProperty] private int _deckLandG;
    [ObservableProperty] private int _deckLandC;
    [ObservableProperty] private int _deckCardsW;
    [ObservableProperty] private int _deckCardsU;
    [ObservableProperty] private int _deckCardsB;
    [ObservableProperty] private int _deckCardsR;
    [ObservableProperty] private int _deckCardsG;
    [ObservableProperty] private int _deckCardsC;
    [ObservableProperty] private ObservableCollection<DeckColorHistogramItemViewModel> _landColorHistogram = new();
    [ObservableProperty] private ObservableCollection<DeckColorHistogramItemViewModel> _cardColorHistogram = new();
    [ObservableProperty] private string _meanTurnsToFirstLandMissText = "-";
    [ObservableProperty] private string _deckTotalPriceText = "$0.00";

    public bool HasActiveDeckColorFilter => !string.IsNullOrWhiteSpace(ActiveDeckColorFilter);
    public string ActiveDeckColorFilterText => ActiveDeckColorFilter ?? "None";

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
    private void ToggleStatsPane() => IsStatsPaneOpen = !IsStatsPaneOpen;

    [RelayCommand]
    private void ToggleDeckColorFilter(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return;

        var normalized = color.Trim().ToUpperInvariant();
        ActiveDeckColorFilter = string.Equals(ActiveDeckColorFilter, normalized, StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    partial void OnActiveDeckColorFilterChanged(string? value)
    {
        ApplyDeckColorFilter();
        RecomputeDeckStats();
        OnPropertyChanged(nameof(HasActiveDeckColorFilter));
        OnPropertyChanged(nameof(ActiveDeckColorFilterText));
    }

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
        _allDeckCards.Clear();

        DeckCards.Clear();
        DeckCardSlots.Clear();
        CommanderDeckCard = null;
        CommanderColorIdentity = string.Empty;

        OnPropertyChanged(nameof(IsCommanderFormat));
        OnPropertyChanged(nameof(ShowCommanderPanel));
        DuplicateWarning = null;
        NotifyDeckCount();

        if (value == null)
        {
            ActiveDeckColorFilter = null;
            ApplyDeckColorFilter();
            RefreshCollectionFilter();
            RecomputeDeckStats();
            return;
        }

        var full = _db.GetDeckWithCards(value.Id);
        if (full == null)
        {
            ActiveDeckColorFilter = null;
            ApplyDeckColorFilter();
            RefreshCollectionFilter();
            RecomputeDeckStats();
            return;
        }

        _allDeckCards = full.Cards.ToList();
        NotifyDeckCount();

        // Restore commander from DB
        CommanderDeckCard = full.Cards.FirstOrDefault(dc => dc.IsCommander);
        CommanderColorIdentity = full.CommanderColorIdentity ?? string.Empty;

        ActiveDeckColorFilter = null;
        ApplyDeckColorFilter();
        var slots = DeckCardSlots.ToList();
        _ = LoadSlotsSequentiallyAsync(slots, _slotsCts.Token);

        RefreshCollectionFilter();
        RecomputeDeckStats();
    }

    private List<DeckCard> _allDeckCards = new();

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

    private static bool IsBasicLand(Card card) =>
        card.TypeLine?.Contains("Basic Land", StringComparison.OrdinalIgnoreCase) == true;

    [RelayCommand]
    private void AddCardToDeck(Card? card)
    {
        if (card == null || SelectedDeck == null) return;

        DuplicateWarning = null;
        if (IsCommanderFormat && !IsBasicLand(card))
        {
            var existing = _allDeckCards.FirstOrDefault(dc =>
                dc.Card.Name.Equals(card.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                DuplicateWarning = $"\"{card.Name}\" is already in the deck — Commander decks allow only one copy of each non-basic card.";
                return;
            }
        }

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
        _allDeckCards = full.Cards.ToList();
        var commander = full.Cards.FirstOrDefault(dc => dc.IsCommander);
        if (commander != null) CommanderDeckCard = commander;
        else CommanderDeckCard = null;

        _slotsCts?.Cancel();
        _slotsCts = new CancellationTokenSource();
        ApplyDeckColorFilter();
        var slots = DeckCardSlots.ToList();
        _ = LoadSlotsSequentiallyAsync(slots, _slotsCts.Token);

        NotifyDeckCount();
        RecomputeDeckStats();
    }

    private void ApplyDeckColorFilter()
    {
        IEnumerable<DeckCard> source = _allDeckCards;
        if (!string.IsNullOrWhiteSpace(ActiveDeckColorFilter))
            source = source.Where(dc => CardMatchesColor(dc.Card, ActiveDeckColorFilter!));

        var filtered = source.ToList();
        DeckCards = new ObservableCollection<DeckCard>(filtered);

        var slots = filtered
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

        if (IsSortedView)
            RebuildDeckCategories();
    }

    private void RecomputeDeckStats()
    {
        if (SelectedDeck == null || _allDeckCards.Count == 0)
        {
            DeckCurveBins = new ObservableCollection<DeckCurveBinViewModel>();
            DeckLandCount = 0;
            DeckLandW = 0;
            DeckLandU = 0;
            DeckLandB = 0;
            DeckLandR = 0;
            DeckLandG = 0;
            DeckLandC = 0;
            DeckCardsW = 0;
            DeckCardsU = 0;
            DeckCardsB = 0;
            DeckCardsR = 0;
            DeckCardsG = 0;
            DeckCardsC = 0;
            LandColorHistogram = new ObservableCollection<DeckColorHistogramItemViewModel>();
            CardColorHistogram = new ObservableCollection<DeckColorHistogramItemViewModel>();
            MeanTurnsToFirstLandMissText = "-";
            DeckTotalPriceText = "$0.00";
            return;
        }

        var entries = _allDeckCards.Where(dc => dc.Card != null && dc.Quantity > 0).ToList();

        var curveCounts = new int[8];
        var landColorCounts = CreateColorCounter();
        var cardColorCounts = CreateColorCounter();
        var landFlags = new List<bool>();
        decimal totalPrice = 0;

        foreach (var dc in entries)
        {
            var card = dc.Card!;
            var qty = Math.Max(0, dc.Quantity);
            if (qty == 0)
                continue;

            var isLand = HasType(card, "Land");

            if (isLand)
            {
                AddColorCounts(landColorCounts, card.ColorIdentity, qty);
                for (var i = 0; i < qty; i++) landFlags.Add(true);
            }
            else
            {
                var mv = EstimateManaValue(card.ManaCost);
                var idx = Math.Clamp(mv, 0, 7);
                curveCounts[idx] += qty;
                for (var i = 0; i < qty; i++) landFlags.Add(false);
            }

            AddColorCounts(cardColorCounts, card.ColorIdentity, qty);

            var price = card.CurrentMarketPrice ?? card.BaselineMarketPrice ?? card.PurchasePrice ?? 0m;
            totalPrice += price * qty;
        }

        DeckLandCount = landFlags.Count(v => v);
        DeckLandW = landColorCounts["W"];
        DeckLandU = landColorCounts["U"];
        DeckLandB = landColorCounts["B"];
        DeckLandR = landColorCounts["R"];
        DeckLandG = landColorCounts["G"];
        DeckLandC = landColorCounts["C"];

        DeckCardsW = cardColorCounts["W"];
        DeckCardsU = cardColorCounts["U"];
        DeckCardsB = cardColorCounts["B"];
        DeckCardsR = cardColorCounts["R"];
        DeckCardsG = cardColorCounts["G"];
        DeckCardsC = cardColorCounts["C"];

        LandColorHistogram = BuildColorHistogram(landColorCounts, 140, ActiveDeckColorFilter);
        CardColorHistogram = BuildColorHistogram(cardColorCounts, 140, ActiveDeckColorFilter);

        var maxCurve = Math.Max(1, curveCounts.Max());
        var labels = new[] { "0", "1", "2", "3", "4", "5", "6", "7+" };
        DeckCurveBins = new ObservableCollection<DeckCurveBinViewModel>(
            labels.Select((label, i) =>
            {
                var count = curveCounts[i];
                var width = count == 0 ? 2 : (count * 160.0 / maxCurve);
                return new DeckCurveBinViewModel(label, count, width);
            }));

        var meanTurn = EstimateMeanFirstLandMissTurn(landFlags, 2500, 20);
        MeanTurnsToFirstLandMissText = meanTurn.HasValue
            ? meanTurn.Value.ToString("0.0", CultureInfo.InvariantCulture) + " turns"
            : ">20 turns";

        DeckTotalPriceText = "$" + totalPrice.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, int> CreateColorCounter() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["W"] = 0,
            ["U"] = 0,
            ["B"] = 0,
            ["R"] = 0,
            ["G"] = 0,
            ["C"] = 0,
        };

    private static void AddColorCounts(Dictionary<string, int> counter, string? colorIdentity, int qty)
    {
        if (string.IsNullOrWhiteSpace(colorIdentity))
        {
            counter["C"] += qty;
            return;
        }

        var colors = colorIdentity
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => counter.ContainsKey(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (colors.Count == 0)
        {
            counter["C"] += qty;
            return;
        }

        foreach (var color in colors)
            counter[color] += qty;
    }

    private static bool HasType(Card card, string type) =>
        card.TypeLine?.Contains(type, StringComparison.OrdinalIgnoreCase) == true;

    private static bool CardMatchesColor(Card? card, string color)
    {
        if (card == null)
            return false;

        if (string.IsNullOrWhiteSpace(card.ColorIdentity))
            return string.Equals(color, "C", StringComparison.OrdinalIgnoreCase);

        var colors = card.ColorIdentity
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (colors.Count == 0)
            return string.Equals(color, "C", StringComparison.OrdinalIgnoreCase);

        return colors.Contains(color);
    }

    private static int EstimateManaValue(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
            return 0;

        var total = 0;
        foreach (Match match in Regex.Matches(manaCost, "\\{([^}]+)\\}"))
        {
            var symbol = match.Groups[1].Value.Trim();
            if (int.TryParse(symbol, out var numeric))
            {
                total += numeric;
                continue;
            }

            if (symbol.Equals("X", StringComparison.OrdinalIgnoreCase))
                continue;

            // Hybrid and phyrexian symbols still contribute 1 to mana value.
            total += 1;
        }

        return total;
    }

    private static double? EstimateMeanFirstLandMissTurn(List<bool> isLandDeck, int simulations, int maxTurns)
    {
        if (isLandDeck.Count == 0)
            return null;

        var rng = new Random(1337 + isLandDeck.Count);
        var turnTotal = 0.0;

        for (var sim = 0; sim < simulations; sim++)
        {
            var deck = isLandDeck.ToArray();
            Shuffle(deck, rng);

            var handSize = Math.Min(7, deck.Length);
            var landsInHand = 0;
            for (var i = 0; i < handSize; i++)
                if (deck[i]) landsInHand++;

            var drawIndex = handSize;
            var missedTurn = maxTurns + 1;

            for (var turn = 1; turn <= maxTurns; turn++)
            {
                if (landsInHand <= 0)
                {
                    missedTurn = turn;
                    break;
                }

                landsInHand -= 1;

                if (drawIndex < deck.Length)
                {
                    if (deck[drawIndex]) landsInHand += 1;
                    drawIndex += 1;
                }
            }

            turnTotal += missedTurn;
        }

        return turnTotal / simulations;
    }

    private static void Shuffle(bool[] array, Random rng)
    {
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    private static ObservableCollection<DeckColorHistogramItemViewModel> BuildColorHistogram(
        Dictionary<string, int> counts,
        double maxWidth,
        string? activeColor)
    {
        var order = new[] { "W", "U", "B", "R", "G", "C" };
        var maxCount = Math.Max(1, order.Max(c => counts[c]));
        var hasActive = !string.IsNullOrWhiteSpace(activeColor);

        var items = order.Select(color =>
        {
            var count = counts[color];
            var width = count == 0 ? 2 : count * maxWidth / maxCount;
            var isActive = string.Equals(activeColor, color, StringComparison.OrdinalIgnoreCase);
            return new DeckColorHistogramItemViewModel(
                color,
                count,
                width,
                color switch
                {
                    "W" => "#EAD9A8",
                    "U" => "#4D8CD8",
                    "B" => "#3A3A44",
                    "R" => "#D15A4A",
                    "G" => "#4FA069",
                    _ => "#8B8B8B",
                },
                color is "W" or "C" ? "#1F1F1F" : "White",
                isActive,
                hasActive ? (isActive ? 1.0 : 0.4) : 1.0);
        });

        return new ObservableCollection<DeckColorHistogramItemViewModel>(items);
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

        ReloadDeckCards();
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

        ReloadDeckCards();
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
