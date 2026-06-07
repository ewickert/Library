using Avalonia.Controls;
using Library.Models;
using Library.Services;
using Library.ViewModels;

namespace Library.Views;

public partial class BulkEditCardWindow : Window
{
    public BulkEditCardWindow(DatabaseService db, ScryfallService scryfall, IReadOnlyList<Card> cards)
    {
        InitializeComponent();
        var vm = new BulkEditCardViewModel(db, scryfall, cards);
        vm.Saved     += () => Close();
        vm.Cancelled += () => Close();
        DataContext = vm;
    }
}
