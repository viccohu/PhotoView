using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace PhotoView.Controls;

public sealed partial class ShimmerControl : UserControl
{
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content),
        typeof(object),
        typeof(ShimmerControl),
        new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty ShimmerContentProperty = DependencyProperty.Register(
        nameof(ShimmerContent),
        typeof(object),
        typeof(ShimmerControl),
        new PropertyMetadata(null, OnShimmerContentChanged));

    public static readonly DependencyProperty IsLoadingProperty = DependencyProperty.Register(
        nameof(IsLoading),
        typeof(bool),
        typeof(ShimmerControl),
        new PropertyMetadata(false, OnIsLoadingChanged));

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public object? ShimmerContent
    {
        get => GetValue(ShimmerContentProperty);
        set => SetValue(ShimmerContentProperty, value);
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

    private static void OnShimmerContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ShimmerControl control)
        {
            control.ShimmerContentPresenter.Content = e.NewValue;
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
            ContentPresenter.Visibility = Visibility.Collapsed;
            ShimmerGrid.Visibility = Visibility.Visible;
            ShimmerAnimation.Begin();
        }
        else
        {
            ContentPresenter.Visibility = Visibility.Visible;
            ShimmerGrid.Visibility = Visibility.Collapsed;
            ShimmerAnimation.Stop();
        }
    }
}
