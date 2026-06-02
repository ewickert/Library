using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Library.Models;
using Library.ViewModels;
using System.ComponentModel;

namespace Library.Views;

public partial class CollectionView : UserControl
{
    private CollectionViewModel? ViewModel => DataContext as CollectionViewModel;
    private CollectionViewModel? _subscribedViewModel;

    public CollectionView()
    {
        InitializeComponent();
        GridScrollViewer.AddHandler(PointerWheelChangedEvent, OnGridWheel, RoutingStrategies.Tunnel);
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        UpdateCompactClass(Bounds.Width);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (DataContext is CollectionViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel = vm;
        }
        else
        {
            _subscribedViewModel = null;
        }

        UpdateCompactClass(Bounds.Width);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CollectionViewModel.IsGridView) or nameof(CollectionViewModel.IsExternalSearch))
            UpdateCompactClass(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdateCompactClass(e.NewSize.Width);

    private void UpdateCompactClass(double width)
    {
        // iPad portrait + split view can become too narrow for side-by-side details.
        var compact = width < 1050;
        Classes.Set("compact", compact);
        var vm = ViewModel;

        if (RootGrid.ColumnDefinitions.Count > 1)
        {
            RootGrid.ColumnDefinitions[1].Width = compact
                ? new GridLength(0)
                : new GridLength(0.34, GridUnitType.Star);
        }

        var showMainList = vm is { IsGridView: false, IsExternalSearch: false };
        var showCompactList = compact && showMainList;

        CollectionDataGrid.IsVisible = showMainList && !compact;
        MobileCollectionList.IsVisible = showCompactList;

        CardDetailPane.IsVisible = !compact;
        if (CompactCardDetailPane != null)
        {
            CompactCardDetailPane.IsVisible = compact && vm is { IsGridView: false, IsExternalSearch: false };
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
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
        {
            await win.ShowDialog(owner);
            return;
        }

        // Mobile single-view lifetimes do not always have a window owner available.
        // Fall back to a modeless window so the help content still opens instead of crashing.
        win.Show();
    }

    private void OnAddCardClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;
        var win = TopLevel.GetTopLevel(this) as MainWindow;
        if (win == null) return;
        var dialog = new AddEditCardWindow(win.DatabaseService, win.ScryfallService);
        dialog.ShowDialog(win).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.LoadCards()));
    }

    private void OnEditCardClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        if (vm?.SelectedCard == null) return;
        var win = TopLevel.GetTopLevel(this) as MainWindow;
        if (win == null) return;
        var dialog = new AddEditCardWindow(win.DatabaseService, win.ScryfallService, vm.SelectedCard);
        dialog.ShowDialog(win).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.LoadCards()));
    }

    private void OnCollectionRowEditEnded(object? sender, DataGridRowEditEndedEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.DataContext is not Card card) return;
        var win = TopLevel.GetTopLevel(this) as MainWindow;
        win?.DatabaseService.UpdateCardPurchasePrice(card.Id, card.PurchasePrice, card.PurchasePriceCurrency);
    }
}
