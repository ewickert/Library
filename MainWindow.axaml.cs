using Avalonia.Controls;
using Library.Services;
using Library.ViewModels;

namespace Library;

public partial class MainWindow : Window
{
    public DatabaseService DatabaseService { get; }
    public ScryfallService ScryfallService { get; }

    public MainWindow()
    {
        DatabaseService = new DatabaseService();
        ScryfallService = new ScryfallService();
        DataContext = new MainWindowViewModel(DatabaseService, ScryfallService);
        InitializeComponent();
    }
}