# CollectPage EXIF 获取链路优化方案（第三轮）

## 一、当前问题诊断

### 1.1 上一轮修改导致 DateTaken 丢失

上一轮将 `ExifService.GetExifDataAsync` 中对 `StorageFilePropertyReader.GetImagePropertiesAsync` 的调用移除，改为仅通过 `BitmapDecoder.BitmapProperties.GetPropertiesAsync` 获取 `DateTaken`。

**问题**：`BitmapProperties.GetPropertiesAsync` 对很多 RAW 格式（`.cr2`, `.nef`, `.arw` 等）**不支持** `System.Photo.DateTaken`，因为 RAW 格式的 BitmapDecoder 通常不实现 `BitmapProperties` 接口。这导致信息抽屉中的拍摄时间显示为 `----`。

**根因**：`ImageProperties.DateTaken` 通过 Windows 属性系统（Shell 属性处理器）获取，对 RAW 格式有 codec 支持；而 `BitmapProperties` 直接读 EXIF IFD，很多 RAW codec 不暴露此接口。

### 1.2 仍然存在的并发/重入风险

#### 风险 A：`GetBasicPropertiesAsync` 与 `GetImagePropertiesAsync` 共用同一信号量但不互斥

`StorageFilePropertyReader` 中 `GetBasicPropertiesAsync` 和 `GetImagePropertiesAsync` 共用同一个 `FileGate`（基于文件路径），信号量相同，所以**同一文件的 Basic 和 Image 属性访问是串行的**。

但 Windows 属性系统内部，`GetBasicPropertiesAsync` 和 `GetImagePropertiesAsync` **可能共享底层 COM 对象**。当两者对同一文件并发调用时（即使通过了我们的信号量，因为它们用的是同一个信号量所以不会并发），实际上不存在并发风险。

**结论**：当前 `StorageFilePropertyReader` 的设计已经正确保护了同一文件的并发访问。

#### 风险 B：CollectPage 预览阶段的并发场景

当用户选中图片时，以下操作可能**同时或几乎同时**触发：

```
选中图片 →
  ├─ 1. CollectViewModel.HydrateImageMetadataAsync（如果延迟加载还没完成）
  │     → StorageFilePropertyReader.GetImagePropertiesAsync(file) → Rating + 宽高
  │
  ├─ 2. CollectPage.UpdateSelectedImageUi
  │     → PreviewInfoViewModel.LoadFileDetailsAsync
  │       ├─ StorageFilePropertyReader.GetBasicPropertiesAsync(file) → Size
  │       └─ ExifService.GetExifDataAsync
  │           └─ BitmapDecoder + BitmapProperties → EXIF
  │
  └─ 3. ThumbnailService.CreateCacheKeyAsync
        → StorageFilePropertyReader.GetBasicPropertiesAsync(file) → 缓存键
```

- ① 和 ② 中的 `GetBasicPropertiesAsync` 对同一文件：通过 `StorageFilePropertyReader` 信号量串行，**安全**
- ① 和 ② 中的 `GetImagePropertiesAsync`：① 走 `StorageFilePropertyReader`，② 不再调用 `GetImagePropertiesAsync`，**安全**
- ② 的 `BitmapDecoder` 是流式读取，不涉及属性系统，**安全**

**结论**：经过前两轮修复后，并发/重入风险已基本消除。唯一的问题是 DateTaken 丢失。

#### 风险 C：MainViewModel 中 Rating 预加载与 HydrateImageMetadataAsync 的竞争

MainViewModel 中 `QueueRatingPreloadForCurrentImages` 会调用 `RatingService.GetRatingAsync` → `StorageFilePropertyReader.GetImagePropertiesAsync`，而 `HydrateImageMetadataAsync` 也调用 `StorageFilePropertyReader.GetImagePropertiesAsync`。两者对同一文件通过信号量串行，**安全**。

但 CollectViewModel 中上一轮已将 Rating 合并到 `HydrateImageMetadataAsync`，所以 CollectViewModel 不再有此竞争。

### 1.3 重复调用仍然存在

当前 CollectPage 中同一文件的 `GetImagePropertiesAsync` 调用链路：

```
分批加载阶段:
  ① CollectViewModel.HydrateImageMetadataAsync
     → StorageFilePropertyReader.GetImagePropertiesAsync(file) → Rating + 宽高（1次）

预览阶段（选中图片）:
  ② PreviewInfoViewModel.LoadFileDetailsAsync
     ├─ StorageFilePropertyReader.GetBasicPropertiesAsync(file) → Size（1次）
     └─ ExifService.GetExifDataAsync
         └─ BitmapDecoder + BitmapProperties → EXIF（0次 GetImagePropertiesAsync）
         ⚠️ DateTaken 丢失！
```

## 二、优化方案

### 核心设计思路

按用户需求：
- **分批加载时**：一次 `GetImagePropertiesAsync` 同时获取 Width/Height/Title + Rating + DateTaken，缓存到 ImageFileInfo
- **预览时**：从缓存读取 DateTaken，通过 BitmapDecoder 获取其余 EXIF，不再调用 `GetImagePropertiesAsync`

### 步骤 1：在 ImageFileInfo 中缓存 DateTaken

**文件**: `ImageFileInfo.cs`

新增字段和属性：

```csharp
private DateTime? _dateTaken;
private ExifData? _cachedExifData;

public DateTime? DateTaken
{
    get => _dateTaken;
    private set => SetProperty(ref _dateTaken, value);
}

public ExifData? CachedExifData
{
    get => _cachedExifData;
    private set => SetProperty(ref _cachedExifData, value);
}
```

新增方法，从 `ImageProperties` 中提取并缓存 DateTaken：

```csharp
public void SetDateTakenFromProperties(DateTime? dateTaken)
{
    DateTaken = dateTaken;
}
```

### 步骤 2：在 HydrateImageMetadataAsync 中同时提取 DateTaken

**文件**: `CollectViewModel.cs`

```csharp
private static async Task HydrateImageMetadataAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
{
    try
    {
        var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(imageInfo.ImageFile, cancellationToken);

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

        imageInfo.SetRatingFromProperties(properties.Rating, RatingSource.WinRT);

        // 缓存 DateTaken
        if (properties.DateTaken != DateTimeOffset.MinValue && properties.DateTaken != default)
        {
            imageInfo.SetDateTakenFromProperties(properties.DateTaken.DateTime);
        }
    }
    catch (OperationCanceledException) { }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[CollectViewModel] metadata load failed: {ex.Message}");
    }
}
```

### 步骤 3：修复 ExifService.GetExifDataAsync 恢复 DateTaken 获取

**文件**: `ExifService.cs`

恢复 `GetImagePropertiesAsync` 调用以获取 DateTaken，但增加优化：**如果调用方已缓存了 DateTaken，则跳过此调用**。

方案 A（推荐）：`GetExifDataAsync` 增加可选参数 `DateTime? cachedDateTaken`：

```csharp
public async Task<ExifData> GetExifDataAsync(
    StorageFile file,
    CancellationToken cancellationToken = default,
    DateTime? cachedDateTaken = null)
{
    await _concurrencyLimiter.WaitAsync(cancellationToken);
    try
    {
        var exifData = new ExifData();

        // 如果已有缓存的 DateTaken，直接使用
        if (cachedDateTaken.HasValue)
        {
            exifData.DateTaken = cachedDateTaken;
        }

        using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        // ... 其余不变 ...

        try
        {
            var properties = await decoder.BitmapProperties.GetPropertiesAsync(ExifPropertyNames);

            // 仅当没有缓存 DateTaken 时，尝试从 BitmapProperties 获取
            if (!exifData.DateTaken.HasValue)
            {
                if (properties.TryGetValue("System.Photo.DateTaken", out var dateProp) && dateProp.Value is DateTime dateTaken)
                {
                    exifData.DateTaken = dateTaken;
                }
            }
            // ... 其余属性解析不变 ...
        }
        // ...
    }
}
```

方案 B（备选）：在 `GetExifDataAsync` 内部恢复 `GetImagePropertiesAsync` 调用，仅获取 DateTaken：

```csharp
// 在 BitmapDecoder 之前
if (!cachedDateTaken.HasValue)
{
    try
    {
        var imageProperties = await StorageFilePropertyReader.GetImagePropertiesAsync(file, cancellationToken);
        if (imageProperties.DateTaken != DateTimeOffset.MinValue && imageProperties.DateTaken != default)
        {
            exifData.DateTaken = imageProperties.DateTaken.DateTime;
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[ExifService] ImageProperties 获取失败: {ex.Message}");
    }
}
```

**推荐方案 A**，因为：
- CollectPage 场景下，DateTaken 已在分批加载时缓存，预览时无需再调用 `GetImagePropertiesAsync`
- 其他场景（如 ViewerContainer 直接打开图片）没有缓存，传入 null，自动走 BitmapProperties 获取
- 方案 B 作为兜底：如果 BitmapProperties 也获取不到 DateTaken，再回退到 ImageProperties

### 步骤 4：修改 IExifService 接口

**文件**: `IExifService.cs`

```csharp
Task<ExifData> GetExifDataAsync(StorageFile file, CancellationToken cancellationToken = default, DateTime? cachedDateTaken = null);
```

### 步骤 5：修改 ImageViewerViewModel.LoadFileDetailsAsync

**文件**: `ImageViewerViewModel.cs`

```csharp
public async Task LoadFileDetailsAsync(StorageFile file, DateTime? cachedDateTaken = null)
{
    try
    {
        var basicProps = await StorageFilePropertyReader.GetBasicPropertiesAsync(file);
        FileSize = FormatFileSize(basicProps.Size);
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"LoadFileDetailsAsync - 文件大小读取错误: {ex}");
    }

    try
    {
        var exifData = await _exifService.GetExifDataAsync(file, default, cachedDateTaken);
        // ... 其余不变 ...
    }
    // ...
}
```

### 步骤 6：修改 CollectPage.UpdateSelectedImageUi

**文件**: `CollectPage.xaml.cs`

```csharp
private async void UpdateSelectedImageUi()
{
    var imageInfo = ViewModel.SelectedImage;
    if (imageInfo == null)
    {
        PreviewInfoViewModel.Clear();
        return;
    }

    var version = ++_selectedImageVersion;
    PreviewInfoViewModel.SetBasicInfo(imageInfo);

    // 传入已缓存的 DateTaken，避免再次调用 GetImagePropertiesAsync
    await PreviewInfoViewModel.LoadFileDetailsAsync(imageInfo.ImageFile, imageInfo.DateTaken);

    if (version != _selectedImageVersion)
    {
        return;
    }
}
```

### 步骤 7：同步修改 ImageViewerControl 中的调用

**文件**: `ImageViewerControl.xaml.cs`

ImageViewerControl 中 `LoadExifAfterImageAsync` 不传 cachedDateTaken（因为 ViewerContainer 场景没有预缓存），保持默认 null：

```csharp
// 无需修改，默认参数 null 即可
await ViewModel.LoadFileDetailsAsync(file);
```

### 步骤 8：同步修改 MainViewModel.HydrateImageMetadataAsync

**文件**: `MainViewModel.cs`

MainViewModel 中也有 `HydrateImageMetadataAsync`，同样需要缓存 DateTaken：

```csharp
private async Task HydrateImageMetadataAsync(ImageFileInfo imageInfo, CancellationToken cancellationToken)
{
    try
    {
        var metadata = await LoadImageMetadataAsync(imageInfo.ImageFile, cancellationToken);
        if (metadata.HasValue && !cancellationToken.IsCancellationRequested)
        {
            var (width, height, title) = metadata.Value;
            imageInfo.UpdateMetadata(width, height, title);
        }
    }
    // ... 
}
```

但 MainViewModel 的 `LoadImageMetadataAsync` 只返回 `(Width, Height, Title)`，不返回 DateTaken。需要扩展它，或者改为直接调用 `StorageFilePropertyReader.GetImagePropertiesAsync` 并提取所有需要的属性。

**优化方案**：修改 `LoadImageMetadataAsync` 返回更多数据：

```csharp
private async Task<(int Width, int Height, string Title, DateTime? DateTaken)?> LoadImageMetadataAsync(
    StorageFile file,
    CancellationToken cancellationToken)
{
    // ... 现有代码 ...
    DateTime? dateTaken = null;

    try
    {
        var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(file, cancellationToken);
        // ... 现有宽高/标题逻辑 ...

        if (properties.DateTaken != DateTimeOffset.MinValue && properties.DateTaken != default)
        {
            dateTaken = properties.DateTaken.DateTime;
        }
    }
    // ...

    return (width, height, title, dateTaken);
}
```

然后在 `HydrateImageMetadataAsync` 中：

```csharp
if (metadata.HasValue && !cancellationToken.IsCancellationRequested)
{
    var (width, height, title, dateTaken) = metadata.Value;
    imageInfo.UpdateMetadata(width, height, title);
    imageInfo.SetDateTakenFromProperties(dateTaken);
}
```

## 三、优化后的完整调用链路

```
分批加载阶段:
  ① CollectViewModel.HydrateImageMetadataAsync
     → StorageFilePropertyReader.GetImagePropertiesAsync(file)
     → Width/Height/Title + Rating + DateTaken  ✅ 一次调用获取所有

预览阶段（选中图片）:
  ② CollectPage.UpdateSelectedImageUi
     → PreviewInfoViewModel.LoadFileDetailsAsync(file, cachedDateTaken)
       ├─ StorageFilePropertyReader.GetBasicPropertiesAsync(file) → Size
       └─ ExifService.GetExifDataAsync(file, ct, cachedDateTaken)
           └─ BitmapDecoder + BitmapProperties → 其余EXIF
           └─ DateTaken 直接用缓存  ✅ 不再调用 GetImagePropertiesAsync
```

**同一文件 `GetImagePropertiesAsync` 调用次数**：1 次（分批加载时）
**预览阶段 `GetImagePropertiesAsync` 调用次数**：0 次

## 四、修改文件清单

| 文件 | 修改内容 | 优先级 |
|------|----------|--------|
| ImageFileInfo.cs | 新增 DateTaken 属性 + SetDateTakenFromProperties 方法 | 高 |
| CollectViewModel.cs | HydrateImageMetadataAsync 提取 DateTaken | 高 |
| IExifService.cs | GetExifDataAsync 增加 cachedDateTaken 参数 | 高 |
| ExifService.cs | GetExifDataAsync 支持 cachedDateTaken，优先用缓存 | 高 |
| ImageViewerViewModel.cs | LoadFileDetailsAsync 增加 cachedDateTaken 参数并传递 | 高 |
| CollectPage.xaml.cs | UpdateSelectedImageUi 传入 imageInfo.DateTaken | 高 |
| MainViewModel.cs | LoadImageMetadataAsync 返回 DateTaken + HydrateImageMetadataAsync 缓存 DateTaken | 中 |

## 五、风险评估

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| BitmapProperties 对 RAW 格式无法获取 DateTaken | 已修复 | 分批加载时通过 ImageProperties 获取并缓存 |
| ViewerContainer 场景无缓存 DateTaken | 低 | cachedDateTaken 默认 null，自动走 BitmapProperties 获取 |
| ImageFileInfo 新增属性增加内存 | 极低 | DateTime? 仅 8 字节 |
| IExifService 接口变更影响其他调用方 | 低 | 新参数有默认值 null，不影响现有调用 |
