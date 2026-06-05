namespace Library.Models;

public class DeckVersion
{
    public int Id { get; set; }
    public int DeckId { get; set; }
    public int VersionNumber { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsAuto { get; set; }

    public string DisplayLabel => !string.IsNullOrWhiteSpace(Label)
        ? $"v{VersionNumber} — {Label}"
        : IsAuto
            ? $"v{VersionNumber} · auto ({CreatedAt.ToLocalTime():MMM d, h:mm tt})"
            : $"v{VersionNumber} ({CreatedAt.ToLocalTime():MMM d, yyyy h:mm tt})";

    public string ShortLabel => $"v{VersionNumber}";
}

public class DeckVersionCard
{
    public int Id { get; set; }
    public int VersionId { get; set; }
    public int CardId { get; set; }
    public string CardName { get; set; } = string.Empty;
    public string SetCode { get; set; } = string.Empty;
    public string CollectorNumber { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public bool IsSideboard { get; set; }
    public bool IsCommander { get; set; }
}

public enum DiffChangeKind { Added, Removed, QuantityChanged, MovedToSideboard, MovedFromSideboard, CommanderChanged }

public class DeckDiffEntry
{
    public string CardName { get; init; } = string.Empty;
    public string SetCode { get; init; } = string.Empty;
    public string CollectorNumber { get; init; } = string.Empty;
    public DiffChangeKind Change { get; init; }
    public int OldQty { get; init; }
    public int NewQty { get; init; }
    public bool WasSideboard { get; init; }
    public bool IsSideboard { get; init; }
    public bool WasCommander { get; init; }
    public bool IsCommander { get; init; }

    public string ChangeLabel => Change switch
    {
        DiffChangeKind.Added => $"+ {NewQty}",
        DiffChangeKind.Removed => $"- {OldQty}",
        DiffChangeKind.QuantityChanged => OldQty < NewQty ? $"+{NewQty - OldQty}" : $"-{OldQty - NewQty}",
        DiffChangeKind.MovedToSideboard => "→ sideboard",
        DiffChangeKind.MovedFromSideboard => "← main",
        DiffChangeKind.CommanderChanged => IsCommander ? "→ commander" : "← not commander",
        _ => ""
    };
}

public static class DeckDiff
{
    public static List<DeckDiffEntry> Compute(List<DeckVersionCard> from, List<DeckVersionCard> to)
    {
        var result = new List<DeckDiffEntry>();
        var fromMap = from.ToDictionary(c => c.CardId);
        var toMap = to.ToDictionary(c => c.CardId);

        foreach (var card in from.Where(c => !toMap.ContainsKey(c.CardId)))
            result.Add(new DeckDiffEntry
            {
                CardName = card.CardName, SetCode = card.SetCode, CollectorNumber = card.CollectorNumber,
                Change = DiffChangeKind.Removed, OldQty = card.Quantity, NewQty = 0,
                WasSideboard = card.IsSideboard, WasCommander = card.IsCommander
            });

        foreach (var card in to.Where(c => !fromMap.ContainsKey(c.CardId)))
            result.Add(new DeckDiffEntry
            {
                CardName = card.CardName, SetCode = card.SetCode, CollectorNumber = card.CollectorNumber,
                Change = DiffChangeKind.Added, OldQty = 0, NewQty = card.Quantity,
                IsSideboard = card.IsSideboard, IsCommander = card.IsCommander
            });

        foreach (var card in to.Where(c => fromMap.ContainsKey(c.CardId)))
        {
            var old = fromMap[card.CardId];
            if (old.Quantity != card.Quantity)
                result.Add(new DeckDiffEntry
                {
                    CardName = card.CardName, SetCode = card.SetCode, CollectorNumber = card.CollectorNumber,
                    Change = DiffChangeKind.QuantityChanged, OldQty = old.Quantity, NewQty = card.Quantity,
                    WasSideboard = old.IsSideboard, IsSideboard = card.IsSideboard,
                    WasCommander = old.IsCommander, IsCommander = card.IsCommander
                });
            else if (old.IsSideboard != card.IsSideboard)
                result.Add(new DeckDiffEntry
                {
                    CardName = card.CardName, SetCode = card.SetCode, CollectorNumber = card.CollectorNumber,
                    Change = card.IsSideboard ? DiffChangeKind.MovedToSideboard : DiffChangeKind.MovedFromSideboard,
                    OldQty = old.Quantity, NewQty = card.Quantity,
                    WasSideboard = old.IsSideboard, IsSideboard = card.IsSideboard,
                    WasCommander = old.IsCommander, IsCommander = card.IsCommander
                });
            else if (old.IsCommander != card.IsCommander)
                result.Add(new DeckDiffEntry
                {
                    CardName = card.CardName, SetCode = card.SetCode, CollectorNumber = card.CollectorNumber,
                    Change = DiffChangeKind.CommanderChanged, OldQty = old.Quantity, NewQty = card.Quantity,
                    WasSideboard = old.IsSideboard, IsSideboard = card.IsSideboard,
                    WasCommander = old.IsCommander, IsCommander = card.IsCommander
                });
        }

        return result
            .OrderBy(e => e.Change)
            .ThenBy(e => e.CardName)
            .ToList();
    }
}
