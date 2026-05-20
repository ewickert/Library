using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Library.Services;
using Library.ViewModels;

namespace Library.Views;

public partial class CollectionView : UserControl
{
    private CollectionViewModel? ViewModel => DataContext as CollectionViewModel;

    public CollectionView()
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

    private void OnAddCardClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;
        var win = TopLevel.GetTopLevel(this) as MainWindow;
        if (win == null) return;
        var dialog = new AddEditCardWindow(win.DatabaseService, win.ScryfallService);
        dialog.ShowDialog(win).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.LoadCards()));
    }

    private void OnEditCardClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm?.SelectedCard == null) return;
        var win = TopLevel.GetTopLevel(this) as MainWindow;
        if (win == null) return;
        var dialog = new AddEditCardWindow(win.DatabaseService, win.ScryfallService, vm.SelectedCard);
        dialog.ShowDialog(win).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.LoadCards()));
    }

    private async void OnImportCsvClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        var win = TopLevel.GetTopLevel(this) as MainWindow;
        if (vm == null || win == null) return;

        var files = await win.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Cards from CSV",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV Files") { Patterns = ["*.csv", "*.txt"] }]
        });

        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path == null) return;

        var importer = new CsvImportService();
        var result = importer.Import(path);

        if (result.HasFatalError)
        {
            await ShowMessageDialog(win, "Import Failed", result.Error!);
            return;
        }

        foreach (var card in result.Cards)
            win.DatabaseService.AddCard(card);

        vm.LoadCards();

        var summary = $"Imported {result.ImportedCount} card(s).";
        if (result.RowErrors.Count > 0)
            summary += $"\n\n{result.RowErrors.Count} row(s) skipped:\n" +
                       string.Join("\n", result.RowErrors.Take(10));
        await ShowMessageDialog(win, "Import Complete", summary);
    }

    private static async Task ShowMessageDialog(Window owner, string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right }
                }
            }
        };
        // Wire OK button
        var panel = (StackPanel)dialog.Content!;
        ((Button)panel.Children[1]).Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
