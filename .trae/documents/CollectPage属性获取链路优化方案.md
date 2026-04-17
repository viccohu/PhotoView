# CollectPage 属性获取链路优化方案（第二轮）

## 一、当前问题诊断

### 1.1 仍然存在的灾难性故障原因

尽管第一轮已将 RatingService、ExifService、MainViewModel 的大部分调用改为 `StorageFilePropertyReader`，但 **MainViewModel.LoadImageInfoSafeAsync（L920）仍然直接调用** `file.Properties.GetImagePropertiesAsync().AsTask(cancellationToken)`，绕过了 StorageFilePropertyReader 的保护。当此调用与 CollectViewModel 的 `HydrateImageMetadataAsync` 对同一文件并发时，仍会触发 `8000FFFF 灾难性故障`。

### 1.2 重复调用分析

以 CollectPage 中**同一张图片**为例，当前完整的属性获取链路如下：

```
文件加载阶段（缩略图列表）:
  ① MainViewModel.LoadImageInfo
     → StorageFilePropertyReader.GetImagePropertiesAsync(file)   → Width/Height/Title

  ② CollectViewModel.StartDeferredImageInfoLoadAsync:
     ├─ HydrateImageMetadataAsync
     │   → StorageFilePropertyReader.GetImagePropertiesAsync(file)   → Width/Height/Title  ⚠️与①重复
     └─ imageInfo.LoadRatingAsync
         → RatingService.GetRatingAsync
           → StorageFilePropertyReader.GetImagePropertiesAsync(file)   → Rating  ⚠️与②重复

预览阶段（选中图片）:
  ③ CollectPage.UpdateSelectedImageUi:
     → PreviewInfoViewModel.LoadFileDetailsAsync
       ├─ StorageFilePropertyReader.GetBasicPropertiesAsync(file)   → Size
       └─ ExifService.GetExifDataAsync
           ├─ StorageFilePropertyReader.GetImagePropertiesAsync(file)   → DateTaken  ⚠️与②重复
           └─ BitmapDecoder + BitmapProperties.GetPropertiesAsync      → 完整EXIF
```

**同一文件的 `GetImagePropertiesAsync` 被调用了 4 次**（①②③中的Rating③中的DateTaken），其中 3 次是重复的。

### 1.3 问题总结

| 问题 | 严重性 | 说明 |
|------|--------|------|
| LoadImageInfoSafeAsync 绕过保护 | **高** | 直接调用导致并发冲突，灾难性故障的根因 |
| LoadImageInfo 与 HydrateImageMetadataAsync 重复 | 中 | 同一文件两次获取 Width/Height/Title |
| HydrateImageMetadataAsync 与 LoadRatingAsync 重复 | 中 | 同一文件两次获取 ImageProperties，仅提取不同字段 |
| ExifService.GetExifDataAsync 重复获取 DateTaken | 低 | 预览时再次获取已在缩略图阶段获取过的属性 |
| RatingService.GetRatingAsync 吞异常 | 中 | L55-57 空 catch 块，违反规则 7.1 |

## 二、优化方案

### 核心设计思路

按照用户的设计意图：
- **缩略图阶段**：一次 `GetImagePropertiesAsync` 同时获取 Width/Height/Title + Rating，缓存到 ImageFileInfo
- **预览阶段**：通过 BitmapDecoder 获取完整 EXIF，不再重复调用 `GetImagePropertiesAsync`

### 步骤 1：修复 LoadImageInfoSafeAsync 绕过保护（高优先级）

**文件**: `MainViewModel.cs` L920

```csharp
// 修改前
var properties = await file.Properties.GetImagePropertiesAsync().AsTask(cancellationToken);

// 修改后
var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(file, cancellationToken);
```

这是灾难性故障的直接根因，必须首先修复。

### 步骤 2：合并 HydrateImageMetadataAsync + LoadRatingAsync（消除重复调用）

**文件**: `CollectViewModel.cs`

当前 `StartDeferredImageInfoLoadAsync` 串行调用：
1. `HydrateImageMetadataAsync` → 获取 Width/Height/Title（已拿到完整 ImageProperties）
2. `imageInfo.LoadRatingAsync` → 再次获取 ImageProperties 仅为了 Rating

**优化方案**：在 `HydrateImageMetadataAsync` 中同时提取 Rating，直接设置到 ImageFileInfo，跳过 `LoadRatingAsync`。

修改 `HydrateImageMetadataAsync`：

```csharp
private async Task HydrateImageMetadataAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
{
    try
    {
        var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(imageInfo.ImageFile, cancellationToken);
        
        // 宽高/标题
        var width = (int)Math.Max(properties.Width, 200);
        var height = (int)Math.Max(properties.Height, 200);
        var orientation = properties.Orientation;
        if (orientation == PhotoOrientation.Rotate90 || ...)
        {
            (width, height) = (height, width);
        }
        imageInfo.UpdateMetadata(width, height, properties.Title);
        
        // 评级 - 从已获取的 properties 中直接提取，无需再次调用
        imageInfo.ApplyLoadedRating(properties.Rating, RatingSource.WinRT, imageInfo.BeginRatingPreload());
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[CollectViewModel] metadata load failed: {ex.Message}");
    }
}
```

修改 `StartDeferredImageInfoLoadAsync`：移除 `LoadRatingAsync` 调用。

```csharp
private async Task StartDeferredImageInfoLoadAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
{
    try
    {
        await Task.Delay(DeferredImageInfoLoadDelayMs, cancellationToken);
        await _metadataHydrationGate.WaitAsync(cancellationToken);
        try
        {
            await HydrateImageMetadataAsync(imageInfo, cancellationToken);
        }
        finally
        {
            _metadataHydrationGate.Release();
        }
        // 不再需要单独调用 LoadRatingAsync，Rating 已在 HydrateImageMetadataAsync 中设置
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[CollectViewModel] deferred info load failed: {ex.Message}");
    }
}
```

**注意**：`ApplyLoadedRating` 和 `BeginRatingPreload` 是 internal 方法，需要在 ImageFileInfo 中调整可见性或提供新的公共方法。具体方案：

在 `ImageFileInfo` 中新增公共方法：

```csharp
public void SetRatingFromProperties(uint rating, RatingSource source)
{
    lock (_thumbnailLoadLock)
    {
        if (_isRatingLoaded)
            return;
        _isRatingLoadRequested = true;
        SetRatingCore(rating);
        RatingSource = source;
        IsRatingLoaded = true;
        IsRatingLoading = false;
    }
}
```

### 步骤 3：优化 ExifService.GetExifDataAsync（消除预览阶段的重复调用）

**文件**: `ExifService.cs`

当前 `GetExifDataAsync` 调用 `StorageFilePropertyReader.GetImagePropertiesAsync` 仅为了获取 `DateTaken`。但 `BitmapDecoder.BitmapProperties.GetPropertiesAsync` 也能获取 `System.Photo.DateTaken`，且已经在代码中有回退逻辑。

**优化方案**：移除 `GetExifDataAsync` 中对 `GetImagePropertiesAsync` 的调用，统一使用 `BitmapProperties` 获取 `DateTaken`。

```csharp
public async Task<ExifData> GetExifDataAsync(StorageFile file, CancellationToken cancellationToken = default)
{
    await _concurrencyLimiter.WaitAsync(cancellationToken);
    try
    {
        var exifData = new ExifData();
        
        // 统一使用 BitmapDecoder + BitmapProperties 获取所有属性（包括 DateTaken）
        using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        
        exifData.Width = decoder.PixelWidth;
        exifData.Height = decoder.PixelHeight;
        // ... 其余不变 ...
        
        try
        {
            var properties = await decoder.BitmapProperties.GetPropertiesAsync(ExifPropertyNames);
            
            // DateTaken - 统一从 BitmapProperties 获取
            if (properties.TryGetValue("System.Photo.DateTaken", out var dateProp) && dateProp.Value is DateTime dateTaken)
            {
                exifData.DateTaken = dateTaken;
            }
            // ... 其余属性解析不变 ...
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExifService] 读取EXIF属性失败: {file.Name}, 错误: {ex.Message}");
        }
        
        return exifData;
    }
    finally
    {
        _concurrencyLimiter.Release();
    }
}
```

这样预览阶段不再调用 `GetImagePropertiesAsync`，仅使用 `BitmapDecoder`（流式读取，不涉及 StorageFile 属性系统）。

### 步骤 4：修复 RatingService 吞异常

**文件**: `RatingService.cs` L55-57

```csharp
// 修改前
catch (Exception ex)
{
}

// 修改后
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"[RatingService] WinRT读取评级失败: {file.Name}, 错误: {ex.Message}");
}
```

### 步骤 5：消除 LoadImageInfo 与 HydrateImageMetadataAsync 的重复

**文件**: `MainViewModel.cs` + `CollectViewModel.cs`

当前流程：
1. `MainViewModel.LoadImageInfo` 创建 ImageFileInfo 时获取一次 Width/Height/Title
2. `CollectViewModel.HydrateImageMetadataAsync` 延迟再次获取 Width/Height/Title

**分析**：`LoadImageInfo` 在文件发现阶段同步获取了精确的宽高信息，而 `HydrateImageMetadataAsync` 是延迟加载，目的是在缩略图加载后更新更精确的元数据。但两者获取的数据完全相同（都来自 `GetImagePropertiesAsync`），所以 `HydrateImageMetadataAsync` 的宽高更新是冗余的。

**优化方案**：在 `HydrateImageMetadataAsync` 中，如果 ImageFileInfo 已有非默认值（Width > 200），则跳过宽高更新，仅设置 Rating。

```csharp
private async Task HydrateImageMetadataAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
{
    try
    {
        var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(imageInfo.ImageFile, cancellationToken);
        
        // 仅当宽高为默认值时才更新（避免与 LoadImageInfo 重复）
        if (imageInfo.Width <= 200 || imageInfo.Height <= 200)
        {
            var width = (int)Math.Max(properties.Width, 200);
            var height = (int)Math.Max(properties.Height, 200);
            var orientation = properties.Orientation;
            if (orientation == PhotoOrientation.Rotate90 || ...)
            {
                (width, height) = (height, width);
            }
            imageInfo.UpdateMetadata(width, height, properties.Title);
        }
        
        // 评级 - 从已获取的 properties 中直接提取
        imageInfo.SetRatingFromProperties(properties.Rating, RatingSource.WinRT);
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[CollectViewModel] metadata load failed: {ex.Message}");
    }
}
```

## 三、优化后的调用链路

```
文件加载阶段（缩略图列表）:
  ① MainViewModel.LoadImageInfo
     → StorageFilePropertyReader.GetImagePropertiesAsync(file)   → Width/Height/Title

  ② CollectViewModel.StartDeferredImageInfoLoadAsync:
     └─ HydrateImageMetadataAsync
         → StorageFilePropertyReader.GetImagePropertiesAsync(file)   → Rating（宽高跳过，已有）
         ✅ 仅1次调用，同时获取 Rating

预览阶段（选中图片）:
  ③ CollectPage.UpdateSelectedImageUi:
     → PreviewInfoViewModel.LoadFileDetailsAsync
       ├─ StorageFilePropertyReader.GetBasicPropertiesAsync(file)   → Size
       └─ ExifService.GetExifDataAsync
           └─ BitmapDecoder + BitmapProperties.GetPropertiesAsync  → 完整EXIF（含DateTaken）
           ✅ 不再调用 GetImagePropertiesAsync
```

**优化前**：同一文件 `GetImagePropertiesAsync` 调用 4 次
**优化后**：同一文件 `GetImagePropertiesAsync` 调用 2 次（①加载 + ②延迟获取Rating），预览阶段 0 次

## 四、修改文件清单

| 文件 | 修改内容 | 优先级 |
|------|----------|--------|
| MainViewModel.cs | L920 改为 StorageFilePropertyReader | **高** |
| ImageFileInfo.cs | 新增 SetRatingFromProperties 公共方法 | 高 |
| CollectViewModel.cs | HydrateImageMetadataAsync 合并 Rating + 移除 LoadRatingAsync 调用 + 跳过已有宽高 | 高 |
| ExifService.cs | GetExifDataAsync 移除 GetImagePropertiesAsync 调用，统一用 BitmapProperties | 中 |
| RatingService.cs | 修复吞异常 | 中 |

## 五、风险评估

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| BitmapProperties 对某些 RAW 格式无法获取 DateTaken | 低 | 已有回退逻辑，DateTaken 显示为空而非崩溃 |
| SetRatingFromProperties 线程安全 | 低 | 使用已有的 _thumbnailLoadLock 保护 |
| HydrateImageMetadataAsync 跳过宽高更新导致 RAW 文件尺寸不准 | 低 | LoadImageInfo 已获取精确值，仅当为默认值(200)时才更新 |
