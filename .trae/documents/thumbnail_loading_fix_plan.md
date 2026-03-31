# 缩略图加载优化计划 - 解决快速滚动崩溃

## 问题分析

根据文档分析，当前实现存在以下致命问题：

### 1. `ElementPrepared + async` 问题（最大雷点）
```
流程：元素创建 → 触发 ElementPrepared → 开始 await GetThumbnailAsync() 
      → 用户快速滚动 → 元素被回收复用 → await 结束 → 写入错误对象
结果：UI 错乱、空引用、崩溃
```

### 2. 全量遍历 UI（严重性能问题）
```csharp
for (var i = 0; i < ImageRepeater.ItemsSourceView.Count; i++)
```
- Repeater 是虚拟化的，大部分元素是 null
- 强行操作虚拟化系统，滚动时直接崩溃

### 3. UI 控件结构查找（极其脆弱）
```csharp
GetImageAndIndicatorFromContentControl(control)
```
- 依赖视觉树结构
- Recycle 后结构可能变化
- 极易 Null / 崩溃

---

## 解决方案：数据驱动模式

### 核心原则
> **UI 不负责加载图片，数据自己负责**

```
数据模型(ImageFileInfo)
    ↓
异步加载
    ↓
属性变更（INotifyPropertyChanged）
    ↓
UI 自动更新（绑定）
```

---

## 实施计划

### [x] 任务 1：在 ImageFileInfo 中添加 Thumbnail 属性
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 添加 `Thumbnail` 属性（BitmapImage? 类型）
  - 添加 `_isLoading` 防重入标志
  - 实现 `INotifyPropertyChanged` 通知
- **成功标准**：Thumbnail 属性可绑定
- **测试要求**：
  - `programmatic` TR-1.1：属性变更时触发 PropertyChanged 事件

### [x] 任务 2：添加 EnsureThumbnailAsync 方法
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 添加 `EnsureThumbnailAsync(ThumbnailSize size)` 方法
  - 实现防重入逻辑（检查 `_isLoading`）
  - 加载完成后设置 Thumbnail 属性
- **成功标准**：方法可被安全调用
- **测试要求**：
  - `programmatic` TR-2.1：重复调用不会重复加载

### [x] 任务 3：修改 XAML 绑定
- **优先级**：P0
- **依赖**：任务 1
- **描述**：
  - 将 Image 的 Source 绑定到 `{x:Bind Thumbnail, Mode=OneWay}`
  - ProgressRing 绑定到加载状态
- **成功标准**：XAML 编译无错误
- **测试要求**：
  - `programmatic` TR-3.1：绑定正常工作

### [x] 任务 4：重构 ElementPrepared 事件
- **优先级**：P0
- **依赖**：任务 2
- **描述**：
  - 只触发数据加载，不操作 UI
  - 使用 DataContext 获取 ImageFileInfo
- **成功标准**：事件处理不操作 UI 控件
- **测试要求**：
  - `programmatic` TR-4.1：快速滚动不崩溃

### [x] 任务 5：删除问题代码
- **优先级**：P0
- **依赖**：任务 4
- **描述**：
  - 删除 `UpdateThumbnails()` 方法
  - 删除 `GetImageAndIndicatorFromContentControl()` 方法
  - 删除 `GetImageFromContentControl()` 方法
- **成功标准**：代码更简洁
- **测试要求**：
  - `programmatic` TR-5.1：编译无错误

### [x] 任务 6：优化选择状态管理
- **优先级**：P1
- **依赖**：任务 5
- **描述**：
  - 在 ImageFileInfo 中添加 `IsSelected` 属性
  - 使用绑定驱动选择状态，而不是遍历 UI
- **成功标准**：选择状态通过绑定更新
- **测试要求**：
  - `programmatic` TR-6.1：选择状态正常工作

---

## 技术要点

### ImageFileInfo 改造
```csharp
private BitmapImage? _thumbnail;
private bool _isLoading;

public BitmapImage? Thumbnail
{
    get => _thumbnail;
    private set => SetProperty(ref _thumbnail, value);
}

public async Task EnsureThumbnailAsync(ThumbnailSize size)
{
    if (Thumbnail != null || _isLoading)
        return;

    _isLoading = true;
    try
    {
        Thumbnail = await GetThumbnailAsync(size);
    }
    finally
    {
        _isLoading = false;
    }
}
```

### XAML 绑定
```xml
<Image Source="{x:Bind Thumbnail, Mode=OneWay}" Stretch="UniformToFill"/>
<ProgressRing IsActive="{x:Bind Thumbnail, Converter={StaticResource NullToBoolConverter}, Mode=OneWay}"/>
```

### ElementPrepared 重构
```csharp
private async void ImageRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
{
    if (sender.ItemsSourceView.GetAt(args.Index) is ImageFileInfo imageInfo)
    {
        await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
    }
}
```

---

## 预期结果
1. 快速滚动不再崩溃
2. 图片不会错位
3. 代码更简洁，符合 MVVM 架构
4. 性能更好，不破坏虚拟化
