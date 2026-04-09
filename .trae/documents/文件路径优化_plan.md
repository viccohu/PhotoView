
# 信息栏-文件路径优化方案（包含修复）

## 1. 需求分析

1. 地址左边显示文件格式 ✅（已实现，但有 bug）
2. 用不同颜色标识不同格式：
   - 一般压缩格式图片（JPG, PNG, GIF, WebP）-> 蓝色
   - 所有 RAW 文件 -> 橙色
   - 无压缩格式（TIFF, BMP）-> 绿色
   - 其他格式 -> 灰色
3. 地址作为可点击跳转到资源管理器中打开
4. 鼠标变成手型

## 2. 当前问题分析

### 2.1 格式标签显示不正确的原因
**XAML 列布局错误**：
- Grid 有 3 列定义，但所有元素都没有设置 `Grid.Column`
- 导致所有元素都重叠在第一列（默认列 0）
- 格式标签、复制按钮、路径文本都重叠在一起

### 2.2 当前 XAML 结构
```xml
<Grid ColumnSpacing="8">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>  <!-- 格式标签 -->
        <ColumnDefinition Width="*"/>     <!-- 路径文本 -->
        <ColumnDefinition Width="Auto"/>  <!-- 复制按钮 -->
    </Grid.ColumnDefinitions>
    
    <!-- 问题：所有元素都没有 Grid.Column -->
    <Border.../>              <!-- 应该在 Grid.Column="0" -->
    <Button.../>              <!-- 应该在 Grid.Column="2" -->
    <TextBlock.../>           <!-- 应该在 Grid.Column="1" -->
</Grid>
```

## 3. 修复方案

### 3.1 修复 XAML 列布局
```xml
<Grid ColumnSpacing="8">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto"/>  <!-- 格式标签 -->
        <ColumnDefinition Width="*"/>     <!-- 路径文本 -->
        <ColumnDefinition Width="Auto"/>  <!-- 复制按钮 -->
    </Grid.ColumnDefinitions>
    
    <!-- 格式标签 -->
    <Border
        Grid.Column="0"
        Background="{Binding FileFormatColor, Converter={StaticResource StringToColorConverter}, Mode=OneWay}"
        CornerRadius="4"
        VerticalAlignment="Center"
        Padding="10,1">
        <TextBlock Text="{Binding FileFormat}" FontSize="14" FontWeight="SemiBold" Foreground="White"/>
    </Border>
    
    <!-- 路径文本（可点击） -->
    <Button
        Grid.Column="1"
        Command="{x:Bind OpenInExplorerCommand}"
        Background="Transparent"
        BorderBrush="Transparent"
        BorderThickness="0"
        Padding="0"
        HorizontalAlignment="Left"
        VerticalAlignment="Center"
        Cursor="Hand">
        <TextBlock
            Text="{x:Bind Path}"
            Foreground="{ThemeResource AccentTextFillColorPrimaryBrush}"
            FontSize="14"
            TextWrapping="Wrap"/>
    </Button>
    
    <!-- 复制按钮 -->
    <Button
        Grid.Column="2"
        Command="{x:Bind CopyPathCommand}"
        Padding="8"
        Background="Transparent"
        BorderBrush="Transparent"
        BorderThickness="0">
        <FontIcon FontFamily="{StaticResource SymbolThemeFontFamily}" Glyph="&#xE8C8;" FontSize="14"/>
    </Button>
</Grid>
```

### 3.2 修改颜色逻辑
```csharp
var fileFormatColor = ext switch
{
    // 压缩格式 - 蓝色
    ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => "Blue",
    // RAW 格式 - 橙色
    ".cr2" or ".cr3" or ".crw" or ".nef" or ".nrw" or ".arw" or ".srf" or ".sr2" or 
    ".dng" or ".orf" or ".pef" or ".raf" or ".rw2" or ".raw" or ".3fr" or ".fff" or 
    ".mos" or ".erf" or ".dcr" or ".mrw" or ".rwl" or ".srw" => "Orange",
    // 无压缩格式 - 绿色
    ".tiff" or ".tif" or ".bmp" => "Green",
    // 其他格式 - 灰色
    _ => "Gray"
};
```

## 4. 修改文件列表
1. `Controls/ImageViewerControl.xaml` - 修复列布局，添加 Grid.Column，将路径改为可点击的 Button
2. `ViewModels/ImageViewerViewModel.cs` - 修改文件格式颜色逻辑

## 5. 风险和注意事项
- Button 默认有悬停效果，需要设置为透明背景
- 保持复制按钮功能不变
- 确保手型光标正确显示
- 格式标签文字需要设置为白色，以便在彩色背景上可见

