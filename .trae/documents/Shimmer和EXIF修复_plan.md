
# Shimmer 和 EXIF 修复方案

## 1. 问题分析

### 1.1 闪光效果看不到
- **原因**：LinearGradientBrush 使用了 `{ThemeResource SubtleFillColorTertiaryBrush}`，这个资源可能是透明的或颜色太浅
- **解决方案**：使用具体的半透明白色作为闪光颜色

### 1.2 遮罩与实际布局有位置偏移
- **原因**：当前 ShimmerContent 是一个整体，覆盖整个内容区域
- **需求**：
  - 每个小内容1对1的位置遮罩
  - 对于已经有的数据不进行遮罩
- **解决方案**：简化 ShimmerControl，直接在内容上覆盖闪光层

### 1.3 EXIF 数据获取不完整
- **原因**：某些图片格式不支持某些属性，或者属性名称不正确
- **解决方案**：添加更多调试信息，检查属性获取情况

## 2. 修复方案

### 2.1 修复闪光效果
修改 `ShimmerControl.xaml`：
- 将 `{ThemeResource SubtleFillColorTertiaryBrush}` 替换为具体的半透明白色
- 使用 `Color="#40FFFFFF"` 作为闪光颜色

### 2.2 简化 Shimmer 逻辑
- 移除 ShimmerContent 属性
- 在 IsLoading 时，直接在 Content 上覆盖一个半透明的闪光层
- 这样可以确保遮罩与实际布局完全一致
- 对于已经有数据的字段，它们会正常显示，闪光层会在上面移动

### 2.3 修复 EXIF 数据获取
- 添加详细的调试日志
- 检查每个属性的获取情况
- 如果某些属性为空，记录日志

## 3. 修改文件列表
1. `Controls/ShimmerControl.xaml` - 修复闪光效果，简化逻辑
2. `Controls/ShimmerControl.xaml.cs` - 移除 ShimmerContent 属性
3. `Controls/ImageViewerControl.xaml` - 移除 ShimmerContent 定义
4. `Services/ExifService.cs` - 添加调试日志
5. `ViewModels/ImageViewerViewModel.cs` - 添加调试日志

