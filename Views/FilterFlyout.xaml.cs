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
            }
        }
    }

    public FilterFlyout()
    {
        this.InitializeComponent();
        RatingConditionComboBox.SelectionChanged += RatingConditionComboBox_SelectionChanged;
        RatingStarsControl.ValueChanged += RatingStarsControl_ValueChanged;
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
    }

    private void InitializeFileTypeButtons()
    {
        UpdateFileTypeButtonsState();
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

        // 添加"全部"按钮
        _ratingAllButton = new ToggleButton
        {
            Content = "全部",
            IsChecked = _filterViewModel.RatingMode == RatingFilterMode.All
        };
        _ratingAllButton.Checked += RatingAllButton_Checked;
        RatingModeButtonsPanel.Children.Add(_ratingAllButton);

        // 添加"有评级"按钮
        _ratingHasButton = new ToggleButton
        {
            Content = "有评级",
            IsChecked = _filterViewModel.RatingMode == RatingFilterMode.HasRating
        };
        _ratingHasButton.Checked += RatingHasButton_Checked;
        RatingModeButtonsPanel.Children.Add(_ratingHasButton);

        // 添加"无评级"按钮
        _ratingNoButton = new ToggleButton
        {
            Content = "无评级",
            IsChecked = _filterViewModel.RatingMode == RatingFilterMode.NoRating
        };
        _ratingNoButton.Checked += RatingNoButton_Checked;
        RatingModeButtonsPanel.Children.Add(_ratingNoButton);
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

        // 设置评级条件
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

        // 设置星级
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
        
        // 自动重置为 ≥1 星
        _filterViewModel.RatingCondition = RatingCondition.GreaterOrEqual;
        _filterViewModel.RatingStars = 1;
        
        // 更新 UI
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

        bool isEnabled = _filterViewModel.RatingMode == RatingFilterMode.HasRating;
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
}
