using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Library.ViewModels;

namespace Library.Views;

public partial class CollectionPaneView : UserControl
{
    private CollectionPaneWindow? _floatingWindow;

    public static readonly StyledProperty<bool> IsFloatingProperty =
        AvaloniaProperty.Register<CollectionPaneView, bool>(nameof(IsFloating));

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

    public CollectionPaneView()
    {
        InitializeComponent();
        PopOutButton.IsVisible = !IsFloating;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CollectionGridScrollViewer.AddHandler(PointerWheelChangedEvent, OnGridWheel, RoutingStrategies.Tunnel);
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

    private async void OnSearchHelpClick(object? sender, RoutedEventArgs e)
    {
        var win = new SearchHelpWindow();
        await win.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private void OnPopOutClick(object? sender, RoutedEventArgs e)
    {
        if (_floatingWindow != null)
        {
            _floatingWindow.Activate();
            return;
        }

        if (DataContext is not DecksViewModel vm) return;

        vm.IsCollectionPaneOpen = false;

        _floatingWindow = new CollectionPaneWindow();
        _floatingWindow.DataContext = vm;
        _floatingWindow.Closed += OnFloatingWindowClosed;
        _floatingWindow.Show(TopLevel.GetTopLevel(this) as Window);
    }

    private void OnFloatingWindowClosed(object? sender, EventArgs e)
    {
        _floatingWindow = null;
        if (DataContext is DecksViewModel vm)
            vm.IsCollectionPaneOpen = true;
    }

    // Exposed for drag-source registration in DecksView
    internal ItemsControl ListPane => CollectionListPane;
    internal ItemsControl GridPane => CollectionGridPane;
    internal ItemsControl ExtListPane => ExternalResultsListView;
    internal ItemsControl ExtGridPane => ExternalResultsGridView;
    internal ScrollViewer GridScroller => CollectionGridScrollViewer;
}
