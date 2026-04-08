# ScrollView 拖动抖动问题优化方案

## 问题分析

当前拖动时抖动、移动不跟手，可能的原因：

1. **坐标计算问题** - `GetCurrentPoint(ImageScrollView)` 获取的是相对于 ScrollView 的坐标，但 ScrollView 内容在滚动，坐标会变化
2. **滚动更新问题** - `ScrollTo` 调用后，下一次 `GetCurrentPoint` 获取的坐标已经变了
3. **指针位置计算不准确** - 应该使用相对于屏幕或窗口的坐标

---

## 解决方案

### 核心问题

当前代码：
```csharp
var currentPoint = e.GetCurrentPoint(ImageScrollView);  // 相对于 ScrollView
var currentPosition = currentPoint.Position;
```

**问题**：当 ScrollView 滚动后，相对于 ScrollView 的坐标会变化，导致计算出的 `deltaX/deltaY` 不准确。

### 正确做法

应该使用相对于**窗口**或**屏幕**的坐标，或者使用相对于**内容**的坐标：

```csharp
// 方案1：使用相对于窗口的坐标
var currentPoint = e.GetCurrentPoint(null);  // null 表示相对于窗口
var currentPosition = currentPoint.Position;

// 方案2：使用相对于内容的坐标（更准确）
var currentPoint = e.GetCurrentPoint(MainImage);  // 相对于内容
var currentPosition = currentPoint.Position;
```

---

## 推荐方案：使用相对于窗口的坐标

### 修改后的代码

```csharp
private void ImageScrollView_PointerPressed(object sender, PointerRoutedEventArgs e)
{
    if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
    {
        var properties = e.GetCurrentPoint(ImageScrollView).Properties;
        if (properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            // 关键：使用相对于窗口的坐标
            _lastPointerPosition = e.GetCurrentPoint(null).Position;
            ImageScrollView.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }
}

private void ImageScrollView_PointerMoved(object sender, PointerRoutedEventArgs e)
{
    if (_isDragging && e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
    {
        // 关键：使用相对于窗口的坐标
        var currentPoint = e.GetCurrentPoint(null);
        var currentPosition = currentPoint.Position;

        var deltaX = _lastPointerPosition.X - currentPosition.X;
        var deltaY = _lastPointerPosition.Y - currentPosition.Y;

        var newHorizontalOffset = ImageScrollView.HorizontalOffset + deltaX;
        var newVerticalOffset = ImageScrollView.VerticalOffset + deltaY;

        var options = new ScrollingScrollOptions(ScrollingAnimationMode.Disabled);
        ImageScrollView.ScrollTo(newHorizontalOffset, newVerticalOffset, options);

        _lastPointerPosition = currentPosition;
        e.Handled = true;
    }
}
```

---

## 为什么这样能解决问题？

| 方案 | 坐标参考 | 滚动后坐标变化 | 效果 |
|-----|---------|--------------|------|
| 相对于 ScrollView | ScrollView | 变化 ❌ | 抖动 |
| 相对于窗口 | 窗口 | 不变 ✅ | 流畅 |
| 相对于内容 | 内容 | 不变 ✅ | 流畅 |

**关键**：使用相对于窗口或内容的坐标，滚动不会影响坐标值，计算出的 `deltaX/deltaY` 才准确。

---

## 实现步骤

### 步骤 1：修改 PointerPressed
- 使用 `e.GetCurrentPoint(null).Position` 获取相对于窗口的坐标

### 步骤 2：修改 PointerMoved
- 使用 `e.GetCurrentPoint(null).Position` 获取相对于窗口的坐标

### 步骤 3：测试验证
- 测试拖动流畅度
- 确认无抖动

---

## 预期效果

- ✅ 拖动流畅，无抖动
- ✅ 移动跟手
- ✅ 响应即时
