using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using System.Collections.ObjectModel;

namespace Library.ViewModels;

// ── Pending item ─────────────────────────────────────────────────────────────

public partial class BulkAddItem : ObservableObject
{
    public ScryfallCardData Data { get; }
    [ObservableProperty] private int _quantity = 1;
    [ObservableProperty] private string _condition = "NM";
    [ObservableProperty] private bool _foil;

    public BulkAddItem(ScryfallCardData data) => Data = data;
    public string Label => $"{Data.Name}  ({Data.SetCode.ToUpperInvariant()} #{Data.CollectorNumber})";
}

// ── Duplicate resolution ──────────────────────────────────────────────────────

public partial class DuplicateResolutionItem : ObservableObject
{
    public BulkAddItem Pending  { get; }
    public Card        Existing { get; }

    /// <summary>True = keep/use the existing copy; False = insert a new copy alongside.</summary>
    [ObservableProperty] private bool _useExisting = true;

    public DuplicateResolutionItem(BulkAddItem pending, Card existing)
    {
        Pending  = pending;
        Existing = existing;
    }

    public string CardLabel    => Pending.Label;
    public string ExistingInfo => $"Already owned: {Existing.Quantity}x  {Existing.Condition}  {Existing.SetCode.ToUpperInvariant()} #{Existing.CollectorNumber}";
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

public partial class BulkAddViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;
    private readonly int?            _deckId;

    // ── Search ─────────────────────────────────────────────────────────────
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<ScryfallCardData> _searchResults = new();
    [ObservableProperty] private bool _isSearching;

    // ── Printing picker ────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ScryfallCardData> _printingOptions = new();
    [ObservableProperty] private string _printingFilter = string.Empty;
    [ObservableProperty] private bool _isLoadingPrintings;
    [ObservableProperty] private ScryfallCardData? _selectedPrinting;
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private bool _isLoadingPreview;

    public IEnumerable<ScryfallCardData> FilteredPrintingOptions =>
        string.IsNullOrWhiteSpace(PrintingFilter)
            ? PrintingOptions
            : PrintingOptions.Where(p =>
                p.SetCode.Contains(PrintingFilter, StringComparison.OrdinalIgnoreCase) ||
                p.SetName.Contains(PrintingFilter, StringComparison.OrdinalIgnoreCase));

    // ── Pending list ───────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<BulkAddItem> _pendingItems = new();

    // ── Duplicate resolution ───────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<DuplicateResolutionItem> _duplicates = new();
    [ObservableProperty] private bool _isResolvingDuplicates;

    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _printingsCts;

    partial void OnPrintingFilterChanged(string value) =>
        OnPropertyChanged(nameof(FilteredPrintingOptions));

    partial void OnPrintingOptionsChanged(ObservableCollection<ScryfallCardData> value) =>
        OnPropertyChanged(nameof(FilteredPrintingOptions));

    public bool   IsFromDeck  => _deckId.HasValue;
    public string Title       => IsFromDeck ? "Bulk Add — Deck & Collection" : "Bulk Add to Collection";
    public string SubmitLabel => PendingItems.Count == 0
        ? "Submit"
        : IsFromDeck
            ? $"Add {PendingItems.Count} card(s) to deck & collection"
            : $"Add {PendingItems.Count} card(s) to collection";

    public static string[] Conditions => ["M", "NM", "LP", "MP", "HP", "DMG"];

    public event Action? Done;
    public event Action? Cancelled;

    public BulkAddViewModel(DatabaseService db, ScryfallService scryfall, int? deckId = null)
    {
        _db     = db;
        _scryfall = scryfall;
        _deckId   = deckId;

        PendingItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SubmitLabel));
            SubmitCommand.NotifyCanExecuteChanged();
        };
    }

    // ── Search ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        IsSearching = true;
        try
        {
            var page = await _scryfall.SearchCardsAsync(SearchQuery, _searchCts.Token);
            SearchResults = new ObservableCollection<ScryfallCardData>(page.Cards);
        }
        catch (OperationCanceledException) { }
        finally { IsSearching = false; }
    }

    [RelayCommand]
    private async Task SelectSearchResult(ScryfallCardData card)
    {
        SearchResults.Clear();
        SearchQuery = string.Empty;
        PrintingOptions.Clear();
        SelectedPrinting = card;
        PreviewImage = null;

        _printingsCts?.Cancel();
        _printingsCts = new CancellationTokenSource();
        IsLoadingPrintings = true;
        try
        {
            var printings = await _scryfall.GetAlternatePrintingsAsync(card.Name, _printingsCts.Token);
            if (!_printingsCts.Token.IsCancellationRequested)
            {
                var sorted = printings.OrderBy(p => p.SetCode, StringComparer.OrdinalIgnoreCase).ToList();
                PrintingFilter  = string.Empty;
                PrintingOptions = new ObservableCollection<ScryfallCardData>(sorted);
                SelectedPrinting = sorted.FirstOrDefault(p => p.ScryfallId == card.ScryfallId) ?? card;
            }
        }
        catch (OperationCanceledException) { }
        finally { IsLoadingPrintings = false; }

        await LoadPreviewAsync(SelectedPrinting);
        AddToListCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SelectPrinting(ScryfallCardData printing)
    {
        SelectedPrinting = printing;
        AddToListCommand.NotifyCanExecuteChanged();
        await LoadPreviewAsync(printing);
    }

    private async Task LoadPreviewAsync(ScryfallCardData? card)
    {
        if (card == null) return;
        IsLoadingPreview = true;
        try { PreviewImage = await _scryfall.GetCardImageAsync(card.ScryfallId); }
        finally { IsLoadingPreview = false; }
    }

    // ── Pending list ────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAddToList))]
    private void AddToList()
    {
        if (SelectedPrinting == null) return;
        PendingItems.Add(new BulkAddItem(SelectedPrinting));
    }

    private bool CanAddToList() => SelectedPrinting != null;

    [RelayCommand]
    private void RemoveItem(BulkAddItem item) => PendingItems.Remove(item);

    // ── Submit / duplicate resolution ───────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void Submit()
    {
        // Detect duplicates (exact match by set + collector number)
        var dupes = PendingItems
            .Select(item => (item, existing: _db.GetCardBySetAndCollector(
                item.Data.Name, item.Data.SetCode, item.Data.CollectorNumber)))
            .Where(t => t.existing != null)
            .Select(t => new DuplicateResolutionItem(t.item, t.existing!))
            .ToList();

        if (dupes.Count > 0)
        {
            Duplicates = new ObservableCollection<DuplicateResolutionItem>(dupes);
            IsResolvingDuplicates = true;
        }
        else
        {
            CommitAll([]);
        }
    }

    private bool CanSubmit() => PendingItems.Count > 0;

    [RelayCommand]
    private void ConfirmResolution()
    {
        CommitAll(Duplicates.ToList());
        IsResolvingDuplicates = false;
        Duplicates.Clear();
    }

    [RelayCommand]
    private void CancelResolution()
    {
        IsResolvingDuplicates = false;
        Duplicates.Clear();
    }

    private void CommitAll(IList<DuplicateResolutionItem> resolutions)
    {
        var dupeMap = resolutions.ToDictionary(r => r.Pending);

        foreach (var item in PendingItems)
        {
            int cardId;
            if (dupeMap.TryGetValue(item, out var res) && res.UseExisting)
            {
                cardId = res.Existing.Id;
            }
            else
            {
                var card = new Card
                {
                    Name                  = item.Data.Name,
                    SetCode               = item.Data.SetCode,
                    SetName               = item.Data.SetName,
                    CollectorNumber       = item.Data.CollectorNumber,
                    Rarity                = item.Data.Rarity,
                    Quantity              = item.Quantity,
                    ScryfallId            = item.Data.ScryfallId,
                    Condition             = item.Condition,
                    Foil                  = item.Foil,
                    Language              = "en",
                    PurchasePriceCurrency = "USD",
                    Added                 = DateTime.UtcNow,
                    ColorIdentity         = item.Data.ColorIdentity,
                    ManaCost              = item.Data.ManaCost,
                    TypeLine              = item.Data.TypeLine,
                };
                cardId = _db.AddCard(card);
            }

            if (_deckId.HasValue)
                _db.AddCardToDeck(_deckId.Value, cardId, item.Quantity, false);
        }

        Done?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
