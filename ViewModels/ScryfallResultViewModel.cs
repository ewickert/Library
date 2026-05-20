using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;

namespace Library.ViewModels;

/// <summary>A single Scryfall search result shown in "search not in collection" mode.</summary>
public partial class ScryfallResultViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;
    public ScryfallCardData Data { get; }

    [ObservableProperty] private bool _isOnShoppingList;
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _isLoading;

    public string Name            => Data.Name;
    public string SetCode         => Data.SetCode;
    public string SetName         => Data.SetName;
    public string CollectorNumber => Data.CollectorNumber;
    public string Rarity          => Data.Rarity;
    public string ManaCost        => Data.ManaCost;
    public string TypeLine        => Data.TypeLine;

    public ScryfallResultViewModel(ScryfallCardData data, DatabaseService db, ScryfallService scryfall)
    {
        Data      = data;
        _db       = db;
        _scryfall = scryfall;
        _isOnShoppingList = db.IsOnShoppingList(data.ScryfallId);
    }

    /// <summary>Kick off lazy image load. Called when the grid view becomes active.</summary>
    public async Task LoadImageAsync(CancellationToken ct = default)
    {
        if (Image != null) return; // already loaded
        IsLoading = true;
        try
        {
            Image = await _scryfall.GetCardImageAsync(Data.ScryfallId, ct);
        }
        catch (OperationCanceledException) { }
        finally { if (!ct.IsCancellationRequested) IsLoading = false; }
    }

    [RelayCommand]
    private void ToggleShoppingList()
    {
        if (IsOnShoppingList)
        {
            // Find by ScryfallId and remove
            var list = _db.GetShoppingList();
            var item = list.FirstOrDefault(s => s.ScryfallId == Data.ScryfallId);
            if (item != null) _db.RemoveFromShoppingList(item.Id);
            IsOnShoppingList = false;
        }
        else
        {
            _db.AddToShoppingList(Data);
            IsOnShoppingList = true;
        }
    }
}
