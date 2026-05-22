using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Library.ViewModels;

namespace Library.Views;

public partial class ShoppingView : UserControl
{
    public ShoppingView()
    {
        InitializeComponent();
    }

    private async void OnExportCkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShoppingViewModel vm) return;
        var text = vm.BuildCardKingdomExport();
        if (string.IsNullOrWhiteSpace(text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetTextAsync(text);
        vm.ExportCopied = true;
        _ = Task.Delay(2000).ContinueWith(
            _ => Dispatcher.UIThread.Post(() => vm.ExportCopied = false));
    }

    private async void OnExportTcgClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShoppingViewModel vm) return;
        var text = vm.BuildTcgPlayerExport();
        if (string.IsNullOrWhiteSpace(text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        await clipboard.SetTextAsync(text);
        vm.TcgExportCopied = true;
        _ = Task.Delay(2000).ContinueWith(
            _ => Dispatcher.UIThread.Post(() => vm.TcgExportCopied = false));
    }
}
