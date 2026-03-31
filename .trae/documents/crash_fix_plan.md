# 快速滚动崩溃问题修复计划

## 错误分析

### 错误代码
`0xc000027b` - 这是一个 Windows 应用程序崩溃错误，通常与以下问题有关：
1. **异步操作中的异常未处理**
2. **线程安全问题**
3. **访问已释放的对象**

### 当前代码问题

#### 1. `async void` 方法中的异常未处理
```csharp
private async void ImageRepeater_ElementPrepared(...)
{
    if (sender.ItemsSourceView.GetAt(args.Index) is ImageFileInfo imageInfo)
    {
        await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
    }
}
```
- `async void` 方法中的异常会导致应用程序崩溃
- `GetThumbnailAsync` 中的文件操作可能抛出异常

#### 2. 索引访问问题
```csharp
sender.ItemsSourceView.GetAt(args.Index)
```
- 快速滚动时，数据源可能已变化
- 索引可能无效

#### 3. `ViewModel_ThumbnailSizeChanged` 方法
```csharp
private async void ViewModel_ThumbnailSizeChanged(...)
{
    foreach (var imageInfo in ViewModel.Images)
    {
        await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
    }
}
```
- 遍历所有图片，可能导致大量异步操作
- 异常未处理

---

## 实施计划

### [x] 任务 1：添加异常处理到 EnsureThumbnailAsync
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 在 `EnsureThumbnailAsync` 方法中添加 try-catch
  - 捕获所有异常，防止崩溃
- **成功标准**：异常不会导致崩溃
- **测试要求**：
  - `programmatic` TR-1.1：文件访问异常不会崩溃

### [x] 任务 2：添加异常处理到 ElementPrepared
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 在 `ImageRepeater_ElementPrepared` 中添加 try-catch
  - 检查索引有效性
- **成功标准**：快速滚动不会崩溃
- **测试要求**：
  - `programmatic` TR-2.1：快速滚动不崩溃

### [x] 任务 3：添加异常处理到 ViewModel_ThumbnailSizeChanged
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 添加 try-catch 处理
  - 使用 Task.WhenAll 并行加载
- **成功标准**：切换大小不会崩溃
- **测试要求**：
  - `programmatic` TR-3.1：切换大小不崩溃

### [x] 任务 4：添加索引有效性检查
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 在访问 `ItemsSourceView.GetAt` 前检查索引
  - 检查 `args.Index >= 0 && args.Index < sender.ItemsSourceView.Count`
- **成功标准**：无效索引不会导致崩溃
- **测试要求**：
  - `programmatic` TR-4.1：无效索引不崩溃

### [x] 任务 5：优化 GetThumbnailAsync 异常处理
- **优先级**：P0
- **依赖**：无
- **描述**：
  - 添加 try-catch 处理文件操作异常
  - 返回默认图片或 null
- **成功标准**：文件操作异常不会崩溃
- **测试要求**：
  - `programmatic` TR-5.1：文件异常不崩溃

---

## 技术要点

### 异常处理模式
```csharp
public async Task EnsureThumbnailAsync(ThumbnailSize size)
{
    if (Thumbnail != null || _isLoading)
        return;

    _isLoading = true;
    try
    {
        Thumbnail = await GetThumbnailAsync(size);
    }
    catch (Exception)
    {
        // 静默处理异常，防止崩溃
    }
    finally
    {
        _isLoading = false;
    }
}
```

### 索引有效性检查
```csharp
private async void ImageRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
{
    try
    {
        if (args.Index < 0 || args.Index >= sender.ItemsSourceView.Count)
            return;

        if (sender.ItemsSourceView.GetAt(args.Index) is ImageFileInfo imageInfo)
        {
            await imageInfo.EnsureThumbnailAsync(ViewModel.ThumbnailSize);
        }
    }
    catch (Exception)
    {
        // 静默处理异常
    }
}
```

---

## 预期结果
1. 快速滚动不会崩溃
2. 文件操作异常不会崩溃
3. 无效索引不会崩溃
4. 切换大小不会崩溃
