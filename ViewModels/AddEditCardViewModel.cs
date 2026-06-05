using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using System.Collections.ObjectModel;

namespace Library.ViewModels;

public partial class AddEditCardViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly ScryfallService _scryfall;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _setCode = string.Empty;
    [ObservableProperty] private string _setName = string.Empty;
    [ObservableProperty] private string _collectorNumber = string.Empty;
    [ObservableProperty] private bool _foil;
    [ObservableProperty] private string _rarity = string.Empty;
    [ObservableProperty] private int _quantity = 1;
    [ObservableProperty] private long? _manaBoxId;
    [ObservableProperty] private string _scryfallId = string.Empty;
    [ObservableProperty] private decimal? _purchasePrice;
    [ObservableProperty] private bool _misprint;
    [ObservableProperty] private bool _altered;
    [ObservableProperty] private string _condition = "NM";
    [ObservableProperty] private string _language = "en";
    [ObservableProperty] private string _purchasePriceCurrency = "USD";
    [ObservableProperty] private DateTime _added = DateTime.UtcNow;
    [ObservableProperty] private string _colorIdentity = string.Empty;
    [ObservableProperty] private string _manaCost = string.Empty;
    [ObservableProperty] private string _typeLine = string.Empty;

    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private ObservableCollection<ScryfallCardData> _scryfallResults = new();
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _previewImage;
    [ObservableProperty] private bool _isLoadingPreview;

    public static string[] Conditions => ["M", "NM", "LP", "MP", "HP", "DMG"];
    public static string[] Languages => ["en", "de", "fr", "it", "es", "pt", "ja", "ko", "ru", "zhs", "zht", "he", "la", "grc", "ar", "sa", "ph"];
    public static string[] Currencies => ["USD", "EUR", "GBP", "JPY"];

    public int? EditingCardId { get; private set; }
    public bool IsEditing => EditingCardId.HasValue;
    public string Title => IsEditing ? "Edit Card" : "Add Card";

    public event Action? Saved;
    public event Action? Cancelled;

    private CancellationTokenSource? _searchCts;

    public AddEditCardViewModel(DatabaseService db, ScryfallService scryfall, Card? cardToEdit = null)
    {
        _db = db;
        _scryfall = scryfall;
        if (cardToEdit != null)
        {
            EditingCardId = cardToEdit.Id;
            Name = cardToEdit.Name;
            SetCode = cardToEdit.SetCode;
            SetName = cardToEdit.SetName;
            CollectorNumber = cardToEdit.CollectorNumber;
            Foil = cardToEdit.Foil;
            Rarity = cardToEdit.Rarity;
            Quantity = cardToEdit.Quantity;
            ManaBoxId = cardToEdit.ManaBoxId;
            ScryfallId = cardToEdit.ScryfallId ?? string.Empty;
            PurchasePrice = cardToEdit.PurchasePrice;
            Misprint = cardToEdit.Misprint;
            Altered = cardToEdit.Altered;
            Condition = cardToEdit.Condition;
            Language = cardToEdit.Language;
            PurchasePriceCurrency = cardToEdit.PurchasePriceCurrency ?? "USD";
            Added = cardToEdit.Added;
            ColorIdentity = cardToEdit.ColorIdentity ?? string.Empty;
            ManaCost = cardToEdit.ManaCost ?? string.Empty;
            TypeLine = cardToEdit.TypeLine ?? string.Empty;
        }
    }

    [RelayCommand]
    private async Task SearchScryfall()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        IsSearching = true;
        try
        {
            var page = await _scryfall.SearchCardsAsync(SearchQuery, _searchCts.Token);
            ScryfallResults = new ObservableCollection<ScryfallCardData>(page.Cards);
        }
        catch (OperationCanceledException) { }
        finally { IsSearching = false; }
    }

    [RelayCommand]
    private async Task SelectScryfallCard(ScryfallCardData card)
    {
        Name = card.Name;
        SetCode = card.SetCode;
        SetName = card.SetName;
        CollectorNumber = card.CollectorNumber;
        Rarity = card.Rarity;
        ScryfallId = card.ScryfallId;
        ColorIdentity = card.ColorIdentity;
        ManaCost = card.ManaCost;
        TypeLine = card.TypeLine;
        ScryfallResults.Clear();
        SearchQuery = string.Empty;

        IsLoadingPreview = true;
        try
        {
            PreviewImage = await _scryfall.GetCardImageAsync(card.ScryfallId);
        }
        finally { IsLoadingPreview = false; }
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;
        var card = new Card
        {
            Id = EditingCardId ?? 0,
            Name = Name,
            SetCode = SetCode,
            SetName = SetName,
            CollectorNumber = CollectorNumber,
            Foil = Foil,
            Rarity = Rarity,
            Quantity = Quantity,
            ManaBoxId = ManaBoxId,
            ScryfallId = string.IsNullOrWhiteSpace(ScryfallId) ? null : ScryfallId,
            PurchasePrice = PurchasePrice,
            Misprint = Misprint,
            Altered = Altered,
            Condition = Condition,
            Language = Language,
            PurchasePriceCurrency = string.IsNullOrWhiteSpace(PurchasePriceCurrency) ? null : PurchasePriceCurrency,
            Added = Added,
            ColorIdentity = string.IsNullOrWhiteSpace(ColorIdentity) ? null : ColorIdentity,
            ManaCost = string.IsNullOrWhiteSpace(ManaCost) ? null : ManaCost,
            TypeLine = string.IsNullOrWhiteSpace(TypeLine) ? null : TypeLine
        };
        if (IsEditing) _db.UpdateCard(card);
        else _db.AddCard(card);
        Saved?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Cancelled?.Invoke();
}
