using System.ComponentModel;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using PhotoView.Contracts.Services;
using PhotoView.Models;

namespace PhotoView.Controls;

public sealed partial class NavigationPaneExplorer : UserControl
{
    public static readonly DependencyProperty ContextProperty = DependencyProperty.Register(
        nameof(Context),
        typeof(INavigationPaneContext),
        typeof(NavigationPaneExplorer),
        new PropertyMetadata(null, OnContextChanged));

    public INavigationPaneContext? Context
    {
        get => (INavigationPaneContext?)GetValue(ContextProperty);
        set => SetValue(ContextProperty, value);
    }

    private int _syncVersion;

    public NavigationPaneExplorer()
    {
        InitializeComponent();
    }

    private static void OnContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NavigationPaneExplorer control)
        {
            control.OnContextChanged((INavigationPaneContext?)e.OldValue, (INavigationPaneContext?)e.NewValue);
        }
    }

    private void OnContextChanged(INavigationPaneContext? oldContext, INavigationPaneContext? newContext)
    {
        if (oldContext is INotifyPropertyChanged oldNotify)
        {
            oldNotify.PropertyChanged -= Context_PropertyChanged;
        }

        DataContext = newContext;

        if (newContext is INotifyPropertyChanged newNotify)
        {
            newNotify.PropertyChanged += Context_PropertyChanged;
        }

        _syncVersion++;
        if (newContext != null)
        {
            var version = _syncVersion;
            DispatcherQueue.TryEnqueue(async () => await SyncSelectedNodeAsync(version));
        }
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(INavigationPaneContext.SelectedNode))
        {
            _syncVersion++;
            var version = _syncVersion;
            DispatcherQueue.TryEnqueue(async () => await SyncSelectedNodeAsync(version));
        }
    }

    private async void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (Context == null || args.Item is not FolderNode node)
        {
            return;
        }

        node.IsExpanded = true;
        await Context.ExpandNodeAsync(node);
        Context.SelectedNode = node;
    }

    private void FolderTreeView_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Item is FolderNode node)
        {
            node.IsExpanded = false;
        }
    }

    private async void FolderTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (Context == null || args.InvokedItem is not FolderNode node)
        {
            return;
        }

        if (Context.ActivateOnSingleClick)
        {
            await Context.ActivateNodeAsync(node);
        }
        else
        {
            await Context.SelectNodeAsync(node);
        }
    }

    private async void TreeViewItem_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Context == null ||
            !Context.ActivateOnDoubleTap ||
            !TryGetFolderNode(sender, out var node))
        {
            return;
        }

        await Context.ActivateNodeSecondaryAsync(node);
        e.Handled = true;
    }

    private async void TreeViewItem_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (Context == null ||
            !TryGetFolderNode(sender, out var node))
        {
            return;
        }

        var actions = Context.GetNodeActions(node);
        if (actions.Count == 0)
        {
            return;
        }

        var flyout = new MenuFlyout();
        foreach (var action in actions)
        {
            if (action.IsSeparator)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                continue;
            }

            var item = new MenuFlyoutItem
            {
                Text = action.Text
            };

            if (!string.IsNullOrWhiteSpace(action.Glyph))
            {
                item.Icon = new FontIcon
                {
                    Glyph = action.Glyph,
                    FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"]
                };
            }

            item.Click += async (_, _) =>
            {
                if (action.ExecuteAsync != null)
                {
                    await action.ExecuteAsync(node);
                }
            };

            flyout.Items.Add(item);
        }

        if (sender is FrameworkElement target)
        {
            flyout.ShowAt(target);
            e.Handled = true;
        }
    }

    private async void RemoveSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (Context == null || sender is not FrameworkElement { Tag: NavigationPaneSourceItem item })
        {
            return;
        }

        await Context.RemoveSourceAsync(item);
    }

    private async void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (Context == null)
        {
            return;
        }

        await Context.ExecutePrimaryActionAsync();
    }

    private async void ToggleOptionButton_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (Context == null || sender is not ToggleButton toggleButton)
        {
            return;
        }

        await Context.ToggleOptionAsync(toggleButton.IsChecked == true);
    }

    private async Task SyncSelectedNodeAsync(int version)
    {
        if (Context?.SelectedNode == null || version != _syncVersion)
        {
            return;
        }

        var path = new List<FolderNode>();
        var current = Context.SelectedNode;
        while (current != null)
        {
            path.Insert(0, current);
            current = current.Parent;
        }

        FolderNode? lastNode = null;
        for (var i = 0; i < path.Count; i++)
        {
            if (version != _syncVersion || Context == null)
            {
                return;
            }

            var node = path[i];
            lastNode = node;

            if (i < path.Count - 1)
            {
                if (!node.IsLoaded)
                {
                    await Context.ExpandNodeAsync(node);
                }

                if (version != _syncVersion || Context == null)
                {
                    return;
                }

                if (!node.IsExpanded)
                {
                    node.IsExpanded = true;
                    await Task.Delay(50);
                }
            }
        }

        if (version != _syncVersion || Context == null || lastNode == null)
        {
            return;
        }

        FolderTreeView.SelectedItem = lastNode;
    }

    private static bool TryGetFolderNode(object sender, out FolderNode node)
    {
        if (sender is TreeViewItem { Content: Grid { Tag: FolderNode taggedNode } })
        {
            node = taggedNode;
            return true;
        }

        if (sender is FrameworkElement { DataContext: FolderNode dataNode })
        {
            node = dataNode;
            return true;
        }

        node = null!;
        return false;
    }
}
