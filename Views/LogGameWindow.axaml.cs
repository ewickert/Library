using Avalonia.Controls;
using Library.ViewModels;

namespace Library.Views;

public partial class LogGameWindow : Window
{
    private readonly GamesViewModel _vm;

    public LogGameWindow(GamesViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.LogGameFormClosed += Close;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _vm.LogGameFormClosed -= Close;
    }
}
