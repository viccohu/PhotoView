# RAW 文件缩略图显示方向错误修复计划

## 问题概述
- JPG 文件正常显示，但 RAW 文件存在问题：
  - 横构图的 RAW 缩略图，显示框却是竖构图
  - 图片显示不完整

## 代码库分析结论

### 分析的关键文件
**MainViewModel.cs** - 包含 `LoadImageInfoSafeAsync` 方法

### 发现的问题

#### LoadImageInfoSafeAsync 方法 (MainViewModel.cs:479-554)
```csharp
private async Task<ImageFileInfo?> LoadImageInfoSafeAsync(StorageFile file, CancellationToken cancellationToken)
{
    // ...
    if (!isRaw)
    {
        // JPG 文件：使用 GetImagePropertiesAsync
        // ✅ 检查了 Orientation，正确交换宽高
        var orientation = properties.Orientation;
        if (orientation == PhotoOrientation.Rotate90 || 
            orientation == PhotoOrientation.Rotate270 ||
            orientation == PhotoOrientation.Transpose ||
            orientation == PhotoOrientation.Transverse)
        {
            (width, height) = (height, width);
        }
    }
    
    if (width == 200 && height == 200)
    {
        // RAW 文件：使用 BitmapDecoder
        using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        width = (int)decoder.PixelWidth;
        height = (int)decoder.PixelHeight;
        
        try
        {
            var properties = await decoder.BitmapProperties.GetPropertiesAsync(new[] { "System.Photo.Orientation" });
            if (properties.TryGetValue("System.Photo.Orientation", out var orientationValue))
            {
                var exifOrientation = Convert.ToUInt16(orientationValue.Value);
                if (exifOrientation == 6 || exifOrientation == 8 || exifOrientation == 5 || exifOrientation == 7)
                {
                    (width, height) = (height, width);
                }
            }
        }
        catch
        {
        }
    }
}
```

### 问题根源
**RAW 文件的 EXIF 方向信息读取失败或未正确处理！**

让我对比一下缩略图加载的不同方法：
- **JPG 文件**：使用 `GetImagePropertiesAsync`，正确处理方向
- **RAW 文件**：尝试使用 `BitmapDecoder` 和 `System.Photo.Orientation` 读取，但可能：
  1. RAW 文件的 EXIF 数据在 `BitmapDecoder` 中无法正确读取
  2. 或者 EXIF 数据读取后没有正确处理

### JPG 正常，RAW 不正常的原因
1. **JPG 文件**：
   - 使用 `GetImagePropertiesAsync()`
   - 正确获取到 `properties.Orientation`
   - 正确交换宽高
   
2. **RAW 文件**：
   - 跳过 `GetImagePropertiesAsync()`（因为 `isRaw`）
   - 尝试用 `BitmapDecoder` 读取，但可能读取不到正确的方向
   - 导致宽高没有根据 EXIF 交换，方向错误！

## 解决方案
对于 RAW 文件，我们也应该尝试先使用 `GetImagePropertiesAsync()`，即使它可能对于某些 RAW 格式返回不正确的值，也比直接跳过好！

## 需要修改的文件
1. **ViewModels/MainViewModel.cs** - 修复 `LoadImageInfoSafeAsync` 方法

## 修改步骤

### 步骤 1：修改 LoadImageInfoSafeAsync 方法
对于 RAW 文件，先尝试使用 `GetImagePropertiesAsync()`，如果失败再用 `BitmapDecoder`。

### 步骤 2：构建验证
确保修改后构建成功，没有错误

## 风险分析
### 低风险
- 只修改了 RAW 文件的尺寸读取逻辑
- 保持 JPG 文件的现有逻辑不变
- 保持备用方案（BitmapDecoder）

## 验证标准
1. RAW 横构图文件显示为横构图
2. RAW 竖构图文件显示为竖构图
3. JPG 文件保持正常
4. 图片显示完整，没有裁剪
---
