using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Library.ViewModels;
using System.ComponentModel;

namespace Library.Views;

public partial class BindersView : UserControl
{
    private const double CompactBreakpoint = 960;
    private BindersViewModel? ViewModel => DataContext as BindersViewModel;
    private BindersViewModel? _subscribedVm;
    private bool _isCompact;

    public BindersView()
    {
        InitializeComponent();
        GridScrollViewer.AddHandler(PointerWheelChangedEvent, OnGridWheel, RoutingStrategies.Tunnel);
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        UpdateResponsiveLayout(Bounds.Width);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;

        _subscribedVm = DataContext as BindersViewModel;
        if (_subscribedVm != null)
            _subscribedVm.PropertyChanged += OnViewModelPropertyChanged;

        UpdateResponsiveLayout(Bounds.Width);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BindersViewModel.SelectedBinder))
            UpdateResponsiveLayout(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout(e.NewSize.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        _isCompact = width < CompactBreakpoint;
        var vm = ViewModel;

        if (!_isCompact)
        {
            RootGrid.ColumnDefinitions[0].Width = new GridLength(0.26, GridUnitType.Star);
            RootGrid.ColumnDefinitions[0].MinWidth = 150;
            RootGrid.ColumnDefinitions[0].MaxWidth = 280;
            RootGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            RootGrid.ColumnDefinitions[1].MinWidth = 260;
            CompactBackToBindersButton.IsVisible = false;
            return;
        }

        var hasBinder = vm?.SelectedBinder != null;
        if (hasBinder)
        {
            RootGrid.ColumnDefinitions[0].Width = new GridLength(0);
            RootGrid.ColumnDefinitions[0].MinWidth = 0;
            RootGrid.ColumnDefinitions[0].MaxWidth = double.PositiveInfinity;
            RootGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
            RootGrid.ColumnDefinitions[1].MinWidth = 150;
            CompactBackToBindersButton.IsVisible = true;
        }
        else
        {
            RootGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            RootGrid.ColumnDefinitions[0].MinWidth = 120;
            RootGrid.ColumnDefinitions[0].MaxWidth = double.PositiveInfinity;
            RootGrid.ColumnDefinitions[1].Width = new GridLength(0);
            RootGrid.ColumnDefinitions[1].MinWidth = 0;
            CompactBackToBindersButton.IsVisible = false;
        }
    }

    private void OnGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (ViewModel is { } vm)
        {
            vm.GridZoom = Math.Clamp(vm.GridZoom + (e.Delta.Y > 0 ? 0.1 : -0.1), 0.4, 3.0);
            e.Handled = true;
        }
    }

    private async void OnSearchHelpClick(object? sender, RoutedEventArgs e)
    {
        var win = new SearchHelpWindow();
        await win.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private void OnCompactBackToBindersClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
            ViewModel.SelectedBinder = null;
    }
}
