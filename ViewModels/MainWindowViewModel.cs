using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Library.Services;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Library.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private int _lastNonCommanderTabIndex;

    public CollectionViewModel Collection { get; }
    public DecksViewModel Decks { get; }
    public BindersViewModel Binders { get; }
    public ShoppingViewModel Shopping { get; }
    public GamesViewModel Games { get; }
    public CommanderLifeViewModel CommanderLife { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isCommanderMode;

    [ObservableProperty]
    private string _importStatus = string.Empty;

    [ObservableProperty]
    private bool _importStatusIsError;

    public bool HasImportStatus => !string.IsNullOrWhiteSpace(ImportStatus);

    partial void OnImportStatusChanged(string value) =>
        OnPropertyChanged(nameof(HasImportStatus));

    // ── Theme selection ────────────────────────────────────────────
    public string[] AvailableThemes => ThemeService.GetAvailableThemes();
    public bool ShowIosThemeRecommendation => OperatingSystem.IsIOS();
    public string IosThemeRecommendationText => "Recommended: Native (iOS)";

    [ObservableProperty]
    private string _currentTheme = ThemeService.Instance.CurrentThemeName;

    partial void OnCurrentThemeChanged(string value) =>
        ThemeService.Instance.Apply(value);

    public MainWindowViewModel(DatabaseService db, ScryfallService scryfall)
    {
        Collection = new CollectionViewModel(db, scryfall);
        Decks = new DecksViewModel(db, scryfall);
        Binders = new BindersViewModel(db, scryfall);
        Shopping = new ShoppingViewModel(db, scryfall, Decks);
        Games = new GamesViewModel(db);
        CommanderLife = new CommanderLifeViewModel();
    }

    // Tab index 3 = Shopping, 4 = Games — reload whenever the user navigates to them
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 3) Shopping.Reload();
        if (value == 4) Games.Reload();
        if (value is >= 0 and <= 4)
            _lastNonCommanderTabIndex = value;
    }

    [RelayCommand]
    private void EnterCommanderMode()
    {
        IsCommanderMode = true;
        KeepScreenOnService.SetEnabled(true);
    }

    [RelayCommand]
    private void ExitCommanderMode()
    {
        IsCommanderMode = false;
        KeepScreenOnService.SetEnabled(false);
        SelectedTabIndex = _lastNonCommanderTabIndex;
    }

    // ── File menu operations ───────────────────────────────────────
    /// <summary>Set by MainWindow to provide StorageProvider access for the file picker.</summary>
    public Func<Task>? RequestImportCsv { get; set; }

    [RelayCommand]
    private async Task ImportCsv() =>
        await (RequestImportCsv?.Invoke() ?? Task.CompletedTask);

    /// <summary>Passthrough to CollectionViewModel so the menu can bind to it directly.</summary>
    public ICommand BackfillMetadataCommand => Collection.BackfillMetadataCommand;

    [RelayCommand]
    private void Quit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime app)
            app.Shutdown();
    }
}
