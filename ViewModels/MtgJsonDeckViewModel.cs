using MtgJson.Models;

namespace Library.ViewModels;

public class MtgJsonDeckViewModel
{
    public DeckEntry Data { get; }

    public MtgJsonDeckViewModel(DeckEntry data) => Data = data;

    public string Name => Data.DisplayName;
    public string Meta => Data.SetCode;
}
