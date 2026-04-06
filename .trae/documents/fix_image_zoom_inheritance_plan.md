# 修复图片缩放位置继承问题 - 实现计划

## 问题分析

通过检查代码，发现了主要问题：

**缩放位置继承问题**：`ImageViewerControl` 是一个单例控件，当切换图片时，`ScrollViewer` 的缩放和滚动状态没有被重置，导致新图片继承上一张图片的缩放位置。

## 修复方案（已完成）

### [x] 任务 1：在 PrepareContent 中重置所有状态
- **Priority**: P0
- **Depends On**: None
- **Description**: 在 PrepareContent 方法中添加代码，重置 ScrollViewer 的缩放和滚动位置，以及所有相关状态变量
- **Success Criteria**: 每次打开新图片时，缩放和滚动位置都被重置为初始状态
- **Test Requirements**:
  - `programmatic` TR-1.1: PrepareContent 方法正确重置 _hasAppliedInitialZoom、_pendingHighResImageSource 等变量
  - `human-judgment` TR-1.2: 打开新图片时，缩放和滚动位置恢复到默认状态

### [x] 任务 2：修改 ShowAfterAnimationAsync 正确应用高清图
- **Priority**: P0
- **Depends On**: 任务 1
- **Description**: 修改 ShowAfterAnimationAsync 方法，在设置 _hasAppliedInitialZoom=true 后检查并应用 _pendingHighResImageSource
- **Success Criteria**: 高清图能正确显示，不破坏 ConnectedAnimation 效果
- **Test Requirements**:
  - `programmatic` TR-2.1: ShowAfterAnimationAsync 方法正确检查并应用 _pendingHighResImageSource
  - `human-judgment` TR-2.2: 新图片打开时 ConnectedAnimation 正常工作，高清图正确显示

## 实际修改的代码（ImageViewerControl.xaml.cs）

### 1. PrepareContent 方法（第 36-63 行）
**添加的内容**：
- 重置 `_hasAppliedInitialZoom = false`
- 重置 `_pendingHighResImageSource = null`
- 重置 `_originalZoomFactor = 1.0`
- 重置 `_is1To1Scale = false`
- 调用 `ImageScrollViewer.ChangeView(0, 0, 1.0f, true)` 重置缩放和滚动位置

### 2. ShowAfterAnimationAsync 方法（第 114-152 行）
**修改的内容**：
- 保持 `_hasAppliedInitialZoom = true`
- 添加检查：如果 `_pendingHighResImageSource != null`，则立即应用到 `mainImage.Source`

## 为什么不调用 ApplyInitialZoomToFit()？

1. **避免破坏 ConnectedAnimation**：ApplyInitialZoomToFit() 会在动画完成后立即改变缩放，破坏从缩略图到高清图的过渡效果
2. **尊重用户期望**：新图片应该以 1:1 比例（或通过 ConnectedAnimation 自然过渡）显示，而不是强制缩放到窗口大小
3. **保持现有行为**：原代码已经注释掉了 ApplyInitialZoomToFit()，说明这是有意为之的设计决策
