using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PhotoView.Helpers;

namespace PhotoView.Dialogs;

public static class KeyboardShortcutsDialog
{
    private static bool _isOpen;

    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        if (_isOpen)
            return;

        _isOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = "KeyboardShortcuts_Title".GetLocalized(),
                Content = CreateContent(),
                CloseButtonText = "Common_Close".GetLocalized(),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            await dialog.ShowAsync();
        }
        finally
        {
            _isOpen = false;
        }
    }

    private static UIElement CreateContent()
    {
        var content = new StackPanel
        {
            Spacing = 18,
            MaxWidth = 640
        };

        AddSection(content, "KeyboardShortcuts_General", new[]
        {
            ("F1", "KeyboardShortcuts_General_Help"),
            ("Left / Up / Right / Down", "KeyboardShortcuts_Common_Directions"),
            ("0-5 / Num 0-5", "KeyboardShortcuts_Common_Rating")
        });

        AddSection(content, "KeyboardShortcuts_SelectionPages", new[]
        {
            ("Delete", "KeyboardShortcuts_Common_Delete")
        });

        AddSection(content, "KeyboardShortcuts_Album", new[]
        {
            ("Space", "KeyboardShortcuts_Album_Space"),
            ("Esc", "KeyboardShortcuts_Album_Escape"),
            ("Ctrl + A", "KeyboardShortcuts_Album_SelectAll")
        });

        AddSection(content, "KeyboardShortcuts_Viewer", new[]
        {
            ("Space / Esc", "KeyboardShortcuts_Viewer_Close")
        });

        AddSection(content, "KeyboardShortcuts_Preview", new[]
        {
            ("Esc", "KeyboardShortcuts_Preview_ResetZoom"),
            ("Tab", "KeyboardShortcuts_Preview_Tab"),
            ("Space", "KeyboardShortcuts_Preview_Space")
        });

        return new ScrollViewer
        {
            Content = content,
            MaxHeight = 560,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto
        };
    }

    private static void AddSection(StackPanel content, string titleKey, IEnumerable<(string Shortcut, string DescriptionKey)> rows)
    {
        var section = new StackPanel
        {
            Spacing = 8
        };

        section.Children.Add(new TextBlock
        {
            Text = titleKey.GetLocalized(),
            Style = Application.Current.Resources["BodyStrongTextBlockStyle"] as Style
        });

        foreach (var row in rows)
        {
            section.Children.Add(CreateShortcutRow(row.Shortcut, row.DescriptionKey.GetLocalized()));
        }

        content.Children.Add(section);
    }

    private static UIElement CreateShortcutRow(string shortcut, string description)
    {
        var row = new Grid
        {
            ColumnSpacing = 16,
            MinHeight = 30
        };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var shortcutText = new TextBlock
        {
            Text = shortcut,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        var descriptionText = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(shortcutText, 0);
        Grid.SetColumn(descriptionText, 1);
        row.Children.Add(shortcutText);
        row.Children.Add(descriptionText);

        return row;
    }
}
