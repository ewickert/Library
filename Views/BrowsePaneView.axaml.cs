using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Library.ViewModels;
using MtgJson.Models;

namespace Library.Views;

public partial class BrowsePaneView : UserControl
{
    // ── Drag state ────────────────────────────────────────────────────────────
    private Point? _pressPoint;
    private object? _dragPayload; // ScryfallResultViewModel or DeckCard
    private const double DragThreshold = 6.0;

    // ── Pop-out window ────────────────────────────────────────────────────────
    private BrowsePaneWindow? _floatingWindow;

    public static readonly StyledProperty<bool> IsFloatingProperty =
        AvaloniaProperty.Register<BrowsePaneView, bool>(nameof(IsFloating));

    public bool IsFloating
    {
        get => GetValue(IsFloatingProperty);
        set
        {
            SetValue(IsFloatingProperty, value);
            if (PopOutButton != null)
                PopOutButton.IsVisible = !value;
        }
    }

    public BrowsePaneView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (PopOutButton != null)
            PopOutButton.IsVisible = !IsFloating;

        // Attach drag initiators to both Scryfall result containers
        ScryfallListItems.AddHandler(PointerPressedEvent,  OnItemPointerPressed,  RoutingStrategies.Bubble);
        ScryfallListItems.AddHandler(PointerMovedEvent,    OnItemPointerMoved,    RoutingStrategies.Bubble);
        ScryfallListItems.AddHandler(PointerReleasedEvent, OnItemPointerReleased, RoutingStrategies.Bubble);

        ScryfallGridItems.AddHandler(PointerPressedEvent,  OnItemPointerPressed,  RoutingStrategies.Bubble);
        ScryfallGridItems.AddHandler(PointerMovedEvent,    OnItemPointerMoved,    RoutingStrategies.Bubble);
        ScryfallGridItems.AddHandler(PointerReleasedEvent, OnItemPointerReleased, RoutingStrategies.Bubble);
    }

    // ── Drag initiation ───────────────────────────────────────────────────────
    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Walk up from the event source to find a DataContext that is a result VM or deck card
        if (e.Source is not Control source) return;
        var payload = FindDragPayload(source);
        if (payload == null) return;

        _pressPoint  = e.GetPosition(this);
        _dragPayload = payload;
    }

    private async void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressPoint == null || _dragPayload == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressPoint = null; _dragPayload = null; return;
        }

        var pos = e.GetPosition(this);
        var dx  = pos.X - _pressPoint.Value.X;
        var dy  = pos.Y - _pressPoint.Value.Y;
        if (dx * dx + dy * dy < DragThreshold * DragThreshold) return;

        var payload    = _dragPayload;
        _pressPoint    = null;
        _dragPayload   = null;

        var data = new DataObject();
        switch (payload)
        {
            case ScryfallResultViewModel srvm:
                data.Set(BrowseDragFormats.ScryfallResult, srvm);
                break;
            case DeckCard dc:
                data.Set(BrowseDragFormats.MtgJsonCard, dc);
                break;
            default:
                return;
        }

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
    }

    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _pressPoint  = null;
        _dragPayload = null;
    }

    private static object? FindDragPayload(Control source)
    {
        Control? current = source;
        while (current != null)
        {
            if (current.DataContext is ScryfallResultViewModel srvm) return srvm;
            if (current.DataContext is DeckCard dc)                  return dc;
            current = current.GetVisualParent() as Control;
        }
        return null;
    }

    // ── Pop-out ───────────────────────────────────────────────────────────────
    private void OnPopOutClick(object? sender, RoutedEventArgs e)
    {
        if (_floatingWindow != null)
        {
            _floatingWindow.Activate();
            return;
        }

        if (DataContext is not BrowsePaneViewModel vm) return;

        vm.RequestClose?.Invoke();

        _floatingWindow = new BrowsePaneWindow();
        _floatingWindow.DataContext = vm;
        _floatingWindow.Closed += OnFloatingClosed;
        _floatingWindow.Show(TopLevel.GetTopLevel(this) as Window);
    }

    private void OnFloatingClosed(object? sender, EventArgs e)
    {
        _floatingWindow = null;
    }
}
