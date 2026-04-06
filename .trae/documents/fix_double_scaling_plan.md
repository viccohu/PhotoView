# 修复图片变成小图问题 - 实现计划

## 问题分析

**根本原因**：双重缩放问题
1. `Image` 控件设置了 `Stretch="Uniform"`，会自动让图片适应容器大小
2. `ApplyInitialZoomToFit()` 又对 `ScrollViewer` 进行了额外的缩放
3. 结果：图片被缩小了两次！

**示例**：
- 假设图片 4000x3000，窗口 800x600
- `Stretch="Uniform"` 已经让图片缩小到适应窗口（显示为 800x600）
- `ApplyInitialZoomToFit()` 计算出 fitScale = 0.2
- ScrollViewer 再缩放 0.2 倍
- 最终图片显示为 160x120（非常小！）

## 解决方案

### 方案 A：移除 `ApplyInitialZoomToFit()` 调用（推荐）
- **优点**：简单直接，利用 `Stretch="Uniform"` 的自动适应功能
- **缺点**：无法手动控制初始缩放比例
- **适用场景**：图片查看器主要依赖 Image 控件的自动适应

### 方案 B：修改 `Image` 的 `Stretch` 为 `None`
- **优点**：完全手动控制缩放，更精确
- **缺点**：需要处理图片比窗口小的情况（可能显示很小）
- **适用场景**：需要精确控制缩放行为

### 方案 C：智能判断是否需要缩放
- **优点**：兼顾自动适应和手动控制
- **缺点**：逻辑复杂
- **适用场景**：需要处理各种图片尺寸

## 推荐方案：方案 A（已完成）

### [x] 任务 1：移除 `ApplyInitialZoomToFit()` 的所有调用
- **Priority**: P0
- **Description**: 
  - 从 `LoadHighResolutionImageAsync` 中移除 `ApplyInitialZoomToFit()` 调用
  - 从 `ShowAfterAnimationAsync` 中移除 `ApplyInitialZoomToFit()` 调用
- **Success Criteria**: 图片不再被双重缩放
- **Test Requirements**:
  - `human-judgment` TR-1.1: 图片显示大小正常，不会被缩小得太多

### [x] 任务 2：保留状态重置逻辑
- **Priority**: P0
- **Description**: 
  - 保留 `PrepareContent` 中的状态重置和 ScrollViewer 重置
  - 确保切换图片时不会继承上一张图的缩放位置
- **Success Criteria**: 切换图片时状态正确重置
- **Test Requirements**:
  - `human-judgment` TR-2.1: 切换图片时不会继承上一张图的缩放位置

### [x] 任务 3：处理高清图加载完成后的显示
- **Priority**: P0
- **Description**: 
  - 高清图加载完成后，直接替换 `mainImage.Source`
  - 不调用 `ApplyInitialZoomToFit()`
  - 依赖 `Stretch="Uniform"` 自动适应
- **Success Criteria**: 高清图正确显示且大小合适
- **Test Requirements**:
  - `human-judgment` TR-3.1: 高清图显示大小正常

## 代码修改点

### ImageViewerControl.xaml.cs

1. **LoadHighResolutionImageAsync 方法**：
   - 移除 `ApplyInitialZoomToFit()` 调用
   - 只替换 `mainImage.Source`

2. **ShowAfterAnimationAsync 方法**：
   - 移除 `ApplyInitialZoomToFit()` 调用
   - 只应用 `_pendingHighResImageSource`

3. **PrepareContent 方法**：
   - 保持不变，继续重置状态和 ScrollViewer

## 预期效果

- ✅ 图片通过 `Stretch="Uniform"` 自动适应窗口
- ✅ 不会出现双重缩放问题
- ✅ 切换图片时状态正确重置
- ✅ ConnectedAnimation 正常工作
