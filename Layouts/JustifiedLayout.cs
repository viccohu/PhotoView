using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhotoView.Models;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace PhotoView.Layouts;

public class JustifiedLayout : VirtualizingLayout
{
    public static readonly DependencyProperty LineHeightProperty =
        DependencyProperty.Register(
            nameof(LineHeight),
            typeof(double),
            typeof(JustifiedLayout),
            new PropertyMetadata(200.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(
            nameof(Spacing),
            typeof(double),
            typeof(JustifiedLayout),
            new PropertyMetadata(4.0, OnLayoutPropertyChanged));

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
            totalHeight += row.Height + Spacing;
        }

        if (_rows.Count > 0)
        {
            totalHeight -= Spacing;
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

            startY = rowEndY + Spacing;
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
                foreach (var itemIndex in row.Indices)
                {
                    if (itemIndex < 0 || itemIndex >= context.ItemCount)
                    {
                        continue;
                    }

                    var element = context.GetOrCreateElementAt(itemIndex);
                    var aspectRatio = GetAspectRatio(context, itemIndex);
                    var width = row.Height * aspectRatio;

                    element.Arrange(new Rect(x, startY, width, row.Height));
                    x += width + Spacing;
                }
            }

            startY = rowEndY + Spacing;
        }

        return finalSize;
    }

    private void BuildRows(VirtualizingLayoutContext context, double containerWidth)
    {
        _rows.Clear();

        var currentRow = new Row { Indices = new List<int>() };
        var currentRowWidth = 0.0;
        var lineHeight = LineHeight;

        for (var i = 0; i < context.ItemCount; i++)
        {
            var aspectRatio = GetAspectRatio(context, i);
            var itemWidth = lineHeight * aspectRatio;

            if (currentRowWidth > 0 && currentRowWidth + itemWidth + Spacing > containerWidth)
            {
                currentRow.Height = lineHeight;
                _rows.Add(currentRow);

                currentRow = new Row { Indices = new List<int>() };
                currentRowWidth = 0;
            }

            currentRow.Indices.Add(i);
            currentRowWidth += itemWidth;

            if (currentRow.Indices.Count > 1)
            {
                currentRowWidth += Spacing;
            }
        }

        if (currentRow.Indices.Count > 0)
        {
            currentRow.Height = lineHeight;
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

    private class Row
    {
        public List<int> Indices { get; set; } = new();
        public double Height { get; set; }
    }
}
