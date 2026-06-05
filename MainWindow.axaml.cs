using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Library.Services;
using Library.ViewModels;
using Library.Views;

namespace Library;

public partial class MainWindow : Window
{
    public DatabaseService DatabaseService { get; }
    public ScryfallService ScryfallService { get; }

    public MainWindow()
    {
        DatabaseService = new DatabaseService();
        ScryfallService = new ScryfallService();
        var vm = new MainWindowViewModel(DatabaseService, ScryfallService);
        DataContext = vm;
        InitializeComponent();
        SymbolService.Instance.BeginLoad();

        // Register the global printing-picker delegate so ViewModels can show the dialog without
        // taking a dependency on Views.
        ScryfallResultViewModel.GlobalPickPrintingAsync = async data =>
        {
            var picker = new PrintingPickerWindow(data, ScryfallService);
            return await picker.ShowDialog<ScryfallCardData?>(this);
        };

        // Provide the File → Import CSV handler (needs StorageProvider from the window)
        vm.RequestImportCsv = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
                await ShowMessageAsync("Import Failed", result.Error!);
                return;
            }

            foreach (var card in result.Cards)
                DatabaseService.AddCard(card);

            vm.Collection.LoadCards();

            var summary = $"Imported {result.ImportedCount} card(s).";
            if (result.RowErrors.Count > 0)
                summary += $"\n\n{result.RowErrors.Count} row(s) skipped:\n" +
                           string.Join("\n", result.RowErrors.Take(10));
            await ShowMessageAsync("Import Complete", summary);
        };
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Avalonia.Controls.TextBlock
            {
                Text = message,
                Margin = new Avalonia.Thickness(20),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        };
        await dialog.ShowDialog(this);
    }
}