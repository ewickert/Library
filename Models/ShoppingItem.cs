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

    /// <summary>Current market price pulled from the placeholder card row (may be null if never fetched).</summary>
    public decimal? CurrentMarketPrice { get; set; }

    /// <summary>When the price was last fetched (UTC). Used for 24-hour staleness check.</summary>
    public DateTime? CurrentMarketPriceFetchedAt { get; set; }
}
