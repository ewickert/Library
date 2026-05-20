namespace Library.Models;

public class Binder
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public List<BinderCard> Cards { get; set; } = new();
}

public class BinderCard
{
    public int Id { get; set; }
    public int BinderId { get; set; }
    public int CardId { get; set; }
    public int Quantity { get; set; }
    public int SlotIndex { get; set; }
    public Card? Card { get; set; }
}
