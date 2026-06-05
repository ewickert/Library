using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Services;
using MtgJson;
using System.Collections.ObjectModel;

namespace Library.ViewModels;

public partial class BrowsePaneViewModel : ObservableObject
{
    private readonly ScryfallService _scryfall;
    private readonly DatabaseService _db;

    public MtgJsonPaneViewModel Decks { get; }

    // ── Tab ──────────────────────────────────────────────────────────────────
    [ObservableProperty] private int _selectedTab; // 0 = Scryfall, 1 = Decks
    public bool IsScryfall => SelectedTab == 0;
    public bool IsDecks    => SelectedTab == 1;
    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsScryfall));
        OnPropertyChanged(nameof(IsDecks));
    }

    // ── Scryfall search ───────────────────────────────────────────────────────
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private bool _isGridView;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private ObservableCollection<ScryfallResultViewModel> _results = new();

    private string? _nextPageUrl;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _gridImageCts;
    private CancellationTokenSource? _debounceCts;

    // ── Callbacks wired by CollectionViewModel ────────────────────────────────
    public Action? OnCollectionChanged { get; set; }
    public Func<MtgJson.Models.Deck, Task>? OnCloneToDeckBuilder { get; set; }
    public Action? RequestClose { get; set; }

    public BrowsePaneViewModel(ScryfallService scryfall, MtgJsonService mtgJson, DatabaseService db)
    {
        _scryfall = scryfall;
        _db = db;

        Decks = new MtgJsonPaneViewModel(mtgJson, db);
        Decks.OnCollectionImported = () => OnCollectionChanged?.Invoke();
        Decks.OnCloneToDeckBuilder = deck => OnCloneToDeckBuilder?.Invoke(deck) ?? Task.CompletedTask;
    }

    // ── Commands ──────────────────────────────────────────────────────────────
    [RelayCommand] private void SelectScryfall() => SelectedTab = 0;
    [RelayCommand] private void SelectDecks()    => SelectedTab = 1;

    [RelayCommand]
    private async Task Search() => await RunSearchAsync(SearchText);

    [RelayCommand]
    private void ToggleView()
    {
        IsGridView = !IsGridView;
        if (IsGridView) _ = LoadGridImagesAsync();
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (_nextPageUrl == null) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;
        IsSearching = true;
        try
        {
            var page = await _scryfall.SearchCardsNextPageAsync(_nextPageUrl, cts.Token);
            if (cts.IsCancellationRequested) return;

            _nextPageUrl = page.NextPageUrl;
            HasMore = page.HasMore;

            foreach (var r in page.Cards)
                Results.Add(MakeScryfallVm(r));

            if (IsGridView) _ = LoadGridImagesAsync();
        }
        catch (OperationCanceledException) { }
        finally { if (!cts.IsCancellationRequested) IsSearching = false; }
    }

    // ── Internals ─────────────────────────────────────────────────────────────
    partial void OnSearchTextChanged(string value)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var cts = _debounceCts;
        _ = Task.Delay(400, cts.Token).ContinueWith(
            _ => RunSearchAsync(value),
            cts.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.FromCurrentSynchronizationContext());
    }

    partial void OnIsGridViewChanged(bool value)
    {
        if (value) _ = LoadGridImagesAsync();
    }

    private async Task RunSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Results.Clear();
            HasMore = false;
            _nextPageUrl = null;
            return;
        }

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;
        IsSearching = true;
        try
        {
            var page = await _scryfall.SearchCardsAsync(query, cts.Token);
            if (cts.IsCancellationRequested) return;

            _nextPageUrl = page.NextPageUrl;
            HasMore = page.HasMore;
            Results = new ObservableCollection<ScryfallResultViewModel>(
                page.Cards.Select(MakeScryfallVm));

            if (IsGridView) _ = LoadGridImagesAsync();
        }
        catch (OperationCanceledException) { }
        finally { if (!cts.IsCancellationRequested) IsSearching = false; }
    }

    private ScryfallResultViewModel MakeScryfallVm(ScryfallCardData r)
    {
        var vm = new ScryfallResultViewModel(r, _db, _scryfall);
        vm.AddedToCollection = () => OnCollectionChanged?.Invoke();
        return vm;
    }

    private async Task LoadGridImagesAsync()
    {
        _gridImageCts?.Cancel();
        _gridImageCts = new CancellationTokenSource();
        var cts = _gridImageCts;
        foreach (var vm in Results.ToList())
        {
            if (cts.IsCancellationRequested) break;
            await vm.LoadImageAsync(cts.Token);
        }
    }
}
