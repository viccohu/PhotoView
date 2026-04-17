using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using PhotoView.Contracts.Services;

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
                rootElement.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Window_KeyDown), handledEventsToo: true);
                Debug.WriteLine("[KeyboardShortcutService] 窗口级键盘事件已注册（handledEventsToo: true）");
            }
        });
    }

    private void Window_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (IsTextInputFocused())
        {
            Debug.WriteLine($"[KeyboardShortcutService] 焦点在输入框，跳过快捷键处理 - Key: {e.Key}");
            return;
        }

        if (string.IsNullOrEmpty(_currentPageKey))
        {
            Debug.WriteLine("[KeyboardShortcutService] 未设置当前页面，跳过快捷键处理");
            return;
        }

        if (_pageHandlers.TryGetValue(_currentPageKey, out var handler))
        {
            Debug.WriteLine($"[KeyboardShortcutService] 调用页面处理器 - Page: {_currentPageKey}, Key: {e.Key}");
            
            if (handler(e))
            {
                e.Handled = true;
                Debug.WriteLine($"[KeyboardShortcutService] 快捷键已处理 - Key: {e.Key}");
            }
        }
        else
        {
            Debug.WriteLine($"[KeyboardShortcutService] 未找到页面处理器 - Page: {_currentPageKey}");
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
        var isTextInput = IsTextInputElement(focusedElement);
        
        if (isTextInput)
        {
            Debug.WriteLine($"[KeyboardShortcutService] 检测到输入框焦点 - Element: {focusedElement?.GetType().Name ?? "null"}");
        }
        
        return isTextInput;
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
