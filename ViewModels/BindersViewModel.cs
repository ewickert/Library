using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using System.Collections.ObjectModel;

namespace Library.ViewModels;

public partial class BindersViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;
    private CancellationTokenSource? _imageCts;

    [ObservableProperty] private ObservableCollection<Binder> _binders = new();
    [ObservableProperty] private Binder? _selectedBinder;
    [ObservableProperty] private ObservableCollection<BinderCard> _binderCards = new();
    [ObservableProperty] private ObservableCollection<BinderCardSlotViewModel> _binderSlots = new();

    [ObservableProperty] private string _newBinderName = string.Empty;
    [ObservableProperty] private string _newBinderDescription = string.Empty;
    [ObservableProperty] private bool _isCreatingBinder;
    [ObservableProperty] private bool _isGridView = true;
    [ObservableProperty] private double _gridZoom = 1.0;
    public double TileWidth  => Math.Round(160 * _gridZoom);
    public double TileHeight => Math.Round(248 * _gridZoom);
    partial void OnGridZoomChanged(double v) { OnPropertyChanged(nameof(TileWidth)); OnPropertyChanged(nameof(TileHeight)); }
    [ObservableProperty] private string _searchText = string.Empty;

    private List<BinderCardSlotViewModel> _allBinderSlots = new();
    private CancellationTokenSource? _searchDebounceCts;

    public BindersViewModel(DatabaseService db, ScryfallService scryfall)
    {
        _db = db;
        _scryfall = scryfall;
        LoadBinders();
    }

    public void LoadBinders()
    {
        Binders = new ObservableCollection<Binder>(_db.GetAllBinders());
    }

    [RelayCommand]
    private void ToggleView() => IsGridView = !IsGridView;

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
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            BinderSlots = new ObservableCollection<BinderCardSlotViewModel>(_allBinderSlots);
            return;
        }
        var filtered = _allBinderSlots
            .Where(s => s.Card != null && LocalCardFilter.Matches(s.Card, SearchText))
            .ToList();
        BinderSlots = new ObservableCollection<BinderCardSlotViewModel>(filtered);
    }

    partial void OnSelectedBinderChanged(Binder? value)
    {
        // Cancel any in-progress image loading
        _imageCts?.Cancel();
        _imageCts = new CancellationTokenSource();

        BinderCards.Clear();
        BinderSlots.Clear();

        if (value == null) return;

        var full = _db.GetBinderWithCards(value.Id);
        if (full == null) return;

        BinderCards = new ObservableCollection<BinderCard>(full.Cards);

        var slots = full.Cards
            .Where(bc => bc.Card != null)
            .Select(bc => new BinderCardSlotViewModel(bc, _scryfall))
            .ToList();
        _allBinderSlots = slots;
        BinderSlots = new ObservableCollection<BinderCardSlotViewModel>(slots);

        _ = LoadSlotImagesAsync(slots, _imageCts.Token);
    }

    private static async Task LoadSlotImagesAsync(List<BinderCardSlotViewModel> slots, CancellationToken ct)
    {
        foreach (var slot in slots)
        {
            if (ct.IsCancellationRequested) break;
            await slot.LoadImageAsync(ct);
        }
    }

    [RelayCommand]
    private void StartCreateBinder()
    {
        NewBinderName = string.Empty;
        NewBinderDescription = string.Empty;
        IsCreatingBinder = true;
    }

    [RelayCommand]
    private void ConfirmCreateBinder()
    {
        if (string.IsNullOrWhiteSpace(NewBinderName)) return;
        var binder = new Binder
        {
            Name = NewBinderName,
            Description = string.IsNullOrWhiteSpace(NewBinderDescription) ? null : NewBinderDescription
        };
        _db.AddBinder(binder);
        Binders.Add(binder);
        IsCreatingBinder = false;
        SelectedBinder = binder;
    }

    [RelayCommand]
    private void CancelCreateBinder() => IsCreatingBinder = false;

    [RelayCommand]
    private void DeleteBinder()
    {
        if (SelectedBinder == null) return;
        _db.DeleteBinder(SelectedBinder.Id);
        Binders.Remove(SelectedBinder);
        SelectedBinder = null;
    }

    [RelayCommand]
    private void RemoveCardFromBinder(BinderCard? binderCard)
    {
        if (binderCard == null) return;
        _db.RemoveCardFromBinder(binderCard.Id);
        BinderCards.Remove(binderCard);
    }
}
