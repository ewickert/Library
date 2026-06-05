namespace Library.Models;

public class Game
{
    public int Id { get; set; }
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    public int? TurnEnded { get; set; }
    public string? Notes { get; set; }
    public List<GamePlayer> Players { get; set; } = new();

    public GamePlayer? MyPlayer => Players.FirstOrDefault(p => p.IsMe);
    public bool IWon => MyPlayer?.FinishPosition == 1;
    public string ResultText => MyPlayer == null ? "?" : IWon ? "W" : "L";

    public string PlayedAtLocal => PlayedAt.ToLocalTime().ToString("MMM d, yyyy");
    public string WinnerName => Players.FirstOrDefault(p => p.FinishPosition == 1)?.PlayerName ?? "?";
    public string PlayerSummary => string.Join(", ", Players.Select(p =>
        p.IsMe ? $"Me ({p.DeckDisplayName})" : p.PlayerName));
}

public class GamePlayer
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public bool IsMe { get; set; }
    public int? DeckId { get; set; }
    public string? DeckName { get; set; }
    public int FinishPosition { get; set; } = 1;
    public Deck? Deck { get; set; }

    public bool IsWinner => FinishPosition == 1;
    public string DeckDisplayName => Deck?.Name ?? DeckName ?? string.Empty;
}
