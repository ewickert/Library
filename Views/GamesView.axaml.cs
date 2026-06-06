using Avalonia.Controls;
using Library.ViewModels;

namespace Library.Views;

public partial class GamesView : UserControl
{
    private GamesViewModel? _vm;

    public GamesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
            _vm.LogGameDialogRequested -= ShowLogGameDialog;

        _vm = DataContext as GamesViewModel;

        if (_vm != null)
            _vm.LogGameDialogRequested += ShowLogGameDialog;
    }

    private async void ShowLogGameDialog()
    {
        if (_vm == null) return;
        var parent = TopLevel.GetTopLevel(this) as Window;
        if (parent == null) return;
        var dialog = new LogGameWindow(_vm);
        await dialog.ShowDialog(parent);
    }
}
