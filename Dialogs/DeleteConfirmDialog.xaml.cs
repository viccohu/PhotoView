using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Collections.Generic;
using System.Linq;

namespace PhotoView.Dialogs;

public sealed partial class DeleteConfirmDialog : ContentDialog
{
    private readonly Dictionary<string, ToggleButton> _extensionButtons = new();
    private ToggleButton? _allButton;

    public List<string> SelectedExtensions { get; private set; } = new();
    public bool IsDeleting { get; private set; }

    public DeleteConfirmDialog(List<string> extensions, int totalFileCount)
    {
        InitializeComponent();
        InitializeToggleButtons(extensions);
        UpdateContent(totalFileCount);
    }

    private void InitializeToggleButtons(List<string> extensions)
    {
        ToggleButtonsPanel.Children.Clear();
        _extensionButtons.Clear();

        foreach (var ext in extensions.OrderBy(e => e))
        {
            var button = new ToggleButton
            {
                Content = ext.ToLowerInvariant(),
                IsChecked = false,
                Tag = ext
            };
            button.Click += ExtensionButton_Click;
            ToggleButtonsPanel.Children.Add(button);
            _extensionButtons[ext] = button;
        }

        if (extensions.Count > 0)
        {
            var separator = new Border
            {
                Width = 1,
                Height = 32,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                Opacity = 0.3,
                Margin = new Thickness(8, 0, 8, 0)
            };
            ToggleButtonsPanel.Children.Add(separator);

            _allButton = new ToggleButton
            {
                Content = "全部",
                IsChecked = false
            };
            _allButton.Click += AllButton_Click;
            ToggleButtonsPanel.Children.Add(_allButton);
        }

        UpdateSelectedExtensions();
    }

    private void ExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button)
        {
            UpdateAllButtonState();
            UpdateSelectedExtensions();
            UpdateFileCount();
        }
    }

    private void AllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_allButton != null)
        {
            bool isChecked = _allButton.IsChecked == true;
            foreach (var button in _extensionButtons.Values)
            {
                button.IsChecked = isChecked;
            }
            UpdateSelectedExtensions();
            UpdateFileCount();
        }
    }

    private void UpdateAllButtonState()
    {
        if (_allButton != null && _extensionButtons.Count > 0)
        {
            bool allChecked = _extensionButtons.Values.All(b => b.IsChecked == true);
            bool noneChecked = _extensionButtons.Values.All(b => b.IsChecked == false);

            if (allChecked)
            {
                _allButton.IsChecked = true;
            }
            else if (noneChecked)
            {
                _allButton.IsChecked = false;
            }
            else
            {
                _allButton.IsChecked = null;
            }
        }
    }

    private void UpdateSelectedExtensions()
    {
        SelectedExtensions = _extensionButtons
            .Where(kvp => kvp.Value.IsChecked == true)
            .Select(kvp => kvp.Key)
            .ToList();
        IsPrimaryButtonEnabled = SelectedExtensions.Count > 0;
    }

    private void UpdateContent(int totalFileCount)
    {
        Title = "确认删除";
        ContentText.Text = $"共 {totalFileCount} 个文件标记为删除，请选择要删除的文件类型。";
    }

    private void UpdateFileCount()
    {
    }

    public void StartProgress()
    {
        IsDeleting = true;
        ProgressPanel.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
    }

    public void SetProgress(int current, int total)
    {
        DeleteProgressBar.Value = current;
        DeleteProgressBar.Maximum = total;
        ProgressText.Text = $"正在删除: {current}/{total}";
    }

    public void SetComplete()
    {
        IsDeleting = false;
        ProgressText.Text = "删除完成";
        DeleteProgressBar.Value = DeleteProgressBar.Maximum;
    }
}
