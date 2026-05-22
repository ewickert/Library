using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Library.Services;

namespace Library.Views;

/// <summary>
/// Fetches all printings for a card and lets the user choose one before adding to the shopping list.
/// Returns the chosen <see cref="ScryfallCardData"/> via <see cref="ShowDialog{T}"/>, or null on cancel.
/// </summary>
public partial class PrintingPickerWindow : Window
{
    private readonly ScryfallService _scryfall;
    private ScryfallCardData? _result;
    private PrintingRow? _selectedRow;
    private CancellationTokenSource? _imageCts;

    public PrintingPickerWindow(ScryfallCardData original, ScryfallService scryfall)
    {
        _scryfall = scryfall;
        InitializeComponent();

        CardNameHeader.Text = original.Name;
        Title = $"Choose Printing — {original.Name}";

        _ = LoadPrintingsAsync(original);
    }

    private async Task LoadPrintingsAsync(ScryfallCardData original)
    {
        LoadingBar.IsVisible = true;
        PrintingCountLabel.Text = "Loading…";

        var printings = await _scryfall.GetAlternatePrintingsAsync(original.Name);

        LoadingBar.IsVisible = false;

        if (printings.Count == 0)
        {
            PrintingCountLabel.Text = "No printings found.";
            return;
        }

        PrintingCountLabel.Text = $"{printings.Count} printing{(printings.Count == 1 ? "" : "s")}";

        var rows = printings.Select(p => new PrintingRow(p)).ToList();
        PrintingsList.ItemsSource = rows;

        // Pre-select the printing that matches the original search result
        var match = rows.FirstOrDefault(r => r.Data.ScryfallId == original.ScryfallId) ?? rows[0];
        PrintingsList.SelectedItem = match;
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _selectedRow = PrintingsList.SelectedItem as PrintingRow;
        OkButton.IsEnabled = _selectedRow != null;

        if (_selectedRow != null)
        {
            SelectionLabel.Text = $"{_selectedRow.SetName} ({_selectedRow.SetCode}) #{_selectedRow.CollectorNumber}";
            _ = LoadPreviewAsync(_selectedRow.Data);
        }
    }

    private async Task LoadPreviewAsync(ScryfallCardData data)
    {
        _imageCts?.Cancel();
        _imageCts = new CancellationTokenSource();
        var cts = _imageCts;

        PreviewImage.Source = null;
        PreviewPlaceholder.IsVisible = false;
        ImageLoadingBar.IsVisible = true;

        try
        {
            var bmp = await _scryfall.GetCardImageAsync(data.ScryfallId, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                PreviewImage.Source = bmp;
                ImageLoadingBar.IsVisible = false;
                PreviewPlaceholder.IsVisible = bmp == null;
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!cts.IsCancellationRequested)
            {
                ImageLoadingBar.IsVisible = false;
                PreviewPlaceholder.IsVisible = true;
            }
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        _result = _selectedRow?.Data;
        Close(_result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}

/// <summary>A single row in the printings list.</summary>
public sealed class PrintingRow
{
    public ScryfallCardData Data { get; }
    public string SetName         => Data.SetName;
    public string SetCode         => Data.SetCode.ToUpperInvariant();
    public string CollectorNumber => Data.CollectorNumber;
    public string Rarity          => Capitalize(Data.Rarity);
    public bool   IsFoil          => false; // Scryfall search results don't carry foil flag; user picks foil at add time
    public IBrush RarityColor => Data.Rarity.ToLowerInvariant() switch
    {
        "mythic"   => Brushes.OrangeRed,
        "rare"     => Brushes.Gold,
        "uncommon" => Brushes.Silver,
        _          => Brushes.Gray,
    };

    public PrintingRow(ScryfallCardData data) => Data = data;

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
