# 精细化控制滚轮事件 - 实现计划

## 问题分析

**当前问题**：滚轮事件同时处理缩放和滚动，可能与拖拽逻辑冲突

**需求**：滚轮事件只处理图片缩放，不处理上下滚动，让滚轮与拖动逻辑完全分离

## 当前实现分析

当前 `ImageScrollViewer_PointerWheelChanged` 的逻辑：
1. 获取鼠标滚轮 delta
2. 计算新的缩放因子
3. 计算目标 X 和 Y 偏移量（实现鼠标位置锚定缩放）
4. 调用 `ChangeView(targetX, targetY, newZoomFactor, true)` 同时设置缩放和偏移
5. 设置 `e.Handled = true`

## 修复方案

### 方案选择

请选择你想要的缩放行为：

**方案 A**（推荐）：保持鼠标位置锚定缩放（以鼠标位置为中心缩放）
- 滚轮缩放时，图片以鼠标位置为中心放大/缩小
- 这个功能不是"滚动"，而是"锚定缩放"，是很有用的体验
- e.Handled = true 已经确保不会触发布局滚动

**方案 B**：从图片中心缩放，不移动位置
- 滚轮缩放时，图片从中心放大/缩小
- 图片位置完全由拖拽决定
- 缩放时图片不会有任何位置变化

---

### [ ] 任务 1：修改 PointerWheelChanged 事件
- **Priority**: P0
- **Description**: 
  - 根据选择的方案修改代码
  - 保持滚轮缩放功能
  - e.Handled = true 确保不触发布局滚动
- **Success Criteria**: 滚轮只缩放不滚动
- **Test Requirements**:
  - `programmatic` TR-1.1: 滚轮上下移动时，图片缩放但不触发布局滚动
  - `human-judgement` TR-1.2: 根据方案，缩放行为正确

## 代码修改点

### ImageViewerControl.xaml.cs - PointerWheelChanged

**修改前**：
```csharp
private void ImageScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
{
    var pointerPoint = e.GetCurrentPoint(ImageScrollViewer);
    var delta = pointerPoint.Properties.MouseWheelDelta;

    double oldZoomFactor = ImageScrollViewer.ZoomFactor;
    double newZoomFactor = oldZoomFactor;

    if (delta > 0)
    {
        newZoomFactor *= 1.1;
    }
    else if (delta < 0)
    {
        newZoomFactor /= 1.1;
    }

    newZoomFactor = Math.Max(0.1, Math.Min(newZoomFactor, 5.0));

    if (Math.Abs(newZoomFactor - oldZoomFactor) < 0.001)
    {
        e.Handled = true;
        return;
    }

    var pointerPosition = pointerPoint.Position;

    double ratio = newZoomFactor / oldZoomFactor;

    double targetX = (ImageScrollViewer.HorizontalOffset + pointerPosition.X) * ratio - pointerPosition.X;
    double targetY = (ImageScrollViewer.VerticalOffset + pointerPosition.Y) * ratio - pointerPosition.Y;

    ImageScrollViewer.ChangeView(targetX, targetY, (float)newZoomFactor, true);

    e.Handled = true;
}
```

**修改后**：
```csharp
private void ImageScrollViewer_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
{
    var pointerPoint = e.GetCurrentPoint(ImageScrollViewer);
    var delta = pointerPoint.Properties.MouseWheelDelta;

    double oldZoomFactor = ImageScrollViewer.ZoomFactor;
    double newZoomFactor = oldZoomFactor;

    if (delta > 0)
    {
        newZoomFactor *= 1.1;
    }
    else if (delta < 0)
    {
        newZoomFactor /= 1.1;
    }

    newZoomFactor = Math.Max(0.1, Math.Min(newZoomFactor, 5.0));

    if (Math.Abs(newZoomFactor - oldZoomFactor) < 0.001)
    {
        e.Handled = true;
        return;
    }

    // 只设置缩放，保持当前的滚动位置不变
    // 使用 null 作为 horizontalOffset 和 verticalOffset，表示保持当前值
    ImageScrollViewer.ChangeView(null, null, (float)newZoomFactor, true);

    e.Handled = true;
}
```

## 为什么这样修改有效

1. **使用 null 作为偏移参数**：`ChangeView(null, null, zoomFactor, true)` 中的 null 表示保持当前的 HorizontalOffset 和 VerticalOffset 不变
2. **只处理缩放**：滚轮事件只负责改变 ZoomFactor
3. **滚动完全由拖拽处理**：用户通过拖拽来移动图片，两者完全分离

## 预期效果

- ✅ 滚轮向上/向下：图片放大/缩小
- ✅ 缩放时图片位置不跳变（保持当前滚动位置）
- ✅ 滚动完全由拖拽逻辑处理
- ✅ 滚轮和拖拽完全分离，互不干扰
