# RAW 文件内嵌 HD 预览优化计划

## 需求概述

1. **高清预览优化**：对于 RAW 文件，优先使用微软 `GetThumbnailAsync` API 快速取出内嵌 HD 图片（一般为 1920×1080 尺寸）
2. **判断逻辑**：
   - 如果解码尺寸小于等于内嵌 HD 尺寸，直接用 GetThumbnailAsync 获取快速展示
   - 如果解码尺寸超出内嵌 HD 尺寸，就使用正常的 BitmapDecoder 解码
3. **设置开关**：在设置页面增加开关项 "RAW 文件始终解码"，默认关闭，说明：关闭时优先展示内置 HD 图片

---

## 一、调研结论

### 当前流程分析

1. **高清预览加载**：`ImageViewerControl.LoadHighResolutionImageAsync()` → `ThumbnailService.GetThumbnailWithSizeAsync()`
2. **缩略图获取**：`ThumbnailService.DecodeThumbnailAsync()` 使用 `BitmapDecoder` 完整解码
3. **RAW 文件检测**：已有 RAW 扩展名列表（.cr2, .nef, .arw, .dng 等）在多个地方使用
4. **设置服务**：`ISettingsService` 和 `SettingsService` 已有完整结构，可添加新设置
5. **设置页面**：`SettingsPage.xaml` 使用 CommunityToolkit SettingsCard，可轻松添加新开关

### 内嵌 Preview API

- Windows.Storage.StorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, requestedSize, ThumbnailOptions.UseCurrentScale)
- 可以快速获取文件内嵌的预览图，RAW 文件通常包含 1920×1080 的内嵌 JPEG

---

## 二、修改文件清单

| 文件 | 修改内容 |
|------|---------|
| `Contracts/Services/ISettingsService.cs` | 添加 `AlwaysDecodeRaw` 属性和事件 |
| `Services/SettingsService.cs` | 实现 `AlwaysDecodeRaw` 设置和持久化 |
| `Services/ThumbnailService.cs` | 新增 RAW 内嵌预览获取逻辑 |
| `Views/SettingsPage.xaml` | 添加 RAW 解码开关 UI |
| `Views/SettingsPage.xaml.cs` | 添加开关事件处理 |

---

## 三、详细修改步骤

### 步骤 1：更新 ISettingsService

1. 添加 `AlwaysDecodeRaw` 属性（bool，默认 false）
2. 添加 `AlwaysDecodeRawChanged` 事件
3. 添加 `SaveAlwaysDecodeRawAsync()` 和 `LoadAlwaysDecodeRawAsync()` 方法

### 步骤 2：更新 SettingsService

1. 添加私有字段 `_alwaysDecodeRaw = false`
2. 实现 `AlwaysDecodeRaw` 属性，触发 `AlwaysDecodeRawChanged` 事件
3. 实现保存和加载方法
4. 在 `InitializeAsync()` 中加载该设置

### 步骤 3：修改 ThumbnailService

1. **新增辅助方法**：`IsRawFile(string extension)` - 判断是否为 RAW 文件
2. **新增方法**：`GetRawEmbeddedPreviewAsync()` - 使用 `file.GetThumbnailAsync()` 获取内嵌预览
3. **修改** `DecodeThumbnailAsync()`：
   - 先判断是否为 RAW 文件
   - 如果是 RAW 且 `AlwaysDecodeRaw == false`：
     - 尝试获取内嵌预览
     - 判断内嵌预览尺寸是否满足解码尺寸要求
     - 满足则直接返回内嵌预览
     - 不满足或获取失败则回退到完整解码

### 步骤 4：修改 SettingsPage.xaml

在 **Performance** 部分（DecodeScaleFactor 之后）添加新的 SettingsCard：
```xml
<toolkit:SettingsCard 
    Description="关闭时优先展示内置 HD 图片"
    Header="RAW 文件始终解码">
    <toolkit:SettingsCard.HeaderIcon>
        <FontIcon Glyph="&#xE7B8;" />
    </toolkit:SettingsCard.HeaderIcon>
    <ToggleSwitch x:Name="AlwaysDecodeRawToggleSwitch" Toggled="AlwaysDecodeRawToggleSwitch_Toggled" />
</toolkit:SettingsCard>
```

### 步骤 5：修改 SettingsPage.xaml.cs

1. 在 `Initialize()` 中加载并设置 `AlwaysDecodeRawToggleSwitch.IsOn`
2. 添加 `AlwaysDecodeRawToggleSwitch_Toggled` 事件处理，保存设置

---

## 四、RAW 文件扩展名列表现有位置

```csharp
// MainViewModel.cs 和 ExifService.cs 已有定义
new[] {
    ".cr2", ".cr3", ".crw",    // Canon
    ".nef", ".nrw",              // Nikon
    ".arw", ".srf", ".sr2",     // Sony
    ".raf",                      // Fujifilm
    ".orf",                      // Olympus
    ".rw2",                      // Panasonic
    ".pef",                      // Pentax
    ".dng",                      // Adobe
    ".3fr", ".iiq", ".eip",     // Phase One/Hasselblad
    ".srw",                      // Samsung
    ".raw"                       // 通用
}
```

建议：在 `Helpers/` 或 `Models/` 下创建静态类 `RawFileHelper` 统一管理

---

## 五、内嵌预览获取伪代码

```csharp
private async Task<DecodeResult?> TryGetRawEmbeddedPreviewAsync(
    StorageFile file, 
    uint targetLongSide, 
    CancellationToken cancellationToken)
{
    try
    {
        // 获取内嵌预览（最大尺寸 1920）
        using var thumbnail = await file.GetThumbnailAsync(
            ThumbnailMode.SingleItem, 
            1920, 
            ThumbnailOptions.UseCurrentScale);
        
        if (thumbnail == null || thumbnail.Size == 0)
            return null;
        
        // 创建 BitmapImage
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(thumbnail);
        
        // 检查尺寸是否满足要求
        var previewLongSide = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
        if (previewLongSide < targetLongSide)
            return null; // 尺寸不够，需要完整解码
        
        return new DecodeResult(
            (uint)bitmap.PixelWidth, 
            (uint)bitmap.PixelHeight, 
            bitmap);
    }
    catch
    {
        return null; // 获取失败，回退到完整解码
    }
}
```

---

## 六、风险与注意事项

1. **线程安全**：注意使用 GetThumbnailAsync 后，BitmapImage 必须在 UI 线程创建
2. **尺寸判断**：内嵌预览可能没有 1920，需要检查实际尺寸
3. **回退机制**：内嵌预览获取失败或尺寸不足时，必须回退到 BitmapDecoder 完整解码
4. **GetThumbnailAsync 可能没有内嵌预览**：不是所有 RAW 文件都有内嵌预览，需要容错处理

---

## 七、验收标准

- [ ] 设置页面新增 "RAW 文件始终解码" 开关，默认关闭
- [ ] 关闭时，RAW 文件优先使用内嵌预览展示（速度快）
- [ ] 开启时，RAW 文件使用完整解码（质量高）
- [ ] 内嵌预览尺寸不足时，自动回退到完整解码
- [ ] 开关状态持久化，重启后记住设置
