using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoView.Helpers;
using PhotoView.Models;

namespace PhotoView.Controls;

public sealed partial class CollectSourcePane : UserControl
{
    public static readonly DependencyProperty SourceItemsProperty = DependencyProperty.Register(
        nameof(SourceItems), typeof(ObservableCollection<NavigationPaneSourceItem>),
        typeof(CollectSourcePane), new PropertyMetadata(null));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(CollectSourcePane), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HasSourceItemsProperty = DependencyProperty.Register(
        nameof(HasSourceItems), typeof(bool), typeof(CollectSourcePane), new PropertyMetadata(false));

    public static readonly DependencyProperty ToggleOptionTextProperty = DependencyProperty.Register(
        nameof(ToggleOptionText), typeof(string), typeof(CollectSourcePane), new PropertyMetadata(null));

    public static readonly DependencyProperty IsToggleOptionVisibleProperty = DependencyProperty.Register(
        nameof(IsToggleOptionVisible), typeof(bool), typeof(CollectSourcePane), new PropertyMetadata(false));

    public static readonly DependencyProperty ToggleOptionValueProperty = DependencyProperty.Register(
        nameof(ToggleOptionValue), typeof(bool), typeof(CollectSourcePane), new PropertyMetadata(false));

    public ObservableCollection<NavigationPaneSourceItem> SourceItems
    {
        get => (ObservableCollection<NavigationPaneSourceItem>)GetValue(SourceItemsProperty);
        set => SetValue(SourceItemsProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public bool HasSourceItems
    {
        get => (bool)GetValue(HasSourceItemsProperty);
        set => SetValue(HasSourceItemsProperty, value);
    }

    public string ToggleOptionText
    {
        get => (string)GetValue(ToggleOptionTextProperty);
        set => SetValue(ToggleOptionTextProperty, value);
    }

    public bool IsToggleOptionVisible
    {
        get => (bool)GetValue(IsToggleOptionVisibleProperty);
        set => SetValue(IsToggleOptionVisibleProperty, value);
    }

    public bool ToggleOptionValue
    {
        get => (bool)GetValue(ToggleOptionValueProperty);
        set => SetValue(ToggleOptionValueProperty, value);
    }

    public Func<NavigationPaneSourceItem, Task>? RemoveSourceHandler { get; set; }
    public Func<NavigationPaneSourceItem, bool, Task>? SourceIncludeSubfoldersHandler { get; set; }
    public Func<Task>? LoadHandler { get; set; }
    public Func<bool, Task>? ToggleOptionHandler { get; set; }

    public CollectSourcePane()
    {
        InitializeComponent();
        LoadFoldersTextBlock.Text = "CollectSourcePane_LoadFolders.Text".GetLocalized();
    }

    private async void RemoveSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NavigationPaneSourceItem item } && RemoveSourceHandler != null)
        {
            await RemoveSourceHandler(item);
        }
    }

    private async void SourceIncludeSubfoldersButton_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton &&
            toggleButton.Tag is NavigationPaneSourceItem item &&
            SourceIncludeSubfoldersHandler != null)
        {
            await SourceIncludeSubfoldersHandler(item, toggleButton.IsChecked == true);
        }
    }

    private void SourceIncludeSubfoldersButton_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            ToolTipService.SetToolTip(toggleButton, "CollectSourcePane_IncludeSubfolders.ToolTipService.ToolTip".GetLocalized());
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (LoadHandler != null)
        {
            await LoadHandler();
        }
    }

    private async void ToggleOptionButton_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton && ToggleOptionHandler != null)
        {
            await ToggleOptionHandler(toggleButton.IsChecked == true);
        }
    }
}
