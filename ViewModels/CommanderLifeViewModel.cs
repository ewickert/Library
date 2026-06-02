using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia;
using Avalonia.Media;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Library.ViewModels;

public partial class CommanderLifeViewModel : ObservableObject
{
    public int[] PlayerCountOptions { get; } = [1, 2, 3, 4, 5, 6];
    public int[] StartingLifeOptions { get; } = [20, 30, 40];
    public int[] RollableDiceSides { get; } = [4, 6, 8, 10, 12, 20];

    public ObservableCollection<CommanderPlayerViewModel> Players { get; } = new(
    [
        new CommanderPlayerViewModel("Player 1", "#2C3E50"),
        new CommanderPlayerViewModel("Player 2", "#7D3C98"),
        new CommanderPlayerViewModel("Player 3", "#1F618D"),
        new CommanderPlayerViewModel("Player 4", "#117A65"),
        new CommanderPlayerViewModel("Player 5", "#9A7D0A"),
        new CommanderPlayerViewModel("Player 6", "#922B21"),
    ]);

    [ObservableProperty] private int _selectedPlayerCount = 4;
    [ObservableProperty] private int _selectedStartingLife = 40;
    [ObservableProperty] private bool _enableCommanderDamage;
    [ObservableProperty] private bool _isHighRollAnimating;
    [ObservableProperty] private bool _isAwaitingRollAcknowledge;

    public bool Show1Players => SelectedPlayerCount == 1;
    public bool Show2Players => SelectedPlayerCount == 2;
    public bool Show3Players => SelectedPlayerCount == 3;
    public bool Show4Players => SelectedPlayerCount == 4;
    public bool Show5Players => SelectedPlayerCount == 5;
    public bool Show6Players => SelectedPlayerCount == 6;

    public IReadOnlyList<CommanderPlayerViewModel> ActivePlayers =>
        Players.Take(SelectedPlayerCount).ToList();

    public string[] CommonCounterTypes { get; } =
    [
        "+1/+1",
        "-1/-1",
        "Poison",
        "Energy",
        "Experience",
        "Treasure",
        "Food",
        "Clue",
        "Gold",
    ];

    public CommanderLifeViewModel()
    {
        foreach (var player in Players)
        {
            player.PropertyChanged += OnPlayerPropertyChanged;
            player.MonarchClaimRequested = OnMonarchClaimRequested;
        }

        SyncActivePlayerRelations();
    }

    partial void OnSelectedPlayerCountChanged(int value)
    {
        OnPropertyChanged(nameof(Show1Players));
        OnPropertyChanged(nameof(Show2Players));
        OnPropertyChanged(nameof(Show3Players));
        OnPropertyChanged(nameof(Show4Players));
        OnPropertyChanged(nameof(Show5Players));
        OnPropertyChanged(nameof(Show6Players));
        OnPropertyChanged(nameof(ActivePlayers));
        SyncActivePlayerRelations();
    }

    partial void OnEnableCommanderDamageChanged(bool value)
    {
        foreach (var player in Players)
            player.TrackCommanderDamage = value;
    }

    [RelayCommand]
    private void ApplyStartingLifeToAll()
    {
        foreach (var player in Players.Take(SelectedPlayerCount))
            player.Life = SelectedStartingLife;
    }

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var player in Players.Take(SelectedPlayerCount))
        {
            player.Life = SelectedStartingLife;
            player.ResetCommanderDamage();
            player.ResetCounters();
            player.ResetStatuses();
            player.ShowRollDisplay = false;
            player.RollDisplayValue = 0;
            player.IsHighRollWinner = false;
        }

        IsHighRollAnimating = false;
        IsAwaitingRollAcknowledge = false;
    }

    public async Task RollDieForPlayerAsync(CommanderPlayerViewModel player, int sides = 20)
    {
        if (IsHighRollAnimating || IsAwaitingRollAcknowledge)
            return;

        ClearWinnerHighlights();
        IsHighRollAnimating = true;
        player.ShowRollDisplay = true;

        for (var step = 0; step < 12; step++)
        {
            player.RollDisplaySides = sides;
            player.RollDisplayValue = Random.Shared.Next(1, sides + 1);
            await Task.Delay(90);
        }

        var roll = Random.Shared.Next(1, sides + 1);
        player.RollDisplaySides = sides;
        player.RollDisplayValue = roll;
        IsHighRollAnimating = false;
        IsAwaitingRollAcknowledge = true;
    }

    public async Task RollAllPlayersAsync(int sides)
    {
        if (IsHighRollAnimating || IsAwaitingRollAcknowledge)
            return;

        var activePlayers = ActivePlayers;
        if (activePlayers.Count == 0)
        {
            return;
        }

        IsHighRollAnimating = true;
        ClearWinnerHighlights();

        foreach (var player in activePlayers)
            player.ShowRollDisplay = true;

        for (var step = 0; step < 14; step++)
        {
            foreach (var player in activePlayers)
            {
                player.RollDisplaySides = sides;
                player.RollDisplayValue = Random.Shared.Next(1, sides + 1);
            }

            await Task.Delay(90);
        }

        foreach (var player in activePlayers)
        {
            var roll = Random.Shared.Next(1, sides + 1);
            player.RollDisplaySides = sides;
            player.RollDisplayValue = roll;
        }

        IsHighRollAnimating = false;
        IsAwaitingRollAcknowledge = true;
    }

    public async Task RollHighForAllPlayersAsync(int sides = 20)
    {
        if (IsHighRollAnimating || IsAwaitingRollAcknowledge)
            return;

        var activePlayers = ActivePlayers;
        if (activePlayers.Count == 0)
        {
            return;
        }

        IsHighRollAnimating = true;
        ClearWinnerHighlights();

        foreach (var player in activePlayers)
            player.ShowRollDisplay = true;

        for (var step = 0; step < 14; step++)
        {
            foreach (var player in activePlayers)
            {
                player.RollDisplaySides = sides;
                player.RollDisplayValue = Random.Shared.Next(1, sides + 1);
            }

            await Task.Delay(90);
        }

        var rolls = activePlayers
            .Select(player => new { Player = player, Roll = Random.Shared.Next(1, sides + 1) })
            .ToList();

        foreach (var entry in rolls)
        {
            entry.Player.RollDisplaySides = sides;
            entry.Player.RollDisplayValue = entry.Roll;
        }

        var highest = rolls.Max(entry => entry.Roll);
        var winnerEntries = rolls
            .Where(entry => entry.Roll == highest)
            .ToList();

        foreach (var winner in winnerEntries)
            winner.Player.IsHighRollWinner = true;

        var winners = winnerEntries.Select(entry => entry.Player.Name).ToList();

        IsHighRollAnimating = false;
        IsAwaitingRollAcknowledge = true;
    }

    [RelayCommand]
    private void AcknowledgeRoll()
    {
        foreach (var player in ActivePlayers)
        {
            player.ShowRollDisplay = false;
            player.RollDisplayValue = 0;
            player.IsHighRollWinner = false;
        }

        IsAwaitingRollAcknowledge = false;
    }

    private void ClearWinnerHighlights()
    {
        foreach (var player in ActivePlayers)
            player.IsHighRollWinner = false;
    }

    private void SyncActivePlayerRelations()
    {
        var active = ActivePlayers;
        foreach (var player in active)
            player.SyncCommanderOpponents(active);
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommanderPlayerViewModel.Name) ||
            e.PropertyName == nameof(CommanderPlayerViewModel.Background))
        {
            SyncActivePlayerRelations();
        }
    }

    private void OnMonarchClaimRequested(CommanderPlayerViewModel claimant)
    {
        foreach (var player in Players)
            player.IsMonarch = ReferenceEquals(player, claimant);
    }

}

public partial class CommanderPlayerViewModel : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private string _background = "#2C3E50";
    [ObservableProperty] private int _life = 40;
    [ObservableProperty] private bool _trackCommanderDamage;
    [ObservableProperty] private bool _showRollDisplay;
    [ObservableProperty] private int _rollDisplayValue;
    [ObservableProperty] private int _rollDisplaySides = 20;
    [ObservableProperty] private bool _isHighRollWinner;
    [ObservableProperty] private bool _isCounterPickerOpen;
    [ObservableProperty] private bool _isStatusPickerOpen;
    [ObservableProperty] private bool _isCommanderDamagePanelOpen;
    [ObservableProperty] private bool _isMonarch;
    [ObservableProperty] private bool _isBlessingOfTheCity;

    public string RollDisplayText => RollDisplayValue.ToString();
    public string RollDieGeometry => RollDisplaySides switch
    {
        4 => "M32 6 L56 52 H8 Z",
        6 => "M10 10 H54 V54 H10 Z",
        8 => "M32 6 L50 22 L50 42 L32 58 L14 42 L14 22 Z",
        10 => "M32 4 L50 12 L58 30 L50 52 L32 60 L14 52 L6 30 L14 12 Z",
        12 => "M32 4 L45 8 L54 18 L56 32 L54 46 L45 56 L32 60 L19 56 L10 46 L8 32 L10 18 L19 8 Z",
        _ => "M32 4 L56 18 L56 46 L32 60 L8 46 L8 18 Z",
    };
    public string RollDieInnerGeometry => RollDisplaySides switch
    {
        4 => "M32 6 L32 52 M20 30 L44 30",
        6 => "M32 10 L32 54 M10 32 L54 32",
        8 => "M32 6 L32 58 M14 22 L50 42 M50 22 L14 42",
        10 => "M32 4 L32 60 M14 12 L50 52 M50 12 L14 52",
        12 => "M32 4 L32 60 M19 8 L45 56 M45 8 L19 56",
        _ => "M32 4 L20 18 L44 18 M8 18 L32 32 L56 18 M8 46 L32 32 L56 46 M20 46 L44 46 L32 60",
    };
    public bool ShowLifeDisplay => !ShowRollDisplay;
    public bool ShowCommanderDamagePanel => IsCommanderDamagePanelOpen;
    public bool HasAnyStatus => IsMonarch || IsBlessingOfTheCity;
    public IBrush WinnerBorderBrush => IsHighRollWinner ? Brushes.Gold : Brushes.Transparent;
    public Thickness WinnerBorderThickness => IsHighRollWinner ? new Thickness(5) : new Thickness(0);

    public ObservableCollection<CommanderDamageEntryViewModel> CommanderDamageByOpponent { get; } = new();
    public ObservableCollection<PlayerCounterViewModel> Counters { get; } = new();
    public bool HasCounters => Counters.Count > 0;
    public string[] CommonCounterTypes { get; } =
    [
        "+1/+1",
        "-1/-1",
        "Poison",
        "Energy",
        "Experience",
        "Treasure",
        "Food",
        "Clue",
        "Gold",
    ];

    public string[] CommonStatusTypes { get; } =
    [
        "Monarch",
        "Blessing Of The City",
    ];

    public Action<CommanderPlayerViewModel>? MonarchClaimRequested { get; set; }

    public CommanderPlayerViewModel(string name, string background)
    {
        _name = name;
        _background = background;
        Counters.CollectionChanged += OnCountersCollectionChanged;
    }

    [RelayCommand]
    private void IncrementLife() => Life += 1;

    [RelayCommand]
    private void DecrementLife() => Life -= 1;

    public void SyncCommanderOpponents(IReadOnlyList<CommanderPlayerViewModel> activePlayers)
    {
        var existing = CommanderDamageByOpponent.ToDictionary(x => x.OpponentName, x => x);
        var opponents = activePlayers.Where(p => !ReferenceEquals(p, this)).ToList();

        for (var i = CommanderDamageByOpponent.Count - 1; i >= 0; i--)
        {
            var entry = CommanderDamageByOpponent[i];
            if (!opponents.Any(p => p.Name == entry.OpponentName))
                CommanderDamageByOpponent.RemoveAt(i);
        }

        foreach (var opponent in opponents)
        {
            if (existing.TryGetValue(opponent.Name, out var current))
            {
                current.OpponentColor = opponent.Background;
                continue;
            }

            CommanderDamageByOpponent.Add(new CommanderDamageEntryViewModel(opponent.Name, opponent.Background));
        }
    }

    public void AddCounter(string name)
    {
        var existing = Counters.FirstOrDefault(c => c.Name == name);
        if (existing is not null)
        {
            existing.Value += 1;
            return;
        }

        Counters.Add(new PlayerCounterViewModel(name));
    }

    [RelayCommand]
    private void OpenCounterPicker() => IsCounterPickerOpen = true;

    [RelayCommand]
    private void CloseCounterPicker() => IsCounterPickerOpen = false;

    [RelayCommand]
    private void AddCounterType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return;

        AddCounter(type);
        IsCounterPickerOpen = false;
    }

    [RelayCommand]
    private void OpenStatusPicker() => IsStatusPickerOpen = true;

    [RelayCommand]
    private void CloseStatusPicker() => IsStatusPickerOpen = false;

    [RelayCommand]
    private void ToggleCommanderDamagePanel() => IsCommanderDamagePanelOpen = !IsCommanderDamagePanelOpen;

    [RelayCommand]
    private void AddStatusType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
            return;

        if (type == "Monarch")
        {
            MonarchClaimRequested?.Invoke(this);
            IsMonarch = true;
        }
        else if (type == "Blessing Of The City")
        {
            IsBlessingOfTheCity = !IsBlessingOfTheCity;
        }

        IsStatusPickerOpen = false;
    }

    [RelayCommand]
    private void RemoveCounter(PlayerCounterViewModel? counter)
    {
        if (counter is null)
            return;

        Counters.Remove(counter);
    }

    public void ResetCommanderDamage()
    {
        foreach (var entry in CommanderDamageByOpponent)
            entry.Value = 0;

        IsCommanderDamagePanelOpen = false;
    }

    public void ResetCounters()
    {
        foreach (var counter in Counters)
            counter.Value = 0;

        IsCounterPickerOpen = false;
    }

    public void ResetStatuses()
    {
        IsMonarch = false;
        IsBlessingOfTheCity = false;
        IsStatusPickerOpen = false;
    }

    partial void OnShowRollDisplayChanged(bool value) => OnPropertyChanged(nameof(ShowLifeDisplay));
    partial void OnTrackCommanderDamageChanged(bool value) => OnPropertyChanged(nameof(ShowCommanderDamagePanel));
    partial void OnIsCommanderDamagePanelOpenChanged(bool value) => OnPropertyChanged(nameof(ShowCommanderDamagePanel));
    partial void OnRollDisplayValueChanged(int value) => OnPropertyChanged(nameof(RollDisplayText));
    partial void OnRollDisplaySidesChanged(int value)
    {
        OnPropertyChanged(nameof(RollDieGeometry));
        OnPropertyChanged(nameof(RollDieInnerGeometry));
    }
    partial void OnIsHighRollWinnerChanged(bool value)
    {
        OnPropertyChanged(nameof(WinnerBorderBrush));
        OnPropertyChanged(nameof(WinnerBorderThickness));
    }

    partial void OnIsMonarchChanged(bool value) => OnPropertyChanged(nameof(HasAnyStatus));
    partial void OnIsBlessingOfTheCityChanged(bool value) => OnPropertyChanged(nameof(HasAnyStatus));

    private void OnCountersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasCounters));
    }
}

public partial class CommanderDamageEntryViewModel : ObservableObject
{
    [ObservableProperty] private string _opponentName;
    [ObservableProperty] private string _opponentColor;
    [ObservableProperty] private int _value;

    public CommanderDamageEntryViewModel(string opponentName, string opponentColor)
    {
        _opponentName = opponentName;
        _opponentColor = opponentColor;
    }

    [RelayCommand]
    private void Increment() => Value += 1;

    [RelayCommand]
    private void Decrement()
    {
        if (Value > 0)
            Value -= 1;
    }
}

public partial class PlayerCounterViewModel : ObservableObject
{
    [ObservableProperty] private string _name;
    [ObservableProperty] private int _value;

    public string IconGeometry => Name switch
    {
        "+1/+1" => "M32 8 L52 24 L44 24 L44 42 L20 42 L20 24 L12 24 Z",
        "-1/-1" => "M12 28 H52 V36 H12 Z",
        "Poison" => "M32 8 C24 20 18 26 18 36 C18 46 24 54 32 54 C40 54 46 46 46 36 C46 26 40 20 32 8 Z",
        "Energy" => "M36 8 L18 34 H30 L24 56 L46 28 H34 Z",
        "Experience" => "M32 8 L38 24 L56 24 L42 34 L48 52 L32 40 L16 52 L22 34 L8 24 L26 24 Z",
        "Treasure" => "M10 22 H54 V46 H10 Z M14 18 H50 V24 H14 Z M22 30 H42 V38 H22 Z",
        "Food" => "M32 12 C42 12 50 20 50 30 C50 42 42 52 32 52 C22 52 14 42 14 30 C14 20 22 12 32 12 Z",
        "Clue" => "M26 14 C34 14 40 20 40 28 C40 36 34 42 26 42 C18 42 12 36 12 28 C12 20 18 14 26 14 Z M36 38 L50 52",
        _ => "M32 10 C44 10 54 20 54 32 C54 44 44 54 32 54 C20 54 10 44 10 32 C10 20 20 10 32 10 Z",
    };

    public string IconInnerGeometry => Name switch
    {
        "+1/+1" => "M28 24 H36 V42 H28 Z M20 30 H44 V36 H20 Z",
        "-1/-1" => "",
        "Poison" => "M24 28 C24 24 27 22 32 22 C37 22 40 24 40 28 C40 30 39 32 37 33 C36 30 34 28 32 28 C30 28 28 30 27 33 C25 32 24 30 24 28 Z M26 38 C26 34 29 32 32 32 C35 32 38 34 38 38 C38 40 37 42 35 43 C34 40 33 39 32 39 C31 39 30 40 29 43 C27 42 26 40 26 38 Z",
        "Energy" => "",
        "Experience" => "M32 18 L35 26 L44 26 L37 31 L40 40 L32 34 L24 40 L27 31 L20 26 L29 26 Z",
        "Treasure" => "M14 26 H50 M22 34 H42",
        "Food" => "M32 10 C34 7 38 6 42 8",
        "Clue" => "",
        _ => "M20 32 H44",
    };

    public IBrush IconFill => Name switch
    {
        "+1/+1" => new SolidColorBrush(Color.Parse("#2E7D32")),
        "-1/-1" => new SolidColorBrush(Color.Parse("#8E2430")),
        "Poison" => new SolidColorBrush(Color.Parse("#4A148C")),
        "Energy" => new SolidColorBrush(Color.Parse("#1565C0")),
        "Experience" => new SolidColorBrush(Color.Parse("#455A64")),
        "Treasure" => new SolidColorBrush(Color.Parse("#6D4C41")),
        "Food" => new SolidColorBrush(Color.Parse("#2E7D32")),
        "Clue" => new SolidColorBrush(Color.Parse("#37474F")),
        _ => new SolidColorBrush(Color.Parse("#B8860B")),
    };

    public PlayerCounterViewModel(string name)
    {
        _name = name;
    }

    [RelayCommand]
    private void Increment() => Value += 1;

    [RelayCommand]
    private void Decrement()
    {
        if (Value > 0)
            Value -= 1;
    }
}
