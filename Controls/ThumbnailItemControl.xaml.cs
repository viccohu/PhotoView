using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PhotoView.ViewModels;

namespace PhotoView.Controls;

// Legacy unconnected path. MainPage currently uses its inline GridView item template.
public sealed partial class ThumbnailItemControl : UserControl
{
    public static readonly DependencyProperty ThumbnailItemProperty =
        DependencyProperty.Register(
            nameof(ThumbnailItem),
            typeof(ThumbnailItemViewModel),
            typeof(ThumbnailItemControl),
            new PropertyMetadata(null, OnThumbnailItemChanged));

    public static readonly DependencyProperty ThumbnailSourceProperty =
        DependencyProperty.Register(
            nameof(ThumbnailSource),
            typeof(ImageSource),
            typeof(ThumbnailItemControl),
            new PropertyMetadata(null, OnThumbnailSourceChanged));

    public ThumbnailItemControl()
    {
        InitializeComponent();
    }

    public ThumbnailItemViewModel? ThumbnailItem
    {
        get => (ThumbnailItemViewModel?)GetValue(ThumbnailItemProperty);
        set => SetValue(ThumbnailItemProperty, value);
    }

    public ImageSource? ThumbnailSource
    {
        get => (ImageSource?)GetValue(ThumbnailSourceProperty);
        set => SetValue(ThumbnailSourceProperty, value);
    }

    private static void OnThumbnailItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ThumbnailItemControl)d;

        if (e.OldValue is ThumbnailItemViewModel oldItem)
        {
            oldItem.PropertyChanged -= control.OnThumbnailItemPropertyChanged;
        }

        if (e.NewValue is ThumbnailItemViewModel newItem)
        {
            newItem.PropertyChanged += control.OnThumbnailItemPropertyChanged;
        }

        control.UpdateVisualState();
    }

    private static void OnThumbnailSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (ThumbnailItemControl)d;
        control.ThumbnailImage.Source = (ImageSource?)e.NewValue;
    }

    private void OnThumbnailItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ThumbnailItemViewModel.IsSelected) or nameof(ThumbnailItemViewModel.IsLoading))
        {
            UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        if (ThumbnailItem == null)
        {
            SelectionBorder.Opacity = 0;
            SelectionCheckBox.Opacity = 0;
            SelectionCheckBox.IsChecked = false;
            return;
        }

        SelectionBorder.Opacity = ThumbnailItem.IsSelected ? 1 : 0;
        SelectionCheckBox.Opacity = ThumbnailItem.IsSelected ? 1 : 0;
        SelectionCheckBox.IsChecked = ThumbnailItem.IsSelected;
    }
}
