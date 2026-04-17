# EXIF/属性获取链路优化方案

## 问题分析

### 当前错误原因

`StorageFilePropertyReader` 只保护了通过它调用的 3 个入口，但还有 **8 处直接调用** `file.Properties.GetImagePropertiesAsync()` 绕过了保护：

| 绕过保护的调用                                  | 文件    | 方法                                          | 影响             |
| ---------------------------------------- | ----- | ------------------------------------------- | -------------- |
| **MainViewModel.LoadImageInfo**          | 第833行 | `file.Properties.GetImagePropertiesAsync()` | MainPage 加载图片时 |
| **MainViewModel.LoadImageMetadataAsync** | 第865行 | `file.Properties.GetImagePropertiesAsync()` | MainPage 延迟加载  |
| **MainViewModel.LoadImageInfoSafeAsync** | 第919行 | `file.Properties.GetImagePropertiesAsync()` | MainPage 安全加载  |
| **RatingService.GetRatingAsync**         | 第52行  | `file.Properties.GetImagePropertiesAsync()` | 评级读取           |
| **RatingService.SetRatingAsync**         | 第88行  | `file.Properties.GetImagePropertiesAsync()` | 评级写入           |
| **ExifService.GetExifDataAsync**         | 第118行 | `file.Properties.GetImagePropertiesAsync()` | EXIF 读取        |
| **ExifService.GetRatingAsync**           | 第325行 | `file.Properties.GetImagePropertiesAsync()` | 评级读取           |
| **ExifService.SetRatingAsync**           | 第352行 | `file.Properties.GetImagePropertiesAsync()` | 评级写入           |

这些调用与 `StorageFilePropertyReader` 保护的调用（ThumbnailService、ImageViewerViewModel、CollectViewModel）在同一个文件上并发执行时，就会触发 `GetBasicPropertiesAsync` 不可重入错误。

### CollectPage 中的重复调用分析

当用户选择一张图片时，以下调用链**同时或几乎同时**触发：

```
选择图片 → SelectedImage changed
  ├─ 1. CollectViewModel.StartDeferredImageInfoLoadAsync
  │     ├─ StorageFilePropertyReader.GetImagePropertiesAsync  (获取宽高)
  │     └─ RatingService.GetRatingAsync → file.Properties.GetImagePropertiesAsync  (获取评级) ⚠️绕过保护
  │
  ├─ 2. CollectPage.UpdateSelectedImageUi
  │     ├─ StorageFilePropertyReader.GetBasicPropertiesAsync  (获取文件大小)
  │     └─ ExifService.GetExifDataAsync → file.Properties.GetImagePropertiesAsync  (获取EXIF) ⚠️绕过保护
  │
  └─ 3. CollectPage.StartSelectedThumbnailLoad
        └─ ThumbnailService → StorageFilePropertyReader.GetBasicPropertiesAsync  (缓存键)
```

调用 1 和调用 2 对同一文件并发调用 `GetImagePropertiesAsync`，调用 3 对同一文件并发调用 `GetBasicPropertiesAsync`。由于 RatingService 和 ExifService 绕过了 `StorageFilePropertyReader`，它们的调用与受保护的调用之间没有串行化，导致冲突。

## 解决方案

### 核心思路

**所有** **`file.Properties`** **调用必须通过** **`StorageFilePropertyReader`**，确保同一文件的属性访问串行化。

### 实施步骤

#### 步骤 1：修复 RatingService（2处）

将 `RatingService.GetRatingAsync` 和 `RatingService.SetRatingAsync` 中的直接调用改为通过 `StorageFilePropertyReader`：

```csharp
// 修改前
var properties = await file.Properties.GetImagePropertiesAsync();

// 修改后
var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(file);
```

注意：`SetRatingAsync` 中还需要 `SavePropertiesAsync()`，这不需要串行化保护（它是对 ImageProperties 对象的操作，不是对 StorageFile 的属性调用）。

#### 步骤 2：修复 ExifService（3处）

将 `ExifService.GetExifDataAsync`、`ExifService.GetRatingAsync`、`ExifService.SetRatingAsync` 中的直接调用改为通过 `StorageFilePropertyReader`：

```csharp
// 修改前
var imageProperties = await file.Properties.GetImagePropertiesAsync();

// 修改后
var imageProperties = await StorageFilePropertyReader.GetImagePropertiesAsync(file);
```

#### 步骤 3：修复 MainViewModel（3处）

将 `MainViewModel.LoadImageInfo`、`LoadImageMetadataAsync`、`LoadImageInfoSafeAsync` 中的直接调用改为通过 `StorageFilePropertyReader`：

```csharp
// 修改前
var properties = await file.Properties.GetImagePropertiesAsync();

// 修改后
var properties = await StorageFilePropertyReader.GetImagePropertiesAsync(file);
```

#### 步骤 4：移除 RatingService 和 ExifService 的内部信号量

由于 `StorageFilePropertyReader` 已经提供了每文件串行化保护，`RatingService` 和 `ExifService` 内部的 `SemaphoreSlim` 可以保留用于限制全局并发数，但不再需要用于防止同一文件的并发访问。

#### 步骤 5：优化 CollectPage 的调用时序

当前选择图片时，`HydrateImageMetadataAsync`（获取宽高+评级）和 `LoadFileDetailsAsync`（获取文件大小+EXIF）几乎同时触发，对同一文件发起多次属性请求。

优化方案：

* `HydrateImageMetadataAsync` 已经获取了 `ImageProperties`，可以从中提取 `Rating` 信息，避免 `RatingService` 再次调用 `GetImagePropertiesAsync`

* 在 `HydrateImageMetadataAsync` 中获取 `ImageProperties` 时，同时缓存 `Rating`，后续 `LoadRatingAsync` 直接使用缓存

#### 步骤 6：验证

编译并测试以下场景：

1. CollectPage 加载大量图片（1万+），检查是否还有 `GetBasicPropertiesAsync` 错误
2. 快速切换图片，检查 EXIF 信息是否正确显示
3. 评级功能正常工作
4. 缩略图加载正常

## 修改文件清单

| 文件                  | 修改内容                                   |
| ------------------- | -------------------------------------- |
| RatingService.cs    | 2处改为 StorageFilePropertyReader         |
| ExifService.cs      | 3处改为 StorageFilePropertyReader         |
| MainViewModel.cs    | 3处改为 StorageFilePropertyReader         |
| CollectViewModel.cs | 优化 HydrateImageMetadataAsync，缓存 Rating |

## 风险评估

| 风险                                           | 影响 | 缓解措施                                                             |
| -------------------------------------------- | -- | ---------------------------------------------------------------- |
| StorageFilePropertyReader 信号量等待导致性能下降        | 低  | 每文件信号量，不同文件不互斥                                                   |
| RatingService/ExifService 内部信号量与 FileGate 死锁 | 极低 | 获取顺序：先内部信号量 → 再 FileGate，不会反向                                    |
| SavePropertiesAsync 不需要保护                    | 无  | SavePropertiesAsync 是对 ImageProperties 对象操作，不涉及 StorageFile 属性系统 |

