using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Library.Models;
using Library.Services;
using Library.ViewModels;
using Library.Views;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Linq;

namespace Library;

public partial class MainWindow : Window
{
    public DatabaseService DatabaseService { get; }
    public ScryfallService ScryfallService { get; }

    public MainWindow()
    {
        DatabaseService = App.Services.GetRequiredService<DatabaseService>();
        ScryfallService = App.Services.GetRequiredService<ScryfallService>();
        var mtgJson = App.Services.GetRequiredService<MtgJson.MtgJsonService>();
        var vm = new MainWindowViewModel(DatabaseService, ScryfallService, mtgJson);
        DataContext = vm;
        InitializeComponent();
        SymbolService.Instance.BeginLoad();
        SetIconService.Instance.BeginLoad();

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
#if DESKTOP
            MacShareHelper.ShareFile(tempPath, TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
#endif
        };

        // ── Copy deck to clipboard ─────────────────────────────────────────────
        vm.Decks.RequestCopyToClipboard = async content =>
        {
            if (Clipboard != null)
                await Clipboard.SetTextAsync(content);
        };

        // ── Alternate printings gallery ────────────────────────────────────────
        vm.Decks.RequestOpenAlternatePrintings = async (cardName, currentScryfallId, contextDeck) =>
        {
            var win = new AlternatePrintingsWindow(cardName, currentScryfallId,
                ScryfallService, DatabaseService, contextDeck);
            win.Closed += (_, _) => vm.Decks.ReloadDeckWishlist();
            await win.ShowDialog(this);
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

            await ImportCsvFromPathAsync(path);
        };
    }

    public void ShowSettings()
    {
        var win = new SettingsWindow { DataContext = DataContext };
        win.ShowDialog(this);
    }

    private void OnSettingsMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ShowSettings();

    public async Task ImportCsvFromPathAsync(string path)
    {
        var vm = DataContext as MainWindowViewModel;
        var importer = new CsvImportService();
        var result = importer.Import(path);

        if (result.HasFatalError)
        {
            await ShowMessageAsync("Import Failed", result.Error!);
            return;
        }

        var duplicateCount = CountDuplicates(result.Cards);
        if (duplicateCount >= 5 && duplicateCount >= result.Cards.Count * 0.3)
        {
            var msg = $"{duplicateCount} of {result.Cards.Count} card(s) in this file are already in your collection.\n\nContinue with the import anyway?";
            if (!await ShowConfirmAsync("Duplicate Cards Detected", msg))
                return;
        }

        foreach (var card in result.Cards)
            DatabaseService.AddCard(card);

        vm?.Collection.RefreshAvailableSets();
        vm?.Collection.LoadCards();

        var summary = $"Imported {result.ImportedCount} card(s).";
        if (result.RowErrors.Count > 0)
            summary += $"\n\n{result.RowErrors.Count} row(s) skipped:\n" +
                       string.Join("\n", result.RowErrors.Take(10));
        await ShowMessageAsync("Import Complete", summary);
    }

    private int CountDuplicates(List<Card> importCards)
    {
        var existing = DatabaseService.GetAllCards();
        var byScryfallId = existing
            .Where(c => !string.IsNullOrEmpty(c.ScryfallId))
            .Select(c => c.ScryfallId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bySetCollector = existing
            .Select(c => (c.Name.ToLowerInvariant(), c.SetCode.ToLowerInvariant(), c.CollectorNumber.ToLowerInvariant()))
            .ToHashSet();

        int count = 0;
        foreach (var card in importCards)
        {
            if (!string.IsNullOrEmpty(card.ScryfallId) && byScryfallId.Contains(card.ScryfallId))
                count++;
            else if (bySetCollector.Contains((card.Name.ToLowerInvariant(), card.SetCode.ToLowerInvariant(), card.CollectorNumber.ToLowerInvariant())))
                count++;
        }
        return count;
    }

    private async Task<bool> ShowConfirmAsync(string title, string message)
    {
        bool confirmed = false;
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var text = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 16),
        };

        var cancelBtn = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Left };
        cancelBtn.Click += (_, _) => { confirmed = false; dialog.Close(); };

        var continueBtn = new Button { Content = "Continue Anyway", HorizontalAlignment = HorizontalAlignment.Right };
        continueBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };

        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(cancelBtn, 0);
        Grid.SetColumn(continueBtn, 2);
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(continueBtn);

        var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 0 };
        panel.Children.Add(text);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(this);
        return confirmed;
    }

    internal async Task ShowMessageAsync(string title, string message)
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