using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace PhotoView.Contracts.Services;

public interface IKeyboardShortcutService
{
    void Initialize(Window window);
    
    void RegisterPageShortcutHandler(string pageKey, Func<KeyRoutedEventArgs, bool> handler);
    
    void UnregisterPageShortcutHandler(string pageKey);
    
    void SetCurrentPage(string pageKey);
    
    bool IsTextInputFocused();
}
