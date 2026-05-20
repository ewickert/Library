using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Library.Models;
using Library.Services;

namespace Library.ViewModels;

public partial class BinderCardSlotViewModel : ObservableObject
{
    private readonly ScryfallService _scryfall;

    public BinderCard BinderCard { get; }
    public Card Card => BinderCard.Card!;

    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _isLoading = true;

    public BinderCardSlotViewModel(BinderCard binderCard, ScryfallService scryfall)
    {
        BinderCard = binderCard;
        _scryfall = scryfall;
    }

    public async Task LoadImageAsync(CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            Bitmap? bmp = null;
            if (!string.IsNullOrEmpty(Card.ScryfallId))
                bmp = await _scryfall.GetCardImageAsync(Card.ScryfallId, ct);
            else if (!string.IsNullOrEmpty(Card.SetCode) && !string.IsNullOrEmpty(Card.CollectorNumber))
                bmp = await _scryfall.GetCardImageByCollectorAsync(Card.SetCode, Card.CollectorNumber, Card.Foil, ct);

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
