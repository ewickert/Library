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

public class DeckStatViewModel
{
    public string DeckName { get; init; } = string.Empty;
    public string? CommanderScryfallId { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Total => Wins + Losses;
    public string RecordText => $"{Wins}–{Losses}";
    public string WinRateText => Total > 0 ? $"{Wins * 100.0 / Total:0}%" : "—";
    public bool HasCommanderArt => !string.IsNullOrEmpty(CommanderScryfallId);
    // Win bar as % of games won, scaled to 100px
    public double WinBarWidth => Total > 0 ? (double)Wins / Total * 100 : 0;
}

public class PlayerCountStatViewModel
{
    public string Label { get; init; } = string.Empty;
    public int Wins { get; init; }
    public int Total { get; init; }
    public int Losses => Total - Wins;
    public string WinRateText => Total > 0 ? $"{Wins * 100.0 / Total:0}%" : "—";
    public string GamesText => Total == 1 ? "1 game" : $"{Total} games";
    public double WinBarWidth => Total > 0 ? (double)Wins / Total * 100 : 0;
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

    // ── Streak ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _currentStreakText = "—";
    [ObservableProperty] private bool _currentStreakIsWin;
    [ObservableProperty] private string _bestWinStreakText = "—";
    [ObservableProperty] private int _gamesLast30Days;

    // ── Deck performance ──────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<DeckStatViewModel> _deckStats = new();

    // ── Player count breakdown ────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<PlayerCountStatViewModel> _playerCountStats = new();

    // ── Log game form ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isLoggingGame;
    [ObservableProperty] private int _playerCount = 4;
    [ObservableProperty] private int _turnEnded;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private ObservableCollection<LogGamePlayerViewModel> _formPlayers = new();
    [ObservableProperty] private ObservableCollection<Deck> _availableDecks = new();
    [ObservableProperty] private string _logGameError = string.Empty;

    private int? _editingGameId;
    private DateTime _editingGamePlayedAt;

    public bool HasLogGameError => !string.IsNullOrEmpty(LogGameError);
    public string LogGameTitle => _editingGameId.HasValue ? "Edit Game" : "Log New Game";
    public string SaveButtonText => _editingGameId.HasValue ? "Save Changes" : "Save Game";
    public static int[] AllPositions => [1, 2, 3, 4, 5, 6];

    // ── Dialog events ─────────────────────────────────────────────────────────
    public event Action? LogGameDialogRequested;
    public event Action? LogGameFormClosed;

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
        TotalWins  = myGames.Count(g => g.IWon);
        TotalLosses = TotalGames - TotalWins;
        WinRateText = TotalGames > 0 ? $"{TotalWins * 100.0 / TotalGames:0.#}%" : "—";

        var wonTurns  = myGames.Where(g =>  g.IWon && g.TurnEnded.HasValue).Select(g => g.TurnEnded!.Value).ToList();
        var lostTurns = myGames.Where(g => !g.IWon && g.TurnEnded.HasValue).Select(g => g.TurnEnded!.Value).ToList();

        AvgWinTurnText  = wonTurns.Count  > 0 ? wonTurns.Average().ToString("0.#",  CultureInfo.InvariantCulture) : "—";
        AvgLossTurnText = lostTurns.Count > 0 ? lostTurns.Average().ToString("0.#", CultureInfo.InvariantCulture) : "—";

        WinTurnBins  = BuildTurnHistogram(wonTurns);
        LossTurnBins = BuildTurnHistogram(lostTurns);

        ComputeStreaks(myGames);
        GamesLast30Days = myGames.Count(g => g.PlayedAt >= DateTime.UtcNow.AddDays(-30));
        DeckStats        = new ObservableCollection<DeckStatViewModel>(ComputeDeckStats(myGames));
        PlayerCountStats = new ObservableCollection<PlayerCountStatViewModel>(ComputePlayerCountStats(myGames));
    }

    private void ComputeStreaks(List<Game> myGames)
    {
        var ordered = myGames.OrderBy(g => g.PlayedAt).ToList();
        if (ordered.Count == 0)
        {
            CurrentStreakText = "—";
            BestWinStreakText = "—";
            return;
        }

        int currentRun = 0;
        bool? lastWon = null;
        int bestWin = 0, runWin = 0;

        foreach (var g in ordered)
        {
            if (lastWon == null || lastWon == g.IWon)
            {
                currentRun++;
            }
            else
            {
                if (lastWon == true) bestWin = Math.Max(bestWin, runWin);
                currentRun = 1;
                runWin = 0;
            }
            lastWon = g.IWon;
            if (g.IWon) runWin++;
        }
        if (lastWon == true) bestWin = Math.Max(bestWin, runWin);

        CurrentStreakIsWin = lastWon == true;
        CurrentStreakText  = currentRun > 1 ? $"{currentRun}" : (lastWon == true ? "W" : "L");
        BestWinStreakText  = bestWin > 0 ? bestWin.ToString() : "—";
    }

    private List<DeckStatViewModel> ComputeDeckStats(List<Game> myGames)
    {
        return myGames
            .Where(g => g.MyPlayer!.DeckName != null || g.MyPlayer!.DeckId.HasValue)
            .GroupBy(g =>
            {
                var p = g.MyPlayer!;
                return p.DeckId.HasValue ? $"id:{p.DeckId}" : $"name:{p.DeckName}";
            })
            .Select(grp =>
            {
                var sample = grp.First().MyPlayer!;
                var deck = sample.DeckId.HasValue
                    ? AvailableDecks.FirstOrDefault(d => d.Id == sample.DeckId)
                    : null;
                return new DeckStatViewModel
                {
                    DeckName             = deck?.Name ?? sample.DeckName ?? "Unknown",
                    CommanderScryfallId  = deck?.CommanderScryfallId,
                    Wins                 = grp.Count(g => g.IWon),
                    Losses               = grp.Count(g => !g.IWon)
                };
            })
            .OrderByDescending(d => d.Total)
            .ThenByDescending(d => d.Wins)
            .ToList();
    }

    private static List<PlayerCountStatViewModel> ComputePlayerCountStats(List<Game> myGames)
    {
        return myGames
            .GroupBy(g => g.Players.Count)
            .OrderBy(grp => grp.Key)
            .Select(grp => new PlayerCountStatViewModel
            {
                Label  = $"{grp.Key}p",
                Wins   = grp.Count(g => g.IWon),
                Total  = grp.Count()
            })
            .ToList();
    }

    private static ObservableCollection<DeckCurveBinViewModel> BuildTurnHistogram(List<int> turns)
    {
        if (turns.Count == 0) return new();

        var buckets = new[] { (1, 5), (6, 10), (11, 15), (16, 20), (21, int.MaxValue) };
        var labels  = new[] { "1–5", "6–10", "11–15", "16–20", "21+" };
        var counts  = buckets.Select(b => turns.Count(t => t >= b.Item1 && t <= b.Item2)).ToArray();
        var max = Math.Max(1, counts.Max());

        return new ObservableCollection<DeckCurveBinViewModel>(
            labels.Select((label, i) => new DeckCurveBinViewModel(
                label, counts[i], counts[i] == 0 ? 2 : counts[i] * 120.0 / max)));
    }

    // ── Log game form commands ────────────────────────────────────────────────

    [RelayCommand]
    private void StartLogGame()
    {
        _editingGameId = null;
        IsLoggingGame  = true;
        LogGameError   = string.Empty;
        TurnEnded      = 0;
        Notes          = string.Empty;
        RebuildFormPlayers(PlayerCount);
        OnPropertyChanged(nameof(LogGameTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        LogGameDialogRequested?.Invoke();
    }

    [RelayCommand]
    private void EditGame(Game game)
    {
        _editingGameId      = game.Id;
        _editingGamePlayedAt = game.PlayedAt;
        IsLoggingGame       = true;
        LogGameError        = string.Empty;
        TurnEnded           = game.TurnEnded ?? 0;
        Notes               = game.Notes ?? string.Empty;

        var ordered = game.Players.OrderBy(p => p.FinishPosition).ToList();
        PlayerCount = ordered.Count;
        FormPlayers.Clear();
        foreach (var p in ordered)
        {
            var linkedDeck = p.DeckId.HasValue
                ? AvailableDecks.FirstOrDefault(d => d.Id == p.DeckId)
                : null;
            FormPlayers.Add(new LogGamePlayerViewModel
            {
                PlayerName     = p.PlayerName,
                IsMe           = p.IsMe,
                LinkedDeck     = linkedDeck,
                DeckName       = p.DeckName ?? string.Empty,
                FinishPosition = p.FinishPosition
            });
        }

        OnPropertyChanged(nameof(LogGameTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        LogGameDialogRequested?.Invoke();
    }

    [RelayCommand]
    private void CancelLogGame()
    {
        IsLoggingGame  = false;
        _editingGameId = null;
        FormPlayers.Clear();
        OnPropertyChanged(nameof(LogGameTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        LogGameFormClosed?.Invoke();
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
            FormPlayers.Add(new LogGamePlayerViewModel
            {
                PlayerName     = prev?.PlayerName ?? (i == 0 ? "Me" : $"Player {i + 1}"),
                IsMe           = prev?.IsMe ?? (i == 0),
                LinkedDeck     = prev?.LinkedDeck,
                DeckName       = prev?.DeckName ?? string.Empty,
                FinishPosition = i == 0 ? 1 : i + 1
            });
        }
    }

    [RelayCommand]
    private void SaveGame()
    {
        LogGameError = string.Empty;

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
        if (!positions.SequenceEqual(Enumerable.Range(1, FormPlayers.Count)))
        {
            LogGameError = $"Finish positions must be 1 through {FormPlayers.Count} with no duplicates.";
            return;
        }

        var players = FormPlayers.Select(p => new GamePlayer
        {
            PlayerName     = p.PlayerName.Trim(),
            IsMe           = p.IsMe,
            DeckId         = p.LinkedDeck?.Id,
            DeckName       = string.IsNullOrWhiteSpace(p.DeckName) ? null : p.DeckName.Trim(),
            DeckVersionId  = p.LinkedDeck != null ? _db.GetOrCreateCurrentSnapshot(p.LinkedDeck.Id) : null,
            FinishPosition = p.FinishPosition
        }).ToList();

        if (_editingGameId.HasValue)
        {
            _db.UpdateGame(new Game
            {
                Id        = _editingGameId.Value,
                PlayedAt  = _editingGamePlayedAt,
                TurnEnded = TurnEnded > 0 ? TurnEnded : null,
                Notes     = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                Players   = players
            });
        }
        else
        {
            _db.AddGame(new Game
            {
                PlayedAt  = DateTime.UtcNow,
                TurnEnded = TurnEnded > 0 ? TurnEnded : null,
                Notes     = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
                Players   = players
            });
        }

        _editingGameId = null;
        IsLoggingGame  = false;
        FormPlayers.Clear();
        OnPropertyChanged(nameof(LogGameTitle));
        OnPropertyChanged(nameof(SaveButtonText));
        Reload();
        LogGameFormClosed?.Invoke();
    }

    [RelayCommand]
    private void DeleteGame(Game game)
    {
        _db.DeleteGame(game.Id);
        Games.Remove(game);
        RecomputeStats(Games.ToList());
    }

    partial void OnLogGameErrorChanged(string value) =>
        OnPropertyChanged(nameof(HasLogGameError));
}
