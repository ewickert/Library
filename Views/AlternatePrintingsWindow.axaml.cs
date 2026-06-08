using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;

namespace Library.Views;

public partial class AlternatePrintingsWindow : Window
{
    private readonly ScryfallService _scryfall;
    private readonly DatabaseService _db;
    private readonly Deck? _contextDeck;
    private CancellationTokenSource? _loadCts;
    private List<AlternatePrintingTile> _tiles = new();
    private string? _nextPageUrl;
    private string  _cardName = string.Empty;
    private int     _totalCards;

    private const double MinZoom  = 0.4;
    private const double MaxZoom  = 2.5;
    private const double ZoomStep = 1.2;
    private const double BaseTileWidth  = 136;
    private const double BaseTileHeight = 220;
    private double _zoom = 1.0;

    // Tracks zoom level at the moment a touch-pinch gesture starts so we can
    // multiply by the cumulative Scale rather than stacking per-event deltas.
    private double _pinchBaseZoom = double.NaN;

    public AlternatePrintingsWindow(
        string cardName,
        string? currentScryfallId,
        ScryfallService scryfall,
        DatabaseService db,
        Deck? contextDeck = null)
    {
        _scryfall    = scryfall;
        _db          = db;
        _contextDeck = contextDeck;

        InitializeComponent();

        _cardName = cardName;
        CardNameHeader.Text = cardName;
        Title = $"Alternate Printings — {cardName}";
        if (contextDeck != null)
            Title += $"  ·  {contextDeck.Name}";

        WireGestures();

        _ = LoadPageAsync(nextPageUrl: null);
    }

    private void WireGestures()
    {
        // Ctrl+Scroll (standard on Windows/Linux; also works on macOS with Ctrl held)
        GalleryScroller.PointerWheelChanged += OnPointerWheel;

        // macOS trackpad pinch-to-zoom (fires when two fingers pinch/spread without Ctrl)
        Gestures.AddPointerTouchPadGestureMagnifyHandler(GalleryScroller, OnTouchPadMagnify);

        // Touch pinch (iOS / Android / touch screens)
        GalleryScroller.GestureRecognizers.Add(new PinchGestureRecognizer());
        Gestures.AddPinchHandler(GalleryScroller, OnPinch);
        Gestures.AddPinchEndedHandler(GalleryScroller, OnPinchEnded);

        // Keyboard: Cmd+=/- on macOS, Ctrl+=/- elsewhere; Cmd/Ctrl+0 resets
        KeyDown += OnKeyDown;
    }

    private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        SetZoom(_zoom * (e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep));
        e.Handled = true;
    }

    private void OnTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        // Delta.Y is the cumulative magnification for this event frame (typically ±0.01–0.05 per tick)
        SetZoom(_zoom * (1.0 + e.Delta.Y));
        e.Handled = true;
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        // Scale is cumulative from gesture start — capture base zoom on first event
        if (double.IsNaN(_pinchBaseZoom))
            _pinchBaseZoom = _zoom;
        SetZoom(_pinchBaseZoom * e.Scale);
        e.Handled = true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _pinchBaseZoom = double.NaN;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var mod = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(mod)) return;
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus:
                SetZoom(_zoom * ZoomStep); e.Handled = true; break;
            case Key.Subtract or Key.OemMinus:
                SetZoom(_zoom / ZoomStep); e.Handled = true; break;
            case Key.D0 or Key.NumPad0:
                SetZoom(1.0); e.Handled = true; break;
        }
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e)  => SetZoom(_zoom * ZoomStep);
    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => SetZoom(_zoom / ZoomStep);

    private void SetZoom(double zoom)
    {
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        ZoomLabel.Text = $"{(int)Math.Round(_zoom * 100)}%";
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        var w = Math.Round(BaseTileWidth  * _zoom);
        var h = Math.Round(BaseTileHeight * _zoom);
        foreach (var tile in _tiles)
        {
            tile.TileWidth  = w;
            tile.TileHeight = h;
        }
    }

    private async Task LoadPageAsync(string? nextPageUrl)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        bool isFirstPage = nextPageUrl == null;

        if (isFirstPage)
        {
            LoadingBar.IsVisible = true;
            PrintingCountLabel.Text = "Loading…";
        }
        else
        {
            LoadMoreBtn.IsEnabled = false;
            LoadMoreBar.IsVisible = true;
        }

        var (printings, nextUrl, total) = await _scryfall.GetAlternatePrintingsPageAsync(
            _cardName, nextPageUrl, ct);

        if (ct.IsCancellationRequested) return;

        LoadingBar.IsVisible = false;
        LoadMoreBar.IsVisible = false;
        LoadMoreBtn.IsEnabled = true;

        if (isFirstPage && printings.Count == 0)
        {
            PrintingCountLabel.Text = "No printings found.";
            return;
        }

        _nextPageUrl = nextUrl;
        if (isFirstPage) _totalCards = total;

        var loaded  = _tiles.Count + printings.Count;
        var ofTotal = _totalCards > loaded ? $" of {_totalCards}" : string.Empty;
        PrintingCountLabel.Text = $"{loaded}{ofTotal} printing{(loaded == 1 ? "" : "s")}";

        Action<string> reportStatus = msg => Dispatcher.UIThread.Post(() =>
        {
            StatusTextBlock.Text = msg;
            StatusBanner.IsVisible = !string.IsNullOrEmpty(msg);
        });

        var newTiles = printings
            .Select(p => new AlternatePrintingTile(p, _scryfall, _db, _contextDeck, reportStatus))
            .ToList();

        _tiles.AddRange(newTiles);
        GalleryPanel.ItemsSource = null;
        GalleryPanel.ItemsSource = _tiles;

        ApplyZoom();

        LoadMorePanel.IsVisible = _nextPageUrl != null;
        if (_nextPageUrl != null)
            LoadMoreBtn.Content = $"Load more  ({_totalCards - _tiles.Count} remaining)";

        foreach (var tile in newTiles)
            _ = tile.LoadImageAsync(ct);
    }

    private void OnLoadMoreClick(object? sender, RoutedEventArgs e) =>
        _ = LoadPageAsync(_nextPageUrl);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _loadCts?.Cancel();
        base.OnClosed(e);
    }
}

/// <summary>A single card tile in the alternate printings gallery.</summary>
public partial class AlternatePrintingTile : ObservableObject
{
    private readonly ScryfallService _scryfall;
    private readonly DatabaseService _db;
    private readonly Deck? _contextDeck;
    private readonly Action<string> _reportStatus;

    public ScryfallCardData Data { get; }

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _isLoadingImage = true;
    [ObservableProperty] private IBrush _borderBrush = Brushes.Transparent;
    [ObservableProperty] private double _tileWidth  = 136;
    [ObservableProperty] private double _tileHeight = 220;

    public string SetName        => Data.SetName;
    public string CollectorLabel => $"#{Data.CollectorNumber}";
    public string RarityLabel    => Data.Rarity.Length > 0
        ? char.ToUpperInvariant(Data.Rarity[0]).ToString()
        : "?";

    public IBrush RarityColor => Data.Rarity.ToLowerInvariant() switch
    {
        "mythic"   => new SolidColorBrush(Color.FromRgb(255, 100, 30)),
        "rare"     => new SolidColorBrush(Color.FromRgb(220, 180, 50)),
        "uncommon" => new SolidColorBrush(Color.FromRgb(180, 200, 210)),
        _          => new SolidColorBrush(Color.FromRgb(160, 160, 160)),
    };

    public bool   HasDeckContext      => _contextDeck != null;
    public string DeckWishlistTooltip => _contextDeck != null
        ? $"Add to {_contextDeck.Name} wishlist"
        : string.Empty;

    public AlternatePrintingTile(ScryfallCardData data, ScryfallService scryfall,
        DatabaseService db, Deck? contextDeck, Action<string> reportStatus)
    {
        Data          = data;
        _scryfall     = scryfall;
        _db           = db;
        _contextDeck  = contextDeck;
        _reportStatus = reportStatus;
    }

    public async Task LoadImageAsync(CancellationToken ct)
    {
        IsLoadingImage = true;
        try
        {
            var bmp = await _scryfall.GetCardImageAsync(Data.ScryfallId, ct);
            if (!ct.IsCancellationRequested)
                Image = bmp;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoadingImage = false;
        }
    }

    [RelayCommand]
    private void AddToShoppingList()
    {
        _db.AddToShoppingList(Data);
        BorderBrush = new SolidColorBrush(Color.FromRgb(45, 110, 80));
        _reportStatus($"Added {Data.Name} ({Data.SetCode.ToUpperInvariant()}) to shopping list");
    }

    [RelayCommand]
    private void AddToDeckWishlist()
    {
        if (_contextDeck == null) return;
        var shoppingId = _db.AddToShoppingList(Data);
        _db.TagShoppingItemToDeck(shoppingId, _contextDeck.Id);
        BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 160));
        _reportStatus($"Added {Data.Name} to {_contextDeck.Name} wishlist");
    }
}
