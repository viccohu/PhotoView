# 方向键匀速切换图片优化计划

## 问题诊断

通过检查代码，发现以下问题：

### 问题 1：当前实现没有切换图片功能

**文件**：`d:\PhotoView\PhotoView\Controls\ImageViewerControl.xaml.cs`

**当前代码**（第 608-613 行）：
```csharp
else if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.Right ||
         e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.Down)
{
    e.Handled = true;
    System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 阻止了方向键 {e.Key}");
}
```

**问题分析**：
- 当前只是阻止了方向键事件，没有实现切换图片的功能
- 用户需要实现匀速切换到下一张图片的功能

### 问题 2：没有速度控制

**问题分析**：
- 用户要求"太快会导致卡顿，取值在一个不卡的速度下尽量快的一个速度"
- 需要使用定时器来控制切换速度
- 需要确定合适的切换间隔时间

## 修复方案

### 方案：实现匀速切换图片功能

**修改文件**：`d:\PhotoView\PhotoView\Controls\ImageViewerControl.xaml.cs`

**实现步骤**：

1. **添加定时器控制切换速度**
   - 使用 DispatcherTimer 来控制切换速度
   - 设置合适的间隔时间（建议 100-200ms）

2. **实现切换图片的逻辑**
   - 获取当前图片在列表中的索引
   - 根据方向键计算下一个图片的索引
   - 调用 ViewModel.SetBasicInfo 切换图片

3. **处理按键按下和释放**
   - 在 KeyDown 事件中启动定时器
   - 在 KeyUp 事件中停止定时器
   - 确保按键释放后停止切换

## 实施步骤

### 步骤 1：添加定时器和相关字段

**文件**：`d:\PhotoView\PhotoView\Controls\ImageViewerControl.xaml.cs`

**添加字段**：
```csharp
private DispatcherTimer? _navigationTimer;
private Windows.System.VirtualKey _currentNavigationKey;
private int _navigationDirection;
```

### 步骤 2：初始化定时器

**位置**：构造函数中

**代码**：
```csharp
_navigationTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromMilliseconds(150) // 150ms 间隔，平衡速度和流畅性
};
_navigationTimer.Tick += NavigationTimer_Tick;
```

### 步骤 3：实现切换图片的方法

**方法**：
```csharp
private void NavigateToNextImage(int direction)
{
    if (_imageFileInfo == null || ViewModel == null)
        return;
    
    // 获取当前图片在列表中的索引
    var currentIndex = ViewModel.GetCurrentImageIndex(_imageFileInfo);
    if (currentIndex == -1)
        return;
    
    // 计算下一个图片的索引
    var nextIndex = currentIndex + direction;
    
    // 检查边界
    var images = ViewModel.GetImages();
    if (nextIndex < 0 || nextIndex >= images.Count)
        return;
    
    // 切换到下一张图片
    var nextImage = images[nextIndex];
    _imageFileInfo = nextImage;
    ViewModel.SetBasicInfo(nextImage);
}
```

### 步骤 4：修改 OnKeyDown 方法

**修改内容**：
```csharp
else if (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.Right ||
         e.Key == Windows.System.VirtualKey.Up || e.Key == Windows.System.VirtualKey.Down)
{
    e.Handled = true;
    System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyDown: 方向键 {e.Key}");
    
    // 计算方向
    _navigationDirection = (e.Key == Windows.System.VirtualKey.Left || e.Key == Windows.System.VirtualKey.Up) ? -1 : 1;
    _currentNavigationKey = e.Key;
    
    // 立即切换一次
    NavigateToNextImage(_navigationDirection);
    
    // 启动定时器，实现连续切换
    _navigationTimer?.Start();
}
```

### 步骤 5：添加 OnKeyUp 方法

**方法**：
```csharp
protected override void OnKeyUp(KeyRoutedEventArgs e)
{
    try
    {
        if (e.Key == _currentNavigationKey)
        {
            // 停止定时器
            _navigationTimer?.Stop();
            System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyUp: 停止定时器，按键={e.Key}");
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[ImageViewerControl] OnKeyUp 异常: {ex.Message}");
    }
    
    base.OnKeyUp(e);
}
```

### 步骤 6：实现定时器 Tick 事件

**方法**：
```csharp
private void NavigationTimer_Tick(object? sender, object e)
{
    NavigateToNextImage(_navigationDirection);
}
```

### 步骤 7：在 ImageViewerViewModel 中添加辅助方法

**文件**：`d:\PhotoView\PhotoView\ViewModels\ImageViewerViewModel.cs`

**方法**：
```csharp
public int GetCurrentImageIndex(ImageFileInfo imageFileInfo)
{
    // 需要从 MainViewModel 获取图片列表
    // 这里需要通过依赖注入或其他方式获取
    return -1; // 临时返回
}

public IReadOnlyList<ImageFileInfo> GetImages()
{
    // 需要从 MainViewModel 获取图片列表
    return new List<ImageFileInfo>(); // 临时返回
}
```

## 速度选择建议

### 推荐间隔时间：150ms

**理由**：
1. **不卡顿**：150ms 的间隔足够让图片加载完成
2. **流畅性**：每秒可以切换约 6-7 张图片，速度适中
3. **用户体验**：不会太快导致用户无法控制，也不会太慢导致用户等待

### 其他可选间隔时间：
- **100ms**：更快，但可能在某些情况下会卡顿
- **200ms**：更慢，但更稳定
- **250ms**：最慢，适合性能较差的设备

## 注意事项

1. **线程安全**：确保在 UI 线程上操作 UI 元素
2. **资源释放**：在控件卸载时停止并释放定时器
3. **边界检查**：确保不会超出图片列表的边界
4. **性能优化**：避免在切换过程中执行耗时的操作

## 风险评估

**风险**：切换速度太快可能导致卡顿
**对策**：使用 150ms 的间隔时间，并在实际测试中调整

**风险**：定时器可能导致内存泄漏
**对策**：在控件卸载时停止并释放定时器

**风险**：切换过程中可能触发其他操作
**对策**：确保切换过程中禁用其他操作，或者使用锁机制
