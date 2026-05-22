using System;
using Avalonia;
using Avalonia.Controls;

namespace Library.Views.Controls;

/// <summary>
/// A panel that arranges children in uniform-width columns that stretch to fill
/// available width. Columns wrap onto new rows when the pane is too narrow to
/// fit another column at MinItemWidth.
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
        // Re-layout when spacing/min-width properties change
        AffectsMeasure<UniformWrapPanel>(MinItemWidthProperty, ItemSpacingProperty);
    }

    private int GetColumnCount(double availableWidth)
    {
        double spacing = ItemSpacing;
        double minWidth = MinItemWidth;
        // Solve: cols * minWidth + (cols - 1) * spacing <= availableWidth
        //   => cols <= (availableWidth + spacing) / (minWidth + spacing)
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
        var itemSize = new Size(itemWidth, availableSize.Height);

        int rows = (Children.Count + cols - 1) / cols;
        double[] rowHeights = new double[rows];

        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(itemSize);
            int r = i / cols;
            rowHeights[r] = Math.Max(rowHeights[r], Children[i].DesiredSize.Height);
        }

        double totalHeight = 0;
        double spacing = ItemSpacing;
        for (int r = 0; r < rows; r++)
        {
            totalHeight += rowHeights[r];
            if (r < rows - 1) totalHeight += spacing;
        }

        return new Size(availableWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0) return finalSize;

        double availableWidth = finalSize.Width;
        int cols = GetColumnCount(availableWidth);
        double itemWidth = GetItemWidth(availableWidth, cols);
        double spacing = ItemSpacing;

        int rows = (Children.Count + cols - 1) / cols;
        double[] rowHeights = new double[rows];
        for (int i = 0; i < Children.Count; i++)
        {
            int r = i / cols;
            rowHeights[r] = Math.Max(rowHeights[r], Children[i].DesiredSize.Height);
        }

        double y = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                if (idx >= Children.Count) break;
                double x = c * (itemWidth + spacing);
                Children[idx].Arrange(new Rect(x, y, itemWidth, rowHeights[r]));
            }
            y += rowHeights[r] + spacing;
        }

        return finalSize;
    }
}
