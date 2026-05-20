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
    // Drag state
    private Card? _pendingDragCard;
    private Bitmap? _pendingBitmap;
    private Point? _dragStart;
    private bool _isDragging;
    private Card? _dragCard;
    private Bitmap? _dragBitmap;
    private const double DragThreshold = 6;

    public DecksView()
    {
        InitializeComponent();
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
        _pendingBitmap = bmp;
        _dragStart = e.GetPosition(this);
    }

    // ── Drag move ─────────────────────────────────────────────────────────────

    private void OnRootPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragCard == null && !_isDragging) return;

        var pos = e.GetPosition(this);

        if (!_isDragging && _dragStart.HasValue)
        {
            var delta = pos - _dragStart.Value;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

            // Threshold exceeded — commit to drag
            _isDragging = true;
            _dragCard = _pendingDragCard;
            _dragBitmap = _pendingBitmap;
            _pendingDragCard = null;
            _pendingBitmap = null;
            _dragStart = null;

            e.Pointer.Capture(this); // keep events flowing even outside the window

            DragPreviewImage.Source = _dragBitmap;
            DragPreviewName.Text = _dragCard?.Name ?? string.Empty;
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
        ResetDragState();
        e.Pointer.Capture(null);

        if (card != null && IsOverControl(DeckPane, pos) && DataContext is DecksViewModel vm)
            vm.AddCardToDeckCommand.Execute(card);
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetDragState();

    private void ResetDragState()
    {
        _isDragging = false;
        _dragCard = null;
        _dragBitmap = null;
        _pendingDragCard = null;
        _pendingBitmap = null;
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
}


