using System;
using Avalonia;
using Avalonia.Controls;

namespace Library.Views.Controls;

/// <summary>
/// Arranges children in uniform-width columns using a masonry (greedy-fill) strategy:
/// each new child is placed into the column with the smallest current height.
/// This eliminates the large blank gaps that appear when row-aligned columns have
/// very different heights.
/// </summary>
public sealed class UniformWrapPanel : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(MinItemWidth), 215);

    public static readonly StyledProperty<double> ItemSpacingProperty =
        AvaloniaProperty.Register<UniformWrapPanel, double>(nameof(ItemSpacing), 10);

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double ItemSpacing
    {
        get => GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    static UniformWrapPanel()
    {
        AffectsMeasure<UniformWrapPanel>(MinItemWidthProperty, ItemSpacingProperty);
    }

    private int GetColumnCount(double availableWidth)
    {
        double spacing = ItemSpacing;
        double minWidth = MinItemWidth;
        int cols = Math.Max(1, (int)((availableWidth + spacing) / (minWidth + spacing)));
        return Children.Count > 0 ? Math.Min(cols, Children.Count) : cols;
    }

    private double GetItemWidth(double availableWidth, int cols)
    {
        double spacing = ItemSpacing;
        return Math.Max(1, (availableWidth - spacing * (cols - 1)) / cols);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0) return new Size(0, 0);

        double availableWidth = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        int cols = GetColumnCount(availableWidth);
        double itemWidth = GetItemWidth(availableWidth, cols);

        foreach (var child in Children)
            child.Measure(new Size(itemWidth, double.PositiveInfinity));

        var colHeights = new double[cols];
        foreach (var child in Children)
        {
            int col = ShortestColumn(colHeights);
            colHeights[col] = (colHeights[col] > 0 ? colHeights[col] + ItemSpacing : 0)
                              + child.DesiredSize.Height;
        }

        double totalHeight = colHeights.Length > 0 ? Max(colHeights) : 0;
        return new Size(availableWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;

        double availableWidth = finalSize.Width;
        int cols = GetColumnCount(availableWidth);
        double itemWidth = GetItemWidth(availableWidth, cols);
        double spacing = ItemSpacing;

        var colHeights = new double[cols];
        foreach (var child in Children)
        {
            int col = ShortestColumn(colHeights);
            double y = colHeights[col] > 0 ? colHeights[col] + spacing : 0;
            double x = col * (itemWidth + spacing);
            child.Arrange(new Rect(x, y, itemWidth, child.DesiredSize.Height));
            colHeights[col] = y + child.DesiredSize.Height;
        }

        return finalSize;
    }

    private static int ShortestColumn(double[] heights)
    {
        int min = 0;
        for (int i = 1; i < heights.Length; i++)
            if (heights[i] < heights[min]) min = i;
        return min;
    }

    private static double Max(double[] values)
    {
        double max = values[0];
        for (int i = 1; i < values.Length; i++)
            if (values[i] > max) max = values[i];
        return max;
    }
}
