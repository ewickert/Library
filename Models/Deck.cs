namespace Library.Models;

public class Deck
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Format { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    /// <summary>Comma-separated color identity of the commander, e.g. "W,U,B". Null when not a Commander deck or no commander set.</summary>
    public string? CommanderColorIdentity { get; set; }
    /// <summary>Name of the commander card, populated when loaded via GetAllDecks.</summary>
    public string? CommanderName { get; set; }
    /// <summary>Scryfall ID of the commander card, populated when loaded via GetAllDecks.</summary>
    public string? CommanderScryfallId { get; set; }
    /// <summary>Converts "W,U,B" → "{W}{U}{B}" for ManaSymbolsControl.</summary>
    public string? ColorIdentityMana =>
        string.IsNullOrWhiteSpace(CommanderColorIdentity) ? null :
        string.Concat(CommanderColorIdentity.Split(',')
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => "{" + s.Trim() + "}"));
    public List<DeckCard> Cards { get; set; } = new();
}

public class DeckCard
{
    public int Id { get; set; }
    public int DeckId { get; set; }
    public int CardId { get; set; }
    public int Quantity { get; set; }
    public bool IsSideboard { get; set; }
    public bool IsCommander { get; set; }
    public Card? Card { get; set; }
}
