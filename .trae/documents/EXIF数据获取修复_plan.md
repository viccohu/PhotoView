
# EXIF 数据获取修复方案（全面版 - 包含所有 RAW 格式）

## 1. 问题分析

根据调试日志：
- `DateTaken: 未找到` - DNG 文件通过 BitmapProperties 获取失败
- `ISOSpeed: 未找到` - 数据类型可能不匹配

## 2. 支持的格式分析

### 2.1 当前支持的格式
`.jpg`, `.jpeg`, `.tif`, `.tiff`, `.png`, `.heic`, `.heif`, `.dng`

### 2.2 Windows Raw Image Extension 支持的 RAW 格式
| 品牌 | 格式 | 备注 |
|------|------|------|
| Canon | CRW, CR2, CR3 | 从老款 DSLR 到最新 EOS R 系列 |
| Nikon | NEF, NRW | D 系列和 Z 系列 |
| Sony | ARW, SRF, SR2 | Alpha 无反和旧型号 |
| Fujifilm | RAF | X 系列和 GFX 中画幅 |
| Panasonic/Lumix | RW2 | 包括最新型号 |
| Olympus | ORF | 所有型号 |
| Pentax | PEF | 所有型号 |
| Leica | RWL | 所有型号 |
| Samsung | SRW | 所有型号 |
| Epson | ERF | 所有型号 |
| Kodak | DCR | 所有型号 |
| Minolta | MRW | 所有型号 |
| Hasselblad | 3FR, FFF | 中画幅相机 |
| Leaf | MOS | 中画幅后背 |
| Adobe | DNG | 通用 RAW 格式 |
| 其他 | RAW | 通用 RAW 格式 |

### 2.3 各格式 EXIF 支持情况
| 格式类型 | DateTaken | ISOSpeed | 最佳 API |
|----------|-----------|----------|----------|
| JPG/JPEG | ✅ | ✅ | BitmapProperties |
| TIFF | ✅ | ✅ | BitmapProperties |
| PNG | ❌ | ❌ | PNG 不支持 EXIF |
| HEIC/HEIF | ✅ | ✅ | ImageProperties |
| 所有 RAW 格式 | ✅ | ✅ | ImageProperties |

### 2.4 关键发现
根据 Microsoft 文档：
1. **ImageProperties API** 更可靠，支持所有格式
2. **BitmapProperties API** 对 RAW 格式支持不完整
3. **ISOSpeed 数据类型**：可能是 `ushort` 而不是 `uint`
4. **RAW 格式需要安装 Raw Image Extension**

## 3. 修复方案

### 3.1 扩展支持的 RAW 格式
添加所有 Windows Raw Image Extension 支持的格式：
```csharp
private static readonly HashSet<string> WinRTSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
{
    // 标准图像格式
    ".jpg", ".jpeg", ".tif", ".tiff", ".png", ".heic", ".heif",
    // RAW 格式 - Canon
    ".crw", ".cr2", ".cr3",
    // RAW 格式 - Nikon
    ".nef", ".nrw",
    // RAW 格式 - Sony
    ".arw", ".srf", ".sr2",
    // RAW 格式 - Fujifilm
    ".raf",
    // RAW 格式 - Panasonic
    ".rw2",
    // RAW 格式 - Olympus
    ".orf",
    // RAW 格式 - Pentax
    ".pef",
    // RAW 格式 - Leica
    ".rwl",
    // RAW 格式 - Samsung
    ".srw",
    // RAW 格式 - Epson
    ".erf",
    // RAW 格式 - Kodak
    ".dcr",
    // RAW 格式 - Minolta
    ".mrw",
    // RAW 格式 - Hasselblad
    ".3fr", ".fff",
    // RAW 格式 - Leaf
    ".mos",
    // RAW 格式 - Adobe
    ".dng",
    // 通用 RAW 格式
    ".raw"
};
```

### 3.2 使用双重策略获取 DateTaken
1. 首先尝试 `ImageProperties.DateTaken`（更可靠，支持所有格式）
2. 如果失败，再尝试 `BitmapProperties.GetPropertiesAsync`

### 3.3 修复 ISOSpeed 数据类型
1. 尝试 `ushort` 类型（正确类型）
2. 如果失败，尝试 `uint` 类型
3. 如果失败，尝试 `double` 类型
4. 记录实际的数据类型

### 3.4 添加更多调试信息
- 记录文件格式
- 记录实际的数据类型
- 记录所有尝试的结果

## 4. 具体实现步骤

### 4.1 修改 GetExifDataAsync 方法
```csharp
public async Task<ExifData> GetExifDataAsync(StorageFile file, CancellationToken cancellationToken = default)
{
    await _concurrencyLimiter.WaitAsync(cancellationToken);
    try
    {
        var exifData = new ExifData();
        
        System.Diagnostics.Debug.WriteLine($"[ExifService] 开始处理文件: {file.Name}, 格式: {file.FileType}");

        // 1. 首先尝试使用 ImageProperties API 获取 DateTaken
        try
        {
            var imageProperties = await file.Properties.GetImagePropertiesAsync();
            if (imageProperties.DateTaken != DateTimeOffset.MinValue && imageProperties.DateTaken != default)
            {
                exifData.DateTaken = imageProperties.DateTaken;
                System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken (ImageProperties): {imageProperties.DateTaken}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken (ImageProperties): 未找到");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExifService] ImageProperties 获取失败: {ex.Message}");
        }

        // 2. 使用 BitmapDecoder 获取其他属性
        using var stream = await file.OpenReadAsync().AsTask(cancellationToken);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        exifData.Width = decoder.PixelWidth;
        exifData.Height = decoder.PixelHeight;

        try
        {
            exifData.DpiX = decoder.DpiX;
            exifData.DpiY = decoder.DpiY;
            exifData.PixelFormat = decoder.BitmapPixelFormat.ToString();
            exifData.BitDepth = GetBitDepthFromPixelFormat(decoder.BitmapPixelFormat);
        }
        catch { }

        try
        {
            var properties = await decoder.BitmapProperties.GetPropertiesAsync(ExifPropertyNames);

            // 如果 ImageProperties 没有获取到 DateTaken，尝试 BitmapProperties
            if (!exifData.DateTaken.HasValue)
            {
                if (properties.TryGetValue("System.Photo.DateTaken", out var dateProp) && dateProp.Value is DateTime dateTaken)
                {
                    exifData.DateTaken = dateTaken;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken (BitmapProperties): {dateTaken}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ExifService] DateTaken (BitmapProperties): 未找到");
                }
            }

            // ISOSpeed - 尝试多种数据类型
            if (properties.TryGetValue("System.Photo.ISOSpeed", out var isoProp))
            {
                System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed 原始值: {isoProp.Value}, 类型: {isoProp.Value?.GetType().Name}");
                
                if (isoProp.Value is ushort isoSpeedUshort)
                {
                    exifData.ISOSpeed = isoSpeedUshort;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed (ushort): {isoSpeedUshort}");
                }
                else if (isoProp.Value is uint isoSpeedUint)
                {
                    exifData.ISOSpeed = isoSpeedUint;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed (uint): {isoSpeedUint}");
                }
                else if (isoProp.Value is double isoSpeedDouble)
                {
                    exifData.ISOSpeed = (uint)isoSpeedDouble;
                    System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed (double): {isoSpeedDouble}");
                }
                else if (isoProp.Value != null)
                {
                    // 尝试直接转换
                    try
                    {
                        exifData.ISOSpeed = Convert.ToUInt32(isoProp.Value);
                        System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed (converted): {exifData.ISOSpeed}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed 转换失败: {ex.Message}");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ExifService] ISOSpeed: 未找到");
            }

            // 其他属性保持不变...
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

## 5. 修改文件列表
1. `Services/ExifService.cs` - 扩展支持的格式，修复 DateTaken 和 ISOSpeed 获取逻辑

## 6. 风险和注意事项
- 保持向后兼容，不影响已支持的格式
- 添加详细的调试日志
- 处理所有可能的异常情况
- RAW 格式需要用户安装 Raw Image Extension

