using System;
using System.Collections.Generic;
using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using LayoutContext = Microsoft.UI.Xaml.Controls.LayoutContext;
using VirtualizingLayout = Microsoft.UI.Xaml.Controls.VirtualizingLayout;
using VirtualizingLayoutContext = Microsoft.UI.Xaml.Controls.VirtualizingLayoutContext;
using System.Reflection;
using System.ComponentModel;

namespace PhotoView.Common;

/// <summary>
/// 瀑布流布局
/// </summary>
class ActivityWaterfallFlowLayout : VirtualizingLayout
{
    #region Layout parameters

    // We'll cache copies of the dependency properties to avoid calling GetValue during layout since that
    // can be quite expensive due to the number of times we'd end up calling these.
    // 我们将缓存依赖项财产的副本，以避免在布局期间调用GetValue，因为这可能会非常昂贵，因为我们最终会调用这些副本的次数。
    private double _rowSpacing;
    private double _colSpacing;
    private Size _minItemSize = Size.Empty;

    /// <summary>
    /// Gets or sets the size of the whitespace gutter to include between rows
    /// 获取或设置要包含在行之间的空白区的大小
    /// </summary>
    public double RowSpacing
    {
        get
        {
            return _rowSpacing;
        }
        set
        {
            SetValue(RowSpacingProperty, value);
        }
    }

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(
            "RowSpacing",
            typeof(double),
            typeof(ActivityWaterfallFlowLayout),
            new PropertyMetadata(0, OnPropertyChanged));

    /// <summary>
    /// Gets or sets the size of the whitespace gutter to include between items on the same row
    /// 获取或设置要包含在同一行的项之间的空白区的大小
    /// </summary>
    public double ColumnSpacing
    {
        get
        {
            return _colSpacing;
        }
        set
        {
            SetValue(ColumnSpacingProperty, value);
        }
    }

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(
            "ColumnSpacing",
            typeof(double),
            typeof(ActivityWaterfallFlowLayout),
            new PropertyMetadata(0, OnPropertyChanged));

    public Size MinItemSize
    {
        get
        {
            return _minItemSize;
        }
        set
        {
            SetValue(MinItemSizeProperty, value);
        }
    }

    public static readonly DependencyProperty MinItemSizeProperty =
        DependencyProperty.Register(
            "MinItemSize",
            typeof(Size),
            typeof(ActivityWaterfallFlowLayout),
            new PropertyMetadata(Size.Empty, OnPropertyChanged));

    private static void OnPropertyChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        var layout = obj as ActivityWaterfallFlowLayout;
        if (args.Property == RowSpacingProperty)
        {
            layout._rowSpacing = (double)args.NewValue;
        }
        else if (args.Property == ColumnSpacingProperty)
        {
            layout._colSpacing = (double)args.NewValue;
        }
        else if (args.Property == MinItemSizeProperty)
        {
            layout._minItemSize = (Size)args.NewValue;
        }
        else
        {
            throw new InvalidOperationException("Don't know what you are talking about!");
        }

        layout.InvalidateMeasure();
    }

    #endregion

    #region Setup / teardown

    protected override void InitializeForContextCore(VirtualizingLayoutContext context)
    {
        base.InitializeForContextCore(context);

        if (!(context.LayoutState is ActivityWaterfallFlowLayoutState state))
        {
            // Store any state we might need since (in theory) the layout could be in use by multiple
            // elements simultaneously
            // In reality for the Xbox Activity Feed there's probably only a single instance.
            context.LayoutState = new ActivityWaterfallFlowLayoutState();
        }
    }

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context)
    {
        base.UninitializeForContextCore(context);

        // clear any state
        context.LayoutState = null;
    }

    #endregion

    #region Layout

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        if (this.MinItemSize == Size.Empty)
        {
            var firstElement = context.GetOrCreateElementAt(0);
            firstElement.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));

            // setting the member value directly to skip invalidating layout
            this._minItemSize = firstElement.DesiredSize;
        }

        // Determine which items will appear on those rows and what the rect will be for each item
        // 确定哪些项目将出现在这些行上，以及每个项目的矩形是什么
        var state = context.LayoutState as ActivityWaterfallFlowLayoutState;
        state.LayoutRects.Clear();

        // Save the index of the first realized item.  We'll use it as a starting point during arrange.
        // 保存第一个实现项目的索引。我们将在安排过程中将其作为起点。
        //state.FirstRealizedIndex = firstRowIndex * 3;

        // ideal item width that will expand/shrink to fill available space
        // 理想的物品宽度，可扩展/收缩以填充可用空间 a space b space c space
        double desiredItemWidth = Math.Max(this.MinItemSize.Width, (availableSize.Width + this.ColumnSpacing) / 12);

        // Foreach item between the first and last index,
        //     Call GetElementOrCreateElementAt which causes an element to either be realized or retrieved
        //       from a recycle pool
        //     Measure the element using an appropriate size
        //
        // Any element that was previously realized which we don't retrieve in this pass (via a call to
        // GetElementOrCreateAt) will be automatically cleared and set aside for later re-use.
        // Note: While this work fine, it does mean that more elements than are required may be
        // created because it isn't until after our MeasureOverride completes that the unused elements
        // will be recycled and available to use.  We could avoid this by choosing to track the first/last
        // index from the previous layout pass.  The diff between the previous range and current range
        // would represent the elements that we can pre-emptively make available for re-use by calling
        // context.RecycleElement(element).

        // 一行12格，设置最小宽度，计算元素宽度在高度一定时等比宽度占几格，当下一个元素宽度占比大于剩余格数时，调整高度以适配间距和宽度
        // 下一个元素换行重新计算，当剩余元素填充不满一行时，高度按设置最小高度计算
        // 如果高度小于最小高度，按宽度重新赋值
        double yoffset = 0;
        // 剩余宽度
        var remainingWidth = availableSize.Width;
        Rect[] boundsForCurrentRows = new Rect[context.ItemCount];
        List<ActivityWithHeightElement> elements = new List<ActivityWithHeightElement>();
        for (int i = 0; i < context.ItemCount; i++)
        {
            // 获取元素
            StackPanel container = (StackPanel)context.GetOrCreateElementAt(i);
            //((StackPanel)context.GetOrCreateElementAt(i)).Children.ElementAt(0);
            // 以minHegiht为标准计算动态宽度，获取一行元素数量后反适配高度
            var tempWidth = container.Width;
            var tempHeight = container.Height;
            if (tempWidth < 1)
            {
                tempWidth = this.MinItemSize.Width;
                tempHeight = this.MinItemSize.Height;
            }
            var proportion = this.MinItemSize.Height / tempHeight;
            if (proportion > 1)
            {
                tempWidth = this.MinItemSize.Width;
                tempHeight = this.MinItemSize.Height;
                proportion = 1;
            }
            remainingWidth = remainingWidth - tempWidth * proportion - this.ColumnSpacing;
            if (remainingWidth < desiredItemWidth)
            {
                // 反适配高度
                double mixP = 0;
                foreach (var element in elements)
                {
                    mixP += element.Width / element.Height;
                }
                var rowHeight = availableSize.Width / mixP;
                double xOffset = 0;
                foreach (var element in elements)
                {
                    var activeWidth = element.Width * rowHeight / element.Height;
                    if (element.Proportion == 1)
                    {
                        activeWidth = rowHeight;
                    }
                    boundsForCurrentRows[i].X = xOffset;
                    boundsForCurrentRows[i].Width = activeWidth;
                    boundsForCurrentRows[i].Y = yoffset;
                    boundsForCurrentRows[i].Height = rowHeight;
                    xOffset += activeWidth + this.ColumnSpacing;
                    container.Measure(
                        new Size(activeWidth, rowHeight));

                    state.LayoutRects.Add(boundsForCurrentRows[i]);
                }
                yoffset += rowHeight + this.RowSpacing;
                remainingWidth = availableSize.Width;
                elements.Clear();
            }
            elements.Add(new ActivityWithHeightElement()
            {
                Width = tempWidth,
                Height = tempHeight,
                Proportion = proportion
            });
            if (i == context.ItemCount - 1)
            {
                // 最后一行处理，高度按最小高度设置
                if (elements.Count > 0)
                {
                    var rowHeight = MinItemSize.Height;
                    double xOffset = 0;
                    foreach (var element in elements)
                    {
                        double activeWidth = element.Width * rowHeight / element.Height;
                        if (activeWidth.Equals(double.NaN))
                        {
                            activeWidth = rowHeight;
                        }
                        boundsForCurrentRows[i].X = xOffset;
                        boundsForCurrentRows[i].Width = activeWidth;
                        boundsForCurrentRows[i].Y = yoffset;
                        boundsForCurrentRows[i].Height = rowHeight;
                        xOffset += activeWidth + this.ColumnSpacing;
                        container.Measure(
                            new Size(activeWidth, rowHeight));
                        state.LayoutRects.Add(boundsForCurrentRows[i]);
                    }
                    yoffset += rowHeight + this.RowSpacing;
                    elements.Clear();
                }
            }
            // w1*h/h1+w2*p2+w3*p3+w4*p4=w
            // w1/h1*h+w2/h2*h+...=w
        }

        // Report this as the desired size for the layout
        return new Size(availableSize.Width, yoffset);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        // walk through the cache of containers and arrange
        var state = context.LayoutState as ActivityWaterfallFlowLayoutState;
        var virtualContext = context as VirtualizingLayoutContext;
        int currentIndex = state.FirstRealizedIndex;

        foreach (var arrangeRect in state.LayoutRects)
        {
            var container = virtualContext.GetOrCreateElementAt(currentIndex);
            container.Arrange(arrangeRect);
            currentIndex++;
        }

        return finalSize;
    }

    #endregion
}

internal class ActivityWaterfallFlowLayoutState
{
    public int FirstRealizedIndex
    {
        get; set;
    }

    /// <summary>
    /// List of layout bounds for items starting with the
    /// FirstRealizedIndex.
    /// </summary>
    public List<Rect> LayoutRects
    {
        get
        {
            if (_layoutRects == null)
            {
                _layoutRects = new List<Rect>();
            }

            return _layoutRects;
        }
    }

    private List<Rect> _layoutRects;
}


internal class ActivityWithHeightElement
{
    internal double Width;
    internal double Height;
    internal double Proportion;
}