using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace PhotoView.Layouts;

public class JustifiedLayout : VirtualizingLayout
{
    private const double DefaultLineHeight = 140.0;
    private const double DefaultSpacing = 8.0;
    private const double DefaultRowSpacing = 8.0;

    public static readonly DependencyProperty LineHeightProperty =
        DependencyProperty.Register(
            nameof(LineHeight),
            typeof(double),
            typeof(JustifiedLayout),
            new PropertyMetadata(DefaultLineHeight, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(JustifiedLayout),
            new PropertyMetadata(DefaultSpacing, OnLayoutPropertyChanged));

    public static readonly DependencyProperty MinimumSpacingProperty =
        DependencyProperty.Register(
            nameof(MinimumSpacing),
            typeof(double),
            typeof(JustifiedLayout),
            new PropertyMetadata(DefaultSpacing, OnLayoutPropertyChanged));

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(
            nameof(RowSpacing),
            typeof(double),
            typeof(JustifiedLayout),
            new PropertyMetadata(DefaultRowSpacing, OnLayoutPropertyChanged));

    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public double MinimumSpacing
    {
        get => (double)GetValue(MinimumSpacingProperty);
        set => SetValue(MinimumSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is JustifiedLayout layout)
        {
            layout._rows.Clear();
            layout._lastAvailableWidth = -1;
            layout._lastItemCount = -1;
            layout.InvalidateMeasure();
        }
    }

    private List<Row> _rows = new();
    private double _lastAvailableWidth = -1;
    private int _lastItemCount = -1;

    protected override void InitializeForContextCore(VirtualizingLayoutContext context)
    {
        base.InitializeForContextCore(context);
        _rows.Clear();
        _lastAvailableWidth = -1;
    }

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        base.UninitializeForContextCore(context);
        _rows.Clear();
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        if (context.ItemCount == 0)
        {
            _rows.Clear();
            _lastItemCount = 0;
            return new Size(availableSize.Width, 0);
        }

        var containerWidth = availableSize.Width;
        if (double.IsInfinity(containerWidth) || containerWidth <= 0)
        {
            containerWidth = 800;
        }

        var needsRebuild = Math.Abs(_lastAvailableWidth - containerWidth) > 0.1 || 
                          _lastItemCount != context.ItemCount;

        if (needsRebuild)
        {
            BuildRows(context, containerWidth);
            _lastAvailableWidth = containerWidth;
            _lastItemCount = context.ItemCount;
        }

        var totalHeight = 0.0;
        foreach (var row in _rows)
        {
            totalHeight += row.Height;
            if (row != _rows.Last())
            {
                totalHeight += RowSpacing;
            }
        }

        var realizationRect = context.RealizationRect;
        var startY = 0.0;

        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var row = _rows[rowIndex];
            var rowEndY = startY + row.Height;

            if (rowEndY >= realizationRect.Top - 100 && startY <= realizationRect.Bottom + 100)
            {
                foreach (var itemIndex in row.Indices)
                {
                    var element = context.GetOrCreateElementAt(itemIndex);
                    element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                }
            }

            startY = rowEndY + RowSpacing;
        }

        return new Size(containerWidth, Math.Max(0, totalHeight));
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        if (context.ItemCount == 0 || _rows.Count == 0)
        {
            return finalSize;
        }

        var realizationRect = context.RealizationRect;
        var startY = 0.0;
        for (var rowIndex = 0; rowIndex < _rows.Count; rowIndex++)
        {
            var row = _rows[rowIndex];
            var rowEndY = startY + row.Height;

            if (rowEndY >= realizationRect.Top - 100 && startY <= realizationRect.Bottom + 100)
            {
                var x = 0.0;
                var itemPosition = 0;
                foreach (var itemIndex in row.Indices)
                {
                    if (itemIndex < 0 || itemIndex >= context.ItemCount)
                    {
                        itemPosition++;
                        continue;
                    }

                    var element = context.GetOrCreateElementAt(itemIndex);
                    var width = row.Widths[itemPosition];

                    element.Arrange(new Rect(x, startY, width, row.Height));
                    x += width + row.Spacing;
                    itemPosition++;
                }
            }

            startY = rowEndY + RowSpacing;
        }

        return finalSize;
    }

    private void BuildRows(VirtualizingLayoutContext context, double containerWidth)
    {
        _rows.Clear();

        var currentRow = new Row
        {
            Indices = new List<int>(),
            Widths = new List<double>(),
            Height = LineHeight,
            Spacing = Spacing
        };
        var currentRowWidth = 0.0;
        var lineHeight = LineHeight;
        var spacing = Spacing;

        for (var i = 0; i < context.ItemCount; i++)
        {
            var aspectRatio = GetAspectRatio(context, i);
            var itemWidth = GetItemWidth(lineHeight, aspectRatio);
            var projectedWidth = currentRow.Indices.Count == 0
                ? itemWidth
                : currentRowWidth + spacing + itemWidth;

            if (currentRow.Indices.Count > 0 && projectedWidth > containerWidth)
            {
                _rows.Add(currentRow);

                currentRow = new Row
                {
                    Indices = new List<int>(),
                    Widths = new List<double>(),
                    Height = lineHeight,
                    Spacing = spacing
                };
                currentRowWidth = 0;
            }

            currentRow.Indices.Add(i);
            currentRow.Widths.Add(itemWidth);
            currentRowWidth = currentRow.Indices.Count == 1
                ? itemWidth
                : currentRowWidth + spacing + itemWidth;
        }

        if (currentRow.Indices.Count > 0)
        {
            _rows.Add(currentRow);
        }
    }

    private double GetAspectRatio(VirtualizingLayoutContext context, int index)
    {
        if (context.GetItemAt(index) is ImageFileInfo imageInfo)
        {
            return imageInfo.AspectRatio;
        }

        return 1.0;
    }

    private static double GetItemWidth(double lineHeight, double aspectRatio)
    {
        return lineHeight * Math.Max(0.05, aspectRatio);
    }

    private class Row
    {
        public List<int> Indices { get; set; } = new();
        public List<double> Widths { get; set; } = new();
        public double Height { get; set; }
        public double Spacing { get; set; }
    }
}
