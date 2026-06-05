using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Library.Services;
using Library.ViewModels;
using Library.Views;
using System.IO;

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

        // ── Deck export ────────────────────────────────────────────────────────
        vm.Decks.RequestExportDeck = async (suggestedName, content) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Deck",
                SuggestedFileName = suggestedName,
                FileTypeChoices = [new FilePickerFileType("Text Files") { Patterns = ["*.txt"] }]
            });
            if (file == null) return null;

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(content);

            return file.TryGetLocalPath();
        };

        // ── Deck share (write to temp → OS share sheet) ────────────────────────
        vm.Decks.RequestShareDeck = async (suggestedName, content) =>
        {
            var tempPath = Path.Combine(Path.GetTempPath(), suggestedName);
            await File.WriteAllTextAsync(tempPath, content, System.Text.Encoding.UTF8);
            MacShareHelper.ShareFile(tempPath, TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        };

        // ── Deck import ────────────────────────────────────────────────────────
        vm.Decks.RequestImportDeck = async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Deck",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Deck Files") { Patterns = ["*.txt", "*.dec"] }]
            });
            if (files.Count == 0) return null;

            var path = files[0].TryGetLocalPath();
            return path == null ? null : await File.ReadAllTextAsync(path);
        };

        // ── File → Import CSV ──────────────────────────────────────────────────
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