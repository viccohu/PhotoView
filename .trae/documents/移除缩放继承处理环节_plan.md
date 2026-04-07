# 移除缩放继承处理环节 - 实施计划

## 背景
由于已使用每次新建 `ImageViewerControl` 实例的方式解决缩放继承问题，原有的缩放继承处理代码不再需要。

## 需要移除的内容

### 1. 字段（第 34-38 行）
- ❌ `_hasAppliedInitialZoom`
- ❌ `_pendingHighResImageSource`
- ❌ `_originalZoomFactor`

### 2. `PrepareContent` 方法（第 48-73 行）
- 移除重置这些字段的代码
- 移除注释掉的 `ScrollTo` 和 `ZoomTo` 代码
- 移除相关的 Debug 日志

### 3. `ShowAfterAnimationAsync` 方法（第 313-349 行）
- 移除 `_hasAppliedInitialZoom = true;`
- 移除 `_pendingHighResImageSource` 检查和设置逻辑

### 4. `ApplyInitialZoomToFit` 方法（第 351-392 行）
- 整个方法已被注释，可以完全删除

### 5. `LoadHighResolutionImageAsync` 方法（第 407-501 行）
- 移除 `_hasAppliedInitialZoom` 的判断逻辑
- 移除 `_pendingHighResImageSource` 的保存逻辑
- 直接在高清图加载完成后立即设置 `mainImage.Source`

## 保留的内容
- ✅ `_is1To1Scale` - 双击切换 1:1 显示仍然需要
- ✅ 双击事件中的 `_originalZoomFactor` - 用于记住切换前的缩放（可以简化为局部变量）

## 实施步骤

1. 移除不需要的字段
2. 简化 `PrepareContent` 方法
3. 简化 `ShowAfterAnimationAsync` 方法
4. 删除 `ApplyInitialZoomToFit` 方法
5. 简化 `LoadHighResolutionImageAsync` 方法
6. 调整双击事件（可选，改为局部变量）
7. 构建和测试

## 风险
- 低风险：仅移除不再需要的代码
- 每次新建实例已确保缩放不会继承
