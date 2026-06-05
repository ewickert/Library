namespace Library.ViewModels;

public sealed class DeckColorHistogramItemViewModel
{
    public DeckColorHistogramItemViewModel(
        string label,
        int count,
        double barWidth,
        string barColor,
        string textColor,
        bool isActive,
        double rowOpacity)
    {
        Label = label;
        Count = count;
        BarWidth = barWidth;
        BarColor = barColor;
        TextColor = textColor;
        IsActive = isActive;
        RowOpacity = rowOpacity;
    }

    public string Label { get; }
    public int Count { get; }
    public double BarWidth { get; }
    public string BarColor { get; }
    public string TextColor { get; }
    public bool IsActive { get; }
    public double RowOpacity { get; }
}
