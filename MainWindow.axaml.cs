using Avalonia.Controls;
using Library.Services;
using Library.ViewModels;
using Library.Views;

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

        // Register the global printing-picker delegate so ViewModels can show the dialog without
        // taking a dependency on Views.
        ScryfallResultViewModel.GlobalPickPrintingAsync = async data =>
        {
            var picker = new PrintingPickerWindow(data, ScryfallService);
            return await picker.ShowDialog<ScryfallCardData?>(this);
        };
    }
}