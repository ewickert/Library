using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia;
using Library.ViewModels;
using System.ComponentModel;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace Library.Views;

public partial class CommanderLifeView : UserControl
{
    private CommanderLifeViewModel? _subscribedVm;

    public CommanderLifeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;

        _subscribedVm = DataContext as CommanderLifeViewModel;
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged += OnVmPropertyChanged;

        RebuildPlayerRegions();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommanderLifeViewModel.SelectedPlayerCount) ||
            e.PropertyName == nameof(CommanderLifeViewModel.ActivePlayers))
        {
            RebuildPlayerRegions();
        }
    }

    private void RebuildPlayerRegions()
    {
        PlayerRegionsHost.RowDefinitions.Clear();
        PlayerRegionsHost.ColumnDefinitions.Clear();
        PlayerRegionsHost.Children.Clear();

        if (DataContext is not CommanderLifeViewModel vm)
            return;

        var players = vm.ActivePlayers;
        var (columns, rows) = GetGridDimensions(players.Count);

        for (var c = 0; c < columns; c++)
            PlayerRegionsHost.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        for (var r = 0; r < rows; r++)
            PlayerRegionsHost.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        for (var i = 0; i < players.Count; i++)
        {
            var view = new CommanderLifePlayerView
            {
                DataContext = players[i],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(view, i % columns);
            Grid.SetRow(view, i / columns);
            PlayerRegionsHost.Children.Add(view);
        }
    }

    private static (int Columns, int Rows) GetGridDimensions(int playerCount)
    {
        return playerCount switch
        {
            <= 1 => (1, 1),
            2 => (2, 1),
            3 => (3, 1),
            4 => (2, 2),
            _ => (3, 2),
        };
    }

    private void OnCenterRollButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control target || DataContext is not CommanderLifeViewModel vm)
            return;

        var menuItems = new List<object>();

        var highRollRoot = new MenuItem
        {
            Header = "High Roll",
            Icon = CreateMenuIcon(20),
        };
        var highRollSidesItems = new List<object>();
        foreach (var sides in vm.RollableDiceSides)
        {
            var dieSides = sides;
            var highRollSidesItem = new MenuItem { Header = $"d{dieSides}", Icon = CreateMenuIcon(dieSides) };
            highRollSidesItem.Click += async (_, _) => await vm.RollHighForAllPlayersAsync(dieSides);
            highRollSidesItems.Add(highRollSidesItem);
        }
        highRollRoot.ItemsSource = highRollSidesItems;
        menuItems.Add(highRollRoot);

        menuItems.Add(new Separator());

        foreach (var sides in vm.RollableDiceSides)
        {
            var dieSides = sides;
            var rollDieRoot = new MenuItem
            {
                Header = $"Roll d{dieSides}",
                Icon = CreateMenuIcon(dieSides),
            };

            var rollTargets = new List<object>();
            foreach (var player in vm.ActivePlayers)
            {
                var selectedPlayer = player;
                var playerItem = new MenuItem { Header = selectedPlayer.Name, Icon = CreateMenuIcon(dieSides) };
                playerItem.Click += async (_, _) => await vm.RollDieForPlayerAsync(selectedPlayer, dieSides);
                rollTargets.Add(playerItem);
            }

            rollTargets.Add(new Separator());
            var allItem = new MenuItem { Header = "All", Icon = CreateMenuIcon(dieSides) };
            allItem.Click += async (_, _) => await vm.RollAllPlayersAsync(dieSides);
            rollTargets.Add(allItem);

            rollDieRoot.ItemsSource = rollTargets;
            menuItems.Add(rollDieRoot);
        }

        var menu = new ContextMenu { ItemsSource = menuItems };
        menu.Open(target);
    }

    private void OnAcknowledgeOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not CommanderLifeViewModel vm || !vm.IsAwaitingRollAcknowledge)
            return;

        vm.AcknowledgeRollCommand.Execute(null);
        e.Handled = true;
    }

    private static Control CreateMenuIcon(int sides)
    {
        var canvas = new Canvas
        {
            Width = 16,
            Height = 16,
        };

        var diePath = new ShapePath
        {
            Data = Geometry.Parse(GetDieGeometry(sides)),
            Stretch = Stretch.None,
            Stroke = Brushes.White,
            StrokeThickness = 1.2,
            Fill = new SolidColorBrush(Color.Parse("#2F203047")),
        };

        var dieInnerPath = new ShapePath
        {
            Data = Geometry.Parse(GetDieInnerGeometry(sides)),
            Stretch = Stretch.None,
            Stroke = new SolidColorBrush(Color.Parse("#A6FFFFFF")),
            StrokeThickness = 0.7,
        };

        var badge = new Border
        {
            Width = sides >= 10 ? 12 : 10,
            Height = 8,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.Parse("#D0101722")),
            BorderBrush = new SolidColorBrush(Color.Parse("#99FFFFFF")),
            BorderThickness = new Thickness(0.8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Canvas.SetLeft(badge, (16 - badge.Width) / 2);
        Canvas.SetTop(badge, (16 - badge.Height) / 2);

        var number = new Label
        {
            Content = sides.ToString(),
            FontSize = sides >= 10 ? 6.2 : 7,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Width = sides >= 10 ? 12 : 10,
            Height = 8,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        Canvas.SetLeft(number, (16 - number.Width) / 2);
        Canvas.SetTop(number, (16 - number.Height) / 2);

        canvas.Children.Add(diePath);
        canvas.Children.Add(dieInnerPath);
        canvas.Children.Add(badge);
        canvas.Children.Add(number);
        return canvas;
    }

    private static string GetDieGeometry(int sides)
    {
        return sides switch
        {
            4 => "M8 1.2 L14.2 14 H1.8 Z",
            6 => "M2 2 H14 V14 H2 Z",
            8 => "M8 1 L13.5 5 L13.5 11 L8 15 L2.5 11 L2.5 5 Z",
            10 => "M8 1 L13.5 3.5 L15.5 8 L13.5 12.5 L8 15 L2.5 12.5 L0.5 8 L2.5 3.5 Z",
            12 => "M8 1 L11.2 2 L13.5 4.2 L14 8 L13.5 11.8 L11.2 14 L8 15 L4.8 14 L2.5 11.8 L2 8 L2.5 4.2 L4.8 2 Z",
            _ => "M8 1 L14.5 4.5 L14.5 11.5 L8 15 L1.5 11.5 L1.5 4.5 Z",
        };
    }

    private static string GetDieInnerGeometry(int sides)
    {
        return sides switch
        {
            4 => "M8 1.2 L8 14 M5 8.3 L11 8.3",
            6 => "M8 2 L8 14 M2 8 L14 8",
            8 => "M8 1 L8 15 M2.5 5 L13.5 11 M13.5 5 L2.5 11",
            10 => "M8 1 L8 15 M2.5 3.5 L13.5 12.5 M13.5 3.5 L2.5 12.5",
            12 => "M8 1 L8 15 M4.8 2 L11.2 14 M11.2 2 L4.8 14",
            _ => "M8 1 L4.8 4.5 L11.2 4.5 M1.5 4.5 L8 8 L14.5 4.5 M1.5 11.5 L8 8 L14.5 11.5 M4.8 11.5 L11.2 11.5 L8 15",
        };
    }
}
