# ScrollView 鼠标拖动平移图片实现方案

## 需求分析

用户想要实现：
1. 鼠标左键拖动平移图片
2. 禁用鼠标滚轮滚动
3. 隐藏滚动条

---

## 官方文档分析

### WinUI 3 滚动控件

WinUI 3 有两个滚动控件：
- **ScrollViewer**：传统控件，使用 `ChangeView` 方法程序化滚动
- **ScrollView**：新控件，基于 `InteractionTracker`，支持动画驱动的视图更改，使用 `ScrollTo` 方法

### 默认行为

| 输入方式 | 默认支持 |
|---------|---------|
| 触摸 | 平移（panning）✅ |
| 笔 | 平移（panning）✅ |
| 鼠标滚轮 | 滚动 ✅ |
| 鼠标拖动 | 平移 ❌（需要手动实现）|

---

## 实现方案

### 方案评估

| 方案 | 优点 | 缺点 | 推荐度 |
|-----|------|------|--------|
| 方案1：Pointer 事件 + ScrollTo | 简单直接，兼容性好 | 需要手动计算偏移 | ⭐⭐⭐⭐⭐ |
| 方案2：Manipulation 事件 | 支持惯性 | 复杂，可能与缩放冲突 | ⭐⭐⭐ |
| 方案3：自定义 IScrollController | 最灵活 | 最复杂 | ⭐⭐ |

### 推荐方案：Pointer 事件 + ScrollTo

**核心思路**：
1. 捕获 `PointerPressed`、`PointerMoved`、`PointerReleased` 事件
2. 记录起始位置和当前偏移
3. 计算新的偏移并调用 `ScrollTo`

---

## 具体实现

### 1. XAML 配置

```xml
<ScrollView
    x:Name="ImageScrollView"
    ZoomMode="Enabled"
    MinZoomFactor="0.1"
    MaxZoomFactor="10.0"
    HorizontalScrollBarVisibility="Hidden"
    VerticalScrollBarVisibility="Hidden"
    PointerPressed="ImageScrollView_PointerPressed"
    PointerMoved="ImageScrollView_PointerMoved"
    PointerReleased="ImageScrollView_PointerReleased"
    PointerCanceled="ImageScrollView_PointerReleased"
    PointerCaptureLost="ImageScrollView_PointerReleased">
    <!-- 内容 -->
</ScrollView>
```

**说明**：
- `HorizontalScrollBarVisibility="Hidden"` - 隐藏滚动条但允许滚动
- `VerticalScrollBarVisibility="Hidden"` - 隐藏滚动条但允许滚动

### 2. C# 实现

```csharp
private bool _isDragging = false;
private Point _lastPointerPosition;

private void ImageScrollView_PointerPressed(object sender, PointerRoutedEventArgs e)
{
    // 只处理鼠标左键
    if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
    {
        var properties = e.GetCurrentPoint(ImageScrollView).Properties;
        if (properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _lastPointerPosition = e.GetCurrentPoint(ImageScrollView).Position;
            ImageScrollView.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }
}

private void ImageScrollView_PointerMoved(object sender, PointerRoutedEventArgs e)
{
    if (_isDragging && e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
    {
        var currentPoint = e.GetCurrentPoint(ImageScrollView);
        var currentPosition = currentPoint.Position;
        
        // 计算偏移量
        var deltaX = _lastPointerPosition.X - currentPosition.X;
        var deltaY = _lastPointerPosition.Y - currentPosition.Y;
        
        // 计算新的滚动位置
        var newHorizontalOffset = ImageScrollView.HorizontalOffset + deltaX;
        var newVerticalOffset = ImageScrollView.VerticalOffset + deltaY;
        
        // 应用新的滚动位置
        ImageScrollView.ScrollTo(newHorizontalOffset, newVerticalOffset);
        
        // 更新上一次位置
        _lastPointerPosition = currentPosition;
        e.Handled = true;
    }
}

private void ImageScrollView_PointerReleased(object sender, PointerRoutedEventArgs e)
{
    if (_isDragging)
    {
        _isDragging = false;
        ImageScrollView.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}
```

### 3. 禁用鼠标滚轮

```xml
<ScrollView
    PointerWheelChanged="ImageScrollView_PointerWheelChanged">
```

```csharp
private void ImageScrollView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
{
    // 禁用鼠标滚轮滚动
    e.Handled = true;
}
```

---

## 注意事项

### 1. 与缩放的兼容性

当前实现支持 Ctrl+滚轮缩放，如果禁用鼠标滚轮，需要单独处理：

```csharp
private void ImageScrollView_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
{
    var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
    if (ctrl.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
    {
        // 允许缩放
        e.Handled = false;
    }
    else
    {
        // 禁用滚动
        e.Handled = true;
    }
}
```

### 2. 与触摸的兼容性

触摸默认支持平移，不需要额外处理。但需要注意：
- 鼠标拖动和触摸平移可能会冲突
- 可以通过 `PointerDeviceType` 区分

### 3. 性能优化

- 使用 `CapturePointer` 确保拖动过程中不会丢失焦点
- 使用 `ReleasePointerCapture` 释放捕获

---

## 完整实现步骤

### 步骤 1：修改 XAML
- 设置 `HorizontalScrollBarVisibility="Hidden"`
- 设置 `VerticalScrollBarVisibility="Hidden"`
- 添加 `PointerPressed`、`PointerMoved`、`PointerReleased`、`PointerWheelChanged` 事件

### 步骤 2：添加字段
```csharp
private bool _isDragging = false;
private Point _lastPointerPosition;
```

### 步骤 3：实现事件处理
- `ImageScrollView_PointerPressed` - 开始拖动
- `ImageScrollView_PointerMoved` - 计算偏移并滚动
- `ImageScrollView_PointerReleased` - 结束拖动
- `ImageScrollView_PointerWheelChanged` - 禁用滚轮（可选）

---

## 预期效果

- ✅ 鼠标左键拖动平移图片
- ✅ 隐藏滚动条
- ✅ 可选：禁用鼠标滚轮滚动
- ✅ 可选：保留 Ctrl+滚轮缩放
- ✅ 触摸平移仍然正常工作
