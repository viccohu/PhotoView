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
            Spacing = 20,
            MaxWidth = 680
        };

        // 提示信息
        var hintText = new TextBlock
        {
            Text = "KeyboardShortcuts_Hint".GetLocalized(),
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
        };

        var hintStyle = Application.Current.Resources["CaptionTextBlockStyle"] as Style;
        if (hintStyle != null)
        {
            hintText.Style = hintStyle;
        }
        content.Children.Add(hintText);

        // 通用快捷键
        AddSection(content, "KeyboardShortcuts_General", new[]
        {
            ("F1", "KeyboardShortcuts_General_Help"),
            ("← / ↑ / → / ↓", "KeyboardShortcuts_Common_Directions"),
            ("0-5 / Num 0-5", "KeyboardShortcuts_Common_Rating")
        });

        // 相册页快捷键
        AddSection(content, "KeyboardShortcuts_Album", new[]
        {
            ("Space", "KeyboardShortcuts_Album_Space"),
            ("Esc", "KeyboardShortcuts_Album_Escape"),
            ("Ctrl + A", "KeyboardShortcuts_Album_SelectAll"),
            ("Delete", "KeyboardShortcuts_Common_Delete")
        });

        // 查看器快捷键
        AddSection(content, "KeyboardShortcuts_Viewer", new[]
        {
            ("Space / Esc", "KeyboardShortcuts_Viewer_Close"),
            ("← / →", "KeyboardShortcuts_Viewer_NextPrev"),
            ("0-5 / Num 0-5", "KeyboardShortcuts_Viewer_Rating")
        });

        // 预览页快捷键
        AddSection(content, "KeyboardShortcuts_Preview", new[]
        {
            ("Esc", "KeyboardShortcuts_Preview_ResetZoom"),
            ("Tab", "KeyboardShortcuts_Preview_Tab"),
            ("Space", "KeyboardShortcuts_Preview_Space"),
            ("Delete", "KeyboardShortcuts_Common_Delete"),
            ("← / ↑ / → / ↓", "KeyboardShortcuts_Common_Directions"),
            ("0-5 / Num 0-5", "KeyboardShortcuts_Common_Rating")
        });

        return new ScrollViewer
        {
            Content = content,
            MaxHeight = 580,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto
        };
    }

    private static void AddSection(StackPanel content, string titleKey, IEnumerable<(string Shortcut, string DescriptionKey)> rows)
    {
        var section = new StackPanel
        {
            Spacing = 10
        };

        // 标题 - 更醒目
        var titleBlock = new TextBlock
        {
            Text = titleKey.GetLocalized(),
            Margin = new Thickness(0, 8, 0, 4),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        var style = Application.Current.Resources["SubtitleTextBlockStyle"] as Style;
        if (style != null)
        {
            titleBlock.Style = style;
        }
        section.Children.Add(titleBlock);

        // 分隔线
        section.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray),
            Margin = new Thickness(0, 0, 0, 12)
        });

        // 快捷键行
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
            ColumnSpacing = 20,
            MinHeight = 40,
            Padding = new Thickness(4, 6, 4, 6)
        };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 快捷键框 - 带背景和圆角，使用主题适配颜色
        var shortcutBorder = new Border
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        shortcutBorder.Background.Opacity = 0.15;

        var shortcutText = new TextBlock
        {
            Text = shortcut,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

        shortcutBorder.Child = shortcutText;

        var descriptionText = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14
        };

        Grid.SetColumn(shortcutBorder, 0);
        Grid.SetColumn(descriptionText, 1);
        row.Children.Add(shortcutBorder);
        row.Children.Add(descriptionText);

        return row;
    }
}
