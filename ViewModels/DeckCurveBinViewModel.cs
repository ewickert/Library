namespace Library.ViewModels;

public sealed class DeckCurveBinViewModel
{
    public DeckCurveBinViewModel(string label, int count, double barWidth)
    {
        Label = label;
        Count = count;
        BarWidth = barWidth;
    }

    public string Label { get; }
    public int Count { get; }
    public double BarWidth { get; }
}
