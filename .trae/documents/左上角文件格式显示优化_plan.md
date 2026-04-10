
# 左上角文件格式显示优化方案

## 1. 需求分析

1. 取消左上角的浮动提示功能，保留图标
2. 按草图样式进行实现左上角显示文件格式
3. 若折叠中存在多种格式则有什么格式按颜色显示出来
4. 每个格式中间有一个1px的分割线
5. 颜色规范：
   - 常规压缩图片格式 - `#00c8ff`
   - 所有RAW格式 - `#ffb300`
   - 无损位图格式 - `#2bff00`

## 2. 当前实现分析

查看代码发现当前没有左上角的浮动提示功能，需要在 ImageViewerControl 中添加。

## 3. 实现方案

### 3.1 ViewModel 层修改

在 `ImageViewerViewModel` 中添加：
- 一个新的属性来存储当前文件夹中的所有格式
- 一个方法来扫描当前文件夹的所有文件并收集格式

### 3.2 View 层修改

在 `ImageViewerControl.xaml` 中添加：
- 左上角的布局容器
- 格式标签显示（多个格式并排）
- 1px 分割线
- 颜色使用硬编码值（不使用 ThemeResource）

## 4. 具体实现步骤

### 4.1 修改 ImageViewerViewModel.cs

添加属性：
```csharp
[ObservableProperty]
private ObservableCollection&lt;FormatTag&gt; _formatTags = new();

public class FormatTag
{
    public string Format { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsLast { get; set; }
}
```

添加方法：
```csharp
public void ScanFormatsInCurrentFolder(IEnumerable&lt;ImageFileInfo&gt; images)
{
    var formats = new HashSet&lt;string&gt;();
    foreach (var image in images)
    {
        var ext = Path.GetExtension(image.ImageName).ToLowerInvariant();
        formats.Add(ext);
    }

    var tags = new ObservableCollection&lt;FormatTag&gt;();
    var sortedFormats = formats.OrderBy(f =&gt; f).ToList();
    
    for (int i = 0; i &lt; sortedFormats.Count; i++)
    {
        var ext = sortedFormats[i];
        var formatName = GetFormatDisplayName(ext);
        var color = GetFormatColor(ext);
        
        tags.Add(new FormatTag
        {
            Format = formatName,
            Color = color,
            IsLast = i == sortedFormats.Count - 1
        });
    }

    FormatTags = tags;
}

private string GetFormatDisplayName(string ext)
{
    return ext switch
    {
        ".jpg" or ".jpeg" =&gt; "JPG",
        ".png" =&gt; "PNG",
        ".gif" =&gt; "GIF",
        ".bmp" =&gt; "BMP",
        ".tiff" or ".tif" =&gt; "TIFF",
        ".webp" =&gt; "WebP",
        ".cr2" or ".cr3" or ".crw" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" or ".dng" or ".orf" or ".pef" or ".raf" or ".rw2" or ".raw" or ".3fr" or ".fff" or ".mos" or ".erf" or ".dcr" or ".mrw" or ".rwl" or ".srw" =&gt; "RAW",
        _ =&gt; ext.ToUpper().TrimStart('.')
    };
}

private string GetFormatColor(string ext)
{
    var compressedFormats = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
    var rawFormats = new[] { ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".orf", ".pef", ".raf", ".rw2", ".raw", ".3fr", ".fff", ".mos", ".erf", ".dcr", ".mrw", ".rwl", ".srw" };
    var losslessFormats = new[] { ".tiff", ".tif", ".bmp" };

    if (compressedFormats.Contains(ext))
        return "#00c8ff";
    if (rawFormats.Contains(ext))
        return "#ffb300";
    if (losslessFormats.Contains(ext))
        return "#2bff00";
    
    return "#808080";
}
```

### 4.2 修改 MainViewModel.cs

在加载图片完成后，调用 `ScanFormatsInCurrentFolder` 方法。

### 4.3 修改 ImageViewerControl.xaml

在 ImageContainer 内添加左上角的格式显示：

```xml
&lt;Grid Margin="24,24,0,0" HorizontalAlignment="Left" VerticalAlignment="Top"&gt;
    &lt;Grid.ColumnDefinitions&gt;
        &lt;ColumnDefinition Width="Auto"/&gt;
        &lt;ColumnDefinition Width="Auto"/&gt;
    &lt;/Grid.ColumnDefinitions&gt;
    
    &lt;!-- 图标 --&gt;
    &lt;FontIcon
        Grid.Column="0"
        FontFamily="{StaticResource SymbolThemeFontFamily}"
        Glyph="&amp;#xE91B;"
        FontSize="16"
        VerticalAlignment="Center"
        Opacity="0.5"/&gt;
    
    &lt;!-- 格式标签 --&gt;
    &lt;ItemsControl
        Grid.Column="1"
        ItemsSource="{Binding FormatTags, Mode=OneWay}"
        Margin="8,0,0,0"
        VerticalAlignment="Center"&gt;
        &lt;ItemsControl.ItemsPanel&gt;
            &lt;ItemsPanelTemplate&gt;
                &lt;StackPanel Orientation="Horizontal"/&gt;
            &lt;/ItemsPanelTemplate&gt;
        &lt;/ItemsControl.ItemsPanel&gt;
        &lt;ItemsControl.ItemTemplate&gt;
            &lt;DataTemplate x:DataType="local:FormatTag"&gt;
                &lt;Grid&gt;
                    &lt;Grid.ColumnDefinitions&gt;
                        &lt;ColumnDefinition Width="Auto"/&gt;
                        &lt;ColumnDefinition Width="Auto"/&gt;
                    &lt;/Grid.ColumnDefinitions&gt;
                    
                    &lt;Border
                        Grid.Column="0"
                        Background="{x:Bind Color}"
                        CornerRadius="4"
                        Padding="10,1"
                        VerticalAlignment="Center"&gt;
                        &lt;TextBlock
                            Text="{x:Bind Format}"
                            FontSize="14"
                            FontWeight="SemiBold"
                            Foreground="White"/&gt;
                    &lt;/Border&gt;
                    
                    &lt;Border
                        Grid.Column="1"
                        Width="1"
                        Background="White"
                        Margin="4,0,4,0"
                        Opacity="0.3"
                        VerticalAlignment="Stretch"
                        Visibility="{x:Bind IsLast, Converter={StaticResource InverseBoolToVisibilityConverter}}"/&gt;
                &lt;/Grid&gt;
            &lt;/DataTemplate&gt;
        &lt;/ItemsControl.ItemTemplate&gt;
    &lt;/ItemsControl&gt;
&lt;/Grid&gt;
```

### 4.4 添加 InverseBoolToVisibilityConverter

在 `Converters` 文件夹中添加新的转换器。

## 5. 修改文件列表

1. `ViewModels/ImageViewerViewModel.cs` - 添加 FormatTags 属性和相关方法
2. `ViewModels/MainViewModel.cs` - 在加载图片后调用 ScanFormatsInCurrentFolder
3. `Controls/ImageViewerControl.xaml` - 添加左上角格式显示
4. `Converters/InverseBoolToVisibilityConverter.cs` - 添加新的转换器

## 6. 风险和注意事项

- 颜色使用硬编码值，不使用 ThemeResource
- 格式标签按字母顺序排序
- 最后一个格式标签不显示分割线
- 保持图标半透明显示

