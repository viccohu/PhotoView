# 修复 CollectPage 焦点丢失导致快捷键失效问题

## 问题分析

### 问题现象

从调试日志可以看到：

```
[CollectPage] PreviewCanvas PointerPressed - 当前焦点: PreviewImageCanvasControl
[CollectPage] 子元素 GotFocus - Border
[CollectPage] 子元素 GotFocus - Grid
[CollectPage] 子元素 LostFocus - PreviewImageCanvasControl
[CollectPage] LostFocus - 失去焦点的元素: PreviewImageCanvasControl
```

点击 PreviewCanvas 后，焦点从 `PreviewImageCanvasControl` 转移到了 `Border` → `Grid` 等容器元素，导致按键事件无法正确响应。

### 根本原因

1. **焦点被容器元素捕获**：点击某些区域后，焦点被 Border、Grid 等容器元素获取
2. **KeyDown 事件依赖焦点**：WinUI 3 中，只有当前*键盘*焦点的 UIElement 才能成为 KeyDown 事件的源
3. **焦点转移后快捷键失效**：焦点不在 PreviewImageCanvasControl 上，导致快捷键无法触发

### WinUI 3 焦点管理机制（来源：Microsoft Learn）

* 只有当前键盘焦点的 UIElement 才能成为 KeyUp 和 KeyDown 事件的源

* 焦点可以通过点击、Tab 键、编程方式转移

* 容器元素（Grid、Border、Panel 等）默认可以获取焦点

* 使用 `IsTabStop="False"` 可以防止元素获取焦点

## 解决方案

### 方案对比

| 方案                | 优点        | 缺点       | 推荐度   |
| ----------------- | --------- | -------- | ----- |
| **方案1：页面级别处理快捷键** | 不依赖焦点，最可靠 | 需要重构代码   | ⭐⭐⭐⭐⭐ |
| **方案2：防止容器获取焦点**  | 简单快速      | 可能影响其他功能 | ⭐⭐⭐⭐  |
| **方案3：点击后重新设置焦点** | 保持现有逻辑    | 可能不够稳定   | ⭐⭐⭐   |
| **方案4：组合方案**      | 最全面       | 实现复杂     | ⭐⭐⭐⭐⭐ |

### 推荐方案：组合方案（方案1 + 方案2 + 方案3）

#### 1. 在 Page 级别处理快捷键（核心方案）

将 KeyDown 事件从 PreviewImageCanvasControl 移到 Page 级别，不依赖特定元素的焦点。

**实现步骤**：

* 在 CollectPage 的构造函数中添加 `KeyDown` 事件处理

* 使用 `this.KeyDown` 而不是依赖 PreviewCanvas 的焦点

* 保持现有的快捷键处理逻辑

**代码示例**：

```csharp
public CollectPage()
{
    InitializeComponent();
    
    // 在页面级别处理键盘事件
    this.KeyDown += CollectPage_KeyDown;
}

private void CollectPage_KeyDown(object sender, KeyRoutedEventArgs e)
{
    // 直接处理快捷键，不依赖焦点
    HandlePageShortcutKey(e);
}
```

#### 2. 防止容器元素获取焦点

设置容器元素的 `IsTabStop="False"`，防止它们获取焦点。

**实现步骤**：

* 在 CollectPage.xaml 中找到所有容器元素（Grid、Border、Panel 等）

* 设置 `IsTabStop="False"`

* 确保只有交互元素（Button、TextBox 等）可以获取焦点

**XAML 示例**：

```xml
<Grid IsTabStop="False">
    <Border IsTabStop="False">
        <!-- 内容 -->
    </Border>
</Grid>
```

#### 3. 点击后重新设置焦点（辅助方案）

在点击 PreviewCanvas 后，强制将焦点设置回 PreviewImageCanvasControl。

**实现步骤**：

* 在 PointerPressed 事件中记录当前焦点

* 在 PointerReleased 事件中恢复焦点

* 使用 `Focus(FocusState.Programmatic)` 设置焦点

**代码示例**：

```csharp
private void PreviewCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
{
    _previousFocusedElement = FocusManager.GetFocusedElement(XamlRoot);
    Debug.WriteLine($"[CollectPage] 记录之前的焦点: {_previousFocusedElement?.GetType().Name ?? "null"}");
}

private void PreviewCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
{
    // 恢复焦点到 PreviewCanvas
    if (PreviewCanvas.Focus(FocusState.Programmatic))
    {
        Debug.WriteLine("[CollectPage] 焦点已恢复到 PreviewCanvas");
    }
}
```

#### 4. 使用 UIElement.LostFocus 事件监控焦点变化

添加焦点丢失监控，在焦点丢失到错误元素时自动恢复。

**实现步骤**：

* 为 PreviewCanvas 添加 LostFocus 事件

* 检查新焦点是否是容器元素

* 如果是容器元素，恢复焦点

**代码示例**：

```csharp
private void PreviewCanvas_LostFocus(object sender, RoutedEventArgs e)
{
    var newFocus = FocusManager.GetFocusedElement(XamlRoot);
    Debug.WriteLine($"[CollectPage] PreviewCanvas 失去焦点，新焦点: {newFocus?.GetType().Name ?? "null"}");
    
    // 如果新焦点是容器元素，恢复焦点
    if (newFocus is Grid or Border or Panel)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            PreviewCanvas.Focus(FocusState.Programmatic);
            Debug.WriteLine("[CollectPage] 焦点已恢复到 PreviewCanvas");
        });
    }
}
```

## 实施计划

### 第一步：在 Page 级别处理快捷键（优先级：高）

1. 修改 CollectPage 构造函数，添加页面级别的 KeyDown 事件
2. 移除对 PreviewCanvas 焦点的依赖
3. 测试快捷键是否正常工作

### 第二步：防止容器元素获取焦点（优先级：高）

1. 在 CollectPage.xaml 中找到所有容器元素
2. 设置 `IsTabStop="False"`
3. 测试焦点是否还会被容器捕获

### 第三步：添加焦点恢复机制（优先级：中）

1. 在 PointerPressed 中记录焦点
2. 在 PointerReleased 中恢复焦点
3. 添加 LostFocus 监控

### 第四步：清理调试代码（优先级：低）

1. 移除或注释掉调试输出
2. 保留必要的日志记录

## 预期效果

1. **快捷键始终可用**：无论焦点在哪里，快捷键都能正常工作
2. **焦点不再丢失**：点击任何区域都不会导致焦点被容器捕获
3. **用户体验提升**：快捷键响应稳定可靠

## 风险评估

| 风险                    | 影响 | 缓解措施                                 |
| --------------------- | -- | ------------------------------------ |
| 页面级别事件可能影响其他控件        | 中  | 只在 CanHandlePageShortcut 返回 true 时处理 |
| IsTabStop 可能影响 Tab 导航 | 低  | 只设置容器元素，不设置交互元素                      |
| 焦点恢复可能导致闪烁            | 低  | 使用 DispatcherQueue 延迟执行              |

## 参考资料

* [UIElement.LostFocus Event - Microsoft Learn](https://learn.microsoft.com/ja-jp/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.lostfocus)

* [FocusManager Class - Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.input.focusmanager)

* [Keyboard interactions - Microsoft Learn](https://learn.microsoft.com/zh-cn/windows/apps/design/input/keyboard-interactions)

* [WinUI Gallery Issue #1927](https://github.com/microsoft/WinUI-Gallery/issues/1927) - 类似的焦点丢失问题

