using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace PhotoView.Controls;

public sealed partial class ShimmerControl : UserControl
{
    public new static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content),
        typeof(object),
        typeof(ShimmerControl),
        new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading),
        typeof(bool),
        typeof(ShimmerControl),
        new PropertyMetadata(false, OnIsLoadingChanged));

    public new object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public ShimmerControl()
    {
        InitializeComponent();
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ShimmerControl control)
        {
            control.ContentPresenter.Content = e.NewValue;
        }
    }

    private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ShimmerControl control)
        {
            control.UpdateLoadingState();
        }
    }

    private void UpdateLoadingState()
    {
        if (IsLoading)
        {
            ShimmerOverlay.Visibility = Visibility.Visible;
            ShimmerAnimation.Begin();
        }
        else
        {
            ShimmerOverlay.Visibility = Visibility.Collapsed;
            ShimmerAnimation.Stop();
        }
    }

    private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RootClip.Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
    }
}
