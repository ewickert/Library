using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Services;
using MtgJson;
using MtgJson.Models;
using System.Collections.ObjectModel;
using LibraryCard = Library.Models.Card;

namespace Library.ViewModels;

public partial class MtgJsonPaneViewModel : ObservableObject
{
    private readonly MtgJsonService _mtgJson;
    private readonly DatabaseService _db;

    private List<DeckEntry>? _allEntries;
    private CancellationTokenSource? _listingCts;
    private CancellationTokenSource? _deckLoadCts;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private bool _isLoadingDeck = false;
    [ObservableProperty] private bool _isImporting = false;
    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private ObservableCollection<MtgJsonDeckViewModel> _deckResults = new();
    [ObservableProperty] private MtgJsonDeckViewModel? _selectedDeckResult;
    [ObservableProperty] private Deck? _selectedDeck;

    public Action? OnCollectionImported { get; set; }
    public Func<Deck, Task>? OnCloneToDeckBuilder { get; set; }

    public MtgJsonPaneViewModel(MtgJsonService mtgJson, DatabaseService db)
    {
        _mtgJson = mtgJson;
        _db = db;
    }

    [RelayCommand]
    private async Task Search()
    {
        SelectedDeckResult = null;
        SelectedDeck = null;
        StatusText = string.Empty;

        if (_allEntries == null)
            await LoadListingAsync();

        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_allEntries != null)
            ApplyFilter();
    }

    private async Task LoadListingAsync()
    {
        _listingCts?.Cancel();
        _listingCts = new CancellationTokenSource();
        IsLoading = true;
        try
        {
            _allEntries = await _mtgJson.GetDeckListingAsync(_listingCts.Token);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        if (_allEntries == null) return;

        var query = SearchText.Trim();
        IEnumerable<DeckEntry> filtered = _allEntries;

        if (!string.IsNullOrWhiteSpace(query))
            filtered = filtered.Where(e =>
                e.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                e.SetCode.Contains(query, StringComparison.OrdinalIgnoreCase));

        DeckResults = new ObservableCollection<MtgJsonDeckViewModel>(
            filtered.Take(100).Select(e => new MtgJsonDeckViewModel(e)));

        StatusText = DeckResults.Count == 0 ? "No decks found." : string.Empty;
    }

    [RelayCommand]
    private void SelectDeckResult(MtgJsonDeckViewModel? vm)
    {
        if (SelectedDeckResult == vm) return;
        SelectedDeckResult = vm;
        SelectedDeck = null;
        StatusText = string.Empty;

        if (vm != null)
            _ = LoadDeckDetailAsync(vm.Data.FileName);
    }

    private async Task LoadDeckDetailAsync(string fileName)
    {
        _deckLoadCts?.Cancel();
        _deckLoadCts = new CancellationTokenSource();
        var cts = _deckLoadCts;

        IsLoadingDeck = true;
        try
        {
            var deckFile = await _mtgJson.GetDeckAsync(fileName, cts.Token);
            if (cts.IsCancellationRequested) return;
            SelectedDeck = deckFile?.Data;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!cts.IsCancellationRequested) IsLoadingDeck = false;
        }
    }

    [RelayCommand]
    private void ImportToCollection()
    {
        if (SelectedDeck == null || IsImporting) return;

        IsImporting = true;
        StatusText = "Importing…";

        try
        {
            int added = 0, skipped = 0;

            foreach (var deckCard in SelectedDeck.Commander.Concat(SelectedDeck.MainBoard))
            {
                var scryfallId = deckCard.Identifiers.ScryfallId;

                if (!string.IsNullOrEmpty(scryfallId) && _db.IsInCollectionByScryfallId(scryfallId))
                {
                    skipped++;
                    continue;
                }

                _db.AddCard(new LibraryCard
                {
                    Name = deckCard.Name,
                    ScryfallId = scryfallId ?? string.Empty,
                    SetCode = deckCard.SetCode,
                    SetName = string.Empty,
                    CollectorNumber = deckCard.Number,
                    Quantity = deckCard.Count,
                    IsPlaceholder = false,
                    ColorIdentity = string.Join(",", deckCard.ColorIdentity),
                    ManaCost = deckCard.ManaCost ?? string.Empty,
                    TypeLine = BuildTypeLine(deckCard),
                    Added = DateTime.UtcNow,
                });
                added++;
            }

            StatusText = added == 0 && skipped > 0
                ? $"All {skipped} cards already in collection."
                : $"Added {added} card(s){(skipped > 0 ? $", skipped {skipped} already owned" : "")}.";

            OnCollectionImported?.Invoke();
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task CloneToDeckBuilder()
    {
        if (SelectedDeck == null || IsImporting || OnCloneToDeckBuilder == null) return;

        IsImporting = true;
        StatusText = "Cloning deck…";

        try
        {
            await OnCloneToDeckBuilder(SelectedDeck);
            StatusText = $"Deck \"{SelectedDeck.Name}\" added to Deck Builder.";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private static string BuildTypeLine(DeckCard card)
    {
        var superAndType = string.Join(" ",
            card.Supertypes.Concat(card.Types).Where(s => !string.IsNullOrEmpty(s)));
        var subTypes = string.Join(" ", card.Subtypes.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(subTypes) ? superAndType : $"{superAndType} — {subTypes}";
    }
}
