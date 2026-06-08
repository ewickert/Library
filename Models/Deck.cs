using CommunityToolkit.Mvvm.ComponentModel;

namespace Library.Models;

public partial class Deck : ObservableObject
{
    public int Id { get; set; }
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string? _description;
    [ObservableProperty] private string? _format;
    public DateTime Created { get; set; } = DateTime.UtcNow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorIdentityMana))]
    private string? _commanderColorIdentity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorIdentityMana))]
    private string? _commanderName;

    [ObservableProperty] private string? _commanderScryfallId;

    /// <summary>Converts "W,U,B" → "{W}{U}{B}" for ManaSymbolsControl.</summary>
    public string? ColorIdentityMana =>
        string.IsNullOrWhiteSpace(CommanderColorIdentity) ? null :
        string.Concat(CommanderColorIdentity.Split(',')
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => "{" + s.Trim() + "}"));

    public List<DeckCard> Cards { get; set; } = new();
}

public partial class DeckCard : ObservableObject
{
    public int Id { get; set; }
    public int DeckId { get; set; }
    public int CardId { get; set; }
    [ObservableProperty] private int _quantity;
    public bool IsSideboard { get; set; }
    public bool IsCommander { get; set; }
    public Card? Card { get; set; }
}
