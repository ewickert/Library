namespace Library.Models;

public class ShoppingItem
{
    public int    Id              { get; set; }
    public string ScryfallId      { get; set; } = string.Empty;
    public string Name            { get; set; } = string.Empty;
    public string SetCode         { get; set; } = string.Empty;
    public string SetName         { get; set; } = string.Empty;
    public string CollectorNumber { get; set; } = string.Empty;
    public string ManaCost        { get; set; } = string.Empty;
    public string TypeLine        { get; set; } = string.Empty;
    public string ColorIdentity   { get; set; } = string.Empty;
    public string Rarity          { get; set; } = string.Empty;
    public DateTime Added         { get; set; } = DateTime.UtcNow;

    /// <summary>The Id of the placeholder Card row in the Cards table (Quantity=0) created for this item.</summary>
    public int? PlaceholderCardId { get; set; }
}
