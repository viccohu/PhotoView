# 格式标签颜色显示修复计划

## 问题诊断

通过查看调试输出，发现以下问题：

### 问题 1：绑定路径错误

**文件**：`d:\PhotoView\PhotoView\Views\MainPage.xaml`

**调试输出**：
```
[StringToBrushConverter] Format: #808080
[StringToBrushConverter] Selected color: Gray (Default)
```

**问题分析**：
- 绑定的是 `Color` 属性，而不是 `Format` 属性
- `Color` 属性的值是 `#808080`（灰色），不是格式名称
- 因此，`StringToBrushConverter` 接收到的是颜色值，不是格式名称
- 结果：所有格式都显示为灰色

### 问题 2：绑定语法错误

**当前代码**（第 372 行）：
```xml
Background="{Binding Color, Converter={StaticResource StringToBrushConverter}}"
```

**正确代码**：
```xml
Background="{Binding Format, Converter={StaticResource StringToBrushConverter}}"
```

## 修复方案

### 方案：修改绑定路径

**修改文件**：`d:\PhotoView\PhotoView\Views\MainPage.xaml`

**修改位置**：第 372 行

**修改内容**：
```xml
<!-- 修改前 -->
Background="{Binding Color, Converter={StaticResource StringToBrushConverter}}"

<!-- 修改后 -->
Background="{Binding Format, Converter={StaticResource StringToBrushConverter}}"
```

## 实施步骤

### 步骤 1：修改 MainPage.xaml 的绑定路径

**文件**：`d:\PhotoView\PhotoView\Views\MainPage.xaml`

**修改位置**：第 372 行

**修改内容**：将 `Color` 改为 `Format`

### 步骤 2：验证修复效果

1. 编译项目，确保没有错误
2. 运行项目，打开包含多个格式的文件夹
3. 查看调试输出窗口，检查格式值是否正确
4. 检查缩略图上的格式标签颜色是否正确：
   - 常规压缩图片格式（JPG、PNG、GIF、WebP）：蓝色
   - 所有 RAW 格式：橙色
   - 无损位图格式（TIFF、BMP）：绿色

## 预期调试输出

**正确的调试输出**：
```
[StringToBrushConverter] Format: JPG
[StringToBrushConverter] Selected color: #00c8ff (Blue)

[StringToBrushConverter] Format: ARW
[StringToBrushConverter] Selected color: #ffb300 (Orange)

[StringToBrushConverter] Format: TIF
[StringToBrushConverter] Selected color: #2bff00 (Green)
```

## 注意事项

1. **绑定路径**：确保绑定的是 `Format` 属性，而不是 `Color` 属性
2. **DataTemplate**：`DataTemplate` 的 `x:DataType` 是 `models:FormatTag`，所以 `{Binding Format}` 应该能正确绑定
3. **转换器**：`StringToBrushConverter` 已经正确修改，直接判断格式名称

## 风险评估

**风险**：绑定路径错误可能导致格式标签不显示或显示为灰色
**对策**：修改绑定路径为 `Format`，确保转换器接收到格式名称

**风险**：格式名称可能与预期不符
**对策**：在 `StringToBrushConverter` 中添加更多格式类型，确保覆盖所有可能的格式
