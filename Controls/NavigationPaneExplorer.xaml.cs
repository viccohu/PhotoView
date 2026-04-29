using System.ComponentModel;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhotoView.Contracts.Services;
using PhotoView.Helpers;
using PhotoView.Models;

namespace PhotoView.Controls;

public sealed partial class NavigationPaneExplorer : UserControl
{
    private bool _suppressCollapseStateUpdates;

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

    public NavigationPaneExplorer()
    {
        InitializeComponent();
    }

    private void NavigationPaneExplorer_Loaded(object sender, RoutedEventArgs e)
    {
        DirectoryHeaderTextBlock.Text = "NavigationPane_Directory".GetLocalized();
        if (Context != null)
        {
            _suppressCollapseStateUpdates = false;
        }
    }

    private void NavigationPaneExplorer_Unloaded(object sender, RoutedEventArgs e)
    {
        _suppressCollapseStateUpdates = true;
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
        _suppressCollapseStateUpdates = newContext == null;

        if (oldContext is INotifyPropertyChanged oldNotify)
        {
            oldNotify.PropertyChanged -= Context_PropertyChanged;
        }

        DataContext = newContext;

        if (newContext is INotifyPropertyChanged newNotify)
        {
            newNotify.PropertyChanged += Context_PropertyChanged;
        }

        QueueSelectedNodeIntoView();
    }

    private void Context_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(INavigationPaneContext.SelectedNode))
        {
            QueueSelectedNodeIntoView();
        }
    }

    private void QueueSelectedNodeIntoView()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Yield();

            var selectedNode = Context?.SelectedNode;
            if (selectedNode == null)
            {
                return;
            }

            FolderTreeView.SelectedItem = selectedNode;

            if (FolderTreeView.ContainerFromItem(selectedNode) is TreeViewItem selectedItem)
            {
                selectedItem.StartBringIntoView();
                selectedItem.Focus(FocusState.Programmatic);
            }
        });
    }

    private async void FolderTreeView_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (Context == null || args.Item is not FolderNode node)
        {
            return;
        }

        node.IsExpanded = true;
        await Context.ExpandNodeAsync(node);
    }

    private void FolderTreeView_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (DataContext == null || _suppressCollapseStateUpdates)
        {
            return;
        }

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
            flyout.ShowAt(target, new FlyoutShowOptions
            {
                Position = e.GetPosition(target)
            });
            e.Handled = true;
        }
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
