using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Library.ViewModels;

namespace Library.Views;

public partial class BindersView : UserControl
{
    private BindersViewModel? ViewModel => DataContext as BindersViewModel;

    public BindersView()
    {
        InitializeComponent();
        GridScrollViewer.AddHandler(PointerWheelChangedEvent, OnGridWheel, RoutingStrategies.Tunnel);
    }

    private void OnGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (ViewModel is { } vm)
        {
            vm.GridZoom = Math.Clamp(vm.GridZoom + (e.Delta.Y > 0 ? 0.1 : -0.1), 0.4, 3.0);
            e.Handled = true;
        }
    }

    private async void OnSearchHelpClick(object? sender, RoutedEventArgs e)
    {
        var win = new SearchHelpWindow();
        await win.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }
}
