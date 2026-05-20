using CommunityToolkit.Mvvm.ComponentModel;
using Library.Services;

namespace Library.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public CollectionViewModel Collection { get; }
    public DecksViewModel Decks { get; }
    public BindersViewModel Binders { get; }
    public ShoppingViewModel Shopping { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    public MainWindowViewModel(DatabaseService db, ScryfallService scryfall)
    {
        Collection = new CollectionViewModel(db, scryfall);
        Decks = new DecksViewModel(db, scryfall);
        Binders = new BindersViewModel(db, scryfall);
        Shopping = new ShoppingViewModel(db, scryfall, Decks);
    }
}
