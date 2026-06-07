using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Library.Models;
using Library.ViewModels;
using System.ComponentModel;
using System.Linq;

namespace Library.Views;

public partial class CollectionView : UserControl
{
    private CollectionViewModel? ViewModel => DataContext as CollectionViewModel;
    private CollectionViewModel? _subscribedViewModel;

    // Guards against SelectionChanged re-entrance when SelectedCard changes via binding
    private bool _suppressDataGridSync;

    public CollectionView()
    {
        InitializeComponent();
        GridScrollViewer.AddHandler(PointerWheelChangedEvent, OnGridWheel, RoutingStrategies.Tunnel);
        GridScrollViewer.AddHandler(PointerPressedEvent, OnGridPointerPressed, RoutingStrategies.Tunnel);
        CollectionPanel.AddHandler(DragDrop.DropEvent, OnCollectionDrop, RoutingStrategies.Bubble);
        CollectionPanel.AddHandler(DragDrop.DragOverEvent, OnCollectionDragOver, RoutingStrategies.Bubble);
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        UpdateCompactClass(Bounds.Width);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel.RequestBulkEdit = null;
        }

        if (DataContext is CollectionViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.RequestBulkEdit = OpenBulkEditDialog;
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
        if (e.PropertyName is nameof(CollectionViewModel.IsGridView))
            UpdateCompactClass(Bounds.Width);

        // When SelectedCard changes due to a VM-side update (e.g. ToggleCardSelection),
        // suppress the DataGrid's SelectionChanged from re-firing SetSelectedCards.
        if (e.PropertyName is nameof(CollectionViewModel.SelectedCard))
        {
            _suppressDataGridSync = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _suppressDataGridSync = false);
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdateCompactClass(e.NewSize.Width);

    private void UpdateCompactClass(double width)
    {
        var compact = width < 1050;
        Classes.Set("compact", compact);
        var vm = ViewModel;

        if (RootGrid.ColumnDefinitions.Count > 1)
        {
            RootGrid.ColumnDefinitions[1].Width = compact
                ? new GridLength(0)
                : new GridLength(0.34, GridUnitType.Star);
        }

        var showMainList = vm is { IsGridView: false };
        var showCompactList = compact && showMainList;

        CollectionDataGrid.IsVisible = showMainList && !compact;
        MobileCollectionList.IsVisible = showCompactList;

        CardDetailPane.IsVisible = !compact;
        if (CompactCardDetailPane != null)
            CompactCardDetailPane.IsVisible = compact && vm is { IsGridView: false };
    }

    // ── Grid view: zoom with Ctrl+scroll ─────────────────────────────────────
    private void OnGridWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta) && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        if (ViewModel is { } vm)
        {
            vm.GridZoom = Math.Clamp(vm.GridZoom + (e.Delta.Y > 0 ? 0.1 : -0.1), 0.4, 3.0);
            e.Handled = true;
        }
    }

    // ── Grid view: Cmd/Ctrl+Click toggles multi-selection ────────────────────
    private void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;

        bool isMultiKey = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                          e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (!isMultiKey)
        {
            // Normal click — clear multi-selection; the Button's command selects the card
            vm.ClearMultiSelection();
            return;
        }

        // Ctrl/Cmd held — toggle the clicked card and swallow the event so the
        // Button's SelectCardCommand doesn't also fire
        var slot = FindSlotFromEvent(e);
        if (slot == null) return;

        vm.ToggleCardSelection(slot.Card);
        e.Handled = true;
    }

    private static CardSlotViewModel? FindSlotFromEvent(PointerPressedEventArgs e)
    {
        var element = e.Source as Visual;
        while (element != null)
        {
            if (element is Control { DataContext: CardSlotViewModel slot }) return slot;
            element = element.GetVisualParent();
        }
        return null;
    }

    // ── DataGrid: sync extended selection to VM ───────────────────────────────
    private void OnDataGridSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressDataGridSync) return;
        var vm = ViewModel;
        if (vm == null) return;

        _suppressDataGridSync = true;
        try
        {
            var selected = CollectionDataGrid.SelectedItems.OfType<Card>().ToList();
            vm.SetSelectedCards(selected);
        }
        finally
        {
            _suppressDataGridSync = false;
        }
    }

    // ── Bulk edit dialog ──────────────────────────────────────────────────────
    private void OpenBulkEditDialog(IReadOnlyList<Card> cards)
    {
        var vm = ViewModel;
        var win = TopLevel.GetTopLevel(this) as MainWindow;
        if (win == null || vm == null) return;

        var dialog = new BulkEditCardWindow(win.DatabaseService, win.ScryfallService, cards);
        dialog.ShowDialog(win).ContinueWith(_ =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => vm.LoadCards()));
    }

    // ── Drag and drop ─────────────────────────────────────────────────────────
    private void OnCollectionDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.GetFiles()?.Any(f =>
            f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
            f.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) == true)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnCollectionDrop(object? sender, DragEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null) return;

        if (e.Data.Contains(BrowseDragFormats.ScryfallResult) &&
            e.Data.Get(BrowseDragFormats.ScryfallResult) is ScryfallResultViewModel srvm)
        {
            await srvm.AddToCollectionCommand.ExecuteAsync(null);
        }
        else if (e.Data.Contains(BrowseDragFormats.MtgJsonCard) &&
                 e.Data.Get(BrowseDragFormats.MtgJsonCard) is MtgJson.Models.DeckCard dc)
        {
            vm.AddMtgJsonCardToCollection(dc);
        }
        else if (e.Data.GetFiles() is { } droppedFiles &&
                 TopLevel.GetTopLevel(this) is MainWindow win)
        {
            var csvPaths = droppedFiles
                .Select(f => f.TryGetLocalPath())
                .OfType<string>()
                .Where(p => p.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                            p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var path in csvPaths)
                await win.ImportCsvFromPathAsync(path);
        }
    }

    // ── Search help ───────────────────────────────────────────────────────────
    private async void OnSearchHelpClick(object? sender, RoutedEventArgs e)
    {
        var win = new SearchHelpWindow();
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
        {
            await win.ShowDialog(owner);
            return;
        }
        win.Show();
    }

    // ── Add / Edit card ───────────────────────────────────────────────────────
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

    private void OnBulkAddClick(object? sender, RoutedEventArgs e)
    {
        var vm = ViewModel;
        var win = TopLevel.GetTopLevel(this) as MainWindow;
        if (vm == null || win == null) return;
        var dialog = new BulkAddWindow(win.DatabaseService, win.ScryfallService);
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
