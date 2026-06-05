using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Library.ViewModels;

namespace Library.Views;

public partial class ScryfallPaneView : UserControl
{
    private ScryfallPaneWindow? _floatingWindow;

    public static readonly StyledProperty<bool> IsFloatingProperty =
        AvaloniaProperty.Register<ScryfallPaneView, bool>(nameof(IsFloating));

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

    public ScryfallPaneView()
    {
        InitializeComponent();
        PopOutButton.IsVisible = !IsFloating;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ScryfallGridScrollViewer.AddHandler(PointerWheelChangedEvent, OnGridWheel, RoutingStrategies.Tunnel);
    }

    private void OnGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (DataContext is DecksViewModel vm)
        {
            vm.CollectionGridZoom = Math.Clamp(vm.CollectionGridZoom + (e.Delta.Y > 0 ? 0.1 : -0.1), 0.4, 3.0);
            e.Handled = true;
        }
    }

    private void OnPopOutClick(object? sender, RoutedEventArgs e)
    {
        if (_floatingWindow != null)
        {
            _floatingWindow.Activate();
            return;
        }

        if (DataContext is not DecksViewModel vm) return;

        vm.IsScryfallPaneOpen = false;

        _floatingWindow = new ScryfallPaneWindow();
        _floatingWindow.DataContext = vm;
        _floatingWindow.Closed += OnFloatingWindowClosed;
        _floatingWindow.Show(TopLevel.GetTopLevel(this) as Window);
    }

    private void OnFloatingWindowClosed(object? sender, EventArgs e)
    {
        _floatingWindow = null;
        if (DataContext is DecksViewModel vm)
            vm.IsScryfallPaneOpen = true;
    }

    internal ItemsControl ListPane => ScryfallResultsListView;
    internal ItemsControl GridPane => ScryfallResultsGridView;
}
