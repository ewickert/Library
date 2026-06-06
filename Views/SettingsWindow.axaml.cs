using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Library.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close();
}
