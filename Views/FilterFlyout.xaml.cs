using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PhotoView.ViewModels;
using System;

namespace PhotoView.Views;

public sealed partial class FilterFlyout : UserControl
{
    private FilterViewModel? _filterViewModel;
    private ToggleButton? _ratingHasButton;
    private ToggleButton? _ratingNoButton;
    private bool _isUpdatingFormatButtons;
    private bool _showBurstFilter;
    private bool _isUpdatingRatingModeButtons;

    public bool ShowBurstFilter
    {
        get => _showBurstFilter;
        set
        {
            _showBurstFilter = value;
            UpdateBurstFilterVisibility();
        }
    }

    public FilterViewModel? FilterViewModel
    {
        get => _filterViewModel;
        set
        {
            if (_filterViewModel != null)
            {
                _filterViewModel.PropertyChanged -= OnFilterViewModelPropertyChanged;
            }

            _filterViewModel = value;

            if (_filterViewModel != null)
            {
                _filterViewModel.PropertyChanged += OnFilterViewModelPropertyChanged;
                InitializeFileTypeButtons();
                InitializeRatingModeButtons();
                InitializeRatingControls();
                UpdatePendingDeleteFilterButton();
                UpdateBurstFilterButton();
            }
        }
    }

    public FilterFlyout()
    {
        InitializeComponent();
        RatingConditionComboBox.SelectionChanged += RatingConditionComboBox_SelectionChanged;
        RatingStarsControl.ValueChanged += RatingStarsControl_ValueChanged;
        ActualThemeChanged += FilterFlyout_ActualThemeChanged;
        UpdateBurstFilterVisibility();
    }

    private void FilterFlyout_ActualThemeChanged(FrameworkElement sender, object args)
    {
        RefreshToggleContentForegrounds();
    }

    private void OnFilterViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterViewModel.IsImageFilter) ||
            e.PropertyName == nameof(FilterViewModel.IsRawFilter) ||
            e.PropertyName == nameof(FilterViewModel.IsImageSingleOnlyFilter) ||
            e.PropertyName == nameof(FilterViewModel.IsRawSingleOnlyFilter) ||
            e.PropertyName == nameof(FilterViewModel.IsDualFormatFilter) ||
            e.PropertyName == nameof(FilterViewModel.IsDualFormatInverseFilter))
        {
            UpdateFileTypeButtonsState();
        }
        else if (e.PropertyName == nameof(FilterViewModel.RatingMode))
        {
            UpdateRatingModeButtonsState();
        }
        else if (e.PropertyName == nameof(FilterViewModel.IsPendingDeleteFilter))
        {
            UpdatePendingDeleteFilterButton();
        }
        else if (e.PropertyName == nameof(FilterViewModel.IsBurstFilter))
        {
            UpdateBurstFilterButton();
        }
    }

    private void InitializeFileTypeButtons()
    {
        UpdateFileTypeButtonsState();
        UpdateBurstFilterVisibility();
        UpdateBurstFilterButton();
    }

    private void UpdateFileTypeButtonsState()
    {
        if (_filterViewModel == null)
            return;

        _isUpdatingFormatButtons = true;

        try
        {
            ImageFilterButton.IsChecked = _filterViewModel.IsImageFilter;
            RawFilterButton.IsChecked = _filterViewModel.IsRawFilter;
            ImageSingleOnlyFilterButton.IsChecked = _filterViewModel.IsImageSingleOnlyFilter;
            RawSingleOnlyFilterButton.IsChecked = _filterViewModel.IsRawSingleOnlyFilter;
            DualFormatFilterButton.IsChecked = _filterViewModel.IsDualFormatFilter;
            DualFormatInverseFilterButton.IsChecked = _filterViewModel.IsDualFormatInverseFilter;
            ImageSingleOnlyFilterButton.IsEnabled = _filterViewModel.IsImageFilter;
            RawSingleOnlyFilterButton.IsEnabled = _filterViewModel.IsRawFilter;
            DualFormatInverseFilterButton.IsEnabled = _filterViewModel.IsDualFormatFilter;
            ImageSingleOnlyFilterButton.Opacity = ImageSingleOnlyFilterButton.IsEnabled ? 1d : 0.55d;
            RawSingleOnlyFilterButton.Opacity = RawSingleOnlyFilterButton.IsEnabled ? 1d : 0.55d;
            DualFormatInverseFilterButton.Opacity = DualFormatInverseFilterButton.IsEnabled ? 1d : 0.55d;
        }
        finally
        {
            _isUpdatingFormatButtons = false;
        }

        UpdateToggleContentForeground(ImageFilterButton);
        UpdateToggleContentForeground(RawFilterButton);
        UpdateToggleContentForeground(ImageSingleOnlyFilterButton);
        UpdateToggleContentForeground(RawSingleOnlyFilterButton);
        UpdateToggleContentForeground(DualFormatFilterButton);
        UpdateToggleContentForeground(DualFormatInverseFilterButton);
    }

    private void RefreshToggleContentForegrounds()
    {
        UpdateToggleContentForeground(ImageFilterButton);
        UpdateToggleContentForeground(RawFilterButton);
        UpdateToggleContentForeground(ImageSingleOnlyFilterButton);
        UpdateToggleContentForeground(RawSingleOnlyFilterButton);
        UpdateToggleContentForeground(DualFormatFilterButton);
        UpdateToggleContentForeground(DualFormatInverseFilterButton);
        UpdateToggleContentForeground(PendingDeleteFilterButton);
        UpdateToggleContentForeground(BurstFilterButton);
        UpdateToggleContentForeground(_ratingHasButton);
        UpdateToggleContentForeground(_ratingNoButton);
    }

    private void InitializeRatingModeButtons()
    {
        RatingModeButtonsPanel.Children.Clear();
        _ratingHasButton = null;
        _ratingNoButton = null;

        if (_filterViewModel == null)
            return;

        _ratingHasButton = CreateRatingModeButton("HasRatingIconTemplate", "有评级", showText: false, _filterViewModel.RatingMode == RatingFilterMode.HasRating);
        _ratingHasButton.Checked += RatingHasButton_Checked;
        _ratingHasButton.Unchecked += RatingHasButton_Unchecked;
        RatingModeButtonsPanel.Children.Add(_ratingHasButton);

        _ratingNoButton = CreateRatingModeButton("NoRatingIconTemplate", "无评级", showText: false, _filterViewModel.RatingMode == RatingFilterMode.NoRating);
        _ratingNoButton.Checked += RatingNoButton_Checked;
        _ratingNoButton.Unchecked += RatingNoButton_Unchecked;
        RatingModeButtonsPanel.Children.Add(_ratingNoButton);
    }

    private ToggleButton CreateRatingModeButton(string iconTemplateKey, string text, bool showText, bool isChecked)
    {
        var button = new ToggleButton
        {
            Content = CreateFilterChipContent(iconTemplateKey, text, showText),
            Style = (Style)Resources["FilterChipToggleButtonStyle"],
            MinWidth = 40,
            IsChecked = isChecked
        };

        UpdateToggleContentForeground(button);
        return button;
    }

    private void UpdateRatingModeButtonsState()
    {
        if (_filterViewModel == null)
            return;

        _isUpdatingRatingModeButtons = true;

        try
        {
            if (_ratingHasButton != null)
            {
                _ratingHasButton.IsChecked = _filterViewModel.RatingMode == RatingFilterMode.HasRating;
                UpdateToggleContentForeground(_ratingHasButton);
            }
            if (_ratingNoButton != null)
            {
                _ratingNoButton.IsChecked = _filterViewModel.RatingMode == RatingFilterMode.NoRating;
                UpdateToggleContentForeground(_ratingNoButton);
            }
        }
        finally
        {
            _isUpdatingRatingModeButtons = false;
        }

        UpdateRatingControlsState();
    }

    private void InitializeRatingControls()
    {
        if (_filterViewModel == null)
            return;

        switch (_filterViewModel.RatingCondition)
        {
            case RatingCondition.GreaterOrEqual:
                RatingConditionComboBox.SelectedIndex = 0;
                break;
            case RatingCondition.Equals:
                RatingConditionComboBox.SelectedIndex = 1;
                break;
            case RatingCondition.LessOrEqual:
                RatingConditionComboBox.SelectedIndex = 2;
                break;
        }

        RatingStarsControl.Value = _filterViewModel.RatingStars > 0 ? _filterViewModel.RatingStars : -1;
        UpdateRatingControlsState();
    }

    private void ImageFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsImageFilter = true;
    }

    private void ImageFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsImageFilter = false;
    }

    private void RawFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsRawFilter = true;
    }

    private void RawFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsRawFilter = false;
    }

    private void ImageSingleOnlyFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsImageSingleOnlyFilter = true;
    }

    private void ImageSingleOnlyFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsImageSingleOnlyFilter = false;
    }

    private void RawSingleOnlyFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsRawSingleOnlyFilter = true;
    }

    private void RawSingleOnlyFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsRawSingleOnlyFilter = false;
    }

    private void DualFormatFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsDualFormatFilter = true;
    }

    private void DualFormatFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsDualFormatFilter = false;
    }

    private void DualFormatInverseFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsDualFormatInverseFilter = true;
    }

    private void DualFormatInverseFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingFormatButtons)
            return;

        _filterViewModel.IsDualFormatInverseFilter = false;
    }

    private void RatingHasButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingRatingModeButtons)
            return;

        if (_ratingNoButton != null)
            _ratingNoButton.IsChecked = false;

        _filterViewModel.RatingMode = RatingFilterMode.HasRating;
        _filterViewModel.RatingCondition = RatingCondition.GreaterOrEqual;
        _filterViewModel.RatingStars = 1;

        RatingConditionComboBox.SelectedIndex = 0;
        RatingStarsControl.Value = 1;
        UpdateToggleContentForeground(_ratingHasButton);
        UpdateToggleContentForeground(_ratingNoButton);
    }

    private void RatingNoButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingRatingModeButtons)
            return;

        if (_ratingHasButton != null)
            _ratingHasButton.IsChecked = false;

        _filterViewModel.RatingMode = RatingFilterMode.NoRating;
        UpdateToggleContentForeground(_ratingHasButton);
        UpdateToggleContentForeground(_ratingNoButton);
    }

    private void RatingHasButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingRatingModeButtons)
            return;

        if (_filterViewModel.RatingMode == RatingFilterMode.HasRating)
        {
            SwitchRatingModeToAll();
        }
    }

    private void RatingNoButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null || _isUpdatingRatingModeButtons)
            return;

        if (_filterViewModel.RatingMode == RatingFilterMode.NoRating)
        {
            SwitchRatingModeToAll();
        }
    }

    private void SwitchRatingModeToAll()
    {
        if (_filterViewModel == null)
            return;

        _isUpdatingRatingModeButtons = true;

        try
        {
            if (_ratingHasButton != null)
                _ratingHasButton.IsChecked = false;
            if (_ratingNoButton != null)
                _ratingNoButton.IsChecked = false;
        }
        finally
        {
            _isUpdatingRatingModeButtons = false;
        }

        _filterViewModel.RatingMode = RatingFilterMode.All;
        UpdateToggleContentForeground(_ratingHasButton);
        UpdateToggleContentForeground(_ratingNoButton);
        UpdateRatingControlsState();
    }

    private void RatingConditionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        if (RatingConditionComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            switch (tag)
            {
                case "Equals":
                    _filterViewModel.RatingCondition = RatingCondition.Equals;
                    break;
                case "GreaterOrEqual":
                    _filterViewModel.RatingCondition = RatingCondition.GreaterOrEqual;
                    break;
                case "LessOrEqual":
                    _filterViewModel.RatingCondition = RatingCondition.LessOrEqual;
                    break;
            }
        }
    }

    private void RatingStarsControl_ValueChanged(RatingControl sender, object args)
    {
        if (_filterViewModel == null)
            return;

        var value = sender.Value;
        _filterViewModel.RatingStars = value > 0 ? (int)Math.Round(value, MidpointRounding.AwayFromZero) : 0;
    }

    private void UpdateRatingControlsState()
    {
        if (_filterViewModel == null)
            return;

        var isEnabled = _filterViewModel.RatingMode == RatingFilterMode.HasRating;
        RatingConditionComboBox.IsEnabled = isEnabled;
        RatingStarsControl.IsEnabled = isEnabled;
    }

    private void UpdatePendingDeleteFilterButton()
    {
        if (_filterViewModel == null)
            return;

        PendingDeleteFilterButton.IsChecked = _filterViewModel.IsPendingDeleteFilter;
        UpdateToggleContentForeground(PendingDeleteFilterButton);
    }

    private void PendingDeleteFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        _filterViewModel.IsPendingDeleteFilter = true;
    }

    private void PendingDeleteFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        _filterViewModel.IsPendingDeleteFilter = false;
    }

    private void UpdateBurstFilterVisibility()
    {
        if (BurstFilterButton == null)
            return;

        BurstFilterButton.Visibility = _showBurstFilter
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateBurstFilterButton()
    {
        if (_filterViewModel == null)
            return;

        BurstFilterButton.IsChecked = _filterViewModel.IsBurstFilter;
        UpdateToggleContentForeground(BurstFilterButton);
    }

    private void BurstFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        _filterViewModel.IsBurstFilter = true;
    }

    private void BurstFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        _filterViewModel.IsBurstFilter = false;
    }

    private StackPanel CreateFilterChipContent(string iconTemplateKey, string text, bool showText)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        panel.Children.Add(new ContentControl
        {
            ContentTemplate = (DataTemplate)Resources[iconTemplateKey]
        });

        if (showText)
        {
            panel.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return panel;
    }

    private void UpdateToggleContentForeground(ToggleButton? button)
    {
        if (button?.Content is not Panel panel)
            return;

        var foregroundBrush = GetToggleContentForegroundBrush(button.IsChecked == true);

        foreach (var child in panel.Children)
        {
            if (child is ContentControl contentControl)
            {
                contentControl.Content = foregroundBrush;
                contentControl.Foreground = foregroundBrush;
            }
            else if (child is TextBlock textBlock)
            {
                textBlock.Foreground = foregroundBrush;
            }
        }
    }

    private Brush GetToggleContentForegroundBrush(bool isChecked)
    {
        var resourceKey = isChecked
            ? "FilterChipCheckedForegroundBrush"
            : "FilterChipUncheckedForegroundBrush";

        return (Brush)Resources[resourceKey];
    }
}
