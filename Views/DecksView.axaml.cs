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

    public DecksView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        DataContextChanged += (_, _) => ApplyResponsiveLayout(Bounds.Width);
        ApplyResponsiveLayout(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        if (DataContext is not DecksViewModel vm)
            return;

        var shouldCollapseCollectionPane = width < 1180;

        if (shouldCollapseCollectionPane)
        {
            if (vm.IsCollectionPaneOpen)
            {
                vm.IsCollectionPaneOpen = false;
                _autoCollapsedCollectionPane = true;
            }
        }
        else if (_autoCollapsedCollectionPane && !vm.IsCollectionPaneOpen)
        {
            vm.IsCollectionPaneOpen = true;
            _autoCollapsedCollectionPane = false;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Zoom with Ctrl+Wheel on the two deck grids
        DeckGridScrollViewer.AddHandler(PointerWheelChangedEvent, OnDeckGridWheel, RoutingStrategies.Tunnel);
        CollectionGridScrollViewer.AddHandler(PointerWheelChangedEvent, OnCollectionGridWheel, RoutingStrategies.Tunnel);

        // Drag sources — tunnel so we get the press before inner controls (buttons etc.)
        CollectionListPane.AddHandler(PointerPressedEvent, OnCollectionPointerPressed, RoutingStrategies.Tunnel);
        CollectionGridPane.AddHandler(PointerPressedEvent, OnCollectionPointerPressed, RoutingStrategies.Tunnel);
        ExternalResultsListView.AddHandler(PointerPressedEvent, OnExternalPointerPressed, RoutingStrategies.Tunnel);
        ExternalResultsGridView.AddHandler(PointerPressedEvent, OnExternalPointerPressed, RoutingStrategies.Tunnel);
        DeckPane.AddHandler(PointerPressedEvent, OnDeckPointerPressed, RoutingStrategies.Tunnel);

        // Track at root level so we keep events even when the pointer leaves the collection pane
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

    private void OnCollectionGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (DataContext is DecksViewModel vm)
        {
            vm.CollectionGridZoom = Math.Clamp(vm.CollectionGridZoom + (e.Delta.Y > 0 ? 0.1 : -0.1), 0.4, 3.0);
            e.Handled = true;
        }
    }

    private async void OnSearchHelpClick(object? sender, RoutedEventArgs e)
    {
        var win = new SearchHelpWindow();
        await win.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
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
        if (!_isDragging) { ResetDragState(); return; }

        var pos = e.GetPosition(this);
        var card = _dragCard;
        var scryfall = _dragScryfall;
        ResetDragState();
        e.Pointer.Capture(null);

        if (DataContext is DecksViewModel vm && _dragSource != DragSourceKind.None)
        {
            if (_dragSource is DragSourceKind.Collection or DragSourceKind.External)
            {
                if (IsOverControl(DeckPane, pos))
                {
                    if (card != null)
                        vm.AddCardToDeckCommand.Execute(card);
                    else if (scryfall != null)
                        vm.AddScryfallCardToDeckCommand.Execute(scryfall);
                }
            }
            else if (_dragSource == DragSourceKind.Deck)
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


