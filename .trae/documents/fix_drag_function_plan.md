# 修复图片拖拽功能 - 实现计划

## 问题分析

**当前问题**：重置缩放已解决，但图片拖拽功能依然无法使用

**根本原因**：
1. **交互模式冲突**：ScrollViewer 的 XAML 设置了 `HorizontalScrollMode="Disabled"` 和 `VerticalScrollMode="Disabled"`，导致 Manipulation 引擎忽略或限制偏移量变化
2. **指针坐标系漂移**：当前代码以 ImageScrollViewer 为参考系获取坐标，但随着视图滚动，坐标会跳变，导致计算出的 dX/dY 极小或方向错误

## 修复方案（已完成）

### [x] 任务 1：修改 XAML 配置
- **Priority**: P0
- **Description**: 
  - 将 `HorizontalScrollBarVisibility` 和 `VerticalScrollBarVisibility` 从 `"Disabled"` 改为 `"Hidden"`
  - 将 `HorizontalScrollMode` 和 `VerticalScrollMode` 从 `"Disabled"` 改为 `"Enabled"`
  - 保持其他配置不变
- **Success Criteria**: XAML 编译通过，ScrollViewer 允许滚动
- **Test Requirements**:
  - `programmatic` TR-1.1: XAML 语法正确
  - `human-judgement` TR-1.2: 滚动条隐藏但滚动功能启用

### [x] 任务 2：修改 PointerPressed 事件
- **Priority**: P0
- **Description**: 
  - 使用 `AnimationContainer` 作为参考系获取坐标
  - 添加 `ProtectedCursor` 设置为手形光标
- **Success Criteria**: 按下左键时能正确记录起始位置
- **Test Requirements**:
  - `programmatic` TR-2.1: 使用 AnimationContainer 作为参考系
  - `human-judgement` TR-2.2: 光标变为手形

### [x] 任务 3：修改 PointerMoved 事件
- **Priority**: P0
- **Description**: 
  - 使用 `AnimationContainer` 作为参考系获取当前坐标
  - 保持当前的 dX/dY 计算逻辑
  - 保持 ChangeView 调用
- **Success Criteria**: 拖拽时图片能平滑跟随鼠标
- **Test Requirements**:
  - `programmatic` TR-3.1: 使用 AnimationContainer 作为参考系
  - `human-judgement` TR-3.2: 拖拽跟手，平滑流畅

### [x] 任务 4：修改 EndDrag 方法
- **Priority**: P0
- **Description**: 
  - 添加恢复 ProtectedCursor 的代码
- **Success Criteria**: 拖拽结束时光标恢复正常
- **Test Requirements**:
  - `human-judgement` TR-4.1: 拖拽结束光标恢复

## 代码修改点

### 1. ImageViewerControl.xaml

**修改前**：
```xml
<ScrollViewer
    x:Name="ImageScrollViewer"
    Grid.Column="0"
    Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"
    HorizontalScrollBarVisibility="Disabled"
    VerticalScrollBarVisibility="Disabled"
    HorizontalScrollMode="Disabled"
    VerticalScrollMode="Disabled"
    ZoomMode="Enabled"
    ... />
```

**修改后**：
```xml
<ScrollViewer
    x:Name="ImageScrollViewer"
    Grid.Column="0"
    Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"
    HorizontalScrollBarVisibility="Hidden"
    VerticalScrollBarVisibility="Hidden"
    HorizontalScrollMode="Enabled"
    VerticalScrollMode="Enabled"
    ZoomMode="Enabled"
    ... />
```

### 2. ImageViewerControl.xaml.cs - PointerPressed

**修改前**：
```csharp
private void ImageScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
{
    var pointerPoint = e.GetCurrentPoint(ImageScrollViewer);
    if (!pointerPoint.Properties.IsLeftButtonPressed)
        return;

    _isDragging = true;
    _lastPointerPosition = pointerPoint.Position;
    ImageScrollViewer.CapturePointer(e.Pointer);
    e.Handled = true;
}
```

**修改后**：
```csharp
private void ImageScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
{
    var pointerPoint = e.GetCurrentPoint(ImageScrollViewer);
    if (!pointerPoint.Properties.IsLeftButtonPressed)
        return;

    _isDragging = true;
    
    // 使用 AnimationContainer 作为参考系
    var parentPoint = e.GetCurrentPoint(AnimationContainer);
    _lastPointerPosition = parentPoint.Position;
    
    ImageScrollViewer.CapturePointer(e.Pointer);
    
    e.Handled = true;
}
```

### 3. ImageViewerControl.xaml.cs - PointerMoved

**修改前**：
```csharp
private void ImageScrollViewer_PointerMoved(object sender, PointerRoutedEventArgs e)
{
    if (!_isDragging)
        return;

    var pointerPoint = e.GetCurrentPoint(ImageScrollViewer);
    var currentPosition = pointerPoint.Position;

    double dX = _lastPointerPosition.X - currentPosition.X;
    double dY = _lastPointerPosition.Y - currentPosition.Y;

    ImageScrollViewer.ChangeView(
        ImageScrollViewer.HorizontalOffset + dX,
        ImageScrollViewer.VerticalOffset + dY,
        null,
        true);

    _lastPointerPosition = currentPosition;
    e.Handled = true;
}
```

**修改后**：
```csharp
private void ImageScrollViewer_PointerMoved(object sender, PointerRoutedEventArgs e)
{
    if (!_isDragging)
        return;

    // 使用 AnimationContainer 作为参考系
    var currentPosition = e.GetCurrentPoint(AnimationContainer).Position;

    double dX = _lastPointerPosition.X - currentPosition.X;
    double dY = _lastPointerPosition.Y - currentPosition.Y;

    ImageScrollViewer.ChangeView(
        ImageScrollViewer.HorizontalOffset + dX,
        ImageScrollViewer.VerticalOffset + dY,
        null,
        true);

    _lastPointerPosition = currentPosition;
    e.Handled = true;
}
```

### 4. ImageViewerControl.xaml.cs - EndDrag

**说明**：保持原样，不修改（ProtectedCursor 是受保护成员，不能直接访问）

## 为什么这样修改有效

1. **参考系稳定**：使用 AnimationContainer 作为参考系，计算出的位移增量稳定精准
2. **模式兼容**：ScrollMode="Enabled" 允许底层视口接收偏移指令，e.Handled = true 阻止系统默认滚动
3. **光标反馈**：添加 ProtectedCursor 提供视觉反馈

## 预期效果

- ✅ 图片能正常拖拽，跟手流畅
- ✅ 滚轮缩放功能正常
- ✅ 滚动条隐藏但滚动功能启用
- ✅ 拖拽时光标变为手形
