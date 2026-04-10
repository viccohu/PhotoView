# 缩略图格式标签显示修复计划

## 问题诊断

根据用户提供的资料，问题不是数据问题，而是 **UI 绑定路径 / 视觉层级问题**。

### 当前实现分析

**文件**：`d:\PhotoView\PhotoView\Views\MainPage.xaml`（第 342-386 行）

**问题 1：颜色绑定错误**
```xml
<TextBlock
    Text="{x:Bind Format}"
    FontSize="11"
    FontWeight="SemiBold"
    Foreground="{x:Bind Color}"/>  <!-- ❌ 错误：Color 是字符串，Foreground 需要 Brush -->
```

**问题 2：格式标签显示位置**
- 当前：直接在 TextBlock 上显示颜色
- 正确：应该在 Border 的 Background 上显示颜色

### 根本原因

`FormatTag.Color` 是字符串类型（如 `"#00c8ff"`），但 `TextBlock.Foreground` 需要的是 `Brush` 类型。

## 修复方案

### 方案 1：修改 XAML 绑定（推荐）

**修改位置**：`d:\PhotoView\PhotoView\Views\MainPage.xaml`（第 368-382 行）

**修改内容**：
```xml
<ItemsControl.ItemTemplate>
    <DataTemplate x:DataType="models:FormatTag">
        <Border
            Background="{x:Bind Color}"
            CornerRadius="4"
            Padding="6,2"
            Margin="0,0,2,0">
            <TextBlock
                Text="{x:Bind Format}"
                FontSize="11"
                FontWeight="SemiBold"
                Foreground="White"/>
        </Border>
    </DataTemplate>
</ItemsControl.ItemTemplate>
```

**优点**：
- 简单直接
- Border.Background 可以接受字符串颜色
- 符合用户要求的显示格式：`[图标][JPG|ARW|TIF]`

### 方案 2：使用转换器（备选）

如果方案 1 不工作，可以创建一个 `StringToBrushConverter`：

**创建文件**：`d:\PhotoView\PhotoView\Converters\StringToBrushConverter.cs`

```csharp
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace PhotoView.Converters;

public class StringToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string colorString)
        {
            try
            {
                var color = Microsoft.UI.ColorHelper.FromArgb(
                    0xFF,
                    Convert.ToByte(colorString.Substring(1, 2), 16),
                    Convert.ToByte(colorString.Substring(3, 2), 16),
                    Convert.ToByte(colorString.Substring(5, 2), 16)
                );
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
```

**修改 XAML**：
```xml
<ItemsControl.ItemTemplate>
    <DataTemplate x:DataType="models:FormatTag">
        <StackPanel Orientation="Horizontal">
            <Border
                Background="{x:Bind Color, Converter={StaticResource StringToBrushConverter}}"
                CornerRadius="4"
                Padding="6,2"
                Margin="0,0,2,0">
                <TextBlock
                    Text="{x:Bind Format}"
                    FontSize="11"
                    FontWeight="SemiBold"
                    Foreground="White"/>
            </Border>
            <Border
                Width="1"
                Background="White"
                Opacity="0.3"
                Margin="2,0,2,0"
                Visibility="{x:Bind IsLast, Converter={StaticResource InverseBooleanToVisibilityConverter}}"/>
        </StackPanel>
    </DataTemplate>
</ItemsControl.ItemTemplate>
```

## 实施步骤

### 步骤 1：修改 MainPage.xaml 的格式标签显示

**文件**：`d:\PhotoView\PhotoView\Views\MainPage.xaml`

**修改位置**：第 367-383 行

**修改内容**：
- 将 TextBlock 的 Foreground 绑定改为 Border 的 Background 绑定
- 移除分割线 Border（因为格式标签已经有背景色，不需要额外的分割线）
- 调整 Padding 和 Margin

### 步骤 2：验证修复效果

1. 编译项目，确保没有错误
2. 运行项目，打开包含多个格式的文件夹
3. 检查缩略图上是否显示格式标签
4. 检查格式标签颜色是否正确

## 最终效果

**显示格式**：`[图标][JPG|ARW|TIF]`

**颜色规范**：
- 常规压缩图片格式（JPG、PNG、GIF、WebP）：`#00c8ff`（蓝色）
- 所有 RAW 格式：`#ffb300`（橙色）
- 无损位图格式（TIFF、BMP）：`#2bff00`（绿色）

**视觉效果**：
- 整体用半透明深底色框住（`#CC000000`）
- 每个格式标签有自己的背景色
- 格式标签之间有 2px 间距

## 注意事项

1. **Border.Background 可以接受字符串颜色**：WinUI 3 会自动将字符串颜色转换为 Brush
2. **层级顺序**：格式标签在图片之后，所以会显示在图片上面
3. **绑定路径**：使用 `{x:Bind FormatTags}` 而不是 `{Binding FormatTags}`，因为 DataContext 是 ImageFileInfo
