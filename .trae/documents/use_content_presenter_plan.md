# 使用 ContentPresenter 重建 ImageViewerControl 方案 - 实现计划

## 问题分析

**原问题**：使用单例 ImageViewerControl 时，状态重置逻辑复杂，容易出错，导致切换图片时继承上一张的缩放状态

**解决方案**：每次打开图片时，销毁并重建 ImageViewerControl 控件，确保 100% 初始化状态

## 方案优势

1. **100% 保证不继承状态**：每次都是全新的对象
2. **代码更简洁**：不需要处理复杂的状态重置逻辑
3. **更易维护**：减少出错可能性
4. **性能可接受**：现代 PC 的微小内存分配开销可以忽略

## 实现步骤（已完成）

### [x] 任务 1：修改 XAML，使用 ContentPresenter 替代 ImageViewerControl
- **Priority**: P0
- **Description**: 
  - 将 `<controls:ImageViewerControl .../>` 替换为 `<ContentPresenter x:Name="ViewerContainer" .../>`
  - 保持相同的 Grid 布局位置
- **Success Criteria**: XAML 编译通过
- **Test Requirements**:
  - `programmatic` TR-1.1: XAML 语法正确，无编译错误

### [x] 任务 2：修改 MainPage.xaml.cs，添加字段存储当前 viewer 实例
- **Priority**: P0
- **Description**: 
  - 添加字段 `private ImageViewerControl? _currentViewer;`
  - 用于存储当前显示的 ImageViewerControl 实例
- **Success Criteria**: 字段添加成功
- **Test Requirements**:
  - `programmatic` TR-2.1: 字段声明正确

### [x] 任务 3：修改 ImageGridView_DoubleTapped，创建新实例
- **Priority**: P0
- **Description**: 
  - 每次双击图片时，创建新的 `ImageViewerControl()` 实例
  - 设置 `ViewerContainer.Content = newViewer;`
  - 订阅新实例的 `Closed` 事件
  - 保存到 `_currentViewer` 字段
- **Success Criteria**: 每次打开新图片都创建新实例
- **Test Requirements**:
  - `programmatic` TR-3.1: 正确创建新实例并设置到 ContentPresenter
  - `human-judgement` TR-3.2: 切换图片时不再继承上一张的缩放状态

### [x] 任务 4：修改 ImageViewer_Closed 事件处理
- **Priority**: P0
- **Description**: 
  - 修改事件处理逻辑，使用 `_currentViewer` 替代 `ImageViewer`
  - 关闭时取消订阅事件
  - 清空 `_currentViewer` 和 `ViewerContainer.Content`
- **Success Criteria**: 关闭动画和逻辑正常工作
- **Test Requirements**:
  - `programmatic` TR-4.1: 关闭动画正常执行
  - `human-judgement` TR-4.2: 关闭后正确清理资源

### [x] 任务 5：修改 MainPage_KeyDown 事件处理
- **Priority**: P0
- **Description**: 
  - 修改 Escape 键处理逻辑，使用 `_currentViewer` 替代 `ImageViewer`
- **Success Criteria**: Escape 键能正确关闭图片查看器
- **Test Requirements**:
  - `human-judgement` TR-5.1: 按 Escape 键能正确关闭图片查看器

## 代码修改点

### 1. MainPage.xaml

**替换前**：
```xml
<controls:ImageViewerControl
    x:Name="ImageViewer"
    Visibility="Collapsed"
    Grid.Column="0"
    Grid.ColumnSpan="2"
    Grid.Row="0"
    Grid.RowSpan="3"/>
```

**替换后**：
```xml
<ContentPresenter
    x:Name="ViewerContainer"
    Grid.Column="0"
    Grid.ColumnSpan="2"
    Grid.Row="0"
    Grid.RowSpan="3"/>
```

### 2. MainPage.xaml.cs

**添加字段**：
```csharp
private ImageViewerControl? _currentViewer;
```

**修改 ImageGridView_DoubleTapped**：
```csharp
// 每次打开图片，都 new 一个新的实例
var newViewer = new ImageViewerControl();
ViewerContainer.Content = newViewer;
_currentViewer = newViewer;

// 订阅关闭事件
newViewer.Closed += ImageViewer_Closed;

newViewer.PrepareContent(imageFileInfo);
// ... 其余代码使用 newViewer 替代 ImageViewer
```

**修改 ImageViewer_Closed**：
```csharp
if (_currentViewer != null && _storedImageFileInfo != null)
{
    // ... 使用 _currentViewer 替代 ImageViewer
    
    // 清理
    _currentViewer.Closed -= ImageViewer_Closed;
    ViewerContainer.Content = null;
    _currentViewer = null;
}
```

**修改 MainPage_KeyDown**：
```csharp
if (_currentViewer != null)
{
    if (e.Key == VirtualKey.Escape)
    {
        _currentViewer.PrepareCloseAnimation();
        e.Handled = true;
    }
}
```

## 预期效果

- ✅ 每次打开图片都是全新的 ImageViewerControl 实例
- ✅ 100% 不继承上一张图片的缩放状态
- ✅ ConnectedAnimation 正常工作
- ✅ 关闭动画正常工作
- ✅ 代码更简洁，更易维护
