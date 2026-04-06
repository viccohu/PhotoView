# 修复图片切换时继承缩放问题 - 实现计划

## 问题分析

当前问题：
- 用户切换到下一张图片时，还是继承了上一张图片的缩放状态
- 虽然 `PrepareContent` 中调用了 `ImageScrollViewer.ChangeView(0, 0, 1.0f, true)`，但可能没有生效

可能的原因：
1. **ChangeView 调用时机不对**：在 `PrepareContent` 调用时，ScrollViewer 可能还没有准备好（ActualWidth/ActualHeight 为 0）
2. **ZoomFactor 没有被正确重置**：ChangeView 可能因为某些原因失败
3. **需要强制刷新布局**：在设置 ZoomFactor 后需要强制更新布局

## 解决方案（已完成）

### [x] 任务 1：在 PrepareContent 中强制重置 ScrollViewer
- **Priority**: P0
- **Description**: 
  - 在重置 ScrollViewer 之前，先强制更新布局
  - 确保 ScrollViewer 已经准备好
  - 使用更可靠的方式重置 ZoomFactor
- **Success Criteria**: ScrollViewer 的 ZoomFactor 被正确重置为 1.0
- **Test Requirements**:
  - `programmatic` TR-1.1: PrepareContent 调用后，ImageScrollViewer.ZoomFactor 应该为 1.0

### [x] 任务 2：添加调试日志验证重置是否成功
- **Priority**: P1
- **Description**: 
  - 在 ChangeView 调用前后添加调试日志
  - 验证 ZoomFactor 是否真的被重置
- **Success Criteria**: 能够通过日志确认 ZoomFactor 重置状态
- **Test Requirements**:
  - `programmatic` TR-2.1: 日志显示 ZoomFactor 重置前后的值

### [x] 任务 3：使用直接设置 ZoomFactor 的方式
- **Priority**: P0
- **Description**: 
  - 使用 `ImageScrollViewer.ZoomFactor = 1.0f;` 直接设置
  - 使用 `ScrollToHorizontalOffset(0)` 和 `ScrollToVerticalOffset(0)` 重置滚动位置
- **Success Criteria**: 确保在正确的时机重置 ZoomFactor
- **Test Requirements**:
  - `human-judgment` TR-3.1: 切换图片时不再继承上一张的缩放

## 代码修改点

### ImageViewerControl.xaml.cs

1. **PrepareContent 方法**：
   ```csharp
   // 方案 A：强制更新布局后再重置
   ImageScrollViewer.UpdateLayout();
   ImageScrollViewer.ChangeView(0, 0, 1.0f, true);
   
   // 方案 B：使用 ZoomFactor 属性直接设置
   ImageScrollViewer.ZoomFactor = 1.0f;
   ```

2. **添加调试日志**：
   ```csharp
   System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 重置前 ZoomFactor={ImageScrollViewer.ZoomFactor}");
   ImageScrollViewer.ChangeView(0, 0, 1.0f, true);
   System.Diagnostics.Debug.WriteLine($"[ImageViewer] PrepareContent: 重置后 ZoomFactor={ImageScrollViewer.ZoomFactor}");
   ```

## 推荐方案

**方案 A**：在 PrepareContent 中强制更新布局后再重置
- 先调用 `ImageScrollViewer.UpdateLayout()`
- 然后调用 `ImageScrollViewer.ChangeView(0, 0, 1.0f, true)`
- 添加调试日志验证

**方案 B**（如果方案 A 不生效）：直接设置 ZoomFactor 属性
- `ImageScrollViewer.ZoomFactor = 1.0f;`
- 这是最直接的方式

**方案 C**（如果方案 B 也不生效）：在 SizeChanged 事件中重置
- 监听 ScrollViewer 的 SizeChanged 事件
- 在事件中重置 ZoomFactor
