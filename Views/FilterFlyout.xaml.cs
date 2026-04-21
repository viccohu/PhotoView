using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoView.ViewModels;
using System;

namespace PhotoView.Views;

public sealed partial class FilterFlyout : UserControl
{
    private FilterViewModel? _filterViewModel;
    private ToggleButton? _ratingAllButton;
    private ToggleButton? _ratingHasButton;
    private ToggleButton? _ratingNoButton;
    private bool _showBurstFilter;

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
        UpdateBurstFilterVisibility();
    }

    private void OnFilterViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterViewModel.IsImageFilter) ||
            e.PropertyName == nameof(FilterViewModel.IsRawFilter))
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

        ImageFilterButton.IsChecked = _filterViewModel.IsImageFilter;
        RawFilterButton.IsChecked = _filterViewModel.IsRawFilter;
    }

    private void InitializeRatingModeButtons()
    {
        RatingModeButtonsPanel.Children.Clear();
        _ratingAllButton = null;
        _ratingHasButton = null;
        _ratingNoButton = null;

        if (_filterViewModel == null)
            return;

        _ratingAllButton = CreateRatingModeButton("AllIconTemplate", "全部", showText: false, _filterViewModel.RatingMode == RatingFilterMode.All);
        _ratingAllButton.Checked += RatingAllButton_Checked;
        RatingModeButtonsPanel.Children.Add(_ratingAllButton);

        _ratingHasButton = CreateRatingModeButton("HasRatingIconTemplate", "有评级", showText: false, _filterViewModel.RatingMode == RatingFilterMode.HasRating);
        _ratingHasButton.Checked += RatingHasButton_Checked;
        RatingModeButtonsPanel.Children.Add(_ratingHasButton);

        _ratingNoButton = CreateRatingModeButton("NoRatingIconTemplate", "无评级", showText: false, _filterViewModel.RatingMode == RatingFilterMode.NoRating);
        _ratingNoButton.Checked += RatingNoButton_Checked;
        RatingModeButtonsPanel.Children.Add(_ratingNoButton);
    }

    private ToggleButton CreateRatingModeButton(string iconTemplateKey, string text, bool showText, bool isChecked)
    {
        return new ToggleButton
        {
            Content = CreateFilterChipContent(iconTemplateKey, text, showText),
            Style = (Style)Resources["FilterChipToggleButtonStyle"],
            MinWidth = 40,
            IsChecked = isChecked
        };
    }

    private void UpdateRatingModeButtonsState()
    {
        if (_filterViewModel == null)
            return;

        if (_ratingAllButton != null)
            _ratingAllButton.IsChecked = _filterViewModel.RatingMode == RatingFilterMode.All;
        if (_ratingHasButton != null)
            _ratingHasButton.IsChecked = _filterViewModel.RatingMode == RatingFilterMode.HasRating;
        if (_ratingNoButton != null)
            _ratingNoButton.IsChecked = _filterViewModel.RatingMode == RatingFilterMode.NoRating;

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
        if (_filterViewModel == null)
            return;

        _filterViewModel.IsImageFilter = true;
    }

    private void ImageFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        _filterViewModel.IsImageFilter = false;
    }

    private void RawFilterButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        _filterViewModel.IsRawFilter = true;
    }

    private void RawFilterButton_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        _filterViewModel.IsRawFilter = false;
    }

    private void RatingAllButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        if (_ratingHasButton != null)
            _ratingHasButton.IsChecked = false;
        if (_ratingNoButton != null)
            _ratingNoButton.IsChecked = false;

        _filterViewModel.RatingMode = RatingFilterMode.All;
    }

    private void RatingHasButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        if (_ratingAllButton != null)
            _ratingAllButton.IsChecked = false;
        if (_ratingNoButton != null)
            _ratingNoButton.IsChecked = false;

        _filterViewModel.RatingMode = RatingFilterMode.HasRating;
        _filterViewModel.RatingCondition = RatingCondition.GreaterOrEqual;
        _filterViewModel.RatingStars = 1;

        RatingConditionComboBox.SelectedIndex = 0;
        RatingStarsControl.Value = 1;
    }

    private void RatingNoButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_filterViewModel == null)
            return;

        if (_ratingAllButton != null)
            _ratingAllButton.IsChecked = false;
        if (_ratingHasButton != null)
            _ratingHasButton.IsChecked = false;

        _filterViewModel.RatingMode = RatingFilterMode.NoRating;
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
}
