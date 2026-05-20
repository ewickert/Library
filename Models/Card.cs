namespace Library.Models;

public class Card
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SetCode { get; set; } = string.Empty;
    public string SetName { get; set; } = string.Empty;
    public string CollectorNumber { get; set; } = string.Empty;
    public bool Foil { get; set; }
    public string Rarity { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public long? ManaBoxId { get; set; }
    public string? ScryfallId { get; set; }
    public decimal? PurchasePrice { get; set; }
    public bool Misprint { get; set; }
    public bool Altered { get; set; }
    public string Condition { get; set; } = "NM";
    public string Language { get; set; } = "en";
    public string? PurchasePriceCurrency { get; set; }
    public DateTime Added { get; set; } = DateTime.UtcNow;
    /// <summary>Comma-separated color identity symbols, e.g. "W,U,B". Null when unknown.</summary>
    public string? ColorIdentity { get; set; }
    /// <summary>Mana cost string, e.g. "{2}{U}{B}". Null when unknown.</summary>
    public string? ManaCost { get; set; }
    /// <summary>Type line, e.g. "Legendary Creature — Human Wizard". Null when unknown.</summary>
    public string? TypeLine { get; set; }
    /// <summary>True when this card was added as a placeholder from the shopping list (Quantity=0).</summary>
    public bool IsPlaceholder { get; set; }
}
