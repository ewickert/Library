using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using System.Collections.ObjectModel;

namespace Library.ViewModels;

public partial class BulkEditCardViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;

    public IReadOnlyList<Card> Cards { get; }
    public string Title => $"Edit {Cards.Count} Cards";

    // Printing picker — null SelectedPrinting means "don't change printing"
    [ObservableProperty] private string _printingSearchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<ScryfallCardData> _scryfallResults = new();
    [ObservableProperty] private bool _isSearchingPrintings;
    [ObservableProperty] private ObservableCollection<ScryfallCardData> _printingOptions = new();
    [ObservableProperty] private bool _isLoadingPrintings;
    [ObservableProperty] private ScryfallCardData? _selectedPrinting;
    [ObservableProperty] private Bitmap? _printingPreview;
    [ObservableProperty] private bool _isLoadingPreview;

    // Field overrides — leading empty-string item means "no change"
    public static string[] ConditionOptions => ["(no change)", .. AddEditCardViewModel.Conditions];
    public static string[] LanguageOptions  => ["(no change)", .. AddEditCardViewModel.Languages];
    public static string[] FoilOptions      => ["(no change)", "Foil", "Non-foil"];

    [ObservableProperty] private string _condition = "(no change)";
    [ObservableProperty] private string _language  = "(no change)";
    [ObservableProperty] private string _foilSelection = "(no change)";

    public event Action? Saved;
    public event Action? Cancelled;

    private CancellationTokenSource? _searchCts;

    public BulkEditCardViewModel(DatabaseService db, ScryfallService scryfall, IReadOnlyList<Card> cards)
    {
        _db = db;
        _scryfall = scryfall;
        Cards = cards;

        _ = LoadPrintingsAsync(cards[0].Name);
    }

    private async Task LoadPrintingsAsync(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName)) return;
        IsLoadingPrintings = true;
        try
        {
            var results = await _scryfall.GetAlternatePrintingsAsync(cardName);
            PrintingOptions = new ObservableCollection<ScryfallCardData>(results);
        }
        finally { IsLoadingPrintings = false; }
    }

    [RelayCommand]
    private async Task SearchPrintings()
    {
        if (string.IsNullOrWhiteSpace(PrintingSearchQuery)) return;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        IsSearchingPrintings = true;
        try
        {
            var page = await _scryfall.SearchCardsAsync(PrintingSearchQuery, _searchCts.Token);
            ScryfallResults = new ObservableCollection<ScryfallCardData>(page.Cards);
        }
        catch (OperationCanceledException) { }
        finally { IsSearchingPrintings = false; }
    }

    [RelayCommand]
    private async Task SelectPrinting(ScryfallCardData printing)
    {
        SelectedPrinting = printing;
        ScryfallResults.Clear();
        PrintingSearchQuery = string.Empty;

        IsLoadingPreview = true;
        try { PrintingPreview = await _scryfall.GetCardImageAsync(printing.ScryfallId); }
        finally { IsLoadingPreview = false; }
    }

    [RelayCommand]
    private void ClearPrinting()
    {
        SelectedPrinting = null;
        PrintingPreview = null;
    }

    [RelayCommand]
    private void Save()
    {
        foreach (var card in Cards)
        {
            bool changed = false;

            if (SelectedPrinting != null)
            {
                card.SetCode = SelectedPrinting.SetCode;
                card.SetName = SelectedPrinting.SetName;
                card.CollectorNumber = SelectedPrinting.CollectorNumber;
                card.Rarity = SelectedPrinting.Rarity;
                card.ScryfallId = SelectedPrinting.ScryfallId;
                card.ColorIdentity = SelectedPrinting.ColorIdentity;
                card.ManaCost = SelectedPrinting.ManaCost;
                card.TypeLine = SelectedPrinting.TypeLine;
                changed = true;
            }

            if (Condition != "(no change)") { card.Condition = Condition; changed = true; }
            if (Language  != "(no change)") { card.Language  = Language;  changed = true; }
            if (FoilSelection == "Foil")     { card.Foil = true;  changed = true; }
            if (FoilSelection == "Non-foil") { card.Foil = false; changed = true; }

            if (changed) _db.UpdateCard(card);
        }

        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
