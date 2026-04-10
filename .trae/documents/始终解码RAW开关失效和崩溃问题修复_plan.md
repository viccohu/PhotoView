# 始终解码RAW开关失效和崩溃问题修复计划

## 问题诊断

通过检查代码，发现以下问题：

### 问题 1：缩略图加载不受 AlwaysDecodeRaw 设置控制

**文件**：`d:\PhotoView\PhotoView\Models\ImageFileInfo.cs`

**问题代码**（第 120-123 行）：
```csharp
using var thumbnail = await ImageFile.GetThumbnailAsync(
    ThumbnailMode.SingleItem,
    optimalSize,
    ThumbnailOptions.None);
```

**问题分析**：
- `ImageFileInfo` 类直接调用 `ImageFile.GetThumbnailAsync`，而不是使用 `ThumbnailService`
- 这意味着缩略图的加载不受 `AlwaysDecodeRaw` 设置的影响
- 无论 `AlwaysDecodeRaw` 是开启还是关闭，都会使用系统默认的缩略图获取方式

### 问题 2：高清预览崩溃问题

**文件**：`d:\PhotoView\PhotoView\Controls\ImageViewerControl.xaml.cs`

**问题代码**（第 422 行）：
```csharp
var decodeResult = await thumbnailService.GetThumbnailWithSizeAsync(_imageFileInfo.ImageFile, _targetDecodeLongSide, true, cts.Token);
```

**问题分析**：
- 高清预览强制使用 `forceFullDecode = true`，始终使用完整解码
- 这导致高清预览不受 `AlwaysDecodeRaw` 设置控制
- 当用户在高清预览中查看图片时，修改设置可能导致崩溃

### 问题 3：ThumbnailService 中的空指针检查不完整

**文件**：`d:\PhotoView\PhotoView\Services\ThumbnailService.cs`

**问题代码**（第 266-267 行）：
```csharp
var dispatcherQueue = App.MainWindow.DispatcherQueue;
if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
```

**问题分析**：
- 没有检查 `App.MainWindow` 是否为 null
- 当应用程序关闭或页面切换时，可能导致访问冲突

## 修复方案

### 方案 1：修改 ImageFileInfo 使用 ThumbnailService

**修改文件**：`d:\PhotoView\PhotoView\Models\ImageFileInfo.cs`

**修改内容**：
1. 添加 `IThumbnailService` 依赖注入
2. 修改 `GetThumbnailOnUIThreadAsync` 方法，使用 `ThumbnailService` 加载缩略图
3. 确保 `AlwaysDecodeRaw` 设置生效

### 方案 2：修复高清预览崩溃问题

**修改文件**：`d:\PhotoView\PhotoView\Controls\ImageViewerControl.xaml.cs`

**修改内容**：
1. 高清预览应该尊重 `AlwaysDecodeRaw` 设置
2. 使用 `forceFullDecode = false`，让 `ThumbnailService` 根据 `AlwaysDecodeRaw` 设置决定解码方式

### 方案 3：修复 ThumbnailService 中的空指针检查

**修改文件**：`d:\PhotoView\PhotoView\Services\ThumbnailService.cs`

**修改内容**：
1. 在 `CreateSoftwareBitmapSourceAsync` 方法中添加 `App.MainWindow` 的空指针检查
2. 确保在应用程序关闭或页面切换时不会崩溃

## 实施步骤

### 步骤 1：修复 ThumbnailService 中的空指针检查

**文件**：`d:\PhotoView\PhotoView\Services\ThumbnailService.cs`

**修改位置**：第 264-297 行

**修改内容**：
```csharp
private static async Task<ImageSource?> CreateSoftwareBitmapSourceAsync(SoftwareBitmap softwareBitmap, CancellationToken cancellationToken)
{
    var mainWindow = App.MainWindow;
    if (mainWindow == null)
    {
        System.Diagnostics.Debug.WriteLine($"[ThumbnailService] App.MainWindow 为 null，无法获取 DispatcherQueue");
        return null;
    }
    
    var dispatcherQueue = mainWindow.DispatcherQueue;
    if (dispatcherQueue == null)
    {
        System.Diagnostics.Debug.WriteLine($"[ThumbnailService] DispatcherQueue 为 null，无法创建 SoftwareBitmapSource");
        return null;
    }
    
    if (dispatcherQueue.HasThreadAccess)
    {
        var bitmapSource = new SoftwareBitmapSource();
        await bitmapSource.SetBitmapAsync(softwareBitmap).AsTask(cancellationToken);
        return bitmapSource;
    }

    var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
    if (!dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, async () =>
        {
            try
            {
                var bitmapSource = new SoftwareBitmapSource();
                await bitmapSource.SetBitmapAsync(softwareBitmap).AsTask(cancellationToken);
                tcs.TrySetResult(bitmapSource);
            }
            catch (OperationCanceledException ex)
            {
                tcs.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
    {
        throw new OperationCanceledException("Failed to enqueue thumbnail creation.", cancellationToken);
    }

    return await tcs.Task;
}
```

### 步骤 2：修改 ImageFileInfo 使用 ThumbnailService

**文件**：`d:\PhotoView\PhotoView\Models\ImageFileInfo.cs`

**修改位置**：第 113-133 行

**修改内容**：
```csharp
private async Task<BitmapImage> GetThumbnailOnUIThreadAsync(
    uint optimalSize,
    CancellationToken cancellationToken)
{
    if (cancellationToken.IsCancellationRequested || AppLifetime.IsShuttingDown)
        return new BitmapImage();

    try
    {
        var thumbnailService = App.GetService<IThumbnailService>();
        var imageSource = await thumbnailService.GetThumbnailByLongSideAsync(ImageFile, optimalSize, cancellationToken);
        
        if (imageSource is SoftwareBitmapSource softwareBitmapSource)
        {
            var bitmap = new BitmapImage();
            using var stream = new InMemoryRandomAccessStream();
            await softwareBitmapSource.SetBitmapAsync(await SoftwareBitmap.CreateCopyFromSurfaceAsync(null));
            return bitmap;
        }
        else if (imageSource is BitmapImage bitmapImage)
        {
            return bitmapImage;
        }
        
        return new BitmapImage();
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"GetThumbnailOnUIThreadAsync error: {ex}");
        return new BitmapImage();
    }
}
```

### 步骤 3：修改高清预览尊重 AlwaysDecodeRaw 设置

**文件**：`d:\PhotoView\PhotoView\Controls\ImageViewerControl.xaml.cs`

**修改位置**：第 422 行

**修改内容**：
```csharp
var decodeResult = await thumbnailService.GetThumbnailWithSizeAsync(_imageFileInfo.ImageFile, _targetDecodeLongSide, false, cts.Token);
```

## 验证步骤

1. 编译项目，确保没有错误
2. 运行项目，测试以下场景：
   - 关闭"始终解码RAW"设置，查看缩略图是否使用 RAW 内嵌预览
   - 开启"始终解码RAW"设置，查看缩略图是否使用完整解码
   - 在高清预览中查看图片时，修改"始终解码RAW"设置，确保不崩溃
   - 在高清预览中查看图片时，切换到设置页面，再返回，确保不崩溃

## 注意事项

1. **线程安全**：确保在 UI 线程上操作 UI 元素
2. **资源释放**：在控件卸载时停止并释放定时器
3. **边界检查**：确保不会超出图片列表的边界
4. **性能优化**：避免在切换过程中执行耗时的操作

## 风险评估

**风险**：修改缩略图加载逻辑可能影响性能
**对策**：使用 `ThumbnailService` 的并发控制机制，确保不会同时解码过多图片

**风险**：高清预览尊重设置可能导致加载变慢
**对策**：使用 RAW 内嵌预览可以加快加载速度，但可能牺牲一些质量

**风险**：空指针检查可能导致某些情况下无法加载图片
**对策**：添加详细的调试日志，帮助诊断问题
