# 缩略图抖动修复计划

## 问题现象
Connect 动画结束展示高清图时，缩略图的展示会上下抖动一下，但高清图展示没有抖动。

## 问题根源分析

根据文档的官方最佳实践：
1. `Image.Stretch` 应该设为 `"None"`，完全由 `ScrollView` 处理缩放和布局
2. 当前代码中 `Image.Stretch` 被改回了 `"Uniform"`，这可能导致与 ScrollView 配合时的布局抖动

## 修复方案

### 1. 将 Image.Stretch 改回 "None"
- 让 Image 保持原始像素大小
- 完全由 ScrollView 处理缩放

### 2. 确保 ImageContainer 正确配置
- 保持 `HorizontalAlignment="Center"` 和 `VerticalAlignment="Center"`
- 确保容器能正确撑开滚动区域

### 3. 简化 XAML 布局
- 移除 ScrollView 上不必要的对齐属性

## 实施步骤

1. 修改 XAML：`Image.Stretch="Uniform"` → `"None"`
2. 移除 ScrollView 的 `HorizontalContentAlignment` 和 `VerticalContentAlignment`（由 ImageContainer 处理）
3. 构建和测试
