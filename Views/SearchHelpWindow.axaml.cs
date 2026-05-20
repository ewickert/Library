using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Library.Views;

public partial class SearchHelpWindow : Window
{
    public SearchHelpWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
