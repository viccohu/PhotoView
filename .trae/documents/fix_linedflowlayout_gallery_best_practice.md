# LinedFlowLayout 布局优化方案 - 基于 WinUI 3 Gallery 最佳实践

## 一、当前代码的致命错误

### 错误 1：每次切换都 new LinedFlowLayout()
```csharp
// 当前代码（错误）
var newLayout = new LinedFlowLayout { ... };
ImageItemsView.Layout = newLayout;
ImageLinedFlowLayout = newLayout;
```
**问题**：ItemsView 内部缓存、选中状态、虚拟化容器彻底混乱

### 错误 2：ItemsSource = null 后再绑定
```csharp
// 当前代码（错误）
ImageItemsView.ItemsSource = null;
await Task.Delay(50);
// ...
ImageItemsView.ItemsSource = ViewModel.Images;
```
**问题**：旧数据还在渲染，新数据又来了 → 重叠、残留

### 错误 3：使用 async lambda
```csharp
// 当前代码（错误）
DispatcherQueue.TryEnqueue(..., async () => { ... });
```
**问题**：可能导致线程问题

## 二、Gallery 最佳实践

### 核心原则
1. **不频繁替换 Layout 实例** - XAML 中固定 Layout
2. **用 ObservableCollection + Clear()** - 触发 Reset 通知
3. **完整清空重置流程** - 不需要 ItemsSource = null

### Gallery 标准流程
```csharp
// 1. 清空选中
MyItemsView.DeselectAll();

// 2. 清空数据源（触发 Reset，控件彻底清空）
ViewModel.Images.Clear();

// 3. 强制布局刷新（不 new，只刷新）
MyLinedFlowLayout.InvalidateLayout();
MyItemsView.InvalidateMeasure();
MyItemsView.UpdateLayout();

// 4. 加载新数据
await ViewModel.LoadNewFolderAsync(folderNode);

// 5. 滚动回顶部
MyScrollView?.ScrollTo(0, 0);
```

## 三、优化方案

### 修改 1：ViewModel_ImagesChanged - 不再 new Layout

**修改前**：
```csharp
private void ViewModel_ImagesChanged(object? sender, EventArgs e)
{
    DispatcherQueue.TryEnqueue(..., async () =>
    {
        ImageItemsView.ItemTransitionProvider = null;
        ImageItemsView.ItemsSource = null;
        await Task.Delay(50);
        // ...
        var newLayout = new LinedFlowLayout { ... };
        ImageItemsView.Layout = newLayout;
        ImageLinedFlowLayout = newLayout;
        ImageItemsView.ItemsSource = ViewModel.Images;
        // ...
    });
}
```

**修改后**：
```csharp
private void ViewModel_ImagesChanged(object? sender, EventArgs e)
{
    if (_isUnloaded || AppLifetime.IsShuttingDown)
        return;

    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[ImagesChanged] 触发, Images.Count={ViewModel.Images.Count}");

            // 1. 清空选中状态
            ClearItemsViewSelection();

            // 2. 布局重置（关键：不 new，只刷新）
            ImageLinedFlowLayout.InvalidateLayout();
            ImageItemsView.InvalidateMeasure();
            ImageItemsView.UpdateLayout();

            // 3. 滚动到顶部
            _imageScrollView?.ScrollTo(0, 0);

            // 4. 加载缩略图
            QueueVisibleThumbnailLoad();

            System.Diagnostics.Debug.WriteLine($"[ImagesChanged] 完成");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImagesChanged] 异常: {ex}");
        }
    });
}
```

### 修改 2：MainViewModel.LoadImagesAsync - 使用 Clear()

**修改前**：
```csharp
var oldImages = Images.ToList();
ImagesChanged?.Invoke(this, EventArgs.Empty);
foreach (var image in oldImages) { ... }
oldImages.Clear();
Images.Clear();
```

**修改后**：
```csharp
public async System.Threading.Tasks.Task LoadImagesAsync(FolderNode folderNode)
{
    _loadImagesCts?.Cancel();
    _loadImagesCts = new CancellationTokenSource();
    var cancellationToken = _loadImagesCts.Token;

    SelectedFolder = folderNode;
    UpdateBreadcrumbPath(folderNode);

    // 1. 清空旧数据（触发 NotifyCollectionChangedAction.Reset）
    Images.Clear();

    // 2. 通知 UI 开始刷新
    ImagesChanged?.Invoke(this, EventArgs.Empty);

    if (folderNode?.Folder == null)
        return;

    try
    {
        var result = folderNode.Folder.CreateFileQueryWithOptions(new QueryOptions());
        uint index = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await result.GetFilesAsync(index, PageSize);
            if (batch.Count == 0)
                break;

            var imageInfos = await System.Threading.Tasks.Task.Run(async () =>
            {
                var tasks = new List<Task<ImageFileInfo?>>();

                foreach (var file in batch)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (IsImageFile(file))
                    {
                        tasks.Add(LoadImageInfoSafeAsync(file, cancellationToken));
                    }
                }

                var results = await Task.WhenAll(tasks);
                return results.Where(r => r != null).Cast<ImageFileInfo>().ToList();
            }, cancellationToken);

            if (!cancellationToken.IsCancellationRequested)
            {
                foreach (var info in imageInfos)
                {
                    info.UpdateDisplaySize(ThumbnailSize);
                    Images.Add(info);
                }
            }

            index += PageSize;
        }

        System.Diagnostics.Debug.WriteLine($"[LoadImagesAsync] 加载完成, Images.Count={Images.Count}");
    }
    catch (OperationCanceledException)
    {
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"LoadImagesAsync error: {ex}");
    }
}
```

### 修改 3：XAML - 保持固定 Layout

**当前 XAML（正确）**：
```xml
<ItemsView.Layout>
    <LinedFlowLayout
        x:Name="ImageLinedFlowLayout"
        ItemsJustification="Start"
        ItemsStretch="None"
        ItemsInfoRequested="ImageLinedFlowLayout_ItemsInfoRequested"
        LineHeight="{x:Bind ViewModel.ThumbnailHeight, Mode=OneWay}"
        LineSpacing="8"
        MinItemSpacing="8" />
</ItemsView.Layout>
```
**无需修改**，保持固定 Layout 即可。

## 四、执行任务

| 序号 | 任务 | 说明 |
|------|------|------|
| 1 | 修改 ViewModel_ImagesChanged | 移除 new Layout，使用 InvalidateLayout() |
| 2 | 修改 MainViewModel.LoadImagesAsync | 简化流程，先 Clear() 再加载 |
| 3 | 构建测试 | 验证修复效果 |

## 五、关键改进对比

| 方面 | 修改前 | 修改后 |
|------|--------|--------|
| Layout 处理 | 每次 new LinedFlowLayout() | 固定 Layout，只调用 InvalidateLayout() |
| 数据源处理 | ItemsSource = null 再绑定 | 使用 Images.Clear() 触发 Reset |
| 线程模型 | async lambda | 同步 lambda |
| 等待时间 | Task.Delay(50) x 2 | 无需等待 |
| 动画处理 | 手动禁用/启用 | 无需处理 |
