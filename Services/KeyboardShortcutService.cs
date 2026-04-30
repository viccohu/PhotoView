using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using PhotoView.Contracts.Services;
using PhotoView.Dialogs;
using Windows.System;

namespace PhotoView.Services;

public class KeyboardShortcutService : IKeyboardShortcutService
{
    private Window? _window;
    private readonly Dictionary<string, Func<KeyRoutedEventArgs, bool>> _pageHandlers = new();
    private string _currentPageKey = "";

    public void Initialize(Window window)
    {
        _window = window;
        
        window.DispatcherQueue.TryEnqueue(() =>
        {
            if (window.Content is UIElement rootElement)
            {
                rootElement.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(Window_PreviewKeyDown), handledEventsToo: true);
                rootElement.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Window_KeyDown), handledEventsToo: true);
                Debug.WriteLine("[KeyboardShortcutService] 窗口级键盘事件已注册（handledEventsToo: true）");
            }
        });
    }

    private static readonly HashSet<VirtualKey> OverrideKeys = new()
    {
        VirtualKey.Space,
        VirtualKey.Escape,
        VirtualKey.Delete,
    };

    private static readonly HashSet<VirtualKey> PreviewOverrideKeys = new()
    {
        VirtualKey.Left,
        VirtualKey.Right,
        VirtualKey.Up,
        VirtualKey.Down,
        VirtualKey.Tab,
    };

    private void Window_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!PreviewOverrideKeys.Contains(e.Key))
            return;

        if (IsTextInputFocused())
            return;

        if (string.IsNullOrEmpty(_currentPageKey))
            return;

        if (_pageHandlers.TryGetValue(_currentPageKey, out var handler) && handler(e))
        {
            e.Handled = true;
        }
    }

    private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsTextInputFocused())
            return;

        if (e.Key == VirtualKey.F1 && _window?.Content is FrameworkElement rootElement)
        {
            _ = KeyboardShortcutsDialog.ShowAsync(rootElement.XamlRoot);
            e.Handled = true;
            return;
        }

        if (string.IsNullOrEmpty(_currentPageKey))
            return;

        if (e.Handled && !OverrideKeys.Contains(e.Key))
            return;

        if (_pageHandlers.TryGetValue(_currentPageKey, out var handler))
        {
            if (handler(e))
            {
                e.Handled = true;
            }
        }
    }

    public void RegisterPageShortcutHandler(string pageKey, Func<KeyRoutedEventArgs, bool> handler)
    {
        if (string.IsNullOrEmpty(pageKey))
            throw new ArgumentNullException(nameof(pageKey));
        
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        _pageHandlers[pageKey] = handler;
        Debug.WriteLine($"[KeyboardShortcutService] 注册页面处理器 - Page: {pageKey}");
    }

    public void UnregisterPageShortcutHandler(string pageKey)
    {
        if (_pageHandlers.Remove(pageKey))
        {
            Debug.WriteLine($"[KeyboardShortcutService] 注销页面处理器 - Page: {pageKey}");
        }
    }

    public void SetCurrentPage(string pageKey)
    {
        _currentPageKey = pageKey;
        Debug.WriteLine($"[KeyboardShortcutService] 设置当前页面 - Page: {pageKey}");
    }

    public bool IsTextInputFocused()
    {
        if (_window?.Content is not UIElement rootElement)
            return false;

        var focusedElement = FocusManager.GetFocusedElement(rootElement.XamlRoot) as DependencyObject;
        return IsTextInputElement(focusedElement);
    }

    private static bool IsTextInputElement(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is TextBox or PasswordBox or RichEditBox or AutoSuggestBox)
                return true;

            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }
}
