using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Models;
using Library.Services;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Library.ViewModels;

public partial class LogGamePlayerViewModel : ObservableObject
{
    [ObservableProperty] private string _playerName = string.Empty;
    [ObservableProperty] private bool _isMe;
    [ObservableProperty] private Deck? _linkedDeck;
    [ObservableProperty] private string _deckName = string.Empty;
    [ObservableProperty] private int _finishPosition = 1;

    public bool HasLinkedDeck => LinkedDeck != null;

    partial void OnLinkedDeckChanged(Deck? value)
    {
        OnPropertyChanged(nameof(HasLinkedDeck));
        if (value != null) DeckName = value.Name;
    }
}

public partial class GamesViewModel : ObservableObject
{
    private readonly DatabaseService _db;

    // ── Game history ──────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<Game> _games = new();

    // ── Overall stats ─────────────────────────────────────────────────────────
    [ObservableProperty] private int _totalGames;
    [ObservableProperty] private int _totalWins;
    [ObservableProperty] private int _totalLosses;
    [ObservableProperty] private string _winRateText = "—";
    [ObservableProperty] private string _avgWinTurnText = "—";
    [ObservableProperty] private string _avgLossTurnText = "—";
    [ObservableProperty] private ObservableCollection<DeckCurveBinViewModel> _winTurnBins = new();
    [ObservableProperty] private ObservableCollection<DeckCurveBinViewModel> _lossTurnBins = new();

    // ── Log game form ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoggingGame;
    [ObservableProperty] private int _playerCount = 4;
    [ObservableProperty] private int _turnEnded;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private ObservableCollection<LogGamePlayerViewModel> _formPlayers = new();
    [ObservableProperty] private ObservableCollection<Deck> _availableDecks = new();
    [ObservableProperty] private string _logGameError = string.Empty;

    public bool HasLogGameError => !string.IsNullOrEmpty(LogGameError);
    public static int[] PlayerCountOptions => [2, 3, 4, 5, 6];

    public GamesViewModel(DatabaseService db)
    {
        _db = db;
    }

    public void Reload()
    {
        var allDecks = _db.GetAllDecks();
        AvailableDecks = new ObservableCollection<Deck>(allDecks);

        var games = _db.GetAllGames();
        Games = new ObservableCollection<Game>(games);
        RecomputeStats(games);
    }

    private void RecomputeStats(List<Game> games)
    {
        var myGames = games.Where(g => g.MyPlayer != null).ToList();
        TotalGames = myGames.Count;
        TotalWins = myGames.Count(g => g.IWon);
        TotalLosses = TotalGames - TotalWins;
        WinRateText = TotalGames > 0
            ? $"{TotalWins * 100.0 / TotalGames:0.#}%"
            : "—";

        var wonGames  = myGames.Where(g => g.IWon  && g.TurnEnded.HasValue).Select(g => g.TurnEnded!.Value).ToList();
        var lostGames = myGames.Where(g => !g.IWon && g.TurnEnded.HasValue).Select(g => g.TurnEnded!.Value).ToList();

        AvgWinTurnText  = wonGames.Count  > 0 ? wonGames.Average().ToString("0.#",  CultureInfo.InvariantCulture) : "—";
        AvgLossTurnText = lostGames.Count > 0 ? lostGames.Average().ToString("0.#", CultureInfo.InvariantCulture) : "—";

        WinTurnBins  = BuildTurnHistogram(wonGames);
        LossTurnBins = BuildTurnHistogram(lostGames);
    }

    private static ObservableCollection<DeckCurveBinViewModel> BuildTurnHistogram(List<int> turns)
    {
        if (turns.Count == 0) return new ObservableCollection<DeckCurveBinViewModel>();

        // Bucket turns: 1-5, 6-10, 11-15, 16-20, 21+
        var buckets = new[] { (1, 5), (6, 10), (11, 15), (16, 20), (21, int.MaxValue) };
        var labels  = new[] { "1–5", "6–10", "11–15", "16–20", "21+" };
        var counts  = buckets.Select(b => turns.Count(t => t >= b.Item1 && t <= b.Item2)).ToArray();
        var max = Math.Max(1, counts.Max());

        var bins = labels.Select((label, i) =>
        {
            var width = counts[i] == 0 ? 2 : counts[i] * 120.0 / max;
            return new DeckCurveBinViewModel(label, counts[i], width);
        });
        return new ObservableCollection<DeckCurveBinViewModel>(bins);
    }

    // ── Log game form commands ────────────────────────────────────────────────

    [RelayCommand]
    private void StartLogGame()
    {
        IsLoggingGame = true;
        LogGameError = string.Empty;
        TurnEnded = 0;
        Notes = string.Empty;
        RebuildFormPlayers(PlayerCount);
    }

    [RelayCommand]
    private void CancelLogGame()
    {
        IsLoggingGame = false;
        FormPlayers.Clear();
    }

    partial void OnPlayerCountChanged(int value)
    {
        if (IsLoggingGame) RebuildFormPlayers(value);
    }

    private void RebuildFormPlayers(int count)
    {
        var existing = FormPlayers.ToList();
        FormPlayers.Clear();

        for (int i = 0; i < count; i++)
        {
            var prev = i < existing.Count ? existing[i] : null;
            var p = new LogGamePlayerViewModel
            {
                PlayerName    = prev?.PlayerName ?? (i == 0 ? "Me" : $"Player {i + 1}"),
                IsMe          = prev?.IsMe ?? (i == 0),
                LinkedDeck    = prev?.LinkedDeck,
                DeckName      = prev?.DeckName ?? string.Empty,
                FinishPosition = i == 0 ? 1 : i + 1
            };
            FormPlayers.Add(p);
        }
    }

    [RelayCommand]
    private void SaveGame()
    {
        LogGameError = string.Empty;

        // Validation
        var winners = FormPlayers.Where(p => p.FinishPosition == 1).ToList();
        if (winners.Count != 1)
        {
            LogGameError = "Exactly one player must have finish position 1 (winner).";
            return;
        }
        if (FormPlayers.Any(p => string.IsNullOrWhiteSpace(p.PlayerName)))
        {
            LogGameError = "All players must have a name.";
            return;
        }
        var positions = FormPlayers.Select(p => p.FinishPosition).OrderBy(x => x).ToList();
        var expected = Enumerable.Range(1, FormPlayers.Count).ToList();
        if (!positions.SequenceEqual(expected))
        {
            LogGameError = $"Finish positions must be 1 through {FormPlayers.Count} with no duplicates.";
            return;
        }

        var game = new Game
        {
            PlayedAt  = DateTime.UtcNow,
            TurnEnded = TurnEnded > 0 ? TurnEnded : null,
            Notes     = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            Players   = FormPlayers.Select(p => new GamePlayer
            {
                PlayerName     = p.PlayerName.Trim(),
                IsMe           = p.IsMe,
                DeckId         = p.LinkedDeck?.Id,
                DeckName       = string.IsNullOrWhiteSpace(p.DeckName) ? null : p.DeckName.Trim(),
                FinishPosition = p.FinishPosition
            }).ToList()
        };

        _db.AddGame(game);
        IsLoggingGame = false;
        FormPlayers.Clear();
        Reload();
    }

    [RelayCommand]
    private void DeleteGame(Game game)
    {
        _db.DeleteGame(game.Id);
        Games.Remove(game);
        var remaining = Games.ToList();
        RecomputeStats(remaining);
    }

    partial void OnLogGameErrorChanged(string value) =>
        OnPropertyChanged(nameof(HasLogGameError));
}
