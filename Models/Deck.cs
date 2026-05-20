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
