using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace PhotoView.Dialogs;

public sealed partial class DeleteConfirmDialog : ContentDialog
{
    private readonly Dictionary<string, ToggleButton> _extensionButtons = new();
    private ToggleButton? _allButton;

    public List<string> SelectedExtensions { get; private set; } = new();
    public bool IsDeleting { get; private set; }
    public Func<DeleteConfirmDialog, Task>? ConfirmDeleteAsync { get; set; }

    public DeleteConfirmDialog(List<string> extensions, int totalFileCount, int pendingBurstGroupCount = 0)
    {
        InitializeComponent();
        InitializeToggleButtons(extensions);
        UpdateContent(totalFileCount, pendingBurstGroupCount);
        PrimaryButtonClick += DeleteConfirmDialog_PrimaryButtonClick;
    }

    private async void DeleteConfirmDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (IsDeleting || ConfirmDeleteAsync == null)
        {
            return;
        }

        args.Cancel = true;
        var deferral = args.GetDeferral();

        try
        {
            await ConfirmDeleteAsync(this);
            await Task.Delay(500);
            Hide();
        }
        catch (Exception ex)
        {
            IsDeleting = false;
            IsSecondaryButtonEnabled = true;
            SecondaryButtonText = "关闭";
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressText.Text = $"删除失败: {ex.Message}";
        }
        finally
        {
            deferral.Complete();
        }
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
                Background = new SolidColorBrush(Microsoft.UI.Colors.Gray),
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
        if (sender is ToggleButton)
        {
            UpdateAllButtonState();
            UpdateSelectedExtensions();
        }
    }

    private void AllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_allButton == null)
        {
            return;
        }

        var isChecked = _allButton.IsChecked == true;
        foreach (var button in _extensionButtons.Values)
        {
            button.IsChecked = isChecked;
        }

        UpdateSelectedExtensions();
    }

    private void UpdateAllButtonState()
    {
        if (_allButton == null || _extensionButtons.Count == 0)
        {
            return;
        }

        var allChecked = _extensionButtons.Values.All(b => b.IsChecked == true);
        var noneChecked = _extensionButtons.Values.All(b => b.IsChecked == false);

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

    private void UpdateSelectedExtensions()
    {
        SelectedExtensions = _extensionButtons
            .Where(kvp => kvp.Value.IsChecked == true)
            .Select(kvp => kvp.Key)
            .ToList();

        IsPrimaryButtonEnabled = SelectedExtensions.Count > 0 && !IsDeleting;
    }

    private void UpdateContent(int totalFileCount, int pendingBurstGroupCount)
    {
        Title = "确认删除";
        ContentText.Text = $"共 {totalFileCount} 个文件标记为删除，请选择要删除的文件类型。";
        SelectionHintText.Text = "选择要删除的文件类型：";

        if (pendingBurstGroupCount > 0)
        {
            BurstWarningHost.Visibility = Visibility.Visible;
            BurstWarningHost.Background = new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xB9, 0x00));
            BurstWarningText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xB9, 0x00));
            BurstWarningText.Text = $"当前预删除中包含 {pendingBurstGroupCount} 个连拍组，删除将作用于组内全部成员。";
        }
        else
        {
            BurstWarningHost.Visibility = Visibility.Collapsed;
            BurstWarningText.Text = string.Empty;
        }
    }

    public void StartProgress()
    {
        IsDeleting = true;
        Title = "正在删除";
        SelectionHintText.Visibility = Visibility.Collapsed;
        BurstWarningHost.Visibility = Visibility.Collapsed;
        ToggleButtonsPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
    }

    public void SetProgress(int current, int total)
    {
        DeleteProgressBar.Maximum = total;
        DeleteProgressBar.Value = current;
        ProgressText.Text = $"正在删除: {current}/{total}";
    }

    public void SetComplete()
    {
        IsDeleting = false;
        Title = "删除完成";
        ProgressText.Text = "删除完成";
        DeleteProgressBar.Value = DeleteProgressBar.Maximum;
        IsPrimaryButtonEnabled = false;
        IsSecondaryButtonEnabled = false;
    }
}
