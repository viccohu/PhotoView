# 缩略图格式标签显示优化计划

## 需求分析

用户要求：
1. 格式标签显示在**缩略图上**（不是高清预览）
2. 显示格式：`[图标&#xF156;][JPG|ARW|TIF]`（文字按颜色规范显示）
3. 整体用半透明深底色框住

颜色规范：
- 常规压缩图片格式：`#00c8ff`（蓝色）
- 所有 RAW 格式：`#ffb300`（橙色）
- 无损位图格式：`#2bff00`（绿色）

## 当前实现问题

1. 格式标签显示在 ImageViewerControl 中（高清预览）
2. 没有在缩略图上显示格式标签
3. 需要移除 ImageViewerControl 中的格式标签显示

## 实现步骤

### 步骤 1：修改 ImageFileInfo 模型
**文件**：`d:\PhotoView\PhotoView\Models\ImageFileInfo.cs`

**修改内容**：
1. 添加 `FormatTags` 属性（`ObservableCollection<FormatTag>`）
2. 添加 `UpdateFormatTags` 方法，根据 Group 中的图片格式更新格式标签
3. 在 `SetGroupInfo` 方法中调用 `UpdateFormatTags`

### 步骤 2：修改 FormatTag 类
**文件**：`d:\PhotoView\PhotoView\ViewModels\ImageViewerViewModel.cs`

**修改内容**：
1. 将 `FormatTag` 类移到 `Models` 文件夹中
2. 确保类定义正确，包含 `Format`、`Color`、`IsLast` 属性

### 步骤 3：修改 MainViewModel
**文件**：`d:\PhotoView\PhotoView\ViewModels\MainViewModel.cs`

**修改内容**：
1. 在图片加载完成后，调用 `UpdateFormatTags` 方法
2. 确保格式标签在缩略图显示时已经准备好

### 步骤 4：修改 MainPage.xaml 缩略图模板
**文件**：`d:\PhotoView\PhotoView\Views\MainPage.xaml`

**修改内容**：
1. 移除现有的 `FormatButton`（第 343-356 行）
2. 在缩略图左上角添加格式标签显示区域
3. 显示格式：`[图标][JPG|ARW|TIF]`
4. 整体用半透明深底色框住
5. 使用 ItemsControl 显示格式标签

### 步骤 5：移除 ImageViewerControl 中的格式标签
**文件**：`d:\PhotoView\PhotoView\Controls\ImageViewerControl.xaml`

**修改内容**：
1. 移除左上角格式显示 Grid（第 116-176 行）
2. 移除 `InverseBoolToVisibilityConverter` 资源引用

### 步骤 6：移除 ImageViewerViewModel 中的格式标签相关代码
**文件**：`d:\PhotoView\PhotoView\ViewModels\ImageViewerViewModel.cs`

**修改内容**：
1. 移除 `FormatTags` 属性
2. 移除 `ScanFormatsInCurrentFolder` 方法
3. 移除 `GetFormatDisplayName` 方法
4. 移除 `GetFormatColor` 方法
5. 移除 `Clear` 方法中的 `FormatTags.Clear()` 调用

### 步骤 7：移除 MainPage.xaml.cs 中的格式扫描调用
**文件**：`d:\PhotoView\PhotoView\Views\MainPage.xaml.cs`

**修改内容**：
1. 移除 `ScanFormatsInCurrentFolder` 调用

### 步骤 8：移除 InverseBoolToVisibilityConverter
**文件**：`d:\PhotoView\PhotoView\Converters\InverseBoolToVisibilityConverter.cs`

**修改内容**：
1. 删除该文件（如果不再需要）

## 详细实现

### 1. ImageFileInfo 模型修改

```csharp
// 添加属性
private ObservableCollection<FormatTag> _formatTags = new();
public ObservableCollection<FormatTag> FormatTags
{
    get => _formatTags;
    private set => SetProperty(ref _formatTags, value);
}

// 添加方法
public void UpdateFormatTags()
{
    FormatTags.Clear();
    
    if (Group == null)
    {
        // 单个文件，显示自己的格式
        var ext = System.IO.Path.GetExtension(ImageName).ToLowerInvariant();
        FormatTags.Add(new FormatTag
        {
            Format = GetFormatDisplayName(ext),
            Color = GetFormatColor(ext),
            IsLast = true
        });
    }
    else
    {
        // 多个文件，显示所有格式
        var formats = new HashSet<string>();
        foreach (var image in Group.Images)
        {
            var ext = System.IO.Path.GetExtension(image.ImageName).ToLowerInvariant();
            formats.Add(ext);
        }
        
        var sortedFormats = formats.OrderBy(f => f).ToList();
        for (int i = 0; i < sortedFormats.Count; i++)
        {
            var ext = sortedFormats[i];
            FormatTags.Add(new FormatTag
            {
                Format = GetFormatDisplayName(ext),
                Color = GetFormatColor(ext),
                IsLast = i == sortedFormats.Count - 1
            });
        }
    }
}

private string GetFormatDisplayName(string ext)
{
    return ext switch
    {
        ".jpg" or ".jpeg" => "JPG",
        ".png" => "PNG",
        ".gif" => "GIF",
        ".bmp" => "BMP",
        ".tiff" or ".tif" => "TIF",
        ".webp" => "WebP",
        ".cr2" or ".cr3" or ".crw" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" or ".dng" or ".orf" or ".pef" or ".raf" or ".rw2" or ".raw" or ".3fr" or ".fff" or ".mos" or ".erf" or ".dcr" or ".mrw" or ".rwl" or ".srw" => "RAW",
        _ => ext.ToUpper().TrimStart('.')
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

### 2. FormatTag 类移动

创建新文件：`d:\PhotoView\PhotoView\Models\FormatTag.cs`

```csharp
namespace PhotoView.Models;

public class FormatTag
{
    public string Format { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsLast { get; set; }
}
```

### 3. MainPage.xaml 缩略图模板修改

替换现有的 FormatButton（第 343-356 行）：

```xml
<!-- 格式标签显示 -->
<Border
    Margin="8,8,0,0"
    HorizontalAlignment="Left"
    VerticalAlignment="Top"
    Background="#CC000000"
    CornerRadius="4"
    Padding="6,3"
    Visibility="{x:Bind FormatTags.Count, Mode=OneWay, Converter={StaticResource IntToVisibilityConverter}}">
    <StackPanel Orientation="Horizontal">
        <!-- 图标 -->
        <FontIcon
            Glyph="&#xF156;"
            FontFamily="{StaticResource SymbolThemeFontFamily}"
            FontSize="12"
            Foreground="White"
            Margin="0,0,4,0"/>
        
        <!-- 格式标签 -->
        <ItemsControl ItemsSource="{x:Bind FormatTags, Mode=OneWay}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal"/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="models:FormatTag">
                    <StackPanel Orientation="Horizontal">
                        <TextBlock
                            Text="{x:Bind Format}"
                            FontSize="11"
                            FontWeight="SemiBold"
                            Foreground="{x:Bind Color}"/>
                        <Border
                            Width="1"
                            Background="White"
                            Opacity="0.3"
                            Margin="4,0,4,0"
                            Visibility="{x:Bind IsLast, Converter={StaticResource InverseBooleanToVisibilityConverter}}"/>
                    </StackPanel>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Border>
```

## 注意事项

1. **线程安全**：确保在 UI 线程上更新 FormatTags
2. **性能优化**：避免频繁更新格式标签
3. **内存管理**：确保 FormatTags 在不需要时被清理
4. **颜色转换**：需要创建 StringToColorConverter 或使用硬编码颜色

## 验证步骤

1. 编译项目，确保没有错误
2. 运行项目，打开包含多个格式的文件夹
3. 检查缩略图上是否显示格式标签
4. 检查格式标签颜色是否正确
5. 检查高清预览中是否没有格式标签
