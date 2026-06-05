using Library.Models;

namespace Library.ViewModels;

public class DeckDiffEntryViewModel
{
    public DeckDiffEntry Entry { get; }

    public DeckDiffEntryViewModel(DeckDiffEntry entry)
    {
        Entry = entry;
    }

    public string CardName => Entry.CardName;
    public string SetCode => Entry.SetCode;
    public string ChangeLabel => Entry.ChangeLabel;
    public DiffChangeKind Change => Entry.Change;

    public string RowColor => Entry.Change switch
    {
        DiffChangeKind.Added => "#1E3A2F",
        DiffChangeKind.Removed => "#3A1E1E",
        DiffChangeKind.QuantityChanged => Entry.NewQty > Entry.OldQty ? "#1E2E3A" : "#2A2A1E",
        _ => "#2A2A2A"
    };

    public string ChangeLabelColor => Entry.Change switch
    {
        DiffChangeKind.Added => "#7DCFAA",
        DiffChangeKind.Removed => "#CF7D7D",
        DiffChangeKind.QuantityChanged => Entry.NewQty > Entry.OldQty ? "#7DB8CF" : "#CFC97D",
        _ => "#CCCCCC"
    };
}
