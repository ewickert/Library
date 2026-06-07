using Avalonia.Controls;
using Library.Models;
using Library.Services;
using Library.ViewModels;

namespace Library.Views;

public partial class BulkAddWindow : Window
{
    public BulkAddWindow(DatabaseService db, ScryfallService scryfall, int? deckId = null)
    {
        InitializeComponent();
        var vm = new BulkAddViewModel(db, scryfall, deckId);
        vm.Done      += Close;
        vm.Cancelled += Close;
        DataContext = vm;
    }
}
