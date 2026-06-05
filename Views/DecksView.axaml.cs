using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Library.Models;
using Library.ViewModels;

namespace Library.Views;

public partial class DecksView : UserControl
{
    private enum DragSourceKind { None, Collection, External, Deck }
    private const double CompactPhoneBreakpoint = 900;

    // Drag state
    private Card? _pendingDragCard;
    private ScryfallResultViewModel? _pendingDragScryfall;
    private Bitmap? _pendingBitmap;
    private DragSourceKind _pendingDragSource;
    private Point? _dragStart;
    private bool _isDragging;
    private Card? _dragCard;
    private ScryfallResultViewModel? _dragScryfall;
    private Bitmap? _dragBitmap;
    private DragSourceKind _dragSource;
    private const double DragThreshold = 6;
    private bool _autoCollapsedCollectionPane;
    private bool _isCompactPhoneLayout;

    public DecksView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        ApplyResponsiveLayout(Bounds.Width);
    }

    private DecksViewModel? _subscribedVm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;

        _subscribedVm = DataContext as DecksViewModel;

        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged += OnVmPropertyChanged;
            UpdatePaneColumns(_subscribedVm.IsDeckSidebarOpen, _subscribedVm.IsCollectionPaneOpen, _subscribedVm.IsScryfallPaneOpen, _subscribedVm.IsStatsPaneOpen, _subscribedVm.IsHistoryPaneOpen);
        }
        else
        {
            UpdatePaneColumns(true, false, false, false, false);
        }

        ApplyResponsiveLayout(Bounds.Width);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not DecksViewModel vm)
            return;

        if (e.PropertyName == nameof(DecksViewModel.IsCollectionPaneOpen) ||
            e.PropertyName == nameof(DecksViewModel.IsStatsPaneOpen) ||
            e.PropertyName == nameof(DecksViewModel.IsDeckSidebarOpen) ||
            e.PropertyName == nameof(DecksViewModel.IsScryfallPaneOpen) ||
            e.PropertyName == nameof(DecksViewModel.IsHistoryPaneOpen))
        {
            UpdatePaneColumns(vm.IsDeckSidebarOpen, vm.IsCollectionPaneOpen, vm.IsScryfallPaneOpen, vm.IsStatsPaneOpen, vm.IsHistoryPaneOpen);
        }
    }

    private void UpdatePaneColumns(bool sidebarOpen, bool collectionOpen, bool scryfallOpen, bool statsOpen, bool historyOpen)
    {
        // Col indices: 0=sidebar, 1=content, 2=splitter, 3=collection, 4=splitter, 5=scryfall, 6=splitter, 7=stats, 8=splitter, 9=history
        var deckListColumn         = RootGrid.ColumnDefinitions[0];
        var deckContentColumn      = RootGrid.ColumnDefinitions[1];
        var deckCollectionSplitter = RootGrid.ColumnDefinitions[2];
        var collectionColumn       = RootGrid.ColumnDefinitions[3];
        var collectionScryfallSplitter = RootGrid.ColumnDefinitions[4];
        var scryfallColumn         = RootGrid.ColumnDefinitions[5];
        var scryfallStatsSplitter  = RootGrid.ColumnDefinitions[6];
        var statsColumn            = RootGrid.ColumnDefinitions[7];
        var statsHistorySplitter   = RootGrid.ColumnDefinitions[8];
        var historyColumn          = RootGrid.ColumnDefinitions[9];

        if (!sidebarOpen)
        {
            deckListColumn.Width = new GridLength(0);
            deckListColumn.MinWidth = 0;
            deckListColumn.MaxWidth = double.PositiveInfinity;
            deckContentColumn.MinWidth = _isCompactPhoneLayout ? 150 : 200;
        }
        else if (_isCompactPhoneLayout)
        {
            if (collectionOpen || scryfallOpen || statsOpen)
            {
                deckListColumn.Width = new GridLength(0);
                deckListColumn.MinWidth = 0;
                deckListColumn.MaxWidth = double.PositiveInfinity;
            }
            else
            {
                deckListColumn.Width = new GridLength(0.34, GridUnitType.Star);
                deckListColumn.MinWidth = 120;
                deckListColumn.MaxWidth = 210;
            }
            deckContentColumn.MinWidth = 150;
        }
        else
        {
            deckListColumn.Width = new GridLength(0.24, GridUnitType.Star);
            deckListColumn.MinWidth = 140;
            deckListColumn.MaxWidth = 260;
            deckContentColumn.MinWidth = 200;
        }

        SetPaneColumn(collectionColumn, deckCollectionSplitter,    collectionOpen, 0.34, 180, 340);
        SetPaneColumn(scryfallColumn,   collectionScryfallSplitter, scryfallOpen,  0.34, 180, 340);
        SetPaneColumn(statsColumn,      scryfallStatsSplitter,      statsOpen,     0.28, 220, 360);
        SetPaneColumn(historyColumn,    statsHistorySplitter,       historyOpen,   0.28, 220, 380);
    }

    private static void SetPaneColumn(ColumnDefinition col, ColumnDefinition splitter, bool open,
        double star, double minWidth, double maxWidth)
    {
        if (open)
        {
            col.Width = new GridLength(star, GridUnitType.Star);
            col.MinWidth = minWidth;
            col.MaxWidth = maxWidth;
            splitter.Width = new GridLength(5);
        }
        else
        {
            col.Width = new GridLength(0);
            col.MinWidth = 0;
            col.MaxWidth = double.PositiveInfinity;
            splitter.Width = new GridLength(0);
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        if (DataContext is not DecksViewModel vm)
            return;

        _isCompactPhoneLayout = width < CompactPhoneBreakpoint;

        var shouldCollapseCollectionPane = width < 1180;

        if (shouldCollapseCollectionPane)
        {
            if (vm.IsCollectionPaneOpen && !_autoCollapsedCollectionPane)
            {
                vm.IsCollectionPaneOpen = false;
                _autoCollapsedCollectionPane = true;
            }
        }
        else
        {
            if (_autoCollapsedCollectionPane && !vm.IsCollectionPaneOpen)
                vm.IsCollectionPaneOpen = true;

            _autoCollapsedCollectionPane = false;
        }

        UpdatePaneColumns(vm.IsDeckSidebarOpen, vm.IsCollectionPaneOpen, vm.IsScryfallPaneOpen, vm.IsStatsPaneOpen, vm.IsHistoryPaneOpen);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Zoom with Ctrl+Wheel on the deck grid
        DeckGridScrollViewer.AddHandler(PointerWheelChangedEvent, OnDeckGridWheel, RoutingStrategies.Tunnel);
        DeckListDataGrid.SelectionChanged += OnDeckListSelectionChanged;

        // Drag sources — tunnel so we get the press before inner controls (buttons etc.)
        CollectionPane.ListPane.AddHandler(PointerPressedEvent, OnCollectionPointerPressed, RoutingStrategies.Tunnel);
        CollectionPane.GridPane.AddHandler(PointerPressedEvent, OnCollectionPointerPressed, RoutingStrategies.Tunnel);
        ScryfallPane.ListPane.AddHandler(PointerPressedEvent, OnExternalPointerPressed, RoutingStrategies.Tunnel);
        ScryfallPane.GridPane.AddHandler(PointerPressedEvent, OnExternalPointerPressed, RoutingStrategies.Tunnel);
        DeckPane.AddHandler(PointerPressedEvent, OnDeckPointerPressed, RoutingStrategies.Tunnel);
        DeckPane.AddHandler(PointerMovedEvent, OnDeckPanePointerMoved, RoutingStrategies.Bubble);

        // Track at root level so we keep events even when the pointer leaves the panes
        this.AddHandler(PointerMovedEvent, OnRootPointerMoved, RoutingStrategies.Tunnel);
        this.AddHandler(PointerReleasedEvent, OnRootPointerReleased, RoutingStrategies.Tunnel);
        this.AddHandler(PointerCaptureLostEvent, OnCaptureLost);
    }

    private void OnDeckGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (DataContext is DecksViewModel vm)
        {
            vm.GridZoom = Math.Clamp(vm.GridZoom + (e.Delta.Y > 0 ? 0.1 : -0.1), 0.4, 3.0);
            e.Handled = true;
        }
    }

    // ── Drag start ────────────────────────────────────────────────────────────

    private void OnCollectionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _pendingDragCard = FindCardFromSource(e.Source as Visual, out var bmp);
        _pendingDragScryfall = null;
        _pendingBitmap = bmp;
        _pendingDragSource = DragSourceKind.Collection;
        _dragStart = e.GetPosition(this);
    }

    private void OnExternalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var scryfall = FindScryfallFromSource(e.Source as Visual);
        if (scryfall == null) return;
        _pendingDragScryfall = scryfall;
        _pendingDragCard = null;
        _pendingBitmap = scryfall.Image;
        _pendingDragSource = DragSourceKind.External;
        _dragStart = e.GetPosition(this);
    }

    private void OnDeckPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var deckCard = FindDeckCardFromSource(e.Source as Visual);
        if (deckCard?.Card == null) return;
        _pendingDragCard = deckCard.Card;
        _pendingDragScryfall = null;
        _pendingBitmap = FindDeckBitmapFromSource(e.Source as Visual);
        _pendingDragSource = DragSourceKind.Deck;
        _dragStart = e.GetPosition(this);
    }

    // ── Drag move ─────────────────────────────────────────────────────────────

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragCard == null && _pendingDragScryfall == null && !_isDragging) return;

        var pos = e.GetPosition(this);

        if (!_isDragging && _dragStart.HasValue)
        {
            var delta = pos - _dragStart.Value;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

            // Threshold exceeded — commit to drag
            _isDragging = true;
            _dragCard = _pendingDragCard;
            _dragScryfall = _pendingDragScryfall;
            _dragBitmap = _pendingBitmap;
            _dragSource = _pendingDragSource;
            _pendingDragCard = null;
            _pendingDragScryfall = null;
            _pendingBitmap = null;
            _pendingDragSource = DragSourceKind.None;
            _dragStart = null;

            e.Pointer.Capture(this); // keep events flowing even outside the window

            DragPreviewImage.Source = _dragBitmap;
            DragPreviewName.Text = _dragCard?.Name ?? _dragScryfall?.Name ?? string.Empty;
            DragOverlay.IsVisible = true;
        }

        if (_isDragging)
        {
            Canvas.SetLeft(DragPreview, pos.X + 12);
            Canvas.SetTop(DragPreview, pos.Y + 8);
            e.Handled = true;
        }
    }

    // ── Drag end ──────────────────────────────────────────────────────────────

    private void OnRootPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
        {
            if (_pendingDragSource == DragSourceKind.Deck && _pendingDragCard != null &&
                DataContext is DecksViewModel clickVm)
            {
                clickVm.OpenCardDetailCommand.Execute(_pendingDragCard);
            }
            ResetDragState();
            return;
        }

        var pos = e.GetPosition(this);
        var card = _dragCard;
        var scryfall = _dragScryfall;
        var dragSource = _dragSource;
        ResetDragState();
        e.Pointer.Capture(null);

        if (DataContext is DecksViewModel vm && dragSource != DragSourceKind.None)
        {
            if (dragSource is DragSourceKind.Collection or DragSourceKind.External)
            {
                if (IsOverControl(DeckPane, pos))
                {
                    if (card != null)
                        vm.AddCardToDeckCommand.Execute(card);
                    else if (scryfall != null)
                        vm.AddScryfallCardToDeckCommand.Execute(scryfall);
                }
            }
            else if (dragSource == DragSourceKind.Deck)
            {
                if (card != null && IsOverControl(CollectionPaneRoot, pos))
                    vm.RemoveCardByCardFromDeckCommand.Execute(card);
            }
        }
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetDragState();

    private void ResetDragState()
    {
        _isDragging = false;
        _dragCard = null;
        _dragScryfall = null;
        _dragBitmap = null;
        _dragSource = DragSourceKind.None;
        _pendingDragCard = null;
        _pendingDragScryfall = null;
        _pendingBitmap = null;
        _pendingDragSource = DragSourceKind.None;
        _dragStart = null;
        DragOverlay.IsVisible = false;
    }

    private void OnDeckListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not DecksViewModel vm) return;
        if (DeckListDataGrid.SelectedItem is DeckCard deckCard && deckCard.Card != null)
            vm.OpenCardDetailCommand.Execute(deckCard.Card);
    }

    private void OnDeckPanePointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not DecksViewModel vm || !vm.IsListViewMode || _isDragging) return;
        var deckCard = FindDeckCardFromSource(e.Source as Visual);
        if (deckCard?.Card == null || vm.DetailSlot?.Card.Id == deckCard.Card.Id) return;
        vm.OpenCardDetailCommand.Execute(deckCard.Card);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool IsOverControl(Control control, Point ptInThis)
    {
        var tl = control.TranslatePoint(new Point(0, 0), this);
        var br = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), this);
        if (tl is null || br is null) return false;
        return new Rect(tl.Value, br.Value).Contains(ptInThis);
    }

    private static ScryfallResultViewModel? FindScryfallFromSource(Visual? source)
    {
        var element = source as StyledElement;
        while (element != null)
        {
            if (element.DataContext is ScryfallResultViewModel vm) return vm;
            if (element is ItemsControl) break;
            element = (element as Visual)?.GetVisualParent() as StyledElement;
        }
        return null;
    }

    private static Card? FindCardFromSource(Visual? source, out Bitmap? bitmap)
    {
        bitmap = null;
        var element = source as StyledElement;
        while (element != null)
        {
            if (element.DataContext is Card c) return c;
            if (element.DataContext is CardSlotViewModel s) { bitmap = s.Image; return s.Card; }
            if (element is ItemsControl) break;
            element = (element as Visual)?.GetVisualParent() as StyledElement;
        }
        return null;
    }

    private static DeckCard? FindDeckCardFromSource(Visual? source)
    {
        var element = source as StyledElement;
        while (element != null)
        {
            if (element.DataContext is DeckCard dc) return dc;
            if (element.DataContext is DeckCardSortedItem sorted) return sorted.DeckCard;
            if (element.DataContext is CardSlotViewModel slot)
                return new DeckCard { Card = slot.Card };
            if (element is ItemsControl) break;
            element = (element as Visual)?.GetVisualParent() as StyledElement;
        }
        return null;
    }

    private static Bitmap? FindDeckBitmapFromSource(Visual? source)
    {
        var element = source as StyledElement;
        while (element != null)
        {
            if (element.DataContext is CardSlotViewModel slot) return slot.Image;
            if (element.DataContext is DeckCardSortedItem sorted) return sorted.Slot?.Image;
            if (element is ItemsControl) break;
            element = (element as Visual)?.GetVisualParent() as StyledElement;
        }
        return null;
    }
}


