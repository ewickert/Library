using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Library.Services;

namespace Library.ViewModels;

public partial class AlternatePrintingViewModel : ObservableObject
{
    private readonly ScryfallService _scryfall;

    public ScryfallCardData Data { get; }

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _isLoading = true;

    public AlternatePrintingViewModel(ScryfallCardData data, ScryfallService scryfall)
    {
        Data = data;
        _scryfall = scryfall;
    }

    public async Task LoadImageAsync(CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            var bmp = await _scryfall.GetCardImageAsync(Data.ScryfallId, ct);
            if (!ct.IsCancellationRequested)
                Image = bmp;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoading = false;
        }
    }
}
