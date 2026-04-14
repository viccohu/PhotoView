using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhotoView.ViewModels;

namespace PhotoView.Controls;

public sealed partial class ImageInfoPanelControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(ImageViewerViewModel),
        typeof(ImageInfoPanelControl),
        new PropertyMetadata(null, OnViewModelChanged));

    public ImageViewerViewModel? ViewModel
    {
        get => (ImageViewerViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public ImageInfoPanelControl()
    {
        InitializeComponent();
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ImageInfoPanelControl control)
        {
            control.DataContext = e.NewValue;
        }
    }

    private void PathText_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel != null && sender is TextBlock { Text: { } path })
        {
            _ = ViewModel.OpenInExplorerAsync(path);
        }

        e.Handled = true;
    }

    private void ImageRatingControl_ValueChanged(RatingControl sender, object args)
    {
        if (ViewModel == null)
        {
            return;
        }

        var newRating = sender.Value < 0
            ? -1
            : Math.Clamp((int)Math.Round(sender.Value, MidpointRounding.AwayFromZero), 0, 5);

        _ = ViewModel.SetRatingCommand.ExecuteAsync(newRating);
    }
}
