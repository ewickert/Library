using Avalonia.Controls;
using Library.Models;
using Library.Services;
using Library.ViewModels;

namespace Library.Views;

public partial class AddEditCardWindow : Window
{
    public AddEditCardWindow(DatabaseService db, ScryfallService scryfall, Card? card = null)
    {
        InitializeComponent();
        var vm = new AddEditCardViewModel(db, scryfall, card);
        vm.Saved += () => Close();
        vm.Cancelled += () => Close();
        DataContext = vm;
    }
}
