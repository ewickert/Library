using Avalonia.Controls;

namespace Library.Views;

public partial class CommanderLifePlayerView : UserControl
{
    public CommanderLifePlayerView()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
        Loaded += (_, _) => ApplyResponsiveLayout(Bounds.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        double buttonSize;
        double fontSize;

        if (width >= 300)
        {
            buttonSize = 64;
            fontSize = 56;
        }
        else if (width >= 220)
        {
            buttonSize = 50;
            fontSize = 44;
        }
        else if (width >= 160)
        {
            buttonSize = 40;
            fontSize = 34;
        }
        else
        {
            buttonSize = 34;
            fontSize = 28;
        }

        BigMinusButton.Width = BigMinusButton.Height = buttonSize;
        BigPlusButton.Width = BigPlusButton.Height = buttonSize;
        LifeText.FontSize = fontSize;
    }
}
