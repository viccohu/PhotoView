
# Shimmer 骨架效果优化方案

## 1. 现状问题
- 当前 ShimmerControl 只是一个简单的背景遮罩
- 没有具体的骨架占位形状
- 闪光效果只在一个大矩形上，不符合需求

## 2. 需求分析（根据草图）
Shimmer 骨架需要包含具体的占位：
- 文件名区域：图标 + 文本占位
- 评级区域：图标 + 星级占位
- 日期区域：图标 + 4个方块占位
- 文件信息区域：标题 + 6个方块网格占位
- 设备信息区域：标题 + 6个方块网格占位
- 文件路径区域：标题 + 路径占位
- 每个占位都有闪光效果

## 3. 解决方案

### 3.1 重构 ShimmerControl
- 添加 `ShimmerContent` 属性，用于定义加载时显示的骨架内容
- 保留 `Content` 属性，用于定义正常显示的内容
- 闪光效果应用到整个 ShimmerContent 区域
- 根据 `IsLoading` 属性切换显示 Content 或 ShimmerContent

### 3.2 在 ImageViewerControl 中定义 ShimmerContent
- 完全按照实际内容的布局创建 ShimmerContent
- 使用占位符替代实际内容
  - 文本：使用 Rectangle 占位
  - RatingControl：使用 Rectangle 占位
  - 图标：保持图标（灰色）
- 保持相同的间距和布局结构

### 3.3 闪光效果
- 使用 LinearGradientBrush 创建从左到右的闪光效果
- 动画从左到右移动
- 覆盖整个 ShimmerContent 区域

## 4. 修改文件列表
1. `Controls/ShimmerControl.xaml` - 重构，添加 ShimmerContent 属性
2. `Controls/ShimmerControl.xaml.cs` - 重构，添加 ShimmerContent 属性
3. `Controls/ImageViewerControl.xaml` - 添加 ShimmerContent 定义

