using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Library.ViewModels;
using System.ComponentModel;

namespace Library.Views;

public partial class MainView : UserControl
{
    private const double MobileBreakpoint = 600;

    private bool _isMobile;
    private MainWindowViewModel? _vm;

    public MainView()
    {
        InitializeComponent();

        _isMobile = OperatingSystem.IsIOS() || OperatingSystem.IsAndroid();
        var insetsManager = TopLevel.GetTopLevel(this)?.InsetsManager;
        if (insetsManager != null)
        {
            insetsManager.DisplayEdgeToEdge = true;
            insetsManager.DisplayEdgeToEdgePreference = true;
        }
        ApplyLayout();

        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MainWindowViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateCommanderLayer();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsCommanderMode))
        {
            UpdateCommanderLayer();
            if (_vm?.IsCommanderMode == true)
                CloseHamburger();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var wasMobile = _isMobile;
        _isMobile = e.NewSize.Width < MobileBreakpoint;
        if (wasMobile != _isMobile)
        {
            ApplyLayout();
            if (!_isMobile)
                CloseHamburger();
        }
    }

    private void ApplyLayout()
    {
        MainTabControl.TabStripPlacement = _isMobile ? Dock.Bottom : Dock.Top;
        DesktopHeader.IsVisible = !_isMobile;
        MobileHeader.IsVisible = _isMobile;
    }

    private void UpdateCommanderLayer()
    {
        var inCommander = _vm?.IsCommanderMode ?? false;
        CommanderLayer.IsVisible = inCommander;
        // Hide the tab UI behind the commander overlay so it doesn't receive input
        MainTabControl.IsVisible = !inCommander;
        MobileHeader.IsVisible = _isMobile && !inCommander;
        DesktopHeader.IsVisible = !_isMobile && !inCommander;
        StatusBar.IsVisible = !inCommander;
    }

    private void OnHamburgerClick(object? sender, RoutedEventArgs e) =>
        HamburgerOverlay.IsVisible = true;

    private void OnHamburgerCloseClick(object? sender, RoutedEventArgs e) =>
        CloseHamburger();

    private void OnHamburgerScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        CloseHamburger();
        e.Handled = true;
    }

    private void OnHamburgerActionClick(object? sender, RoutedEventArgs e) =>
        CloseHamburger();

    private void CloseHamburger() =>
        HamburgerOverlay.IsVisible = false;
}
